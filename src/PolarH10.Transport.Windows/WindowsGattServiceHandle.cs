using PolarH10.Transport.Abstractions;
using global::Windows.Devices.Bluetooth.GenericAttributeProfile;
using global::Windows.Devices.Enumeration;

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
        var uuid = Guid.Parse(characteristicUuid);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var access = await _service.RequestAccessAsync().AsTask(ct);
            if (access != DeviceAccessStatus.Allowed)
            {
                if (attempt == 2)
                    return null;

                await Task.Delay(200 * (attempt + 1), ct);
                continue;
            }

            var result = await _service.GetCharacteristicsForUuidAsync(
                uuid,
                global::Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached).AsTask(ct);

            if (result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0)
                return new WindowsGattCharacteristicHandle(result.Characteristics[0]);

            if (result.Status == GattCommunicationStatus.Success && result.Characteristics.Count == 0)
            {
                var cachedResult = await _service.GetCharacteristicsForUuidAsync(
                    uuid,
                    global::Windows.Devices.Bluetooth.BluetoothCacheMode.Cached).AsTask(ct);

                if (cachedResult.Status == GattCommunicationStatus.Success && cachedResult.Characteristics.Count > 0)
                    return new WindowsGattCharacteristicHandle(cachedResult.Characteristics[0]);
            }

            if (result.Status != GattCommunicationStatus.AccessDenied &&
                result.Status != GattCommunicationStatus.Unreachable)
            {
                return null;
            }

            if (attempt < 2)
                await Task.Delay(200 * (attempt + 1), ct);
        }

        return null;
    }
}
