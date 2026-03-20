using System.Globalization;
using PolarH10.Protocol;

namespace PolarH10.App;

public partial class MainWindow
{
    private static readonly PolarHrvSettings AppHrvDefaults = PolarHrvSettings.CreateDefault();
    private const float HrvFirstSolveCoverageRequirement01 = 0.99f;

    private sealed class DeviceHrvState
    {
        public PolarHrvTracker Tracker { get; } = new(AppHrvDefaults);
        public bool HasTelemetry;
        public PolarHrvTelemetry LastTelemetry;
        public readonly List<float> RmssdValues = [];
        public readonly List<float> SdnnValues = [];
        public readonly List<string> LogLines = [];
        public bool HasPlottedValue;
        public float LastPlottedRmssd;
        public float LastPlottedSdnn;

        public void ClearSeries()
        {
            RmssdValues.Clear();
            SdnnValues.Clear();
            HasPlottedValue = false;
            LastPlottedRmssd = 0f;
            LastPlottedSdnn = 0f;
        }
    }

    private readonly Dictionary<string, DeviceHrvState> _hrvStates = new(StringComparer.OrdinalIgnoreCase);
    private HrvWindow? _hrvWindow;
    private WaveformChart _hrvChart = null!;
    private int _hrvRmssdSeries;
    private int _hrvSdnnSeries;
    private string? _hrvChartAddress;
    private string? _hrvEditorAddress;

    private void InitializeHrvChart()
    {
        RebuildHrvChart();
    }

    private DeviceHrvState GetOrCreateHrvState(string address)
    {
        if (!_hrvStates.TryGetValue(address, out DeviceHrvState? state))
        {
            state = new DeviceHrvState();
            _hrvStates[address] = state;
        }

        return state;
    }

    private void RemoveHrvState(string address)
    {
        _hrvStates.Remove(address);
        if (string.Equals(_hrvChartAddress, address, StringComparison.OrdinalIgnoreCase))
            _hrvChartAddress = null;
        if (string.Equals(_hrvEditorAddress, address, StringComparison.OrdinalIgnoreCase))
            _hrvEditorAddress = null;
    }

    private void ClearHrvStates()
    {
        _hrvStates.Clear();
        _hrvChartAddress = null;
        _hrvEditorAddress = null;

        if (_hrvWindow != null)
        {
            _hrvWindow.LogListElement.Items.Clear();
            RebuildHrvChart();
        }
    }

    private void RefreshHrvTrackers()
    {
        foreach ((string address, DeviceHrvState state) in _hrvStates)
        {
            state.Tracker.Advance();
            CaptureHrvTelemetry(address, state, pushChartValue: false);
        }
    }

    private void RebuildHrvChart()
    {
        _hrvChart = CreateChart("Short-term HRV", _hrvChartAxisOptions);
        _hrvRmssdSeries = _hrvChart.AddSeries("RMSSD", FocusBlue, 360);
        _hrvSdnnSeries = _hrvChart.AddSeries("SDNN", TelemetryGreen, 360);

        if (!string.IsNullOrWhiteSpace(_selectedAddress) &&
            _hrvStates.TryGetValue(_selectedAddress, out DeviceHrvState? state))
        {
            ReplayHrvSeries(state);
            _hrvChartAddress = _selectedAddress;
        }
        else
        {
            _hrvChartAddress = null;
        }

        if (_hrvWindow != null)
        {
            SetChartHostContent(_hrvWindow.ChartHostElement, _hrvChart, _hrvChartAxisOptions);
            _hrvChart.Refresh();
        }
    }

    private void ReplayHrvSeries(DeviceHrvState state)
    {
        foreach (float value in state.RmssdValues)
            _hrvChart.Push(_hrvRmssdSeries, value);
        foreach (float value in state.SdnnValues)
            _hrvChart.Push(_hrvSdnnSeries, value);
    }

    private void CaptureHrvTelemetry(string address, DeviceHrvState state, bool pushChartValue)
    {
        PolarHrvTelemetry telemetry = state.Tracker.GetTelemetry();
        if ((pushChartValue && telemetry.HasMetricsSample) || ShouldAppendHrvValue(state, telemetry))
            AppendHrvValue(address, state, telemetry);

        if (state.HasTelemetry)
            LogHrvTransitions(address, state.LastTelemetry, telemetry, state);

        state.LastTelemetry = telemetry;
        state.HasTelemetry = true;

        if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
            UpdateHrvPanel(address);
    }

    private static bool ShouldAppendHrvValue(DeviceHrvState state, PolarHrvTelemetry telemetry)
    {
        if (!telemetry.HasMetricsSample)
            return false;

        if (!state.HasPlottedValue)
            return true;

        return Math.Abs(state.LastPlottedRmssd - telemetry.CurrentRmssdMs) >= 0.25f ||
               Math.Abs(state.LastPlottedSdnn - telemetry.SdnnMs) >= 0.25f ||
               telemetry.TrackingState != state.LastTelemetry.TrackingState;
    }

    private void AppendHrvValue(string address, DeviceHrvState state, PolarHrvTelemetry telemetry)
    {
        AppendRolling(state.RmssdValues, telemetry.CurrentRmssdMs);
        AppendRolling(state.SdnnValues, telemetry.SdnnMs);
        state.HasPlottedValue = true;
        state.LastPlottedRmssd = telemetry.CurrentRmssdMs;
        state.LastPlottedSdnn = telemetry.SdnnMs;

        DeviceChartState chartState = GetOrCreateChartState(address);
        AppendRolling(chartState.HrvRmssdValues, telemetry.CurrentRmssdMs);
        PushMetricSampleToTelemetryCharts(address, TelemetryMetric.HrvRmssd, telemetry.CurrentRmssdMs);

        if (!string.IsNullOrWhiteSpace(_selectedAddress) &&
            string.Equals(_selectedAddress, _hrvChartAddress, StringComparison.OrdinalIgnoreCase))
        {
            _hrvChart.Push(_hrvRmssdSeries, telemetry.CurrentRmssdMs);
            _hrvChart.Push(_hrvSdnnSeries, telemetry.SdnnMs);
        }
    }

    private void LogHrvTransitions(
        string address,
        PolarHrvTelemetry previous,
        PolarHrvTelemetry current,
        DeviceHrvState state)
    {
        if (previous.IsTransportConnected != current.IsTransportConnected)
            AddHrvLog(address, current.IsTransportConnected ? "transport connected" : "transport disconnected", state);

        if (!previous.HasReceivedAnyRrSample && current.HasReceivedAnyRrSample)
            AddHrvLog(address, "RR input detected", state);

        if (previous.TrackingState != current.TrackingState)
        {
            AddHrvLog(
                address,
                current.TrackingState switch
                {
                    PolarHrvTrackingState.Tracking => "short-term HRV tracking ready",
                    PolarHrvTrackingState.WarmingUp => "short-term HRV window warming up",
                    PolarHrvTrackingState.WaitingForRr => "short-term HRV waiting for RR input",
                    PolarHrvTrackingState.Stale => "short-term HRV stale",
                    _ => "short-term HRV unavailable",
                },
                state);
        }

        if (!previous.HasMetricsSample && current.HasMetricsSample)
            AddHrvLog(address, "first short-term HRV window solved", state);
    }

    private void AddHrvLog(string address, string message, DeviceHrvState? state = null)
    {
        state ??= GetOrCreateHrvState(address);
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        state.LogLines.Add(line);
        if (state.LogLines.Count > 250)
            state.LogLines.RemoveAt(0);

        if (_hrvWindow != null &&
            _selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
        {
            _hrvWindow.LogListElement.Items.Add(line);
            if (_hrvWindow.LogListElement.Items.Count > 250)
                _hrvWindow.LogListElement.Items.RemoveAt(0);
            _hrvWindow.LogListElement.ScrollIntoView(_hrvWindow.LogListElement.Items[^1]);
        }
    }

    private void RefreshHrvLogList(DeviceHrvState state)
    {
        if (_hrvWindow == null)
            return;

        _hrvWindow.LogListElement.Items.Clear();
        foreach (string line in state.LogLines)
            _hrvWindow.LogListElement.Items.Add(line);

        if (_hrvWindow.LogListElement.Items.Count > 0)
            _hrvWindow.LogListElement.ScrollIntoView(_hrvWindow.LogListElement.Items[^1]);
    }

    private HrvWindow EnsureHrvWindow()
    {
        if (_hrvWindow != null)
            return _hrvWindow;

        _hrvWindow = new HrvWindow();
        HrvTab.Content = DetachWindowContent(_hrvWindow);
        _hrvWindow.ApplyTuningRequested += OnHrvApplyTuningRequested;
        _hrvWindow.RestoreDefaultsRequested += OnHrvRestoreDefaultsRequested;
        _hrvWindow.ResetTrackerRequested += OnHrvResetTrackerRequested;

        RebuildHrvChart();
        UpdateHrvPanel(_selectedAddress);
        return _hrvWindow;
    }

    private void EnsureHrvEditorLoaded(string? address)
    {
        if (_hrvWindow == null || string.IsNullOrWhiteSpace(address))
            return;

        if (string.Equals(_hrvEditorAddress, address, StringComparison.OrdinalIgnoreCase))
            return;

        DeviceHrvState state = GetOrCreateHrvState(address);
        PolarHrvSettings settings = state.HasTelemetry ? state.LastTelemetry.Settings : state.Tracker.GetTelemetry().Settings;
        LoadHrvSettingsToUi(settings);
        RefreshHrvLogList(state);
        _hrvEditorAddress = address;
    }

    private void UpdateHrvPanel(string? address)
    {
        if (_hrvWindow == null)
            return;

        if (string.IsNullOrWhiteSpace(address))
        {
            _hrvWindow.SelectedDeviceTextBlock.Text = "No device selected";
            _hrvWindow.SummaryTextBlock.Text = "RMSSD is the headline short-term HRV value; supporting telemetry includes SDNN, pNN50, SD1, mean NN, and mean HR from the accepted RR window.";
            _hrvWindow.HrvValueTextBlock.Text = "--";
            _hrvWindow.HrvStateValueTextBlock.Text = "Unavailable";
            _hrvWindow.HrvTrackingValueTextBlock.Text = "Awaiting device selection";
            _hrvWindow.HrvSupportValueTextBlock.Text = "SDNN -- | pNN50 --";
            _hrvWindow.RequirementTextBlock.Text = "The first short-term HRV solve waits for RR accumulation and nearly a full buffered RR window.";
            _hrvWindow.WarmupHintTextBlock.Text = "Select a device to inspect short-term HRV readiness.";
            _hrvWindow.SampleProgressBar.Value = 0d;
            _hrvWindow.SampleProgressTextBlock.Text = "--";
            _hrvWindow.CoverageProgressBar.Value = 0d;
            _hrvWindow.CoverageProgressTextBlock.Text = "--";
            _hrvWindow.RemainingTextBlock.Text = "--";
            _hrvWindow.TrackingTextBlock.Text = "--";
            _hrvWindow.LastRrTextBlock.Text = "--";
            _hrvWindow.HeartbeatTextBlock.Text = "--";
            _hrvWindow.SampleCountTextBlock.Text = "--";
            _hrvWindow.WindowCountTextBlock.Text = "--";
            _hrvWindow.CoverageTextBlock.Text = "--";
            _hrvWindow.LastUpdateTextBlock.Text = "--";
            _hrvWindow.MeanNnTextBlock.Text = "--";
            _hrvWindow.MeanHrTextBlock.Text = "--";
            _hrvWindow.SdnnTextBlock.Text = "--";
            _hrvWindow.Pnn50TextBlock.Text = "--";
            _hrvWindow.Sd1TextBlock.Text = "--";
            _hrvWindow.LnRmssdTextBlock.Text = "--";
            SetHrvStatus("No device selected", "GraphiteBrush");
            if (_hrvChartAddress != null)
                RebuildHrvChart();
            return;
        }

        DeviceHrvState state = GetOrCreateHrvState(address);
        EnsureHrvEditorLoaded(address);

        if (!string.Equals(_hrvChartAddress, address, StringComparison.OrdinalIgnoreCase))
            RebuildHrvChart();

        PolarHrvTelemetry telemetry = state.HasTelemetry ? state.LastTelemetry : state.Tracker.GetTelemetry();
        _hrvWindow.Title = $"Polar H10 // HRV // {CompactDisplayName(address)}";
        _hrvWindow.SelectedDeviceTextBlock.Text = DisplayName(address);
        _hrvWindow.SummaryTextBlock.Text = "RMSSD is the headline short-term HRV value; supporting telemetry includes SDNN, pNN50, SD1, mean NN, and mean HR from the accepted RR window.";
        _hrvWindow.HrvValueTextBlock.Text = telemetry.HasMetricsSample ? $"{telemetry.CurrentRmssdMs:0.0} ms" : "--";
        _hrvWindow.HrvStateValueTextBlock.Text = FormatHrvDisplayState(telemetry);
        _hrvWindow.HrvTrackingValueTextBlock.Text = BuildHrvTrackingLine(telemetry);
        _hrvWindow.HrvSupportValueTextBlock.Text = telemetry.HasMetricsSample
            ? $"SDNN {telemetry.SdnnMs:0.0} ms | pNN50 {telemetry.Pnn50Percent:0}%"
            : BuildHrvSupportPendingText(telemetry);
        _hrvWindow.HrvValueTextBlock.Foreground = ResourceBrush(GetHrvAccentBrushKey(telemetry));
        _hrvWindow.RequirementTextBlock.Text = BuildHrvRequirementLine(telemetry);
        _hrvWindow.WarmupHintTextBlock.Text = BuildHrvWarmupHint(telemetry);
        _hrvWindow.SampleProgressBar.Value = telemetry.SampleRequirementProgress01;
        _hrvWindow.SampleProgressBar.Foreground = ResourceBrush(GetHrvStatusBrushKey(telemetry));
        _hrvWindow.SampleProgressTextBlock.Text = BuildHrvSampleText(telemetry);
        _hrvWindow.CoverageProgressBar.Value = GetHrvCoverageProgress01(telemetry);
        _hrvWindow.CoverageProgressBar.Foreground = ResourceBrush(GetHrvStatusBrushKey(telemetry));
        _hrvWindow.CoverageProgressTextBlock.Text = BuildHrvCoverageText(telemetry);
        _hrvWindow.RemainingTextBlock.Text = BuildHrvRemainingText(telemetry);

        _hrvWindow.TrackingTextBlock.Text = FormatHrvDisplayState(telemetry);
        _hrvWindow.LastRrTextBlock.Text = telemetry.HasReceivedAnyRrSample ? $"{telemetry.CurrentHeartbeatIbiMs:0} ms" : "No RR";
        _hrvWindow.HeartbeatTextBlock.Text = telemetry.HasReceivedAnyRrSample ? $"{telemetry.CurrentHeartbeatBpm:0.0} BPM" : "No RR";
        _hrvWindow.SampleCountTextBlock.Text = $"{telemetry.RrSampleCount} seen";
        _hrvWindow.WindowCountTextBlock.Text = $"{telemetry.AcceptedWindowSampleCount} accepted";
        _hrvWindow.CoverageTextBlock.Text = BuildHrvCoverageText(telemetry);
        _hrvWindow.LastUpdateTextBlock.Text = telemetry.HasMetricsSample ? $"{telemetry.LastMetricsAgeSeconds:0.00} s ago" : "Pending first solve";
        _hrvWindow.MeanNnTextBlock.Text = telemetry.HasMetricsSample ? $"{telemetry.MeanNnMs:0.0} ms" : "Pending first solve";
        _hrvWindow.MeanHrTextBlock.Text = telemetry.HasMetricsSample ? $"{telemetry.MeanHeartRateBpm:0.0} BPM" : "Pending first solve";
        _hrvWindow.SdnnTextBlock.Text = telemetry.HasMetricsSample ? $"{telemetry.SdnnMs:0.0} ms" : "Pending first solve";
        _hrvWindow.Pnn50TextBlock.Text = telemetry.HasMetricsSample ? $"{telemetry.Pnn50Percent:0.0} %" : "Pending first solve";
        _hrvWindow.Sd1TextBlock.Text = telemetry.HasMetricsSample ? $"{telemetry.Sd1Ms:0.0} ms" : "Pending first solve";
        _hrvWindow.LnRmssdTextBlock.Text = telemetry.HasMetricsSample ? telemetry.LnRmssd.ToString("0.000", CultureInfo.InvariantCulture) : "Pending first solve";

        SetHrvStatus(BuildHrvStatusLine(telemetry), GetHrvStatusBrushKey(telemetry));
    }

    private static bool IsHrvWaitingForRr(PolarHrvTelemetry telemetry)
        => telemetry.IsTransportConnected && !telemetry.HasReceivedAnyRrSample;

    private static bool IsHrvWarmingUp(PolarHrvTelemetry telemetry)
        => telemetry.IsTransportConnected && telemetry.HasReceivedAnyRrSample && !telemetry.HasMetricsSample;

    private static string FormatHrvDisplayState(PolarHrvTelemetry telemetry) => telemetry switch
    {
        { HasTracking: true } => "Tracking",
        _ when IsHrvWarmingUp(telemetry) => "Warming up",
        _ when IsHrvWaitingForRr(telemetry) => "Waiting for RR",
        { IsTransportConnected: true, HasMetricsSample: true } => "Stale",
        _ => "Unavailable",
    };

    private static string GetHrvAccentBrushKey(PolarHrvTelemetry telemetry) => telemetry switch
    {
        { HasTracking: true } => "TelemetryGreenBrush",
        _ when IsHrvWarmingUp(telemetry) => "FocusBlueBrush",
        { IsTransportConnected: true, HasMetricsSample: true } => "SafetyOrangeBrush",
        _ => "GraphiteBrush",
    };

    private static string GetHrvStatusBrushKey(PolarHrvTelemetry telemetry) => telemetry switch
    {
        { HasTracking: true } => "TelemetryGreenBrush",
        _ when IsHrvWarmingUp(telemetry) => "FocusBlueBrush",
        _ when IsHrvWaitingForRr(telemetry) => "FocusBlueBrush",
        { IsTransportConnected: true, HasMetricsSample: true } => "SafetyOrangeBrush",
        _ => "GraphiteBrush",
    };

    private static float GetHrvCoverageProgress01(PolarHrvTelemetry telemetry)
        => Math.Clamp(telemetry.WindowCoverage01 / HrvFirstSolveCoverageRequirement01, 0f, 1f);

    private static string BuildHrvTrackingLine(PolarHrvTelemetry telemetry)
    {
        if (telemetry.HasTracking)
            return "Short-term HRV live";
        if (IsHrvWarmingUp(telemetry))
            return $"Building first {telemetry.Settings.WindowSeconds:0.#} s short-term RR window";
        if (IsHrvWaitingForRr(telemetry))
            return "Connected but waiting for RR intervals";
        if (telemetry.IsTransportConnected)
            return "Last solved short-term HRV window expired";
        return "Transport offline";
    }

    private static string BuildHrvSupportPendingText(PolarHrvTelemetry telemetry)
    {
        if (IsHrvWarmingUp(telemetry))
            return "SDNN pending first solve | pNN50 pending";
        if (IsHrvWaitingForRr(telemetry))
            return "SDNN waiting for RR | pNN50 waiting for RR";
        return "SDNN -- | pNN50 --";
    }

    private static string BuildHrvRequirementLine(PolarHrvTelemetry telemetry)
    {
        float requiredWindowSeconds = telemetry.Settings.WindowSeconds * HrvFirstSolveCoverageRequirement01;
        return $"The first solve waits for at least {telemetry.MinimumSampleRequirement} accepted RR intervals and {requiredWindowSeconds:0.#} s of buffered RR history (99% of the configured {telemetry.Settings.WindowSeconds:0.#} s short-term window).";
    }

    private static string BuildHrvWarmupHint(PolarHrvTelemetry telemetry)
    {
        if (telemetry.HasTracking)
            return "Ready. RMSSD is live from the accepted RR window, and the supporting SDNN, pNN50, SD1, mean NN, and mean HR values are now updating with it.";
        if (IsHrvWarmingUp(telemetry))
            return "The tracker is filling a short-term RR window before it publishes the first RMSSD value. By default that window follows the five-minute reference standard discussed by Shaffer and Ginsberg (2017).";
        if (IsHrvWaitingForRr(telemetry))
            return "Connected. Once RR intervals arrive, the tracker will start filling the short-term HRV window immediately.";
        if (telemetry.IsTransportConnected)
            return $"The last solved window is stale. Fresh RR input inside the {telemetry.Settings.StaleTimeoutSeconds:0.#} s timeout will make HRV live again.";
        return "Connect a device and stream RR intervals to begin the short-term HRV window.";
    }

    private static string BuildHrvSampleText(PolarHrvTelemetry telemetry)
        => $"{telemetry.AcceptedWindowSampleCount}/{telemetry.MinimumSampleRequirement} ({telemetry.SampleRequirementProgress01 * 100f:0}%)";

    private static string BuildHrvCoverageText(PolarHrvTelemetry telemetry)
    {
        double requiredWindowSeconds = telemetry.Settings.WindowSeconds * HrvFirstSolveCoverageRequirement01;
        double bufferedWindowSeconds = telemetry.Settings.WindowSeconds * telemetry.WindowCoverage01;
        return $"{bufferedWindowSeconds:0.0}/{requiredWindowSeconds:0.0} s ({GetHrvCoverageProgress01(telemetry) * 100f:0}%)";
    }

    private static string BuildHrvRemainingText(PolarHrvTelemetry telemetry)
    {
        if (telemetry.HasTracking)
            return "Short-term HRV is live. Keep RR input clean if you want the rolling short-term window to stay representative.";

        if (IsHrvWarmingUp(telemetry))
        {
            double requiredWindowSeconds = telemetry.Settings.WindowSeconds * HrvFirstSolveCoverageRequirement01;
            double bufferedWindowSeconds = telemetry.Settings.WindowSeconds * telemetry.WindowCoverage01;
            double remainingWindowSeconds = Math.Max(0d, requiredWindowSeconds - bufferedWindowSeconds);
            int remainingSamples = Math.Max(0, telemetry.MinimumSampleRequirement - telemetry.AcceptedWindowSampleCount);

            if (remainingSamples > 0)
                return $"Waiting for {remainingSamples} more accepted RR interval(s) and about {remainingWindowSeconds:0.#} s more buffered RR history.";

            return $"Waiting for about {remainingWindowSeconds:0.#} s more buffered RR history before the first short-term HRV solve.";
        }

        if (IsHrvWaitingForRr(telemetry))
            return "No RR intervals received yet. Once RR arrives, the tracker will begin filling the short-term HRV window.";

        if (telemetry.IsTransportConnected && telemetry.HasMetricsSample)
            return $"Last solve is stale after {telemetry.LastMetricsAgeSeconds:0.#} s. New RR input will refresh the short-term window.";

        return "No active transport.";
    }

    private static string BuildHrvStatusLine(PolarHrvTelemetry telemetry)
    {
        if (telemetry.HasTracking)
            return $"Short-term HRV live with a {telemetry.Settings.WindowSeconds:0.#} s RR window";
        if (IsHrvWarmingUp(telemetry))
            return $"Warming up: {BuildHrvCoverageText(telemetry)} buffered, {BuildHrvSampleText(telemetry)} accepted RR";
        if (IsHrvWaitingForRr(telemetry))
            return $"Connected and waiting for RR intervals to start the {telemetry.Settings.WindowSeconds:0.#} s HRV window";
        if (telemetry.IsTransportConnected)
            return $"Short-term HRV stale after {telemetry.LastMetricsAgeSeconds:0.#} s without fresh RR input";
        return "Short-term HRV tracker is offline";
    }

    private void SetHrvStatus(string text, string brushKey)
    {
        if (_hrvWindow == null)
            return;

        _hrvWindow.StatusTextBlock.Text = text;
        _hrvWindow.StatusTextBlock.Foreground = ResourceBrush(brushKey);
    }

    private void LoadHrvSettingsToUi(PolarHrvSettings settings)
    {
        if (_hrvWindow == null)
            return;

        _hrvWindow.MinimumRrSamplesBoxElement.Text = settings.MinimumRrSamples.ToString(CultureInfo.InvariantCulture);
        _hrvWindow.WindowSecondsBoxElement.Text = settings.WindowSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        _hrvWindow.StaleTimeoutBoxElement.Text = settings.StaleTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private bool TryReadHrvSettingsFromUi(PolarHrvSettings baseSettings, out PolarHrvSettings settings, out string error)
    {
        settings = baseSettings;
        error = string.Empty;

        if (_hrvWindow == null)
            return false;

        if (!TryReadInt(_hrvWindow.MinimumRrSamplesBoxElement, "Min RR samples", 8, 4096, out int minimumRrSamples, out error) ||
            !TryReadFloat(_hrvWindow.WindowSecondsBoxElement, "Window seconds", 30f, 600f, out float windowSeconds, out error) ||
            !TryReadFloat(_hrvWindow.StaleTimeoutBoxElement, "Stale timeout", 0.1f, 120f, out float staleTimeout, out error))
        {
            return false;
        }

        settings = (baseSettings with
        {
            MinimumRrSamples = minimumRrSamples,
            WindowSeconds = windowSeconds,
            StaleTimeoutSeconds = staleTimeout,
        }).Clamp();

        return true;
    }

    private void OnHrvApplyTuningRequested(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        DeviceHrvState state = GetOrCreateHrvState(_selectedAddress);
        PolarHrvSettings baseSettings = state.HasTelemetry ? state.LastTelemetry.Settings : state.Tracker.GetTelemetry().Settings;
        if (!TryReadHrvSettingsFromUi(baseSettings, out PolarHrvSettings settings, out string error))
        {
            SetHrvStatus(error, "SignalRedBrush");
            return;
        }

        state.Tracker.ApplySettings(settings, resetTracker: true);
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearHrvHistory(_selectedAddress);
        CaptureHrvTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildHrvChart();
        RebuildTelemetrySummaryCharts();
        AddHrvLog(_selectedAddress, "tuning applied and tracker reset", state);
        SetHrvStatus("Tuning applied", "FocusBlueBrush");
        UpdateHrvPanel(_selectedAddress);
    }

    private void OnHrvRestoreDefaultsRequested(object? sender, EventArgs e)
    {
        LoadHrvSettingsToUi(AppHrvDefaults);
        if (string.IsNullOrWhiteSpace(_selectedAddress))
        {
            SetHrvStatus("Defaults loaded", "FocusBlueBrush");
            return;
        }

        DeviceHrvState state = GetOrCreateHrvState(_selectedAddress);
        state.Tracker.ApplySettings(AppHrvDefaults, resetTracker: true);
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearHrvHistory(_selectedAddress);
        CaptureHrvTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildHrvChart();
        RebuildTelemetrySummaryCharts();
        AddHrvLog(_selectedAddress, "defaults restored and tracker reset", state);
        SetHrvStatus("Defaults restored", "FocusBlueBrush");
        UpdateHrvPanel(_selectedAddress);
    }

    private void OnHrvResetTrackerRequested(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        DeviceHrvState state = GetOrCreateHrvState(_selectedAddress);
        state.Tracker.Reset();
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearHrvHistory(_selectedAddress);
        CaptureHrvTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildHrvChart();
        RebuildTelemetrySummaryCharts();
        AddHrvLog(_selectedAddress, "tracker reset", state);
        SetHrvStatus("Tracker reset", "GraphiteBrush");
        UpdateHrvPanel(_selectedAddress);
    }

    private void ClearHrvHistory(string address)
    {
        if (_chartStates.TryGetValue(address, out DeviceChartState? chartState))
            chartState.HrvRmssdValues.Clear();
    }

    private void SeedPreviewHrvState((string Address, string Name, string Status)[] previewDevices)
    {
        PolarHrvSettings previewSettings = (AppHrvDefaults with
        {
            MinimumRrSamples = 32,
            WindowSeconds = 120f,
            StaleTimeoutSeconds = 6f,
        }).Clamp();

        for (int deviceIndex = 0; deviceIndex < previewDevices.Length; deviceIndex++)
        {
            (string address, _, string status) = previewDevices[deviceIndex];
            DeviceHrvState state = GetOrCreateHrvState(address);
            state.Tracker.ApplySettings(previewSettings, resetTracker: true);
            state.Tracker.SetTransportConnected(!string.IsNullOrWhiteSpace(status));
            state.HasTelemetry = false;

            if (string.IsNullOrWhiteSpace(status))
            {
                CaptureHrvTelemetry(address, state, pushChartValue: true);
                AddHrvLog(address, "preview short-term HRV tracker idle", state);
                continue;
            }

            double elapsed = 0d;
            double modulationHz = 0.085d + (deviceIndex * 0.008d);
            float centerIbiMs = 910f + (deviceIndex * 22f);
            float amplitudeMs = 76f - (deviceIndex * 10f);
            while (elapsed < 220d)
            {
                double phase = elapsed * modulationHz * Math.PI * 2.0;
                float ibiMs = centerIbiMs + (float)(Math.Sin(phase) * amplitudeMs) + (float)(Math.Cos(phase * 0.5d) * 8d);
                state.Tracker.SubmitRrInterval(ibiMs);
                elapsed += ibiMs / 1000.0;
                CaptureHrvTelemetry(address, state, pushChartValue: true);
            }

            AddHrvLog(address, "preview short-term HRV tracker armed", state);
        }
    }
}
