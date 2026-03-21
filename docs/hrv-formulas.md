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

```latex
\overline{NN} = \frac{1}{n}\sum_{i=1}^{n} NN_i
```

```latex
\overline{HR} = \frac{60000}{\overline{NN}}
```

```latex
SDNN = \sqrt{\frac{1}{n-1}\sum_{i=1}^{n}(NN_i - \overline{NN})^2}
```

```latex
RMSSD = \sqrt{\frac{1}{n-1}\sum_{i=2}^{n}(NN_i - NN_{i-1})^2}
```

```latex
\ln RMSSD = \ln(RMSSD)
```

```latex
pNN50 = 100 \times
\frac{\#\left\{|NN_i - NN_{i-1}| > 50\,ms\right\}}{n-1}
```

```latex
SD1 = \frac{RMSSD}{\sqrt{2}}
```

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

## Reference Note

Implementation review for this update was checked against the local PDF file:

`C:\Users\tillh\Downloads\fpubh-05-00258.pdf`

## References

- [HRV Workflow](hrv-workflow.md)
- [References](references.md)
- Shaffer and Ginsberg, "An Overview of Heart Rate Variability Metrics and Norms" (2017)
