using System.CommandLine;
using PolarH10.Transport.Windows;

namespace PolarH10.Cli.Commands;

internal static class ScanCommand
{
    public static Command Create()
    {
        var durationOption = new Option<int>(
            "--duration",
            () => 10,
            "Scan duration in seconds");

        var jsonOption = new Option<bool>(
            "--json",
            () => false,
            "Output results as JSON");

        var cmd = new Command("scan", "Discover nearby Polar H10 devices")
        {
            durationOption,
            jsonOption,
        };

        cmd.SetHandler(async (int duration, bool json) =>
        {
            var factory = new WindowsBleAdapterFactory();
            using var scanner = (WindowsBleScanner)factory.CreateScanner();
            var found = new List<(string Address, string Name, int Rssi)>();

            scanner.DeviceFound += device =>
            {
                if (string.IsNullOrEmpty(device.Name) ||
                    device.Name.IndexOf("Polar", StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                found.Add((device.Address, device.Name, device.Rssi));

                if (!json)
                    Console.WriteLine($"  {device.Name,-30} {device.Address}  RSSI={device.Rssi} dBm");
            };

            if (!json)
                Console.WriteLine($"Scanning for {duration}s...\n");

            await scanner.StartScanAsync(TimeSpan.FromSeconds(duration));
            await Task.Delay(TimeSpan.FromSeconds(duration));

            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                    found.Select(d => new { d.Address, d.Name, d.Rssi }),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"\n{found.Count} Polar device(s) found.");
            }
        }, durationOption, jsonOption);

        return cmd;
    }
}
