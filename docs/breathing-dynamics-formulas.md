---
title: Breathing Dynamics Formula Sheet
description: Exact feature definitions, entropy settings, and reference alignment notes for the app's breathing-dynamics window.
summary: Use this sheet when you want the derived breath interval and amplitude series, plus ACW50, PSD slope, LZC, sample entropy, and multiscale entropy written out directly.
nav_label: Dynamics Formulas
nav_group: Internals
nav_order: 74
---

# Breathing Dynamics Formula Sheet

<p>
  <a class="button primary" href="assets/reference-markdown/breathing-dynamics-formulas.md">Download Markdown</a>
  <a class="button" href="assets/formula-sheets/breathing-dynamics-formulas.pdf">Download PDF</a>
</p>

## Data Path

- input waveform: calibrated `VolumeBase01` from the Breathing tracker
- event extraction: alternating peaks and troughs from that calibrated waveform
- derived series:
  - `interval series`: spacing between same-polarity extrema
  - `amplitude series`: absolute peak-trough excursion

This is the main adaptation relative to the cited paper: the paper uses a
respiration-belt signal, while this app uses the repo's calibrated ACC
breathing waveform.

## Derived Breath Series

For accepted extrema times `t_k` and values `x_k`:

```latex
\text{interval}_j = t_j - t_{j-1}
```

Where the two extrema belong to the same polarity family, for example peak to
peak or trough to trough.

```latex
\text{amplitude}_j = |x_j - x_{j-1}|
```

Where the two extrema are alternating peak and trough partners in a completed
breath excursion.

## Basic Statistics

For either derived series `s_1 ... s_n`:

```latex
\mu = \frac{1}{n}\sum_{i=1}^{n}s_i
```

```latex
SD = \sqrt{\frac{1}{n-1}\sum_{i=1}^{n}(s_i - \mu)^2}
```

```latex
CV = \frac{SD}{|\mu|}
```

## ACW50

The autocorrelation window is the first lag where the normalized
autocorrelation falls below `0.5`:

```latex
ACW50 = \min \left\{ \ell \ge 1 : \rho(\ell) < 0.5 \right\}
```

## PSD Slope

The implementation now follows the cited NeuroKit2 path more closely:

- linearly detrend the series
- standardize it with population SD
- compute a one-sided FFT power spectrum
- keep the lower-frequency quartile of the spectrum
- fit the slope in log-log space

Formula sketch:

```latex
\text{PSD slope} = \operatorname{slope}\left(
\log_{10}(f),
\log_{10}(PSD(f))
\right)
```

## Lempel-Ziv Complexity

The series is binarized around its mean:

```latex
b_i =
\begin{cases}
1 & s_i \ge \mu \\
0 & s_i < \mu
\end{cases}
```

The Lempel-Ziv counter then scans the binary string left-to-right and adds a new
phrase whenever it encounters a substring not yet present in the discovered
pattern set. The app publishes a normalized form:

```latex
LZC_{norm} = \frac{c(n)}{n / \log_2(n)}
```

## Sample Entropy

Default settings:

- `m = 2`
- `delay = 1`
- `r = 0.2 * SD`

Formula sketch:

```latex
SampEn(m, r, N) = -\ln\left(\frac{A}{B}\right)
```

Where:

- `B` counts matching embedded vectors of length `m`
- `A` counts matching embedded vectors of length `m + 1`
- matching uses the Chebyshev-distance tolerance `r`

If the conditional probability is undefined, the app now suppresses the
headline instead of inventing a finite fallback value.

## Multiscale Entropy

Default settings:

- `m = 3`
- `delay = 1`
- `r = 0.2 * SD` of the original series
- scales `1..5`
- published summary: area under the entropy-by-scale curve divided by the count
  of usable scales

For each scale `tau`, the series is coarse-grained by averaging
non-overlapping blocks:

```latex
y_j^{(\tau)} = \frac{1}{\tau}\sum_{i=(j-1)\tau + 1}^{j\tau} s_i
```

Sample entropy is then computed on each coarse-grained series with the same
base tolerance. The reported MSE summary is:

```latex
MSE_{AUC} = \frac{\operatorname{trapz}(SampEn_{\tau})}{K}
```

Where `K` is the number of usable finite scales kept for the summary.

## Alignment Status

This implementation is aligned to the cited method family for:

- interval and amplitude series extraction from extrema
- mean, `SD`, `CV`, `ACW50`, `PSD slope`, `LZC`, `SampEn`, and `MSE`
- NeuroKit2-style `SampEn` and `MSE` defaults
- NeuroKit2-style handling of undefined entropy values
- NeuroKit2-style `PSD slope` preprocessing and low-frequency fit region

The remaining explicit adaptation is the waveform source:

- the paper uses respiration-belt input
- this repo uses the calibrated ACC breathing waveform from `PolarBreathingTracker`

## References

- [Breathing Dynamics Workflow](breathing-dynamics-workflow.md)
- [Breathing Workflow](breathing-workflow.md)
- [References](references.md)
- Goheen et al., "It's About Time: Breathing Dynamics Modulate Emotion and Cognition" (2025)
