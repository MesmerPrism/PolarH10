using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PolarH10.Protocol;

namespace PolarH10.App;

public partial class MainWindow
{
    private static readonly PolarBreathingDynamicsSettings AppBreathingDynamicsDefaults = PolarBreathingDynamicsSettings.CreateDefault();

    private sealed class DeviceBreathingDynamicsState
    {
        public PolarBreathingDynamicsTracker Tracker { get; } = new(AppBreathingDynamicsDefaults);
        public bool HasTelemetry;
        public PolarBreathingDynamicsTelemetry LastTelemetry;
        public readonly List<float> IntervalEntropyValues = [];
        public readonly List<float> AmplitudeEntropyValues = [];
        public readonly List<string> LogLines = [];
        public bool HasPlottedValue;
        public float LastPlottedIntervalEntropy;
        public float LastPlottedAmplitudeEntropy;

        public void ClearSeries()
        {
            IntervalEntropyValues.Clear();
            AmplitudeEntropyValues.Clear();
            HasPlottedValue = false;
            LastPlottedIntervalEntropy = 0f;
            LastPlottedAmplitudeEntropy = 0f;
        }
    }

    private readonly Dictionary<string, DeviceBreathingDynamicsState> _breathingDynamicsStates = new(StringComparer.OrdinalIgnoreCase);
    private BreathingDynamicsWindow? _breathingDynamicsWindow;
    private WaveformChart _breathingDynamicsChart = null!;
    private int _breathingDynamicsIntervalSeries;
    private int _breathingDynamicsAmplitudeSeries;
    private string? _breathingDynamicsChartAddress;
    private string? _breathingDynamicsEditorAddress;

    private void InitializeBreathingDynamicsChart()
    {
        RebuildBreathingDynamicsChart();
    }

    private DeviceBreathingDynamicsState GetOrCreateBreathingDynamicsState(string address)
    {
        if (!_breathingDynamicsStates.TryGetValue(address, out DeviceBreathingDynamicsState? state))
        {
            state = new DeviceBreathingDynamicsState();
            _breathingDynamicsStates[address] = state;
        }

        return state;
    }

    private void RemoveBreathingDynamicsState(string address)
    {
        _breathingDynamicsStates.Remove(address);
        if (string.Equals(_breathingDynamicsChartAddress, address, StringComparison.OrdinalIgnoreCase))
            _breathingDynamicsChartAddress = null;
        if (string.Equals(_breathingDynamicsEditorAddress, address, StringComparison.OrdinalIgnoreCase))
            _breathingDynamicsEditorAddress = null;
    }

    private void ClearBreathingDynamicsStates()
    {
        _breathingDynamicsStates.Clear();
        _breathingDynamicsChartAddress = null;
        _breathingDynamicsEditorAddress = null;

        if (_breathingDynamicsWindow != null)
        {
            _breathingDynamicsWindow.LogListElement.Items.Clear();
            RebuildBreathingDynamicsChart();
        }
    }

    private void RefreshBreathingDynamicsTrackers()
    {
        foreach ((string address, DeviceBreathingDynamicsState state) in _breathingDynamicsStates)
        {
            state.Tracker.Advance();
            CaptureBreathingDynamicsTelemetry(address, state, pushChartValue: false);
        }
    }

    private void RebuildBreathingDynamicsChart()
    {
        _breathingDynamicsChart = CreateChart("Breath Variability", _breathingDynamicsChartAxisOptions);
        _breathingDynamicsIntervalSeries = _breathingDynamicsChart.AddSeries("Interval entropy", FocusBlue, 360);
        _breathingDynamicsAmplitudeSeries = _breathingDynamicsChart.AddSeries("Amplitude entropy", TelemetryGreen, 360);

        if (!string.IsNullOrWhiteSpace(_selectedAddress) &&
            _breathingDynamicsStates.TryGetValue(_selectedAddress, out DeviceBreathingDynamicsState? state))
        {
            ReplayBreathingDynamicsSeries(state);
            _breathingDynamicsChartAddress = _selectedAddress;
        }
        else
        {
            _breathingDynamicsChartAddress = null;
        }

        if (_breathingDynamicsWindow != null)
        {
            SetChartHostContent(_breathingDynamicsWindow.ChartHostElement, _breathingDynamicsChart, _breathingDynamicsChartAxisOptions);
            _breathingDynamicsChart.Refresh();
        }
    }

    private void ReplayBreathingDynamicsSeries(DeviceBreathingDynamicsState state)
    {
        foreach (float value in state.IntervalEntropyValues)
            _breathingDynamicsChart.Push(_breathingDynamicsIntervalSeries, value);
        foreach (float value in state.AmplitudeEntropyValues)
            _breathingDynamicsChart.Push(_breathingDynamicsAmplitudeSeries, value);
    }

    private void CaptureBreathingDynamicsTelemetry(string address, DeviceBreathingDynamicsState state, bool pushChartValue)
    {
        PolarBreathingDynamicsTelemetry telemetry = state.Tracker.GetTelemetry();
        bool resetHistory = state.HasTelemetry &&
            state.LastTelemetry.IntervalBreathCount > 0 &&
            telemetry.IntervalBreathCount == 0 &&
            telemetry.AmplitudeBreathCount == 0;
        if (resetHistory)
        {
            state.ClearSeries();
            ClearBreathingDynamicsHistory(address);
        }

        if (pushChartValue || ShouldAppendBreathingDynamicsValue(state, telemetry))
            AppendBreathingDynamicsValue(address, state, telemetry);

        if (state.HasTelemetry)
            LogBreathingDynamicsTransitions(address, state.LastTelemetry, telemetry, state);

        state.LastTelemetry = telemetry;
        state.HasTelemetry = true;

        if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
            UpdateBreathingDynamicsPanel(address);
    }

    private static bool ShouldAppendBreathingDynamicsValue(DeviceBreathingDynamicsState state, PolarBreathingDynamicsTelemetry telemetry)
    {
        if (!state.HasPlottedValue)
            return true;

        float currentIntervalEntropy = telemetry.IntervalHasEntropyMetrics ? telemetry.Interval.SampleEntropy : 0f;
        float currentAmplitudeEntropy = telemetry.AmplitudeHasEntropyMetrics ? telemetry.Amplitude.SampleEntropy : 0f;
        return Math.Abs(state.LastPlottedIntervalEntropy - currentIntervalEntropy) >= 0.005f ||
               Math.Abs(state.LastPlottedAmplitudeEntropy - currentAmplitudeEntropy) >= 0.005f ||
               telemetry.TrackingState != state.LastTelemetry.TrackingState ||
               telemetry.IntervalHasEntropyMetrics != state.LastTelemetry.IntervalHasEntropyMetrics ||
               telemetry.AmplitudeHasEntropyMetrics != state.LastTelemetry.AmplitudeHasEntropyMetrics;
    }

    private void AppendBreathingDynamicsValue(string address, DeviceBreathingDynamicsState state, PolarBreathingDynamicsTelemetry telemetry)
    {
        float intervalEntropy = telemetry.IntervalHasEntropyMetrics ? telemetry.Interval.SampleEntropy : 0f;
        float amplitudeEntropy = telemetry.AmplitudeHasEntropyMetrics ? telemetry.Amplitude.SampleEntropy : 0f;
        AppendRolling(state.IntervalEntropyValues, intervalEntropy);
        AppendRolling(state.AmplitudeEntropyValues, amplitudeEntropy);
        state.HasPlottedValue = true;
        state.LastPlottedIntervalEntropy = intervalEntropy;
        state.LastPlottedAmplitudeEntropy = amplitudeEntropy;

        DeviceChartState chartState = GetOrCreateChartState(address);
        AppendRolling(chartState.BreathIntervalEntropyValues, intervalEntropy);
        AppendRolling(chartState.BreathAmplitudeEntropyValues, amplitudeEntropy);
        PushMetricSampleToTelemetryCharts(address, TelemetryMetric.BreathIntervalEntropy, intervalEntropy);
        PushMetricSampleToTelemetryCharts(address, TelemetryMetric.BreathAmplitudeEntropy, amplitudeEntropy);

        if (!string.IsNullOrWhiteSpace(_selectedAddress) &&
            string.Equals(_selectedAddress, _breathingDynamicsChartAddress, StringComparison.OrdinalIgnoreCase))
        {
            _breathingDynamicsChart.Push(_breathingDynamicsIntervalSeries, intervalEntropy);
            _breathingDynamicsChart.Push(_breathingDynamicsAmplitudeSeries, amplitudeEntropy);
        }
    }

    private void LogBreathingDynamicsTransitions(
        string address,
        PolarBreathingDynamicsTelemetry previous,
        PolarBreathingDynamicsTelemetry current,
        DeviceBreathingDynamicsState state)
    {
        if (previous.IsTransportConnected != current.IsTransportConnected)
            AddBreathingDynamicsLog(address, current.IsTransportConnected ? "transport connected" : "transport disconnected", state);

        if (!previous.HasReceivedAnyWaveformSample && current.HasReceivedAnyWaveformSample)
            AddBreathingDynamicsLog(address, "base breathing waveform detected", state);

        if (previous.TrackingState != current.TrackingState)
        {
            AddBreathingDynamicsLog(
                address,
                current.TrackingState switch
                {
                    PolarBreathingDynamicsTrackingState.Tracking => "breath variability tracking live",
                    PolarBreathingDynamicsTrackingState.WaitingForCalibration => "waiting for breathing calibration",
                    PolarBreathingDynamicsTrackingState.WaitingForBreathingTracking => "waiting for stable breathing tracking",
                    PolarBreathingDynamicsTrackingState.Stale => "breath variability input stale",
                    _ => "breath variability unavailable",
                },
                state);
        }

        if (!previous.IntervalHasEntropyMetrics && current.IntervalHasEntropyMetrics)
            AddBreathingDynamicsLog(address, "interval entropy ready", state);
        if (!previous.AmplitudeHasEntropyMetrics && current.AmplitudeHasEntropyMetrics)
            AddBreathingDynamicsLog(address, "amplitude entropy ready", state);
    }

    private void AddBreathingDynamicsLog(string address, string message, DeviceBreathingDynamicsState? state = null)
    {
        state ??= GetOrCreateBreathingDynamicsState(address);
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        state.LogLines.Add(line);
        if (state.LogLines.Count > 250)
            state.LogLines.RemoveAt(0);

        if (_breathingDynamicsWindow != null &&
            _selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
        {
            _breathingDynamicsWindow.LogListElement.Items.Add(line);
            if (_breathingDynamicsWindow.LogListElement.Items.Count > 250)
                _breathingDynamicsWindow.LogListElement.Items.RemoveAt(0);
            _breathingDynamicsWindow.LogListElement.ScrollIntoView(_breathingDynamicsWindow.LogListElement.Items[^1]);
        }
    }

    private void RefreshBreathingDynamicsLogList(DeviceBreathingDynamicsState state)
    {
        if (_breathingDynamicsWindow == null)
            return;

        _breathingDynamicsWindow.LogListElement.Items.Clear();
        foreach (string line in state.LogLines)
            _breathingDynamicsWindow.LogListElement.Items.Add(line);

        if (_breathingDynamicsWindow.LogListElement.Items.Count > 0)
            _breathingDynamicsWindow.LogListElement.ScrollIntoView(_breathingDynamicsWindow.LogListElement.Items[^1]);
    }

    private BreathingDynamicsWindow EnsureBreathingDynamicsWindow()
    {
        if (_breathingDynamicsWindow != null)
            return _breathingDynamicsWindow;

        _breathingDynamicsWindow = new BreathingDynamicsWindow();
        DynamicsTab.Content = DetachWindowContent(_breathingDynamicsWindow);
        _breathingDynamicsWindow.ApplyTuningRequested += OnBreathingDynamicsApplyTuningRequested;
        _breathingDynamicsWindow.RestoreDefaultsRequested += OnBreathingDynamicsRestoreDefaultsRequested;
        _breathingDynamicsWindow.ResetTrackerRequested += OnBreathingDynamicsResetTrackerRequested;

        RebuildBreathingDynamicsChart();
        UpdateBreathingDynamicsPanel(_selectedAddress);
        return _breathingDynamicsWindow;
    }

    private void EnsureBreathingDynamicsEditorLoaded(string? address)
    {
        if (_breathingDynamicsWindow == null || string.IsNullOrWhiteSpace(address))
            return;

        if (string.Equals(_breathingDynamicsEditorAddress, address, StringComparison.OrdinalIgnoreCase))
            return;

        DeviceBreathingDynamicsState state = GetOrCreateBreathingDynamicsState(address);
        PolarBreathingDynamicsSettings settings = state.HasTelemetry ? state.LastTelemetry.Settings : state.Tracker.GetTelemetry().Settings;
        LoadBreathingDynamicsSettingsToUi(settings);
        RefreshBreathingDynamicsLogList(state);
        _breathingDynamicsEditorAddress = address;
    }

    private void UpdateBreathingDynamicsPanel(string? address)
    {
        if (_breathingDynamicsWindow == null)
            return;

        if (string.IsNullOrWhiteSpace(address))
        {
            _breathingDynamicsWindow.SelectedDeviceTextBlock.Text = "No device selected";
            _breathingDynamicsWindow.SummaryTextBlock.Text = "Breath variability tracks derived interval and amplitude series from the calibrated breathing waveform, then unlocks entropy as those series mature.";
            _breathingDynamicsWindow.IntervalEntropyValueTextBlock.Text = "--";
            _breathingDynamicsWindow.IntervalEntropyHintTextBlock.Text = "Waiting for enough derived breaths";
            _breathingDynamicsWindow.AmplitudeEntropyValueTextBlock.Text = "--";
            _breathingDynamicsWindow.AmplitudeEntropyHintTextBlock.Text = "Waiting for enough derived breaths";
            _breathingDynamicsWindow.RequirementTextBlock.Text = "Variability metrics need accepted interval and amplitude breath series; basic stats unlock first, then entropy as the derived series fills.";
            _breathingDynamicsWindow.WarmupHintTextBlock.Text = "Select a device, finish breathing calibration, and keep breathing long enough to accumulate derived breaths.";
            _breathingDynamicsWindow.MaturityProgressBar.Value = 0d;
            _breathingDynamicsWindow.MaturityProgressBar.Foreground = ResourceBrush("GraphiteBrush");
            _breathingDynamicsWindow.MaturityProgressTextBlock.Text = "--";
            _breathingDynamicsWindow.IntervalProgressBar.Value = 0d;
            _breathingDynamicsWindow.IntervalProgressBar.Foreground = ResourceBrush("GraphiteBrush");
            _breathingDynamicsWindow.IntervalProgressTextBlock.Text = "--";
            _breathingDynamicsWindow.AmplitudeProgressBar.Value = 0d;
            _breathingDynamicsWindow.AmplitudeProgressBar.Foreground = ResourceBrush("GraphiteBrush");
            _breathingDynamicsWindow.AmplitudeProgressTextBlock.Text = "--";
            _breathingDynamicsWindow.TrackingTextBlock.Text = "--";
            _breathingDynamicsWindow.LastWaveformTextBlock.Text = "--";
            _breathingDynamicsWindow.LastBreathTextBlock.Text = "--";
            _breathingDynamicsWindow.ExtremaCountTextBlock.Text = "--";
            _breathingDynamicsWindow.ConfidenceTextBlock.Text = "--";
            _breathingDynamicsWindow.IntervalCountTextBlock.Text = "--";
            _breathingDynamicsWindow.AmplitudeCountTextBlock.Text = "--";
            _breathingDynamicsWindow.StabilizationTextBlock.Text = "--";
            _breathingDynamicsWindow.IntervalReadinessTextBlock.Text = "--";
            _breathingDynamicsWindow.AmplitudeReadinessTextBlock.Text = "--";
            SetFeatureBundle(_breathingDynamicsWindow, interval: true, hasBasicStats: false, hasEntropyMetrics: false, PolarBreathingFeatureSet.Empty);
            SetFeatureBundle(_breathingDynamicsWindow, interval: false, hasBasicStats: false, hasEntropyMetrics: false, PolarBreathingFeatureSet.Empty);
            SetBreathingDynamicsStatus("No device selected", "GraphiteBrush");
            if (_breathingDynamicsChartAddress != null)
                RebuildBreathingDynamicsChart();
            return;
        }

        DeviceBreathingDynamicsState state = GetOrCreateBreathingDynamicsState(address);
        EnsureBreathingDynamicsEditorLoaded(address);

        if (!string.Equals(_breathingDynamicsChartAddress, address, StringComparison.OrdinalIgnoreCase))
            RebuildBreathingDynamicsChart();

        PolarBreathingDynamicsTelemetry telemetry = state.HasTelemetry ? state.LastTelemetry : state.Tracker.GetTelemetry();
        _breathingDynamicsWindow.Title = $"Polar H10 // Breath Variability // {CompactDisplayName(address)}";
        _breathingDynamicsWindow.SelectedDeviceTextBlock.Text = DisplayName(address);
        _breathingDynamicsWindow.SummaryTextBlock.Text = "Breath variability tracks derived interval and amplitude series from the calibrated breathing waveform, then unlocks entropy as those series mature.";
        _breathingDynamicsWindow.IntervalEntropyValueTextBlock.Text = telemetry.IntervalHasEntropyMetrics
            ? telemetry.Interval.SampleEntropy.ToString("0.00", CultureInfo.InvariantCulture)
            : "--";
        _breathingDynamicsWindow.IntervalEntropyHintTextBlock.Text = BuildReadinessText(telemetry.IntervalHasBasicStats, telemetry.IntervalHasEntropyMetrics, telemetry.IntervalBreathCount);
        _breathingDynamicsWindow.AmplitudeEntropyValueTextBlock.Text = telemetry.AmplitudeHasEntropyMetrics
            ? telemetry.Amplitude.SampleEntropy.ToString("0.00", CultureInfo.InvariantCulture)
            : "--";
        _breathingDynamicsWindow.AmplitudeEntropyHintTextBlock.Text = BuildReadinessText(telemetry.AmplitudeHasBasicStats, telemetry.AmplitudeHasEntropyMetrics, telemetry.AmplitudeBreathCount);
        _breathingDynamicsWindow.IntervalEntropyValueTextBlock.Foreground = ResourceBrush(GetBreathingDynamicsAccentBrushKey(telemetry.TrackingState));
        _breathingDynamicsWindow.AmplitudeEntropyValueTextBlock.Foreground = ResourceBrush(telemetry.AmplitudeHasEntropyMetrics ? "TelemetryGreenBrush" : "GraphiteBrush");
        _breathingDynamicsWindow.RequirementTextBlock.Text = BuildBreathingDynamicsRequirementText(telemetry);
        _breathingDynamicsWindow.WarmupHintTextBlock.Text = BuildBreathingDynamicsWarmupHint(telemetry);
        _breathingDynamicsWindow.MaturityProgressBar.Value = telemetry.StabilizationProgress01;
        _breathingDynamicsWindow.MaturityProgressBar.Foreground = ResourceBrush(GetBreathingDynamicsMaturityBrushKey(telemetry));
        _breathingDynamicsWindow.MaturityProgressTextBlock.Text = BuildBreathingDynamicsMaturityText(telemetry);
        _breathingDynamicsWindow.IntervalProgressBar.Value = GetBreathingDynamicsEntropyProgress(telemetry.IntervalBreathCount, telemetry.Settings.MinimumBreathsForEntropy);
        _breathingDynamicsWindow.IntervalProgressBar.Foreground = ResourceBrush(GetBreathingDynamicsSeriesBrushKey(telemetry.IntervalHasEntropyMetrics, telemetry.IntervalBreathCount > 0, telemetry.IsTransportConnected));
        _breathingDynamicsWindow.IntervalProgressTextBlock.Text = BuildBreathingDynamicsSeriesProgressText(telemetry.IntervalBreathCount, telemetry.Settings.MinimumBreathsForEntropy);
        _breathingDynamicsWindow.AmplitudeProgressBar.Value = GetBreathingDynamicsEntropyProgress(telemetry.AmplitudeBreathCount, telemetry.Settings.MinimumBreathsForEntropy);
        _breathingDynamicsWindow.AmplitudeProgressBar.Foreground = ResourceBrush(GetBreathingDynamicsSeriesBrushKey(telemetry.AmplitudeHasEntropyMetrics, telemetry.AmplitudeBreathCount > 0, telemetry.IsTransportConnected));
        _breathingDynamicsWindow.AmplitudeProgressTextBlock.Text = BuildBreathingDynamicsSeriesProgressText(telemetry.AmplitudeBreathCount, telemetry.Settings.MinimumBreathsForEntropy);

        _breathingDynamicsWindow.TrackingTextBlock.Text = FormatBreathingDynamicsTrackingState(telemetry.TrackingState);
        _breathingDynamicsWindow.LastWaveformTextBlock.Text = telemetry.HasReceivedAnyWaveformSample ? $"{telemetry.LastWaveformSampleAgeSeconds:0.00} s ago" : "Never";
        _breathingDynamicsWindow.LastBreathTextBlock.Text = telemetry.HasAcceptedAnyBreath ? $"{telemetry.LastBreathAgeSeconds:0.00} s ago" : "No breath yet";
        _breathingDynamicsWindow.ExtremaCountTextBlock.Text = $"{telemetry.AcceptedExtremumCount}";
        _breathingDynamicsWindow.ConfidenceTextBlock.Text = $"{telemetry.Confidence01 * 100f:0}%";
        _breathingDynamicsWindow.IntervalCountTextBlock.Text = $"{telemetry.IntervalBreathCount} derived intervals";
        _breathingDynamicsWindow.AmplitudeCountTextBlock.Text = $"{telemetry.AmplitudeBreathCount} breath excursions";
        _breathingDynamicsWindow.StabilizationTextBlock.Text = $"{telemetry.StabilizationProgress01 * 100f:0}%";
        _breathingDynamicsWindow.IntervalReadinessTextBlock.Text = BuildReadinessText(telemetry.IntervalHasBasicStats, telemetry.IntervalHasEntropyMetrics, telemetry.IntervalBreathCount);
        _breathingDynamicsWindow.AmplitudeReadinessTextBlock.Text = BuildReadinessText(telemetry.AmplitudeHasBasicStats, telemetry.AmplitudeHasEntropyMetrics, telemetry.AmplitudeBreathCount);
        SetFeatureBundle(_breathingDynamicsWindow, interval: true, telemetry.IntervalHasBasicStats, telemetry.IntervalHasEntropyMetrics, telemetry.Interval);
        SetFeatureBundle(_breathingDynamicsWindow, interval: false, telemetry.AmplitudeHasBasicStats, telemetry.AmplitudeHasEntropyMetrics, telemetry.Amplitude);

        SetBreathingDynamicsStatus(
            BuildBreathingDynamicsStatusLine(telemetry),
            telemetry.HasTracking ? "TelemetryGreenBrush" : telemetry.IsTransportConnected ? "FocusBlueBrush" : "GraphiteBrush");
    }

    private static string FormatBreathingDynamicsTrackingState(PolarBreathingDynamicsTrackingState state) => state switch
    {
        PolarBreathingDynamicsTrackingState.Tracking => "Tracking",
        PolarBreathingDynamicsTrackingState.WaitingForCalibration => "Waiting for calibration",
        PolarBreathingDynamicsTrackingState.WaitingForBreathingTracking => "Waiting for breathing tracking",
        PolarBreathingDynamicsTrackingState.Stale => "Stale",
        _ => "Unavailable",
    };

    private static string GetBreathingDynamicsAccentBrushKey(PolarBreathingDynamicsTrackingState state) => state switch
    {
        PolarBreathingDynamicsTrackingState.Tracking => "FocusBlueBrush",
        PolarBreathingDynamicsTrackingState.Stale => "SafetyOrangeBrush",
        _ => "GraphiteBrush",
    };

    private static string BuildBreathingDynamicsStatusLine(PolarBreathingDynamicsTelemetry telemetry)
    {
        if (telemetry.HasTracking && telemetry.IntervalHasEntropyMetrics && telemetry.AmplitudeHasEntropyMetrics)
            return "Breath variability is live with interval and amplitude entropy";
        if (telemetry.HasTracking)
            return "Tracking live and accumulating enough derived breaths for entropy";
        if (telemetry.IsTransportConnected && telemetry.IsBreathingCalibrated)
            return "Connected and waiting for stable breathing tracking";
        if (telemetry.IsTransportConnected)
            return "Connected and waiting for breathing calibration";
        return "Breath variability tracker is offline";
    }

    private static string BuildBreathingDynamicsRequirementText(PolarBreathingDynamicsTelemetry telemetry)
        => $"Basic stats unlock at {telemetry.Settings.MinimumBreathsForBasicStats} breaths per series. Entropy unlocks at {telemetry.Settings.MinimumBreathsForEntropy}, and the confidence target ramps toward {telemetry.Settings.FullConfidenceBreathCount} accepted breaths.";

    private static string BuildBreathingDynamicsWarmupHint(PolarBreathingDynamicsTelemetry telemetry)
    {
        if (!telemetry.IsTransportConnected)
            return "Connect the strap and keep breathing tracking live to start deriving interval and amplitude breaths.";
        if (!telemetry.IsBreathingCalibrated)
            return "Breathing calibration must finish before the variability tracker can derive breaths.";
        if (!telemetry.HasBreathingTracking)
            return "The breathing tracker is not live yet. Hold a stable breathing waveform first.";
        if (telemetry.IntervalHasEntropyMetrics && telemetry.AmplitudeHasEntropyMetrics)
            return "Interval and amplitude entropy are live.";
        if (!telemetry.HasAcceptedAnyBreath)
            return "Tracking is live. Keep breathing to accumulate the first accepted interval and amplitude breaths.";

        int remainingInterval = Math.Max(0, telemetry.Settings.MinimumBreathsForEntropy - telemetry.IntervalBreathCount);
        int remainingAmplitude = Math.Max(0, telemetry.Settings.MinimumBreathsForEntropy - telemetry.AmplitudeBreathCount);
        if (remainingInterval > 0 || remainingAmplitude > 0)
            return $"Need {remainingInterval} more interval breath(s) and {remainingAmplitude} more amplitude breath(s) for entropy.";

        int maturityCount = Math.Min(telemetry.IntervalBreathCount, telemetry.AmplitudeBreathCount);
        int remainingConfidence = Math.Max(0, telemetry.Settings.FullConfidenceBreathCount - maturityCount);
        return remainingConfidence > 0
            ? $"Entropy is live. Keep recording for about {remainingConfidence} more matched breaths to approach full confidence."
            : "Entropy and confidence targets are fully armed.";
    }

    private static string BuildBreathingDynamicsMaturityText(PolarBreathingDynamicsTelemetry telemetry)
    {
        int maturityCount = Math.Min(telemetry.IntervalBreathCount, telemetry.AmplitudeBreathCount);
        return $"{maturityCount}/{telemetry.Settings.FullConfidenceBreathCount} breaths ({telemetry.StabilizationProgress01 * 100f:0}%)";
    }

    private static double GetBreathingDynamicsEntropyProgress(int breathCount, int entropyTarget)
    {
        if (entropyTarget <= 0)
            return breathCount > 0 ? 1d : 0d;

        return Math.Clamp(breathCount / (double)entropyTarget, 0d, 1d);
    }

    private static string BuildBreathingDynamicsSeriesProgressText(int breathCount, int entropyTarget)
    {
        if (breathCount >= entropyTarget)
            return $"Ready ({breathCount} breaths)";

        return $"{breathCount}/{entropyTarget} breaths";
    }

    private static string GetBreathingDynamicsMaturityBrushKey(PolarBreathingDynamicsTelemetry telemetry)
    {
        if (telemetry.IntervalHasEntropyMetrics && telemetry.AmplitudeHasEntropyMetrics)
            return "TelemetryGreenBrush";
        if (telemetry.HasAcceptedAnyBreath || telemetry.HasTracking)
            return "FocusBlueBrush";
        return "GraphiteBrush";
    }

    private static string GetBreathingDynamicsSeriesBrushKey(bool isReady, bool hasSeries, bool isConnected)
    {
        if (isReady)
            return "TelemetryGreenBrush";
        if (hasSeries || isConnected)
            return "FocusBlueBrush";
        return "GraphiteBrush";
    }

    private void SetBreathingDynamicsStatus(string text, string brushKey)
    {
        if (_breathingDynamicsWindow == null)
            return;

        _breathingDynamicsWindow.StatusTextBlock.Text = text;
        _breathingDynamicsWindow.StatusTextBlock.Foreground = ResourceBrush(brushKey);
    }

    private static string BuildReadinessText(bool hasBasicStats, bool hasEntropyMetrics, int sampleCount)
    {
        if (hasEntropyMetrics)
            return $"Entropy ready with {sampleCount} derived breaths";
        if (hasBasicStats)
            return $"Basic stats ready; entropy warming up ({sampleCount} breaths)";
        return $"Collecting derived breaths ({sampleCount})";
    }

    private static void SetFeatureBundle(
        BreathingDynamicsWindow window,
        bool interval,
        bool hasBasicStats,
        bool hasEntropyMetrics,
        PolarBreathingFeatureSet features)
    {
        TextBlock mean = interval ? window.IntervalMeanTextBlock : window.AmplitudeMeanTextBlock;
        TextBlock sd = interval ? window.IntervalSdTextBlock : window.AmplitudeSdTextBlock;
        TextBlock cv = interval ? window.IntervalCvTextBlock : window.AmplitudeCvTextBlock;
        TextBlock acw = interval ? window.IntervalAcwTextBlock : window.AmplitudeAcwTextBlock;
        TextBlock psd = interval ? window.IntervalPsdSlopeTextBlock : window.AmplitudePsdSlopeTextBlock;
        TextBlock lzc = interval ? window.IntervalLzcTextBlock : window.AmplitudeLzcTextBlock;
        TextBlock sampleEntropy = interval ? window.IntervalSampleEntropyTextBlock : window.AmplitudeSampleEntropyTextBlock;
        TextBlock multiscaleEntropy = interval ? window.IntervalMultiscaleEntropyTextBlock : window.AmplitudeMultiscaleEntropyTextBlock;

        if (!hasBasicStats)
        {
            mean.Text = "--";
            sd.Text = "--";
            cv.Text = "--";
            acw.Text = "--";
            psd.Text = "--";
            lzc.Text = "--";
            sampleEntropy.Text = "--";
            multiscaleEntropy.Text = "--";
            return;
        }

        mean.Text = interval ? $"{features.Mean:0.000} s" : $"{features.Mean:0.000}";
        sd.Text = $"{features.StandardDeviation:0.000}";
        cv.Text = $"{features.CoefficientOfVariation:0.000}";
        acw.Text = $"{features.AutocorrelationWindow50:0.0}";
        psd.Text = $"{features.PsdSlope:0.000}";
        if (!hasEntropyMetrics)
        {
            lzc.Text = "--";
            sampleEntropy.Text = "--";
            multiscaleEntropy.Text = "--";
            return;
        }

        lzc.Text = $"{features.LempelZivComplexity:0.000}";
        sampleEntropy.Text = $"{features.SampleEntropy:0.000}";
        multiscaleEntropy.Text = $"{features.MultiscaleEntropy:0.000}";
    }

    private void LoadBreathingDynamicsSettingsToUi(PolarBreathingDynamicsSettings settings)
    {
        if (_breathingDynamicsWindow == null)
            return;

        _breathingDynamicsWindow.TurningThresholdBoxElement.Text = settings.TurningPointDeltaThreshold.ToString("0.####", CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.MinimumSpacingBoxElement.Text = settings.MinimumExtremumSpacingSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.MinimumExcursionBoxElement.Text = settings.MinimumCycleExcursion01.ToString("0.###", CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.RetainedBreathsBoxElement.Text = settings.RetainedBreathCount.ToString(CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.MinimumBasicBreathsBoxElement.Text = settings.MinimumBreathsForBasicStats.ToString(CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.MinimumEntropyBreathsBoxElement.Text = settings.MinimumBreathsForEntropy.ToString(CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.FullConfidenceBreathsBoxElement.Text = settings.FullConfidenceBreathCount.ToString(CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.StaleTimeoutBoxElement.Text = settings.StaleTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.SampleEntropyDimensionBoxElement.Text = settings.SampleEntropyDimension.ToString(CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.SampleEntropyDelayBoxElement.Text = settings.SampleEntropyDelay.ToString(CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.SampleEntropyToleranceBoxElement.Text = settings.SampleEntropyToleranceSdFactor.ToString("0.###", CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.MultiscaleEntropyDimensionBoxElement.Text = settings.MultiscaleEntropyDimension.ToString(CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.MultiscaleEntropyDelayBoxElement.Text = settings.MultiscaleEntropyDelay.ToString(CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.MultiscaleEntropyToleranceBoxElement.Text = settings.MultiscaleEntropyToleranceSdFactor.ToString("0.###", CultureInfo.InvariantCulture);
        _breathingDynamicsWindow.MultiscaleEntropyMaxScaleBoxElement.Text = settings.MultiscaleEntropyMaxScale.ToString(CultureInfo.InvariantCulture);
    }

    private bool TryReadBreathingDynamicsSettingsFromUi(PolarBreathingDynamicsSettings baseSettings, out PolarBreathingDynamicsSettings settings, out string error)
    {
        settings = baseSettings;
        error = string.Empty;

        if (_breathingDynamicsWindow == null)
            return false;

        if (!TryReadFloat(_breathingDynamicsWindow.TurningThresholdBoxElement, "Extremum delta", 0.0001f, 0.25f, out float turningThreshold, out error) ||
            !TryReadFloat(_breathingDynamicsWindow.MinimumSpacingBoxElement, "Peak/trough gap", 0.05f, 30f, out float minimumSpacing, out error) ||
            !TryReadFloat(_breathingDynamicsWindow.MinimumExcursionBoxElement, "Peak-trough excursion", 0.001f, 1f, out float minimumExcursion, out error) ||
            !TryReadInt(_breathingDynamicsWindow.RetainedBreathsBoxElement, "Retained series size", 32, 4096, out int retainedBreaths, out error) ||
            !TryReadInt(_breathingDynamicsWindow.MinimumBasicBreathsBoxElement, "Basic-stats warmup", 4, 4096, out int minimumBasicBreaths, out error) ||
            !TryReadInt(_breathingDynamicsWindow.MinimumEntropyBreathsBoxElement, "Entropy warmup", 8, 4096, out int minimumEntropyBreaths, out error) ||
            !TryReadInt(_breathingDynamicsWindow.FullConfidenceBreathsBoxElement, "Confidence target", 16, 4096, out int fullConfidenceBreaths, out error) ||
            !TryReadFloat(_breathingDynamicsWindow.StaleTimeoutBoxElement, "Stale timeout", 0.1f, 120f, out float staleTimeout, out error) ||
            !TryReadInt(_breathingDynamicsWindow.SampleEntropyDimensionBoxElement, "SampEn m", 1, 8, out int sampleEntropyDimension, out error) ||
            !TryReadInt(_breathingDynamicsWindow.SampleEntropyDelayBoxElement, "SampEn delay", 1, 16, out int sampleEntropyDelay, out error) ||
            !TryReadFloat(_breathingDynamicsWindow.SampleEntropyToleranceBoxElement, "SampEn r·SD", 0.01f, 2f, out float sampleEntropyTolerance, out error) ||
            !TryReadInt(_breathingDynamicsWindow.MultiscaleEntropyDimensionBoxElement, "MSE m", 1, 8, out int multiscaleEntropyDimension, out error) ||
            !TryReadInt(_breathingDynamicsWindow.MultiscaleEntropyDelayBoxElement, "MSE delay", 1, 16, out int multiscaleEntropyDelay, out error) ||
            !TryReadFloat(_breathingDynamicsWindow.MultiscaleEntropyToleranceBoxElement, "MSE r·SD", 0.01f, 2f, out float multiscaleEntropyTolerance, out error) ||
            !TryReadInt(_breathingDynamicsWindow.MultiscaleEntropyMaxScaleBoxElement, "MSE max scale", 1, 32, out int multiscaleEntropyMaxScale, out error))
        {
            return false;
        }

        settings = (baseSettings with
        {
            TurningPointDeltaThreshold = turningThreshold,
            MinimumExtremumSpacingSeconds = minimumSpacing,
            MinimumCycleExcursion01 = minimumExcursion,
            RetainedBreathCount = retainedBreaths,
            MinimumBreathsForBasicStats = minimumBasicBreaths,
            MinimumBreathsForEntropy = minimumEntropyBreaths,
            FullConfidenceBreathCount = fullConfidenceBreaths,
            StaleTimeoutSeconds = staleTimeout,
            SampleEntropyDimension = sampleEntropyDimension,
            SampleEntropyDelay = sampleEntropyDelay,
            SampleEntropyToleranceSdFactor = sampleEntropyTolerance,
            MultiscaleEntropyDimension = multiscaleEntropyDimension,
            MultiscaleEntropyDelay = multiscaleEntropyDelay,
            MultiscaleEntropyToleranceSdFactor = multiscaleEntropyTolerance,
            MultiscaleEntropyMaxScale = multiscaleEntropyMaxScale,
        }).Clamp();

        if (settings.MinimumBreathsForEntropy < settings.MinimumBreathsForBasicStats)
        {
            error = "Entropy warmup must be greater than or equal to basic-stats warmup.";
            return false;
        }

        if (settings.FullConfidenceBreathCount < settings.MinimumBreathsForEntropy)
        {
            error = "Confidence target must be greater than or equal to entropy warmup.";
            return false;
        }

        return true;
    }

    private void OnBreathingDynamicsApplyTuningRequested(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        DeviceBreathingDynamicsState state = GetOrCreateBreathingDynamicsState(_selectedAddress);
        PolarBreathingDynamicsSettings baseSettings = state.HasTelemetry ? state.LastTelemetry.Settings : state.Tracker.GetTelemetry().Settings;
        if (!TryReadBreathingDynamicsSettingsFromUi(baseSettings, out PolarBreathingDynamicsSettings settings, out string error))
        {
            SetBreathingDynamicsStatus(error, "SignalRedBrush");
            return;
        }

        state.Tracker.ApplySettings(settings, resetTracker: true);
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearBreathingDynamicsHistory(_selectedAddress);
        CaptureBreathingDynamicsTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildBreathingDynamicsChart();
        RebuildTelemetrySummaryCharts();
        AddBreathingDynamicsLog(_selectedAddress, "tuning applied and tracker reset", state);
        SetBreathingDynamicsStatus("Tuning applied", "FocusBlueBrush");
        UpdateBreathingDynamicsPanel(_selectedAddress);
    }

    private void OnBreathingDynamicsRestoreDefaultsRequested(object? sender, EventArgs e)
    {
        LoadBreathingDynamicsSettingsToUi(AppBreathingDynamicsDefaults);
        if (string.IsNullOrWhiteSpace(_selectedAddress))
        {
            SetBreathingDynamicsStatus("Defaults loaded", "FocusBlueBrush");
            return;
        }

        DeviceBreathingDynamicsState state = GetOrCreateBreathingDynamicsState(_selectedAddress);
        state.Tracker.ApplySettings(AppBreathingDynamicsDefaults, resetTracker: true);
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearBreathingDynamicsHistory(_selectedAddress);
        CaptureBreathingDynamicsTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildBreathingDynamicsChart();
        RebuildTelemetrySummaryCharts();
        AddBreathingDynamicsLog(_selectedAddress, "defaults restored and tracker reset", state);
        SetBreathingDynamicsStatus("Defaults restored", "FocusBlueBrush");
        UpdateBreathingDynamicsPanel(_selectedAddress);
    }

    private void OnBreathingDynamicsResetTrackerRequested(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        DeviceBreathingDynamicsState state = GetOrCreateBreathingDynamicsState(_selectedAddress);
        state.Tracker.Reset();
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearBreathingDynamicsHistory(_selectedAddress);
        CaptureBreathingDynamicsTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildBreathingDynamicsChart();
        RebuildTelemetrySummaryCharts();
        AddBreathingDynamicsLog(_selectedAddress, "tracker reset", state);
        SetBreathingDynamicsStatus("Tracker reset", "GraphiteBrush");
        UpdateBreathingDynamicsPanel(_selectedAddress);
    }

    private void ClearBreathingDynamicsHistory(string address)
    {
        if (_chartStates.TryGetValue(address, out DeviceChartState? chartState))
        {
            chartState.BreathIntervalEntropyValues.Clear();
            chartState.BreathAmplitudeEntropyValues.Clear();
        }
    }

    private void OnOpenBreathingDynamicsWindowClick(object sender, RoutedEventArgs e)
    {
        EnsureBreathingDynamicsWindow();
        DeviceTabControl.SelectedItem = DynamicsTab;
        UpdateBreathingDynamicsPanel(_selectedAddress);
    }

    private void SeedPreviewBreathingDynamicsState((string Address, string Name, string Status)[] previewDevices)
    {
        PolarBreathingDynamicsSettings previewSettings = (AppBreathingDynamicsDefaults with
        {
            RetainedBreathCount = 180,
            MinimumBreathsForBasicStats = 6,
            MinimumBreathsForEntropy = 18,
            FullConfidenceBreathCount = 72,
        }).Clamp();

        for (int deviceIndex = 0; deviceIndex < previewDevices.Length; deviceIndex++)
        {
            (string address, _, string status) = previewDevices[deviceIndex];
            DeviceBreathingDynamicsState state = GetOrCreateBreathingDynamicsState(address);
            state.Tracker.ApplySettings(previewSettings, resetTracker: true);
            bool connected = !string.IsNullOrWhiteSpace(status);
            state.Tracker.SetTransportConnected(connected);
            state.HasTelemetry = false;

            if (!connected)
            {
                CaptureBreathingDynamicsTelemetry(address, state, pushChartValue: true);
                AddBreathingDynamicsLog(address, "preview breath variability tracker idle", state);
                continue;
            }

            DateTimeOffset previewStartUtc = DateTimeOffset.UtcNow - TimeSpan.FromSeconds((2200d / 12.5d) + (deviceIndex * 2d));
            for (int sampleIndex = 0; sampleIndex < 2200; sampleIndex++)
            {
                double t = sampleIndex / 12.5;
                double phase = (t * (Math.PI * 2.0 / (5.2 + (deviceIndex * 0.4)))) + (deviceIndex * 0.45);
                float baseVolume = (float)(
                    0.5 +
                    (Math.Sin(phase) * 0.28) +
                    (Math.Sin(phase * 2.0) * 0.03) +
                    (Math.Cos(phase * 0.35) * 0.015));
                baseVolume = Math.Clamp(baseVolume, 0.05f, 0.95f);
                PolarBreathingTelemetry telemetry = CreatePreviewBreathingTelemetry(baseVolume, previewStartUtc.AddSeconds(t));
                state.Tracker.SubmitBreathingTelemetry(telemetry);
                CaptureBreathingDynamicsTelemetry(address, state, pushChartValue: true);
            }

            AddBreathingDynamicsLog(address, "preview breath variability tracker armed", state);
        }
    }

    private static PolarBreathingTelemetry CreatePreviewBreathingTelemetry(float volumeBase01, DateTimeOffset? sampleAtUtc = null)
    {
        float volume = Math.Clamp(volumeBase01, 0f, 1f);
        return new PolarBreathingTelemetry(
            IsTransportConnected: true,
            HasReceivedAnySample: true,
            IsCalibrating: false,
            IsCalibrated: true,
            HasTracking: true,
            HasUsefulSignal: true,
            HasXzModel: true,
            CalibrationProgress01: 1f,
            CurrentVolume01: volume,
            CurrentState: PolarBreathingState.Pausing,
            EstimatedSampleRateHz: 100f,
            UsefulAxisRangeG: 0.024f,
            LastProjectionG: 0f,
            Volume3d01: volume,
            VolumeBase01: volume,
            VolumeXz01: volume,
            Axis: System.Numerics.Vector3.UnitZ,
            Center: System.Numerics.Vector3.Zero,
            BoundMin: 0f,
            BoundMax: 1f,
            XzAxis: new System.Numerics.Vector2(1f, 0f),
            XzBoundMin: 0f,
            XzBoundMax: 1f,
            AccFrameCount: 1,
            AccSampleCount: 1,
            LastSampleAgeSeconds: 0f,
            LastCalibrationFailureReason: string.Empty,
            Settings: PolarBreathingSettings.CreateDefault(),
            LastSampleReceivedAtUtc: sampleAtUtc ?? DateTimeOffset.UtcNow);
    }
}
