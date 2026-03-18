namespace PolarH10.Transport.Abstractions;

/// <summary>
/// Scans for nearby BLE devices and emits discovery events.
/// </summary>
public interface IBleScanner
{
    /// <summary>Raised when a BLE device is discovered during scanning.</summary>
    event Action<BleDeviceFound> DeviceFound;

    /// <summary>Raised when the scan completes or is stopped.</summary>
    event Action ScanCompleted;

    /// <summary>Start scanning for BLE devices.</summary>
    Task StartScanAsync(TimeSpan duration, CancellationToken ct = default);

    /// <summary>Stop an active scan.</summary>
    void StopScan();
}

/// <summary>
/// Discovery data for a nearby BLE device.
/// </summary>
public readonly record struct BleDeviceFound(string Address, string Name, int Rssi);
