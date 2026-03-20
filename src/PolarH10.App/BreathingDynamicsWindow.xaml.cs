using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace PolarH10.App;

public partial class BreathingDynamicsWindow : Window
{
    public event EventHandler? ApplyTuningRequested;
    public event EventHandler? RestoreDefaultsRequested;
    public event EventHandler? ResetTrackerRequested;

    public BreathingDynamicsWindow()
    {
        InitializeComponent();
    }

    public Border ChartHostElement => BreathingDynamicsChartHost;
    public ListBox LogListElement => BreathingDynamicsLogList;

    public TextBlock SelectedDeviceTextBlock => SelectedBreathingDynamicsDeviceText;
    public TextBlock SummaryTextBlock => BreathingDynamicsSummaryText;
    public TextBlock IntervalEntropyValueTextBlock => IntervalEntropyValueText;
    public TextBlock IntervalEntropyHintTextBlock => IntervalEntropyHintText;
    public TextBlock AmplitudeEntropyValueTextBlock => AmplitudeEntropyValueText;
    public TextBlock AmplitudeEntropyHintTextBlock => AmplitudeEntropyHintText;
    public TextBlock StatusTextBlock => BreathingDynamicsStatusText;
    public TextBlock TrackingTextBlock => BreathingDynamicsTrackingText;
    public TextBlock LastWaveformTextBlock => BreathingDynamicsLastWaveformText;
    public TextBlock LastBreathTextBlock => BreathingDynamicsLastBreathText;
    public TextBlock ExtremaCountTextBlock => BreathingDynamicsExtremaCountText;
    public TextBlock ConfidenceTextBlock => BreathingDynamicsConfidenceText;
    public TextBlock IntervalCountTextBlock => BreathingDynamicsIntervalCountText;
    public TextBlock AmplitudeCountTextBlock => BreathingDynamicsAmplitudeCountText;
    public TextBlock StabilizationTextBlock => BreathingDynamicsStabilizationText;
    public TextBlock IntervalReadinessTextBlock => BreathingDynamicsIntervalReadinessText;
    public TextBlock AmplitudeReadinessTextBlock => BreathingDynamicsAmplitudeReadinessText;

    public TextBlock IntervalMeanTextBlock => IntervalMeanText;
    public TextBlock IntervalSdTextBlock => IntervalSdText;
    public TextBlock IntervalCvTextBlock => IntervalCvText;
    public TextBlock IntervalAcwTextBlock => IntervalAcwText;
    public TextBlock IntervalPsdSlopeTextBlock => IntervalPsdSlopeText;
    public TextBlock IntervalLzcTextBlock => IntervalLzcText;
    public TextBlock IntervalSampleEntropyTextBlock => IntervalSampleEntropyText;
    public TextBlock IntervalMultiscaleEntropyTextBlock => IntervalMultiscaleEntropyText;

    public TextBlock AmplitudeMeanTextBlock => AmplitudeMeanText;
    public TextBlock AmplitudeSdTextBlock => AmplitudeSdText;
    public TextBlock AmplitudeCvTextBlock => AmplitudeCvText;
    public TextBlock AmplitudeAcwTextBlock => AmplitudeAcwText;
    public TextBlock AmplitudePsdSlopeTextBlock => AmplitudePsdSlopeText;
    public TextBlock AmplitudeLzcTextBlock => AmplitudeLzcText;
    public TextBlock AmplitudeSampleEntropyTextBlock => AmplitudeSampleEntropyText;
    public TextBlock AmplitudeMultiscaleEntropyTextBlock => AmplitudeMultiscaleEntropyText;

    public TextBox TurningThresholdBoxElement => DynamicsTurningThresholdBox;
    public TextBox MinimumSpacingBoxElement => DynamicsMinSpacingBox;
    public TextBox MinimumExcursionBoxElement => DynamicsMinExcursionBox;
    public TextBox RetainedBreathsBoxElement => DynamicsRetainedBreathsBox;
    public TextBox MinimumBasicBreathsBoxElement => DynamicsMinBasicBreathsBox;
    public TextBox MinimumEntropyBreathsBoxElement => DynamicsMinEntropyBreathsBox;
    public TextBox FullConfidenceBreathsBoxElement => DynamicsFullConfidenceBox;
    public TextBox StaleTimeoutBoxElement => DynamicsStaleTimeoutBox;
    public TextBox SampleEntropyDimensionBoxElement => DynamicsSampleEntropyDimensionBox;
    public TextBox SampleEntropyDelayBoxElement => DynamicsSampleEntropyDelayBox;
    public TextBox SampleEntropyToleranceBoxElement => DynamicsSampleEntropyToleranceBox;
    public TextBox MultiscaleEntropyDimensionBoxElement => DynamicsMultiscaleEntropyDimensionBox;
    public TextBox MultiscaleEntropyDelayBoxElement => DynamicsMultiscaleEntropyDelayBox;
    public TextBox MultiscaleEntropyToleranceBoxElement => DynamicsMultiscaleEntropyToleranceBox;
    public TextBox MultiscaleEntropyMaxScaleBoxElement => DynamicsMseMaxScaleBox;

    private void OnApplyTuningClick(object sender, RoutedEventArgs e)
        => ApplyTuningRequested?.Invoke(this, EventArgs.Empty);

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
        => RestoreDefaultsRequested?.Invoke(this, EventArgs.Empty);

    private void OnResetTrackerClick(object sender, RoutedEventArgs e)
        => ResetTrackerRequested?.Invoke(this, EventArgs.Empty);

    private void OnReferenceNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is null)
            return;

        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
        e.Handled = true;
    }
}
