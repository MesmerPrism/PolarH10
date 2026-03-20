using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Protocol.Tests;

public sealed class PolarHrvTrackerTests
{
    [Fact]
    public void Tracker_ProducesShortTermMetrics_OnSyntheticRrSignal()
    {
        PolarHrvTracker tracker = new(new PolarHrvSettings
        {
            MinimumRrSamples = 32,
            WindowSeconds = 120f,
        });

        tracker.SetTransportConnected(true);
        FeedRrSignal(tracker, totalSeconds: 180d, centerIbiMs: 1000f, amplitudeMs: 80f, modulationHz: 0.10d);

        PolarHrvTelemetry telemetry = tracker.GetTelemetry();

        Assert.True(telemetry.HasMetricsSample);
        Assert.True(telemetry.HasTracking);
        Assert.InRange(telemetry.WindowCoverage01, 0.99f, 1f);
        Assert.InRange(telemetry.CurrentRmssdMs, 20f, 140f);
        Assert.InRange(telemetry.SdnnMs, 30f, 90f);
        Assert.InRange(telemetry.Pnn50Percent, 0f, 100f);
        Assert.Equal(telemetry.CurrentRmssdMs / MathF.Sqrt(2f), telemetry.Sd1Ms, 3);
        Assert.Equal((float)Math.Log(telemetry.CurrentRmssdMs), telemetry.LnRmssd, 3);
        Assert.InRange(telemetry.MeanHeartRateBpm, 50f, 75f);
    }

    [Fact]
    public void Tracker_BecomesStale_WhenRrStops()
    {
        PolarHrvTracker tracker = new(new PolarHrvSettings
        {
            MinimumRrSamples = 24,
            WindowSeconds = 60f,
            StaleTimeoutSeconds = 0.1f,
        });

        tracker.SetTransportConnected(true);
        FeedRrSignal(tracker, totalSeconds: 96d, centerIbiMs: 940f, amplitudeMs: 65f, modulationHz: 0.09d);

        Thread.Sleep(150);
        PolarHrvTelemetry telemetry = tracker.GetTelemetry();

        Assert.Equal(PolarHrvTrackingState.Stale, telemetry.TrackingState);
        Assert.False(telemetry.HasTracking);
        Assert.True(telemetry.HasMetricsSample);
    }

    [Fact]
    public void ApplySettings_ResetTracker_ClearsRuntimeStateAndKeepsConnection()
    {
        PolarHrvTracker tracker = new();
        tracker.SetTransportConnected(true);
        FeedRrSignal(tracker, totalSeconds: 340d, centerIbiMs: 980f, amplitudeMs: 70f, modulationHz: 0.08d);

        tracker.ApplySettings(PolarHrvSettings.CreateDefault() with
        {
            MinimumRrSamples = 48,
            WindowSeconds = 180f,
        }, resetTracker: true);

        PolarHrvTelemetry telemetry = tracker.GetTelemetry();

        Assert.True(telemetry.IsTransportConnected);
        Assert.False(telemetry.HasReceivedAnyRrSample);
        Assert.False(telemetry.HasMetricsSample);
        Assert.Equal(48, telemetry.Settings.MinimumRrSamples);
        Assert.Equal(180f, telemetry.Settings.WindowSeconds, 3);
    }

    private static void FeedRrSignal(
        PolarHrvTracker tracker,
        double totalSeconds,
        float centerIbiMs,
        float amplitudeMs,
        double modulationHz)
    {
        double elapsed = 0d;
        while (elapsed < totalSeconds)
        {
            double phase = elapsed * modulationHz * Math.PI * 2.0;
            float ibiMs = centerIbiMs + (float)(Math.Sin(phase) * amplitudeMs);
            tracker.SubmitRrInterval(ibiMs);
            elapsed += ibiMs / 1000.0;
        }
    }
}
