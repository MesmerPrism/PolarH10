using System.Diagnostics;
using System.Numerics;

namespace PolarH10.Protocol;

/// <summary>
/// ACC-only breathing volume approximation based on the Polar H10 PMD accelerometer stream.
/// Ported from the existing Unity runtime so the desktop app can expose the same tuning and calibration model.
/// </summary>
public sealed class PolarBreathingTracker
{
    private struct TimedVectorSample
    {
        public float Time;
        public Vector3 Value;
    }

    private struct TimedScalarSample
    {
        public float Time;
        public float Value;
    }

    private readonly List<TimedVectorSample> _warmupSamples = new(1024);
    private readonly List<TimedVectorSample> _calibrationSamples = new(4096);
    private readonly List<float> _projectionScratch = new(4096);
    private readonly List<TimedScalarSample> _adaptiveProjectionSamples = new(8192);
    private readonly List<TimedScalarSample> _adaptiveXzProjectionSamples = new(8192);
    private readonly List<float> _fusionScratch = new(512);

    private bool _isCalibrating;
    private bool _isCalibrated;
    private bool _isTransportConnected;
    private float _calibrationStartTime;
    private float _nextAutoCalibrationTime;
    private string _lastCalibrationFailureReason = string.Empty;

    private bool _hasFilteredSample;
    private Vector3 _filteredAccG;
    private bool _hasProjectionEma;
    private float _projectionEma;
    private bool _hasXzProjectionEma;
    private float _xzProjectionEma;

    private Vector3 _axis = Vector3.UnitY;
    private Vector3 _center = Vector3.Zero;
    private float _boundMin = -0.02f;
    private float _boundMax = 0.02f;
    private float _initialBoundSpan;
    private float _nextAdaptiveBoundsUpdateAt;
    private float _lastAdaptiveBoundsUpdateAt = -1f;
    private float _latestProjection;

    private Vector2 _xzAxis = Vector2.UnitX;
    private float _xzBoundMin = -0.02f;
    private float _xzBoundMax = 0.02f;
    private float _xzInitialBoundSpan;
    private bool _hasXzModel;

    private bool _hasUsefulSignal;
    private float _latestUsefulAxisRangeG;

    private float _lastFrameAt = -1f;
    private bool _hasLastSensorFrameTimestamp;
    private long _lastSensorFrameTimestampNs;
    private float _estimatedSampleDtSeconds = 0.005f;
    private float _lastProcessedSampleAt = -1f;
    private float _sampleRateHzEma;
    private float _lastSampleAt = -1f;
    private bool _hasReceivedAnySample;

    private float _currentVolume = 0.5f;
    private PolarBreathingState _currentState = PolarBreathingState.BadTracking;
    private bool _hasStateSample;
    private float _lastStateVolume;
    private float _lastStateAt;

    private float _lastAcc3dVolume = 0.5f;
    private float _lastAccBaseVolume = 0.5f;
    private float _lastAccXzVolume = 0.5f;

    public PolarBreathingTracker(PolarBreathingSettings? settings = null)
    {
        Settings = (settings ?? PolarBreathingSettings.CreateDefault()).Clamp();
        ResetRuntimeState();
    }

    public PolarBreathingSettings Settings { get; private set; }

    public long AccFrameCount { get; private set; }
    public long AccSampleCount { get; private set; }
    public DateTimeOffset? LastSampleReceivedAtUtc { get; private set; }

    public bool BeginCalibration()
    {
        StartCalibration();
        return true;
    }

    public bool CancelCalibration()
    {
        if (!_isCalibrating)
            return false;

        _isCalibrating = false;
        _calibrationSamples.Clear();
        _nextAutoCalibrationTime = NowSeconds() + Math.Max(0.1f, Settings.CalibrationRetryDelaySeconds);
        SetCalibration(false);
        SetVolume(0.5f, force: true);
        _currentState = PolarBreathingState.BadTracking;
        _hasStateSample = false;
        return true;
    }

    public void Reset()
    {
        ResetRuntimeState();
    }

    public void ApplySettings(PolarBreathingSettings settings, bool resetTracker = true)
    {
        Settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Clamp();
        if (resetTracker)
            ResetRuntimeState();
    }

    public void SetTransportConnected(bool isConnected)
    {
        _isTransportConnected = isConnected;
        if (!isConnected)
        {
            _currentState = PolarBreathingState.BadTracking;
            _hasStateSample = false;
        }
    }

    public void SubmitAccFrame(PolarAccFrame frame)
    {
        if (frame.Samples is null || frame.Samples.Length == 0)
            return;

        float now = NowSeconds();
        _hasReceivedAnySample = true;
        _lastSampleAt = now;
        LastSampleReceivedAtUtc = DateTimeOffset.UtcNow;
        AccFrameCount++;
        AccSampleCount += frame.Samples.Length;

        float frameDtSec = 0f;
        if (_hasLastSensorFrameTimestamp && frame.SensorTimestampNs > _lastSensorFrameTimestampNs)
        {
            double sensorDt = (frame.SensorTimestampNs - _lastSensorFrameTimestampNs) * 1e-9;
            if (sensorDt > 0.0001 && sensorDt < 1.0)
                frameDtSec = (float)sensorDt;
        }
        else if (_lastFrameAt > 0f)
        {
            float hostDt = now - _lastFrameAt;
            if (hostDt > 0.0001f && hostDt < 1f)
                frameDtSec = hostDt;
        }

        if (frameDtSec > 0.0001f)
        {
            float frameSampleRate = frame.Samples.Length / frameDtSec;
            _sampleRateHzEma = _sampleRateHzEma <= 0f
                ? frameSampleRate
                : Lerp(_sampleRateHzEma, frameSampleRate, 0.20f);
        }

        float sampleDtSec;
        if (frameDtSec > 0.0001f)
            sampleDtSec = frameDtSec / Math.Max(1, frame.Samples.Length);
        else if (_sampleRateHzEma > 1f)
            sampleDtSec = 1f / _sampleRateHzEma;
        else
            sampleDtSec = _estimatedSampleDtSeconds;

        sampleDtSec = Math.Clamp(sampleDtSec, 0.001f, 0.05f);
        _estimatedSampleDtSeconds = Lerp(_estimatedSampleDtSeconds, sampleDtSec, 0.20f);

        _lastFrameAt = now;
        _lastSensorFrameTimestampNs = frame.SensorTimestampNs;
        _hasLastSensorFrameTimestamp = true;

        for (int i = 0; i < frame.Samples.Length; i++)
        {
            int samplesFromEnd = frame.Samples.Length - 1 - i;
            float sampleNow = now - (samplesFromEnd * sampleDtSec);
            if (_lastProcessedSampleAt > 0f && sampleNow <= _lastProcessedSampleAt)
                sampleNow = _lastProcessedSampleAt + sampleDtSec;

            var sample = frame.Samples[i];
            var rawG = new Vector3(sample.X * 0.001f, sample.Y * 0.001f, sample.Z * 0.001f);
            ProcessSample(rawG, sampleNow);
            _lastProcessedSampleAt = sampleNow;
        }

        if (!_isCalibrated)
            EvaluateUsefulSignalAndMaybeAutoCalibrate(now);

        Advance(now);
    }

    public void Advance()
    {
        Advance(NowSeconds());
    }

    public PolarBreathingTelemetry GetTelemetry()
    {
        float now = NowSeconds();
        float age = _hasReceivedAnySample ? Math.Max(0f, now - _lastSampleAt) : 0f;
        bool hasTracking = _isTransportConnected &&
            _hasReceivedAnySample &&
            _isCalibrated &&
            age <= Math.Max(0.1f, Settings.StaleTimeoutSeconds);

        return new PolarBreathingTelemetry(
            IsTransportConnected: _isTransportConnected,
            HasReceivedAnySample: _hasReceivedAnySample,
            IsCalibrating: _isCalibrating,
            IsCalibrated: _isCalibrated,
            HasTracking: hasTracking,
            HasUsefulSignal: _hasUsefulSignal,
            HasXzModel: _hasXzModel,
            CalibrationProgress01: GetCalibrationProgress(now),
            CurrentVolume01: _currentVolume,
            CurrentState: _currentState,
            EstimatedSampleRateHz: _sampleRateHzEma,
            UsefulAxisRangeG: _latestUsefulAxisRangeG,
            LastProjectionG: _latestProjection,
            Volume3d01: _lastAcc3dVolume,
            VolumeBase01: _lastAccBaseVolume,
            VolumeXz01: _lastAccXzVolume,
            Axis: _axis,
            Center: _center,
            BoundMin: _boundMin,
            BoundMax: _boundMax,
            XzAxis: _xzAxis,
            XzBoundMin: _xzBoundMin,
            XzBoundMax: _xzBoundMax,
            AccFrameCount: AccFrameCount,
            AccSampleCount: AccSampleCount,
            LastSampleAgeSeconds: age,
            LastCalibrationFailureReason: _lastCalibrationFailureReason,
            Settings: Settings,
            LastSampleReceivedAtUtc: LastSampleReceivedAtUtc);
    }

    private void Advance(float now)
    {
        if (_hasReceivedAnySample)
        {
            float age = now - _lastSampleAt;
            if (age > Math.Max(0.1f, Settings.StaleTimeoutSeconds))
            {
                SetVolume(0.5f);
                _currentState = PolarBreathingState.BadTracking;
                _hasStateSample = false;
            }
        }

        if (_isCalibrating && now - _calibrationStartTime >= Math.Max(0.1f, Settings.CalibrationDurationSeconds))
            CompleteCalibrationAttempt(now);

        if (_isCalibrated)
            UpdateAdaptiveBounds(now);

        if (!_isCalibrated && !_isCalibrating)
            _currentState = PolarBreathingState.BadTracking;
    }

    private float GetCalibrationProgress(float now)
    {
        if (!_isCalibrating)
            return _isCalibrated ? 1f : 0f;

        float duration = Math.Max(0.1f, Settings.CalibrationDurationSeconds);
        return Clamp01((now - _calibrationStartTime) / duration);
    }

    private void ProcessSample(Vector3 rawG, float now)
    {
        if (!_hasFilteredSample)
        {
            _filteredAccG = rawG;
            _hasFilteredSample = true;
        }
        else
        {
            _filteredAccG = Vector3.Lerp(_filteredAccG, rawG, Clamp01(Settings.SampleEmaAlpha));
        }

        if (!_isCalibrated)
            AddWarmupSample(now, _filteredAccG);

        if (_isCalibrating)
        {
            _calibrationSamples.Add(new TimedVectorSample
            {
                Time = now,
                Value = _filteredAccG,
            });
        }

        if (_isCalibrated)
            UpdateBreathingFromFilteredSample(_filteredAccG, now);
    }

    private void AddWarmupSample(float now, Vector3 value)
    {
        _warmupSamples.Add(new TimedVectorSample
        {
            Time = now,
            Value = value,
        });

        TrimTimedSamples(_warmupSamples, now - Math.Max(0.25f, Settings.UsefulSignalWindowSeconds), hardCap: 6000);
    }

    private void EvaluateUsefulSignalAndMaybeAutoCalibrate(float now)
    {
        bool hasUseful = TryGetUsefulSignalStats(out float axisRange, out _);
        _latestUsefulAxisRangeG = axisRange;
        _hasUsefulSignal = hasUseful;

        if (!Settings.AutoCalibrateOnUsefulSignal || _isCalibrated || _isCalibrating)
            return;

        if (now < _nextAutoCalibrationTime || !hasUseful)
            return;

        StartCalibration();
    }

    private bool TryGetUsefulSignalStats(out float axisRange, out string reason)
    {
        axisRange = 0f;
        reason = string.Empty;

        int available = _warmupSamples.Count;
        if (available >= 2)
        {
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;

            for (int i = 0; i < available; i++)
            {
                Vector3 value = _warmupSamples[i].Value;
                minX = Math.Min(minX, value.X);
                minY = Math.Min(minY, value.Y);
                minZ = Math.Min(minZ, value.Z);
                maxX = Math.Max(maxX, value.X);
                maxY = Math.Max(maxY, value.Y);
                maxZ = Math.Max(maxZ, value.Z);
            }

            axisRange = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        }

        int neededSamples = Math.Max(16, Settings.MinUsefulSamples);
        if (available < neededSamples)
        {
            reason = $"samples {available}/{neededSamples}";
            return false;
        }

        if (_sampleRateHzEma > 0f && _sampleRateHzEma < Math.Max(1f, Settings.MinUsefulSampleRateHz))
        {
            reason = $"sampleRate {_sampleRateHzEma:F1}Hz < {Settings.MinUsefulSampleRateHz:F1}Hz";
            return false;
        }

        float requiredRange = Math.Max(0.0005f, Settings.MinUsefulAxisRangeG);
        if (axisRange < requiredRange)
        {
            reason = $"axisRange {axisRange:F4}g < {requiredRange:F4}g";
            return false;
        }

        return true;
    }

    private void StartCalibration()
    {
        _isCalibrating = true;
        _calibrationStartTime = NowSeconds();
        _calibrationSamples.Clear();
        _hasProjectionEma = false;
        _hasXzProjectionEma = false;
        _adaptiveProjectionSamples.Clear();
        _adaptiveXzProjectionSamples.Clear();
        _nextAdaptiveBoundsUpdateAt = 0f;
        _lastAdaptiveBoundsUpdateAt = -1f;
        _initialBoundSpan = 0f;
        _xzInitialBoundSpan = 0f;
        _hasXzModel = false;
        _lastCalibrationFailureReason = string.Empty;
        SetCalibration(false);
        SetVolume(0.5f, force: true);
        _currentState = PolarBreathingState.BadTracking;
        _hasStateSample = false;
    }

    private void CompleteCalibrationAttempt(float now)
    {
        if (!_isCalibrating)
            return;

        if (!TryBuildCalibrationModel(
                out Vector3 center,
                out Vector3 axis,
                out float boundMin,
                out float boundMax,
                out Vector2 xzAxis,
                out float xzBoundMin,
                out float xzBoundMax,
                out float rawTravelG,
                out float rawTravelXzG,
                out string error))
        {
            FailCalibration(error);
            return;
        }

        _center = center;
        _axis = axis;
        _boundMin = boundMin;
        _boundMax = boundMax;
        _initialBoundSpan = Math.Max(0.001f, _boundMax - _boundMin);
        _xzAxis = xzAxis;
        _xzBoundMin = xzBoundMin;
        _xzBoundMax = xzBoundMax;
        _xzInitialBoundSpan = Math.Max(0.001f, _xzBoundMax - _xzBoundMin);
        _hasXzModel = rawTravelXzG > 0.0005f;
        _isCalibrating = false;
        _hasProjectionEma = false;
        _hasXzProjectionEma = false;
        _adaptiveProjectionSamples.Clear();
        _adaptiveXzProjectionSamples.Clear();
        _nextAdaptiveBoundsUpdateAt = now + Math.Max(0.1f, Settings.AdaptiveBoundsUpdateIntervalSeconds);
        _lastAdaptiveBoundsUpdateAt = now;
        _lastCalibrationFailureReason = string.Empty;
        SetCalibration(true);

        if (_hasFilteredSample)
            UpdateBreathingFromFilteredSample(_filteredAccG, now);
    }

    private bool TryBuildCalibrationModel(
        out Vector3 center,
        out Vector3 axis,
        out float boundMin,
        out float boundMax,
        out Vector2 xzAxis,
        out float xzBoundMin,
        out float xzBoundMax,
        out float rawTravelG,
        out float rawTravelXzG,
        out string error)
    {
        center = Vector3.Zero;
        axis = Vector3.UnitY;
        boundMin = 0f;
        boundMax = 0f;
        xzAxis = Vector2.UnitX;
        xzBoundMin = 0f;
        xzBoundMax = 0f;
        rawTravelG = 0f;
        rawTravelXzG = 0f;
        error = string.Empty;

        int sampleCount = _calibrationSamples.Count;
        int requiredSamples = Math.Max(16, Settings.MinCalibrationSamples);
        if (sampleCount < requiredSamples)
        {
            error = $"not enough samples ({sampleCount}/{requiredSamples})";
            return false;
        }

        for (int i = 0; i < sampleCount; i++)
            center += _calibrationSamples[i].Value;
        center /= sampleCount;

        float c00 = 0f, c01 = 0f, c02 = 0f, c11 = 0f, c12 = 0f, c22 = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            Vector3 delta = _calibrationSamples[i].Value - center;
            c00 += delta.X * delta.X;
            c01 += delta.X * delta.Y;
            c02 += delta.X * delta.Z;
            c11 += delta.Y * delta.Y;
            c12 += delta.Y * delta.Z;
            c22 += delta.Z * delta.Z;
        }

        float inv = 1f / sampleCount;
        c00 *= inv;
        c01 *= inv;
        c02 *= inv;
        c11 *= inv;
        c12 *= inv;
        c22 *= inv;

        axis = _axis.LengthSquared() > 1e-6f ? _axis : Vector3.UnitY;
        for (int iter = 0; iter < 6; iter++)
        {
            axis = new Vector3(
                c00 * axis.X + c01 * axis.Y + c02 * axis.Z,
                c01 * axis.X + c11 * axis.Y + c12 * axis.Z,
                c02 * axis.X + c12 * axis.Y + c22 * axis.Z);

            float magnitude = axis.Length();
            if (magnitude < 1e-6f)
            {
                error = "principal axis estimation failed";
                return false;
            }

            axis /= magnitude;
        }

        if (_isCalibrated && _axis.LengthSquared() > 1e-6f && Vector3.Dot(axis, _axis) < 0f)
            axis = -axis;

        axis = ApplyDirectionReference(axis);

        _projectionScratch.Clear();
        for (int i = 0; i < sampleCount; i++)
            _projectionScratch.Add(Vector3.Dot(_calibrationSamples[i].Value - center, axis));

        if (!TryComputeQuantileBoundsInPlace(_projectionScratch, Settings.BoundsLowerQuantile, Settings.BoundsUpperQuantile, out float lo, out float hi))
        {
            error = "collapsed quantile bounds";
            return false;
        }

        rawTravelG = Math.Max(0f, hi - lo);
        float requiredTravel = Math.Max(0.001f, Settings.MinCalibrationTravelG);
        if (rawTravelG < requiredTravel)
        {
            error = $"insufficient travel ({rawTravelG:F4}g < {requiredTravel:F4}g)";
            return false;
        }

        boundMin = lo;
        boundMax = hi;
        ApplyEdgeEase(ref boundMin, ref boundMax, Settings.BoundsEdgeEase);

        if (boundMax - boundMin < 1e-6f)
        {
            error = "collapsed bounds after edge easing";
            return false;
        }

        TryBuildXzModel(center, out xzAxis, out xzBoundMin, out xzBoundMax, out rawTravelXzG);
        return true;
    }

    private Vector3 ApplyDirectionReference(Vector3 axis)
    {
        if (!Settings.UseDirectionReference)
            return axis;

        Vector3 desired = Settings.DirectionReference;
        if (desired.LengthSquared() < 1e-6f)
            return axis;

        desired = Vector3.Normalize(desired);
        if (!Settings.AssumeInhaleMovesAlongDirectionReference)
            desired = -desired;

        float alignment = Vector3.Dot(axis, desired);
        if (Math.Abs(alignment) < Math.Clamp(Settings.DirectionReferenceMinAbsDot, 0f, 1f))
            return axis;

        return alignment < 0f ? -axis : axis;
    }

    private void TryBuildXzModel(
        Vector3 center,
        out Vector2 xzAxis,
        out float xzBoundMin,
        out float xzBoundMax,
        out float rawTravelXzG)
    {
        xzAxis = _xzAxis.LengthSquared() > 1e-6f ? Vector2.Normalize(_xzAxis) : Vector2.UnitX;
        xzBoundMin = -0.02f;
        xzBoundMax = 0.02f;
        rawTravelXzG = 0f;

        int sampleCount = _calibrationSamples.Count;
        if (sampleCount < 8)
            return;

        float c00 = 0f, c01 = 0f, c11 = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            Vector3 delta = _calibrationSamples[i].Value - center;
            c00 += delta.X * delta.X;
            c01 += delta.X * delta.Z;
            c11 += delta.Z * delta.Z;
        }

        float inv = 1f / sampleCount;
        c00 *= inv;
        c01 *= inv;
        c11 *= inv;

        for (int iter = 0; iter < 6; iter++)
        {
            Vector2 next = new(
                c00 * xzAxis.X + c01 * xzAxis.Y,
                c01 * xzAxis.X + c11 * xzAxis.Y);

            float magnitude = next.Length();
            if (magnitude < 1e-6f)
                break;
            xzAxis = next / magnitude;
        }

        if (_hasXzModel && Vector2.Dot(xzAxis, _xzAxis) < 0f)
            xzAxis = -xzAxis;

        if (Settings.UseDirectionReference)
        {
            Vector3 reference3 = Settings.DirectionReference;
            Vector2 reference = new(reference3.X, reference3.Z);
            if (reference.LengthSquared() > 1e-6f)
            {
                reference = Vector2.Normalize(reference);
                if (!Settings.AssumeInhaleMovesAlongDirectionReference)
                    reference = -reference;

                float dot = Vector2.Dot(xzAxis, reference);
                if (Math.Abs(dot) >= Math.Clamp(Settings.DirectionReferenceMinAbsDot, 0f, 1f) && dot < 0f)
                    xzAxis = -xzAxis;
            }
        }

        _fusionScratch.Clear();
        for (int i = 0; i < sampleCount; i++)
        {
            Vector3 delta = _calibrationSamples[i].Value - center;
            float projection = Vector2.Dot(new Vector2(delta.X, delta.Z), xzAxis);
            _fusionScratch.Add(projection);
        }

        if (!TryComputeQuantileBoundsInPlace(_fusionScratch, Settings.BoundsLowerQuantile, Settings.BoundsUpperQuantile, out float lo, out float hi))
            return;

        rawTravelXzG = Math.Max(0f, hi - lo);
        xzBoundMin = lo;
        xzBoundMax = hi;
        ApplyEdgeEase(ref xzBoundMin, ref xzBoundMax, Settings.BoundsEdgeEase);

        float minSpan = Math.Max(0.005f, Settings.MinCalibrationTravelG * 0.5f);
        EnforceSpanBounds(ref xzBoundMin, ref xzBoundMax, minSpan, float.MaxValue);
    }

    private void FailCalibration(string reason)
    {
        _isCalibrating = false;
        _calibrationSamples.Clear();
        _lastCalibrationFailureReason = reason ?? "unknown error";
        _nextAutoCalibrationTime = NowSeconds() + Math.Max(0.1f, Settings.CalibrationRetryDelaySeconds);
        SetCalibration(false);
        SetVolume(0.5f, force: true);
        _currentState = PolarBreathingState.BadTracking;
        _hasStateSample = false;
    }

    private void UpdateBreathingFromFilteredSample(Vector3 filteredAccG, float now)
    {
        Vector3 centered = filteredAccG - _center;
        float projection = Vector3.Dot(centered, _axis);
        _latestProjection = projection;

        if (!_hasProjectionEma)
        {
            _projectionEma = projection;
            _hasProjectionEma = true;
        }
        else
        {
            _projectionEma = Lerp(_projectionEma, projection, Clamp01(Settings.ProjectionEmaAlpha));
        }

        float volume3d = InverseLerp(_boundMin, _boundMax, _projectionEma);
        float xzVolume = volume3d;
        bool hasXzProjection = false;
        float xzProjectionForBounds = 0f;

        if (_hasXzModel)
        {
            float xzProjection = Vector2.Dot(new Vector2(centered.X, centered.Z), _xzAxis);
            if (!_hasXzProjectionEma)
            {
                _xzProjectionEma = xzProjection;
                _hasXzProjectionEma = true;
            }
            else
            {
                _xzProjectionEma = Lerp(_xzProjectionEma, xzProjection, Clamp01(Settings.ProjectionEmaAlpha));
            }

            hasXzProjection = true;
            xzProjectionForBounds = _xzProjectionEma;
            xzVolume = InverseLerp(_xzBoundMin, _xzBoundMax, _xzProjectionEma);
        }

        RecordAdaptiveProjectionSample(now, _projectionEma, xzProjectionForBounds, hasXzProjection);

        bool useXzBase = Settings.BaseMode == PolarBreathingBaseMode.Xz && _hasXzModel;
        float baseVolume = useXzBase ? xzVolume : volume3d;
        float outputVolume = Settings.InvertVolume ? 1f - baseVolume : baseVolume;

        _lastAcc3dVolume = volume3d;
        _lastAccBaseVolume = baseVolume;
        _lastAccXzVolume = xzVolume;
        SetVolume(outputVolume);
        UpdateStateFromVolume(now);
    }

    private void UpdateStateFromVolume(float now)
    {
        if (!_isCalibrated)
        {
            _currentState = PolarBreathingState.BadTracking;
            _hasStateSample = false;
            return;
        }

        if (!_hasStateSample)
        {
            _hasStateSample = true;
            _lastStateVolume = _currentVolume;
            _lastStateAt = now;
            _currentState = PolarBreathingState.Pausing;
            return;
        }

        if ((now - _lastStateAt) > Math.Max(0.05f, Settings.StaleTimeoutSeconds))
        {
            _lastStateVolume = _currentVolume;
            _lastStateAt = now;
            _currentState = PolarBreathingState.BadTracking;
            return;
        }

        float delta = _currentVolume - _lastStateVolume;
        _lastStateVolume = _currentVolume;
        _lastStateAt = now;

        if (delta > Math.Max(0.0001f, Settings.StateDeltaThreshold))
        {
            _currentState = PolarBreathingState.Inhaling;
            return;
        }

        if (delta < -Math.Max(0.0001f, Settings.StateDeltaThreshold))
        {
            _currentState = PolarBreathingState.Exhaling;
            return;
        }

        _currentState = PolarBreathingState.Pausing;
    }

    private void RecordAdaptiveProjectionSample(float now, float projection, float xzProjection, bool hasXzProjection)
    {
        if (!Settings.UseAdaptiveBounds || !_isCalibrated)
            return;

        _adaptiveProjectionSamples.Add(new TimedScalarSample
        {
            Time = now,
            Value = projection,
        });

        int hardCap = Math.Max(2048, (int)MathF.Round(Math.Max(4f, Settings.AdaptiveBoundsWindowSeconds) * Math.Max(20f, _sampleRateHzEma) * 2.5f));
        if (_adaptiveProjectionSamples.Count > hardCap)
            _adaptiveProjectionSamples.RemoveRange(0, _adaptiveProjectionSamples.Count - hardCap);

        if (hasXzProjection && _hasXzModel)
        {
            _adaptiveXzProjectionSamples.Add(new TimedScalarSample
            {
                Time = now,
                Value = xzProjection,
            });

            if (_adaptiveXzProjectionSamples.Count > hardCap)
                _adaptiveXzProjectionSamples.RemoveRange(0, _adaptiveXzProjectionSamples.Count - hardCap);
        }
    }

    private void UpdateAdaptiveBounds(float now)
    {
        if (!Settings.UseAdaptiveBounds || _isCalibrating)
            return;

        float windowSeconds = Math.Max(4f, Settings.AdaptiveBoundsWindowSeconds);
        TrimTimedScalarSamples(_adaptiveProjectionSamples, now - windowSeconds, hardCap: 0);
        if (_hasXzModel)
            TrimTimedScalarSamples(_adaptiveXzProjectionSamples, now - windowSeconds, hardCap: 0);

        int requiredSamples = ComputeAdaptiveRequiredSamples(_sampleRateHzEma);
        if (_adaptiveProjectionSamples.Count < requiredSamples)
            return;

        if (now < _nextAdaptiveBoundsUpdateAt)
            return;

        _nextAdaptiveBoundsUpdateAt = now + Math.Max(0.1f, Settings.AdaptiveBoundsUpdateIntervalSeconds);

        float dt = _lastAdaptiveBoundsUpdateAt >= 0f
            ? Math.Max(0.0001f, now - _lastAdaptiveBoundsUpdateAt)
            : Math.Max(0.1f, Settings.AdaptiveBoundsUpdateIntervalSeconds);
        _lastAdaptiveBoundsUpdateAt = now;

        UpdateAdaptiveBoundsChannel(_adaptiveProjectionSamples, _initialBoundSpan, dt, ref _boundMin, ref _boundMax);

        if (_hasXzModel && _adaptiveXzProjectionSamples.Count >= requiredSamples)
            UpdateAdaptiveBoundsChannel(_adaptiveXzProjectionSamples, _xzInitialBoundSpan, dt, ref _xzBoundMin, ref _xzBoundMax);
    }

    private void UpdateAdaptiveBoundsChannel(List<TimedScalarSample> samples, float initialSpan, float dt, ref float boundMin, ref float boundMax)
    {
        _projectionScratch.Clear();
        for (int i = 0; i < samples.Count; i++)
            _projectionScratch.Add(samples[i].Value);

        if (!TryComputeQuantileBoundsInPlace(_projectionScratch, Settings.BoundsLowerQuantile, Settings.BoundsUpperQuantile, out float targetMin, out float targetMax))
            return;

        ApplyEdgeEase(ref targetMin, ref targetMax, Settings.BoundsEdgeEase);

        float minSpan = initialSpan > 0f
            ? Math.Max(0.001f, initialSpan * Math.Clamp(Settings.AdaptiveBoundsMinInitialRangeFactor, 0.01f, 1f))
            : 0.001f;
        float maxSpan = initialSpan > 0f
            ? Math.Max(minSpan, initialSpan * Math.Max(1f, Settings.AdaptiveBoundsMaxInitialRangeFactor))
            : float.MaxValue;

        EnforceSpanBounds(ref targetMin, ref targetMax, minSpan, maxSpan);

        float expandSpeed = Math.Max(0.01f, Settings.AdaptiveBoundsLerpSpeed);
        float contractSpeed = Math.Max(0.01f, expandSpeed * Math.Clamp(Settings.AdaptiveBoundsContractSpeedMultiplier, 0.1f, 1f));
        float minSpeed = targetMin < boundMin ? expandSpeed : contractSpeed;
        float maxSpeed = targetMax > boundMax ? expandSpeed : contractSpeed;
        float minLerpT = ComputeExponentialLerp(minSpeed, dt);
        float maxLerpT = ComputeExponentialLerp(maxSpeed, dt);

        float newMin = Lerp(boundMin, targetMin, minLerpT);
        float newMax = Lerp(boundMax, targetMax, maxLerpT);
        EnforceSpanBounds(ref newMin, ref newMax, minSpan, maxSpan);

        boundMin = newMin;
        boundMax = newMax;
    }

    private void SetVolume(float volume, bool force = false)
    {
        float clamped = Clamp01(volume);
        if (!force && Math.Abs(_currentVolume - clamped) < Math.Max(0.0001f, Settings.VolumeEventMinDelta))
            return;

        _currentVolume = clamped;
    }

    private void SetCalibration(bool calibrated)
    {
        _isCalibrated = calibrated;
        if (!calibrated)
        {
            _currentState = PolarBreathingState.BadTracking;
            _hasStateSample = false;
        }
    }

    private void ResetRuntimeState()
    {
        _isCalibrating = false;
        _isCalibrated = false;
        _calibrationStartTime = 0f;
        _nextAutoCalibrationTime = 0f;
        _lastCalibrationFailureReason = string.Empty;

        _hasFilteredSample = false;
        _filteredAccG = Vector3.Zero;
        _hasProjectionEma = false;
        _projectionEma = 0f;
        _latestProjection = 0f;
        _hasXzProjectionEma = false;
        _xzProjectionEma = 0f;

        _axis = Vector3.UnitY;
        _center = Vector3.Zero;
        _boundMin = -0.02f;
        _boundMax = 0.02f;
        _initialBoundSpan = 0f;
        _xzAxis = Vector2.UnitX;
        _xzBoundMin = -0.02f;
        _xzBoundMax = 0.02f;
        _xzInitialBoundSpan = 0f;
        _hasXzModel = false;
        _nextAdaptiveBoundsUpdateAt = 0f;
        _lastAdaptiveBoundsUpdateAt = -1f;

        _warmupSamples.Clear();
        _calibrationSamples.Clear();
        _projectionScratch.Clear();
        _adaptiveProjectionSamples.Clear();
        _adaptiveXzProjectionSamples.Clear();
        _fusionScratch.Clear();

        _hasUsefulSignal = false;
        _latestUsefulAxisRangeG = 0f;

        _lastFrameAt = -1f;
        _hasLastSensorFrameTimestamp = false;
        _lastSensorFrameTimestampNs = 0L;
        _estimatedSampleDtSeconds = 0.005f;
        _lastProcessedSampleAt = -1f;
        _sampleRateHzEma = 0f;
        _lastSampleAt = -1f;
        _hasReceivedAnySample = false;
        _currentVolume = 0.5f;
        _currentState = PolarBreathingState.BadTracking;
        _hasStateSample = false;
        _lastStateVolume = 0f;
        _lastStateAt = 0f;
        _lastAcc3dVolume = 0.5f;
        _lastAccBaseVolume = 0.5f;
        _lastAccXzVolume = 0.5f;
        AccFrameCount = 0;
        AccSampleCount = 0;
        LastSampleReceivedAtUtc = null;
    }

    private int ComputeAdaptiveRequiredSamples(float sampleRateHz)
    {
        float windowSeconds = Math.Max(4f, Settings.AdaptiveBoundsWindowSeconds);
        float sampleRate = Math.Clamp(sampleRateHz, Math.Max(5f, Settings.MinUsefulSampleRateHz), 320f);
        float coverage = Math.Clamp(Settings.AdaptiveBoundsMinWindowCoverage, 0.25f, 1f);
        int coverageSamples = (int)MathF.Round(windowSeconds * sampleRate * coverage);
        return Math.Max(Math.Max(16, Settings.MinAdaptiveBoundsSamples), coverageSamples);
    }

    private static void TrimTimedSamples(List<TimedVectorSample> list, float cutoffTime, int hardCap)
    {
        int remove = 0;
        while (remove < list.Count && list[remove].Time < cutoffTime)
            remove++;

        if (remove > 0)
            list.RemoveRange(0, remove);

        if (hardCap > 0 && list.Count > hardCap)
            list.RemoveRange(0, list.Count - hardCap);
    }

    private static void TrimTimedScalarSamples(List<TimedScalarSample> list, float cutoffTime, int hardCap)
    {
        int remove = 0;
        while (remove < list.Count && list[remove].Time < cutoffTime)
            remove++;

        if (remove > 0)
            list.RemoveRange(0, remove);

        if (hardCap > 0 && list.Count > hardCap)
            list.RemoveRange(0, list.Count - hardCap);
    }

    private static bool TryComputeQuantileBoundsInPlace(List<float> values, float lowerQuantile, float upperQuantile, out float lower, out float upper)
    {
        lower = 0f;
        upper = 0f;
        if (values.Count == 0)
            return false;

        values.Sort();
        lower = EvaluateSortedQuantile(values, lowerQuantile);
        upper = EvaluateSortedQuantile(values, upperQuantile);
        return upper > lower;
    }

    private static float EvaluateSortedQuantile(List<float> sortedValues, float quantile)
    {
        if (sortedValues.Count == 0)
            return 0f;

        quantile = Clamp01(quantile);
        int maxIndex = sortedValues.Count - 1;
        float position = maxIndex * quantile;
        int lo = (int)MathF.Floor(position);
        int hi = (int)MathF.Ceiling(position);
        if (lo == hi)
            return sortedValues[lo];

        float t = position - lo;
        return Lerp(sortedValues[lo], sortedValues[hi], t);
    }

    private static void ApplyEdgeEase(ref float min, ref float max, float edgeEase01)
    {
        float span = Math.Max(max - min, 0f);
        if (span < 1e-6f)
            return;

        float shrink = Math.Clamp(span * Clamp01(edgeEase01), 0f, span * 0.49f);
        min += shrink;
        max -= shrink;
    }

    private static void EnforceSpanBounds(ref float min, ref float max, float minSpan, float maxSpan)
    {
        minSpan = Math.Max(0.0001f, minSpan);
        if (!float.IsInfinity(maxSpan))
            maxSpan = Math.Max(minSpan, maxSpan);

        float center = (min + max) * 0.5f;
        float span = Math.Max(max - min, minSpan);
        if (!float.IsInfinity(maxSpan))
            span = Math.Min(span, maxSpan);

        float half = span * 0.5f;
        min = center - half;
        max = center + half;
    }

    private static float ComputeExponentialLerp(float speed, float dt)
    {
        if (speed <= 0f || dt <= 0f)
            return 0f;

        return 1f - MathF.Exp(-speed * dt);
    }

    private static float Lerp(float from, float to, float t) => from + ((to - from) * Clamp01(t));

    private static float InverseLerp(float min, float max, float value)
    {
        if (Math.Abs(max - min) < 1e-6f)
            return 0.5f;

        return Clamp01((value - min) / (max - min));
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static float NowSeconds() => (float)(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
}
