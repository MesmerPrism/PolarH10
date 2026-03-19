using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PolarH10.App;

/// <summary>
/// A lightweight multi-series rolling waveform chart rendered directly via OnRender.
/// Supports per-series independent normalization for overlay mode.
/// </summary>
public sealed class WaveformChart : FrameworkElement
{
    private sealed class SeriesState
    {
        public required string Name;
        public required Pen Pen;
        public required double[] Ring;
        public int Head;
        public int Count;
        public bool Visible = true;
    }

    private static readonly Color PaperColor = Color.FromRgb(0xFF, 0xFD, 0xF9);
    private static readonly Color SurfaceColor = Color.FromRgb(0xF6, 0xF1, 0xE9);
    private static readonly Color CarbonColor = Color.FromRgb(0x1F, 0x22, 0x26);
    private static readonly Color GraphiteColor = Color.FromRgb(0x5B, 0x63, 0x6F);
    private static readonly Color GridColor = Color.FromRgb(0xD7, 0xCF, 0xC4);
    private static readonly Color RuleColor = Color.FromRgb(0xC7, 0xC0, 0xB5);
    private static readonly Color AccentColor = Color.FromRgb(0x25, 0x8A, 0xCB);

    private static readonly Brush BackgroundBrush = CreateFrozenBrush(PaperColor);
    private static readonly Brush HeaderBrush = CreateFrozenBrush(SurfaceColor);
    private static readonly Brush LabelBrush = CreateFrozenBrush(CarbonColor);
    private static readonly Brush MutedBrush = CreateFrozenBrush(GraphiteColor);
    private static readonly Brush TitleBrush = CreateFrozenBrush(AccentColor);
    private static readonly Pen GridPen = CreateFrozenPen(GridColor, 0.8);
    private static readonly Pen BorderPen = CreateFrozenPen(RuleColor, 1.0);
    private static readonly Pen HeaderRulePen = CreateFrozenPen(RuleColor, 1.0);
    private static readonly Typeface UiTypeface = new(
        new FontFamily("Bahnschrift Condensed"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Condensed);
    private static readonly Typeface TitleTypeface = new(
        new FontFamily("Bahnschrift Condensed"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Condensed);

    private readonly List<SeriesState> _series = [];
    private bool _dirty;

    /// <summary>Chart title drawn in the top-left corner.</summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// When true, each series is normalized independently to fill the chart height.
    /// Useful for overlaying signals with different scales.
    /// </summary>
    public bool NormalizePerSeries { get; set; }

    public int AddSeries(string name, Color color, int capacity = 500, double thickness = 1.2)
    {
        var brush = CreateFrozenBrush(color);
        var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Miter };
        pen.Freeze();

        _series.Add(new SeriesState
        {
            Name = name,
            Pen = pen,
            Ring = new double[capacity],
        });
        return _series.Count - 1;
    }

    public void Push(int seriesIndex, double value)
    {
        var s = _series[seriesIndex];
        s.Ring[s.Head] = value;
        s.Head = (s.Head + 1) % s.Ring.Length;
        if (s.Count < s.Ring.Length)
            s.Count++;
        _dirty = true;
    }

    public void SetVisible(int seriesIndex, bool visible)
    {
        _series[seriesIndex].Visible = visible;
        _dirty = true;
    }

    public void Refresh()
    {
        if (_dirty)
        {
            _dirty = false;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double headerHeight = 22;
        double left = NormalizePerSeries ? 12 : 56;
        double top = headerHeight + 6;
        const double right = 10;
        const double bottom = 8;
        double chartWidth = width - left - right;
        double chartHeight = height - top - bottom;
        if (chartWidth <= 0 || chartHeight <= 0)
            return;

        dc.DrawRectangle(HeaderBrush, null, new Rect(0, 0, width, headerHeight));
        dc.DrawLine(HeaderRulePen, new Point(0, headerHeight), new Point(width, headerHeight));

        if (!HasVisibleData())
        {
            DrawGrid(dc, left, top, chartWidth, chartHeight);
            DrawEmptyState(dc, dpi, left, top, chartWidth, chartHeight);
            DrawHeaderLegend(dc, dpi, width);
            dc.DrawRectangle(null, BorderPen, new Rect(left, top, chartWidth, chartHeight));
            return;
        }

        if (NormalizePerSeries)
        {
            DrawNormalizedChart(dc, dpi, left, top, chartWidth, chartHeight);
        }
        else
        {
            DrawStandardChart(dc, dpi, left, top, chartWidth, chartHeight);
        }

        dc.DrawRectangle(null, BorderPen, new Rect(left, top, chartWidth, chartHeight));
    }

    private bool HasVisibleData()
    {
        foreach (var s in _series)
        {
            if (s.Visible && s.Count > 0)
                return true;
        }

        return false;
    }

    private void DrawNormalizedChart(
        DrawingContext dc,
        double dpi,
        double left,
        double top,
        double chartWidth,
        double chartHeight)
    {
        DrawGrid(dc, left, top, chartWidth, chartHeight);

        foreach (var s in _series)
        {
            if (!s.Visible || s.Count < 2)
                continue;

            GetSeriesRange(s, out double yMin, out double yMax);
            double yRange = yMax - yMin;
            if (yRange < 1e-9)
            {
                yMin -= 1;
                yMax += 1;
                yRange = yMax - yMin;
            }

            DrawSeries(dc, s, left, top, chartWidth, chartHeight, yMin, yRange);
        }

        DrawHeaderLegend(dc, dpi, ActualWidth);
    }

    private void DrawStandardChart(
        DrawingContext dc,
        double dpi,
        double left,
        double top,
        double chartWidth,
        double chartHeight)
    {
        double yMin = double.MaxValue;
        double yMax = double.MinValue;

        foreach (var s in _series)
        {
            if (!s.Visible || s.Count == 0)
                continue;

            GetSeriesRange(s, out double sMin, out double sMax);
            if (sMin < yMin)
                yMin = sMin;
            if (sMax > yMax)
                yMax = sMax;
        }

        if (yMin >= yMax)
        {
            yMin -= 1;
            yMax += 1;
        }

        double pad = (yMax - yMin) * 0.05;
        yMin -= pad;
        yMax += pad;
        double yRange = yMax - yMin;

        DrawGrid(dc, left, top, chartWidth, chartHeight);

        for (int i = 0; i <= 4; i++)
        {
            double y = top + chartHeight * i / 4.0;
            double value = yMax - (yMax - yMin) * i / 4.0;
            var label = MakeText(value.ToString("G4"), 9, MutedBrush, dpi, UiTypeface);
            dc.DrawText(label, new Point(left - label.Width - 6, y - label.Height / 2));
        }

        foreach (var s in _series)
        {
            if (!s.Visible || s.Count < 2)
                continue;

            DrawSeries(dc, s, left, top, chartWidth, chartHeight, yMin, yRange);
        }

        DrawHeaderLegend(dc, dpi, ActualWidth);
    }

    private void DrawEmptyState(
        DrawingContext dc,
        double dpi,
        double left,
        double top,
        double chartWidth,
        double chartHeight)
    {
        var stateText = MakeText("Awaiting telemetry", 12, MutedBrush, dpi, UiTypeface);
        var x = left + Math.Max((chartWidth - stateText.Width) / 2.0, 8);
        var y = top + Math.Max((chartHeight - stateText.Height) / 2.0, 8);
        dc.DrawText(stateText, new Point(x, y));
    }

    private void DrawHeaderLegend(DrawingContext dc, double dpi, double width)
    {
        double x = 10;

        if (!string.IsNullOrEmpty(Title))
        {
            var titleText = MakeText(Title, 11, TitleBrush, dpi, TitleTypeface);
            dc.DrawText(titleText, new Point(x, 3));
            x += titleText.Width + 18;
        }

        foreach (var s in _series)
        {
            if (!s.Visible || s.Count == 0)
                continue;

            var text = MakeText(s.Name, 10, s.Pen.Brush, dpi, UiTypeface);
            if (x + text.Width > width - 12)
                break;

            dc.DrawText(text, new Point(x, 3));
            x += text.Width + 18;
        }
    }

    private static void DrawGrid(DrawingContext dc, double left, double top, double chartWidth, double chartHeight)
    {
        for (int i = 0; i <= 4; i++)
        {
            double y = top + chartHeight * i / 4.0;
            dc.DrawLine(GridPen, new Point(left, y), new Point(left + chartWidth, y));
        }
    }

    private static void DrawSeries(
        DrawingContext dc,
        SeriesState s,
        double left,
        double top,
        double chartWidth,
        double chartHeight,
        double yMin,
        double yRange)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            double xStep = chartWidth / Math.Max(s.Ring.Length - 1, 1);
            for (int i = 0; i < s.Count; i++)
            {
                int idx = (s.Head - s.Count + i + s.Ring.Length) % s.Ring.Length;
                double x = left + xStep * i;
                double y = top + chartHeight * (1.0 - (s.Ring[idx] - yMin) / yRange);

                if (i == 0)
                    ctx.BeginFigure(new Point(x, y), false, false);
                else
                    ctx.LineTo(new Point(x, y), true, false);
            }
        }

        geom.Freeze();
        dc.DrawGeometry(null, s.Pen, geom);
    }

    private static void GetSeriesRange(SeriesState s, out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        for (int i = 0; i < s.Count; i++)
        {
            int idx = (s.Head - s.Count + i + s.Ring.Length) % s.Ring.Length;
            double v = s.Ring[idx];
            if (v < min)
                min = v;
            if (v > max)
                max = v;
        }
    }

    private static FormattedText MakeText(string text, double size, Brush brush, double dpi, Typeface typeface)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            dpi);
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
