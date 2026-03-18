using PolarH10.Transport.Abstractions;
using global::Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace PolarH10.Transport.Windows;

/// <summary>
/// Windows WinRT wrapper around a discovered GATT service.
/// </summary>
public sealed class WindowsGattServiceHandle : IGattServiceHandle
{
    private readonly GattDeviceService _service;

    public string Uuid => _service.Uuid.ToString();

    internal WindowsGattServiceHandle(GattDeviceService service)
    {
        _service = service;
    }

    public async Task<IGattCharacteristicHandle?> GetCharacteristicAsync(
        string characteristicUuid, CancellationToken ct = default)
    {
        var result = await _service.GetCharacteristicsForUuidAsync(
            Guid.Parse(characteristicUuid),
            global::Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached).AsTask(ct);

        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
            return null;

        return new WindowsGattCharacteristicHandle(result.Characteristics[0]);
    }
}
