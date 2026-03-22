namespace PolarH10.Protocol;

public readonly record struct PolarRrSamplePoint(
    double TimeSeconds,
    float IbiMs);

public readonly record struct PolarSeriesPoint(
    double X,
    double Y);

public readonly record struct PolarSpectrumPoint(
    double FrequencyHz,
    double Power);

public readonly record struct PolarCoherenceDiagnostics(
    IReadOnlyList<PolarRrSamplePoint> AcceptedRrSamples,
    IReadOnlyList<PolarSeriesPoint> ResampledTachogram,
    IReadOnlyList<PolarSpectrumPoint> PowerSpectrum,
    float PeakFrequencyHz,
    float PeakWindowLowerHz,
    float PeakWindowUpperHz,
    float PeakBandPower,
    float TotalBandLowerHz,
    float TotalBandUpperHz,
    float TotalBandPower,
    float PaperCoherenceRatio,
    float NormalizedCoherence01);
