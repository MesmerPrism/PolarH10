using System.IO;
using System.Text.Json;

namespace PolarH10.App;

internal sealed record PolarAppRuntimeStatus(
    int ProcessId,
    string? ProcessPath,
    string TransportName,
    string SyntheticPipeBaseName,
    DateTimeOffset StartedAtUtc);

internal static class PolarAppRuntimeStatusStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PolarH10",
            "runtime-status.json");

    public static void Write(AppTransportSettings transportSettings)
    {
        string path = DefaultPath;
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var status = new PolarAppRuntimeStatus(
            Environment.ProcessId,
            Environment.ProcessPath,
            transportSettings.TransportName,
            transportSettings.SyntheticPipeBaseName,
            DateTimeOffset.UtcNow);

        File.WriteAllText(path, JsonSerializer.Serialize(status, JsonOptions));
    }

    public static void ClearOwnedStatus()
    {
        string path = DefaultPath;
        if (!File.Exists(path))
            return;

        try
        {
            PolarAppRuntimeStatus? status = JsonSerializer.Deserialize<PolarAppRuntimeStatus>(
                File.ReadAllText(path),
                JsonOptions);

            if (status is not null && status.ProcessId != Environment.ProcessId)
                return;
        }
        catch
        {
            // If the status file is malformed, this process still owns the cleanup path.
        }

        File.Delete(path);
    }
}
