using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Windows;

/// <summary>
/// Factory for creating Windows BLE adapter instances.
/// </summary>
public sealed class WindowsBleAdapterFactory : IBleAdapterFactory
{
    public IBleScanner CreateScanner() => new WindowsBleScanner();

    public IBleConnection CreateConnection(string deviceAddress) =>
        new WindowsBleConnection(deviceAddress);
}
