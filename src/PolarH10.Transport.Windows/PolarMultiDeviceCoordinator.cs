using PolarH10.Protocol;
using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Windows;

/// <summary>
/// Orchestrates multiple concurrent <see cref="PolarH10Session"/> instances,
/// one per physical device. Thread-safe via a lock around the device dictionary.
/// </summary>
public sealed class PolarMultiDeviceCoordinator : IAsyncDisposable
{
    private readonly IBleAdapterFactory _factory;
    private readonly PolarDeviceRegistry _registry;
    private readonly Dictionary<string, DeviceContext> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>Raised when any device's status changes.</summary>
    public event Action<DeviceContext>? DeviceStatusChanged;

    /// <summary>Raised when a device is added to the coordinator.</summary>
    public event Action<DeviceContext>? DeviceAdded;

    /// <summary>Raised when a device is removed from the coordinator.</summary>
    public event Action<DeviceContext>? DeviceRemoved;

    public PolarMultiDeviceCoordinator(IBleAdapterFactory factory, PolarDeviceRegistry registry)
    {
        _factory = factory;
        _registry = registry;
    }

    /// <summary>All active device contexts. Returns a snapshot.</summary>
    public IReadOnlyList<DeviceContext> Devices
    {
        get
        {
            lock (_lock)
                return _devices.Values.ToList();
        }
    }

    /// <summary>Get a device context by Bluetooth address.</summary>
    public DeviceContext? GetDevice(string bluetoothAddress)
    {
        lock (_lock)
            return _devices.TryGetValue(bluetoothAddress, out var ctx) ? ctx : null;
    }

    /// <summary>
    /// Connect to a device. Creates a new session if one does not already exist for the address.
    /// Throws if the address is already connected.
    /// </summary>
    public async Task<DeviceContext> ConnectAsync(string bluetoothAddress, string? advertisedName = null, CancellationToken ct = default)
    {
        DeviceContext ctx;
        lock (_lock)
        {
            if (_devices.TryGetValue(bluetoothAddress, out var existing))
            {
                if (existing.Status is DeviceConnectionStatus.Connected or DeviceConnectionStatus.Connecting or DeviceConnectionStatus.Streaming)
                    throw new InvalidOperationException($"Device {bluetoothAddress} is already connected or connecting.");

                // Reuse the context entry (e.g. after a disconnect)
                ctx = existing;
            }
            else
            {
                var identity = _registry.RecordSeen(bluetoothAddress, advertisedName);
                var session = new PolarH10Session(_factory);
                ctx = new DeviceContext(bluetoothAddress, identity, session);
                ctx.StatusChanged += c => DeviceStatusChanged?.Invoke(c);
                _devices[bluetoothAddress] = ctx;
                DeviceAdded?.Invoke(ctx);
            }
        }

        ctx.Status = DeviceConnectionStatus.Connecting;
        ctx.RaiseStatusChanged();

        try
        {
            ctx.Session.ConnectionChanged += connected =>
            {
                if (!connected)
                {
                    ctx.Status = DeviceConnectionStatus.Disconnected;
                    ctx.RaiseStatusChanged();
                }
            };

            await ctx.Session.ConnectAsync(bluetoothAddress, ct);

            _registry.RecordConnected(bluetoothAddress, advertisedName);
            _registry.Save();

            ctx.Status = DeviceConnectionStatus.Connected;
            ctx.RaiseStatusChanged();

            return ctx;
        }
        catch
        {
            ctx.Status = DeviceConnectionStatus.Error;
            ctx.RaiseStatusChanged();
            throw;
        }
    }

    /// <summary>
    /// Start ECG and ACC streaming on a connected device.
    /// </summary>
    public async Task StartStreamingAsync(string bluetoothAddress, CancellationToken ct = default)
    {
        var ctx = GetDeviceOrThrow(bluetoothAddress);

        if (!ctx.Session.IsPmdReady)
            throw new InvalidOperationException($"PMD service not available on {bluetoothAddress}.");

        await ctx.Session.RequestSettingsAsync(PolarGattIds.MeasurementTypeEcg, ct);
        await Task.Delay(500, ct);
        await ctx.Session.StartEcgAsync(ct: ct);
        if (!ctx.Session.HasSyntheticBreathingTelemetry)
        {
            await Task.Delay(500, ct);
            await ctx.Session.StartAccAsync(ct: ct);
        }

        ctx.Status = DeviceConnectionStatus.Streaming;
        ctx.RaiseStatusChanged();
    }

    /// <summary>
    /// Disconnect a device and dispose its session.
    /// </summary>
    public async Task DisconnectAsync(string bluetoothAddress)
    {
        DeviceContext? ctx;
        lock (_lock)
        {
            if (!_devices.TryGetValue(bluetoothAddress, out ctx))
                return;
            _devices.Remove(bluetoothAddress);
        }

        try
        {
            if (ctx.Session.IsPmdReady && ctx.Session.IsConnected)
            {
                try
                {
                    await ctx.Session.StopStreamAsync(PolarGattIds.MeasurementTypeEcg);
                    await ctx.Session.StopStreamAsync(PolarGattIds.MeasurementTypeAcc);
                }
                catch { /* best-effort */ }
            }
        }
        finally
        {
            await ctx.DisposeAsync();
            ctx.Status = DeviceConnectionStatus.Disconnected;
            ctx.RaiseStatusChanged();
            DeviceRemoved?.Invoke(ctx);
        }
    }

    /// <summary>
    /// Start recording on a device.
    /// </summary>
    public PolarSessionRecorder StartRecording(string bluetoothAddress)
    {
        var ctx = GetDeviceOrThrow(bluetoothAddress);
        if (ctx.Recorder != null)
            throw new InvalidOperationException($"Already recording on {bluetoothAddress}.");

        var recorder = new PolarSessionRecorder
        {
            DeviceAddress = bluetoothAddress,
            DeviceName = ctx.Identity.AdvertisedName,
            DeviceAlias = ctx.Identity.UserAlias,
        };
        ctx.Recorder = recorder;

        ctx.Session.HrRrReceived += recorder.RecordHrRr;
        ctx.Session.EcgFrameReceived += recorder.RecordEcg;
        ctx.Session.AccFrameReceived += recorder.RecordAcc;

        return recorder;
    }

    /// <summary>
    /// Stop recording on a device and save the session to disk.
    /// </summary>
    public async Task<PolarSessionRecorder> StopRecordingAsync(string bluetoothAddress, string outputFolder, CancellationToken ct = default)
    {
        var ctx = GetDeviceOrThrow(bluetoothAddress);
        var recorder = ctx.Recorder ?? throw new InvalidOperationException($"No active recording on {bluetoothAddress}.");

        ctx.Session.HrRrReceived -= recorder.RecordHrRr;
        ctx.Session.EcgFrameReceived -= recorder.RecordEcg;
        ctx.Session.AccFrameReceived -= recorder.RecordAcc;

        await recorder.SaveAsync(outputFolder, ct);
        ctx.Recorder = null;

        return recorder;
    }

    /// <summary>
    /// Disconnect all devices and dispose everything.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        List<DeviceContext> snapshot;
        lock (_lock)
        {
            snapshot = _devices.Values.ToList();
            _devices.Clear();
        }

        foreach (var ctx in snapshot)
        {
            try { await ctx.DisposeAsync(); }
            catch { /* best-effort */ }
            ctx.Status = DeviceConnectionStatus.Disconnected;
            ctx.RaiseStatusChanged();
            DeviceRemoved?.Invoke(ctx);
        }
    }

    private DeviceContext GetDeviceOrThrow(string bluetoothAddress)
    {
        lock (_lock)
        {
            if (_devices.TryGetValue(bluetoothAddress, out var ctx))
                return ctx;
        }
        throw new InvalidOperationException($"Unknown device: {bluetoothAddress}");
    }
}
