namespace PolarH10.App;

internal enum TelemetryMetric
{
    HeartRate = 0,
    RrInterval = 1,
    Ecg = 2,
    Breathing = 3,
    Coherence = 4,
    CoherenceConfidence = 5,
    HrvRmssd = 6,
    AccX = 7,
    AccY = 8,
    AccZ = 9,
    BreathIntervalEntropy = 10,
    BreathAmplitudeEntropy = 11,
}

internal readonly record struct TelemetryMetricOption(TelemetryMetric Metric, string Label);
