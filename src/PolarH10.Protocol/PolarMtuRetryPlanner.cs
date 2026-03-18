namespace PolarH10.Protocol;

/// <summary>
/// Builds deterministic MTU retry candidates for Polar H10 connections.
/// Keeps the desired MTU first, removes duplicates, and filters invalid values.
/// </summary>
public static class PolarMtuRetryPlanner
{
    /// <summary>Minimum valid BLE MTU size.</summary>
    public const int MinimumMtu = 23;

    public static int[] BuildOrderedCandidates(int desiredMtu, int[]? retryCandidates)
    {
        var ordered = new List<int>();
        var seen = new HashSet<int>();

        if (desiredMtu > MinimumMtu && seen.Add(desiredMtu))
            ordered.Add(desiredMtu);

        if (retryCandidates is null || retryCandidates.Length == 0)
            return ordered.ToArray();

        for (int i = 0; i < retryCandidates.Length; i++)
        {
            int candidate = retryCandidates[i];
            if (candidate <= MinimumMtu) continue;
            if (!seen.Add(candidate)) continue;
            ordered.Add(candidate);
        }

        return ordered.ToArray();
    }
}
