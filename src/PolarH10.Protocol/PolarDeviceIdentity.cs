namespace PolarH10.Protocol;

/// <summary>
/// Persistent identity record for a known Polar H10 device,
/// keyed by Bluetooth address.
/// </summary>
public sealed class PolarDeviceIdentity
{
    public required string BluetoothAddress { get; set; }
    public string? UserAlias { get; set; }
    public string? AdvertisedName { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public DateTimeOffset LastConnectedAtUtc { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Returns <see cref="UserAlias"/> if set, otherwise the Bluetooth address.
    /// </summary>
    public string DisplayName => UserAlias ?? BluetoothAddress;
}
