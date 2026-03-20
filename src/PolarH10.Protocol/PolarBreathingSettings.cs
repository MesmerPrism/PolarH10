using System.Numerics;

namespace PolarH10.Protocol;

public enum PolarBreathingBaseMode
{
    ThreeD = 0,
    Xz = 1,
}

public sealed record PolarBreathingSettings
{
    public bool AutoCalibrateOnUsefulSignal { get; init; } = true;
    public float UsefulSignalWindowSeconds { get; init; } = 4f;
    public int MinUsefulSamples { get; init; } = 80;
    public float MinUsefulSampleRateHz { get; init; } = 20f;
    public float MinUsefulAxisRangeG { get; init; } = 0.002f;

    public float CalibrationDurationSeconds { get; init; } = 12f;
    public int MinCalibrationSamples { get; init; } = 240;
    public float MinCalibrationTravelG { get; init; } = 0.01f;
    public float CalibrationRetryDelaySeconds { get; init; } = 2f;

    public float SampleEmaAlpha { get; init; } = 0.10f;
    public float BoundsLowerQuantile { get; init; } = 0.05f;
    public float BoundsUpperQuantile { get; init; } = 0.95f;
    public float BoundsEdgeEase { get; init; } = 0.03f;
    public float ProjectionEmaAlpha { get; init; } = 0.10f;
    public bool InvertVolume { get; init; }
    public PolarBreathingBaseMode BaseMode { get; init; } = PolarBreathingBaseMode.Xz;

    public bool UseAdaptiveBounds { get; init; } = true;
    public float AdaptiveBoundsWindowSeconds { get; init; } = 20f;
    public float AdaptiveBoundsMinInitialRangeFactor { get; init; } = 0.75f;
    public float AdaptiveBoundsMaxInitialRangeFactor { get; init; } = 1.35f;
    public float AdaptiveBoundsMinWindowCoverage { get; init; } = 0.85f;
    public float AdaptiveBoundsUpdateIntervalSeconds { get; init; } = 0.5f;
    public float AdaptiveBoundsLerpSpeed { get; init; } = 0.35f;
    public float AdaptiveBoundsContractSpeedMultiplier { get; init; } = 0.45f;
    public int MinAdaptiveBoundsSamples { get; init; } = 640;

    public float StaleTimeoutSeconds { get; init; } = 3f;
    public float VolumeEventMinDelta { get; init; } = 0.001f;
    public float StateDeltaThreshold { get; init; } = 0.003f;

    public bool UseDirectionReference { get; init; }
    public bool AssumeInhaleMovesAlongDirectionReference { get; init; } = true;
    public Vector3 DirectionReference { get; init; } = Vector3.UnitZ;
    public float DirectionReferenceMinAbsDot { get; init; } = 0.10f;

    public static PolarBreathingSettings CreateDefault() => new();

    public PolarBreathingSettings Clamp()
    {
        return this with
        {
            UsefulSignalWindowSeconds = Math.Clamp(UsefulSignalWindowSeconds, 0.25f, 120f),
            MinUsefulSamples = Math.Clamp(MinUsefulSamples, 16, 100000),
            MinUsefulSampleRateHz = Math.Clamp(MinUsefulSampleRateHz, 1f, 1000f),
            MinUsefulAxisRangeG = Math.Clamp(MinUsefulAxisRangeG, 0.0005f, 1f),
            CalibrationDurationSeconds = Math.Clamp(CalibrationDurationSeconds, 1f, 120f),
            MinCalibrationSamples = Math.Clamp(MinCalibrationSamples, 60, 100000),
            MinCalibrationTravelG = Math.Clamp(MinCalibrationTravelG, 0.001f, 1f),
            CalibrationRetryDelaySeconds = Math.Clamp(CalibrationRetryDelaySeconds, 0.1f, 120f),
            SampleEmaAlpha = Math.Clamp(SampleEmaAlpha, 0.01f, 1f),
            BoundsLowerQuantile = Math.Clamp(BoundsLowerQuantile, 0f, 0.25f),
            BoundsUpperQuantile = Math.Clamp(BoundsUpperQuantile, 0.75f, 1f),
            BoundsEdgeEase = Math.Clamp(BoundsEdgeEase, 0f, 0.2f),
            ProjectionEmaAlpha = Math.Clamp(ProjectionEmaAlpha, 0.01f, 1f),
            AdaptiveBoundsWindowSeconds = Math.Clamp(AdaptiveBoundsWindowSeconds, 4f, 300f),
            AdaptiveBoundsMinInitialRangeFactor = Math.Clamp(AdaptiveBoundsMinInitialRangeFactor, 0.5f, 1f),
            AdaptiveBoundsMaxInitialRangeFactor = Math.Clamp(AdaptiveBoundsMaxInitialRangeFactor, 1f, 10f),
            AdaptiveBoundsMinWindowCoverage = Math.Clamp(AdaptiveBoundsMinWindowCoverage, 0.25f, 1f),
            AdaptiveBoundsUpdateIntervalSeconds = Math.Clamp(AdaptiveBoundsUpdateIntervalSeconds, 0.1f, 30f),
            AdaptiveBoundsLerpSpeed = Math.Clamp(AdaptiveBoundsLerpSpeed, 0.05f, 10f),
            AdaptiveBoundsContractSpeedMultiplier = Math.Clamp(AdaptiveBoundsContractSpeedMultiplier, 0.1f, 1f),
            MinAdaptiveBoundsSamples = Math.Clamp(MinAdaptiveBoundsSamples, 16, 200000),
            StaleTimeoutSeconds = Math.Clamp(StaleTimeoutSeconds, 0.1f, 120f),
            VolumeEventMinDelta = Math.Clamp(VolumeEventMinDelta, 0.0001f, 0.05f),
            StateDeltaThreshold = Math.Clamp(StateDeltaThreshold, 0.0001f, 0.25f),
            DirectionReferenceMinAbsDot = Math.Clamp(DirectionReferenceMinAbsDot, 0f, 1f),
        };
    }
}
