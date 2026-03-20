using System.IO.Pipes;
using System.Numerics;
using PolarH10.Protocol;
using PolarH10.Transport.Abstractions;

namespace PolarH10.Transport.Synthetic;

internal sealed class SyntheticBleConnection : IBleConnection, ISyntheticBreathingTelemetrySource
{
    private readonly SyntheticTransportOptions _options;
    private readonly SyntheticGattCharacteristicHandle _hrCharacteristic;
    private readonly SyntheticGattServiceHandle _hrService;
    private NamedPipeClientStream? _stream;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;

    public SyntheticBleConnection(string deviceAddress, SyntheticTransportOptions options)
    {
        DeviceAddress = deviceAddress;
        _options = options.Normalize();
        _hrCharacteristic = new SyntheticGattCharacteristicHandle(PolarGattIds.HeartRateMeasurement);
        _hrService = new SyntheticGattServiceHandle(PolarGattIds.HeartRateService, [_hrCharacteristic]);
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
        IGattServiceHandle? service = string.Equals(serviceUuid, PolarGattIds.HeartRateService, StringComparison.OrdinalIgnoreCase)
            ? _hrService
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
