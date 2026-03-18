using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PolarH10.Protocol;
using PolarH10.Transport.Windows;

namespace PolarH10.App;

public partial class MainWindow : Window
{
    private static readonly Color SignalRed = Color.FromRgb(0xD9, 0x2C, 0x2C);
    private static readonly Color HazardYellow = Color.FromRgb(0xD8, 0xD6, 0x1A);
    private static readonly Color SafetyOrange = Color.FromRgb(0xF0, 0x5A, 0x22);
    private static readonly Color TelemetryCyan = Color.FromRgb(0x00, 0xA7, 0xA0);
    private static readonly Color Paper = Color.FromRgb(0xF1, 0xEE, 0xE6);
    private static readonly Color Graphite = Color.FromRgb(0x8A, 0x8E, 0x91);

    private readonly PolarDeviceRegistry _deviceRegistry = new(PolarDeviceRegistry.DefaultFilePath);
    private readonly PolarMultiDeviceCoordinator _coordinator;

    // Scan state
    private readonly Dictionary<string, (string Name, int Rssi)> _seenDevices = new();

    // Currently selected device address (from the left device list)
    private string? _selectedAddress;

    // Per-device charting state, keyed by address
    private readonly Dictionary<string, DeviceChartState> _chartStates = new(StringComparer.OrdinalIgnoreCase);

    // Track completed session entries for the current capture run (per base output folder)
    private CaptureRunManifest? _activeRunManifest;

    // Individual charts + series indices (bound to the selected device)
    private WaveformChart _hrChart = null!;
    private WaveformChart _rrChart = null!;
    private WaveformChart _ecgChart = null!;
    private WaveformChart _accXChart = null!;
    private WaveformChart _accYChart = null!;
    private WaveformChart _accZChart = null!;
    private int _hrSeries, _rrSeries, _ecgSeries, _accXSeries, _accYSeries, _accZSeries;

    // Overlay chart + series indices
    private WaveformChart _overlayChart = null!;
    private int _ovHr, _ovRr, _ovEcg, _ovAccX, _ovAccY, _ovAccZ;

    private DispatcherTimer _refreshTimer = null!;

    public MainWindow()
    {
        InitializeComponent();
        InitializeCharts();
        _deviceRegistry.Load();

        var factory = new WindowsBleAdapterFactory();
        _coordinator = new PolarMultiDeviceCoordinator(factory, _deviceRegistry);
        _coordinator.DeviceStatusChanged += ctx =>
            Dispatcher.Invoke(() => RefreshDeviceList());
        _coordinator.DeviceAdded += ctx =>
            Dispatcher.Invoke(() =>
            {
                WireDeviceEvents(ctx);
                RefreshDeviceList();
            });
        _coordinator.DeviceRemoved += ctx =>
            Dispatcher.Invoke(() =>
            {
                _chartStates.Remove(ctx.BluetoothAddress);
                RefreshDeviceList();
            });
    }

    // ── Chart init (same per-signal charts, rebound when selection changes) ──
    private void InitializeCharts()
    {
        _hrChart = CreateSingleChart("01 HR // BPM", SignalRed, 120, out _hrSeries);
        HrChartHost.Child = _hrChart;

        _rrChart = CreateSingleChart("02 RR // MS", HazardYellow, 120, out _rrSeries);
        RrChartHost.Child = _rrChart;

        _ecgChart = CreateSingleChart("03 ECG // UV", TelemetryCyan, 650, out _ecgSeries);
        EcgChartHost.Child = _ecgChart;

        _accXChart = CreateSingleChart("04 ACC X // MG", SafetyOrange, 500, out _accXSeries);
        AccXChartHost.Child = _accXChart;

        _accYChart = CreateSingleChart("05 ACC Y // MG", Paper, 500, out _accYSeries);
        AccYChartHost.Child = _accYChart;

        _accZChart = CreateSingleChart("06 ACC Z // MG", Graphite, 500, out _accZSeries);
        AccZChartHost.Child = _accZChart;

        _overlayChart = new WaveformChart { NormalizePerSeries = true };
        _ovHr   = _overlayChart.AddSeries("HR",    SignalRed,     300);
        _ovRr   = _overlayChart.AddSeries("RR",    HazardYellow,  300);
        _ovEcg  = _overlayChart.AddSeries("ECG",   TelemetryCyan, 650);
        _ovAccX = _overlayChart.AddSeries("ACC X", SafetyOrange,  500);
        _ovAccY = _overlayChart.AddSeries("ACC Y", Paper,         500);
        _ovAccZ = _overlayChart.AddSeries("ACC Z", Graphite,      500);
        OverlayChartHost.Child = _overlayChart;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _refreshTimer.Tick += (_, _) =>
        {
            _hrChart.Refresh();
            _rrChart.Refresh();
            _ecgChart.Refresh();
            _accXChart.Refresh();
            _accYChart.Refresh();
            _accZChart.Refresh();
            _overlayChart.Refresh();
        };
        _refreshTimer.Start();
    }

    private static WaveformChart CreateSingleChart(string title, Color color, int capacity, out int seriesIndex)
    {
        var chart = new WaveformChart { Title = title };
        seriesIndex = chart.AddSeries(title, color, capacity);
        return chart;
    }

    // ── Per-device chart data tracking ──────────────────────────
    private sealed class DeviceChartState
    {
        public int HrCount, EcgCount, AccCount;
        // Waveform data buffers per signal — we push into the shared charts when this device is selected
        public readonly List<float> HrValues = [];
        public readonly List<float> RrValues = [];
        public readonly List<float> EcgValues = [];
        public readonly List<float> AccXValues = [];
        public readonly List<float> AccYValues = [];
        public readonly List<float> AccZValues = [];
    }

    private DeviceChartState GetOrCreateChartState(string address)
    {
        if (!_chartStates.TryGetValue(address, out var state))
        {
            state = new DeviceChartState();
            _chartStates[address] = state;
        }
        return state;
    }

    // ── Wire up event handlers for a new device context ─────────
    private void WireDeviceEvents(DeviceContext ctx)
    {
        var address = ctx.BluetoothAddress;

        ctx.Session.ConnectionChanged += connected =>
            Dispatcher.Invoke(() =>
            {
                if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
                {
                    StatusText.Text = connected ? "LINKED" : "OFFLINE";
                    ConnectButton.IsEnabled = !connected;
                    DisconnectButton.IsEnabled = connected;
                }

                if (connected)
                {
                    AddDiagLog($"[{address}] BLE connected");
                }
                else
                {
                    AddLiveLog($"[{DisplayName(address)}] Connection lost");
                    AddDiagLog($"[{address}] BLE disconnected");
                }
                RefreshDeviceList();
            });

        ctx.Session.HrRrReceived += sample =>
            Dispatcher.Invoke(() =>
            {
                var cs = GetOrCreateChartState(address);
                cs.HrCount++;

                cs.HrValues.Add(sample.HeartRateBpm);
                foreach (var rr in sample.RrIntervalsMs)
                    cs.RrValues.Add(rr);

                if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
                {
                    HrValueText.Text = $"{sample.HeartRateBpm} BPM";
                    RrValueText.Text = sample.RrIntervalsMs.Length > 0
                        ? $"RR // {string.Join(", ", sample.RrIntervalsMs.Select(r => $"{r:F0} MS"))}"
                        : "RR // --";
                    HrCountText.Text = $"HR // {cs.HrCount}";

                    _hrChart.Push(_hrSeries, sample.HeartRateBpm);
                    _overlayChart.Push(_ovHr, sample.HeartRateBpm);
                    foreach (var rr in sample.RrIntervalsMs)
                    {
                        _rrChart.Push(_rrSeries, rr);
                        _overlayChart.Push(_ovRr, rr);
                    }
                }
            });

        ctx.Session.EcgFrameReceived += frame =>
            Dispatcher.Invoke(() =>
            {
                var cs = GetOrCreateChartState(address);
                cs.EcgCount++;

                foreach (var uv in frame.MicroVolts)
                    cs.EcgValues.Add(uv);

                if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
                {
                    EcgCountText.Text = $"ECG FRAMES // {cs.EcgCount}";
                    foreach (var uv in frame.MicroVolts)
                    {
                        _ecgChart.Push(_ecgSeries, uv);
                        _overlayChart.Push(_ovEcg, uv);
                    }
                }
            });

        ctx.Session.AccFrameReceived += frame =>
            Dispatcher.Invoke(() =>
            {
                var cs = GetOrCreateChartState(address);
                cs.AccCount++;

                foreach (var s in frame.Samples)
                {
                    cs.AccXValues.Add(s.X);
                    cs.AccYValues.Add(s.Y);
                    cs.AccZValues.Add(s.Z);
                }

                if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
                {
                    AccCountText.Text = $"ACC FRAMES // {cs.AccCount}";
                    foreach (var s in frame.Samples)
                    {
                        _accXChart.Push(_accXSeries, s.X);
                        _accYChart.Push(_accYSeries, s.Y);
                        _accZChart.Push(_accZSeries, s.Z);
                        _overlayChart.Push(_ovAccX, s.X);
                        _overlayChart.Push(_ovAccY, s.Y);
                        _overlayChart.Push(_ovAccZ, s.Z);
                    }
                }
            });

        ctx.Session.PmdCtrlResponse += resp =>
            Dispatcher.Invoke(() =>
                AddDiagLog($"[{address}] CTRL op=0x{resp.OpCode:X2} meas=0x{resp.MeasurementType:X2} err=0x{resp.ErrorCode:X2} ok={resp.IsSuccess}"));
    }

    // ── Device list management ──────────────────────────────────
    private void RefreshDeviceList()
    {
        var prevSelected = _selectedAddress;
        DeviceListBox.Items.Clear();

        // Show all seen devices (from scan) + all connected devices
        var allAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var addr in _seenDevices.Keys) allAddresses.Add(addr);
        foreach (var ctx in _coordinator.Devices) allAddresses.Add(ctx.BluetoothAddress);

        foreach (var addr in allAddresses)
        {
            var ctx = _coordinator.GetDevice(addr);
            var statusTag = ctx?.Status switch
            {
                DeviceConnectionStatus.Connecting => " [ARMING]",
                DeviceConnectionStatus.Connected => " [LINK]",
                DeviceConnectionStatus.Streaming => " [LIVE]",
                DeviceConnectionStatus.Error => " [FAULT]",
                _ => "",
            };

            var recTag = ctx?.Recorder != null ? " [REC]" : "";
            var display = $"{DisplayName(addr)}{statusTag}{recTag}";

            var item = new ListBoxItem { Content = display, Tag = addr };
            DeviceListBox.Items.Add(item);

            if (addr.Equals(prevSelected, StringComparison.OrdinalIgnoreCase))
                item.IsSelected = true;
        }
    }

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceListBox.SelectedItem is ListBoxItem item && item.Tag is string addr)
        {
            _selectedAddress = addr;
            UpdateDetailPanel(addr);
        }
    }

    private void UpdateDetailPanel(string address)
    {
        var identity = _deviceRegistry.Get(address);
        var ctx = _coordinator.GetDevice(address);

        SelectedDeviceLabel.Text = DisplayName(address);
        AliasBox.Text = identity?.UserAlias ?? "";

        var isConnected = ctx?.Status is DeviceConnectionStatus.Connected or DeviceConnectionStatus.Streaming;
        ConnectButton.IsEnabled = !isConnected;
        DisconnectButton.IsEnabled = isConnected;
        StatusText.Text = ctx?.Status switch
        {
            DeviceConnectionStatus.Connecting => "ARMING",
            DeviceConnectionStatus.Connected => "LINKED",
            DeviceConnectionStatus.Streaming => "LIVE",
            DeviceConnectionStatus.Error => "FAULT",
            _ => "OFFLINE",
        };

        // Update counters
        if (_chartStates.TryGetValue(address, out var cs))
        {
            HrCountText.Text = $"HR // {cs.HrCount}";
            EcgCountText.Text = $"ECG FRAMES // {cs.EcgCount}";
            AccCountText.Text = $"ACC FRAMES // {cs.AccCount}";
        }
        else
        {
            HrCountText.Text = "HR // 0";
            EcgCountText.Text = "ECG FRAMES // 0";
            AccCountText.Text = "ACC FRAMES // 0";
            HrValueText.Text = "-- BPM";
            RrValueText.Text = "RR // --";
        }

        // Update recording UI
        var isRecording = ctx?.Recorder != null;
        StartRecordButton.IsEnabled = isConnected && !isRecording;
        StopRecordButton.IsEnabled = isRecording;
        RecordStatusText.Text = isRecording ? "RECORDING ACTIVE" : "NOT RECORDING";
    }

    // ── Scan ────────────────────────────────────────────────────
    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        ScanStatusText.Text = "Scanning...";
        _seenDevices.Clear();
        AddLiveLog("Scanning for Polar devices...");

        var factory = new WindowsBleAdapterFactory();
        using var scanner = (WindowsBleScanner)factory.CreateScanner();
        var scanCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        scanner.ScanCompleted += () => scanCompleted.TrySetResult();

        scanner.DeviceFound += device =>
        {
            var knownIdentity = _deviceRegistry.Get(device.Address);
            var isPolarByName = !string.IsNullOrWhiteSpace(device.Name) &&
                device.Name.Contains("Polar", StringComparison.OrdinalIgnoreCase);
            var isKnownPolar = knownIdentity?.AdvertisedName?.Contains("Polar", StringComparison.OrdinalIgnoreCase) == true ||
                !string.IsNullOrWhiteSpace(knownIdentity?.UserAlias);

            if (!isPolarByName && !isKnownPolar)
                return;

            Dispatcher.Invoke(() =>
            {
                var displayName = !string.IsNullOrWhiteSpace(device.Name)
                    ? device.Name
                    : knownIdentity?.AdvertisedName ?? device.Address;
                bool isNew = !_seenDevices.ContainsKey(device.Address);
                _seenDevices[device.Address] = (displayName, device.Rssi);
                _deviceRegistry.RecordSeen(device.Address, string.IsNullOrWhiteSpace(device.Name) ? knownIdentity?.AdvertisedName : device.Name);

                if (isNew)
                {
                    AddLiveLog($"Found: {DisplayName(device.Address)} RSSI={device.Rssi}");
                    RefreshDeviceList();
                }
            });
        };

        await scanner.StartScanAsync(TimeSpan.FromSeconds(8));
        await scanCompleted.Task;

        ScanStatusText.Text = $"{_seenDevices.Count} device(s)";
        ScanButton.IsEnabled = true;
        _deviceRegistry.Save();
        RefreshDeviceList();
    }

    // ── Connect ─────────────────────────────────────────────────
    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAddress is null) return;
        var address = _selectedAddress;

        ConnectButton.IsEnabled = false;
        StatusText.Text = "ARMING";
        AddLiveLog($"Connecting to {DisplayName(address)}...");

        try
        {
            var advertisedName = _seenDevices.TryGetValue(address, out var seen) ? seen.Name : null;
            var ctx = await _coordinator.ConnectAsync(address, advertisedName);

            AddLiveLog($"Connected to {DisplayName(address)}");

            // Start PMD streaming if available
            if (ctx.Session.IsPmdReady)
            {
                AddDiagLog($"[{address}] Starting PMD streams...");
                try
                {
                    await _coordinator.StartStreamingAsync(address);
                    AddLiveLog($"[{DisplayName(address)}] Streaming ECG + ACC");
                }
                catch (Exception ex)
                {
                    AddDiagLog($"[{address}] Stream start failed: {ex.Message}");
                }
            }
            else
            {
                AddLiveLog($"[{DisplayName(address)}] PMD not available — HR only");
            }

            UpdateDetailPanel(address);
        }
        catch (Exception ex)
        {
            StatusText.Text = "FAULT";
            ConnectButton.IsEnabled = true;
            AddLiveLog($"Error: {ex.Message}");
            AddDiagLog($"[{address}] Exception: {ex}");
        }
    }

    // ── Disconnect ──────────────────────────────────────────────
    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAddress is null) return;
        var address = _selectedAddress;

        await _coordinator.DisconnectAsync(address);

        StatusText.Text = "OFFLINE";
        ConnectButton.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
        AddLiveLog($"Disconnected from {DisplayName(address)}");
        RefreshDeviceList();
    }

    // ── Alias editing ───────────────────────────────────────────
    private void OnSetAliasClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAddress is null) return;
        var alias = AliasBox.Text.Trim();

        _deviceRegistry.RecordSeen(_selectedAddress); // ensure exists
        _deviceRegistry.SetAlias(_selectedAddress, string.IsNullOrEmpty(alias) ? null : alias);
        _deviceRegistry.Save();

        SelectedDeviceLabel.Text = DisplayName(_selectedAddress);
        RefreshDeviceList();
        AddLiveLog($"Alias for {_selectedAddress} set to: {(string.IsNullOrEmpty(alias) ? "(cleared)" : alias)}");
    }

    // ── Overlay toggles ────────────────────────────────────────
    private void OnOverlayToggle(object sender, RoutedEventArgs e)
    {
        if (_overlayChart == null ||
            OvHrToggle == null || OvRrToggle == null || OvEcgToggle == null ||
            OvAccXToggle == null || OvAccYToggle == null || OvAccZToggle == null)
            return;

        _overlayChart.SetVisible(_ovHr,   OvHrToggle.IsChecked == true);
        _overlayChart.SetVisible(_ovRr,   OvRrToggle.IsChecked == true);
        _overlayChart.SetVisible(_ovEcg,  OvEcgToggle.IsChecked == true);
        _overlayChart.SetVisible(_ovAccX, OvAccXToggle.IsChecked == true);
        _overlayChart.SetVisible(_ovAccY, OvAccYToggle.IsChecked == true);
        _overlayChart.SetVisible(_ovAccZ, OvAccZToggle.IsChecked == true);
    }

    // ── Recording ───────────────────────────────────────────────
    private void OnStartRecordClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAddress is null) return;

        try
        {
            // Initialize run manifest on first recording start
            _activeRunManifest ??= new CaptureRunManifest
            {
                StartedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
            };

            _coordinator.StartRecording(_selectedAddress);
            StartRecordButton.IsEnabled = false;
            StopRecordButton.IsEnabled = true;
            RecordStatusText.Text = "RECORDING ACTIVE";
            AddLiveLog($"[{DisplayName(_selectedAddress)}] Recording started");
            RefreshDeviceList();
        }
        catch (Exception ex)
        {
            RecordStatusText.Text = $"Record failed: {ex.Message}";
        }
    }

    private async void OnStopRecordClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAddress is null) return;
        var address = _selectedAddress;

        var baseFolder = OutputFolderBox.Text.Trim();
        RecordStatusText.Text = "Saving...";

        try
        {
            // Use deterministic subfolder per device
            var ctx = _coordinator.GetDevice(address);
            var folderName = ctx?.Recorder?.GenerateFolderName() ?? address;
            var outputPath = Path.Combine(baseFolder, folderName);

            var recorder = await _coordinator.StopRecordingAsync(address, outputPath);
            RecordStatusText.Text = $"Saved to {outputPath}  (HR={recorder.HrRrCount} ECG={recorder.EcgFrameCount} ACC={recorder.AccFrameCount})";
            AddLiveLog($"[{DisplayName(address)}] Session saved to {outputPath}");

            // Append to run manifest and write it
            if (_activeRunManifest != null)
            {
                _activeRunManifest.DeviceSessions.Add(new CaptureRunManifest.DeviceSessionEntry
                {
                    DeviceAddress = address,
                    DeviceAlias = ctx?.Identity.UserAlias,
                    SubFolder = folderName,
                    SessionId = recorder.SessionId,
                    HrRrSampleCount = recorder.HrRrCount,
                    EcgFrameCount = recorder.EcgFrameCount,
                    AccFrameCount = recorder.AccFrameCount,
                });
                _activeRunManifest.StoppedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O");

                var runJsonPath = Path.Combine(baseFolder, "run.json");
                await CaptureRunManifest.SaveAsync(runJsonPath, _activeRunManifest);

                // Reset manifest if no more devices are recording
                var anyStillRecording = _coordinator.Devices.Any(d => d.Recorder != null);
                if (!anyStillRecording)
                    _activeRunManifest = null;
            }
        }
        catch (Exception ex)
        {
            RecordStatusText.Text = $"Save failed: {ex.Message}";
        }

        StartRecordButton.IsEnabled = true;
        StopRecordButton.IsEnabled = false;
        RefreshDeviceList();
    }

    // ── Helpers ──────────────────────────────────────────────────
    private string DisplayName(string address)
    {
        var identity = _deviceRegistry.Get(address);
        if (identity?.UserAlias is not null)
            return $"{identity.UserAlias} ({address})";
        if (!string.IsNullOrWhiteSpace(identity?.AdvertisedName))
            return $"{identity.AdvertisedName} ({address})";
        if (_seenDevices.TryGetValue(address, out var seen))
            return $"{seen.Name} ({address})";
        return address;
    }

    private void AddLiveLog(string message)
    {
        LiveLogList.Items.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        if (LiveLogList.Items.Count > 500)
            LiveLogList.Items.RemoveAt(0);
        LiveLogList.ScrollIntoView(LiveLogList.Items[^1]);
    }

    private void AddDiagLog(string message)
    {
        DiagLogList.Items.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        if (DiagLogList.Items.Count > 500)
            DiagLogList.Items.RemoveAt(0);
        DiagLogList.ScrollIntoView(DiagLogList.Items[^1]);
    }
}
