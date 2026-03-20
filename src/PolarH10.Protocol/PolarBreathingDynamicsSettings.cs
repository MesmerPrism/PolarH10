namespace PolarH10.Protocol;

public sealed record PolarBreathingDynamicsSettings
{
    public float TurningPointDeltaThreshold { get; init; } = 0.0025f;
    public float MinimumExtremumSpacingSeconds { get; init; } = 0.35f;
    public float MinimumCycleExcursion01 { get; init; } = 0.08f;
    public int RetainedBreathCount { get; init; } = 256;
    public int MinimumBreathsForBasicStats { get; init; } = 8;
    public int MinimumBreathsForEntropy { get; init; } = 24;
    public int FullConfidenceBreathCount { get; init; } = 200;
    public float StaleTimeoutSeconds { get; init; } = 4f;

    public int SampleEntropyDimension { get; init; } = 2;
    public int SampleEntropyDelay { get; init; } = 1;
    public float SampleEntropyToleranceSdFactor { get; init; } = 0.20f;

    public int MultiscaleEntropyDimension { get; init; } = 3;
    public int MultiscaleEntropyDelay { get; init; } = 1;
    public float MultiscaleEntropyToleranceSdFactor { get; init; } = 0.20f;
    public int MultiscaleEntropyMaxScale { get; init; } = 5;

    public static PolarBreathingDynamicsSettings CreateDefault() => new();

    public PolarBreathingDynamicsSettings Clamp()
    {
        return this with
        {
            TurningPointDeltaThreshold = Math.Clamp(TurningPointDeltaThreshold, 0.0001f, 0.25f),
            MinimumExtremumSpacingSeconds = Math.Clamp(MinimumExtremumSpacingSeconds, 0.05f, 30f),
            MinimumCycleExcursion01 = Math.Clamp(MinimumCycleExcursion01, 0.001f, 1f),
            RetainedBreathCount = Math.Clamp(RetainedBreathCount, 32, 4096),
            MinimumBreathsForBasicStats = Math.Clamp(MinimumBreathsForBasicStats, 4, 4096),
            MinimumBreathsForEntropy = Math.Clamp(MinimumBreathsForEntropy, 8, 4096),
            FullConfidenceBreathCount = Math.Clamp(FullConfidenceBreathCount, 16, 4096),
            StaleTimeoutSeconds = Math.Clamp(StaleTimeoutSeconds, 0.1f, 120f),
            SampleEntropyDimension = Math.Clamp(SampleEntropyDimension, 1, 8),
            SampleEntropyDelay = Math.Clamp(SampleEntropyDelay, 1, 16),
            SampleEntropyToleranceSdFactor = Math.Clamp(SampleEntropyToleranceSdFactor, 0.01f, 2f),
            MultiscaleEntropyDimension = Math.Clamp(MultiscaleEntropyDimension, 1, 8),
            MultiscaleEntropyDelay = Math.Clamp(MultiscaleEntropyDelay, 1, 16),
            MultiscaleEntropyToleranceSdFactor = Math.Clamp(MultiscaleEntropyToleranceSdFactor, 0.01f, 2f),
            MultiscaleEntropyMaxScale = Math.Clamp(MultiscaleEntropyMaxScale, 1, 32),
        };
    }
}
