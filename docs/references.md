---
title: References
description: Primary protocol sources, Bluetooth specification pointers, and notes about how this repo derives its documentation.
summary: These references anchor the protocol docs and derived telemetry pages, including coherence, short-term HRV, and breathing-dynamics documentation.
nav_label: References
nav_group: Internals
nav_order: 90
---

# References

## Formula Sheet Downloads

- [Formula Sheets](formula-sheets.md)
- [Coherence Formula Sheet](coherence-formulas.md)
- [HRV Formula Sheet](hrv-formulas.md)
- [Breathing From ACC Formula Sheet](breathing-formulas.md)
- [Breathing Dynamics Formula Sheet](breathing-dynamics-formulas.md)

## Primary Sources

1. **Polar BLE SDK** (MIT License)
   Repository: <https://github.com/polarofficial/polar-ble-sdk>
   Technical documentation (tag 4.0.0):
   <https://github.com/polarofficial/polar-ble-sdk/tree/4.0.0/technical_documentation/>

   The SDK's open-source code and technical documentation provide the authoritative
   reference for the Polar Measurement Data (PMD) service protocol, including
   measurement type identifiers, control point command structures, and data frame
   encoding formats.

2. **Polar Measurement Data Specification for 3rd Party** (PDF)
   Published by Polar Electro. This document describes the PMD service interface
   for third-party developers. It is referenced here for protocol comprehension;
   content in this repository's documentation is written independently.

3. **Sieciński, S.; Kostka, P.S.; Piaseczna, N.J.; Janik, S.; Delgado-Prieto, M.;
   Boczar, T.** "The Newer, the More Secure? Comparing the Polar Verity Sense and
   H10 Heart Rate Sensors." *Sensors* 2025, 25, 2005.
   <https://doi.org/10.3390/s25072005>

   This paper evaluates the Polar H10 and Verity Sense in terms of data quality and
   measurement reliability, providing independent validation of ECG and accelerometer
   data characteristics.

4. **McCraty, R.; Atkinson, M.; Tomasino, D.; Bradley, R.T.** *The Coherent Heart:
   Heart-Brain Interactions, Psychophysiological Coherence, and the Emergence of
   System-Wide Order.* Institute of HeartMath, Publication No. 06-022, 2006.

   This monograph provides the spectral coherence ratio used by the app's
   RR-derived coherence workflow: search the dominant peak in `0.04-0.26 Hz`,
   integrate a `0.030 Hz` peak window, and compare it against total power in
   `0.0033-0.4 Hz`.

   Real-session note: the app consumes RR intervals from the Heart Rate
   Measurement characteristic, while the physical H10 derives those beat
   intervals from ECG timing inside the strap.

## Short-Term HRV Source

1. **Shaffer, F.; Ginsberg, J. P.** "An Overview of Heart Rate Variability
   Metrics and Norms." *Front. Public Health* 5:258, 2017.
   <https://doi.org/10.3389/fpubh.2017.00258>

   This paper is the short-term HRV reference for the app's HRV tab. The tab
   defaults to a `300 s` RR window, uses `RMSSD` as the headline value, and
   keeps `SDNN`, `pNN50`, `SD1`, `mean NN`, `mean HR`, and `lnRMSSD` visible as
   supporting telemetry.

   Implementation reference used during this update:
   `C:\Users\tillh\Downloads\fpubh-05-00258.pdf`

   The repository intentionally treats these values as short-term RR-derived
   telemetry, not as a substitute for `24 h` norms or full clinical artifact
   correction.

   Synthetic-session note: the sibling `SyntheticBio` harness now emits PMD ECG
   frames that stay synchronized with the same synthetic RR schedule used for
   coherence and HRV warmup, so synthetic operator tests keep the waveform and
   interval views aligned even though breathing still bypasses PMD ACC.

## Breathing Dynamics Sources

1. **Goheen, D. P.; et al.** "It's About Time: Breathing Dynamics Modulate Emotion
   and Cognition." *Psychophysiology* 2025.
   <https://doi.org/10.1111/psyp.70149>

   This paper defines the breathing-dynamics feature family used by the app's
   breathing-dynamics window: breath interval and breath amplitude series plus
   mean, standard deviation, coefficient of variation, autocorrelation window,
   PSD slope, Lempel-Ziv complexity, sample entropy, and multiscale entropy.

2. **CANALLAB / breathing_wm**
   <https://github.com/CANALLAB/breathing_wm>

   The paper links this repository as the companion analysis code for the
   published workflow. It is included here as method provenance only; this repo
   does not embed that code directly.

3. **NeuroKit2 implementation references**
   - sample entropy:
     <https://neuropsychology.github.io/NeuroKit/_modules/neurokit2/complexity/entropy_sample.html>
   - multiscale entropy:
     <https://neuropsychology.github.io/NeuroKit/_modules/neurokit2/complexity/entropy_multiscale.html>
   - PSD slope:
     <https://neuropsychology.github.io/NeuroKit/_modules/neurokit2/complexity/fractal_psdslope.html>
   - autocorrelation:
     <https://neuropsychology.github.io/NeuroKit/_modules/neurokit2/signal/signal_autocor.html>
   - Lempel-Ziv complexity:
     <https://neuropsychology.github.io/NeuroKit/_modules/neurokit2/complexity/complexity_lempelziv.html>

   Goheen et al. explicitly state that these feature families were computed with
   NeuroKit2. Where the paper leaves entropy hyperparameters unspecified, this
   repository documents explicit defaults and keeps them close to that
   implementation path.

## Breathing Dynamics Provenance Note

The breathing-dynamics runtime in this repository is an adaptation, not a claim
of numeric identity with the paper's respiration-belt pipeline. Goheen et al.
derive their breath interval and amplitude series from a preprocessed belt
signal; this app derives the same feature family from the calibrated ACC-based
breathing waveform already available in `PolarBreathingTracker`.

The app's default entropy settings are explicit and operator-visible:

- SampEn: `m=2`, `delay=1`, `r=0.2 * SD`
- MSE: `m=3`, `delay=1`, `r=0.2 * SD`, scales `1..5`, summary=`AUC`

Those defaults are intended to stay aligned with the NeuroKit2 path cited by
the paper while making the runtime behavior reproducible inside this repository.

The current runtime also suppresses undefined entropy solves instead of
inventing a finite fallback value, and its PSD slope path now follows the
NeuroKit2-style detrend plus low-frequency log-log fit more closely.

## Breathing From ACC Provenance Note

The ACC breathing-volume tracker in this repository is repo-specific code. It is
documented as an approximation rather than a validated published method.

Current status:

- it has not yet been externally validated
- a validation study is actively being worked on

## Bluetooth SIG Specifications

- **Heart Rate Service** (Service UUID `0x180D`):
  Bluetooth SIG Assigned Numbers and GATT Specification Supplement.
- **Heart Rate Measurement Characteristic** (`0x2A37`):
  Format defined in the GATT Specification Supplement, Section 3.106.
- **Client Characteristic Configuration Descriptor** (`0x2902`):
  Defined in the Bluetooth Core Specification, Vol 3, Part G, Section 3.3.3.3.

## License Note

This repository is an independent implementation. Protocol knowledge was derived from
the sources above, the Polar BLE SDK's MIT-licensed code, and direct BLE traffic
analysis. No proprietary Polar documentation is reproduced verbatim.
