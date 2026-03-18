using System.CommandLine;
using PolarH10.Protocol;

namespace PolarH10.Cli.Commands;

internal static class SessionsCommand
{
    public static Command Create()
    {
        var pathArg = new Argument<string>(
            "path",
            () => ".",
            "Root folder to scan for recorded sessions and capture runs");

        var cmd = new Command("sessions", "List saved sessions and capture runs found under a folder")
        {
            pathArg,
        };

        cmd.SetHandler(async (string rootPath) =>
        {
            if (!Directory.Exists(rootPath))
            {
                Console.Error.WriteLine($"Folder not found: {rootPath}");
                return;
            }

            var result = await SessionDiscovery.ScanAsync(rootPath);

            if (result.CaptureRuns.Count == 0 && result.StandaloneSessions.Count == 0)
            {
                Console.WriteLine("No sessions found.");
                return;
            }

            // Show capture runs first
            if (result.CaptureRuns.Count > 0)
            {
                Console.WriteLine($"=== Capture Runs ({result.CaptureRuns.Count}) ===");
                Console.WriteLine();

                foreach (var run in result.CaptureRuns)
                {
                    Console.WriteLine($"  Run: {Path.GetFileName(run.FolderPath)}");
                    Console.WriteLine($"    Path     : {run.FolderPath}");
                    Console.WriteLine($"    Run ID   : {run.Manifest.RunId}");
                    if (run.Manifest.StartedAtUtc is not null)
                        Console.WriteLine($"    Started  : {run.Manifest.StartedAtUtc}");
                    if (run.Manifest.StoppedAtUtc is not null)
                        Console.WriteLine($"    Stopped  : {run.Manifest.StoppedAtUtc}");
                    Console.WriteLine($"    Devices  : {run.Manifest.DeviceSessions.Count}");

                    foreach (var ds in run.DeviceSessions)
                    {
                        var label = ds.DeviceAlias ?? ds.DeviceName ?? ds.DeviceAddress ?? "(unknown)";
                        Console.WriteLine($"      [{label}] HR={ds.HrRrSampleCount} ECG={ds.EcgFrameCount} ACC={ds.AccFrameCount}  ({Path.GetFileName(ds.FolderPath)})");
                    }
                    Console.WriteLine();
                }
            }

            // Show standalone sessions
            if (result.StandaloneSessions.Count > 0)
            {
                Console.WriteLine($"=== Standalone Sessions ({result.StandaloneSessions.Count}) ===");
                Console.WriteLine();

                foreach (var s in result.StandaloneSessions)
                {
                    Console.WriteLine($"  {s.DisplayLabel}");
                    Console.WriteLine($"    Path     : {s.FolderPath}");
                    if (s.SessionId is not null)
                        Console.WriteLine($"    Session  : {s.SessionId}");
                    if (s.DeviceAddress is not null)
                        Console.WriteLine($"    Address  : {s.DeviceAddress}");
                    if (s.DeviceAlias is not null)
                        Console.WriteLine($"    Alias    : {s.DeviceAlias}");
                    if (s.StartedAtUtc is not null)
                        Console.WriteLine($"    Started  : {s.StartedAtUtc}");
                    if (s.SavedAtUtc is not null)
                        Console.WriteLine($"    Saved    : {s.SavedAtUtc}");
                    Console.WriteLine($"    Data     : HR={s.HrRrSampleCount} ECG={s.EcgFrameCount} ACC={s.AccFrameCount}  schema=v{s.SchemaVersion}");
                    Console.WriteLine();
                }
            }
        }, pathArg);

        return cmd;
    }
}
