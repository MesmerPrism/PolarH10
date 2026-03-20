using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PolarH10.Protocol;

namespace PolarH10.App;

public partial class MainWindow
{
    private static readonly PolarCoherenceSettings AppCoherenceDefaults = PolarCoherenceSettings.CreateDefault();
    private const float CoherenceFirstSolveCoverageRequirement01 = 0.99f;

    private sealed class DeviceCoherenceState
    {
        public PolarCoherenceTracker Tracker { get; } = new(AppCoherenceDefaults);
        public bool HasTelemetry;
        public PolarCoherenceTelemetry LastTelemetry;
        public readonly List<float> CoherenceValues = [];
        public readonly List<float> ConfidenceValues = [];
        public readonly List<string> LogLines = [];
        public bool HasPlottedValue;
        public float LastPlottedCoherence;
        public float LastPlottedConfidence;

        public void ClearSeries()
        {
            CoherenceValues.Clear();
            ConfidenceValues.Clear();
            HasPlottedValue = false;
            LastPlottedCoherence = 0f;
            LastPlottedConfidence = 0f;
        }
    }

    private readonly Dictionary<string, DeviceCoherenceState> _coherenceStates = new(StringComparer.OrdinalIgnoreCase);
    private CoherenceWindow? _coherenceWindow;
    private WaveformChart _coherenceChart = null!;
    private int _coherenceValueSeries;
    private int _coherenceConfidenceSeries;
    private string? _coherenceChartAddress;
    private string? _coherenceEditorAddress;

    private void InitializeCoherenceChart()
    {
        RebuildCoherenceChart();
    }

    private DeviceCoherenceState GetOrCreateCoherenceState(string address)
    {
        if (!_coherenceStates.TryGetValue(address, out DeviceCoherenceState? state))
        {
            state = new DeviceCoherenceState();
            _coherenceStates[address] = state;
        }

        return state;
    }

    private void RemoveCoherenceState(string address)
    {
        _coherenceStates.Remove(address);
        if (string.Equals(_coherenceChartAddress, address, StringComparison.OrdinalIgnoreCase))
            _coherenceChartAddress = null;
        if (string.Equals(_coherenceEditorAddress, address, StringComparison.OrdinalIgnoreCase))
            _coherenceEditorAddress = null;
    }

    private void RefreshCoherenceTrackers()
    {
        foreach ((string address, DeviceCoherenceState state) in _coherenceStates)
        {
            state.Tracker.Advance();
            CaptureCoherenceTelemetry(address, state, pushChartValue: false);
        }
    }

    private void RebuildCoherenceChart()
    {
        _coherenceChart = CreateChart("Coherence", _coherenceChartAxisOptions);
        _coherenceValueSeries = _coherenceChart.AddSeries("Coherence", FocusBlue, 360);
        _coherenceConfidenceSeries = _coherenceChart.AddSeries("Confidence", TelemetryGreen, 360);

        if (!string.IsNullOrWhiteSpace(_selectedAddress) &&
            _coherenceStates.TryGetValue(_selectedAddress, out DeviceCoherenceState? state))
        {
            ReplayCoherenceSeries(state);
            _coherenceChartAddress = _selectedAddress;
        }
        else
        {
            _coherenceChartAddress = null;
        }

        if (_coherenceWindow != null)
        {
            SetChartHostContent(_coherenceWindow.ChartHostElement, _coherenceChart, _coherenceChartAxisOptions);
            _coherenceChart.Refresh();
        }
    }

    private void ReplayCoherenceSeries(DeviceCoherenceState state)
    {
        foreach (float value in state.CoherenceValues)
            _coherenceChart.Push(_coherenceValueSeries, value);
        foreach (float value in state.ConfidenceValues)
            _coherenceChart.Push(_coherenceConfidenceSeries, value);
    }

    private void CaptureCoherenceTelemetry(string address, DeviceCoherenceState state, bool pushChartValue)
    {
        PolarCoherenceTelemetry telemetry = state.Tracker.GetTelemetry();
        if ((pushChartValue && telemetry.HasCoherenceSample) || ShouldAppendCoherenceValue(state, telemetry))
            AppendCoherenceValue(address, state, telemetry);

        if (state.HasTelemetry)
            LogCoherenceTransitions(address, state.LastTelemetry, telemetry, state);

        state.LastTelemetry = telemetry;
        state.HasTelemetry = true;

        if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
            UpdateCoherencePanel(address);
    }

    private static bool ShouldAppendCoherenceValue(DeviceCoherenceState state, PolarCoherenceTelemetry telemetry)
    {
        if (!telemetry.HasCoherenceSample)
            return false;

        if (!state.HasPlottedValue)
            return true;

        return Math.Abs(state.LastPlottedCoherence - telemetry.CurrentCoherence01) >= 0.0005f ||
               Math.Abs(state.LastPlottedConfidence - telemetry.Confidence01) >= 0.005f ||
               telemetry.TrackingState != state.LastTelemetry.TrackingState;
    }

    private void AppendCoherenceValue(string address, DeviceCoherenceState state, PolarCoherenceTelemetry telemetry)
    {
        AppendRolling(state.CoherenceValues, telemetry.CurrentCoherence01);
        AppendRolling(state.ConfidenceValues, telemetry.Confidence01);
        state.HasPlottedValue = true;
        state.LastPlottedCoherence = telemetry.CurrentCoherence01;
        state.LastPlottedConfidence = telemetry.Confidence01;

        DeviceChartState chartState = GetOrCreateChartState(address);
        AppendRolling(chartState.CoherenceValues, telemetry.CurrentCoherence01);
        AppendRolling(chartState.CoherenceConfidenceValues, telemetry.Confidence01);
        PushMetricSampleToTelemetryCharts(address, TelemetryMetric.Coherence, telemetry.CurrentCoherence01);
        PushMetricSampleToTelemetryCharts(address, TelemetryMetric.CoherenceConfidence, telemetry.Confidence01);

        if (!string.IsNullOrWhiteSpace(_selectedAddress) &&
            string.Equals(_selectedAddress, _coherenceChartAddress, StringComparison.OrdinalIgnoreCase))
        {
            _coherenceChart.Push(_coherenceValueSeries, telemetry.CurrentCoherence01);
            _coherenceChart.Push(_coherenceConfidenceSeries, telemetry.Confidence01);
        }
    }

    private void LogCoherenceTransitions(
        string address,
        PolarCoherenceTelemetry previous,
        PolarCoherenceTelemetry current,
        DeviceCoherenceState state)
    {
        if (previous.IsTransportConnected != current.IsTransportConnected)
            AddCoherenceLog(address, current.IsTransportConnected ? "transport connected" : "transport disconnected", state);

        if (!previous.HasReceivedAnyRrSample && current.HasReceivedAnyRrSample)
            AddCoherenceLog(address, "RR input detected", state);

        if (previous.TrackingState != current.TrackingState)
        {
            AddCoherenceLog(
                address,
                current.TrackingState switch
                {
                    PolarCoherenceTrackingState.Tracking => "coherence tracking ready",
                    PolarCoherenceTrackingState.Stale when IsCoherenceWaitingForRr(current) => "coherence waiting for RR input",
                    PolarCoherenceTrackingState.Stale when IsCoherenceWarmingUp(current) => "coherence warmup active",
                    PolarCoherenceTrackingState.Stale => "coherence tracking stale",
                    _ => "coherence unavailable",
                },
                state);
        }

        if (!previous.HasCoherenceSample && current.HasCoherenceSample)
            AddCoherenceLog(address, "first coherence window solved", state);
    }

    private void AddCoherenceLog(string address, string message, DeviceCoherenceState? state = null)
    {
        state ??= GetOrCreateCoherenceState(address);
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        state.LogLines.Add(line);
        if (state.LogLines.Count > 250)
            state.LogLines.RemoveAt(0);

        if (_coherenceWindow != null &&
            _selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
        {
            _coherenceWindow.LogListElement.Items.Add(line);
            if (_coherenceWindow.LogListElement.Items.Count > 250)
                _coherenceWindow.LogListElement.Items.RemoveAt(0);
            _coherenceWindow.LogListElement.ScrollIntoView(_coherenceWindow.LogListElement.Items[^1]);
        }
    }

    private void RefreshCoherenceLogList(DeviceCoherenceState state)
    {
        if (_coherenceWindow == null)
            return;

        _coherenceWindow.LogListElement.Items.Clear();
        foreach (string line in state.LogLines)
            _coherenceWindow.LogListElement.Items.Add(line);

        if (_coherenceWindow.LogListElement.Items.Count > 0)
            _coherenceWindow.LogListElement.ScrollIntoView(_coherenceWindow.LogListElement.Items[^1]);
    }

    private CoherenceWindow EnsureCoherenceWindow()
    {
        if (_coherenceWindow != null)
            return _coherenceWindow;

        _coherenceWindow = new CoherenceWindow();
        CoherenceTab.Content = DetachWindowContent(_coherenceWindow);
        _coherenceWindow.ApplyTuningRequested += OnCoherenceApplyTuningRequested;
        _coherenceWindow.RestoreDefaultsRequested += OnCoherenceRestoreDefaultsRequested;
        _coherenceWindow.ResetTrackerRequested += OnCoherenceResetTrackerRequested;

        RebuildCoherenceChart();
        UpdateCoherencePanel(_selectedAddress);
        return _coherenceWindow;
    }

    private void EnsureCoherenceEditorLoaded(string? address)
    {
        if (_coherenceWindow == null || string.IsNullOrWhiteSpace(address))
            return;

        if (string.Equals(_coherenceEditorAddress, address, StringComparison.OrdinalIgnoreCase))
            return;

        DeviceCoherenceState state = GetOrCreateCoherenceState(address);
        PolarCoherenceSettings settings = state.HasTelemetry ? state.LastTelemetry.Settings : state.Tracker.GetTelemetry().Settings;
        LoadCoherenceSettingsToUi(settings);
        RefreshCoherenceLogList(state);
        _coherenceEditorAddress = address;
    }

    private void UpdateCoherencePanel(string? address)
    {
        if (_coherenceWindow == null)
            return;

        if (string.IsNullOrWhiteSpace(address))
        {
            _coherenceWindow.SelectedDeviceTextBlock.Text = "No device selected";
            _coherenceWindow.SummaryTextBlock.Text = "RR-derived coherence uses The Coherent Heart peak-window method and also exposes the Astral-compatible normalized score used by the app.";
            _coherenceWindow.CoherenceValueTextBlock.Text = "--";
            _coherenceWindow.CoherenceStateValueTextBlock.Text = "Unavailable";
            _coherenceWindow.CoherenceTrackingValueTextBlock.Text = "Awaiting device selection";
            _coherenceWindow.CoherenceConfidenceValueTextBlock.Text = "Confidence --";
            _coherenceWindow.RequirementTextBlock.Text = "First valid value requires RR stabilization and nearly a full RR history window.";
            _coherenceWindow.WarmupHintTextBlock.Text = "Select a device to inspect coherence readiness.";
            _coherenceWindow.StabilizationProgressBar.Value = 0d;
            _coherenceWindow.StabilizationProgressTextBlock.Text = "--";
            _coherenceWindow.CoverageProgressBar.Value = 0d;
            _coherenceWindow.CoverageProgressTextBlock.Text = "--";
            _coherenceWindow.RemainingTextBlock.Text = "--";
            _coherenceWindow.TrackingTextBlock.Text = "--";
            _coherenceWindow.LastRrTextBlock.Text = "--";
            _coherenceWindow.HeartbeatTextBlock.Text = "--";
            _coherenceWindow.SampleCountTextBlock.Text = "--";
            _coherenceWindow.CoverageTextBlock.Text = "--";
            _coherenceWindow.StabilizationTextBlock.Text = "--";
            _coherenceWindow.LastUpdateTextBlock.Text = "--";
            _coherenceWindow.PeakFrequencyTextBlock.Text = "--";
            _coherenceWindow.PeakBandPowerTextBlock.Text = "--";
            _coherenceWindow.TotalPowerTextBlock.Text = "--";
            _coherenceWindow.PaperRatioTextBlock.Text = "--";
            _coherenceWindow.NormalizedScoreTextBlock.Text = "--";
            SetCoherenceStatus("No device selected", "GraphiteBrush");
            if (_coherenceChartAddress != null)
                RebuildCoherenceChart();
            return;
        }

        DeviceCoherenceState state = GetOrCreateCoherenceState(address);
        EnsureCoherenceEditorLoaded(address);

        if (!string.Equals(_coherenceChartAddress, address, StringComparison.OrdinalIgnoreCase))
            RebuildCoherenceChart();

        PolarCoherenceTelemetry telemetry = state.HasTelemetry ? state.LastTelemetry : state.Tracker.GetTelemetry();
        _coherenceWindow.Title = $"Polar H10 // Coherence // {CompactDisplayName(address)}";
        _coherenceWindow.SelectedDeviceTextBlock.Text = DisplayName(address);
        _coherenceWindow.SummaryTextBlock.Text = "The headline and chart use the Astral-compatible normalized score; the detail panel also shows the raw paper-defined ratio from The Coherent Heart.";
        _coherenceWindow.CoherenceValueTextBlock.Text = telemetry.HasCoherenceSample ? $"{telemetry.CurrentCoherence01:0.00}" : "--";
        _coherenceWindow.CoherenceStateValueTextBlock.Text = FormatCoherenceDisplayState(telemetry);
        _coherenceWindow.CoherenceTrackingValueTextBlock.Text = BuildCoherenceTrackingLine(telemetry);
        _coherenceWindow.CoherenceConfidenceValueTextBlock.Text = BuildCoherenceConfidenceLine(telemetry);
        _coherenceWindow.CoherenceValueTextBlock.Foreground = ResourceBrush(GetCoherenceAccentBrushKey(telemetry));
        _coherenceWindow.RequirementTextBlock.Text = BuildCoherenceRequirementLine(telemetry);
        _coherenceWindow.WarmupHintTextBlock.Text = BuildCoherenceWarmupHint(telemetry);
        _coherenceWindow.StabilizationProgressBar.Value = telemetry.StabilizationProgress01;
        _coherenceWindow.StabilizationProgressBar.Foreground = ResourceBrush(GetCoherenceStatusBrushKey(telemetry));
        _coherenceWindow.StabilizationProgressTextBlock.Text = BuildCoherenceStabilizationText(telemetry);
        _coherenceWindow.CoverageProgressBar.Value = GetCoherenceCoverageProgress01(telemetry);
        _coherenceWindow.CoverageProgressBar.Foreground = ResourceBrush(GetCoherenceStatusBrushKey(telemetry));
        _coherenceWindow.CoverageProgressTextBlock.Text = BuildCoherenceCoverageText(telemetry);
        _coherenceWindow.RemainingTextBlock.Text = BuildCoherenceRemainingText(telemetry);

        _coherenceWindow.TrackingTextBlock.Text = FormatCoherenceDisplayState(telemetry);
        _coherenceWindow.LastRrTextBlock.Text = telemetry.HasReceivedAnyRrSample ? $"{telemetry.CurrentHeartbeatIbiMs:0} ms" : "No RR";
        _coherenceWindow.HeartbeatTextBlock.Text = telemetry.HasReceivedAnyRrSample ? $"{telemetry.CurrentHeartbeatBpm:0.0} BPM" : "No RR";
        _coherenceWindow.SampleCountTextBlock.Text = $"{telemetry.RrSampleCount} seen (gate {telemetry.Settings.MinimumIbiSamples}+)";
        _coherenceWindow.CoverageTextBlock.Text = BuildCoherenceCoverageText(telemetry);
        _coherenceWindow.StabilizationTextBlock.Text = BuildCoherenceStabilizationText(telemetry);
        _coherenceWindow.LastUpdateTextBlock.Text = telemetry.HasCoherenceSample ? $"{telemetry.LastCoherenceAgeSeconds:0.00} s ago" : "Pending first solve";
        _coherenceWindow.PeakFrequencyTextBlock.Text = telemetry.HasCoherenceSample ? $"{telemetry.PeakFrequencyHz:0.000} Hz" : "Pending first solve";
        _coherenceWindow.PeakBandPowerTextBlock.Text = telemetry.HasCoherenceSample ? telemetry.PeakBandPower.ToString("0.####", CultureInfo.InvariantCulture) : "Pending first solve";
        _coherenceWindow.TotalPowerTextBlock.Text = telemetry.HasCoherenceSample ? telemetry.TotalBandPower.ToString("0.####", CultureInfo.InvariantCulture) : "Pending first solve";
        _coherenceWindow.PaperRatioTextBlock.Text = telemetry.HasCoherenceSample ? telemetry.PaperCoherenceRatio.ToString("0.###", CultureInfo.InvariantCulture) : "Pending first solve";
        _coherenceWindow.NormalizedScoreTextBlock.Text = telemetry.HasCoherenceSample ? telemetry.NormalizedCoherence01.ToString("0.00", CultureInfo.InvariantCulture) : "Pending first solve";

        SetCoherenceStatus(
            BuildCoherenceStatusLine(telemetry),
            GetCoherenceStatusBrushKey(telemetry));
    }

    private static bool IsCoherenceWaitingForRr(PolarCoherenceTelemetry telemetry)
        => telemetry.IsTransportConnected && !telemetry.HasReceivedAnyRrSample;

    private static bool IsCoherenceWarmingUp(PolarCoherenceTelemetry telemetry)
        => telemetry.IsTransportConnected && telemetry.HasReceivedAnyRrSample && !telemetry.HasCoherenceSample;

    private static string FormatCoherenceDisplayState(PolarCoherenceTelemetry telemetry) => telemetry switch
    {
        { HasTracking: true } => "Tracking",
        _ when IsCoherenceWarmingUp(telemetry) => "Warming up",
        _ when IsCoherenceWaitingForRr(telemetry) => "Waiting for RR",
        { IsTransportConnected: true, HasCoherenceSample: true } => "Stale",
        _ => "Unavailable",
    };

    private static string GetCoherenceAccentBrushKey(PolarCoherenceTelemetry telemetry) => telemetry switch
    {
        { HasTracking: true } => "TelemetryGreenBrush",
        _ when IsCoherenceWarmingUp(telemetry) => "FocusBlueBrush",
        { IsTransportConnected: true, HasCoherenceSample: true } => "SafetyOrangeBrush",
        _ => "GraphiteBrush",
    };

    private static string GetCoherenceStatusBrushKey(PolarCoherenceTelemetry telemetry) => telemetry switch
    {
        { HasTracking: true } => "TelemetryGreenBrush",
        _ when IsCoherenceWarmingUp(telemetry) => "FocusBlueBrush",
        _ when IsCoherenceWaitingForRr(telemetry) => "FocusBlueBrush",
        { IsTransportConnected: true, HasCoherenceSample: true } => "SafetyOrangeBrush",
        _ => "GraphiteBrush",
    };

    private static float GetCoherenceCoverageProgress01(PolarCoherenceTelemetry telemetry)
        => Math.Clamp(telemetry.WindowCoverage01 / CoherenceFirstSolveCoverageRequirement01, 0f, 1f);

    private static string BuildCoherenceTrackingLine(PolarCoherenceTelemetry telemetry)
    {
        if (telemetry.HasTracking)
            return "Coherence live";
        if (IsCoherenceWarmingUp(telemetry))
            return $"Building first {telemetry.Settings.CoherenceWindowSeconds:0.#} s RR solve";
        if (IsCoherenceWaitingForRr(telemetry))
            return "Connected but waiting for RR intervals";
        if (telemetry.IsTransportConnected)
            return "Last solved window expired";
        return "Transport offline";
    }

    private static string BuildCoherenceConfidenceLine(PolarCoherenceTelemetry telemetry)
    {
        if (telemetry.HasCoherenceSample)
            return $"Confidence {telemetry.Confidence01:0.00}";
        if (IsCoherenceWarmingUp(telemetry))
            return "Confidence pending first solve";
        if (IsCoherenceWaitingForRr(telemetry))
            return "Confidence waiting for RR input";
        return "Confidence --";
    }

    private static string BuildCoherenceRequirementLine(PolarCoherenceTelemetry telemetry)
    {
        float requiredWindowSeconds = telemetry.Settings.CoherenceWindowSeconds * CoherenceFirstSolveCoverageRequirement01;
        return $"First valid value requires {telemetry.StabilizationRequiredCount} consecutive valid RR intervals, at least {telemetry.Settings.MinimumIbiSamples} RR samples, and {requiredWindowSeconds:0.#} s of buffered RR history (99% of the configured {telemetry.Settings.CoherenceWindowSeconds:0.#} s window).";
    }

    private static string BuildCoherenceWarmupHint(PolarCoherenceTelemetry telemetry)
    {
        if (telemetry.HasTracking)
            return "Ready. Fresh RR updates are now driving the live coherence score and the raw paper ratio.";
        if (IsCoherenceWarmingUp(telemetry))
            return "The tracker is buffering RR history for the first spectral solve. The page stays blank until that first solve is valid.";
        if (IsCoherenceWaitingForRr(telemetry))
            return "Connected. Once RR intervals arrive, the tracker will start buffering the first coherence window immediately.";
        if (telemetry.IsTransportConnected)
            return $"The last solved window is stale. Fresh RR input inside the {telemetry.Settings.StaleTimeoutSeconds:0.#} s timeout will make coherence live again.";
        return "Connect a device and stream RR intervals to begin the coherence window.";
    }

    private static string BuildCoherenceCoverageText(PolarCoherenceTelemetry telemetry)
    {
        double requiredWindowSeconds = telemetry.Settings.CoherenceWindowSeconds * CoherenceFirstSolveCoverageRequirement01;
        double bufferedWindowSeconds = telemetry.Settings.CoherenceWindowSeconds * telemetry.WindowCoverage01;
        return $"{bufferedWindowSeconds:0.0}/{requiredWindowSeconds:0.0} s ({GetCoherenceCoverageProgress01(telemetry) * 100f:0}%)";
    }

    private static string BuildCoherenceStabilizationText(PolarCoherenceTelemetry telemetry)
        => $"{telemetry.ConsecutiveValidCount}/{telemetry.StabilizationRequiredCount} ({telemetry.StabilizationProgress01 * 100f:0}%)";

    private static string BuildCoherenceRemainingText(PolarCoherenceTelemetry telemetry)
    {
        if (telemetry.HasTracking)
            return "First solve complete. Fresh RR input is currently keeping the coherence window live.";

        if (IsCoherenceWarmingUp(telemetry))
        {
            double requiredWindowSeconds = telemetry.Settings.CoherenceWindowSeconds * CoherenceFirstSolveCoverageRequirement01;
            double bufferedWindowSeconds = telemetry.Settings.CoherenceWindowSeconds * telemetry.WindowCoverage01;
            double remainingWindowSeconds = Math.Max(0d, requiredWindowSeconds - bufferedWindowSeconds);
            int remainingStabilization = Math.Max(0, telemetry.StabilizationRequiredCount - telemetry.ConsecutiveValidCount);

            if (remainingStabilization > 0)
                return $"Waiting for {remainingStabilization} more consecutive valid RR interval(s) and about {remainingWindowSeconds:0.#} s more buffered RR history.";

            return $"Waiting for about {remainingWindowSeconds:0.#} s more buffered RR history before the first valid coherence solve.";
        }

        if (IsCoherenceWaitingForRr(telemetry))
            return "No RR intervals received yet. Once RR arrives, the tracker will begin filling the coherence window.";

        if (telemetry.IsTransportConnected && telemetry.HasCoherenceSample)
            return $"Last solve is stale after {telemetry.LastCoherenceAgeSeconds:0.#} s. New RR input will refresh the solved window.";

        return "No active transport.";
    }

    private static string BuildCoherenceStatusLine(PolarCoherenceTelemetry telemetry)
    {
        if (telemetry.HasTracking)
            return $"Coherence live with a {telemetry.Settings.CoherenceWindowSeconds:0.#} s RR window";
        if (IsCoherenceWarmingUp(telemetry))
            return $"Warming up: {BuildCoherenceCoverageText(telemetry)} buffered, {BuildCoherenceStabilizationText(telemetry)} valid RR run";
        if (IsCoherenceWaitingForRr(telemetry))
            return $"Connected and waiting for RR intervals to start the {telemetry.Settings.CoherenceWindowSeconds:0.#} s window";
        if (telemetry.IsTransportConnected)
            return $"Coherence stale after {telemetry.LastCoherenceAgeSeconds:0.#} s without a fresh solve";
        return "Coherence tracker is offline";
    }

    private void SetCoherenceStatus(string text, string brushKey)
    {
        if (_coherenceWindow == null)
            return;

        _coherenceWindow.StatusTextBlock.Text = text;
        _coherenceWindow.StatusTextBlock.Foreground = ResourceBrush(brushKey);
    }

    private void LoadCoherenceSettingsToUi(PolarCoherenceSettings settings)
    {
        if (_coherenceWindow == null)
            return;

        _coherenceWindow.MinimumIbiSamplesBoxElement.Text = settings.MinimumIbiSamples.ToString(CultureInfo.InvariantCulture);
        _coherenceWindow.WindowSecondsBoxElement.Text = settings.CoherenceWindowSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        _coherenceWindow.SmoothingSpeedBoxElement.Text = settings.CoherenceSmoothingSpeed.ToString("0.###", CultureInfo.InvariantCulture);
        _coherenceWindow.StaleTimeoutBoxElement.Text = settings.StaleTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private bool TryReadCoherenceSettingsFromUi(PolarCoherenceSettings baseSettings, out PolarCoherenceSettings settings, out string error)
    {
        settings = baseSettings;
        error = string.Empty;

        if (_coherenceWindow == null)
            return false;

        if (!TryReadInt(_coherenceWindow.MinimumIbiSamplesBoxElement, "Min RR samples", 4, 4096, out int minimumIbiSamples, out error) ||
            !TryReadFloat(_coherenceWindow.WindowSecondsBoxElement, "Window seconds", 16f, 180f, out float windowSeconds, out error) ||
            !TryReadFloat(_coherenceWindow.SmoothingSpeedBoxElement, "Smoothing speed", 0f, 20f, out float smoothingSpeed, out error) ||
            !TryReadFloat(_coherenceWindow.StaleTimeoutBoxElement, "Stale timeout", 0.1f, 120f, out float staleTimeout, out error))
        {
            return false;
        }

        settings = (baseSettings with
        {
            MinimumIbiSamples = minimumIbiSamples,
            CoherenceWindowSeconds = windowSeconds,
            CoherenceSmoothingSpeed = smoothingSpeed,
            StaleTimeoutSeconds = staleTimeout,
        }).Clamp();

        return true;
    }

    private void OnCoherenceApplyTuningRequested(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        DeviceCoherenceState state = GetOrCreateCoherenceState(_selectedAddress);
        PolarCoherenceSettings baseSettings = state.HasTelemetry ? state.LastTelemetry.Settings : state.Tracker.GetTelemetry().Settings;
        if (!TryReadCoherenceSettingsFromUi(baseSettings, out PolarCoherenceSettings settings, out string error))
        {
            SetCoherenceStatus(error, "SignalRedBrush");
            return;
        }

        state.Tracker.ApplySettings(settings, resetTracker: true);
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearCoherenceHistory(_selectedAddress);
        CaptureCoherenceTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildCoherenceChart();
        RebuildTelemetrySummaryCharts();
        AddCoherenceLog(_selectedAddress, "tuning applied and tracker reset", state);
        SetCoherenceStatus("Tuning applied", "FocusBlueBrush");
        UpdateCoherencePanel(_selectedAddress);
    }

    private void OnCoherenceRestoreDefaultsRequested(object? sender, EventArgs e)
    {
        LoadCoherenceSettingsToUi(AppCoherenceDefaults);
        if (string.IsNullOrWhiteSpace(_selectedAddress))
        {
            SetCoherenceStatus("Defaults loaded", "FocusBlueBrush");
            return;
        }

        DeviceCoherenceState state = GetOrCreateCoherenceState(_selectedAddress);
        state.Tracker.ApplySettings(AppCoherenceDefaults, resetTracker: true);
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearCoherenceHistory(_selectedAddress);
        CaptureCoherenceTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildCoherenceChart();
        RebuildTelemetrySummaryCharts();
        AddCoherenceLog(_selectedAddress, "defaults restored and tracker reset", state);
        SetCoherenceStatus("Defaults restored", "FocusBlueBrush");
        UpdateCoherencePanel(_selectedAddress);
    }

    private void OnCoherenceResetTrackerRequested(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        DeviceCoherenceState state = GetOrCreateCoherenceState(_selectedAddress);
        state.Tracker.Reset();
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearCoherenceHistory(_selectedAddress);
        CaptureCoherenceTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildCoherenceChart();
        RebuildTelemetrySummaryCharts();
        AddCoherenceLog(_selectedAddress, "tracker reset", state);
        SetCoherenceStatus("Tracker reset", "GraphiteBrush");
        UpdateCoherencePanel(_selectedAddress);
    }

    private void ClearCoherenceHistory(string address)
    {
        if (_chartStates.TryGetValue(address, out DeviceChartState? chartState))
        {
            chartState.CoherenceValues.Clear();
            chartState.CoherenceConfidenceValues.Clear();
        }
    }

    private void OnOpenCoherenceWindowClick(object sender, RoutedEventArgs e)
    {
        EnsureCoherenceWindow();
        DeviceTabControl.SelectedItem = CoherenceTab;
        UpdateCoherencePanel(_selectedAddress);
    }

    private void SeedPreviewCoherenceState((string Address, string Name, string Status)[] previewDevices)
    {
        PolarCoherenceSettings previewSettings = (AppCoherenceDefaults with
        {
            MinimumIbiSamples = 12,
            CoherenceWindowSeconds = 32f,
            CoherenceSmoothingSpeed = 0f,
        }).Clamp();

        for (int deviceIndex = 0; deviceIndex < previewDevices.Length; deviceIndex++)
        {
            (string address, _, string status) = previewDevices[deviceIndex];
            DeviceCoherenceState state = GetOrCreateCoherenceState(address);
            state.Tracker.ApplySettings(previewSettings, resetTracker: true);
            state.Tracker.SetTransportConnected(!string.IsNullOrWhiteSpace(status));
            state.HasTelemetry = false;

            if (string.IsNullOrWhiteSpace(status))
            {
                CaptureCoherenceTelemetry(address, state, pushChartValue: true);
                AddCoherenceLog(address, "preview coherence tracker idle", state);
                continue;
            }

            double elapsed = 0d;
            double resonanceHz = 0.098d + (deviceIndex * 0.008d);
            float centerIbiMs = 830f + (deviceIndex * 12f);
            float amplitudeMs = 72f - (deviceIndex * 14f);
            while (elapsed < 72d)
            {
                double phase = elapsed * resonanceHz * Math.PI * 2.0;
                float ibiMs = centerIbiMs + (float)(Math.Sin(phase) * amplitudeMs);
                state.Tracker.SubmitRrInterval(ibiMs);
                elapsed += ibiMs / 1000.0;
                CaptureCoherenceTelemetry(address, state, pushChartValue: true);
            }

            AddCoherenceLog(address, "preview coherence tracker armed", state);
        }
    }
}
