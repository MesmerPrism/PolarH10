using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PolarH10.Protocol;
using PolarH10.Transport.Windows;

namespace PolarH10.App;

public partial class MainWindow : Window
{
    private static readonly Color SignalRed = Color.FromRgb(0xC9, 0x4E, 0x3A);
    private static readonly Color HazardYellow = Color.FromRgb(0x9E, 0x77, 0x14);
    private static readonly Color SafetyOrange = Color.FromRgb(0xC8, 0x6E, 0x1C);
    private static readonly Color TelemetryBlue = Color.FromRgb(0x1E, 0x6E, 0x9E);
    private static readonly Color TelemetryGreen = Color.FromRgb(0x62, 0x8F, 0x37);
    private static readonly Color Graphite = Color.FromRgb(0x5B, 0x63, 0x6F);
    private static readonly bool IsPreviewMode = string.Equals(
        Environment.GetEnvironmentVariable("POLARH10_PREVIEW"),
        "1",
        StringComparison.Ordinal);

    private readonly PolarDeviceRegistry _deviceRegistry = new(PolarDeviceRegistry.DefaultFilePath);
    private readonly PolarMultiDeviceCoordinator _coordinator;
    private readonly string? _capturePath = Environment.GetEnvironmentVariable("POLARH10_CAPTURE_PATH");

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
    private bool _previewPrepared;

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

        Loaded += OnLoaded;
    }

    // ── Chart init (same per-signal charts, rebound when selection changes) ──
    private void InitializeCharts()
    {
        _hrChart = CreateSingleChart("HR", SignalRed, 120, out _hrSeries);
        HrChartHost.Child = _hrChart;

        _rrChart = CreateSingleChart("RR", HazardYellow, 120, out _rrSeries);
        RrChartHost.Child = _rrChart;

        _ecgChart = CreateSingleChart("ECG", TelemetryBlue, 650, out _ecgSeries);
        EcgChartHost.Child = _ecgChart;

        _accXChart = CreateSingleChart("ACC X", SafetyOrange, 500, out _accXSeries);
        AccXChartHost.Child = _accXChart;

        _accYChart = CreateSingleChart("ACC Y", TelemetryGreen, 500, out _accYSeries);
        AccYChartHost.Child = _accYChart;

        _accZChart = CreateSingleChart("ACC Z", Graphite, 500, out _accZSeries);
        AccZChartHost.Child = _accZChart;

        _overlayChart = new WaveformChart { NormalizePerSeries = true };
        _ovHr   = _overlayChart.AddSeries("HR", SignalRed, 300);
        _ovRr   = _overlayChart.AddSeries("RR", HazardYellow, 300);
        _ovEcg  = _overlayChart.AddSeries("ECG", TelemetryBlue, 650);
        _ovAccX = _overlayChart.AddSeries("ACC X", SafetyOrange, 500);
        _ovAccY = _overlayChart.AddSeries("ACC Y", TelemetryGreen, 500);
        _ovAccZ = _overlayChart.AddSeries("ACC Z", Graphite, 500);
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

    private static string FormatHrCount(int count) => $"HR samples {count}";
    private static string FormatEcgCount(int count) => $"ECG frames {count}";
    private static string FormatAccCount(int count) => $"ACC frames {count}";

    private Brush ResourceBrush(string key) => (Brush)Application.Current.Resources[key];

    private void SetConnectionStatus(string text, string brushKey)
    {
        StatusText.Text = text;
        StatusText.Foreground = ResourceBrush(brushKey);
    }

    private void SetRecordStatus(string text, string brushKey)
    {
        RecordStatusText.Text = text;
        RecordStatusText.Foreground = ResourceBrush(brushKey);
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
                    SetConnectionStatus(connected ? "Connected" : "Offline", connected ? "FocusBlueBrush" : "GraphiteBrush");
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
                        ? $"RR intervals {string.Join(", ", sample.RrIntervalsMs.Select(r => $"{r:F0} ms"))}"
                        : "RR intervals --";
                    HrCountText.Text = FormatHrCount(cs.HrCount);

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
                    EcgCountText.Text = FormatEcgCount(cs.EcgCount);
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
                    AccCountText.Text = FormatAccCount(cs.AccCount);
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
                DeviceConnectionStatus.Connecting => "  Connecting",
                DeviceConnectionStatus.Connected => "  Connected",
                DeviceConnectionStatus.Streaming => "  Streaming",
                DeviceConnectionStatus.Error => "  Fault",
                _ => "",
            };

            var recTag = ctx?.Recorder != null ? "  Recording" : "";
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

        // Update counters
        if (_chartStates.TryGetValue(address, out var cs))
        {
            HrCountText.Text = FormatHrCount(cs.HrCount);
            EcgCountText.Text = FormatEcgCount(cs.EcgCount);
            AccCountText.Text = FormatAccCount(cs.AccCount);
        }
        else
        {
            HrCountText.Text = FormatHrCount(0);
            EcgCountText.Text = FormatEcgCount(0);
            AccCountText.Text = FormatAccCount(0);
            HrValueText.Text = "-- BPM";
            RrValueText.Text = "RR intervals --";
        }

        // Update recording UI
        var isRecording = ctx?.Recorder != null;
        StartRecordButton.IsEnabled = isConnected && !isRecording;
        StopRecordButton.IsEnabled = isRecording;
        SetRecordStatus(isRecording ? "Recording active" : "Not recording", isRecording ? "SignalRedBrush" : "GraphiteBrush");

        switch (ctx?.Status)
        {
            case DeviceConnectionStatus.Connecting:
                SetConnectionStatus("Connecting", "SafetyOrangeBrush");
                break;
            case DeviceConnectionStatus.Connected:
                SetConnectionStatus("Connected", "FocusBlueBrush");
                break;
            case DeviceConnectionStatus.Streaming:
                SetConnectionStatus("Streaming", "TelemetryGreenBrush");
                break;
            case DeviceConnectionStatus.Error:
                SetConnectionStatus("Fault", "SignalRedBrush");
                break;
            default:
                SetConnectionStatus("Offline", "GraphiteBrush");
                break;
        }
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

        ScanStatusText.Text = $"{_seenDevices.Count} devices";
        ScanButton.IsEnabled = true;
        _deviceRegistry.Save();
        RefreshDeviceList();
    }

    // ── Connect ─────────────────────────────────────────────────
    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (IsPreviewMode)
            return;

        if (_selectedAddress is null) return;
        var address = _selectedAddress;

        ConnectButton.IsEnabled = false;
        SetConnectionStatus("Connecting", "SafetyOrangeBrush");
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
            SetConnectionStatus("Fault", "SignalRedBrush");
            ConnectButton.IsEnabled = true;
            AddLiveLog($"Error: {ex.Message}");
            AddDiagLog($"[{address}] Exception: {ex}");
        }
    }

    // ── Disconnect ──────────────────────────────────────────────
    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (IsPreviewMode)
            return;

        if (_selectedAddress is null) return;
        var address = _selectedAddress;

        await _coordinator.DisconnectAsync(address);

        SetConnectionStatus("Offline", "GraphiteBrush");
        ConnectButton.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
        AddLiveLog($"Disconnected from {DisplayName(address)}");
        RefreshDeviceList();
    }

    // ── Alias editing ───────────────────────────────────────────
    private void OnSetAliasClick(object sender, RoutedEventArgs e)
    {
        if (IsPreviewMode)
            return;

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
        if (IsPreviewMode)
            return;

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
            SetRecordStatus("Recording active", "SignalRedBrush");
            AddLiveLog($"[{DisplayName(_selectedAddress)}] Recording started");
            RefreshDeviceList();
        }
        catch (Exception ex)
        {
            SetRecordStatus($"Record failed: {ex.Message}", "SignalRedBrush");
        }
    }

    private async void OnStopRecordClick(object sender, RoutedEventArgs e)
    {
        if (IsPreviewMode)
            return;

        if (_selectedAddress is null) return;
        var address = _selectedAddress;

        var baseFolder = OutputFolderBox.Text.Trim();
        SetRecordStatus("Saving...", "SafetyOrangeBrush");

        try
        {
            // Use deterministic subfolder per device
            var ctx = _coordinator.GetDevice(address);
            var folderName = ctx?.Recorder?.GenerateFolderName() ?? address;
            var outputPath = Path.Combine(baseFolder, folderName);

            var recorder = await _coordinator.StopRecordingAsync(address, outputPath);
            SetRecordStatus($"Saved to {outputPath}  (HR={recorder.HrRrCount} ECG={recorder.EcgFrameCount} ACC={recorder.AccFrameCount})", "TelemetryGreenBrush");
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
            SetRecordStatus($"Save failed: {ex.Message}", "SignalRedBrush");
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!IsPreviewMode || _previewPrepared)
            return;

        _previewPrepared = true;
        Width = 1520;
        Height = 1100;

        SeedPreviewState();
        UpdateLayout();

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(250);

        if (!string.IsNullOrWhiteSpace(_capturePath))
        {
            SavePreviewCapture(_capturePath);
            Application.Current.Shutdown();
        }
    }

    private void SeedPreviewState()
    {
        DeviceListBox.Items.Clear();
        LiveLogList.Items.Clear();
        DiagLogList.Items.Clear();

        var previewDevices = new[]
        {
            ("A0:9F:1D:42:11:7C", "Unit Alpha  Streaming"),
            ("A0:9F:1D:42:11:91", "Unit Beta  Connected"),
            ("A0:9F:1D:42:12:04", "Lab Harness"),
        };

        foreach (var (address, label) in previewDevices)
        {
            DeviceListBox.Items.Add(new ListBoxItem
            {
                Content = label,
                Tag = address,
            });
        }

        DeviceListBox.SelectedIndex = 0;
        _selectedAddress = previewDevices[0].Item1;

        ScanStatusText.Text = "3 devices";
        SelectedDeviceLabel.Text = "Unit Alpha";
        AliasBox.Text = "Alpha-01";
        SetConnectionStatus("Streaming", "TelemetryGreenBrush");
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = true;
        StartRecordButton.IsEnabled = true;
        StopRecordButton.IsEnabled = false;
        OutputFolderBox.Text = @".\session\preview";
        SetRecordStatus("Ready to capture", "FocusBlueBrush");

        HrValueText.Text = "72 BPM";
        RrValueText.Text = "RR intervals 828 ms, 833 ms, 829 ms";
        HrCountText.Text = FormatHrCount(256);
        EcgCountText.Text = FormatEcgCount(514);
        AccCountText.Text = FormatAccCount(384);

        foreach (var message in new[]
        {
            "[Unit Alpha] Preview session armed",
            "[Unit Alpha] ECG stream stable at 130 Hz",
            "[Unit Alpha] ACC stream stable at 100 Hz",
            "[Unit Alpha] Recorder idle and ready",
        })
        {
            AddLiveLog(message);
        }

        foreach (var message in new[]
        {
            "[A0:9F:1D:42:11:7C] CTRL op=0x01 meas=0x00 err=0x00 ok=True",
            "[A0:9F:1D:42:11:7C] CTRL op=0x02 meas=0x00 err=0x00 ok=True",
            "[A0:9F:1D:42:11:7C] PMD ECG stream active",
            "[A0:9F:1D:42:11:7C] PMD ACC stream active",
        })
        {
            AddDiagLog(message);
        }

        LiveLogList.UpdateLayout();
        DiagLogList.UpdateLayout();
        PopulatePreviewCharts();
    }

    private void PopulatePreviewCharts()
    {
        const int sampleCount = 240;

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / 11.0;
            var hr = 72 + Math.Sin(i / 18.0) * 2.2 + Math.Cos(i / 31.0) * 1.1;
            var rr = 828 + Math.Sin(i / 13.0) * 14 + Math.Cos(i / 29.0) * 9;
            var ecg =
                Math.Sin(t) * 210 +
                Math.Sin(t * 2.4) * 34 +
                Math.Sin(t * 6.8) * 12 +
                (i % 32 == 0 ? 280 : 0);
            var accX = Math.Sin(i / 9.0) * 180 + Math.Cos(i / 17.0) * 40;
            var accY = Math.Cos(i / 11.0) * 150 + Math.Sin(i / 21.0) * 45;
            var accZ = Math.Sin(i / 7.0) * 120 + Math.Cos(i / 19.0) * 55;

            _hrChart.Push(_hrSeries, hr);
            _rrChart.Push(_rrSeries, rr);
            _ecgChart.Push(_ecgSeries, ecg);
            _accXChart.Push(_accXSeries, accX);
            _accYChart.Push(_accYSeries, accY);
            _accZChart.Push(_accZSeries, accZ);

            _overlayChart.Push(_ovHr, hr);
            _overlayChart.Push(_ovRr, rr);
            _overlayChart.Push(_ovEcg, ecg);
            _overlayChart.Push(_ovAccX, accX);
            _overlayChart.Push(_ovAccY, accY);
            _overlayChart.Push(_ovAccZ, accZ);
        }

        _hrChart.Refresh();
        _rrChart.Refresh();
        _ecgChart.Refresh();
        _accXChart.Refresh();
        _accYChart.Refresh();
        _accZChart.Refresh();
        _overlayChart.Refresh();
    }

    private void SavePreviewCapture(string outputPath)
    {
        RootGrid.UpdateLayout();

        var width = Math.Max((int)Math.Ceiling(RootGrid.ActualWidth), 1);
        var height = Math.Max((int)Math.Ceiling(RootGrid.ActualHeight), 1);
        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(RootGrid);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(fullPath);
        encoder.Save(stream);
    }
}
