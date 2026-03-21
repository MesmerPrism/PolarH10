---
title: Coherence Formula Sheet
description: Exact formulas, data path, and reference notes for the app's RR-derived coherence calculation.
summary: Use this sheet when you want the fixed spectral constants, the paper ratio, the normalized UI score, and the runtime adaptations behind the coherence tab.
nav_label: Coherence Formulas
nav_group: Internals
nav_order: 71
---

# Coherence Formula Sheet

<p>
  <a class="button primary" href="assets/reference-markdown/coherence-formulas.md">Download Markdown</a>
  <a class="button" href="assets/formula-sheets/coherence-formulas.pdf">Download PDF</a>
</p>

## Data Path

- input stream: accepted RR intervals from the Heart Rate Measurement characteristic
- physical H10: those RR intervals are device-derived from ECG beat timing
- synthetic transport: the coherence solve still uses RR, while synthetic PMD
  ECG is emitted separately so the waveform view stays aligned to the same beat
  schedule

## Fixed Spectral Model

The app keeps the paper-defined spectral constants fixed:

- peak search band: `0.04-0.26 Hz`
- peak integration window: `0.030 Hz` centered on the dominant peak
- total-power band: `0.0033-0.4 Hz`

Formula sketch:

```latex
f_{peak} = \arg\max_{f \in [0.04, 0.26]} PSD(f)
```

```latex
P_{peak} = \int_{f_{peak} - 0.015}^{f_{peak} + 0.015} PSD(f)\,df
```

```latex
P_{total} = \int_{0.0033}^{0.4} PSD(f)\,df
```

```latex
\text{paper coherence ratio} =
\left(\frac{P_{peak}}{P_{total} - P_{peak}}\right)^2
```

## UI-Facing Normalized Score

The app also publishes a bounded score for the headline readout and summary
charts:

```latex
\text{normalized coherence}_{0..1} = \operatorname{clamp}\left(\frac{P_{peak}}{P_{total}}, 0, 1\right)
```

This normalized score is an app-side presentation choice. It is not the McCraty
paper ratio itself.

## Runtime Choices Around The Paper Formula

The cited source fixes the spectral constants above, but it does not define the
operator-side runtime details used by this app. The current implementation uses:

- a rolling RR window
- cubic-spline resampling of the RR tachogram
- a `128`-point Hann-windowed spectrum solve
- a smoothed headline `0..1` display value
- a separate confidence score based on RR count and window coverage

Those runtime choices affect how quickly the app stabilizes, but not the core
paper ratio formula shown above.

## Alignment Status

The implementation is aligned to the cited reference at the formula level for:

- peak search band
- peak-window width
- total-band limits
- paper coherence ratio

The display normalization, smoothing, and confidence telemetry are explicit
application-level additions.

## References

- [Coherence Workflow](coherence-workflow.md)
- [References](references.md)
- McCraty, Atkinson, Tomasino, and Bradley, *The Coherent Heart* (2006)
