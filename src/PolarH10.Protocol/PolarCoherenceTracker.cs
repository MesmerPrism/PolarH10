using System.Diagnostics;

namespace PolarH10.Protocol;

/// <summary>
/// RR-interval coherence tracker that preserves the app's normalized 0..1
/// operator-facing behavior while exposing the raw paper-defined coherence ratio and
/// spectral telemetry for inspection.
/// </summary>
public sealed class PolarCoherenceTracker
{
    private readonly PolarCoherenceRrWindowCalculator _calculator = new();

    private bool _isTransportConnected;
    private bool _hasReceivedAnyRrSample;
    private bool _hasCoherence;
    private double _lastRrSampleAt;
    private double _lastCoherenceAt;
    private double _lastAdvanceAt;
    private float _targetCoherence01;
    private float _targetConfidence01;
    private float _smoothedCoherence01;
    private bool _hasSmoothedCoherence;
    private float _lastAcceptedIbiMs;
    private float _lastHeartbeatBpm;

    public PolarCoherenceTracker(PolarCoherenceSettings? settings = null)
    {
        Settings = (settings ?? PolarCoherenceSettings.CreateDefault()).Clamp();
        ResetRuntimeState();
    }

    public PolarCoherenceSettings Settings { get; private set; }

    public long RrSampleCount { get; private set; }
    public DateTimeOffset? LastRrReceivedAtUtc { get; private set; }

    public void Reset()
    {
        ResetRuntimeState();
    }

    public void ApplySettings(PolarCoherenceSettings settings, bool resetTracker = true)
    {
        Settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Clamp();
        if (resetTracker)
            ResetRuntimeState();
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

        if (PolarCoherenceRrWindowCalculator.IsValidIbi(ibiMs))
        {
            _lastAcceptedIbiMs = ibiMs;
            _lastHeartbeatBpm = Clamp(60000f / ibiMs, 0f, 260f);
        }

        if (_calculator.PushIbi(
            ibiMs,
            Settings.CoherenceWindowSeconds,
            Settings.MinimumIbiSamples,
            out float coherence01,
            out float confidence01))
        {
            _targetCoherence01 = coherence01;
            _targetConfidence01 = confidence01;
            _hasCoherence = true;
            _lastCoherenceAt = now;
        }

        Advance(now);
    }

    public void Advance()
    {
        Advance(NowSeconds());
    }

    public PolarCoherenceTelemetry GetTelemetry()
    {
        double now = NowSeconds();
        Advance(now);

        bool fresh = _hasCoherence && (now - _lastCoherenceAt) <= Math.Max(0.1f, Settings.StaleTimeoutSeconds);
        PolarCoherenceTrackingState trackingState = !_isTransportConnected
            ? PolarCoherenceTrackingState.Unavailable
            : fresh
                ? PolarCoherenceTrackingState.Tracking
                : PolarCoherenceTrackingState.Stale;

        return new PolarCoherenceTelemetry(
            IsTransportConnected: _isTransportConnected,
            HasReceivedAnyRrSample: _hasReceivedAnyRrSample,
            HasCoherenceSample: _hasCoherence,
            TrackingState: trackingState,
            HasTracking: trackingState == PolarCoherenceTrackingState.Tracking,
            CurrentCoherence01: _hasCoherence ? _smoothedCoherence01 : 0f,
            NormalizedCoherence01: _hasCoherence ? _targetCoherence01 : 0f,
            Confidence01: fresh ? _targetConfidence01 : 0f,
            CurrentHeartbeatBpm: _lastHeartbeatBpm,
            CurrentHeartbeatIbiMs: _lastAcceptedIbiMs,
            RrSampleCount: (int)Math.Min(int.MaxValue, RrSampleCount),
            ConsecutiveValidCount: _calculator.ConsecutiveValidCount,
            StabilizationRequiredCount: _calculator.StabilizationRequiredCount,
            StabilizationProgress01: _calculator.StabilizationProgress01,
            WindowCoverage01: _calculator.BufferCoverage01,
            LastRrSampleAgeSeconds: _hasReceivedAnyRrSample ? (float)Math.Max(0d, now - _lastRrSampleAt) : 0f,
            LastCoherenceAgeSeconds: _hasCoherence ? (float)Math.Max(0d, now - _lastCoherenceAt) : 0f,
            PeakFrequencyHz: _calculator.LastPeakFrequencyHz,
            PeakBandPower: _calculator.LastPeakBandPower,
            TotalBandPower: _calculator.LastTotalPower,
            PaperCoherenceRatio: _calculator.LastPaperCoherenceRatio,
            Settings: Settings,
            LastRrReceivedAtUtc: LastRrReceivedAtUtc);
    }

    public PolarCoherenceDiagnostics GetDiagnostics()
        => _calculator.GetDiagnostics();

    private void Advance(double now)
    {
        if (_lastAdvanceAt <= 0d)
        {
            _lastAdvanceAt = now;
            if (_hasCoherence && !_hasSmoothedCoherence)
            {
                _smoothedCoherence01 = _targetCoherence01;
                _hasSmoothedCoherence = true;
            }

            return;
        }

        double dt = Math.Max(0d, now - _lastAdvanceAt);
        _lastAdvanceAt = now;

        if (!_hasCoherence)
            return;

        if (!_hasSmoothedCoherence)
        {
            _smoothedCoherence01 = _targetCoherence01;
            _hasSmoothedCoherence = true;
            return;
        }

        double blend = 1.0 - Math.Exp(-Math.Max(0f, Settings.CoherenceSmoothingSpeed) * dt);
        _smoothedCoherence01 = Lerp(_smoothedCoherence01, _targetCoherence01, (float)blend);
    }

    private void ResetRuntimeState()
    {
        _calculator.Reset();
        _hasReceivedAnyRrSample = false;
        _hasCoherence = false;
        _lastRrSampleAt = 0d;
        _lastCoherenceAt = 0d;
        _lastAdvanceAt = 0d;
        _targetCoherence01 = 0f;
        _targetConfidence01 = 0f;
        _smoothedCoherence01 = 0f;
        _hasSmoothedCoherence = false;
        _lastAcceptedIbiMs = 0f;
        _lastHeartbeatBpm = 0f;
        RrSampleCount = 0;
        LastRrReceivedAtUtc = null;
    }

    private static double NowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private static float Lerp(float from, float to, float t)
        => from + ((to - from) * Clamp(t, 0f, 1f));

    private static float Clamp(float value, float min, float max)
        => Math.Clamp(value, min, max);
}
