---
title: Breathing Dynamics Workflow
description: Use the dedicated breathing-dynamics window to inspect interval and amplitude entropy derived from the calibrated ACC breathing waveform.
summary: Open the dynamics window after breathing calibration, wait through the readiness ramp, and use the dedicated feature view when the Live tab summary plots are too small.
nav_label: Breathing Dynamics Workflow
nav_group: Task Guides
nav_order: 25
---

# Breathing Dynamics Workflow

The `Dynamics window` is the breathing-dynamics operator surface. It uses the
calibrated breathing base waveform, detects alternating extrema, derives breath
interval and amplitude series, and computes a feature bundle for each series.

## Method Provenance

This window follows the breathing-dynamics feature family described by Goheen
et al. in *Psychophysiology* (2025): breath interval and breath amplitude
series, then mean, SD, CV, ACW50, PSD slope, LZC, sample entropy, and
multiscale entropy for each derived series.

The implementation here is intentionally explicit about one adaptation: the
paper derives those series from a respiration-belt signal after its own
preprocessing, while this app derives them from the calibrated ACC breathing
base waveform already exposed by the Polar breathing tracker.

The paper also cites NeuroKit2 for the entropy and complexity functions. Where
the paper does not publish exact entropy hyperparameters, this app uses fixed
defaults that stay close to that path:

- SampEn: `m=2`, `delay=1`, `r=0.2 * SD`
- MSE: `m=3`, `delay=1`, `r=0.2 * SD`, scales `1..5`, summary=`AUC`

Use the window's `Method references` panel or the shared [References](references.md)
page if you need the paper DOI, the linked paper-code repo, or the NeuroKit2
source pages.

## What The Window Exposes

- headline `Interval entropy` and `Amplitude entropy` tiles
- tracker readiness, recording maturity, and entropy confidence state
- counts for accepted extrema plus retained interval/amplitude breaths
- feature bundles for mean, SD, CV, ACW50, PSD slope, LZC, sample entropy, and multiscale entropy
- reset, apply-tuning, and restore-defaults controls aligned with the coherence window
- a dedicated chart and log stream for the currently selected device

## Parameter Meanings

- `Extremum delta`: minimum waveform change required before a trend reversal is treated as a candidate peak or trough.
- `Peak/trough gap (s)`: minimum allowed time between alternating accepted extrema.
- `Peak-trough excursion`: minimum waveform excursion required for a completed breath to count.
- `Retained series size`: rolling history length kept for the derived interval and amplitude series.
- `Basic-stats warmup`: minimum derived-breath count before mean, SD, CV, ACW50, and PSD slope are shown.
- `Entropy warmup`: minimum derived-breath count before LZC, sample entropy, and multiscale entropy are shown.
- `Confidence target`: longer-recording target used for `Recording maturity` and `Entropy confidence`.
- `Recording maturity`: warmup score based on the smaller of the interval and amplitude series sizes, scaled from `Basic-stats warmup` to `Confidence target`.
- `Entropy confidence`: confidence score that stays at zero until both interval and amplitude entropy are available, then ramps toward `Confidence target`.
- `SampEn m / delay / r·SD`: sample-entropy embedding dimension, delay, and tolerance as a multiple of signal SD.
- `MSE m / delay / r·SD`: multiscale-entropy embedding dimension, delay, and tolerance as a multiple of signal SD.
- `MSE max scale`: largest coarse-graining scale used before the entropy-by-scale curve is reduced to AUC.

## Manual App Test

1. Wear the strap and wet the electrodes.
2. Start the app and connect to the intended strap.
3. Confirm HR and ACC counters are moving on the `Live` tab.
4. Open `Breathing` and complete calibration until the tracker is live.
5. Return to `Live` and open `Dynamics window`.
6. Check the top status line:
   - before enough breaths accumulate, entropy should stay unavailable
   - readiness should move from collecting breaths to basic stats, then to entropy-ready
7. Keep breathing normally while the warmup runs.
8. Confirm the dynamics window starts updating:
   - interval entropy and amplitude entropy leave `--`
   - interval and amplitude breath counts increase
   - recording maturity and entropy confidence move upward over longer recordings
9. Use the shared telemetry selector if you want the Live tab to plot:
   - `Breath interval entropy`
   - `Breath amplitude entropy`
10. Adjust tuning only if needed:
    - `Apply tuning` resets the tracker with the new thresholds
    - `Restore defaults` returns to the app defaults
    - `Reset tracker` clears the current derived-history window

## Fast Failure Checks

- If the status line says calibration is required, the breathing tracker is not ready yet.
- If the window stays in `Waiting for breathing tracking`, ACC is flowing but the calibrated waveform is not stable enough for cycle extraction.
- If interval or amplitude counts stay near zero, the extrema thresholds are too strict for the current signal or the breathing waveform is too flat.
- If shared telemetry plots stay flat at zero while the window is still warming up, that is expected until entropy becomes available.

## Related Pages

- [Breathing Workflow](breathing-workflow.md)
- [Coherence Workflow](coherence-workflow.md)
- [App Overview](app-overview.md)
- [Getting Started on Windows](getting-started.md)
- [References](references.md)
