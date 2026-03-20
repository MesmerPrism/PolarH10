using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Protocol.Tests;

public sealed class PolarBreathingTrackerTests
{
    [Fact]
    public void Tracker_CalibratesAndProducesTracking_OnSyntheticAccSignal()
    {
        var tracker = new PolarBreathingTracker(new PolarBreathingSettings
        {
            CalibrationDurationSeconds = 1.0f,
            MinCalibrationSamples = 80,
            UseAdaptiveBounds = false,
            BaseMode = PolarBreathingBaseMode.Xz,
        });

        tracker.SetTransportConnected(true);
        tracker.BeginCalibration();

        long sensorTimestampNs = 0;
        for (int frameIndex = 0; frameIndex < 40; frameIndex++)
        {
            tracker.SubmitAccFrame(new PolarAccFrame(sensorTimestampNs, 0, BuildSyntheticSamples(frameIndex * 8)));
            sensorTimestampNs += 80_000_000;
        }

        Thread.Sleep(1100);
        tracker.Advance();

        float minVolume = 1f;
        float maxVolume = 0f;
        bool sawDirectionalState = false;

        for (int frameIndex = 40; frameIndex < 90; frameIndex++)
        {
            tracker.SubmitAccFrame(new PolarAccFrame(sensorTimestampNs, 0, BuildSyntheticSamples(frameIndex * 8)));
            sensorTimestampNs += 80_000_000;

            var telemetry = tracker.GetTelemetry();
            if (!telemetry.IsCalibrated)
                continue;

            minVolume = Math.Min(minVolume, telemetry.CurrentVolume01);
            maxVolume = Math.Max(maxVolume, telemetry.CurrentVolume01);
            sawDirectionalState |= telemetry.CurrentState is PolarBreathingState.Inhaling or PolarBreathingState.Exhaling;
        }

        var finalTelemetry = tracker.GetTelemetry();

        Assert.True(finalTelemetry.IsCalibrated);
        Assert.True(finalTelemetry.HasTracking);
        Assert.True(maxVolume - minVolume > 0.25f);
        Assert.True(sawDirectionalState);
    }

    [Fact]
    public void ApplySettings_ResetTracker_ClearsCalibrationState()
    {
        var tracker = new PolarBreathingTracker();
        tracker.SetTransportConnected(true);
        tracker.BeginCalibration();
        tracker.ApplySettings(PolarBreathingSettings.CreateDefault() with { InvertVolume = true }, resetTracker: true);

        var telemetry = tracker.GetTelemetry();

        Assert.False(telemetry.IsCalibrated);
        Assert.False(telemetry.IsCalibrating);
        Assert.Equal(0.5f, telemetry.CurrentVolume01, 3);
        Assert.True(telemetry.Settings.InvertVolume);
    }

    private static AccSampleMg[] BuildSyntheticSamples(int startIndex)
    {
        var samples = new AccSampleMg[8];
        for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
        {
            int index = startIndex + sampleIndex;
            double t = index / 100.0;
            double phase = t * (Math.PI * 2.0 / 5.6);
            short x = (short)Math.Round((Math.Sin(phase) * 55) + (Math.Cos(phase * 2.1) * 10));
            short y = (short)Math.Round((Math.Cos(phase * 0.7) * 14) + (Math.Sin(phase * 1.9) * 4));
            short z = (short)Math.Round((Math.Sin(phase + 0.4) * 72) + (Math.Cos(phase * 2.2) * 11));
            samples[sampleIndex] = new AccSampleMg(x, y, z);
        }

        return samples;
    }
}
