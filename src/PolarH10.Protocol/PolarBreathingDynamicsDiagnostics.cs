namespace PolarH10.Protocol;

public readonly record struct PolarBreathingWaveformPoint(
    double TimeSeconds,
    float Value01);

public readonly record struct PolarBreathingExtremumPoint(
    string Kind,
    double TimeSeconds,
    float Value01);

public readonly record struct PolarBreathingDerivedPoint(
    int Index,
    double TimeSeconds,
    float Value);

public readonly record struct PolarBreathingDynamicsDiagnostics(
    IReadOnlyList<PolarBreathingWaveformPoint> WaveformSamples,
    IReadOnlyList<PolarBreathingExtremumPoint> AcceptedExtrema,
    IReadOnlyList<PolarBreathingDerivedPoint> IntervalSeries,
    IReadOnlyList<PolarBreathingDerivedPoint> AmplitudeSeries);
