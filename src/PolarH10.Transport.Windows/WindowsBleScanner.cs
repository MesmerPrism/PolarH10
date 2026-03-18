using PolarH10.Transport.Abstractions;
using global::Windows.Devices.Bluetooth;
using global::Windows.Devices.Bluetooth.Advertisement;

namespace PolarH10.Transport.Windows;

/// <summary>
/// Windows BLE scanner using <see cref="BluetoothLEAdvertisementWatcher"/>.
/// </summary>
public sealed class WindowsBleScanner : IBleScanner, IDisposable
{
    private BluetoothLEAdvertisementWatcher? _watcher;
    private CancellationTokenRegistration _ctRegistration;

    public event Action<BleDeviceFound>? DeviceFound;
    public event Action? ScanCompleted;

    public Task StartScanAsync(TimeSpan duration, CancellationToken ct = default)
    {
        StopScan();

        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        _watcher.Received += OnAdvertisementReceived;
        _watcher.Stopped += OnWatcherStopped;

        _watcher.Start();

        // Auto-stop after duration
        _ctRegistration = ct.Register(() => StopScan());
        _ = Task.Delay(duration, ct).ContinueWith(_ => StopScan(), TaskScheduler.Default);

        return Task.CompletedTask;
    }

    public void StopScan()
    {
        if (_watcher is { Status: BluetoothLEAdvertisementWatcherStatus.Started })
        {
            _watcher.Stop();
        }
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string address = args.BluetoothAddress.ToString("X12");
        string name = args.Advertisement.LocalName ?? string.Empty;
        int rssi = args.RawSignalStrengthInDBm;

        DeviceFound?.Invoke(new BleDeviceFound(address, name, rssi));
    }

    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        ScanCompleted?.Invoke();
    }

    public void Dispose()
    {
        StopScan();
        _ctRegistration.Dispose();
        if (_watcher != null)
        {
            _watcher.Received -= OnAdvertisementReceived;
            _watcher.Stopped -= OnWatcherStopped;
            _watcher = null;
        }
    }
}
