using System.Windows;
using System.Windows.Controls;

namespace PolarH10.App;

public partial class MainWindow
{
    private static readonly TelemetryMetricOption[] TelemetryMetricOptions =
    [
        new(TelemetryMetric.HeartRate, "Heart rate"),
        new(TelemetryMetric.RrInterval, "RR intervals"),
        new(TelemetryMetric.Breathing, "Breathing"),
        new(TelemetryMetric.Coherence, "Coherence"),
        new(TelemetryMetric.CoherenceConfidence, "Coherence confidence"),
        new(TelemetryMetric.HrvRmssd, "HRV (RMSSD)"),
        new(TelemetryMetric.BreathIntervalEntropy, "Breath interval entropy"),
        new(TelemetryMetric.BreathAmplitudeEntropy, "Breath amplitude entropy"),
        new(TelemetryMetric.Ecg, "ECG"),
        new(TelemetryMetric.AccX, "ACC X"),
        new(TelemetryMetric.AccY, "ACC Y"),
        new(TelemetryMetric.AccZ, "ACC Z"),
    ];

    private sealed class TelemetrySummarySlot
    {
        public required string Label;
        public required Border Host;
        public required ComboBox Selector;
        public required ChartAxisOptions AxisOptions;
        public TelemetryMetric Metric;
        public WaveformChart Chart = null!;
        public readonly Dictionary<string, int> SeriesByAddress = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly List<TelemetrySummarySlot> _telemetrySummarySlots = [];
    private bool _initializingTelemetryMetricSelectors;
    private RawTelemetryWindow? _rawTelemetryWindow;

    private void InitializeTelemetrySummarySlots()
    {
        _telemetrySummarySlots.Clear();

        _telemetrySummarySlots.Add(CreateTelemetrySummarySlot(
            "Plot A",
            TelemetryPlot1ChartHost,
            TelemetryPlot1MetricBox,
            TelemetryMetric.HeartRate));
        _telemetrySummarySlots.Add(CreateTelemetrySummarySlot(
            "Plot B",
            TelemetryPlot2ChartHost,
            TelemetryPlot2MetricBox,
            TelemetryMetric.RrInterval));
        _telemetrySummarySlots.Add(CreateTelemetrySummarySlot(
            "Plot C",
            TelemetryPlot3ChartHost,
            TelemetryPlot3MetricBox,
            TelemetryMetric.Breathing));
        _telemetrySummarySlots.Add(CreateTelemetrySummarySlot(
            "Plot D",
            TelemetryPlot4ChartHost,
            TelemetryPlot4MetricBox,
            TelemetryMetric.Coherence));

        _initializingTelemetryMetricSelectors = true;
        try
        {
            foreach (TelemetrySummarySlot slot in _telemetrySummarySlots)
            {
                slot.Selector.ItemsSource = TelemetryMetricOptions;
                slot.Selector.DisplayMemberPath = nameof(TelemetryMetricOption.Label);
                slot.Selector.SelectedValuePath = nameof(TelemetryMetricOption.Metric);
                slot.Selector.SelectedValue = slot.Metric;
            }
        }
        finally
        {
            _initializingTelemetryMetricSelectors = false;
        }

        RebuildTelemetrySummaryCharts();
    }

    private static TelemetrySummarySlot CreateTelemetrySummarySlot(
        string label,
        Border host,
        ComboBox selector,
        TelemetryMetric metric)
    {
        return new TelemetrySummarySlot
        {
            Label = label,
            Host = host,
            Selector = selector,
            AxisOptions = new ChartAxisOptions
            {
                ManualYAxisSymmetric = MetricUsesSymmetricYAxis(metric),
            },
            Metric = metric,
        };
    }

    private void RebuildTelemetrySummaryCharts()
    {
        foreach (TelemetrySummarySlot slot in _telemetrySummarySlots)
            RebuildTelemetrySummarySlot(slot);
    }

    private void RebuildTelemetrySummarySlot(TelemetrySummarySlot slot)
    {
        slot.SeriesByAddress.Clear();
        slot.AxisOptions.ManualYAxisSymmetric = MetricUsesSymmetricYAxis(slot.Metric);
        slot.Chart = CreateChart(GetTelemetryMetricTitle(slot.Metric), slot.AxisOptions);

        List<string> trackedAddresses = GetTrackedChartAddresses();
        for (int index = 0; index < trackedAddresses.Count; index++)
        {
            string address = trackedAddresses[index];
            int seriesIndex = slot.Chart.AddSeries(
                CompactDisplayName(address),
                DeviceTraceColor(index),
                GetTelemetryMetricCapacity(slot.Metric));
            slot.SeriesByAddress[address] = seriesIndex;

            if (_chartStates.TryGetValue(address, out DeviceChartState? state))
                ReplayMetricSeries(slot.Chart, seriesIndex, state, slot.Metric);
        }

        SetChartHostContent(slot.Host, slot.Chart, slot.AxisOptions);
        slot.Chart.Refresh();
    }

    private void ReplayMetricSeries(WaveformChart chart, int seriesIndex, DeviceChartState state, TelemetryMetric metric)
    {
        IEnumerable<float> values = GetMetricValues(state, metric);
        foreach (float value in values)
            chart.Push(seriesIndex, value);
    }

    private static IEnumerable<float> GetMetricValues(DeviceChartState state, TelemetryMetric metric) => metric switch
    {
        TelemetryMetric.HeartRate => state.HrValues,
        TelemetryMetric.RrInterval => state.RrValues,
        TelemetryMetric.Ecg => state.EcgValues,
        TelemetryMetric.Breathing => state.BreathingValues,
        TelemetryMetric.Coherence => state.CoherenceValues,
        TelemetryMetric.CoherenceConfidence => state.CoherenceConfidenceValues,
        TelemetryMetric.HrvRmssd => state.HrvRmssdValues,
        TelemetryMetric.BreathIntervalEntropy => state.BreathIntervalEntropyValues,
        TelemetryMetric.BreathAmplitudeEntropy => state.BreathAmplitudeEntropyValues,
        TelemetryMetric.AccX => state.AccXValues,
        TelemetryMetric.AccY => state.AccYValues,
        TelemetryMetric.AccZ => state.AccZValues,
        _ => [],
    };

    private void PushMetricSampleToTelemetryCharts(string address, TelemetryMetric metric, float value)
    {
        foreach (TelemetrySummarySlot slot in _telemetrySummarySlots)
        {
            if (slot.Metric != metric)
                continue;

            if (slot.SeriesByAddress.TryGetValue(address, out int seriesIndex))
                slot.Chart.Push(seriesIndex, value);
        }
    }

    private static string GetTelemetryMetricTitle(TelemetryMetric metric) => metric switch
    {
        TelemetryMetric.HeartRate => "Heart rate",
        TelemetryMetric.RrInterval => "RR intervals",
        TelemetryMetric.Ecg => "ECG",
        TelemetryMetric.Breathing => "Breathing",
        TelemetryMetric.Coherence => "Coherence",
        TelemetryMetric.CoherenceConfidence => "Coherence confidence",
        TelemetryMetric.HrvRmssd => "HRV (RMSSD)",
        TelemetryMetric.BreathIntervalEntropy => "Breath interval entropy",
        TelemetryMetric.BreathAmplitudeEntropy => "Breath amplitude entropy",
        TelemetryMetric.AccX => "ACC X",
        TelemetryMetric.AccY => "ACC Y",
        TelemetryMetric.AccZ => "ACC Z",
        _ => "Telemetry",
    };

    private static int GetTelemetryMetricCapacity(TelemetryMetric metric) => metric switch
    {
        TelemetryMetric.HeartRate => 120,
        TelemetryMetric.RrInterval => 120,
        TelemetryMetric.Ecg => 650,
        TelemetryMetric.Breathing => 360,
        TelemetryMetric.Coherence => 360,
        TelemetryMetric.CoherenceConfidence => 360,
        TelemetryMetric.HrvRmssd => 360,
        TelemetryMetric.BreathIntervalEntropy => 360,
        TelemetryMetric.BreathAmplitudeEntropy => 360,
        TelemetryMetric.AccX => 500,
        TelemetryMetric.AccY => 500,
        TelemetryMetric.AccZ => 500,
        _ => 360,
    };

    private static bool MetricUsesSymmetricYAxis(TelemetryMetric metric) => metric is
        TelemetryMetric.Ecg or
        TelemetryMetric.AccX or
        TelemetryMetric.AccY or
        TelemetryMetric.AccZ;

    private void RefreshTelemetrySummaryCharts()
    {
        foreach (TelemetrySummarySlot slot in _telemetrySummarySlots)
            slot.Chart?.Refresh();
    }

    private void OnTelemetryPlotMetricChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingTelemetryMetricSelectors ||
            sender is not ComboBox comboBox ||
            comboBox.SelectedValue is not TelemetryMetric metric)
        {
            return;
        }

        TelemetrySummarySlot? slot = _telemetrySummarySlots.FirstOrDefault(candidate => ReferenceEquals(candidate.Selector, comboBox));
        if (slot is null)
            return;

        slot.Metric = metric;
        RebuildTelemetrySummarySlot(slot);
    }

    private RawTelemetryWindow EnsureRawTelemetryWindow()
    {
        if (_rawTelemetryWindow != null)
            return _rawTelemetryWindow;

        _rawTelemetryWindow = new RawTelemetryWindow();
        RawTab.Content = DetachWindowContent(_rawTelemetryWindow);

        UpdateRawTelemetryWindow();
        RebuildLiveCharts();
        return _rawTelemetryWindow;
    }

    private void ApplyRawTelemetryWindowChartHosts()
    {
        if (_rawTelemetryWindow == null)
            return;

        SetChartHostContent(_rawTelemetryWindow.HrChartHostElement, _hrChart, _hrChartAxisOptions);
        SetChartHostContent(_rawTelemetryWindow.RrChartHostElement, _rrChart, _rrChartAxisOptions);
        SetChartHostContent(_rawTelemetryWindow.EcgChartHostElement, _ecgChart, _ecgChartAxisOptions);
        SetChartHostContent(_rawTelemetryWindow.AccXChartHostElement, _accXChart, _accXChartAxisOptions);
        SetChartHostContent(_rawTelemetryWindow.AccYChartHostElement, _accYChart, _accYChartAxisOptions);
        SetChartHostContent(_rawTelemetryWindow.AccZChartHostElement, _accZChart, _accZChartAxisOptions);
    }

    private void UpdateRawTelemetryWindow()
    {
        if (_rawTelemetryWindow == null)
            return;

        _rawTelemetryWindow.SelectedDeviceTextBlock.Text = string.IsNullOrWhiteSpace(_selectedAddress)
            ? "No device selected"
            : DisplayName(_selectedAddress);
        _rawTelemetryWindow.TrackingSummaryTextBlock.Text = BuildRawTelemetrySummaryText();
        _rawTelemetryWindow.Title = string.IsNullOrWhiteSpace(_selectedAddress)
            ? "Polar H10 // Raw Telemetry"
            : $"Polar H10 // Raw Telemetry // {CompactDisplayName(_selectedAddress)}";
    }

    private string BuildRawTelemetrySummaryText()
    {
        List<string> tracked = GetTrackedChartAddresses();
        if (tracked.Count == 0)
            return "No chart targets selected.";

        if (_trackingFollowsSelection)
            return $"Following {CompactDisplayName(tracked[0])} across HR, RR, ECG, and ACC.";

        return tracked.Count switch
        {
            1 => $"Tracking {CompactDisplayName(tracked[0])} across the raw telemetry plots.",
            2 => $"Tracking {CompactDisplayName(tracked[0])} and {CompactDisplayName(tracked[1])}.",
            _ => $"Tracking {CompactDisplayName(tracked[0])}, {CompactDisplayName(tracked[1])}, +{tracked.Count - 2} more.",
        };
    }

    private void OnOpenRawTelemetryWindowClick(object sender, RoutedEventArgs e)
    {
        EnsureRawTelemetryWindow();
        DeviceTabControl.SelectedItem = RawTab;
        UpdateRawTelemetryWindow();
        RebuildLiveCharts();
    }

    private void OnOpenBreathingTabClick(object sender, RoutedEventArgs e)
    {
        DeviceTabControl.SelectedItem = BreathingTab;
    }
}
