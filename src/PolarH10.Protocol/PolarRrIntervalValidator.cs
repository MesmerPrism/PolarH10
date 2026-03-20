namespace PolarH10.Protocol;

internal static class PolarRrIntervalValidator
{
    private const float LowestValidIbiMs = 400f;
    private const float HighestValidIbiMs = 1800f;

    public static bool IsValid(float ibiMs)
        => ibiMs >= LowestValidIbiMs && ibiMs <= HighestValidIbiMs;
}
