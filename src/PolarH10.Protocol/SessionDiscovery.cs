using System.Text.Json;

namespace PolarH10.Protocol;

/// <summary>
/// Discovers saved sessions and capture runs by scanning directories
/// for <c>session.json</c> and <c>run.json</c> files.
/// </summary>
public static class SessionDiscovery
{
    /// <summary>
    /// A discovered session on disk with its parsed metadata.
    /// </summary>
    public sealed class DiscoveredSession
    {
        public required string FolderPath { get; init; }
        public string? SessionId { get; init; }
        public int SchemaVersion { get; init; }
        public string? DeviceName { get; init; }
        public string? DeviceAddress { get; init; }
        public string? DeviceAlias { get; init; }
        public string? StartedAtUtc { get; init; }
        public string? SavedAtUtc { get; init; }
        public int HrRrSampleCount { get; init; }
        public int EcgFrameCount { get; init; }
        public int AccFrameCount { get; init; }
        public int TranscriptEntryCount { get; init; }

        /// <summary>
        /// Best display label: alias if set, else device name, else address, else folder name.
        /// </summary>
        public string DisplayLabel =>
            DeviceAlias ?? DeviceName ?? DeviceAddress ?? Path.GetFileName(FolderPath);
    }

    /// <summary>
    /// A discovered capture run (multi-device recording folder).
    /// </summary>
    public sealed class DiscoveredRun
    {
        public required string FolderPath { get; init; }
        public required CaptureRunManifest Manifest { get; init; }
        public List<DiscoveredSession> DeviceSessions { get; init; } = [];
    }

    /// <summary>
    /// Combined result: standalone sessions + capture runs.
    /// </summary>
    public sealed class DiscoveryResult
    {
        public List<DiscoveredSession> StandaloneSessions { get; } = [];
        public List<DiscoveredRun> CaptureRuns { get; } = [];
    }

    /// <summary>
    /// Scan a root folder recursively for sessions and capture runs.
    /// A directory with <c>run.json</c> is treated as a capture run parent.
    /// A directory with <c>session.json</c> (and no parent run.json) is a standalone session.
    /// </summary>
    public static async Task<DiscoveryResult> ScanAsync(string rootFolder, CancellationToken ct = default)
    {
        var result = new DiscoveryResult();
        if (!Directory.Exists(rootFolder)) return result;

        // Track which session folders belong to a run so we don't double-list them.
        var runOwnedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First pass: find capture runs (run.json).
        foreach (var runJsonPath in Directory.EnumerateFiles(rootFolder, "run.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var runFolder = Path.GetDirectoryName(runJsonPath)!;

            var manifest = await CaptureRunManifest.LoadAsync(runJsonPath, ct);
            if (manifest is null) continue;

            var run = new DiscoveredRun { FolderPath = runFolder, Manifest = manifest };

            // Discover child sessions from manifest entries.
            foreach (var entry in manifest.DeviceSessions)
            {
                if (entry.SubFolder is null) continue;
                var childFolder = Path.Combine(runFolder, entry.SubFolder);
                runOwnedFolders.Add(Path.GetFullPath(childFolder));

                var childSession = await TryLoadSession(childFolder, ct);
                if (childSession != null)
                    run.DeviceSessions.Add(childSession);
            }

            result.CaptureRuns.Add(run);
        }

        // Second pass: find standalone sessions (session.json not owned by a run).
        foreach (var sessionJsonPath in Directory.EnumerateFiles(rootFolder, "session.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var sessionFolder = Path.GetFullPath(Path.GetDirectoryName(sessionJsonPath)!);
            if (runOwnedFolders.Contains(sessionFolder)) continue;

            var session = await TryLoadSession(sessionFolder, ct);
            if (session != null)
                result.StandaloneSessions.Add(session);
        }

        return result;
    }

    /// <summary>
    /// Try to load a single session from a folder containing session.json.
    /// Returns null if the folder is invalid or the file is corrupt.
    /// Tolerates both v1 (legacy) and v2 schema.
    /// </summary>
    public static async Task<DiscoveredSession?> TryLoadSession(string folderPath, CancellationToken ct = default)
    {
        var metaPath = Path.Combine(folderPath, "session.json");
        if (!File.Exists(metaPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(metaPath, ct);
            var meta = JsonSerializer.Deserialize<SessionMetaDto>(json);
            if (meta is null) return null;

            return new DiscoveredSession
            {
                FolderPath = folderPath,
                SchemaVersion = meta.SchemaVersion,
                SessionId = meta.SessionId,
                DeviceName = meta.DeviceName,
                DeviceAddress = meta.DeviceAddress,
                DeviceAlias = meta.DeviceAlias,
                StartedAtUtc = meta.StartedAtUtc,
                SavedAtUtc = meta.SavedAtUtc,
                HrRrSampleCount = meta.HrRrSampleCount,
                EcgFrameCount = meta.EcgFrameCount,
                AccFrameCount = meta.AccFrameCount,
                TranscriptEntryCount = meta.TranscriptEntryCount,
            };
        }
        catch (JsonException)
        {
            return null; // Corrupt or unrecognized format — skip silently.
        }
    }

    /// <summary>
    /// Backward-compatible DTO for reading both v1 (no sessionId/alias) and v2 session.json.
    /// </summary>
    private sealed class SessionMetaDto
    {
        public int SchemaVersion { get; set; }
        public string? SessionId { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceAddress { get; set; }
        public string? DeviceAlias { get; set; }
        public string? Notes { get; set; }
        public string? StartedAtUtc { get; set; }
        public string? SavedAtUtc { get; set; }
        public int HrRrSampleCount { get; set; }
        public int EcgFrameCount { get; set; }
        public int AccFrameCount { get; set; }
        public int TranscriptEntryCount { get; set; }
    }
}
