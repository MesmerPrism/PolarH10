using System.Text.Json;

namespace PolarH10.Protocol;

/// <summary>
/// A single entry in the protocol transcript (for debugging BLE traffic).
/// </summary>
public sealed class PolarProtocolTranscriptEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Direction { get; init; } // "tx" or "rx"
    public required string Channel { get; init; } // "pmd_ctrl", "pmd_data", "hr", etc.
    public required string HexPayload { get; init; }
    public string? Note { get; init; }
    public string? DeviceAddress { get; init; }
    public string? DeviceAlias { get; init; }
}

/// <summary>
/// Writes and reads protocol transcript files (JSONL format, one entry per line).
/// </summary>
public static class PolarProtocolTranscript
{
    /// <summary>
    /// Create a transcript entry from raw bytes.
    /// </summary>
    public static PolarProtocolTranscriptEntry CreateEntry(
        string direction,
        string channel,
        byte[] payload,
        string? note = null)
    {
        return new PolarProtocolTranscriptEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Direction = direction,
            Channel = channel,
            HexPayload = Convert.ToHexString(payload),
            Note = note,
        };
    }

    /// <summary>
    /// Write transcript entries to a JSONL (newline-delimited JSON) file.
    /// </summary>
    public static async Task WriteJsonlAsync(
        string path,
        IReadOnlyList<PolarProtocolTranscriptEntry> entries,
        CancellationToken ct = default)
    {
        using var writer = new StreamWriter(path, append: false);
        foreach (var entry in entries)
        {
            var line = JsonSerializer.Serialize(entry);
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }
    }

    /// <summary>
    /// Read transcript entries from a JSONL file.
    /// </summary>
    public static async Task<List<PolarProtocolTranscriptEntry>> ReadJsonlAsync(
        string path,
        CancellationToken ct = default)
    {
        var entries = new List<PolarProtocolTranscriptEntry>();
        using var reader = new StreamReader(path);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<PolarProtocolTranscriptEntry>(line);
            if (entry != null) entries.Add(entry);
        }

        return entries;
    }
}
