using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Synthetic;

internal sealed class SyntheticGattServiceHandle : IGattServiceHandle
{
    private readonly Dictionary<string, IGattCharacteristicHandle> _characteristics;

    public SyntheticGattServiceHandle(string uuid, IEnumerable<IGattCharacteristicHandle> characteristics)
    {
        Uuid = uuid;
        _characteristics = characteristics.ToDictionary(c => c.Uuid, StringComparer.OrdinalIgnoreCase);
    }

    public string Uuid { get; }

    public Task<IGattCharacteristicHandle?> GetCharacteristicAsync(string characteristicUuid, CancellationToken ct = default)
    {
        _characteristics.TryGetValue(characteristicUuid, out IGattCharacteristicHandle? characteristic);
        return Task.FromResult(characteristic);
    }
}
