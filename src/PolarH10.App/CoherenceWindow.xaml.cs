using System.Windows;
using System.Windows.Controls;

namespace PolarH10.App;

public partial class CoherenceWindow : Window
{
    public event EventHandler? ApplyTuningRequested;
    public event EventHandler? RestoreDefaultsRequested;
    public event EventHandler? ResetTrackerRequested;

    public CoherenceWindow()
    {
        InitializeComponent();
    }

    public Border ChartHostElement => CoherenceChartHost;
    public ListBox LogListElement => CoherenceLogList;

    public TextBlock SelectedDeviceTextBlock => SelectedCoherenceDeviceText;
    public TextBlock SummaryTextBlock => CoherenceWindowSummaryText;
    public TextBlock CoherenceValueTextBlock => CoherenceValueText;
    public TextBlock CoherenceStateValueTextBlock => CoherenceStateValueText;
    public TextBlock CoherenceTrackingValueTextBlock => CoherenceTrackingValueText;
    public TextBlock CoherenceConfidenceValueTextBlock => CoherenceConfidenceValueText;
    public TextBlock StatusTextBlock => CoherenceStatusText;
    public TextBlock RequirementTextBlock => CoherenceRequirementText;
    public TextBlock WarmupHintTextBlock => CoherenceWarmupHintText;
    public ProgressBar StabilizationProgressBar => CoherenceReadinessStabilizationBar;
    public TextBlock StabilizationProgressTextBlock => CoherenceReadinessStabilizationText;
    public ProgressBar CoverageProgressBar => CoherenceReadinessCoverageBar;
    public TextBlock CoverageProgressTextBlock => CoherenceReadinessCoverageText;
    public TextBlock RemainingTextBlock => CoherenceReadinessRemainingText;

    public TextBlock TrackingTextBlock => CoherenceTelemetryTrackingText;
    public TextBlock LastRrTextBlock => CoherenceTelemetryLastRrText;
    public TextBlock HeartbeatTextBlock => CoherenceTelemetryHeartbeatText;
    public TextBlock SampleCountTextBlock => CoherenceTelemetrySampleCountText;
    public TextBlock CoverageTextBlock => CoherenceTelemetryCoverageText;
    public TextBlock StabilizationTextBlock => CoherenceTelemetryStabilizationText;
    public TextBlock LastUpdateTextBlock => CoherenceTelemetryLastUpdateText;
    public TextBlock PeakFrequencyTextBlock => CoherenceTelemetryPeakFrequencyText;
    public TextBlock PeakBandPowerTextBlock => CoherenceTelemetryPeakBandPowerText;
    public TextBlock TotalPowerTextBlock => CoherenceTelemetryTotalPowerText;
    public TextBlock PaperRatioTextBlock => CoherenceTelemetryPaperRatioText;
    public TextBlock NormalizedScoreTextBlock => CoherenceTelemetryNormalizedScoreText;

    public TextBox MinimumIbiSamplesBoxElement => CoherenceMinimumIbiSamplesBox;
    public TextBox WindowSecondsBoxElement => CoherenceWindowSecondsBox;
    public TextBox SmoothingSpeedBoxElement => CoherenceSmoothingSpeedBox;
    public TextBox StaleTimeoutBoxElement => CoherenceStaleTimeoutBox;

    private void OnApplyTuningClick(object sender, RoutedEventArgs e)
        => ApplyTuningRequested?.Invoke(this, EventArgs.Empty);

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
        => RestoreDefaultsRequested?.Invoke(this, EventArgs.Empty);

    private void OnResetTrackerClick(object sender, RoutedEventArgs e)
        => ResetTrackerRequested?.Invoke(this, EventArgs.Empty);
}
