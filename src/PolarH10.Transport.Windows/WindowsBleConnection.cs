using PolarH10.Transport.Abstractions;
using global::Windows.Devices.Bluetooth;
using global::Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace PolarH10.Transport.Windows;

/// <summary>
/// Windows BLE connection using WinRT GATT APIs.
/// </summary>
public sealed class WindowsBleConnection : IBleConnection
{
    private BluetoothLEDevice? _device;
    private GattSession? _gattSession;
    private readonly ulong _bluetoothAddress;

    public string DeviceAddress { get; }
    public bool IsConnected => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;

    public event Action<BleConnectionStateChanged>? ConnectionStateChanged;

    public WindowsBleConnection(string deviceAddress)
    {
        DeviceAddress = deviceAddress;
        _bluetoothAddress = ulong.Parse(deviceAddress, System.Globalization.NumberStyles.HexNumber);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(_bluetoothAddress).AsTask(ct);

        if (_device == null)
            throw new InvalidOperationException($"Device not found: {DeviceAddress}");

        // Create a persistent GattSession to keep the connection alive
        _gattSession = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(ct);
        _gattSession.MaintainConnection = true;

        _device.ConnectionStatusChanged += OnConnectionStatusChanged;

        // WinRT frequently reports the device as disconnected immediately after
        // creating the BluetoothLEDevice, then flips to connected once service
        // discovery starts. Emitting that transient false state makes the UI
        // look like it connected, disconnected, and connected again.
        if (IsConnected)
            ConnectionStateChanged?.Invoke(new BleConnectionStateChanged(true, null));
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        Cleanup();
        ConnectionStateChanged?.Invoke(new BleConnectionStateChanged(false, "Disconnected by user"));
        return Task.CompletedTask;
    }

    public Task<int> RequestMtuAsync(int desiredMtu, CancellationToken ct = default)
    {
        if (_gattSession == null) throw new InvalidOperationException("Not connected");
        // WinRT negotiates MTU automatically; return the current negotiated PDU size.
        return Task.FromResult((int)_gattSession.MaxPduSize);
    }

    public async Task<IGattServiceHandle?> GetServiceAsync(string serviceUuid, CancellationToken ct = default)
    {
        if (_device == null) throw new InvalidOperationException("Not connected");

        var result = await _device.GetGattServicesForUuidAsync(
            Guid.Parse(serviceUuid),
            BluetoothCacheMode.Uncached).AsTask(ct);

        if (result.Status != GattCommunicationStatus.Success || result.Services.Count == 0)
            return null;

        return new WindowsGattServiceHandle(result.Services[0]);
    }

    public async ValueTask DisposeAsync()
    {
        Cleanup();
        await Task.CompletedTask;
    }

    private void Cleanup()
    {
        if (_gattSession != null)
        {
            _gattSession.Dispose();
            _gattSession = null;
        }
        if (_device != null)
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _device.Dispose();
            _device = null;
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        bool connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
        ConnectionStateChanged?.Invoke(new BleConnectionStateChanged(connected, null));
    }
}
