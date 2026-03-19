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
    private static readonly Color FocusBlue = Color.FromRgb(0x25, 0x8A, 0xCB);
    private static readonly Color SignalRed = Color.FromRgb(0xC9, 0x4E, 0x3A);
    private static readonly Color HazardYellow = Color.FromRgb(0x9E, 0x77, 0x14);
    private static readonly Color SafetyOrange = Color.FromRgb(0xC8, 0x6E, 0x1C);
    private static readonly Color TelemetryBlue = Color.FromRgb(0x1E, 0x6E, 0x9E);
    private static readonly Color TelemetryGreen = Color.FromRgb(0x62, 0x8F, 0x37);
    private static readonly Color Graphite = Color.FromRgb(0x5B, 0x63, 0x6F);
    private static readonly Color Umber = Color.FromRgb(0x8D, 0x5C, 0x2E);
    private static readonly Color Slate = Color.FromRgb(0x4F, 0x63, 0x75);
    private static readonly Color[] DeviceTracePalette =
    [
        FocusBlue,
        TelemetryGreen,
        SignalRed,
        SafetyOrange,
        Slate,
        Umber,
    ];
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
    private bool _trackingFollowsSelection = true;
    private bool _updatingTrackingUi;
    private readonly HashSet<string> _trackedAddresses = new(StringComparer.OrdinalIgnoreCase);

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
    private readonly Dictionary<string, LiveSeriesBinding> _liveSeriesBindings = new(StringComparer.OrdinalIgnoreCase);

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

    private sealed class LiveSeriesBinding
    {
        public required int Hr;
        public required int Rr;
        public required int Ecg;
        public required int AccX;
        public required int AccY;
        public required int AccZ;
    }

    // ── Chart init (same per-signal charts, rebound when tracked devices change) ──
    private void InitializeCharts()
    {
        RebuildTelemetryCharts();

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

    private static WaveformChart CreateChart(string title, bool normalizePerSeries = false)
    {
        return new WaveformChart
        {
            Title = title,
            NormalizePerSeries = normalizePerSeries,
        };
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

    private IEnumerable<string> GetKnownDeviceAddresses()
    {
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var addr in _seenDevices.Keys)
            addresses.Add(addr);
        if (_coordinator is not null)
        {
            foreach (var ctx in _coordinator.Devices)
                addresses.Add(ctx.BluetoothAddress);
        }
        foreach (var addr in _chartStates.Keys)
            addresses.Add(addr);
        if (!string.IsNullOrWhiteSpace(_selectedAddress))
            addresses.Add(_selectedAddress);

        return addresses.OrderBy(CompactDisplayName, StringComparer.OrdinalIgnoreCase);
    }

    private List<string> GetTrackedChartAddresses()
    {
        if (_trackingFollowsSelection)
        {
            return string.IsNullOrWhiteSpace(_selectedAddress)
                ? []
                : [_selectedAddress];
        }

        return GetKnownDeviceAddresses()
            .Where(address => _trackedAddresses.Contains(address))
            .ToList();
    }

    private string CompactDisplayName(string address)
    {
        var identity = _deviceRegistry.Get(address);
        if (!string.IsNullOrWhiteSpace(identity?.UserAlias))
            return identity.UserAlias;
        if (!string.IsNullOrWhiteSpace(identity?.AdvertisedName))
            return identity.AdvertisedName;
        if (_seenDevices.TryGetValue(address, out var seen) && !string.IsNullOrWhiteSpace(seen.Name))
            return seen.Name;
        return address.Length > 8 ? address[^8..] : address;
    }

    private static Color DeviceTraceColor(int index) => DeviceTracePalette[index % DeviceTracePalette.Length];

    private void RebuildTelemetryCharts()
    {
        RebuildLiveCharts();
        RebuildOverlayChart();
        UpdateTrackedDevicesSummary();
    }

    private void RebuildLiveCharts()
    {
        _liveSeriesBindings.Clear();

        _hrChart = CreateChart("HR");
        _rrChart = CreateChart("RR");
        _ecgChart = CreateChart("ECG");
        _accXChart = CreateChart("ACC X");
        _accYChart = CreateChart("ACC Y");
        _accZChart = CreateChart("ACC Z");

        var trackedAddresses = GetTrackedChartAddresses();
        for (var index = 0; index < trackedAddresses.Count; index++)
        {
            var address = trackedAddresses[index];
            var label = CompactDisplayName(address);
            var color = DeviceTraceColor(index);

            var binding = new LiveSeriesBinding
            {
                Hr = _hrChart.AddSeries(label, color, 120),
                Rr = _rrChart.AddSeries(label, color, 120),
                Ecg = _ecgChart.AddSeries(label, color, 650),
                AccX = _accXChart.AddSeries(label, color, 500),
                AccY = _accYChart.AddSeries(label, color, 500),
                AccZ = _accZChart.AddSeries(label, color, 500),
            };

            _liveSeriesBindings[address] = binding;

            if (_chartStates.TryGetValue(address, out var state))
                ReplayLiveSeries(binding, state);
        }

        HrChartHost.Child = _hrChart;
        RrChartHost.Child = _rrChart;
        EcgChartHost.Child = _ecgChart;
        AccXChartHost.Child = _accXChart;
        AccYChartHost.Child = _accYChart;
        AccZChartHost.Child = _accZChart;

        _hrChart.Refresh();
        _rrChart.Refresh();
        _ecgChart.Refresh();
        _accXChart.Refresh();
        _accYChart.Refresh();
        _accZChart.Refresh();
    }

    private void ReplayLiveSeries(LiveSeriesBinding binding, DeviceChartState state)
    {
        foreach (var value in state.HrValues)
            _hrChart.Push(binding.Hr, value);
        foreach (var value in state.RrValues)
            _rrChart.Push(binding.Rr, value);
        foreach (var value in state.EcgValues)
            _ecgChart.Push(binding.Ecg, value);
        foreach (var value in state.AccXValues)
            _accXChart.Push(binding.AccX, value);
        foreach (var value in state.AccYValues)
            _accYChart.Push(binding.AccY, value);
        foreach (var value in state.AccZValues)
            _accZChart.Push(binding.AccZ, value);
    }

    private void RebuildOverlayChart()
    {
        _overlayChart = CreateChart("Overlay", normalizePerSeries: true);
        _ovHr = _overlayChart.AddSeries("HR", SignalRed, 300);
        _ovRr = _overlayChart.AddSeries("RR", HazardYellow, 300);
        _ovEcg = _overlayChart.AddSeries("ECG", TelemetryBlue, 650);
        _ovAccX = _overlayChart.AddSeries("ACC X", SafetyOrange, 500);
        _ovAccY = _overlayChart.AddSeries("ACC Y", TelemetryGreen, 500);
        _ovAccZ = _overlayChart.AddSeries("ACC Z", Graphite, 500);

        if (!string.IsNullOrWhiteSpace(_selectedAddress) && _chartStates.TryGetValue(_selectedAddress, out var state))
            ReplayOverlaySeries(state);

        OverlayChartHost.Child = _overlayChart;
        ApplyOverlayVisibility();
        _overlayChart.Refresh();
    }

    private void ReplayOverlaySeries(DeviceChartState state)
    {
        foreach (var value in state.HrValues)
            _overlayChart.Push(_ovHr, value);
        foreach (var value in state.RrValues)
            _overlayChart.Push(_ovRr, value);
        foreach (var value in state.EcgValues)
            _overlayChart.Push(_ovEcg, value);
        foreach (var value in state.AccXValues)
            _overlayChart.Push(_ovAccX, value);
        foreach (var value in state.AccYValues)
            _overlayChart.Push(_ovAccY, value);
        foreach (var value in state.AccZValues)
            _overlayChart.Push(_ovAccZ, value);
    }

    private void ApplyOverlayVisibility()
    {
        if (_overlayChart == null ||
            OvHrToggle == null || OvRrToggle == null || OvEcgToggle == null ||
            OvAccXToggle == null || OvAccYToggle == null || OvAccZToggle == null)
            return;

        _overlayChart.SetVisible(_ovHr, OvHrToggle.IsChecked == true);
        _overlayChart.SetVisible(_ovRr, OvRrToggle.IsChecked == true);
        _overlayChart.SetVisible(_ovEcg, OvEcgToggle.IsChecked == true);
        _overlayChart.SetVisible(_ovAccX, OvAccXToggle.IsChecked == true);
        _overlayChart.SetVisible(_ovAccY, OvAccYToggle.IsChecked == true);
        _overlayChart.SetVisible(_ovAccZ, OvAccZToggle.IsChecked == true);
    }

    private void PopulateTrackedDevicesPanel()
    {
        if (TrackedDevicesPanel == null || FollowSelectedTrackingCheckBox == null)
            return;

        _updatingTrackingUi = true;
        try
        {
            FollowSelectedTrackingCheckBox.IsChecked = _trackingFollowsSelection;
            TrackedDevicesPanel.Children.Clear();

            var addresses = GetKnownDeviceAddresses().ToList();
            if (addresses.Count == 0)
            {
                TrackedDevicesPanel.Children.Add(new TextBlock
                {
                    Text = "No devices available yet",
                    Foreground = ResourceBrush("GraphiteBrush"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                });
                return;
            }

            foreach (var address in addresses)
            {
                var checkBox = new CheckBox
                {
                    Content = DisplayName(address),
                    Tag = address,
                    Margin = new Thickness(0, 0, 0, 6),
                    IsEnabled = !_trackingFollowsSelection,
                    IsChecked = _trackingFollowsSelection
                        ? string.Equals(address, _selectedAddress, StringComparison.OrdinalIgnoreCase)
                        : _trackedAddresses.Contains(address),
                };
                checkBox.Checked += OnTrackedDeviceCheckChanged;
                checkBox.Unchecked += OnTrackedDeviceCheckChanged;
                TrackedDevicesPanel.Children.Add(checkBox);
            }
        }
        finally
        {
            _updatingTrackingUi = false;
        }
    }

    private void UpdateTrackedDevicesSummary()
    {
        if (TrackedDevicesSummaryText == null)
            return;

        if (_trackingFollowsSelection)
        {
            TrackedDevicesSummaryText.Text = string.IsNullOrWhiteSpace(_selectedAddress)
                ? "No device selected"
                : $"Following {CompactDisplayName(_selectedAddress)} on the live charts";
        }
        else
        {
            var tracked = GetTrackedChartAddresses();
            TrackedDevicesSummaryText.Text = tracked.Count switch
            {
                0 => "No chart targets selected",
                1 => $"Tracking {CompactDisplayName(tracked[0])}",
                2 => $"Tracking {CompactDisplayName(tracked[0])} and {CompactDisplayName(tracked[1])}",
                _ => $"Tracking {CompactDisplayName(tracked[0])}, {CompactDisplayName(tracked[1])}, +{tracked.Count - 2} more",
            };
        }

        if (OverlayTrackingSummaryText != null)
        {
            OverlayTrackingSummaryText.Text = string.IsNullOrWhiteSpace(_selectedAddress)
                ? "Overlay follows the selected device"
                : $"Overlay follows {CompactDisplayName(_selectedAddress)}";
        }
    }

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

                if (_liveSeriesBindings.TryGetValue(address, out var binding))
                {
                    _hrChart.Push(binding.Hr, sample.HeartRateBpm);
                    foreach (var rr in sample.RrIntervalsMs)
                        _rrChart.Push(binding.Rr, rr);
                }

                if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
                {
                    HrValueText.Text = $"{sample.HeartRateBpm} BPM";
                    RrValueText.Text = sample.RrIntervalsMs.Length > 0
                        ? $"RR intervals {string.Join(", ", sample.RrIntervalsMs.Select(r => $"{r:F0} ms"))}"
                        : "RR intervals --";
                    HrCountText.Text = FormatHrCount(cs.HrCount);

                    _overlayChart.Push(_ovHr, sample.HeartRateBpm);
                    foreach (var rr in sample.RrIntervalsMs)
                        _overlayChart.Push(_ovRr, rr);
                }
            });

        ctx.Session.EcgFrameReceived += frame =>
            Dispatcher.Invoke(() =>
            {
                var cs = GetOrCreateChartState(address);
                cs.EcgCount++;

                foreach (var uv in frame.MicroVolts)
                    cs.EcgValues.Add(uv);

                if (_liveSeriesBindings.TryGetValue(address, out var binding))
                {
                    foreach (var uv in frame.MicroVolts)
                        _ecgChart.Push(binding.Ecg, uv);
                }

                if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
                {
                    EcgCountText.Text = FormatEcgCount(cs.EcgCount);
                    foreach (var uv in frame.MicroVolts)
                    {
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

                if (_liveSeriesBindings.TryGetValue(address, out var binding))
                {
                    foreach (var s in frame.Samples)
                    {
                        _accXChart.Push(binding.AccX, s.X);
                        _accYChart.Push(binding.AccY, s.Y);
                        _accZChart.Push(binding.AccZ, s.Z);
                    }
                }

                if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
                {
                    AccCountText.Text = FormatAccCount(cs.AccCount);
                    foreach (var s in frame.Samples)
                    {
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
        foreach (var addr in _chartStates.Keys) allAddresses.Add(addr);

        foreach (var addr in allAddresses.OrderBy(CompactDisplayName, StringComparer.OrdinalIgnoreCase))
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

            var item = CreateDeviceListItem(display, addr);
            DeviceListBox.Items.Add(item);

            if (addr.Equals(prevSelected, StringComparison.OrdinalIgnoreCase))
                item.IsSelected = true;
        }

        PopulateTrackedDevicesPanel();
        UpdateTrackedDevicesSummary();
    }

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceListBox.SelectedItem is ListBoxItem item && item.Tag is string addr)
        {
            _selectedAddress = addr;
            if (_trackingFollowsSelection)
            {
                _trackedAddresses.Clear();
                _trackedAddresses.Add(addr);
            }
            RebuildTelemetryCharts();
            PopulateTrackedDevicesPanel();
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
            HrValueText.Text = cs.HrValues.Count > 0
                ? $"{cs.HrValues[^1]:F0} BPM"
                : "-- BPM";
            RrValueText.Text = cs.RrValues.Count > 0
                ? $"RR intervals {string.Join(", ", cs.RrValues.TakeLast(Math.Min(3, cs.RrValues.Count)).Select(value => $"{value:F0} ms"))}"
                : "RR intervals --";
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
        RebuildTelemetryCharts();
        AddLiveLog($"Alias for {_selectedAddress} set to: {(string.IsNullOrEmpty(alias) ? "(cleared)" : alias)}");
    }

    // ── Overlay toggles ────────────────────────────────────────
    private void OnOverlayToggle(object sender, RoutedEventArgs e)
    {
        ApplyOverlayVisibility();
    }

    private void OnTrackedDevicesButtonClick(object sender, RoutedEventArgs e)
    {
        PopulateTrackedDevicesPanel();
        TrackedDevicesPopup.IsOpen = !TrackedDevicesPopup.IsOpen;
    }

    private void OnFollowSelectedTrackingChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingTrackingUi || FollowSelectedTrackingCheckBox == null ||
            HrChartHost == null || RrChartHost == null || EcgChartHost == null ||
            AccXChartHost == null || AccYChartHost == null || AccZChartHost == null)
            return;

        _trackingFollowsSelection = FollowSelectedTrackingCheckBox.IsChecked == true;
        _trackedAddresses.Clear();

        if (!string.IsNullOrWhiteSpace(_selectedAddress))
            _trackedAddresses.Add(_selectedAddress);

        RebuildTelemetryCharts();
        PopulateTrackedDevicesPanel();
    }

    private void OnTrackedDeviceCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingTrackingUi || sender is not CheckBox checkBox || checkBox.Tag is not string address)
            return;

        _trackingFollowsSelection = false;
        if (checkBox.IsChecked == true)
            _trackedAddresses.Add(address);
        else
            _trackedAddresses.Remove(address);

        if (_trackedAddresses.Count == 0)
            _trackedAddresses.Add(address);

        RebuildLiveCharts();
        UpdateTrackedDevicesSummary();
        PopulateTrackedDevicesPanel();
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
        if (!IsPreviewMode)
        {
            PopulateTrackedDevicesPanel();
            UpdateTrackedDevicesSummary();
            return;
        }

        if (_previewPrepared)
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
        _seenDevices.Clear();
        _chartStates.Clear();
        _trackedAddresses.Clear();

        var previewDevices = new[]
        {
            ("A0:9F:1D:42:11:7C", "Unit Alpha", "Streaming"),
            ("A0:9F:1D:42:11:91", "Unit Beta", "Connected"),
            ("A0:9F:1D:42:12:04", "Lab Harness", ""),
        };

        foreach (var (address, name, status) in previewDevices)
        {
            _seenDevices[address] = (name, -40);
            _deviceRegistry.RecordSeen(address, name);
            var label = string.IsNullOrWhiteSpace(status) ? name : $"{name}  {status}";
            DeviceListBox.Items.Add(CreateDeviceListItem(label, address));
        }
        _deviceRegistry.SetAlias(previewDevices[0].Item1, "Alpha-01");

        DeviceListBox.SelectedIndex = 0;
        _selectedAddress = previewDevices[0].Item1;
        _trackingFollowsSelection = false;
        _trackedAddresses.Add(previewDevices[0].Item1);
        _trackedAddresses.Add(previewDevices[1].Item1);

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
        PopulatePreviewCharts(previewDevices[0].Item1, offset: 0.0, connectedScale: 1.0);
        PopulatePreviewCharts(previewDevices[1].Item1, offset: 0.9, connectedScale: 0.82);
        PopulatePreviewCharts(previewDevices[2].Item1, offset: 1.6, connectedScale: 0.58);
        RebuildTelemetryCharts();
        PopulateTrackedDevicesPanel();
        UpdateDetailPanel(_selectedAddress);
    }

    private ListBoxItem CreateDeviceListItem(string display, string address)
    {
        return new ListBoxItem
        {
            Content = display,
            Tag = address,
            Style = (Style)FindResource("DeviceRailListItemStyle"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
    }

    private void PopulatePreviewCharts(string address, double offset, double connectedScale)
    {
        var state = GetOrCreateChartState(address);
        const int sampleCount = 240;

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / 11.0 + offset;
            var hr = 72 + Math.Sin(i / 18.0 + offset) * 2.2 * connectedScale + Math.Cos(i / 31.0 + offset) * 1.1;
            var rr = 828 + Math.Sin(i / 13.0 + offset) * 14 * connectedScale + Math.Cos(i / 29.0 + offset) * 9;
            var ecg =
                Math.Sin(t) * 210 * connectedScale +
                Math.Sin(t * 2.4) * 34 +
                Math.Sin(t * 6.8) * 12 +
                (i % 32 == 0 ? 280 * connectedScale : 0);
            var accX = Math.Sin(i / 9.0 + offset) * 180 * connectedScale + Math.Cos(i / 17.0 + offset) * 40;
            var accY = Math.Cos(i / 11.0 + offset) * 150 * connectedScale + Math.Sin(i / 21.0 + offset) * 45;
            var accZ = Math.Sin(i / 7.0 + offset) * 120 * connectedScale + Math.Cos(i / 19.0 + offset) * 55;

            state.HrValues.Add((float)hr);
            state.RrValues.Add((float)rr);
            state.EcgValues.Add((float)ecg);
            state.AccXValues.Add((float)accX);
            state.AccYValues.Add((float)accY);
            state.AccZValues.Add((float)accZ);
        }

        state.HrCount = sampleCount;
        state.EcgCount = sampleCount;
        state.AccCount = sampleCount;
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
