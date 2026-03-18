using PolarH10.Protocol;
using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Windows;

/// <summary>
/// Per-device runtime context that bundles a <see cref="PolarH10Session"/>,
/// an optional <see cref="PolarSessionRecorder"/>, status, and counters.
/// Owned by a <see cref="PolarMultiDeviceCoordinator"/>.
/// </summary>
public sealed class DeviceContext : IAsyncDisposable
{
    public string BluetoothAddress { get; }
    public PolarDeviceIdentity Identity { get; }
    public PolarH10Session Session { get; }
    public PolarSessionRecorder? Recorder { get; set; }

    public DeviceConnectionStatus Status { get; internal set; } = DeviceConnectionStatus.Disconnected;
    public int HrCount { get; internal set; }
    public int EcgFrameCount { get; internal set; }
    public int AccFrameCount { get; internal set; }

    /// <summary>Raised on UI-relevant state changes.</summary>
    public event Action<DeviceContext>? StatusChanged;

    internal DeviceContext(string bluetoothAddress, PolarDeviceIdentity identity, PolarH10Session session)
    {
        BluetoothAddress = bluetoothAddress;
        Identity = identity;
        Session = session;
    }

    internal void RaiseStatusChanged() => StatusChanged?.Invoke(this);

    public async ValueTask DisposeAsync()
    {
        if (Recorder != null)
            Recorder = null;
        await Session.DisposeAsync();
    }
}

public enum DeviceConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Streaming,
    Error,
}
