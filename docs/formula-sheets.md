---
title: Formula Sheets
description: Downloadable method sheets for the app's downstream telemetry families, including coherence, short-term HRV, breathing from ACC, and breathing-dynamics entropy.
summary: Use these pages when you want the exact formulas, implementation notes, and references behind the app's derived telemetry instead of only the operator workflow.
nav_label: Formula Sheets
nav_group: Internals
nav_order: 70
---

# Formula Sheets

These sheets cover the app's downstream telemetry families: RR-derived
coherence, short-term HRV, ACC breathing volume, and breathing-dynamics
entropy.

Each sheet is published in three forms:

- an HTML page for quick reading on the site
- a raw Markdown download copied into the Pages build
- a LaTeX-generated PDF download for a compact, typeset reference

## Downloadable Sheets

### Coherence

RR-derived spectral coherence from the accepted RR interval stream.

<p>
  <a class="button primary" href="coherence-formulas.md">Open HTML sheet</a>
  <a class="button" href="assets/reference-markdown/coherence-formulas.md">Download Markdown</a>
  <a class="button" href="assets/formula-sheets/coherence-formulas.pdf">Download PDF</a>
</p>

### HRV

Short-term RR-derived time-domain HRV with RMSSD as the headline metric.

<p>
  <a class="button primary" href="hrv-formulas.md">Open HTML sheet</a>
  <a class="button" href="assets/reference-markdown/hrv-formulas.md">Download Markdown</a>
  <a class="button" href="assets/formula-sheets/hrv-formulas.pdf">Download PDF</a>
</p>

### Breathing From ACC

The repo-specific ACC breathing-volume approximation used by the Breathing tab.

<p>
  <a class="button primary" href="breathing-formulas.md">Open HTML sheet</a>
  <a class="button" href="assets/reference-markdown/breathing-formulas.md">Download Markdown</a>
  <a class="button" href="assets/formula-sheets/breathing-formulas.pdf">Download PDF</a>
</p>

### Breathing Dynamics And Entropy

Breath interval and amplitude features derived from the calibrated ACC waveform.

<p>
  <a class="button primary" href="breathing-dynamics-formulas.md">Open HTML sheet</a>
  <a class="button" href="assets/reference-markdown/breathing-dynamics-formulas.md">Download Markdown</a>
  <a class="button" href="assets/formula-sheets/breathing-dynamics-formulas.pdf">Download PDF</a>
</p>

## Alignment Summary

- `Coherence`: the spectral peak search, peak-window integration, total-band
  integration, and paper ratio match the McCraty reference; the headline `0..1`
  score, smoothing, and confidence are app-side runtime choices.
- `HRV`: `RMSSD`, `SDNN`, `pNN50`, `SD1`, `mean NN`, `mean HR`, and `lnRMSSD`
  follow the short-term RR-derived definitions summarized by Shaffer and
  Ginsberg.
- `Breathing dynamics`: the feature family is aligned to the Goheen paper and
  the NeuroKit2 implementations it cites, but the source waveform is this
  repo's calibrated ACC breathing signal instead of a respiration belt.
- `Breathing from ACC`: this is repository-specific code, not a cited published
  method. The docs state explicitly that it has not yet been externally
  validated and that a validation study is actively in progress.

## Related Pages

- [Coherence Workflow](coherence-workflow.md)
- [HRV Workflow](hrv-workflow.md)
- [Breathing Workflow](breathing-workflow.md)
- [Breathing Dynamics Workflow](breathing-dynamics-workflow.md)
- [References](references.md)
