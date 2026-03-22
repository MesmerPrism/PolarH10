using System.Diagnostics;

namespace PolarH10.Protocol;

/// <summary>
/// Extracts breath-cycle interval and amplitude series from the calibrated
/// breathing waveform and computes a breathing-dynamics feature bundle.
/// </summary>
public sealed class PolarBreathingDynamicsTracker
{
    private enum TimebaseKind
    {
        Uninitialized = 0,
        Stopwatch = 1,
        UtcTimestamp = 2,
    }

    private enum ExtremumKind
    {
        Peak = 0,
        Trough = 1,
    }

    private readonly record struct Extremum(ExtremumKind Kind, double TimeSeconds, float Value01);

    private bool _isTransportConnected;
    private bool _hasReceivedAnyWaveformSample;
    private bool _isBreathingCalibrated;
    private bool _hasBreathingTracking;
    private bool _wasBreathingCalibrated;

    private double _lastWaveformSampleAt;
    private double _lastBreathAcceptedAt;
    private double _lastAdvanceAt;
    private bool _hasLastVolumeSample;
    private double _lastVolumeTime;
    private float _lastVolumeBase01;
    private sbyte _trendDirection;
    private bool _hasTrendExtreme;
    private double _trendExtremeTime;
    private float _trendExtremeValue;

    private Extremum? _lastAcceptedExtremum;
    private Extremum? _lastAcceptedPeak;
    private Extremum? _lastAcceptedTrough;
    private readonly List<float> _intervalBreaths = new(256);
    private readonly List<float> _amplitudeBreaths = new(256);
    private readonly List<PolarBreathingWaveformPoint> _waveformSamples = new(4096);
    private readonly List<PolarBreathingExtremumPoint> _acceptedExtremaHistory = new(256);
    private readonly List<PolarBreathingDerivedPoint> _intervalSeriesHistory = new(256);
    private readonly List<PolarBreathingDerivedPoint> _amplitudeSeriesHistory = new(256);
    private PolarBreathingFeatureSet _intervalFeatures = PolarBreathingFeatureSet.Empty;
    private PolarBreathingFeatureSet _amplitudeFeatures = PolarBreathingFeatureSet.Empty;
    private TimebaseKind _timebaseKind;
    private DateTimeOffset _utcTimebaseOrigin;

    public PolarBreathingDynamicsTracker(PolarBreathingDynamicsSettings? settings = null)
    {
        Settings = (settings ?? PolarBreathingDynamicsSettings.CreateDefault()).Clamp();
        ResetRuntimeState();
    }

    public PolarBreathingDynamicsSettings Settings { get; private set; }

    public long WaveformSampleCount { get; private set; }
    public long AcceptedExtremumCount { get; private set; }
    public DateTimeOffset? LastWaveformReceivedAtUtc { get; private set; }
    public DateTimeOffset? LastBreathDetectedAtUtc { get; private set; }

    public void Reset()
    {
        ResetRuntimeState();
    }

    public void ApplySettings(PolarBreathingDynamicsSettings settings, bool resetTracker = true)
    {
        Settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Clamp();
        if (resetTracker)
        {
            ResetRuntimeState();
        }
        else
        {
            RecomputeCachedFeatures();
        }
    }

    public void SetTransportConnected(bool isConnected)
    {
        _isTransportConnected = isConnected;
    }

    public void SubmitBreathingTelemetry(PolarBreathingTelemetry telemetry)
    {
        double now = ResolveSampleTimeSeconds(telemetry.LastSampleReceivedAtUtc);
        _isTransportConnected = telemetry.IsTransportConnected;
        _isBreathingCalibrated = telemetry.IsCalibrated;
        _hasBreathingTracking = telemetry.HasTracking;

        if (_wasBreathingCalibrated && !telemetry.IsCalibrated)
            ResetDerivedSeries();
        _wasBreathingCalibrated = telemetry.IsCalibrated;

        if (!telemetry.HasReceivedAnySample)
        {
            Advance(now);
            return;
        }

        _hasReceivedAnyWaveformSample = true;
        _lastWaveformSampleAt = now;
        LastWaveformReceivedAtUtc = telemetry.LastSampleReceivedAtUtc ?? DateTimeOffset.UtcNow;
        WaveformSampleCount++;

        if (telemetry.IsTransportConnected && telemetry.IsCalibrated && telemetry.HasTracking)
            ProcessCanonicalWaveformSample(telemetry.VolumeBase01, now);

        Advance(now);
    }

    public void Advance()
    {
        Advance(GetCurrentTimeSeconds());
    }

    public PolarBreathingDynamicsTelemetry GetTelemetry()
    {
        double now = GetCurrentTimeSeconds();
        Advance(now);

        bool isFresh = _hasReceivedAnyWaveformSample &&
            (now - _lastWaveformSampleAt) <= Math.Max(0.1f, Settings.StaleTimeoutSeconds);
        PolarBreathingDynamicsTrackingState trackingState = !_isTransportConnected
            ? PolarBreathingDynamicsTrackingState.Unavailable
            : !_isBreathingCalibrated
                ? PolarBreathingDynamicsTrackingState.WaitingForCalibration
                : !_hasBreathingTracking
                    ? PolarBreathingDynamicsTrackingState.WaitingForBreathingTracking
                    : isFresh
                        ? PolarBreathingDynamicsTrackingState.Tracking
                        : PolarBreathingDynamicsTrackingState.Stale;

        int stabilizationCount = Math.Min(_intervalBreaths.Count, _amplitudeBreaths.Count);
        bool intervalBasicReady = _intervalBreaths.Count >= Settings.MinimumBreathsForBasicStats;
        bool amplitudeBasicReady = _amplitudeBreaths.Count >= Settings.MinimumBreathsForBasicStats;
        bool intervalEntropyReady = _intervalBreaths.Count >= Settings.MinimumBreathsForEntropy &&
            HasFiniteEntropyMetrics(_intervalFeatures);
        bool amplitudeEntropyReady = _amplitudeBreaths.Count >= Settings.MinimumBreathsForEntropy &&
            HasFiniteEntropyMetrics(_amplitudeFeatures);

        return new PolarBreathingDynamicsTelemetry(
            IsTransportConnected: _isTransportConnected,
            HasReceivedAnyWaveformSample: _hasReceivedAnyWaveformSample,
            IsBreathingCalibrated: _isBreathingCalibrated,
            HasBreathingTracking: _hasBreathingTracking,
            TrackingState: trackingState,
            HasTracking: trackingState == PolarBreathingDynamicsTrackingState.Tracking,
            HasAcceptedAnyBreath: _amplitudeBreaths.Count > 0 || _intervalBreaths.Count > 0,
            IntervalHasBasicStats: intervalBasicReady,
            AmplitudeHasBasicStats: amplitudeBasicReady,
            IntervalHasEntropyMetrics: intervalEntropyReady,
            AmplitudeHasEntropyMetrics: amplitudeEntropyReady,
            AcceptedExtremumCount: (int)Math.Min(int.MaxValue, AcceptedExtremumCount),
            IntervalBreathCount: _intervalBreaths.Count,
            AmplitudeBreathCount: _amplitudeBreaths.Count,
            StabilizationProgress01: InverseLerp(Settings.MinimumBreathsForBasicStats, Settings.FullConfidenceBreathCount, stabilizationCount),
            Confidence01: intervalEntropyReady && amplitudeEntropyReady
                ? InverseLerp(Settings.MinimumBreathsForEntropy, Settings.FullConfidenceBreathCount, stabilizationCount)
                : 0f,
            LastWaveformSampleAgeSeconds: LastWaveformReceivedAtUtc.HasValue
                ? (float)Math.Max(0d, (DateTimeOffset.UtcNow - LastWaveformReceivedAtUtc.Value).TotalSeconds)
                : _hasReceivedAnyWaveformSample ? (float)Math.Max(0d, now - _lastWaveformSampleAt) : 0f,
            LastBreathAgeSeconds: LastBreathDetectedAtUtc.HasValue
                ? (float)Math.Max(0d, (DateTimeOffset.UtcNow - LastBreathDetectedAtUtc.Value).TotalSeconds)
                : (float)Math.Max(0d, now - _lastBreathAcceptedAt),
            Interval: _intervalFeatures,
            Amplitude: _amplitudeFeatures,
            Settings: Settings,
            LastWaveformReceivedAtUtc: LastWaveformReceivedAtUtc,
            LastBreathDetectedAtUtc: LastBreathDetectedAtUtc);
    }

    public PolarBreathingDynamicsDiagnostics GetDiagnostics()
    {
        return new PolarBreathingDynamicsDiagnostics(
            WaveformSamples: _waveformSamples.ToArray(),
            AcceptedExtrema: _acceptedExtremaHistory.ToArray(),
            IntervalSeries: _intervalSeriesHistory.ToArray(),
            AmplitudeSeries: _amplitudeSeriesHistory.ToArray());
    }

    private void Advance(double now)
    {
        if (_lastAdvanceAt <= 0d)
            _lastAdvanceAt = now;
    }

    private void ProcessCanonicalWaveformSample(float volumeBase01, double now)
    {
        float volume = Clamp01(volumeBase01);
        AppendRollingWaveform(_waveformSamples, new PolarBreathingWaveformPoint(now, volume), 8192);
        if (!_hasLastVolumeSample)
        {
            _hasLastVolumeSample = true;
            _lastVolumeBase01 = volume;
            _lastVolumeTime = now;
            return;
        }

        float delta = volume - _lastVolumeBase01;
        if (Math.Abs(delta) < Math.Max(0.0001f, Settings.TurningPointDeltaThreshold))
        {
            _lastVolumeBase01 = volume;
            _lastVolumeTime = now;
            return;
        }

        sbyte direction = delta > 0f ? (sbyte)1 : (sbyte)-1;
        if (_trendDirection == 0)
        {
            _trendDirection = direction;
            _trendExtremeTime = _lastVolumeTime;
            _trendExtremeValue = _lastVolumeBase01;
            _hasTrendExtreme = true;
        }
        else if (direction == _trendDirection)
        {
            UpdateTrendExtreme(volume, now);
        }
        else
        {
            if (_hasTrendExtreme)
            {
                TryAcceptExtremum(
                    _trendDirection > 0 ? ExtremumKind.Peak : ExtremumKind.Trough,
                    _trendExtremeTime,
                    _trendExtremeValue);
            }

            _trendDirection = direction;
            _trendExtremeTime = now;
            _trendExtremeValue = volume;
            _hasTrendExtreme = true;
        }

        _lastVolumeBase01 = volume;
        _lastVolumeTime = now;
    }

    private void UpdateTrendExtreme(float volume, double now)
    {
        if (_trendDirection > 0)
        {
            if (volume >= _trendExtremeValue)
            {
                _trendExtremeValue = volume;
                _trendExtremeTime = now;
            }
        }
        else if (volume <= _trendExtremeValue)
        {
            _trendExtremeValue = volume;
            _trendExtremeTime = now;
        }
    }

    private void TryAcceptExtremum(ExtremumKind kind, double timeSeconds, float value01)
    {
        Extremum candidate = new(kind, timeSeconds, value01);
        if (_lastAcceptedExtremum is not Extremum lastAccepted)
        {
            _lastAcceptedExtremum = candidate;
            if (candidate.Kind == ExtremumKind.Peak)
                _lastAcceptedPeak = candidate;
            else
                _lastAcceptedTrough = candidate;
            AcceptedExtremumCount = 1;
            _lastBreathAcceptedAt = timeSeconds;
            LastBreathDetectedAtUtc = ResolveEventTimeUtc(timeSeconds);
            UpsertLastAcceptedExtremumHistory(candidate);
            return;
        }

        if (lastAccepted.Kind == candidate.Kind)
        {
            bool isBetterReplacement = candidate.Kind == ExtremumKind.Peak
                ? candidate.Value01 > lastAccepted.Value01
                : candidate.Value01 < lastAccepted.Value01;
            if (isBetterReplacement)
            {
                _lastAcceptedExtremum = candidate;
                if (candidate.Kind == ExtremumKind.Peak)
                    _lastAcceptedPeak = candidate;
                else
                    _lastAcceptedTrough = candidate;
                UpsertLastAcceptedExtremumHistory(candidate);
            }
            return;
        }

        double spacingSeconds = candidate.TimeSeconds - lastAccepted.TimeSeconds;
        if (spacingSeconds < Math.Max(0.05f, Settings.MinimumExtremumSpacingSeconds))
            return;

        float excursion01 = Math.Abs(candidate.Value01 - lastAccepted.Value01);
        if (excursion01 < Math.Max(0.001f, Settings.MinimumCycleExcursion01))
            return;

        Extremum? previousSameKind = candidate.Kind == ExtremumKind.Peak
            ? _lastAcceptedPeak
            : _lastAcceptedTrough;
        _lastAcceptedExtremum = candidate;
        AcceptedExtremumCount++;
        _lastBreathAcceptedAt = candidate.TimeSeconds;
        LastBreathDetectedAtUtc = ResolveEventTimeUtc(candidate.TimeSeconds);
        AppendRolling(
            _amplitudeSeriesHistory,
            new PolarBreathingDerivedPoint(
                _amplitudeSeriesHistory.Count + 1,
                candidate.TimeSeconds,
                excursion01),
            Settings.RetainedBreathCount);

        AppendRolling(_amplitudeBreaths, excursion01, Settings.RetainedBreathCount);
        if (previousSameKind is Extremum sameKind)
        {
            float intervalSeconds = (float)Math.Max(0d, candidate.TimeSeconds - sameKind.TimeSeconds);
            AppendRolling(_intervalBreaths, intervalSeconds, Settings.RetainedBreathCount);
            AppendRolling(
                _intervalSeriesHistory,
                new PolarBreathingDerivedPoint(
                    _intervalSeriesHistory.Count + 1,
                    candidate.TimeSeconds,
                    intervalSeconds),
                Settings.RetainedBreathCount);
        }

        if (candidate.Kind == ExtremumKind.Peak)
            _lastAcceptedPeak = candidate;
        else
            _lastAcceptedTrough = candidate;

        _acceptedExtremaHistory.Add(ToExtremumPoint(candidate));
        RecomputeCachedFeatures();
    }

    private void RecomputeCachedFeatures()
    {
        _intervalFeatures = PolarBreathingFeatureCalculator.Compute(_intervalBreaths, Settings);
        _amplitudeFeatures = PolarBreathingFeatureCalculator.Compute(_amplitudeBreaths, Settings);
    }

    private void ResetRuntimeState()
    {
        ResetDerivedSeries();
        _hasReceivedAnyWaveformSample = false;
        _isBreathingCalibrated = false;
        _hasBreathingTracking = false;
        _wasBreathingCalibrated = false;
        _lastWaveformSampleAt = 0d;
        _lastAdvanceAt = 0d;
        _timebaseKind = TimebaseKind.Uninitialized;
        _utcTimebaseOrigin = default;
        WaveformSampleCount = 0;
        LastWaveformReceivedAtUtc = null;
    }

    private void ResetDerivedSeries()
    {
        _hasLastVolumeSample = false;
        _lastVolumeTime = 0d;
        _lastVolumeBase01 = 0f;
        _trendDirection = 0;
        _hasTrendExtreme = false;
        _trendExtremeTime = 0d;
        _trendExtremeValue = 0f;
        _lastAcceptedExtremum = null;
        _lastAcceptedPeak = null;
        _lastAcceptedTrough = null;
        _intervalBreaths.Clear();
        _amplitudeBreaths.Clear();
        _waveformSamples.Clear();
        _acceptedExtremaHistory.Clear();
        _intervalSeriesHistory.Clear();
        _amplitudeSeriesHistory.Clear();
        _intervalFeatures = PolarBreathingFeatureSet.Empty;
        _amplitudeFeatures = PolarBreathingFeatureSet.Empty;
        AcceptedExtremumCount = 0;
        _lastBreathAcceptedAt = 0d;
        LastBreathDetectedAtUtc = null;
    }

    private static void AppendRolling(List<float> values, float value, int maxCount)
    {
        values.Add(value);
        if (values.Count > maxCount)
            values.RemoveAt(0);
    }

    private static void AppendRolling<T>(List<T> values, T value, int maxCount)
    {
        values.Add(value);
        if (values.Count > maxCount)
            values.RemoveAt(0);
    }

    private static void AppendRollingWaveform(List<PolarBreathingWaveformPoint> values, PolarBreathingWaveformPoint value, int maxCount)
    {
        values.Add(value);
        if (values.Count > maxCount)
            values.RemoveAt(0);
    }

    private void UpsertLastAcceptedExtremumHistory(Extremum candidate)
    {
        PolarBreathingExtremumPoint point = ToExtremumPoint(candidate);
        if (_acceptedExtremaHistory.Count == 0)
        {
            _acceptedExtremaHistory.Add(point);
            return;
        }

        _acceptedExtremaHistory[^1] = point;
    }

    private static PolarBreathingExtremumPoint ToExtremumPoint(Extremum extremum)
        => new(
            Kind: extremum.Kind == ExtremumKind.Peak ? "Peak" : "Trough",
            TimeSeconds: extremum.TimeSeconds,
            Value01: extremum.Value01);

    private static bool HasFiniteEntropyMetrics(PolarBreathingFeatureSet features)
        => float.IsFinite(features.SampleEntropy) &&
           float.IsFinite(features.MultiscaleEntropy);

    private double ResolveSampleTimeSeconds(DateTimeOffset? sampleAtUtc)
    {
        if (_timebaseKind == TimebaseKind.Uninitialized)
        {
            if (sampleAtUtc.HasValue)
            {
                _timebaseKind = TimebaseKind.UtcTimestamp;
                _utcTimebaseOrigin = sampleAtUtc.Value;
                return 0d;
            }

            _timebaseKind = TimebaseKind.Stopwatch;
        }

        if (_timebaseKind == TimebaseKind.UtcTimestamp && sampleAtUtc.HasValue)
            return Math.Max(0d, (sampleAtUtc.Value - _utcTimebaseOrigin).TotalSeconds);

        return NowSeconds();
    }

    private double GetCurrentTimeSeconds()
    {
        if (_timebaseKind == TimebaseKind.UtcTimestamp && LastWaveformReceivedAtUtc.HasValue)
            return _lastWaveformSampleAt + Math.Max(0d, (DateTimeOffset.UtcNow - LastWaveformReceivedAtUtc.Value).TotalSeconds);

        return NowSeconds();
    }

    private DateTimeOffset ResolveEventTimeUtc(double timeSeconds)
    {
        return _timebaseKind == TimebaseKind.UtcTimestamp
            ? _utcTimebaseOrigin.AddSeconds(timeSeconds)
            : DateTimeOffset.UtcNow;
    }

    private static double NowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static float InverseLerp(int start, int end, int value)
    {
        if (end <= start)
            return value >= end ? 1f : 0f;

        float t = (value - start) / (float)(end - start);
        return Clamp01(t);
    }

    private static class PolarBreathingFeatureCalculator
    {
        public static PolarBreathingFeatureSet Compute(IReadOnlyList<float> samples, PolarBreathingDynamicsSettings settings)
        {
            if (samples.Count < settings.MinimumBreathsForBasicStats)
                return PolarBreathingFeatureSet.Empty;

            float mean = ComputeMean(samples);
            float standardDeviation = ComputeStandardDeviation(samples, mean);
            float coefficientOfVariation = Math.Abs(mean) > 1e-6f ? standardDeviation / Math.Abs(mean) : 0f;
            float autocorrelationWindow50 = ComputeAutocorrelationWindow50(samples, mean);
            float psdSlope = ComputePsdSlope(samples);

            if (samples.Count < settings.MinimumBreathsForEntropy)
            {
                return new PolarBreathingFeatureSet(
                    Mean: mean,
                    StandardDeviation: standardDeviation,
                    CoefficientOfVariation: coefficientOfVariation,
                    AutocorrelationWindow50: autocorrelationWindow50,
                    PsdSlope: psdSlope,
                    LempelZivComplexity: 0f,
                    SampleEntropy: 0f,
                    MultiscaleEntropy: 0f);
            }

            float lempelZivComplexity = ComputeNormalizedLempelZivComplexity(samples, mean);
            float sampleEntropy = ComputeSampleEntropy(
                samples,
                settings.SampleEntropyDimension,
                settings.SampleEntropyDelay,
                settings.SampleEntropyToleranceSdFactor);
            float multiscaleEntropy = ComputeMultiscaleEntropyAuc(
                samples,
                settings.MultiscaleEntropyDimension,
                settings.MultiscaleEntropyDelay,
                settings.MultiscaleEntropyToleranceSdFactor,
                settings.MultiscaleEntropyMaxScale);

            return new PolarBreathingFeatureSet(
                Mean: mean,
                StandardDeviation: standardDeviation,
                CoefficientOfVariation: coefficientOfVariation,
                AutocorrelationWindow50: autocorrelationWindow50,
                PsdSlope: psdSlope,
                LempelZivComplexity: lempelZivComplexity,
                SampleEntropy: sampleEntropy,
                MultiscaleEntropy: multiscaleEntropy);
        }

        private static float ComputeMean(IReadOnlyList<float> samples)
        {
            double sum = 0d;
            for (int i = 0; i < samples.Count; i++)
                sum += samples[i];
            return (float)(sum / samples.Count);
        }

        private static float ComputeStandardDeviation(IReadOnlyList<float> samples, float mean)
        {
            if (samples.Count < 2)
                return 0f;

            double sum = 0d;
            for (int i = 0; i < samples.Count; i++)
            {
                double delta = samples[i] - mean;
                sum += delta * delta;
            }

            return (float)Math.Sqrt(sum / (samples.Count - 1));
        }

        private static float ComputeAutocorrelationWindow50(IReadOnlyList<float> samples, float mean)
        {
            if (samples.Count < 3)
                return 0f;

            double denominator = 0d;
            for (int i = 0; i < samples.Count; i++)
            {
                double delta = samples[i] - mean;
                denominator += delta * delta;
            }

            if (denominator <= 0d)
                return 0f;

            for (int lag = 1; lag < samples.Count; lag++)
            {
                double numerator = 0d;
                for (int i = 0; i < samples.Count - lag; i++)
                    numerator += (samples[i] - mean) * (samples[i + lag] - mean);

                double rho = numerator / denominator;
                if (rho < 0.5d)
                    return lag;
            }

            return samples.Count - 1;
        }

        private static float ComputePsdSlope(IReadOnlyList<float> samples)
        {
            int count = samples.Count;
            if (count < 8)
                return 0f;

            double[] detrended = DetrendLinearly(samples);
            double squaredSum = 0d;
            for (int i = 0; i < detrended.Length; i++)
                squaredSum += detrended[i] * detrended[i];

            double standardDeviation = Math.Sqrt(squaredSum / count);
            if (standardDeviation <= 1e-9d)
                return 0f;

            for (int i = 0; i < detrended.Length; i++)
                detrended[i] /= standardDeviation;

            int bins = (count / 2) + 1;
            double[] frequencies = new double[bins];
            double[] powers = new double[bins];
            const double sampleRateHz = 1000d;
            for (int k = 0; k < bins; k++)
            {
                double real = 0d;
                double imaginary = 0d;
                for (int n = 0; n < count; n++)
                {
                    double angle = -2d * Math.PI * k * n / count;
                    real += detrended[n] * Math.Cos(angle);
                    imaginary += detrended[n] * Math.Sin(angle);
                }

                frequencies[k] = k * sampleRateHz / count;
                powers[k] = (real * real) + (imaginary * imaginary);
            }

            double maxFrequency = frequencies[^1];
            double cutoffFrequency = maxFrequency * 0.25d;
            List<double> logFrequencies = new(count / 2);
            List<double> logPowers = new(count / 2);
            for (int i = 1; i < frequencies.Length; i++)
            {
                double frequency = frequencies[i];
                double power = powers[i];
                if (frequency <= 0d || frequency >= cutoffFrequency || power <= 0d)
                    continue;

                logFrequencies.Add(Math.Log10(frequency));
                logPowers.Add(Math.Log10(power));
            }

            if (logFrequencies.Count < 3)
                return 0f;

            double meanX = logFrequencies.Average();
            double meanY = logPowers.Average();
            double numerator = 0d;
            double denominator = 0d;
            for (int i = 0; i < logFrequencies.Count; i++)
            {
                double dx = logFrequencies[i] - meanX;
                numerator += dx * (logPowers[i] - meanY);
                denominator += dx * dx;
            }

            if (denominator <= 0d)
                return 0f;

            return (float)(numerator / denominator);
        }

        private static float ComputeNormalizedLempelZivComplexity(IReadOnlyList<float> samples, float mean)
        {
            if (samples.Count == 0)
                return 0f;

            char[] binary = new char[samples.Count];
            for (int i = 0; i < samples.Count; i++)
                binary[i] = samples[i] >= mean ? '1' : '0';

            int complexity = 1;
            int index = 0;
            int candidateLength = 1;
            while (index + candidateLength <= binary.Length)
            {
                string candidate = new(binary, index, candidateLength);
                bool found = false;
                for (int search = 0; search < index; search++)
                {
                    int remaining = index - search;
                    if (remaining < candidateLength)
                        continue;

                    bool matches = true;
                    for (int offset = 0; offset < candidateLength; offset++)
                    {
                        if (binary[search + offset] != candidate[offset])
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                    {
                        found = true;
                        break;
                    }
                }

                if (found && index + candidateLength < binary.Length)
                {
                    candidateLength++;
                    continue;
                }

                if (index + candidateLength < binary.Length)
                    complexity++;

                index += candidateLength;
                candidateLength = 1;
            }

            double normalization = samples.Count / Math.Max(1d, Math.Log(samples.Count, 2d));
            return normalization > 0d ? (float)(complexity / normalization) : 0f;
        }

        private static float ComputeSampleEntropy(
            IReadOnlyList<float> samples,
            int dimension,
            int delay,
            float toleranceSdFactor,
            float? absoluteTolerance = null)
        {
            int requiredCount = (dimension + 1) * delay + 1;
            if (samples.Count < requiredCount)
                return 0f;

            float standardDeviation = ComputeStandardDeviation(samples, ComputeMean(samples));
            if (standardDeviation <= 1e-6f)
                return 0f;

            float tolerance = absoluteTolerance ?? (standardDeviation * Math.Max(0.01f, toleranceSdFactor));
            if (!float.IsFinite(tolerance) || tolerance <= 0f)
                return float.NaN;

            int b = CountMatches(samples, dimension, delay, tolerance);
            int a = CountMatches(samples, dimension + 1, delay, tolerance);
            if (b <= 0)
                return float.NaN;
            if (a <= 0)
                return float.PositiveInfinity;

            return (float)(-Math.Log(a / (double)b));
        }

        private static int CountMatches(IReadOnlyList<float> samples, int dimension, int delay, float tolerance)
        {
            int count = 0;
            int maxIndex = samples.Count - ((dimension - 1) * delay);
            for (int i = 0; i < maxIndex; i++)
            {
                for (int j = i + 1; j < maxIndex; j++)
                {
                    bool matches = true;
                    for (int k = 0; k < dimension; k++)
                    {
                        int offset = k * delay;
                        if (Math.Abs(samples[i + offset] - samples[j + offset]) > tolerance)
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                        count++;
                }
            }

            return count;
        }

        private static float ComputeMultiscaleEntropyAuc(
            IReadOnlyList<float> samples,
            int dimension,
            int delay,
            float toleranceSdFactor,
            int maxScale)
        {
            if (samples.Count < Math.Max(4, maxScale))
                return 0f;

            float absoluteTolerance = ComputeAbsoluteTolerance(samples, toleranceSdFactor);
            if (!float.IsFinite(absoluteTolerance) || absoluteTolerance <= 0f)
                return 0f;

            List<float> entropyByScale = new(maxScale);
            for (int scale = 1; scale <= Math.Max(1, maxScale); scale++)
            {
                float[] coarse = CoarseGrain(samples, scale);
                if (coarse.Length < ((dimension + 1) * delay) + 1)
                    continue;

                float entropy = ComputeSampleEntropy(
                    coarse,
                    dimension,
                    delay,
                    toleranceSdFactor,
                    absoluteTolerance);
                if (!float.IsFinite(entropy) || entropy == 0f)
                    continue;

                entropyByScale.Add(entropy);
            }

            if (entropyByScale.Count == 0)
                return float.NaN;
            if (entropyByScale.Count == 1)
                return entropyByScale[0];

            double area = 0d;
            for (int i = 1; i < entropyByScale.Count; i++)
                area += (entropyByScale[i - 1] + entropyByScale[i]) * 0.5d;

            return (float)(area / entropyByScale.Count);
        }

        private static float[] CoarseGrain(IReadOnlyList<float> samples, int scale)
        {
            int coarseCount = samples.Count / scale;
            float[] coarse = new float[coarseCount];
            for (int coarseIndex = 0; coarseIndex < coarseCount; coarseIndex++)
            {
                double sum = 0d;
                int start = coarseIndex * scale;
                for (int offset = 0; offset < scale; offset++)
                    sum += samples[start + offset];
                coarse[coarseIndex] = (float)(sum / scale);
            }

            return coarse;
        }

        private static float ComputeAbsoluteTolerance(IReadOnlyList<float> samples, float toleranceSdFactor)
        {
            float standardDeviation = ComputeStandardDeviation(samples, ComputeMean(samples));
            if (standardDeviation <= 1e-6f)
                return 0f;

            return standardDeviation * Math.Max(0.01f, toleranceSdFactor);
        }

        private static double[] DetrendLinearly(IReadOnlyList<float> samples)
        {
            int count = samples.Count;
            double meanX = (count - 1) * 0.5d;
            double meanY = samples.Average(static value => (double)value);
            double numerator = 0d;
            double denominator = 0d;
            for (int i = 0; i < count; i++)
            {
                double dx = i - meanX;
                numerator += dx * (samples[i] - meanY);
                denominator += dx * dx;
            }

            double slope = denominator > 0d ? numerator / denominator : 0d;
            double intercept = meanY - (slope * meanX);
            double[] detrended = new double[count];
            for (int i = 0; i < count; i++)
                detrended[i] = samples[i] - ((slope * i) + intercept);

            return detrended;
        }
    }
}
