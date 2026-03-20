using System.IO.Pipes;
using System.Numerics;
using PolarH10.Protocol;
using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Synthetic;

internal sealed class SyntheticBleConnection : IBleConnection, ISyntheticBreathingTelemetrySource
{
    private const byte PmdResponseFrameId = 0xF0;
    private const byte PmdOpcodeGetSettings = 0x01;
    private const byte PmdOpcodeStartStream = 0x02;
    private const byte PmdOpcodeStopStream = 0x03;

    private readonly SyntheticTransportOptions _options;
    private readonly SyntheticGattCharacteristicHandle _hrCharacteristic;
    private readonly SyntheticGattServiceHandle _hrService;
    private readonly SyntheticGattCharacteristicHandle _pmdControlCharacteristic;
    private readonly SyntheticGattCharacteristicHandle _pmdDataCharacteristic;
    private readonly SyntheticGattServiceHandle _pmdService;
    private NamedPipeClientStream? _stream;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;
    private bool _ecgStreamingEnabled;
    private bool _accStreamingEnabled;

    public SyntheticBleConnection(string deviceAddress, SyntheticTransportOptions options)
    {
        DeviceAddress = deviceAddress;
        _options = options.Normalize();
        _hrCharacteristic = new SyntheticGattCharacteristicHandle(PolarGattIds.HeartRateMeasurement);
        _hrService = new SyntheticGattServiceHandle(PolarGattIds.HeartRateService, [_hrCharacteristic]);
        _pmdControlCharacteristic = new SyntheticGattCharacteristicHandle(PolarGattIds.PmdControlPoint, HandlePmdControlWriteAsync);
        _pmdDataCharacteristic = new SyntheticGattCharacteristicHandle(PolarGattIds.PmdData);
        _pmdService = new SyntheticGattServiceHandle(PolarGattIds.PmdService, [_pmdControlCharacteristic, _pmdDataCharacteristic]);
    }

    public string DeviceAddress { get; }

    public bool IsConnected { get; private set; }

    public event Action<BleConnectionStateChanged>? ConnectionStateChanged;
    public event Action<PolarBreathingTelemetry>? BreathingTelemetryReceived;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        string pipeName = SyntheticPipeProtocol.GetDevicePipeName(_options, DeviceAddress);
        _stream = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await _stream.ConnectAsync(5_000, ct);
        _ecgStreamingEnabled = false;
        _accStreamingEnabled = false;
        IsConnected = true;
        ConnectionStateChanged?.Invoke(new BleConnectionStateChanged(true, null));

        _readLoopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readLoopTask = RunReadLoopAsync(_stream, _readLoopCts.Token);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (!IsConnected)
            return;

        IsConnected = false;
        _ecgStreamingEnabled = false;
        _accStreamingEnabled = false;

        if (_readLoopCts is not null)
        {
            _readLoopCts.Cancel();
            _readLoopCts.Dispose();
            _readLoopCts = null;
        }

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }

        ConnectionStateChanged?.Invoke(new BleConnectionStateChanged(false, "Disconnect requested"));
    }

    public Task<int> RequestMtuAsync(int desiredMtu, CancellationToken ct = default)
        => Task.FromResult(Math.Max(23, desiredMtu));

    public Task<IGattServiceHandle?> GetServiceAsync(string serviceUuid, CancellationToken ct = default)
    {
        IGattServiceHandle? service =
            string.Equals(serviceUuid, PolarGattIds.HeartRateService, StringComparison.OrdinalIgnoreCase)
                ? _hrService
                : string.Equals(serviceUuid, PolarGattIds.PmdService, StringComparison.OrdinalIgnoreCase)
                    ? _pmdService
                    : null;
        return Task.FromResult(service);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();

        if (_readLoopTask is not null)
        {
            try
            {
                await _readLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunReadLoopAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                SyntheticPipeProtocol.ServerEnvelope? envelope =
                    await SyntheticPipeProtocol.ReadMessageAsync<SyntheticPipeProtocol.ServerEnvelope>(stream, ct);
                if (envelope is null)
                    break;

                switch (envelope.Type)
                {
                    case "hrNotification" when envelope.Payload is not null:
                        _hrCharacteristic.Publish(envelope.Payload);
                        break;
                    case "pmdData" when envelope.Payload is not null:
                        PublishPmdData(envelope.Payload);
                        break;
                    case "breathing" when envelope.Breathing is not null:
                        BreathingTelemetryReceived?.Invoke(ToTelemetry(envelope.Breathing));
                        break;
                    case "disconnect":
                        await DisconnectAsync();
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            if (IsConnected)
            {
                IsConnected = false;
                ConnectionStateChanged?.Invoke(new BleConnectionStateChanged(false, "Synthetic stream closed"));
            }
        }
    }

    private Task<BleWriteResult> HandlePmdControlWriteAsync(byte[] data, CancellationToken ct)
    {
        _ = ct;

        if (data is null || data.Length < 2)
            return Task.FromResult(new BleWriteResult(false, "PMD control payload too short."));

        byte opCode = data[0];
        byte measurementType = data[1];

        switch (opCode)
        {
            case PmdOpcodeGetSettings:
                _pmdControlCharacteristic.Publish(BuildSettingsResponse(measurementType));
                return Task.FromResult(new BleWriteResult(true, null));

            case PmdOpcodeStartStream:
                if (measurementType == PolarGattIds.MeasurementTypeEcg)
                    _ecgStreamingEnabled = true;
                else if (measurementType == PolarGattIds.MeasurementTypeAcc)
                    _accStreamingEnabled = true;

                _pmdControlCharacteristic.Publish(BuildControlResponse(opCode, measurementType, errorCode: 0x00));
                return Task.FromResult(new BleWriteResult(true, null));

            case PmdOpcodeStopStream:
                if (measurementType == PolarGattIds.MeasurementTypeEcg)
                    _ecgStreamingEnabled = false;
                else if (measurementType == PolarGattIds.MeasurementTypeAcc)
                    _accStreamingEnabled = false;

                _pmdControlCharacteristic.Publish(BuildControlResponse(opCode, measurementType, errorCode: 0x00));
                return Task.FromResult(new BleWriteResult(true, null));

            default:
                _pmdControlCharacteristic.Publish(BuildControlResponse(opCode, measurementType, errorCode: 0x01));
                return Task.FromResult(new BleWriteResult(true, null));
        }
    }

    private void PublishPmdData(byte[] payload)
    {
        if (payload.Length == 0)
            return;

        switch (payload[0])
        {
            case PolarGattIds.MeasurementTypeEcg when _ecgStreamingEnabled:
                _pmdDataCharacteristic.Publish(payload);
                break;
            case PolarGattIds.MeasurementTypeAcc when _accStreamingEnabled:
                _pmdDataCharacteristic.Publish(payload);
                break;
        }
    }

    private static byte[] BuildControlResponse(byte opCode, byte measurementType, byte errorCode)
        => [PmdResponseFrameId, opCode, measurementType, errorCode];

    private static byte[] BuildSettingsResponse(byte measurementType)
    {
        var payload = new List<byte>(16)
        {
            PmdResponseFrameId,
            PmdOpcodeGetSettings,
            measurementType,
            0x00,
        };

        switch (measurementType)
        {
            case PolarGattIds.MeasurementTypeEcg:
                AppendSetting(payload, PolarGattIds.SettingTypeSampleRate, 130);
                AppendSetting(payload, PolarGattIds.SettingTypeResolution, 14);
                break;

            case PolarGattIds.MeasurementTypeAcc:
                AppendSetting(payload, PolarGattIds.SettingTypeSampleRate, 200);
                AppendSetting(payload, PolarGattIds.SettingTypeResolution, 16);
                AppendSetting(payload, PolarGattIds.SettingTypeRange, 8);
                break;
        }

        return payload.ToArray();
    }

    private static void AppendSetting(List<byte> payload, byte settingType, ushort value)
    {
        payload.Add(settingType);
        payload.Add(0x01);
        payload.Add((byte)(value & 0xFF));
        payload.Add((byte)((value >> 8) & 0xFF));
    }

    private static PolarBreathingTelemetry ToTelemetry(SyntheticPipeProtocol.BreathingTelemetryEnvelope breathing)
    {
        bool hasTracking = breathing.HasTracking && !breathing.IsStale;
        PolarBreathingState state = Enum.TryParse(breathing.State, ignoreCase: true, out PolarBreathingState parsed)
            ? parsed
            : PolarBreathingState.Pausing;

        return new PolarBreathingTelemetry(
            IsTransportConnected: true,
            HasReceivedAnySample: true,
            IsCalibrating: false,
            IsCalibrated: true,
            HasTracking: hasTracking,
            HasUsefulSignal: hasTracking,
            HasXzModel: true,
            CalibrationProgress01: 1f,
            CurrentVolume01: breathing.Volume01,
            CurrentState: state,
            EstimatedSampleRateHz: 12.5f,
            UsefulAxisRangeG: 0.024f,
            LastProjectionG: 0f,
            Volume3d01: breathing.Volume01,
            VolumeBase01: breathing.Volume01,
            VolumeXz01: breathing.Volume01,
            Axis: Vector3.UnitZ,
            Center: Vector3.Zero,
            BoundMin: 0f,
            BoundMax: 1f,
            XzAxis: new Vector2(1f, 0f),
            XzBoundMin: 0f,
            XzBoundMax: 1f,
            AccFrameCount: 0,
            AccSampleCount: 0,
            LastSampleAgeSeconds: Math.Max(0f, (float)(DateTimeOffset.UtcNow - breathing.SampleTimeUtc).TotalSeconds),
            LastCalibrationFailureReason: string.Empty,
            Settings: PolarBreathingSettings.CreateDefault(),
            LastSampleReceivedAtUtc: breathing.SampleTimeUtc);
    }
}
