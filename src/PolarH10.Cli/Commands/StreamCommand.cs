using System.CommandLine;
using PolarH10.Protocol;
using PolarH10.Transport.Windows;

namespace PolarH10.Cli.Commands;

internal static class StreamCommand
{
    public static Command Create()
    {
        var deviceOption = new Option<string>(
            "--device",
            "Bluetooth address of the Polar H10") { IsRequired = true };

        var jsonOption = new Option<bool>(
            "--json",
            () => false,
            "Output as JSON lines instead of plain text");

        var rawHexOption = new Option<bool>(
            "--raw-hex",
            () => false,
            "Include raw hex payload in output");

        var ecgCmd = new Command("ecg", "Stream ECG data to stdout")
        {
            deviceOption, jsonOption, rawHexOption,
        };

        ecgCmd.SetHandler(async (string device, bool json, bool rawHex) =>
        {
            await StreamMeasurement(device, "ecg", json, rawHex);
        }, deviceOption, jsonOption, rawHexOption);

        var accCmd = new Command("acc", "Stream ACC data to stdout")
        {
            deviceOption, jsonOption, rawHexOption,
        };

        accCmd.SetHandler(async (string device, bool json, bool rawHex) =>
        {
            await StreamMeasurement(device, "acc", json, rawHex);
        }, deviceOption, jsonOption, rawHexOption);

        var cmd = new Command("stream", "Stream a single measurement channel to stdout")
        {
            ecgCmd,
            accCmd,
        };

        return cmd;
    }

    private static async Task StreamMeasurement(string device, string channel, bool json, bool rawHex)
    {
        var factory = new WindowsBleAdapterFactory();
        var session = new PolarH10Session(factory);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await session.ConnectAsync(device, cts.Token);

        if (channel == "ecg")
        {
            session.EcgFrameReceived += f =>
            {
                if (json)
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        ts_ns = f.SensorTimestampNs,
                        samples = f.MicroVolts,
                    }));
                else
                    Console.WriteLine($"{f.SensorTimestampNs}\t{string.Join('\t', f.MicroVolts)}");
            };
            await session.StartEcgAsync(ct: cts.Token);
        }
        else
        {
            session.AccFrameReceived += f =>
            {
                foreach (var s in f.Samples)
                {
                    if (json)
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            ts_ns = f.SensorTimestampNs,
                            x = s.X, y = s.Y, z = s.Z,
                        }));
                    else
                        Console.WriteLine($"{f.SensorTimestampNs}\t{s.X}\t{s.Y}\t{s.Z}");
                }
            };
            await session.StartAccAsync(ct: cts.Token);
        }

        try { await Task.Delay(Timeout.Infinite, cts.Token); }
        catch (OperationCanceledException) { }

        await session.DisposeAsync();
    }
}
