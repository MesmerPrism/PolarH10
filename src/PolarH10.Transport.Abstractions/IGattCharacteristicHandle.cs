namespace PolarH10.Transport.Abstractions;

/// <summary>
/// Handle to a GATT characteristic on a connected BLE device.
/// </summary>
public interface IGattCharacteristicHandle
{
    /// <summary>The characteristic UUID.</summary>
    string Uuid { get; }

    /// <summary>Raised when a notification is received from this characteristic.</summary>
    event Action<BleNotification> NotificationReceived;

    /// <summary>Enable notifications (writes CCCD descriptor).</summary>
    Task EnableNotificationsAsync(CancellationToken ct = default);

    /// <summary>Disable notifications.</summary>
    Task DisableNotificationsAsync(CancellationToken ct = default);

    /// <summary>Write a value to this characteristic and wait for the GATT response.</summary>
    Task<BleWriteResult> WriteAsync(byte[] data, CancellationToken ct = default);

    /// <summary>Read the current value of this characteristic.</summary>
    Task<byte[]> ReadAsync(CancellationToken ct = default);
}

/// <summary>
/// Notification data received from a GATT characteristic.
/// </summary>
public readonly record struct BleNotification(string CharacteristicUuid, byte[] Data);

/// <summary>
/// Result of a GATT write operation.
/// </summary>
public readonly record struct BleWriteResult(bool Success, string? ErrorMessage);
