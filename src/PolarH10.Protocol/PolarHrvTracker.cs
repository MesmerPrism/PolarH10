using System.Diagnostics;

namespace PolarH10.Protocol;

/// <summary>
/// Rolling short-term HRV tracker built from accepted RR intervals.
/// It exposes RMSSD as the headline value and keeps the related time-domain
/// telemetry that the operator surface needs for interpretation.
/// </summary>
public sealed class PolarHrvTracker
{
    private const float FirstSolveCoverageRequirement01 = 0.99f;
    private const float PnnThresholdMs = 50f;

    private struct SamplePoint
    {
        public double XMs;
        public float IbiMs;
    }

    private readonly List<SamplePoint> _samples = new(512);

    private bool _isTransportConnected;
    private bool _hasReceivedAnyRrSample;
    private bool _hasMetrics;
    private double _lastRrSampleAt;
    private double _lastMetricsAt;
    private double _currentXMs;
    private float _currentRmssdMs;
    private float _lnRmssd;
    private float _sdnnMs;
    private float _pnn50Percent;
    private float _sd1Ms;
    private float _meanNnMs;
    private float _meanHeartRateBpm;
    private float _lastAcceptedIbiMs;
    private float _lastHeartbeatBpm;

    public PolarHrvTracker(PolarHrvSettings? settings = null)
    {
        Settings = (settings ?? PolarHrvSettings.CreateDefault()).Clamp();
        ResetRuntimeState();
    }

    public PolarHrvSettings Settings { get; private set; }

    public long RrSampleCount { get; private set; }
    public DateTimeOffset? LastRrReceivedAtUtc { get; private set; }

    public void Reset()
    {
        ResetRuntimeState();
    }

    public void ApplySettings(PolarHrvSettings settings, bool resetTracker = true)
    {
        Settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Clamp();
        if (resetTracker)
        {
            ResetRuntimeState();
        }
        else
        {
            TrimWindow();
            TrySolveMetrics(NowSeconds());
        }
    }

    public void SetTransportConnected(bool isConnected)
    {
        _isTransportConnected = isConnected;
    }

    public void SubmitHrRrSample(HrRrSample sample)
    {
        if (sample.RrIntervalsMs is null || sample.RrIntervalsMs.Length == 0)
            return;

        for (int i = 0; i < sample.RrIntervalsMs.Length; i++)
            SubmitRrInterval(sample.RrIntervalsMs[i]);
    }

    public void SubmitRrInterval(float ibiMs)
    {
        if (ibiMs <= 0f)
            return;

        double now = NowSeconds();
        _hasReceivedAnyRrSample = true;
        _lastRrSampleAt = now;
        LastRrReceivedAtUtc = DateTimeOffset.UtcNow;
        RrSampleCount++;

        if (!PolarRrIntervalValidator.IsValid(ibiMs))
            return;

        _lastAcceptedIbiMs = ibiMs;
        _lastHeartbeatBpm = Clamp(60000f / ibiMs, 0f, 260f);

        _samples.Add(new SamplePoint
        {
            XMs = _currentXMs,
            IbiMs = ibiMs,
        });
        _currentXMs += ibiMs;

        TrimWindow();
        TrySolveMetrics(now);
    }

    public void Advance()
    {
        _ = GetTelemetry();
    }

    public PolarHrvTelemetry GetTelemetry()
    {
        double now = NowSeconds();
        bool fresh = _hasMetrics && (now - _lastRrSampleAt) <= Math.Max(0.1f, Settings.StaleTimeoutSeconds);
        PolarHrvTrackingState trackingState = !_isTransportConnected
            ? PolarHrvTrackingState.Unavailable
            : !_hasReceivedAnyRrSample
                ? PolarHrvTrackingState.WaitingForRr
                : fresh
                    ? PolarHrvTrackingState.Tracking
                    : _hasMetrics
                        ? PolarHrvTrackingState.Stale
                        : PolarHrvTrackingState.WarmingUp;

        int minimumSampleRequirement = Math.Max(2, Settings.MinimumRrSamples);
        return new PolarHrvTelemetry(
            IsTransportConnected: _isTransportConnected,
            HasReceivedAnyRrSample: _hasReceivedAnyRrSample,
            HasMetricsSample: _hasMetrics,
            TrackingState: trackingState,
            HasTracking: trackingState == PolarHrvTrackingState.Tracking,
            CurrentRmssdMs: _hasMetrics ? _currentRmssdMs : 0f,
            LnRmssd: _hasMetrics ? _lnRmssd : 0f,
            SdnnMs: _hasMetrics ? _sdnnMs : 0f,
            Pnn50Percent: _hasMetrics ? _pnn50Percent : 0f,
            Sd1Ms: _hasMetrics ? _sd1Ms : 0f,
            MeanNnMs: _hasMetrics ? _meanNnMs : 0f,
            MeanHeartRateBpm: _hasMetrics ? _meanHeartRateBpm : 0f,
            CurrentHeartbeatBpm: _lastHeartbeatBpm,
            CurrentHeartbeatIbiMs: _lastAcceptedIbiMs,
            RrSampleCount: (int)Math.Min(int.MaxValue, RrSampleCount),
            AcceptedWindowSampleCount: _samples.Count,
            MinimumSampleRequirement: minimumSampleRequirement,
            SampleRequirementProgress01: InverseLerp(minimumSampleRequirement, _samples.Count),
            WindowCoverage01: GetWindowCoverage01(),
            LastRrSampleAgeSeconds: LastRrReceivedAtUtc.HasValue
                ? (float)Math.Max(0d, (DateTimeOffset.UtcNow - LastRrReceivedAtUtc.Value).TotalSeconds)
                : _hasReceivedAnyRrSample ? (float)Math.Max(0d, now - _lastRrSampleAt) : 0f,
            LastMetricsAgeSeconds: _hasMetrics ? (float)Math.Max(0d, now - _lastMetricsAt) : 0f,
            Settings: Settings,
            LastRrReceivedAtUtc: LastRrReceivedAtUtc);
    }

    public PolarHrvDiagnostics GetDiagnostics()
    {
        PolarRrSamplePoint[] acceptedRrSamples = _samples
            .Select(static sample => new PolarRrSamplePoint(sample.XMs / 1000.0, sample.IbiMs))
            .ToArray();

        PolarHrvDeltaPoint[] adjacentRrDeltas = _samples.Count < 2
            ? []
            : Enumerable.Range(1, _samples.Count - 1)
                .Select(index => new PolarHrvDeltaPoint(
                    _samples[index].XMs / 1000.0,
                    _samples[index].IbiMs - _samples[index - 1].IbiMs))
                .ToArray();

        return new PolarHrvDiagnostics(
            AcceptedRrSamples: acceptedRrSamples,
            AdjacentRrDeltas: adjacentRrDeltas);
    }

    private void TrySolveMetrics(double now)
    {
        int minimumSampleRequirement = Math.Max(2, Settings.MinimumRrSamples);
        if (_samples.Count < minimumSampleRequirement)
            return;

        if (GetWindowCoverage01() < FirstSolveCoverageRequirement01)
            return;

        if (!TryComputeMetrics(
            out float rmssdMs,
            out float lnRmssd,
            out float sdnnMs,
            out float pnn50Percent,
            out float sd1Ms,
            out float meanNnMs,
            out float meanHeartRateBpm))
        {
            return;
        }

        _currentRmssdMs = rmssdMs;
        _lnRmssd = lnRmssd;
        _sdnnMs = sdnnMs;
        _pnn50Percent = pnn50Percent;
        _sd1Ms = sd1Ms;
        _meanNnMs = meanNnMs;
        _meanHeartRateBpm = meanHeartRateBpm;
        _hasMetrics = true;
        _lastMetricsAt = now;
    }

    private bool TryComputeMetrics(
        out float rmssdMs,
        out float lnRmssd,
        out float sdnnMs,
        out float pnn50Percent,
        out float sd1Ms,
        out float meanNnMs,
        out float meanHeartRateBpm)
    {
        rmssdMs = 0f;
        lnRmssd = 0f;
        sdnnMs = 0f;
        pnn50Percent = 0f;
        sd1Ms = 0f;
        meanNnMs = 0f;
        meanHeartRateBpm = 0f;

        if (_samples.Count < 2)
            return false;

        double sum = 0d;
        for (int i = 0; i < _samples.Count; i++)
            sum += _samples[i].IbiMs;

        double mean = sum / _samples.Count;
        if (mean <= 0d)
            return false;

        double varianceSum = 0d;
        for (int i = 0; i < _samples.Count; i++)
        {
            double delta = _samples[i].IbiMs - mean;
            varianceSum += delta * delta;
        }

        double rmssdNumerator = 0d;
        int diffCount = 0;
        int diffOver50Count = 0;
        for (int i = 1; i < _samples.Count; i++)
        {
            double delta = _samples[i].IbiMs - _samples[i - 1].IbiMs;
            rmssdNumerator += delta * delta;
            diffCount++;
            if (Math.Abs(delta) > PnnThresholdMs)
                diffOver50Count++;
        }

        if (diffCount <= 0)
            return false;

        meanNnMs = (float)mean;
        meanHeartRateBpm = Clamp(60000f / meanNnMs, 0f, 260f);
        sdnnMs = (float)Math.Sqrt(varianceSum / (_samples.Count - 1));
        rmssdMs = (float)Math.Sqrt(rmssdNumerator / diffCount);
        lnRmssd = rmssdMs > 0f ? (float)Math.Log(rmssdMs) : 0f;
        pnn50Percent = diffCount > 0 ? diffOver50Count * 100f / diffCount : 0f;
        sd1Ms = rmssdMs * 0.7071067811865475f;
        return true;
    }

    private void TrimWindow()
    {
        double windowMs = GetWindowLengthMs();
        while (_samples.Count > 1 && _samples[1].XMs < _currentXMs - windowMs)
            _samples.RemoveAt(0);
    }

    private float GetWindowCoverage01()
    {
        if (_samples.Count < 2)
            return 0f;

        double spanMs = _samples[^1].XMs - _samples[0].XMs;
        return Clamp((float)(spanMs / GetWindowLengthMs()), 0f, 1f);
    }

    private double GetWindowLengthMs()
        => Math.Max(30000d, Math.Min(600000d, Math.Max(30f, Settings.WindowSeconds) * 1000d));

    private void ResetRuntimeState()
    {
        _samples.Clear();
        _hasReceivedAnyRrSample = false;
        _hasMetrics = false;
        _lastRrSampleAt = 0d;
        _lastMetricsAt = 0d;
        _currentXMs = 0d;
        _currentRmssdMs = 0f;
        _lnRmssd = 0f;
        _sdnnMs = 0f;
        _pnn50Percent = 0f;
        _sd1Ms = 0f;
        _meanNnMs = 0f;
        _meanHeartRateBpm = 0f;
        _lastAcceptedIbiMs = 0f;
        _lastHeartbeatBpm = 0f;
        RrSampleCount = 0;
        LastRrReceivedAtUtc = null;
    }

    private static double NowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private static float InverseLerp(int end, int value)
    {
        if (end <= 0)
            return 1f;

        return Clamp(value / (float)end, 0f, 1f);
    }

    private static float Clamp(float value, float min, float max)
        => Math.Clamp(value, min, max);
}
