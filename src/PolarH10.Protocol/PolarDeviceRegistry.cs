using System.Text.Json;

namespace PolarH10.Protocol;

/// <summary>
/// Persistent registry of known Polar H10 devices, stored as JSON
/// in the user's local application data folder.
/// </summary>
public sealed class PolarDeviceRegistry
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly Dictionary<string, PolarDeviceIdentity> _devices = new(StringComparer.OrdinalIgnoreCase);

    public PolarDeviceRegistry(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Returns the default registry path under the user's local AppData.
    /// </summary>
    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PolarH10",
            "device-registry.json");

    /// <summary>All known devices.</summary>
    public IReadOnlyCollection<PolarDeviceIdentity> Devices => _devices.Values;

    /// <summary>
    /// Get identity by Bluetooth address, or null if unknown.
    /// </summary>
    public PolarDeviceIdentity? Get(string bluetoothAddress) =>
        _devices.TryGetValue(bluetoothAddress, out var id) ? id : null;

    /// <summary>
    /// Record that a device was seen during scanning.
    /// Creates a new identity if unknown; updates <see cref="PolarDeviceIdentity.AdvertisedName"/> if provided.
    /// </summary>
    public PolarDeviceIdentity RecordSeen(string bluetoothAddress, string? advertisedName = null)
    {
        if (!_devices.TryGetValue(bluetoothAddress, out var identity))
        {
            identity = new PolarDeviceIdentity
            {
                BluetoothAddress = bluetoothAddress,
                AdvertisedName = advertisedName,
                FirstSeenAtUtc = DateTimeOffset.UtcNow,
                LastConnectedAtUtc = default,
            };
            _devices[bluetoothAddress] = identity;
        }
        else if (advertisedName is not null)
        {
            identity.AdvertisedName = advertisedName;
        }

        return identity;
    }

    /// <summary>
    /// Record that a device was successfully connected.
    /// Also calls <see cref="RecordSeen"/> to ensure the device exists.
    /// </summary>
    public PolarDeviceIdentity RecordConnected(string bluetoothAddress, string? advertisedName = null)
    {
        var identity = RecordSeen(bluetoothAddress, advertisedName);
        identity.LastConnectedAtUtc = DateTimeOffset.UtcNow;
        return identity;
    }

    /// <summary>
    /// Set or clear the user alias for a device.
    /// </summary>
    public bool SetAlias(string bluetoothAddress, string? alias)
    {
        if (!_devices.TryGetValue(bluetoothAddress, out var identity))
            return false;

        identity.UserAlias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        return true;
    }

    /// <summary>
    /// Load the registry from disk. Silently starts empty if the file does not exist or is malformed.
    /// </summary>
    public void Load()
    {
        _devices.Clear();

        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var stored = JsonSerializer.Deserialize<StoredRegistry>(json);
            if (stored?.Devices is null)
                return;

            foreach (var d in stored.Devices)
            {
                if (string.IsNullOrWhiteSpace(d.BluetoothAddress))
                    continue;
                _devices[d.BluetoothAddress] = d;
            }
        }
        catch (JsonException)
        {
            // Corrupt file — start fresh.
        }
    }

    /// <summary>
    /// Save the registry to disk.
    /// </summary>
    public void Save()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var stored = new StoredRegistry
        {
            SchemaVersion = SchemaVersion,
            Devices = _devices.Values.ToList(),
        };

        var json = JsonSerializer.Serialize(stored, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    /// <summary>
    /// Async variant of <see cref="Load"/>.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        _devices.Clear();

        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var stored = JsonSerializer.Deserialize<StoredRegistry>(json);
            if (stored?.Devices is null)
                return;

            foreach (var d in stored.Devices)
            {
                if (string.IsNullOrWhiteSpace(d.BluetoothAddress))
                    continue;
                _devices[d.BluetoothAddress] = d;
            }
        }
        catch (JsonException)
        {
            // Corrupt file — start fresh.
        }
    }

    /// <summary>
    /// Async variant of <see cref="Save"/>.
    /// </summary>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var stored = new StoredRegistry
        {
            SchemaVersion = SchemaVersion,
            Devices = _devices.Values.ToList(),
        };

        var json = JsonSerializer.Serialize(stored, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    private sealed class StoredRegistry
    {
        public int SchemaVersion { get; set; }
        public List<PolarDeviceIdentity>? Devices { get; set; }
    }
}
