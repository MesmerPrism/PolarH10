using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Synthetic;

internal sealed class SyntheticGattCharacteristicHandle : IGattCharacteristicHandle
{
    private readonly Func<byte[], CancellationToken, Task<BleWriteResult>> _writeHandler;

    public SyntheticGattCharacteristicHandle(
        string uuid,
        Func<byte[], CancellationToken, Task<BleWriteResult>>? writeHandler = null)
    {
        Uuid = uuid;
        _writeHandler = writeHandler ?? ((_, _) => Task.FromResult(new BleWriteResult(false, "Writes are not supported.")));
    }

    public string Uuid { get; }

    public event Action<BleNotification>? NotificationReceived;

    public Task EnableNotificationsAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DisableNotificationsAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<BleWriteResult> WriteAsync(byte[] data, CancellationToken ct = default)
        => _writeHandler(data, ct);

    public Task<byte[]> ReadAsync(CancellationToken ct = default)
        => Task.FromResult(Array.Empty<byte>());

    public void Publish(byte[] payload)
        => NotificationReceived?.Invoke(new BleNotification(Uuid, payload));
}
