---
title: Breathing From ACC Formula Sheet
description: Repo-specific formulas and algorithm notes for the app's ACC-only breathing-volume approximation.
summary: Use this sheet when you want the calibration, projection, bound selection, and volume mapping behind the Breathing tab written out clearly, with an explicit validation disclaimer.
nav_label: Breathing Formulas
nav_group: Internals
nav_order: 73
---

# Breathing From ACC Formula Sheet

<p>
  <a class="button primary" href="assets/reference-markdown/breathing-formulas.md">Download Markdown</a>
  <a class="button" href="assets/formula-sheets/breathing-formulas.pdf">Download PDF</a>
</p>

## Provenance

This breathing-volume model is repository-specific code ported from the existing
Unity runtime. It is not presented as a published external method reference.

Important status note:

- this ACC breathing-volume model has not yet been externally validated
- a validation study is actively being worked on

## Data Path

- input stream: PMD accelerometer samples from the Polar H10
- preprocessing: exponential moving average on the raw ACC signal
- calibration: estimate a center point and a principal breathing axis from a
  calibration window
- projection: map filtered ACC onto the chosen breathing axis
- bounds: derive lower and upper projection bounds from calibration quantiles
- output: convert the projected signal into a `0..1` breathing-volume estimate

## Core Approximation

Filtered ACC sample:

```latex
a_f(t) = (1 - \alpha)a_f(t-1) + \alpha a_{raw}(t)
```

Calibration center:

```latex
c = \frac{1}{N}\sum_{i=1}^{N} a_f(i)
```

Principal-axis projection:

```latex
p(t) = \langle a_f(t) - c, u \rangle
```

Where `u` is the dominant calibration axis estimated from the calibration-window
covariance.

If the optional `XZ` model is available, the tracker also keeps a 2-D projection
in the chest plane:

```latex
p_{xz}(t) = \langle (x(t), z(t)) - c_{xz}, u_{xz} \rangle
```

Lower and upper projection bounds come from calibration quantiles:

```latex
b_{min} = Q_{lower}(p), \qquad b_{max} = Q_{upper}(p)
```

The runtime then applies the configured edge-ease and optional adaptive-bounds
updates before mapping the projection to volume:

```latex
v(t) = \operatorname{clamp}\left(\frac{p(t) - b_{min}}{b_{max} - b_{min}}, 0, 1\right)
```

The final base volume uses either the 3-D axis model or the `XZ` model,
depending on the tracker settings. The displayed output may then be inverted if
the configured inhale/exhale direction is flipped.

## State Classification

The tracker derives `Inhaling`, `Exhaling`, and `Pausing` from the first
difference of the output volume:

```latex
\Delta v(t) = v(t) - v(t-1)
```

- `Inhaling` when `Delta v` exceeds the configured positive threshold
- `Exhaling` when `Delta v` is below the negative threshold
- `Pausing` otherwise

## Alignment Status

There is no external paper claim here. The correct interpretation is:

- this page documents what the app computes
- the model is intended as a practical ACC-only breathing approximation
- external validation is still pending

## References

- [Breathing Workflow](breathing-workflow.md)
- [App Overview](app-overview.md)
- [References](references.md)
