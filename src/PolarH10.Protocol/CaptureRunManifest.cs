using System.Text.Json;

namespace PolarH10.Protocol;

/// <summary>
/// Describes a multi-device capture run: a parent recording session that
/// may contain one or more per-device subfolders.
/// Serialized as <c>run.json</c> in the parent recording folder.
/// </summary>
public sealed class CaptureRunManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public string? StartedAtUtc { get; set; }
    public string? StoppedAtUtc { get; set; }
    public List<DeviceSessionEntry> DeviceSessions { get; set; } = [];

    public sealed class DeviceSessionEntry
    {
        public string? DeviceAddress { get; set; }
        public string? DeviceAlias { get; set; }
        public string? SubFolder { get; set; }
        public string? SessionId { get; set; }
        public int HrRrSampleCount { get; set; }
        public int EcgFrameCount { get; set; }
        public int AccFrameCount { get; set; }
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public static async Task SaveAsync(string path, CaptureRunManifest manifest, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(manifest, s_jsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public static async Task<CaptureRunManifest?> LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<CaptureRunManifest>(json);
    }
}
