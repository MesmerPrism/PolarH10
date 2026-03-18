using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolarH10.Protocol;

namespace PolarH10.Cli.Commands;

internal static class ReplayCommand
{
    public static Command Create()
    {
        var pathArg = new Argument<string>(
            "session-path",
            "Path to a recorded session folder or capture run folder");

        var cmd = new Command("replay", "Replay a previously recorded session without hardware")
        {
            pathArg,
        };

        cmd.SetHandler(async (string sessionPath) =>
        {
            if (!Directory.Exists(sessionPath))
            {
                Console.Error.WriteLine($"Session folder not found: {sessionPath}");
                return;
            }

            // Check if this is a capture run (has run.json)
            var runJsonPath = Path.Combine(sessionPath, "run.json");
            if (File.Exists(runJsonPath))
            {
                await ReplayCaptureRun(sessionPath, runJsonPath);
                return;
            }

            // Otherwise replay as a single session
            await ReplaySingleSession(sessionPath);
        }, pathArg);

        return cmd;
    }

    private static async Task ReplayCaptureRun(string runFolder, string runJsonPath)
    {
        var manifest = await CaptureRunManifest.LoadAsync(runJsonPath);
        if (manifest is null)
        {
            Console.Error.WriteLine("Failed to parse run.json");
            return;
        }

        Console.WriteLine($"=== Capture Run: {Path.GetFileName(runFolder)} ===");
        Console.WriteLine($"  Run ID  : {manifest.RunId}");
        if (manifest.StartedAtUtc is not null) Console.WriteLine($"  Started : {manifest.StartedAtUtc}");
        if (manifest.StoppedAtUtc is not null) Console.WriteLine($"  Stopped : {manifest.StoppedAtUtc}");
        Console.WriteLine($"  Devices : {manifest.DeviceSessions.Count}");
        Console.WriteLine();

        foreach (var entry in manifest.DeviceSessions)
        {
            var label = entry.DeviceAlias ?? entry.DeviceAddress ?? "(unknown)";
            Console.WriteLine($"--- Device: {label} ---");
            if (entry.SubFolder is null)
            {
                Console.WriteLine("  (no subfolder recorded)");
                Console.WriteLine();
                continue;
            }

            var childFolder = Path.Combine(runFolder, entry.SubFolder);
            if (!Directory.Exists(childFolder))
            {
                Console.WriteLine($"  Subfolder missing: {entry.SubFolder}");
                Console.WriteLine();
                continue;
            }

            await ReplaySingleSession(childFolder);
        }
    }

    private static async Task ReplaySingleSession(string sessionPath)
    {
        var metadataPath = Path.Combine(sessionPath, "session.json");
        if (File.Exists(metadataPath))
        {
            var json = await File.ReadAllTextAsync(metadataPath);
            Console.WriteLine("Session metadata:");

            try
            {
                var meta = JsonSerializer.Deserialize<ReplayMetadata>(json);
                if (meta is not null)
                {
                    if (meta.SessionId is not null) Console.WriteLine($"  Session ID : {meta.SessionId}");
                    if (meta.DeviceName is not null) Console.WriteLine($"  Device     : {meta.DeviceName}");
                    if (meta.DeviceAddress is not null) Console.WriteLine($"  Address    : {meta.DeviceAddress}");
                    if (meta.DeviceAlias is not null) Console.WriteLine($"  Alias      : {meta.DeviceAlias}");
                    if (meta.StartedAtUtc is not null) Console.WriteLine($"  Started    : {meta.StartedAtUtc}");
                    if (meta.SavedAtUtc is not null) Console.WriteLine($"  Saved      : {meta.SavedAtUtc}");
                    if (meta.Notes is not null) Console.WriteLine($"  Notes      : {meta.Notes}");
                    Console.WriteLine($"  HR/RR={meta.HrRrSampleCount}  ECG={meta.EcgFrameCount}  ACC={meta.AccFrameCount}  Transcript={meta.TranscriptEntryCount}");
                }
                else
                {
                    Console.WriteLine(json);
                }
            }
            catch (JsonException)
            {
                // Legacy or corrupt — show raw JSON
                Console.WriteLine(json);
            }
            Console.WriteLine();
        }

        await ReplayCsvPreview(sessionPath, "hr_rr.csv", "HR/RR");
        await ReplayCsvPreview(sessionPath, "ecg.csv", "ECG");
        await ReplayCsvPreview(sessionPath, "acc.csv", "ACC");

        var transcriptPath = Path.Combine(sessionPath, "protocol.jsonl");
        if (File.Exists(transcriptPath))
        {
            var entries = await PolarProtocolTranscript.ReadJsonlAsync(transcriptPath);
            Console.WriteLine($"Protocol transcript: {entries.Count} entries");
            foreach (var e in entries.Take(10))
                Console.WriteLine($"  [{e.Timestamp:HH:mm:ss.fff}] {e.Direction} {e.Channel}: {e.HexPayload}");
        }
    }

    private static async Task ReplayCsvPreview(string sessionPath, string fileName, string label)
    {
        var path = Path.Combine(sessionPath, fileName);
        if (!File.Exists(path)) return;

        var lines = await File.ReadAllLinesAsync(path);
        Console.WriteLine($"{label}: {lines.Length - 1} rows");
        foreach (var line in lines.Take(6))
            Console.WriteLine($"  {line}");
        if (lines.Length > 6) Console.WriteLine($"  ... ({lines.Length - 6} more)");
        Console.WriteLine();
    }

    /// <summary>Backward-compatible metadata model for reading both v1 and v2 session.json files.</summary>
    private sealed class ReplayMetadata
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
