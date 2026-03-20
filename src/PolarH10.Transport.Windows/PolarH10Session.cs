using System.Diagnostics;
using PolarH10.Protocol;
using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Windows;

/// <summary>
/// High-level coordinator for a Polar H10 session on Windows.
/// Manages the connection lifecycle, notification subscriptions, PMD command flow,
/// and routes decoded data to consumers.
/// </summary>
public sealed class PolarH10Session : IAsyncDisposable
{
    private readonly IBleAdapterFactory _factory;
    private IBleConnection? _connection;
    private IGattServiceHandle? _hrService;
    private IGattServiceHandle? _pmdService;
    private IGattCharacteristicHandle? _hrChar;
    private IGattCharacteristicHandle? _pmdCtrl;
    private IGattCharacteristicHandle? _pmdData;
    private ISyntheticBreathingTelemetrySource? _syntheticBreathingSource;

    public bool IsConnected => _connection?.IsConnected ?? false;
    public bool IsPmdReady { get; private set; }
    public bool HasSyntheticBreathingTelemetry => _syntheticBreathingSource is not null;

    public event Action<HrRrSample>? HrRrReceived;
    public event Action<PolarEcgFrame>? EcgFrameReceived;
    public event Action<PolarAccFrame>? AccFrameReceived;
    public event Action<PmdControlPointResponse>? PmdCtrlResponse;
    public event Action<PolarBreathingTelemetry>? BreathingTelemetryReceived;
    public event Action<bool>? ConnectionChanged;

    public PolarH10Session(IBleAdapterFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Connect to a Polar H10 at the given address and subscribe to HR + PMD notifications.
    /// </summary>
    public async Task ConnectAsync(string deviceAddress, CancellationToken ct = default)
    {
        _connection = _factory.CreateConnection(deviceAddress);
        _connection.ConnectionStateChanged += e => ConnectionChanged?.Invoke(e.IsConnected);
        if (_connection is ISyntheticBreathingTelemetrySource syntheticBreathingSource)
        {
            _syntheticBreathingSource = syntheticBreathingSource;
            _syntheticBreathingSource.BreathingTelemetryReceived += OnSyntheticBreathingTelemetry;
        }

        await _connection.ConnectAsync(ct);

        // Small pause to let the GATT session stabilize
        await Task.Delay(500, ct);

        // HR service (keep reference to prevent GC)
        _hrService = await _connection.GetServiceAsync(PolarGattIds.HeartRateService, ct);
        if (_hrService != null)
        {
            _hrChar = await _hrService.GetCharacteristicAsync(PolarGattIds.HeartRateMeasurement, ct);
            if (_hrChar != null)
            {
                _hrChar.NotificationReceived += OnHrNotification;
                await _hrChar.EnableNotificationsAsync(ct);
            }
        }

        // PMD service (keep reference to prevent GC)
        _pmdService = await _connection.GetServiceAsync(PolarGattIds.PmdService, ct);
        if (_pmdService != null)
        {
            _pmdCtrl = await _pmdService.GetCharacteristicAsync(PolarGattIds.PmdControlPoint, ct);
            _pmdData = await _pmdService.GetCharacteristicAsync(PolarGattIds.PmdData, ct);

            if (_pmdCtrl != null)
            {
                _pmdCtrl.NotificationReceived += OnPmdCtrlNotification;
                await _pmdCtrl.EnableNotificationsAsync(ct);
                await Task.Delay(200, ct);
            }

            if (_pmdData != null)
            {
                _pmdData.NotificationReceived += OnPmdDataNotification;
                await _pmdData.EnableNotificationsAsync(ct);
                await Task.Delay(200, ct);
            }

            IsPmdReady = _pmdCtrl != null && _pmdData != null;
        }
    }

    /// <summary>
    /// Write a PMD command to the control point.
    /// </summary>
    public async Task WritePmdCommandAsync(byte[] command, CancellationToken ct = default)
    {
        if (_pmdCtrl == null) throw new InvalidOperationException("PMD control point not available");
        var result = await _pmdCtrl.WriteAsync(command, ct);
        if (!result.Success)
            throw new InvalidOperationException($"PMD write failed: {result.ErrorMessage}");
    }

    /// <summary>
    /// Request measurement settings for a given type (ECG=0x00, ACC=0x02).
    /// </summary>
    public Task RequestSettingsAsync(byte measurementType, CancellationToken ct = default)
    {
        var cmd = PolarPmdCommandBuilder.BuildGetSettingsRequest(measurementType);
        return WritePmdCommandAsync(cmd, ct);
    }

    /// <summary>
    /// Start the ECG stream with specified parameters.
    /// </summary>
    public Task StartEcgAsync(int sampleRate = 130, int resolution = 14, CancellationToken ct = default)
    {
        var cmd = PolarPmdCommandBuilder.BuildStartEcgRequest(sampleRate, resolution);
        return WritePmdCommandAsync(cmd, ct);
    }

    /// <summary>
    /// Start the ACC stream with specified parameters.
    /// </summary>
    public Task StartAccAsync(int sampleRate = 200, int resolution = 16, int rangeG = 8, CancellationToken ct = default)
    {
        var cmd = PolarPmdCommandBuilder.BuildStartAccRequest(sampleRate, resolution, rangeG);
        return WritePmdCommandAsync(cmd, ct);
    }

    /// <summary>
    /// Stop a measurement stream.
    /// </summary>
    public Task StopStreamAsync(byte measurementType, CancellationToken ct = default)
    {
        var cmd = PolarPmdCommandBuilder.BuildStopRequest(measurementType);
        return WritePmdCommandAsync(cmd, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_syntheticBreathingSource is not null)
        {
            _syntheticBreathingSource.BreathingTelemetryReceived -= OnSyntheticBreathingTelemetry;
            _syntheticBreathingSource = null;
        }

        if (_connection != null)
            await _connection.DisposeAsync();
    }

    private void OnHrNotification(BleNotification n)
    {
        var sample = PolarHrRrDecoder.Decode(n.Data);
        HrRrReceived?.Invoke(sample);
    }

    private void OnPmdCtrlNotification(BleNotification n)
    {
        if (PolarPmdControlPointParser.TryParse(n.Data, out var response))
            PmdCtrlResponse?.Invoke(response);
    }

    private void OnPmdDataNotification(BleNotification n)
    {
        if (n.Data.Length < 10) return;

        byte measType = n.Data[0];
        byte frameType = n.Data[9];
        bool compressed = (frameType & 0x80) != 0;
        byte frameTypeBase = (byte)(frameType & 0x7F);
        long receivedTicks = Stopwatch.GetTimestamp();

        switch (measType)
        {
            case PolarGattIds.MeasurementTypeEcg when frameType == 0x00:
                try
                {
                    var ecgFrame = PolarEcgDecoder.DecodeFrame(n.Data, receivedTicks);
                    EcgFrameReceived?.Invoke(ecgFrame);
                }
                catch { /* malformed frame */ }
                break;

            case PolarGattIds.MeasurementTypeAcc:
                try
                {
                    var accFrame = PolarAccDecoder.DecodeFrame(n.Data, receivedTicks, compressed, frameTypeBase);
                    AccFrameReceived?.Invoke(accFrame);
                }
                catch { /* malformed frame */ }
                break;
        }
    }

    private void OnSyntheticBreathingTelemetry(PolarBreathingTelemetry telemetry)
    {
        BreathingTelemetryReceived?.Invoke(telemetry);
    }
}
