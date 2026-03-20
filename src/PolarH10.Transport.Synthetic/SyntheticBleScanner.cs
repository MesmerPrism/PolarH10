using System.IO.Pipes;
using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Synthetic;

public sealed class SyntheticBleScanner : IBleScanner
{
    private readonly SyntheticTransportOptions _options;

    public SyntheticBleScanner(SyntheticTransportOptions options)
    {
        _options = options.Normalize();
    }

    public event Action<BleDeviceFound>? DeviceFound;
    public event Action? ScanCompleted;

    public async Task StartScanAsync(TimeSpan duration, CancellationToken ct = default)
    {
        string pipeName = SyntheticPipeProtocol.GetDiscoveryPipeName(_options);
        await using var stream = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await stream.ConnectAsync((int)Math.Clamp(duration.TotalMilliseconds, 100, 30_000), ct);
        await SyntheticPipeProtocol.WriteMessageAsync(stream, new SyntheticPipeProtocol.ClientEnvelope
        {
            Type = "scan",
            DurationMs = (int)Math.Max(100, duration.TotalMilliseconds),
        }, ct);

        SyntheticPipeProtocol.ServerEnvelope? response =
            await SyntheticPipeProtocol.ReadMessageAsync<SyntheticPipeProtocol.ServerEnvelope>(stream, ct);

        if (response?.Devices is not null)
        {
            foreach (SyntheticPipeProtocol.DiscoveredDevice device in response.Devices)
                DeviceFound?.Invoke(new BleDeviceFound(device.Address, device.Name, device.Rssi));
        }

        ScanCompleted?.Invoke();
    }

    public void StopScan()
    {
    }
}
