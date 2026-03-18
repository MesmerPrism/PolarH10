using System.Text.Json;

namespace PolarH10.Protocol;

/// <summary>
/// Records a live Polar H10 session into in-memory buffers.
/// Call <see cref="SaveAsync"/> to persist to disk.
/// </summary>
public sealed class PolarSessionRecorder
{
    private readonly List<HrRrSample> _hrRrSamples = [];
    private readonly List<PolarEcgFrame> _ecgFrames = [];
    private readonly List<PolarAccFrame> _accFrames = [];
    private readonly List<PolarProtocolTranscriptEntry> _transcript = [];

    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public string? DeviceName { get; set; }
    public string? DeviceAddress { get; set; }
    public string? DeviceAlias { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public int HrRrCount => _hrRrSamples.Count;
    public int EcgFrameCount => _ecgFrames.Count;
    public int AccFrameCount => _accFrames.Count;
    public int TranscriptEntryCount => _transcript.Count;

    public void RecordHrRr(HrRrSample sample) => _hrRrSamples.Add(sample);
    public void RecordEcg(PolarEcgFrame frame) => _ecgFrames.Add(frame);
    public void RecordAcc(PolarAccFrame frame) => _accFrames.Add(frame);
    public void RecordTranscript(PolarProtocolTranscriptEntry entry) => _transcript.Add(entry);

    /// <summary>
    /// Generates a deterministic folder name for this session based on
    /// timestamp and device identity: <c>yyyyMMdd-HHmmssZ_alias-or-address</c>.
    /// </summary>
    public string GenerateFolderName()
    {
        var ts = StartedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss") + "Z";
        var tag = SanitizeFileName(DeviceAlias ?? DeviceAddress ?? "unknown");
        return $"{ts}_{tag}";
    }

    /// <summary>
    /// Save the session to the specified output folder.
    /// Creates: session.json, hr_rr.csv, ecg.csv, acc.csv, protocol.jsonl
    /// </summary>
    public async Task SaveAsync(string outputFolder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputFolder);

        var metadata = new SessionMetadata
        {
            SchemaVersion = 2,
            SessionId = SessionId,
            DeviceName = DeviceName,
            DeviceAddress = DeviceAddress,
            DeviceAlias = DeviceAlias,
            Notes = Notes,
            StartedAtUtc = StartedAt.UtcDateTime.ToString("O"),
            SavedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
            HrRrSampleCount = _hrRrSamples.Count,
            EcgFrameCount = _ecgFrames.Count,
            AccFrameCount = _accFrames.Count,
            TranscriptEntryCount = _transcript.Count,
        };

        var jsonPath = Path.Combine(outputFolder, "session.json");
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonPath, json, ct);

        await PolarCsvExporter.WriteHrRrCsvAsync(
            Path.Combine(outputFolder, "hr_rr.csv"), _hrRrSamples, DeviceAddress, DeviceAlias, ct);

        await PolarCsvExporter.WriteEcgCsvAsync(
            Path.Combine(outputFolder, "ecg.csv"), _ecgFrames, DeviceAddress, DeviceAlias, ct);

        await PolarCsvExporter.WriteAccCsvAsync(
            Path.Combine(outputFolder, "acc.csv"), _accFrames, DeviceAddress, DeviceAlias, ct);

        await PolarProtocolTranscript.WriteJsonlAsync(
            Path.Combine(outputFolder, "protocol.jsonl"), _transcript, ct);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ' ')
                chars[i] = '-';
        }
        return new string(chars);
    }

    internal sealed class SessionMetadata
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
