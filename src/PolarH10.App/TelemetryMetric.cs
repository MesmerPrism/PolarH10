namespace PolarH10.App;

internal enum TelemetryMetric
{
    HeartRate = 0,
    RrInterval = 1,
    Ecg = 2,
    Breathing = 3,
    Coherence = 4,
    CoherenceConfidence = 5,
    AccX = 6,
    AccY = 7,
    AccZ = 8,
    BreathIntervalEntropy = 9,
    BreathAmplitudeEntropy = 10,
}

internal readonly record struct TelemetryMetricOption(TelemetryMetric Metric, string Label);
