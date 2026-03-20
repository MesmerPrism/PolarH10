using System.CommandLine;
using PolarH10.Cli;

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

        var transportOption = CliTransportOptions.CreateTransportOption();
        var syntheticPipeOption = CliTransportOptions.CreateSyntheticPipeOption();

        var cmd = new Command("scan", "Discover nearby Polar H10 devices")
        {
            durationOption,
            jsonOption,
            transportOption,
            syntheticPipeOption,
        };

        cmd.SetHandler(async (int duration, bool json, string transport, string syntheticPipe) =>
        {
            var factory = CliTransportOptions.CreateFactory(transport, syntheticPipe);
            var scanner = factory.CreateScanner();
            var found = new List<(string Address, string Name, int Rssi)>();

            try
            {
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
            }
            finally
            {
                if (scanner is IDisposable disposable)
                    disposable.Dispose();
            }
        }, durationOption, jsonOption, transportOption, syntheticPipeOption);

        return cmd;
    }
}
