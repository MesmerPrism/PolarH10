using System.CommandLine;
using PolarH10.Protocol;
using PolarH10.Transport.Windows;

namespace PolarH10.Cli.Commands;

internal static class MonitorCommand
{
    public static Command Create()
    {
        var deviceOption = new Option<string>(
            "--device",
            "Bluetooth address of the Polar H10") { IsRequired = true };

        var channelsOption = new Option<string>(
            "--channels",
            () => "hr,rr,ecg,acc",
            "Comma-separated channels: hr,rr,ecg,acc");

        var verboseOption = new Option<bool>(
            "--verbose",
            () => false,
            "Show detailed protocol messages");

        var cmd = new Command("monitor", "Live terminal dashboard for a connected Polar H10")
        {
            deviceOption,
            channelsOption,
            verboseOption,
        };

        cmd.SetHandler(async (string device, string channels, bool verbose) =>
        {
            var factory = new WindowsBleAdapterFactory();
            var session = new PolarH10Session(factory);

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            Console.WriteLine($"Connecting to {device}...");
            await session.ConnectAsync(device, cts.Token);
            Console.WriteLine("Connected. Press Ctrl+C to stop.\n");

            var ch = channels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool wantHr = ch.Any(c => c.Equals("hr", StringComparison.OrdinalIgnoreCase));
            bool wantEcg = ch.Any(c => c.Equals("ecg", StringComparison.OrdinalIgnoreCase));
            bool wantAcc = ch.Any(c => c.Equals("acc", StringComparison.OrdinalIgnoreCase));

            if (wantHr)
                session.HrRrReceived += s => Console.WriteLine($"HR: {s.HeartRateBpm} bpm  RR: [{string.Join(", ", s.RrIntervalsMs.Select(r => $"{r:F1}"))}]");

            if (wantEcg)
            {
                session.EcgFrameReceived += f => Console.WriteLine($"ECG: {f.MicroVolts.Length} samples  ts={f.SensorTimestampNs}");
                await session.StartEcgAsync(ct: cts.Token);
            }

            if (wantAcc)
            {
                session.AccFrameReceived += f => Console.WriteLine($"ACC: {f.Samples.Length} samples  ts={f.SensorTimestampNs}");
                await session.StartAccAsync(ct: cts.Token);
            }

            if (verbose)
                session.PmdCtrlResponse += r => Console.WriteLine($"CTRL: op=0x{r.OpCode:X2} meas=0x{r.MeasurementType:X2} err=0x{r.ErrorCode:X2}");

            try { await Task.Delay(Timeout.Infinite, cts.Token); }
            catch (OperationCanceledException) { }

            Console.WriteLine("\nDisconnecting...");
            await session.DisposeAsync();
        }, deviceOption, channelsOption, verboseOption);

        return cmd;
    }
}
