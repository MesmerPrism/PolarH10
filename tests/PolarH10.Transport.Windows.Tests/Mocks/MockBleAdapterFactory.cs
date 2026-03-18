using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Windows.Tests.Mocks;

/// <summary>
/// In-memory mock of the BLE adapter factory for testing multi-device scenarios
/// without real hardware.
/// </summary>
internal sealed class MockBleAdapterFactory : IBleAdapterFactory
{
    private readonly Dictionary<string, MockBleConnection> _connections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pre-register a mock connection that will be returned for the given address.</summary>
    public MockBleConnection RegisterDevice(string address)
    {
        var conn = new MockBleConnection(address);
        _connections[address] = conn;
        return conn;
    }

    public IBleScanner CreateScanner() => new MockBleScanner();

    public IBleConnection CreateConnection(string deviceAddress)
    {
        if (_connections.TryGetValue(deviceAddress, out var mock))
            return mock;

        // Auto-create if not pre-registered
        var conn = new MockBleConnection(deviceAddress);
        _connections[deviceAddress] = conn;
        return conn;
    }
}

internal sealed class MockBleScanner : IBleScanner
{
#pragma warning disable CS0067 // Event is never used
    public event Action<BleDeviceFound>? DeviceFound;
#pragma warning restore CS0067
    public event Action? ScanCompleted;

    public Task StartScanAsync(TimeSpan duration, CancellationToken ct = default)
    {
        ScanCompleted?.Invoke();
        return Task.CompletedTask;
    }

    public void StopScan() { }
}

internal sealed class MockBleConnection : IBleConnection, IAsyncDisposable
{
    private bool _disposed;

    public string DeviceAddress { get; }
    public bool IsConnected { get; private set; }
    public bool ConnectShouldFail { get; set; }

    public event Action<BleConnectionStateChanged>? ConnectionStateChanged;

    public MockBleConnection(string deviceAddress) => DeviceAddress = deviceAddress;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (ConnectShouldFail)
            throw new InvalidOperationException("Simulated connection failure");

        IsConnected = true;
        ConnectionStateChanged?.Invoke(new BleConnectionStateChanged(true, null));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        IsConnected = false;
        ConnectionStateChanged?.Invoke(new BleConnectionStateChanged(false, "Disconnect requested"));
        return Task.CompletedTask;
    }

    public Task<int> RequestMtuAsync(int desiredMtu, CancellationToken ct = default) =>
        Task.FromResult(desiredMtu);

    public Task<IGattServiceHandle?> GetServiceAsync(string serviceUuid, CancellationToken ct = default)
    {
        return Task.FromResult<IGattServiceHandle?>(new MockGattServiceHandle(serviceUuid));
    }

    /// <summary>Simulate a connection drop from the peripheral side.</summary>
    public void SimulateDisconnect()
    {
        IsConnected = false;
        ConnectionStateChanged?.Invoke(new BleConnectionStateChanged(false, "Simulated remote disconnect"));
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            IsConnected = false;
        }
        return ValueTask.CompletedTask;
    }
}

internal sealed class MockGattServiceHandle : IGattServiceHandle
{
    public string Uuid { get; }

    public MockGattServiceHandle(string uuid) => Uuid = uuid;

    public Task<IGattCharacteristicHandle?> GetCharacteristicAsync(string characteristicUuid, CancellationToken ct = default)
    {
        return Task.FromResult<IGattCharacteristicHandle?>(new MockGattCharacteristicHandle(characteristicUuid));
    }
}

internal sealed class MockGattCharacteristicHandle : IGattCharacteristicHandle
{
    public string Uuid { get; }
    public event Action<BleNotification>? NotificationReceived;

    public MockGattCharacteristicHandle(string uuid) => Uuid = uuid;

    public Task EnableNotificationsAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DisableNotificationsAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<BleWriteResult> WriteAsync(byte[] data, CancellationToken ct = default) =>
        Task.FromResult(new BleWriteResult(true, null));

    public Task<byte[]> ReadAsync(CancellationToken ct = default) =>
        Task.FromResult(Array.Empty<byte>());

    /// <summary>Simulate an incoming notification from the device.</summary>
    public void SimulateNotification(byte[] data) =>
        NotificationReceived?.Invoke(new BleNotification(Uuid, data));
}
