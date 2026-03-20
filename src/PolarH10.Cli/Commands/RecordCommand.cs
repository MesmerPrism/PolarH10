using System.CommandLine;
using PolarH10.Cli;
using PolarH10.Protocol;
using PolarH10.Transport.Windows;

namespace PolarH10.Cli.Commands;

internal static class RecordCommand
{
    public static Command Create()
    {
        var deviceOption = new Option<string>(
            "--device",
            "Bluetooth address of the Polar H10") { IsRequired = true };

        var outOption = new Option<string>(
            "--out",
            () => "./session",
            "Output folder for recorded session files");

        var formatOption = new Option<string>(
            "--format",
            () => "csv",
            "Output format: csv");

        var durationOption = new Option<int?>(
            "--duration",
            "Recording duration in seconds (omit for indefinite)");

        var transportOption = CliTransportOptions.CreateTransportOption();
        var syntheticPipeOption = CliTransportOptions.CreateSyntheticPipeOption();

        var cmd = new Command("record", "Record a session to disk: session.json, hr_rr.csv, ecg.csv, acc.csv, protocol.jsonl")
        {
            deviceOption,
            outOption,
            formatOption,
            durationOption,
            transportOption,
            syntheticPipeOption,
        };

        cmd.SetHandler(async (string device, string outDir, string format, int? duration, string transport, string syntheticPipe) =>
        {
            var factory = CliTransportOptions.CreateFactory(transport, syntheticPipe);
            var session = new PolarH10Session(factory);

            var registry = new PolarDeviceRegistry(PolarDeviceRegistry.DefaultFilePath);
            registry.Load();

            var recorder = new PolarSessionRecorder { DeviceAddress = device };

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            try
            {
                Console.WriteLine($"Connecting to {device}...");
                await session.ConnectAsync(device, cts.Token);

                var identity = registry.RecordConnected(device);
                recorder.DeviceName = identity.AdvertisedName ?? device;
                recorder.DeviceAlias = identity.UserAlias;
                registry.Save();

                Console.WriteLine("Connected. Recording...");

                session.HrRrReceived += recorder.RecordHrRr;
                session.EcgFrameReceived += recorder.RecordEcg;
                session.AccFrameReceived += recorder.RecordAcc;

                if (session.IsPmdReady)
                {
                    await session.RequestSettingsAsync(PolarGattIds.MeasurementTypeEcg, cts.Token);
                    await Task.Delay(1500, cts.Token);
                    await session.StartEcgAsync(ct: cts.Token);
                    if (!session.HasSyntheticBreathingTelemetry)
                    {
                        await Task.Delay(2000, cts.Token);
                        await session.StartAccAsync(ct: cts.Token);
                    }
                    else
                    {
                        Console.WriteLine("Synthetic breathing telemetry is active; recording HR/RR + ECG without PMD ACC.");
                    }
                }
                else
                {
                    Console.WriteLine("PMD service not available; recording HR only.");
                }

                try
                {
                    if (duration.HasValue)
                        await Task.Delay(TimeSpan.FromSeconds(duration.Value), cts.Token);
                    else
                        await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException)
                {
                }

                var folderName = recorder.GenerateFolderName();
                var outputPath = Path.Combine(outDir, folderName);

                Console.WriteLine($"\nSaving to {outputPath}...");
                await recorder.SaveAsync(outputPath, CancellationToken.None);
                Console.WriteLine($"Done. HR/RR={recorder.HrRrCount} ECG={recorder.EcgFrameCount} ACC={recorder.AccFrameCount}");
            }
            finally
            {
                await session.DisposeAsync();
            }
        }, deviceOption, outOption, formatOption, durationOption, transportOption, syntheticPipeOption);

        return cmd;
    }
}
