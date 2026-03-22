namespace PolarH10.Protocol;

public readonly record struct PolarHrvDeltaPoint(
    double TimeSeconds,
    float DeltaMs);

public readonly record struct PolarHrvDiagnostics(
    IReadOnlyList<PolarRrSamplePoint> AcceptedRrSamples,
    IReadOnlyList<PolarHrvDeltaPoint> AdjacentRrDeltas);
