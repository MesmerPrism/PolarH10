---
title: HRV Formula Sheet
description: Exact short-term HRV formulas, data path notes, and reference alignment for the app's HRV tab.
summary: Use this sheet when you want RMSSD, SDNN, pNN50, SD1, mean NN, mean HR, and lnRMSSD written out directly with the repo's short-term window assumptions.
nav_label: HRV Formulas
nav_group: Internals
nav_order: 72
---

# HRV Formula Sheet

<p>
  <a class="button primary" href="assets/reference-markdown/hrv-formulas.md">Download Markdown</a>
  <a class="button" href="assets/formula-sheets/hrv-formulas.pdf">Download PDF</a>
</p>

## Data Path

- input stream: accepted RR intervals from live BLE heart-rate measurement packets
- physical H10: RR intervals are derived inside the strap from ECG beat timing
- synthetic transport: RR remains the source for the HRV solve, while synthetic
  PMD ECG is emitted on the same synthetic beat schedule for operator review

## Rolling Short-Term Window

The app uses a rolling RR window with a default length of `300 s`. That default
tracks the conventional short-term setting discussed by Shaffer and Ginsberg.

The first solve is intentionally delayed until the rolling window is nearly full
enough to avoid publishing an early headline value from a partial capture.

## Time-Domain Formulas

For accepted RR intervals `NN_1 ... NN_n`:

Here `NN_i` means the `i`-th accepted beat interval in milliseconds, `n` is the
number of accepted intervals in the current rolling window, and an overline
means a window average. These formulas are applied to the accepted short-term
series the tracker keeps for the HRV solve, not to every raw packet value.

```latex
\overline{NN} = \frac{1}{n}\sum_{i=1}^{n} NN_i
```

This is the average accepted beat interval over the current short-term window.

```latex
\overline{HR} = \frac{60000}{\overline{NN}}
```

This converts the mean interval into beats per minute using `60000 ms = 1 min`.

```latex
SDNN = \sqrt{\frac{1}{n-1}\sum_{i=1}^{n}(NN_i - \overline{NN})^2}
```

`SDNN` measures how widely the accepted intervals are spread across the whole
window.

```latex
RMSSD = \sqrt{\frac{1}{n-1}\sum_{i=2}^{n}(NN_i - NN_{i-1})^2}
```

`RMSSD` focuses on beat-to-beat change by differencing neighboring intervals
before averaging.

```latex
\ln RMSSD = \ln(RMSSD)
```

`\ln RMSSD` is the natural-log form of `RMSSD`; it compresses the scale and is
often easier to compare across sessions.

```latex
pNN50 = 100 \times
\frac{\#\left\{|NN_i - NN_{i-1}| > 50\,ms\right\}}{n-1}
```

`pNN50` is the percentage of adjacent interval pairs whose absolute difference
exceeds `50 ms`.

```latex
SD1 = \frac{RMSSD}{\sqrt{2}}
```

`SD1` is the short-axis Poincare equivalent of short-term variability and is
shown because many readers know HRV through that geometric view.

## Alignment Status

The implementation is aligned to the cited short-term HRV guidance for:

- `RMSSD` as the headline short-term vagally weighted metric
- `SDNN` as sample standard deviation over the accepted short-term window
- `pNN50` with a fixed `50 ms` threshold
- `SD1 = RMSSD / sqrt(2)`
- `mean NN`, `mean HR`, and `lnRMSSD`

The app does not claim to reproduce:

- `24 h` norms
- clinical artifact adjudication
- full Holter-style NN editing

It is therefore documented as short-term RR-derived telemetry rather than a
clinical HRV workstation.

## References

- [HRV Workflow](hrv-workflow.md)
- [References](references.md)
- Shaffer and Ginsberg, "An Overview of Heart Rate Variability Metrics and Norms" (2017).
  [DOI: 10.3389/fpubh.2017.00258](https://doi.org/10.3389/fpubh.2017.00258)
