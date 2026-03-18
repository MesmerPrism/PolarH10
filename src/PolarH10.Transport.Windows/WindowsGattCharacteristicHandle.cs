using PolarH10.Transport.Abstractions;
using global::Windows.Devices.Bluetooth.GenericAttributeProfile;
using global::Windows.Storage.Streams;

namespace PolarH10.Transport.Windows;

/// <summary>
/// Windows WinRT wrapper around a GATT characteristic.
/// Handles notification subscriptions and write operations via the WinRT GATT APIs.
/// </summary>
public sealed class WindowsGattCharacteristicHandle : IGattCharacteristicHandle
{
    private readonly GattCharacteristic _characteristic;

    public string Uuid => _characteristic.Uuid.ToString();

    public event Action<BleNotification>? NotificationReceived;

    internal WindowsGattCharacteristicHandle(GattCharacteristic characteristic)
    {
        _characteristic = characteristic;
    }

    public async Task EnableNotificationsAsync(CancellationToken ct = default)
    {
        var cccdValue = _characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate)
            ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
            : GattClientCharacteristicConfigurationDescriptorValue.Notify;

        var status = await _characteristic
            .WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue)
            .AsTask(ct);

        if (status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"Failed to enable notifications: {status}");

        _characteristic.ValueChanged += OnValueChanged;
    }

    public async Task DisableNotificationsAsync(CancellationToken ct = default)
    {
        _characteristic.ValueChanged -= OnValueChanged;

        await _characteristic
            .WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.None)
            .AsTask(ct);
    }

    public async Task<BleWriteResult> WriteAsync(byte[] data, CancellationToken ct = default)
    {
        try
        {
            var writer = new DataWriter();
            writer.WriteBytes(data);

            var result = await _characteristic
                .WriteValueWithResultAsync(writer.DetachBuffer())
                .AsTask(ct);

            return result.Status == GattCommunicationStatus.Success
                ? new BleWriteResult(true, null)
                : new BleWriteResult(false, result.Status.ToString());
        }
        catch (ObjectDisposedException ex)
        {
            return new BleWriteResult(false, ex.Message);
        }
    }

    public async Task<byte[]> ReadAsync(CancellationToken ct = default)
    {
        var result = await _characteristic.ReadValueAsync().AsTask(ct);

        if (result.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"Failed to read characteristic: {result.Status}");

        var reader = DataReader.FromBuffer(result.Value);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var reader = DataReader.FromBuffer(args.CharacteristicValue);
        var data = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(data);

        NotificationReceived?.Invoke(new BleNotification(Uuid, data));
    }
}
