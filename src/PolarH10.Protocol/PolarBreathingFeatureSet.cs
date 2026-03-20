namespace PolarH10.Protocol;

public readonly record struct PolarBreathingFeatureSet(
    float Mean,
    float StandardDeviation,
    float CoefficientOfVariation,
    float AutocorrelationWindow50,
    float PsdSlope,
    float LempelZivComplexity,
    float SampleEntropy,
    float MultiscaleEntropy)
{
    public static PolarBreathingFeatureSet Empty { get; } = new();
}
