namespace PolarH10.Transport.Abstractions;

/// <summary>
/// Represents a connection to a specific BLE device.
/// </summary>
public interface IBleConnection : IAsyncDisposable
{
    /// <summary>The device address this connection targets.</summary>
    string DeviceAddress { get; }

    /// <summary>True when the GATT connection is active.</summary>
    bool IsConnected { get; }

    /// <summary>Raised when the connection state changes.</summary>
    event Action<BleConnectionStateChanged> ConnectionStateChanged;

    /// <summary>Connect to the device and discover services.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Disconnect from the device.</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Request a specific MTU size. Returns the negotiated MTU.</summary>
    Task<int> RequestMtuAsync(int desiredMtu, CancellationToken ct = default);

    /// <summary>Get a handle to a specific GATT service by UUID.</summary>
    Task<IGattServiceHandle?> GetServiceAsync(string serviceUuid, CancellationToken ct = default);
}

/// <summary>
/// Connection state change event data.
/// </summary>
public readonly record struct BleConnectionStateChanged(bool IsConnected, string? Reason);
