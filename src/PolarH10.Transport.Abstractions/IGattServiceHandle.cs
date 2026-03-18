namespace PolarH10.Transport.Abstractions;

/// <summary>
/// Handle to a discovered GATT service on a connected BLE device.
/// </summary>
public interface IGattServiceHandle
{
    /// <summary>The service UUID.</summary>
    string Uuid { get; }

    /// <summary>Get a characteristic handle by UUID.</summary>
    Task<IGattCharacteristicHandle?> GetCharacteristicAsync(string characteristicUuid, CancellationToken ct = default);
}
