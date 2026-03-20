namespace PolarH10.Protocol;

/// <summary>
/// Runtime controls for RR-derived short-term HRV telemetry.
/// The default window follows the conventional five-minute short-term standard
/// described by Shaffer and Ginsberg (2017); shorter windows are still allowed
/// for operator preview, but should not be treated as equivalent to that standard.
/// </summary>
public sealed record PolarHrvSettings
{
    /// <summary>
    /// Minimum accepted RR intervals required before the tracker will publish HRV metrics.
    /// </summary>
    public int MinimumRrSamples { get; init; } = 90;

    /// <summary>
    /// Rolling RR history span, in seconds, used for the short-term HRV window.
    /// </summary>
    public float WindowSeconds { get; init; } = 300f;

    /// <summary>
    /// Maximum age, in seconds, before the latest HRV solve is marked stale.
    /// </summary>
    public float StaleTimeoutSeconds { get; init; } = 4f;

    public static PolarHrvSettings CreateDefault() => new();

    public PolarHrvSettings Clamp()
    {
        return this with
        {
            MinimumRrSamples = Math.Clamp(MinimumRrSamples, 8, 4096),
            WindowSeconds = Math.Clamp(WindowSeconds, 30f, 600f),
            StaleTimeoutSeconds = Math.Clamp(StaleTimeoutSeconds, 0.1f, 120f),
        };
    }
}
