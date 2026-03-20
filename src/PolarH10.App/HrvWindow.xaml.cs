using System.Windows;
using System.Windows.Controls;

namespace PolarH10.App;

public partial class HrvWindow : Window
{
    public event EventHandler? ApplyTuningRequested;
    public event EventHandler? RestoreDefaultsRequested;
    public event EventHandler? ResetTrackerRequested;

    public HrvWindow()
    {
        InitializeComponent();
    }

    public Border ChartHostElement => HrvChartHost;
    public ListBox LogListElement => HrvLogList;

    public TextBlock SelectedDeviceTextBlock => SelectedHrvDeviceText;
    public TextBlock SummaryTextBlock => HrvWindowSummaryText;
    public TextBlock HrvValueTextBlock => HrvValueText;
    public TextBlock HrvStateValueTextBlock => HrvStateValueText;
    public TextBlock HrvTrackingValueTextBlock => HrvTrackingValueText;
    public TextBlock HrvSupportValueTextBlock => HrvSupportValueText;
    public TextBlock StatusTextBlock => HrvStatusText;
    public TextBlock RequirementTextBlock => HrvRequirementText;
    public TextBlock WarmupHintTextBlock => HrvWarmupHintText;
    public ProgressBar SampleProgressBar => HrvReadinessSamplesBar;
    public TextBlock SampleProgressTextBlock => HrvReadinessSamplesText;
    public ProgressBar CoverageProgressBar => HrvReadinessCoverageBar;
    public TextBlock CoverageProgressTextBlock => HrvReadinessCoverageText;
    public TextBlock RemainingTextBlock => HrvReadinessRemainingText;

    public TextBlock TrackingTextBlock => HrvTelemetryTrackingText;
    public TextBlock LastRrTextBlock => HrvTelemetryLastRrText;
    public TextBlock HeartbeatTextBlock => HrvTelemetryHeartbeatText;
    public TextBlock SampleCountTextBlock => HrvTelemetrySampleCountText;
    public TextBlock WindowCountTextBlock => HrvTelemetryWindowCountText;
    public TextBlock CoverageTextBlock => HrvTelemetryCoverageText;
    public TextBlock LastUpdateTextBlock => HrvTelemetryLastUpdateText;
    public TextBlock MeanNnTextBlock => HrvTelemetryMeanNnText;
    public TextBlock MeanHrTextBlock => HrvTelemetryMeanHrText;
    public TextBlock SdnnTextBlock => HrvTelemetrySdnnText;
    public TextBlock Pnn50TextBlock => HrvTelemetryPnn50Text;
    public TextBlock Sd1TextBlock => HrvTelemetrySd1Text;
    public TextBlock LnRmssdTextBlock => HrvTelemetryLnRmssdText;

    public TextBox MinimumRrSamplesBoxElement => HrvMinimumRrSamplesBox;
    public TextBox WindowSecondsBoxElement => HrvWindowSecondsBox;
    public TextBox StaleTimeoutBoxElement => HrvStaleTimeoutBox;

    private void OnApplyTuningClick(object sender, RoutedEventArgs e)
        => ApplyTuningRequested?.Invoke(this, EventArgs.Empty);

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
        => RestoreDefaultsRequested?.Invoke(this, EventArgs.Empty);

    private void OnResetTrackerClick(object sender, RoutedEventArgs e)
        => ResetTrackerRequested?.Invoke(this, EventArgs.Empty);
}
