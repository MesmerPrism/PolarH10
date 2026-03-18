using System.CommandLine;
using PolarH10.Protocol;
using PolarH10.Transport.Windows;

namespace PolarH10.Cli.Commands;

internal static class DoctorCommand
{
    public static Command Create()
    {
        var deviceOption = new Option<string>(
            "--device",
            "Bluetooth address of the Polar H10") { IsRequired = true };

        var cmd = new Command("doctor", "Verify connectivity: connect, discover services, enable notifications, request settings, decode a test frame")
        {
            deviceOption,
        };

        cmd.SetHandler(async (string device) =>
        {
            var factory = new WindowsBleAdapterFactory();
            var session = new PolarH10Session(factory);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            Console.WriteLine($"[doctor] Connecting to {device}...");
            try
            {
                await session.ConnectAsync(device, cts.Token);
                Console.WriteLine("[doctor] OK  Connected");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[doctor] FAIL  Connection: {ex.Message}");
                return;
            }

            Console.WriteLine($"[doctor] OK  PMD ready: {session.IsPmdReady}");

            bool gotHr = false;
            session.HrRrReceived += _ => gotHr = true;

            bool gotCtrl = false;
            session.PmdCtrlResponse += _ => gotCtrl = true;

            bool gotEcg = false;
            session.EcgFrameReceived += _ => gotEcg = true;

            bool gotAcc = false;
            session.AccFrameReceived += _ => gotAcc = true;

            // Request settings
            Console.WriteLine("[doctor] Requesting ECG settings...");
            try
            {
                await session.RequestSettingsAsync(PolarGattIds.MeasurementTypeEcg, cts.Token);
                await Task.Delay(1500, cts.Token);
                Console.WriteLine($"[doctor] {(gotCtrl ? "OK" : "WARN")}  PMD ctrl response received: {gotCtrl}");
            }
            catch (Exception ex) { Console.WriteLine($"[doctor] FAIL  Settings request: {ex.Message}"); }

            // Start ECG briefly
            Console.WriteLine("[doctor] Starting ECG stream...");
            try
            {
                await session.StartEcgAsync(ct: cts.Token);
                await Task.Delay(2000, cts.Token);
                Console.WriteLine($"[doctor] {(gotEcg ? "OK" : "WARN")}  ECG frames received: {gotEcg}");
            }
            catch (Exception ex) { Console.WriteLine($"[doctor] FAIL  ECG: {ex.Message}"); }

            // Start ACC briefly
            Console.WriteLine("[doctor] Starting ACC stream...");
            try
            {
                await session.StartAccAsync(ct: cts.Token);
                await Task.Delay(2000, cts.Token);
                Console.WriteLine($"[doctor] {(gotAcc ? "OK" : "WARN")}  ACC frames received: {gotAcc}");
            }
            catch (Exception ex) { Console.WriteLine($"[doctor] FAIL  ACC: {ex.Message}"); }

            // Check HR
            await Task.Delay(2000, cts.Token);
            Console.WriteLine($"[doctor] {(gotHr ? "OK" : "WARN")}  HR notification received: {gotHr}");

            Console.WriteLine("[doctor] Disconnecting...");
            await session.DisposeAsync();
            Console.WriteLine("[doctor] Done.");
        }, deviceOption);

        return cmd;
    }
}
