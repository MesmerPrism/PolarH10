using System.Windows;
using System.Windows.Controls;

namespace PolarH10.App;

public partial class RawTelemetryWindow : Window
{
    public RawTelemetryWindow()
    {
        InitializeComponent();
    }

    public Border HrChartHostElement => HrChartHost;
    public Border RrChartHostElement => RrChartHost;
    public Border EcgChartHostElement => EcgChartHost;
    public Border AccXChartHostElement => AccXChartHost;
    public Border AccYChartHostElement => AccYChartHost;
    public Border AccZChartHostElement => AccZChartHost;
    public TextBlock SelectedDeviceTextBlock => SelectedDeviceText;
    public TextBlock TrackingSummaryTextBlock => TrackingSummaryText;
}
