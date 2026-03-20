using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Protocol.Tests;

public sealed class PolarCoherenceTrackerTests
{
    [Fact]
    public void Tracker_ProducesTrackingAndPeakNearResonance_OnSyntheticRrSignal()
    {
        var tracker = new PolarCoherenceTracker(new PolarCoherenceSettings
        {
            CoherenceSmoothingSpeed = 0f,
        });

        tracker.SetTransportConnected(true);
        FeedResonantSignal(tracker, totalSeconds: 96d, centerIbiMs: 1000f, amplitudeMs: 110f, resonanceHz: 0.10d);

        PolarCoherenceTelemetry telemetry = tracker.GetTelemetry();

        Assert.True(telemetry.HasCoherenceSample);
        Assert.True(telemetry.HasTracking);
        Assert.InRange(telemetry.CurrentCoherence01, 0.25f, 1f);
        Assert.InRange(telemetry.PeakFrequencyHz, 0.08f, 0.12f);
        Assert.True(telemetry.WindowCoverage01 >= 0.99f);
        Assert.True(telemetry.TotalBandPower > telemetry.PeakBandPower);
        Assert.Equal(
            Math.Clamp(telemetry.PeakBandPower / telemetry.TotalBandPower, 0f, 1f),
            telemetry.NormalizedCoherence01,
            3);

        double expectedPaperRatio = Math.Pow(
            telemetry.PeakBandPower / (telemetry.TotalBandPower - telemetry.PeakBandPower),
            2.0);
        Assert.InRange(Math.Abs(telemetry.PaperCoherenceRatio - (float)expectedPaperRatio), 0f, 0.1f);
    }

    [Fact]
    public void Tracker_BecomesStale_WhenSamplesStop()
    {
        var tracker = new PolarCoherenceTracker(new PolarCoherenceSettings
        {
            MinimumIbiSamples = 8,
            CoherenceWindowSeconds = 16f,
            CoherenceSmoothingSpeed = 0f,
            StaleTimeoutSeconds = 0.1f,
        });

        tracker.SetTransportConnected(true);
        FeedResonantSignal(tracker, totalSeconds: 28d, centerIbiMs: 960f, amplitudeMs: 70f, resonanceHz: 0.11d);

        Thread.Sleep(150);
        PolarCoherenceTelemetry telemetry = tracker.GetTelemetry();

        Assert.Equal(PolarCoherenceTrackingState.Stale, telemetry.TrackingState);
        Assert.False(telemetry.HasTracking);
        Assert.Equal(0f, telemetry.Confidence01, 3);
    }

    [Fact]
    public void ApplySettings_ResetTracker_ClearsRuntimeStateAndKeepsConnection()
    {
        var tracker = new PolarCoherenceTracker();
        tracker.SetTransportConnected(true);
        FeedResonantSignal(tracker, totalSeconds: 24d, centerIbiMs: 1000f, amplitudeMs: 80f, resonanceHz: 0.10d);

        tracker.ApplySettings(PolarCoherenceSettings.CreateDefault() with
        {
            MinimumIbiSamples = 12,
            CoherenceWindowSeconds = 32f,
        }, resetTracker: true);

        PolarCoherenceTelemetry telemetry = tracker.GetTelemetry();

        Assert.True(telemetry.IsTransportConnected);
        Assert.False(telemetry.HasReceivedAnyRrSample);
        Assert.False(telemetry.HasCoherenceSample);
        Assert.Equal(12, telemetry.Settings.MinimumIbiSamples);
        Assert.Equal(32f, telemetry.Settings.CoherenceWindowSeconds, 3);
    }

    private static void FeedResonantSignal(
        PolarCoherenceTracker tracker,
        double totalSeconds,
        float centerIbiMs,
        float amplitudeMs,
        double resonanceHz)
    {
        double elapsed = 0d;
        while (elapsed < totalSeconds)
        {
            double phase = elapsed * resonanceHz * Math.PI * 2.0;
            float ibiMs = centerIbiMs + (float)(Math.Sin(phase) * amplitudeMs);
            tracker.SubmitRrInterval(ibiMs);
            elapsed += ibiMs / 1000.0;
        }
    }
}
