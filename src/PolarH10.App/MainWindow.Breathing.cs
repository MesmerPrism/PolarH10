using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PolarH10.Protocol;

namespace PolarH10.App;

public partial class MainWindow
{
    private static readonly PolarBreathingSettings AppBreathingDefaults = PolarBreathingSettings.CreateDefault();

    private sealed class DeviceBreathingState
    {
        public PolarBreathingTracker Tracker { get; } = new(AppBreathingDefaults);
        public bool HasTelemetry;
        public bool UsesExternalTelemetry;
        public PolarBreathingTelemetry LastTelemetry;
        public readonly List<float> OutputValues = [];
        public readonly List<float> Volume3dValues = [];
        public readonly List<float> VolumeXzValues = [];
        public readonly List<string> LogLines = [];
        public bool HasPlottedValue;
        public float LastPlottedValue;

        public void ClearSeries()
        {
            OutputValues.Clear();
            Volume3dValues.Clear();
            VolumeXzValues.Clear();
            HasPlottedValue = false;
            LastPlottedValue = 0f;
        }
    }

    private readonly Dictionary<string, DeviceBreathingState> _breathingStates = new(StringComparer.OrdinalIgnoreCase);
    private WaveformChart _breathingChart = null!;
    private int _breathingOutputSeries;
    private int _breathing3dSeries;
    private int _breathingXzSeries;
    private string? _breathingChartAddress;
    private string? _breathingEditorAddress;

    private void InitializeBreathingChart()
    {
        RebuildBreathingChart();
    }

    private DeviceBreathingState GetOrCreateBreathingState(string address)
    {
        if (!_breathingStates.TryGetValue(address, out var state))
        {
            state = new DeviceBreathingState();
            _breathingStates[address] = state;
        }

        return state;
    }

    private void RemoveBreathingState(string address)
    {
        _breathingStates.Remove(address);
        if (string.Equals(_breathingChartAddress, address, StringComparison.OrdinalIgnoreCase))
            _breathingChartAddress = null;
        if (string.Equals(_breathingEditorAddress, address, StringComparison.OrdinalIgnoreCase))
            _breathingEditorAddress = null;
    }

    private void ClearBreathingStates()
    {
        _breathingStates.Clear();
        _breathingChartAddress = null;
        _breathingEditorAddress = null;

        if (BreathingLogList != null)
            BreathingLogList.Items.Clear();

        if (BreathingChartHost != null)
            RebuildBreathingChart();
    }

    private void RefreshBreathingTrackers()
    {
        foreach (var (address, state) in _breathingStates)
        {
            if (state.UsesExternalTelemetry)
                continue;

            state.Tracker.Advance();
            CaptureBreathingTelemetry(address, state, pushChartValue: false);
        }
    }

    private void RebuildBreathingChart()
    {
        _breathingChart = CreateChart("Breathing", _breathingChartAxisOptions);
        _breathingOutputSeries = _breathingChart.AddSeries("Volume", FocusBlue, 360);
        _breathing3dSeries = _breathingChart.AddSeries("3D", SignalRed, 360);
        _breathingXzSeries = _breathingChart.AddSeries("XZ", TelemetryGreen, 360);

        if (!string.IsNullOrWhiteSpace(_selectedAddress) &&
            _breathingStates.TryGetValue(_selectedAddress, out var state))
        {
            ReplayBreathingSeries(state);
            _breathingChartAddress = _selectedAddress;
        }
        else
        {
            _breathingChartAddress = null;
        }

        SetChartHostContent(BreathingChartHost, _breathingChart, _breathingChartAxisOptions);
        _breathingChart.Refresh();
    }

    private void ReplayBreathingSeries(DeviceBreathingState state)
    {
        foreach (var value in state.OutputValues)
            _breathingChart.Push(_breathingOutputSeries, value);
        foreach (var value in state.Volume3dValues)
            _breathingChart.Push(_breathing3dSeries, value);
        foreach (var value in state.VolumeXzValues)
            _breathingChart.Push(_breathingXzSeries, value);
    }

    private void EnsureBreathingEditorLoaded(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;

        if (string.Equals(_breathingEditorAddress, address, StringComparison.OrdinalIgnoreCase))
            return;

        var state = GetOrCreateBreathingState(address);
        var settings = state.HasTelemetry ? state.LastTelemetry.Settings : state.Tracker.GetTelemetry().Settings;
        LoadBreathingSettingsToUi(settings);
        RefreshBreathingLogList(state);
        _breathingEditorAddress = address;
    }

    private void CaptureBreathingTelemetry(string address, DeviceBreathingState state, bool pushChartValue)
    {
        CaptureBreathingTelemetry(address, state, state.Tracker.GetTelemetry(), pushChartValue);
    }

    private void CaptureBreathingTelemetry(
        string address,
        DeviceBreathingState state,
        PolarBreathingTelemetry telemetry,
        bool pushChartValue)
    {
        bool hadLiveBreathing = state.HasTelemetry && ShouldPlotLiveBreathing(state.LastTelemetry);
        if (pushChartValue || ShouldAppendBreathingValue(state, telemetry))
            AppendBreathingValue(address, state, telemetry);

        if (hadLiveBreathing && !ShouldPlotLiveBreathing(telemetry))
            ClearLiveBreathingHistory(address);

        if (state.HasTelemetry)
            LogBreathingTransitions(address, state.LastTelemetry, telemetry, state);

        state.LastTelemetry = telemetry;
        state.HasTelemetry = true;

        if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
            UpdateBreathingPanel(address);
    }

    private void CaptureSyntheticBreathingTelemetry(string address, DeviceBreathingState state, PolarBreathingTelemetry telemetry, bool pushChartValue)
    {
        state.UsesExternalTelemetry = true;
        CaptureBreathingTelemetry(address, state, telemetry, pushChartValue);
    }

    private static bool ShouldAppendBreathingValue(DeviceBreathingState state, PolarBreathingTelemetry telemetry)
    {
        if (!state.HasPlottedValue)
            return true;

        return Math.Abs(state.LastPlottedValue - telemetry.CurrentVolume01) >= 0.0005f ||
               telemetry.CurrentState != state.LastTelemetry.CurrentState ||
               telemetry.IsCalibrated != state.LastTelemetry.IsCalibrated;
    }

    private void AppendBreathingValue(string address, DeviceBreathingState state, PolarBreathingTelemetry telemetry)
    {
        AppendRolling(state.OutputValues, telemetry.CurrentVolume01);
        AppendRolling(state.Volume3dValues, telemetry.Volume3d01);
        AppendRolling(state.VolumeXzValues, telemetry.VolumeXz01);
        state.HasPlottedValue = true;
        state.LastPlottedValue = telemetry.CurrentVolume01;

        if (ShouldPlotLiveBreathing(telemetry))
        {
            var chartState = GetOrCreateChartState(address);
            AppendRolling(chartState.BreathingValues, telemetry.CurrentVolume01);
            PushMetricSampleToTelemetryCharts(address, TelemetryMetric.Breathing, telemetry.CurrentVolume01);
            if (_liveSeriesBindings.TryGetValue(address, out var binding))
                _liveBreathingChart.Push(binding.Breathing, telemetry.CurrentVolume01);
        }

        if (!string.IsNullOrWhiteSpace(_selectedAddress) &&
            string.Equals(_selectedAddress, _breathingChartAddress, StringComparison.OrdinalIgnoreCase))
        {
            _breathingChart.Push(_breathingOutputSeries, telemetry.CurrentVolume01);
            _breathingChart.Push(_breathing3dSeries, telemetry.Volume3d01);
            _breathingChart.Push(_breathingXzSeries, telemetry.VolumeXz01);
        }
    }

    private static void AppendRolling(List<float> values, float value, int maxCount = 360)
    {
        values.Add(value);
        if (values.Count > maxCount)
            values.RemoveAt(0);
    }

    private static bool ShouldPlotLiveBreathing(PolarBreathingTelemetry telemetry)
        => telemetry.IsTransportConnected && telemetry.IsCalibrated && telemetry.HasTracking;

    private void ClearLiveBreathingHistory(string address)
    {
        if (!_chartStates.TryGetValue(address, out var chartState) || chartState.BreathingValues.Count == 0)
            return;

        chartState.BreathingValues.Clear();
        if (_liveSeriesBindings.ContainsKey(address))
            RebuildLiveCharts();
        RebuildTelemetrySummaryCharts();
    }

    private void LogBreathingTransitions(
        string address,
        PolarBreathingTelemetry previous,
        PolarBreathingTelemetry current,
        DeviceBreathingState state)
    {
        if (previous.IsTransportConnected != current.IsTransportConnected)
        {
            AddBreathingLog(address, current.IsTransportConnected ? "transport connected" : "transport disconnected", state);
        }

        if (previous.HasUsefulSignal != current.HasUsefulSignal)
        {
            AddBreathingLog(
                address,
                current.HasUsefulSignal
                    ? $"useful ACC signal detected ({current.UsefulAxisRangeG:0.0000} g)"
                    : "useful ACC signal lost",
                state);
        }

        if (!previous.IsCalibrating && current.IsCalibrating)
            AddBreathingLog(address, "calibration started", state);

        if (previous.IsCalibrating && !current.IsCalibrating && current.IsCalibrated)
        {
            AddBreathingLog(
                address,
                $"calibration ready bounds=[{current.BoundMin:0.0000}, {current.BoundMax:0.0000}] g",
                state);
        }

        if (previous.IsCalibrating && !current.IsCalibrating && !current.IsCalibrated &&
            !string.IsNullOrWhiteSpace(current.LastCalibrationFailureReason) &&
            !string.Equals(previous.LastCalibrationFailureReason, current.LastCalibrationFailureReason, StringComparison.Ordinal))
        {
            AddBreathingLog(address, $"calibration failed: {current.LastCalibrationFailureReason}", state);
        }

        if (previous.HasTracking != current.HasTracking)
        {
            AddBreathingLog(address, current.HasTracking ? "tracking ready" : "tracking unavailable", state);
        }
    }

    private void AddBreathingLog(string address, string message, DeviceBreathingState? state = null)
    {
        state ??= GetOrCreateBreathingState(address);
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        state.LogLines.Add(line);
        if (state.LogLines.Count > 250)
            state.LogLines.RemoveAt(0);

        if (_selectedAddress?.Equals(address, StringComparison.OrdinalIgnoreCase) == true)
        {
            BreathingLogList.Items.Add(line);
            if (BreathingLogList.Items.Count > 250)
                BreathingLogList.Items.RemoveAt(0);
            BreathingLogList.ScrollIntoView(BreathingLogList.Items[^1]);
        }
    }

    private void RefreshBreathingLogList(DeviceBreathingState state)
    {
        BreathingLogList.Items.Clear();
        foreach (var line in state.LogLines)
            BreathingLogList.Items.Add(line);

        if (BreathingLogList.Items.Count > 0)
            BreathingLogList.ScrollIntoView(BreathingLogList.Items[^1]);
    }

    private void UpdateBreathingPanel(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            SetBreathingStatus("No device selected", "GraphiteBrush");
            BreathingVolumeValueText.Text = "--";
            BreathingStateValueText.Text = "Bad tracking";
            BreathingTrackingValueText.Text = "Awaiting device selection";
            BreathingCalibrationValueText.Text = "No tracker";
            BreathingTelemetrySampleRateText.Text = "--";
            BreathingTelemetryUsefulSignalText.Text = "--";
            BreathingTelemetryAxisRangeText.Text = "--";
            BreathingTelemetryLastSampleText.Text = "--";
            BreathingTelemetryBaseModeText.Text = "--";
            BreathingTelemetryTrackingText.Text = "--";
            BreathingTelemetryBoundsText.Text = "--";
            BreathingTelemetryXzBoundsText.Text = "--";
            BreathingTelemetryAxisText.Text = "--";
            BreathingTelemetryCenterText.Text = "--";
            BreathingTelemetryVolumesText.Text = "--";
            BreathingTelemetryFailureText.Text = "--";
            BreathingStartCalibrationButton.IsEnabled = false;
            BreathingCancelCalibrationButton.IsEnabled = false;
            BreathingResetTrackerButton.IsEnabled = false;
            BreathingFlipMappingButton.IsEnabled = false;
            BreathingApplyTuningButton.IsEnabled = false;
            BreathingRestoreDefaultsButton.IsEnabled = false;
            if (_breathingChartAddress != null)
                RebuildBreathingChart();
            return;
        }

        var state = GetOrCreateBreathingState(address);
        EnsureBreathingEditorLoaded(address);

        if (!string.Equals(_breathingChartAddress, address, StringComparison.OrdinalIgnoreCase))
            RebuildBreathingChart();

        var telemetry = state.HasTelemetry ? state.LastTelemetry : state.Tracker.GetTelemetry();
        BreathingVolumeValueText.Text = $"{telemetry.CurrentVolume01:0.00}";
        BreathingStateValueText.Text = FormatBreathingState(telemetry.CurrentState);
        BreathingTrackingValueText.Text = telemetry.HasTracking
            ? "Tracking ready"
            : telemetry.IsTransportConnected
                ? "Waiting for calibrated ACC tracking"
                : "Transport offline";
        BreathingCalibrationValueText.Text = telemetry.IsCalibrating
            ? $"Calibrating {telemetry.CalibrationProgress01 * 100f:0}%"
            : telemetry.IsCalibrated
                ? "Calibrated"
                : "Needs calibration";
        BreathingVolumeValueText.Foreground = ResourceBrush(GetBreathingAccentBrushKey(telemetry.CurrentState));

        BreathingTelemetrySampleRateText.Text = telemetry.HasReceivedAnySample ? $"{telemetry.EstimatedSampleRateHz:0.0} Hz" : "No ACC";
        BreathingTelemetryUsefulSignalText.Text = telemetry.HasUsefulSignal ? "Detected" : "Not yet";
        BreathingTelemetryAxisRangeText.Text = $"{telemetry.UsefulAxisRangeG:0.0000} g";
        BreathingTelemetryLastSampleText.Text = telemetry.HasReceivedAnySample
            ? $"{telemetry.LastSampleAgeSeconds:0.00} s ago"
            : "Never";
        BreathingTelemetryBaseModeText.Text = FormatBaseMode(telemetry);
        BreathingTelemetryTrackingText.Text = telemetry.HasTracking ? "Ready" : telemetry.IsTransportConnected ? "Not ready" : "Offline";
        BreathingTelemetryBoundsText.Text = $"[{telemetry.BoundMin:0.0000}, {telemetry.BoundMax:0.0000}] g";
        BreathingTelemetryXzBoundsText.Text = telemetry.HasXzModel
            ? $"[{telemetry.XzBoundMin:0.0000}, {telemetry.XzBoundMax:0.0000}] g"
            : "n/a";
        BreathingTelemetryAxisText.Text = FormatVector3(telemetry.Axis);
        BreathingTelemetryCenterText.Text = FormatVector3(telemetry.Center);
        BreathingTelemetryVolumesText.Text = $"out {telemetry.CurrentVolume01:0.00} | 3D {telemetry.Volume3d01:0.00} | XZ {telemetry.VolumeXz01:0.00}";
        BreathingTelemetryFailureText.Text = string.IsNullOrWhiteSpace(telemetry.LastCalibrationFailureReason)
            ? "none"
            : telemetry.LastCalibrationFailureReason;

        SetBreathingStatus(BuildBreathingStatusLine(telemetry), telemetry.HasTracking ? "TelemetryGreenBrush" : telemetry.IsTransportConnected ? "FocusBlueBrush" : "GraphiteBrush");

        BreathingStartCalibrationButton.IsEnabled = telemetry.IsTransportConnected;
        BreathingCancelCalibrationButton.IsEnabled = telemetry.IsCalibrating;
        BreathingResetTrackerButton.IsEnabled = true;
        BreathingFlipMappingButton.IsEnabled = true;
        BreathingApplyTuningButton.IsEnabled = true;
        BreathingRestoreDefaultsButton.IsEnabled = true;
    }

    private static string FormatBreathingState(PolarBreathingState state) => state switch
    {
        PolarBreathingState.Inhaling => "Inhaling",
        PolarBreathingState.Exhaling => "Exhaling",
        PolarBreathingState.Pausing => "Pausing",
        _ => "Bad tracking",
    };

    private static string FormatBaseMode(PolarBreathingTelemetry telemetry)
    {
        if (telemetry.Settings.BaseMode == PolarBreathingBaseMode.Xz)
            return telemetry.HasXzModel ? "X/Z" : "X/Z requested, using 3D fallback";

        return "3D";
    }

    private static string FormatVector3(System.Numerics.Vector3 value)
        => $"({value.X:0.000}, {value.Y:0.000}, {value.Z:0.000})";

    private static string BuildBreathingStatusLine(PolarBreathingTelemetry telemetry)
    {
        if (telemetry.IsCalibrating)
            return $"Calibration in progress at {telemetry.EstimatedSampleRateHz:0.0} Hz";
        if (telemetry.HasTracking)
            return $"Tracking live via {FormatBaseMode(telemetry)}";
        if (telemetry.IsTransportConnected)
            return "Connected and waiting for calibration-ready breathing output";
        return "Breathing tracker is offline";
    }

    private static string GetBreathingAccentBrushKey(PolarBreathingState state) => state switch
    {
        PolarBreathingState.Inhaling => "TelemetryGreenBrush",
        PolarBreathingState.Exhaling => "SafetyOrangeBrush",
        PolarBreathingState.Pausing => "FocusBlueBrush",
        _ => "GraphiteBrush",
    };

    private void SetBreathingStatus(string text, string brushKey)
    {
        BreathingStatusText.Text = text;
        BreathingStatusText.Foreground = ResourceBrush(brushKey);
    }

    private void LoadBreathingSettingsToUi(PolarBreathingSettings settings)
    {
        BreathingAutoCalibrateCheckBox.IsChecked = settings.AutoCalibrateOnUsefulSignal;
        BreathingAdaptiveBoundsCheckBox.IsChecked = settings.UseAdaptiveBounds;
        BreathingUseXzBaseCheckBox.IsChecked = settings.BaseMode == PolarBreathingBaseMode.Xz;
        BreathingInvertVolumeCheckBox.IsChecked = settings.InvertVolume;

        BreathingCalibrationDurationBox.Text = settings.CalibrationDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingMinCalibrationSamplesBox.Text = settings.MinCalibrationSamples.ToString(CultureInfo.InvariantCulture);
        BreathingMinCalibrationTravelBox.Text = settings.MinCalibrationTravelG.ToString("0.####", CultureInfo.InvariantCulture);
        BreathingUsefulWindowBox.Text = settings.UsefulSignalWindowSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingMinUsefulSamplesBox.Text = settings.MinUsefulSamples.ToString(CultureInfo.InvariantCulture);
        BreathingMinUsefulSampleRateBox.Text = settings.MinUsefulSampleRateHz.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingMinUsefulAxisRangeBox.Text = settings.MinUsefulAxisRangeG.ToString("0.####", CultureInfo.InvariantCulture);
        BreathingSampleEmaBox.Text = settings.SampleEmaAlpha.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingProjectionEmaBox.Text = settings.ProjectionEmaAlpha.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingBoundsLowerQuantileBox.Text = settings.BoundsLowerQuantile.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingBoundsUpperQuantileBox.Text = settings.BoundsUpperQuantile.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingBoundsEdgeEaseBox.Text = settings.BoundsEdgeEase.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingAdaptiveWindowBox.Text = settings.AdaptiveBoundsWindowSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingAdaptiveUpdateIntervalBox.Text = settings.AdaptiveBoundsUpdateIntervalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingAdaptiveLerpSpeedBox.Text = settings.AdaptiveBoundsLerpSpeed.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingAdaptiveContractBox.Text = settings.AdaptiveBoundsContractSpeedMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingStaleTimeoutBox.Text = settings.StaleTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        BreathingStateDeltaThresholdBox.Text = settings.StateDeltaThreshold.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private bool TryReadBreathingSettingsFromUi(PolarBreathingSettings baseSettings, out PolarBreathingSettings settings, out string error)
    {
        settings = baseSettings;
        error = string.Empty;

        if (!TryReadFloat(BreathingCalibrationDurationBox, "Calibration duration", 1f, 120f, out float calibrationDuration, out error) ||
            !TryReadInt(BreathingMinCalibrationSamplesBox, "Min calibration samples", 60, 100000, out int minCalibrationSamples, out error) ||
            !TryReadFloat(BreathingMinCalibrationTravelBox, "Min calibration travel", 0.001f, 1f, out float minCalibrationTravel, out error) ||
            !TryReadFloat(BreathingUsefulWindowBox, "Useful signal window", 0.25f, 120f, out float usefulWindow, out error) ||
            !TryReadInt(BreathingMinUsefulSamplesBox, "Min useful samples", 16, 100000, out int minUsefulSamples, out error) ||
            !TryReadFloat(BreathingMinUsefulSampleRateBox, "Min useful sample rate", 1f, 1000f, out float minUsefulSampleRate, out error) ||
            !TryReadFloat(BreathingMinUsefulAxisRangeBox, "Min useful axis range", 0.0005f, 1f, out float minUsefulAxisRange, out error) ||
            !TryReadFloat(BreathingSampleEmaBox, "Sample EMA", 0.01f, 1f, out float sampleEma, out error) ||
            !TryReadFloat(BreathingProjectionEmaBox, "Projection EMA", 0.01f, 1f, out float projectionEma, out error) ||
            !TryReadFloat(BreathingBoundsLowerQuantileBox, "Lower quantile", 0f, 0.25f, out float lowerQuantile, out error) ||
            !TryReadFloat(BreathingBoundsUpperQuantileBox, "Upper quantile", 0.75f, 1f, out float upperQuantile, out error) ||
            !TryReadFloat(BreathingBoundsEdgeEaseBox, "Bounds edge ease", 0f, 0.2f, out float edgeEase, out error) ||
            !TryReadFloat(BreathingAdaptiveWindowBox, "Adaptive bounds window", 4f, 300f, out float adaptiveWindow, out error) ||
            !TryReadFloat(BreathingAdaptiveUpdateIntervalBox, "Adaptive update interval", 0.1f, 30f, out float adaptiveUpdateInterval, out error) ||
            !TryReadFloat(BreathingAdaptiveLerpSpeedBox, "Adaptive lerp speed", 0.05f, 10f, out float adaptiveLerp, out error) ||
            !TryReadFloat(BreathingAdaptiveContractBox, "Adaptive contract multiplier", 0.1f, 1f, out float adaptiveContract, out error) ||
            !TryReadFloat(BreathingStaleTimeoutBox, "Stale timeout", 0.1f, 120f, out float staleTimeout, out error) ||
            !TryReadFloat(BreathingStateDeltaThresholdBox, "State delta threshold", 0.0001f, 0.25f, out float stateDelta, out error))
        {
            return false;
        }

        settings = (baseSettings with
        {
            AutoCalibrateOnUsefulSignal = BreathingAutoCalibrateCheckBox.IsChecked == true,
            UseAdaptiveBounds = BreathingAdaptiveBoundsCheckBox.IsChecked == true,
            BaseMode = BreathingUseXzBaseCheckBox.IsChecked == true ? PolarBreathingBaseMode.Xz : PolarBreathingBaseMode.ThreeD,
            InvertVolume = BreathingInvertVolumeCheckBox.IsChecked == true,
            CalibrationDurationSeconds = calibrationDuration,
            MinCalibrationSamples = minCalibrationSamples,
            MinCalibrationTravelG = minCalibrationTravel,
            UsefulSignalWindowSeconds = usefulWindow,
            MinUsefulSamples = minUsefulSamples,
            MinUsefulSampleRateHz = minUsefulSampleRate,
            MinUsefulAxisRangeG = minUsefulAxisRange,
            SampleEmaAlpha = sampleEma,
            ProjectionEmaAlpha = projectionEma,
            BoundsLowerQuantile = lowerQuantile,
            BoundsUpperQuantile = upperQuantile,
            BoundsEdgeEase = edgeEase,
            AdaptiveBoundsWindowSeconds = adaptiveWindow,
            AdaptiveBoundsUpdateIntervalSeconds = adaptiveUpdateInterval,
            AdaptiveBoundsLerpSpeed = adaptiveLerp,
            AdaptiveBoundsContractSpeedMultiplier = adaptiveContract,
            StaleTimeoutSeconds = staleTimeout,
            StateDeltaThreshold = stateDelta,
        }).Clamp();

        return true;
    }

    private static bool TryReadFloat(TextBox textBox, string label, float min, float max, out float value, out string error)
    {
        if (!float.TryParse(textBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            float.IsNaN(value) ||
            float.IsInfinity(value))
        {
            error = $"{label} is not a valid number.";
            return false;
        }

        if (value < min || value > max)
        {
            error = $"{label} must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadInt(TextBox textBox, string label, int min, int max, out int value, out string error)
    {
        if (!int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = $"{label} is not a valid integer.";
            return false;
        }

        if (value < min || value > max)
        {
            error = $"{label} must be between {min} and {max}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void OnBreathingApplyTuningClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        var state = GetOrCreateBreathingState(_selectedAddress);
        var baseSettings = state.HasTelemetry ? state.LastTelemetry.Settings : state.Tracker.GetTelemetry().Settings;
        if (!TryReadBreathingSettingsFromUi(baseSettings, out var settings, out var error))
        {
            SetBreathingStatus(error, "SignalRedBrush");
            return;
        }

        state.Tracker.ApplySettings(settings, resetTracker: true);
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearLiveBreathingHistory(_selectedAddress);
        CaptureBreathingTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildBreathingChart();
        AddBreathingLog(_selectedAddress, "tuning applied and tracker reset", state);
        SetBreathingStatus("Tuning applied", "FocusBlueBrush");
        UpdateBreathingPanel(_selectedAddress);
    }

    private void OnBreathingRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        LoadBreathingSettingsToUi(AppBreathingDefaults);
        if (string.IsNullOrWhiteSpace(_selectedAddress))
        {
            SetBreathingStatus("Defaults loaded", "FocusBlueBrush");
            return;
        }

        var state = GetOrCreateBreathingState(_selectedAddress);
        state.Tracker.ApplySettings(AppBreathingDefaults, resetTracker: true);
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearLiveBreathingHistory(_selectedAddress);
        CaptureBreathingTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildBreathingChart();
        AddBreathingLog(_selectedAddress, "defaults restored and tracker reset", state);
        SetBreathingStatus("Defaults restored", "FocusBlueBrush");
        UpdateBreathingPanel(_selectedAddress);
    }

    private void OnBreathingStartCalibrationClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        var state = GetOrCreateBreathingState(_selectedAddress);
        state.Tracker.BeginCalibration();
        CaptureBreathingTelemetry(_selectedAddress, state, pushChartValue: false);
        AddBreathingLog(_selectedAddress, "manual calibration requested", state);
        SetBreathingStatus("Calibration requested", "SafetyOrangeBrush");
        UpdateBreathingPanel(_selectedAddress);
    }

    private void OnBreathingCancelCalibrationClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        var state = GetOrCreateBreathingState(_selectedAddress);
        if (!state.Tracker.CancelCalibration())
            return;

        state.ClearSeries();
        ClearLiveBreathingHistory(_selectedAddress);
        CaptureBreathingTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildBreathingChart();
        AddBreathingLog(_selectedAddress, "calibration cancelled", state);
        SetBreathingStatus("Calibration cancelled", "GraphiteBrush");
        UpdateBreathingPanel(_selectedAddress);
    }

    private void OnBreathingResetTrackerClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        var state = GetOrCreateBreathingState(_selectedAddress);
        state.Tracker.Reset();
        state.ClearSeries();
        state.HasTelemetry = false;
        ClearLiveBreathingHistory(_selectedAddress);
        CaptureBreathingTelemetry(_selectedAddress, state, pushChartValue: true);
        RebuildBreathingChart();
        AddBreathingLog(_selectedAddress, "tracker reset", state);
        SetBreathingStatus("Tracker reset", "GraphiteBrush");
        UpdateBreathingPanel(_selectedAddress);
    }

    private void OnBreathingFlipMappingClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedAddress))
            return;

        var state = GetOrCreateBreathingState(_selectedAddress);
        var currentSettings = state.HasTelemetry ? state.LastTelemetry.Settings : state.Tracker.GetTelemetry().Settings;
        var flippedSettings = currentSettings with
        {
            InvertVolume = !currentSettings.InvertVolume,
        };

        state.Tracker.ApplySettings(flippedSettings, resetTracker: false);
        BreathingInvertVolumeCheckBox.IsChecked = flippedSettings.InvertVolume;
        CaptureBreathingTelemetry(_selectedAddress, state, pushChartValue: true);
        AddBreathingLog(
            _selectedAddress,
            flippedSettings.InvertVolume ? "inhale/exhale mapping flipped" : "inhale/exhale mapping restored",
            state);
        SetBreathingStatus(
            flippedSettings.InvertVolume ? "Inhale/exhale mapping flipped" : "Inhale/exhale mapping restored",
            "FocusBlueBrush");
        UpdateBreathingPanel(_selectedAddress);
    }

    private void SeedPreviewBreathingState((string Address, string Name, string Status)[] previewDevices)
    {
        var previewSettings = (AppBreathingDefaults with
        {
            AutoCalibrateOnUsefulSignal = false,
            UsefulSignalWindowSeconds = 2f,
            MinUsefulSamples = 40,
            CalibrationDurationSeconds = 1f,
            MinCalibrationSamples = 120,
            MinAdaptiveBoundsSamples = 160,
            AdaptiveBoundsWindowSeconds = 8f,
            AdaptiveBoundsUpdateIntervalSeconds = 0.25f,
        }).Clamp();

        for (int deviceIndex = 0; deviceIndex < previewDevices.Length; deviceIndex++)
        {
            var (address, _, status) = previewDevices[deviceIndex];
            var state = GetOrCreateBreathingState(address);
            state.Tracker.ApplySettings(previewSettings, resetTracker: true);
            bool connected = !string.IsNullOrWhiteSpace(status);
            state.Tracker.SetTransportConnected(connected);
            state.HasTelemetry = false;

            if (!connected)
            {
                CaptureBreathingTelemetry(address, state, pushChartValue: false);
                AddBreathingLog(address, "preview tracker idle", state);
                continue;
            }

            state.Tracker.BeginCalibration();
            long sensorTimestampNs = 0;
            for (int frameIndex = 0; frameIndex < 180; frameIndex++)
            {
                var samples = new AccSampleMg[8];
                for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                {
                    int globalIndex = (frameIndex * samples.Length) + sampleIndex;
                    double t = globalIndex / 100.0;
                    double phase = (t * (Math.PI * 2.0 / 5.8)) + (deviceIndex * 0.55);
                    short x = (short)Math.Round((Math.Sin(phase) * 52) + (Math.Cos(phase * 2.3) * 9));
                    short y = (short)Math.Round((Math.Cos(phase * 0.7) * 16) + (Math.Sin(phase * 1.9) * 5));
                    short z = (short)Math.Round((Math.Sin(phase + 0.4) * 68) + (Math.Cos(phase * 2.1) * 12));
                    samples[sampleIndex] = new AccSampleMg(x, y, z);
                }

                sensorTimestampNs += 80_000_000;
                state.Tracker.SubmitAccFrame(new PolarAccFrame(sensorTimestampNs, 0, samples));
                CaptureBreathingTelemetry(address, state, pushChartValue: true);
            }

            System.Threading.Thread.Sleep(1100);
            state.Tracker.Advance();

            for (int frameIndex = 180; frameIndex < 300; frameIndex++)
            {
                var samples = new AccSampleMg[8];
                for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                {
                    int globalIndex = (frameIndex * samples.Length) + sampleIndex;
                    double t = globalIndex / 100.0;
                    double phase = (t * (Math.PI * 2.0 / 5.8)) + (deviceIndex * 0.55);
                    short x = (short)Math.Round((Math.Sin(phase) * 52) + (Math.Cos(phase * 2.3) * 9));
                    short y = (short)Math.Round((Math.Cos(phase * 0.7) * 16) + (Math.Sin(phase * 1.9) * 5));
                    short z = (short)Math.Round((Math.Sin(phase + 0.4) * 68) + (Math.Cos(phase * 2.1) * 12));
                    samples[sampleIndex] = new AccSampleMg(x, y, z);
                }

                sensorTimestampNs += 80_000_000;
                state.Tracker.SubmitAccFrame(new PolarAccFrame(sensorTimestampNs, 0, samples));
                CaptureBreathingTelemetry(address, state, pushChartValue: true);
            }

            AddBreathingLog(address, "preview breathing tracker armed", state);
        }
    }
}
