namespace PolarH10.Transport.Abstractions;

/// <summary>
/// Factory for creating BLE adapter instances on the current platform.
/// </summary>
public interface IBleAdapterFactory
{
    /// <summary>Create a scanner for discovering BLE devices.</summary>
    IBleScanner CreateScanner();

    /// <summary>Create a connection to a specific BLE device.</summary>
    IBleConnection CreateConnection(string deviceAddress);
}
