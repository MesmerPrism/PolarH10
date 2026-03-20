using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Protocol.Tests;

public sealed class PolarBreathingDynamicsTrackerTests
{
    [Fact]
    public void Tracker_ProducesIntervalAndAmplitudeEntropy_OnRegularWaveform()
    {
        PolarBreathingDynamicsTracker tracker = CreateTracker();
        tracker.SetTransportConnected(true);

        FeedWaveform(
            tracker,
            totalSeconds: 220d,
            sampleRateHz: 12.5d,
            volumeSelector: t =>
            {
                double phase = t * (Math.PI * 2.0 / 5.2d);
                return (float)(0.5d + (Math.Sin(phase) * 0.28d) + (Math.Sin(phase * 2.0d) * 0.03d));
            });

        PolarBreathingDynamicsTelemetry telemetry = tracker.GetTelemetry();

        Assert.True(telemetry.HasTracking);
        Assert.True(telemetry.IntervalHasBasicStats);
        Assert.True(telemetry.AmplitudeHasBasicStats);
        Assert.True(telemetry.IntervalHasEntropyMetrics);
        Assert.True(telemetry.AmplitudeHasEntropyMetrics);
        Assert.True(telemetry.HasAcceptedAnyBreath);
        Assert.InRange(telemetry.IntervalBreathCount, 18, 180);
        Assert.InRange(telemetry.AmplitudeBreathCount, 18, 180);
        Assert.InRange(telemetry.StabilizationProgress01, 0.5f, 1f);
        Assert.InRange(telemetry.Confidence01, 0.1f, 1f);
        Assert.InRange(telemetry.Interval.SampleEntropy, 0f, 2f);
        Assert.InRange(telemetry.Amplitude.SampleEntropy, 0f, 2f);
    }

    [Fact]
    public void JitteredWaveform_ProducesHigherEntropyThanRegularWaveform()
    {
        PolarBreathingDynamicsTracker regularTracker = CreateTracker();
        regularTracker.SetTransportConnected(true);
        FeedWaveform(
            regularTracker,
            totalSeconds: 220d,
            sampleRateHz: 12.5d,
            volumeSelector: t =>
            {
                double phase = t * (Math.PI * 2.0 / 5.2d);
                return (float)(0.5d + (Math.Sin(phase) * 0.28d) + (Math.Sin(phase * 2.0d) * 0.03d));
            });

        PolarBreathingDynamicsTracker jitteredTracker = CreateTracker();
        jitteredTracker.SetTransportConnected(true);
        FeedWaveform(
            jitteredTracker,
            totalSeconds: 220d,
            sampleRateHz: 12.5d,
            volumeSelector: BuildJitteredWaveformSelector());

        PolarBreathingDynamicsTelemetry regular = regularTracker.GetTelemetry();
        PolarBreathingDynamicsTelemetry jittered = jitteredTracker.GetTelemetry();

        Assert.True(regular.IntervalHasEntropyMetrics);
        Assert.True(jittered.IntervalHasEntropyMetrics);
        Assert.True(jittered.Interval.SampleEntropy > regular.Interval.SampleEntropy);
        Assert.True(jittered.Amplitude.SampleEntropy > regular.Amplitude.SampleEntropy);
        Assert.True(jittered.Interval.LempelZivComplexity >= regular.Interval.LempelZivComplexity);
        Assert.True(jittered.Amplitude.LempelZivComplexity >= regular.Amplitude.LempelZivComplexity);
    }

    [Fact]
    public void Tracker_BecomesStale_WhenWaveformStops()
    {
        PolarBreathingDynamicsTracker tracker = new(new PolarBreathingDynamicsSettings
        {
            MinimumBreathsForBasicStats = 6,
            MinimumBreathsForEntropy = 18,
            FullConfidenceBreathCount = 48,
            RetainedBreathCount = 180,
            StaleTimeoutSeconds = 0.1f,
        });
        tracker.SetTransportConnected(true);

        FeedWaveform(
            tracker,
            totalSeconds: 120d,
            sampleRateHz: 12.5d,
            volumeSelector: t =>
            {
                double phase = t * (Math.PI * 2.0 / 5.4d);
                return (float)(0.5d + (Math.Sin(phase) * 0.27d));
            });

        Thread.Sleep(150);
        PolarBreathingDynamicsTelemetry telemetry = tracker.GetTelemetry();

        Assert.Equal(PolarBreathingDynamicsTrackingState.Stale, telemetry.TrackingState);
        Assert.False(telemetry.HasTracking);
        Assert.True(telemetry.LastWaveformSampleAgeSeconds >= 0.1f);
    }

    [Fact]
    public void ApplySettings_ResetTracker_ClearsRuntimeStateAndKeepsConnection()
    {
        PolarBreathingDynamicsTracker tracker = CreateTracker();
        tracker.SetTransportConnected(true);
        FeedWaveform(
            tracker,
            totalSeconds: 160d,
            sampleRateHz: 12.5d,
            volumeSelector: t =>
            {
                double phase = t * (Math.PI * 2.0 / 5.0d);
                return (float)(0.5d + (Math.Sin(phase) * 0.29d));
            });

        tracker.ApplySettings(PolarBreathingDynamicsSettings.CreateDefault() with
        {
            MinimumBreathsForBasicStats = 10,
            MinimumBreathsForEntropy = 20,
            FullConfidenceBreathCount = 60,
        }, resetTracker: true);

        PolarBreathingDynamicsTelemetry telemetry = tracker.GetTelemetry();

        Assert.True(telemetry.IsTransportConnected);
        Assert.False(telemetry.HasReceivedAnyWaveformSample);
        Assert.False(telemetry.HasAcceptedAnyBreath);
        Assert.Equal(0, telemetry.IntervalBreathCount);
        Assert.Equal(0, telemetry.AmplitudeBreathCount);
        Assert.Equal(10, telemetry.Settings.MinimumBreathsForBasicStats);
        Assert.Equal(20, telemetry.Settings.MinimumBreathsForEntropy);
    }

    [Fact]
    public void CalibrationLoss_ClearsDerivedHistory()
    {
        PolarBreathingDynamicsTracker tracker = CreateTracker();
        tracker.SetTransportConnected(true);

        FeedWaveform(
            tracker,
            totalSeconds: 180d,
            sampleRateHz: 12.5d,
            volumeSelector: t =>
            {
                double phase = t * (Math.PI * 2.0 / 5.1d);
                return (float)(0.5d + (Math.Sin(phase) * 0.28d));
            });

        DateTimeOffset sampleAtUtc = DateTimeOffset.UtcNow;
        tracker.SubmitBreathingTelemetry(CreateTelemetry(0.5f, sampleAtUtc, connected: true, calibrated: false, tracking: false));

        PolarBreathingDynamicsTelemetry telemetry = tracker.GetTelemetry();

        Assert.Equal(PolarBreathingDynamicsTrackingState.WaitingForCalibration, telemetry.TrackingState);
        Assert.False(telemetry.HasAcceptedAnyBreath);
        Assert.Equal(0, telemetry.IntervalBreathCount);
        Assert.Equal(0, telemetry.AmplitudeBreathCount);
        Assert.False(telemetry.IntervalHasEntropyMetrics);
        Assert.False(telemetry.AmplitudeHasEntropyMetrics);
    }

    private static PolarBreathingDynamicsTracker CreateTracker()
    {
        return new PolarBreathingDynamicsTracker(new PolarBreathingDynamicsSettings
        {
            MinimumBreathsForBasicStats = 6,
            MinimumBreathsForEntropy = 18,
            FullConfidenceBreathCount = 48,
            RetainedBreathCount = 180,
        });
    }

    private static void FeedWaveform(
        PolarBreathingDynamicsTracker tracker,
        double totalSeconds,
        double sampleRateHz,
        Func<double, float> volumeSelector)
    {
        DateTimeOffset startUtc = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(totalSeconds);
        int sampleCount = (int)Math.Ceiling(totalSeconds * sampleRateHz);
        double stepSeconds = 1d / sampleRateHz;

        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            double t = sampleIndex * stepSeconds;
            DateTimeOffset sampleAtUtc = startUtc.AddSeconds(t);
            float volume = Math.Clamp(volumeSelector(t), 0.02f, 0.98f);
            tracker.SubmitBreathingTelemetry(CreateTelemetry(volume, sampleAtUtc, connected: true, calibrated: true, tracking: true));
        }
    }

    private static Func<double, float> BuildJitteredWaveformSelector()
    {
        double phase = 0d;
        double lastTime = 0d;
        bool initialized = false;

        return t =>
        {
            double dt = initialized ? t - lastTime : 0d;
            initialized = true;
            lastTime = t;

            double periodSeconds = 5.0d + (Math.Sin(t * 0.17d) * 1.0d) + (Math.Cos(t * 0.31d) * 0.55d);
            double amplitude = 0.24d + (Math.Sin(t * 0.23d) * 0.06d) + (Math.Cos(t * 0.41d) * 0.03d);
            phase += (Math.PI * 2.0d * dt) / periodSeconds;

            return (float)(
                0.5d +
                (Math.Sin(phase) * amplitude) +
                (Math.Sin((phase * 2.0d) + 0.4d) * 0.05d) +
                (Math.Cos(t * 1.35d) * 0.015d));
        };
    }

    private static PolarBreathingTelemetry CreateTelemetry(
        float volumeBase01,
        DateTimeOffset sampleAtUtc,
        bool connected,
        bool calibrated,
        bool tracking)
    {
        float volume = Math.Clamp(volumeBase01, 0f, 1f);
        return new PolarBreathingTelemetry(
            IsTransportConnected: connected,
            HasReceivedAnySample: true,
            IsCalibrating: false,
            IsCalibrated: calibrated,
            HasTracking: tracking,
            HasUsefulSignal: true,
            HasXzModel: true,
            CalibrationProgress01: calibrated ? 1f : 0f,
            CurrentVolume01: volume,
            CurrentState: PolarBreathingState.Pausing,
            EstimatedSampleRateHz: 100f,
            UsefulAxisRangeG: 0.024f,
            LastProjectionG: 0f,
            Volume3d01: volume,
            VolumeBase01: volume,
            VolumeXz01: volume,
            Axis: System.Numerics.Vector3.UnitZ,
            Center: System.Numerics.Vector3.Zero,
            BoundMin: 0f,
            BoundMax: 1f,
            XzAxis: new System.Numerics.Vector2(1f, 0f),
            XzBoundMin: 0f,
            XzBoundMax: 1f,
            AccFrameCount: 1,
            AccSampleCount: 1,
            LastSampleAgeSeconds: 0f,
            LastCalibrationFailureReason: string.Empty,
            Settings: PolarBreathingSettings.CreateDefault(),
            LastSampleReceivedAtUtc: sampleAtUtc);
    }
}
