using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Synthetic;

public sealed class SyntheticBleAdapterFactory : IBleAdapterFactory
{
    private readonly SyntheticTransportOptions _options;

    public SyntheticBleAdapterFactory(SyntheticTransportOptions? options = null)
    {
        _options = (options ?? SyntheticTransportOptions.CreateDefault()).Normalize();
    }

    public IBleScanner CreateScanner() => new SyntheticBleScanner(_options);

    public IBleConnection CreateConnection(string deviceAddress)
        => new SyntheticBleConnection(deviceAddress, _options);
}
