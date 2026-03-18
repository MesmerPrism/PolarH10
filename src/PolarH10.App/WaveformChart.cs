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

    private readonly List<SeriesState> _series = [];
    private static readonly Typeface Typeface = new("Segoe UI");
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
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round };
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
        if (s.Count < s.Ring.Length) s.Count++;
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
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Background
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));

        double left = NormalizePerSeries ? 10 : 52;
        double top = 20, right = 10, bottom = 5;
        double cw = w - left - right, ch = h - top - bottom;
        if (cw <= 0 || ch <= 0) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var gridPen = CreateFrozenPen(Colors.LightGray, 0.5);
        var borderPen = CreateFrozenPen(Colors.LightGray, 1);

        if (NormalizePerSeries)
        {
            // Grid lines (no Y labels in normalized mode)
            for (int i = 0; i <= 4; i++)
            {
                double y = top + ch * i / 4.0;
                dc.DrawLine(gridPen, new Point(left, y), new Point(left + cw, y));
            }

            // Draw each series with its own Y range
            foreach (var s in _series)
            {
                if (!s.Visible || s.Count < 2) continue;

                GetSeriesRange(s, out double yMin, out double yMax);
                double yRange = yMax - yMin;
                if (yRange < 1e-9) { yMin -= 1; yMax += 1; yRange = yMax - yMin; }

                DrawSeries(dc, s, left, top, cw, ch, yMin, yRange);
            }

            // Legend
            double lx = left + 4;
            foreach (var s in _series)
            {
                if (!s.Visible) continue;
                double currentVal = s.Count > 0 ? s.Ring[(s.Head - 1 + s.Ring.Length) % s.Ring.Length] : 0;
                var text = MakeText($"{s.Name}: {currentVal:F0}", 9, s.Pen.Brush, dpi);
                dc.DrawText(text, new Point(lx, 2));
                lx += text.Width + 16;
            }
        }
        else
        {
            // Global Y range from all visible series
            double yMin = double.MaxValue, yMax = double.MinValue;
            foreach (var s in _series)
            {
                if (!s.Visible || s.Count == 0) continue;
                GetSeriesRange(s, out double sMin, out double sMax);
                if (sMin < yMin) yMin = sMin;
                if (sMax > yMax) yMax = sMax;
            }
            if (yMin >= yMax) { yMin -= 1; yMax += 1; }
            double pad = (yMax - yMin) * 0.05;
            yMin -= pad; yMax += pad;
            double yRange = yMax - yMin;

            // Grid lines + Y labels
            for (int i = 0; i <= 4; i++)
            {
                double y = top + ch * i / 4.0;
                dc.DrawLine(gridPen, new Point(left, y), new Point(left + cw, y));
                double val = yMax - (yMax - yMin) * i / 4.0;
                var label = MakeText(val.ToString("G4"), 9, Brushes.Gray, dpi);
                dc.DrawText(label, new Point(left - label.Width - 4, y - label.Height / 2));
            }

            foreach (var s in _series)
            {
                if (!s.Visible || s.Count < 2) continue;
                DrawSeries(dc, s, left, top, cw, ch, yMin, yRange);
            }

            // Title
            if (!string.IsNullOrEmpty(Title))
            {
                var titleText = MakeText(Title, 11, Brushes.DarkGray, dpi);
                dc.DrawText(titleText, new Point(left + 4, 2));
            }
        }

        dc.DrawRectangle(null, borderPen, new Rect(left, top, cw, ch));
    }

    private static void DrawSeries(DrawingContext dc, SeriesState s,
        double left, double top, double cw, double ch, double yMin, double yRange)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            double xStep = cw / Math.Max(s.Ring.Length - 1, 1);
            for (int i = 0; i < s.Count; i++)
            {
                int idx = (s.Head - s.Count + i + s.Ring.Length) % s.Ring.Length;
                double x = left + xStep * i;
                double y = top + ch * (1.0 - (s.Ring[idx] - yMin) / yRange);

                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geom.Freeze();
        dc.DrawGeometry(null, s.Pen, geom);
    }

    private static void GetSeriesRange(SeriesState s, out double min, out double max)
    {
        min = double.MaxValue; max = double.MinValue;
        for (int i = 0; i < s.Count; i++)
        {
            int idx = (s.Head - s.Count + i + s.Ring.Length) % s.Ring.Length;
            double v = s.Ring[idx];
            if (v < min) min = v;
            if (v > max) max = v;
        }
    }

    private static FormattedText MakeText(string text, double size, Brush brush, double dpi)
    {
        return new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface, size, brush, dpi);
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
