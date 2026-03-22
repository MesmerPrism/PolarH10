using System.Collections.Generic;

namespace PolarH10.Protocol;

/// <summary>
/// Rolling IBI coherence estimator aligned to the fixed spectral constants described in
/// McCraty et al., <c>The Coherent Heart</c> (2006), while also retaining the normalized
/// 0..1 operator-facing adaptation used by the app.
/// </summary>
internal sealed class PolarCoherenceRrWindowCalculator
{
    private const int FftInputLength = 128;
    private const double DefaultWindowMs = 64000.0;

    private const double CoherenceBandLowerBound = 0.04;
    private const double CoherenceBandUpperBound = 0.26;
    private const double CoherencePeakWindowWidth = 0.03;
    private const double TotalBandLowerBound = 0.0033;
    private const double TotalBandUpperBound = 0.4;

    private const int StabilizationDatapoints = 5;

    private struct SamplePoint
    {
        public double XMs;
        public float IbiMs;
    }

    private readonly List<SamplePoint> _samples = new(256);
    private readonly double[] _hannWindow;
    private PolarRrSamplePoint[] _acceptedRrSamples = [];
    private PolarSeriesPoint[] _resampledTachogram = [];
    private PolarSpectrumPoint[] _powerSpectrum = [];
    private double _currentXMs;
    private int _consecutiveArtifactCount;
    private int _consecutiveValids;
    private double _windowLengthMs = DefaultWindowMs;

    public PolarCoherenceRrWindowCalculator()
    {
        _hannWindow = BuildHannWindow(FftInputLength);
        Reset();
    }

    public int SampleCount => _samples.Count;
    public int ConsecutiveValidCount => Math.Max(0, _consecutiveValids);
    public int StabilizationRequiredCount => StabilizationDatapoints;
    public float StabilizationProgress01 => Clamp01(_consecutiveValids / (float)StabilizationDatapoints);
    public double SpanMs => _samples.Count > 1 ? _samples[^1].XMs - _samples[0].XMs : 0.0;
    public float BufferCoverage01 => Clamp01((float)(SpanMs / Math.Max(1.0, _windowLengthMs)));

    public float LastPeakFrequencyHz { get; private set; }
    public float LastPeakBandPower { get; private set; }
    public float LastTotalPower { get; private set; }
    public float LastPaperCoherenceRatio { get; private set; }
    public float LastComputedCoherence01 { get; private set; }
    public float LastPeakWindowLowerHz { get; private set; }
    public float LastPeakWindowUpperHz { get; private set; }
    public float LastTotalBandLowerHz => (float)TotalBandLowerBound;
    public float LastTotalBandUpperHz => (float)TotalBandUpperBound;

    public static bool IsValidIbi(float ibiMs) => PolarRrIntervalValidator.IsValid(ibiMs);

    public void Reset()
    {
        _samples.Clear();
        _currentXMs = 0.0;
        _consecutiveArtifactCount = 0;
        _consecutiveValids = 0;
        _windowLengthMs = DefaultWindowMs;
        LastPeakFrequencyHz = 0f;
        LastPeakBandPower = 0f;
        LastTotalPower = 0f;
        LastPaperCoherenceRatio = 0f;
        LastComputedCoherence01 = 0f;
        LastPeakWindowLowerHz = 0f;
        LastPeakWindowUpperHz = 0f;
        _acceptedRrSamples = [];
        _resampledTachogram = [];
        _powerSpectrum = [];
    }

    public bool PushIbi(
        float ibiMs,
        float windowSeconds,
        int minimumSamples,
        out float coherence01,
        out float confidence01)
    {
        coherence01 = 0f;
        confidence01 = 0f;

        _windowLengthMs = Math.Max(16000.0, Math.Min(180000.0, Math.Max(16f, windowSeconds) * 1000.0));

        if (ibiMs <= 0f)
            return false;

        if (!IsValidIbi(ibiMs))
        {
            _consecutiveValids = 0;
            _consecutiveArtifactCount++;
            return false;
        }

        _consecutiveArtifactCount = 0;
        _consecutiveValids++;
        if (_consecutiveValids < StabilizationDatapoints)
            return false;

        _samples.Add(new SamplePoint { XMs = _currentXMs, IbiMs = ibiMs });
        _currentXMs += ibiMs;

        while (_samples.Count > 1 && _samples[1].XMs < _samples[^1].XMs - _windowLengthMs)
            _samples.RemoveAt(0);

        int minCount = Math.Max(5, minimumSamples);
        if (_samples.Count < minCount)
            return false;

        if (BufferCoverage01 < 0.99f)
            return false;

        if (!TryComputeCoherence(out coherence01))
            return false;

        float sampleCountConfidence = InverseLerp(minCount, minCount + 32, _samples.Count);
        float coverageConfidence = BufferCoverage01;
        float artifactPenalty = _consecutiveArtifactCount > 0 ? 0.75f : 1f;
        confidence01 = Clamp01((sampleCountConfidence * 0.55f + coverageConfidence * 0.45f) * artifactPenalty);
        return true;
    }

    private bool TryComputeCoherence(out float coherence01)
    {
        coherence01 = 0f;
        if (_samples.Count < 8)
            return false;

        double[] x = new double[_samples.Count];
        double[] y = new double[_samples.Count];
        for (int i = 0; i < _samples.Count; i++)
        {
            x[i] = _samples[i].XMs;
            y[i] = _samples[i].IbiMs;
        }

        _acceptedRrSamples = _samples
            .Select(static sample => new PolarRrSamplePoint(sample.XMs / 1000.0, sample.IbiMs))
            .ToArray();

        NaturalCubicSpline ibiSpline = NaturalCubicSpline.Fit(x, y);

        double[] resampled = new double[FftInputLength];
        double resamplingIntervalMs = _windowLengthMs / FftInputLength;
        double t = _samples[^1].XMs - _windowLengthMs;
        for (int i = 0; i < FftInputLength; i++)
        {
            t += resamplingIntervalMs;
            resampled[i] = ibiSpline.Interpolate(t);
        }

        _resampledTachogram = resampled
            .Select((value, index) => new PolarSeriesPoint(
                (_samples[^1].XMs - _windowLengthMs + ((index + 1) * resamplingIntervalMs)) / 1000.0,
                value))
            .ToArray();

        double mean = 0.0;
        for (int i = 0; i < resampled.Length; i++)
            mean += resampled[i];
        mean /= resampled.Length;

        for (int i = 0; i < resampled.Length; i++)
            resampled[i] = (resampled[i] - mean) * _hannWindow[i];

        double sampleRateHz = 1000.0 / Math.Max(1e-6, resamplingIntervalMs);
        BuildPowerSpectrum(resampled, sampleRateHz, out double[] frequencies, out double[] magnitudes);
        if (frequencies.Length < 4)
            return false;

        _powerSpectrum = frequencies
            .Select((frequency, index) => new PolarSpectrumPoint(frequency, magnitudes[index]))
            .ToArray();

        NaturalCubicSpline psdSpline = NaturalCubicSpline.Fit(frequencies, magnitudes);

        double peakFrequency = CoherenceBandLowerBound;
        double peakPower = 0.0;
        for (double f = CoherenceBandLowerBound; f <= CoherenceBandUpperBound; f += 0.001)
        {
            double power = psdSpline.Interpolate(f);
            if (power > peakPower)
            {
                peakPower = power;
                peakFrequency = f;
            }
        }

        double halfWindow = CoherencePeakWindowWidth * 0.5;
        double peakBandPower = psdSpline.Integrate(peakFrequency - halfWindow, peakFrequency + halfWindow);
        double totalPower = psdSpline.Integrate(TotalBandLowerBound, TotalBandUpperBound);
        double remainingPower = totalPower - peakBandPower;
        if (totalPower <= 0.0 || remainingPower <= 1e-9)
            return false;

        double paperRatio = Math.Pow(peakBandPower / remainingPower, 2.0);
        coherence01 = Clamp01((float)(peakBandPower / totalPower));
        LastPeakFrequencyHz = (float)peakFrequency;
        LastPeakWindowLowerHz = (float)(peakFrequency - halfWindow);
        LastPeakWindowUpperHz = (float)(peakFrequency + halfWindow);
        LastPeakBandPower = (float)peakBandPower;
        LastTotalPower = (float)totalPower;
        LastPaperCoherenceRatio = (float)Math.Min(float.MaxValue, paperRatio);
        LastComputedCoherence01 = coherence01;
        return true;
    }

    public PolarCoherenceDiagnostics GetDiagnostics()
    {
        return new PolarCoherenceDiagnostics(
            AcceptedRrSamples: _acceptedRrSamples,
            ResampledTachogram: _resampledTachogram,
            PowerSpectrum: _powerSpectrum,
            PeakFrequencyHz: LastPeakFrequencyHz,
            PeakWindowLowerHz: LastPeakWindowLowerHz,
            PeakWindowUpperHz: LastPeakWindowUpperHz,
            PeakBandPower: LastPeakBandPower,
            TotalBandLowerHz: LastTotalBandLowerHz,
            TotalBandUpperHz: LastTotalBandUpperHz,
            TotalBandPower: LastTotalPower,
            PaperCoherenceRatio: LastPaperCoherenceRatio,
            NormalizedCoherence01: LastComputedCoherence01);
    }

    private static void BuildPowerSpectrum(double[] samples, double sampleRateHz, out double[] frequencies, out double[] magnitudes)
    {
        int n = samples.Length;
        int bins = n / 2 + 1;
        frequencies = new double[bins];
        magnitudes = new double[bins];

        for (int k = 0; k < bins; k++)
        {
            double re = 0.0;
            double im = 0.0;
            for (int t = 0; t < n; t++)
            {
                double angle = -2.0 * Math.PI * k * t / n;
                re += samples[t] * Math.Cos(angle);
                im += samples[t] * Math.Sin(angle);
            }

            frequencies[k] = k * sampleRateHz / n;
            magnitudes[k] = re * re + im * im;
        }
    }

    private static double[] BuildHannWindow(int count)
    {
        double[] window = new double[count];
        if (count <= 1)
        {
            window[0] = 1.0;
            return window;
        }

        for (int i = 0; i < count; i++)
            window[i] = 0.5 * (1.0 - Math.Cos((2.0 * Math.PI * i) / (count - 1)));

        return window;
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static float InverseLerp(int a, int b, int value)
    {
        if (a == b)
            return 0f;

        float t = (value - a) / (float)(b - a);
        return Clamp01(t);
    }

    private sealed class NaturalCubicSpline
    {
        private readonly double[] _x;
        private readonly double[] _y;
        private readonly double[] _b;
        private readonly double[] _c;
        private readonly double[] _d;

        private NaturalCubicSpline(double[] x, double[] y, double[] b, double[] c, double[] d)
        {
            _x = x;
            _y = y;
            _b = b;
            _c = c;
            _d = d;
        }

        public double Interpolate(double value)
        {
            int segment = LocateSegment(value);
            double offset = value - _x[segment];
            return _y[segment]
                + _b[segment] * offset
                + _c[segment] * offset * offset
                + _d[segment] * offset * offset * offset;
        }

        public double Integrate(double start, double end)
        {
            if (double.IsNaN(start) || double.IsNaN(end))
                return 0.0;

            if (Math.Abs(end - start) < double.Epsilon)
                return 0.0;

            bool reversed = end < start;
            if (reversed)
            {
                double tmp = start;
                start = end;
                end = tmp;
            }

            start = ClampToRange(start);
            end = ClampToRange(end);
            if (end <= start)
                return 0.0;

            double total = 0.0;
            int segment = LocateSegment(start);
            while (segment < _x.Length - 1 && start < end)
            {
                double segmentStart = Math.Max(start, _x[segment]);
                double segmentEnd = Math.Min(end, _x[segment + 1]);
                total += IntegrateSegment(segment, segmentStart, segmentEnd);
                start = segmentEnd;
                segment++;
            }

            return reversed ? -total : total;
        }

        private double IntegrateSegment(int segment, double absoluteStart, double absoluteEnd)
        {
            double localStart = absoluteStart - _x[segment];
            double localEnd = absoluteEnd - _x[segment];
            return EvaluatePrimitive(segment, localEnd) - EvaluatePrimitive(segment, localStart);
        }

        private double EvaluatePrimitive(int segment, double t)
        {
            double a = _y[segment];
            double b = _b[segment];
            double c = _c[segment];
            double d = _d[segment];
            double t2 = t * t;
            double t3 = t2 * t;
            double t4 = t3 * t;
            return a * t + b * t2 * 0.5 + c * t3 / 3.0 + d * t4 * 0.25;
        }

        private int LocateSegment(double value)
        {
            int lastIndex = _x.Length - 1;
            if (value <= _x[0])
                return 0;
            if (value >= _x[lastIndex])
                return lastIndex - 1;

            int low = 0;
            int high = lastIndex - 1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (value < _x[mid])
                {
                    high = mid - 1;
                }
                else if (value > _x[mid + 1])
                {
                    low = mid + 1;
                }
                else
                {
                    return mid;
                }
            }

            return Math.Max(0, Math.Min(lastIndex - 1, low));
        }

        private double ClampToRange(double value)
        {
            if (value <= _x[0])
                return _x[0];
            if (value >= _x[^1])
                return _x[^1];
            return value;
        }

        public static NaturalCubicSpline Fit(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            if (x.Count != y.Count)
                throw new ArgumentException("X and Y collections must have the same length.");
            if (x.Count < 2)
                throw new ArgumentException("At least two points are required to build a spline.");

            int n = x.Count;
            double[] h = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
                h[i] = x[i + 1] - x[i];

            double[] alpha = new double[n];
            for (int i = 1; i < n - 1; i++)
            {
                double numerator = (y[i + 1] - y[i]) / h[i] - (y[i] - y[i - 1]) / h[i - 1];
                alpha[i] = 3.0 * numerator;
            }

            double[] c = new double[n];
            double[] l = new double[n];
            double[] mu = new double[n];
            double[] z = new double[n];

            l[0] = 1.0;
            mu[0] = 0.0;
            z[0] = 0.0;

            for (int i = 1; i < n - 1; i++)
            {
                l[i] = 2.0 * (x[i + 1] - x[i - 1]) - h[i - 1] * mu[i - 1];
                mu[i] = h[i] / l[i];
                z[i] = (alpha[i] - h[i - 1] * z[i - 1]) / l[i];
            }

            l[n - 1] = 1.0;
            z[n - 1] = 0.0;
            c[n - 1] = 0.0;

            double[] b = new double[n - 1];
            double[] d = new double[n - 1];

            for (int i = n - 2; i >= 0; i--)
            {
                c[i] = z[i] - mu[i] * c[i + 1];
                b[i] = (y[i + 1] - y[i]) / h[i] - h[i] * (c[i + 1] + 2.0 * c[i]) / 3.0;
                d[i] = (c[i + 1] - c[i]) / (3.0 * h[i]);
            }

            double[] xs = new double[n];
            double[] ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = x[i];
                ys[i] = y[i];
            }

            return new NaturalCubicSpline(xs, ys, b, c, d);
        }
    }
}
