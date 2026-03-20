namespace PolarH10.Protocol;

/// <summary>
/// App-side runtime controls for RR-derived coherence tracking.
/// The spectral constants from McCraty et al., <c>The Coherent Heart</c> (2006)
/// remain fixed in the calculator; these settings only control windowing,
/// readiness, display smoothing, and freshness behavior in the tracker.
/// </summary>
public sealed record PolarCoherenceSettings
{
    /// <summary>
    /// Minimum accepted RR intervals required before the tracker attempts a solve.
    /// </summary>
    public int MinimumIbiSamples { get; init; } = 20;

    /// <summary>
    /// Rolling RR history span, in seconds, used for spline resampling and PSD analysis.
    /// </summary>
    public float CoherenceWindowSeconds { get; init; } = 64f;

    /// <summary>
    /// Exponential smoothing speed applied to the displayed normalized coherence score.
    /// </summary>
    public float CoherenceSmoothingSpeed { get; init; } = 4f;

    /// <summary>
    /// Maximum age, in seconds, before a solved coherence sample becomes stale.
    /// </summary>
    public float StaleTimeoutSeconds { get; init; } = 3f;

    public static PolarCoherenceSettings CreateDefault() => new();

    public PolarCoherenceSettings Clamp()
    {
        return this with
        {
            MinimumIbiSamples = Math.Clamp(MinimumIbiSamples, 4, 4096),
            CoherenceWindowSeconds = Math.Clamp(CoherenceWindowSeconds, 16f, 180f),
            CoherenceSmoothingSpeed = Math.Clamp(CoherenceSmoothingSpeed, 0f, 20f),
            StaleTimeoutSeconds = Math.Clamp(StaleTimeoutSeconds, 0.1f, 120f),
        };
    }
}
