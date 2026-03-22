using System.Diagnostics;
using PolarH10.Protocol;
using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Runtime;

/// <summary>
/// High-level coordinator for a Polar H10 session using an abstract BLE adapter.
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
        await Task.Delay(500, ct);

        _hrService = await _connection.GetServiceAsync(PolarGattIds.HeartRateService, ct);
        if (_hrService is not null)
        {
            _hrChar = await _hrService.GetCharacteristicAsync(PolarGattIds.HeartRateMeasurement, ct);
            if (_hrChar is not null)
            {
                _hrChar.NotificationReceived += OnHrNotification;
                await _hrChar.EnableNotificationsAsync(ct);
            }
        }

        _pmdService = await _connection.GetServiceAsync(PolarGattIds.PmdService, ct);
        if (_pmdService is not null)
        {
            _pmdCtrl = await _pmdService.GetCharacteristicAsync(PolarGattIds.PmdControlPoint, ct);
            _pmdData = await _pmdService.GetCharacteristicAsync(PolarGattIds.PmdData, ct);

            if (_pmdCtrl is not null)
            {
                _pmdCtrl.NotificationReceived += OnPmdCtrlNotification;
                await _pmdCtrl.EnableNotificationsAsync(ct);
                await Task.Delay(200, ct);
            }

            if (_pmdData is not null)
            {
                _pmdData.NotificationReceived += OnPmdDataNotification;
                await _pmdData.EnableNotificationsAsync(ct);
                await Task.Delay(200, ct);
            }

            IsPmdReady = _pmdCtrl is not null && _pmdData is not null;
        }
    }

    public async Task WritePmdCommandAsync(byte[] command, CancellationToken ct = default)
    {
        if (_pmdCtrl is null)
            throw new InvalidOperationException("PMD control point not available");

        BleWriteResult result = await _pmdCtrl.WriteAsync(command, ct);
        if (!result.Success)
            throw new InvalidOperationException($"PMD write failed: {result.ErrorMessage}");
    }

    public Task RequestSettingsAsync(byte measurementType, CancellationToken ct = default)
    {
        byte[] command = PolarPmdCommandBuilder.BuildGetSettingsRequest(measurementType);
        return WritePmdCommandAsync(command, ct);
    }

    public Task StartEcgAsync(int sampleRate = 130, int resolution = 14, CancellationToken ct = default)
    {
        byte[] command = PolarPmdCommandBuilder.BuildStartEcgRequest(sampleRate, resolution);
        return WritePmdCommandAsync(command, ct);
    }

    public Task StartAccAsync(int sampleRate = 200, int resolution = 16, int rangeG = 8, CancellationToken ct = default)
    {
        byte[] command = PolarPmdCommandBuilder.BuildStartAccRequest(sampleRate, resolution, rangeG);
        return WritePmdCommandAsync(command, ct);
    }

    public Task StopStreamAsync(byte measurementType, CancellationToken ct = default)
    {
        byte[] command = PolarPmdCommandBuilder.BuildStopRequest(measurementType);
        return WritePmdCommandAsync(command, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_syntheticBreathingSource is not null)
        {
            _syntheticBreathingSource.BreathingTelemetryReceived -= OnSyntheticBreathingTelemetry;
            _syntheticBreathingSource = null;
        }

        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    private void OnHrNotification(BleNotification notification)
    {
        HrRrSample sample = PolarHrRrDecoder.Decode(notification.Data);
        HrRrReceived?.Invoke(sample);
    }

    private void OnPmdCtrlNotification(BleNotification notification)
    {
        if (PolarPmdControlPointParser.TryParse(notification.Data, out PmdControlPointResponse response))
            PmdCtrlResponse?.Invoke(response);
    }

    private void OnPmdDataNotification(BleNotification notification)
    {
        if (notification.Data.Length < 10)
            return;

        byte measurementType = notification.Data[0];
        byte frameType = notification.Data[9];
        bool compressed = (frameType & 0x80) != 0;
        byte frameTypeBase = (byte)(frameType & 0x7F);
        long receivedTicks = Stopwatch.GetTimestamp();

        switch (measurementType)
        {
            case PolarGattIds.MeasurementTypeEcg when frameType == 0x00:
                try
                {
                    PolarEcgFrame ecgFrame = PolarEcgDecoder.DecodeFrame(notification.Data, receivedTicks);
                    EcgFrameReceived?.Invoke(ecgFrame);
                }
                catch
                {
                }
                break;

            case PolarGattIds.MeasurementTypeAcc:
                try
                {
                    PolarAccFrame accFrame = PolarAccDecoder.DecodeFrame(notification.Data, receivedTicks, compressed, frameTypeBase);
                    AccFrameReceived?.Invoke(accFrame);
                }
                catch
                {
                }
                break;
        }
    }

    private void OnSyntheticBreathingTelemetry(PolarBreathingTelemetry telemetry)
    {
        BreathingTelemetryReceived?.Invoke(telemetry);
    }
}
