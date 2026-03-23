---
title: SyntheticBio Companion App
description: SyntheticBio is the companion app for repeatable same-machine PolarH10 demos, fixture export, and synthetic transport testing. Download it and read the full guide on its own GitHub Pages site.
summary: Use SyntheticBio when you want deterministic HR/RR, PMD ECG, and breathing-volume telemetry for PolarH10 without needing a live chest strap for every test or documentation run.
nav_label: SyntheticBio Companion
nav_group: Start Here
nav_order: 25
---

# SyntheticBio Companion App

`SyntheticBio` is the companion Windows app for running deterministic same-machine synthetic sessions against `PolarH10`.

Use it when you want to:

- demo coherence, HRV, or breathing-dynamics behavior with repeatable inputs
- export fixture folders with ground truth and analysis traces
- validate the Windows app and synthetic transport path without needing live hardware for every run

The full SyntheticBio-specific guidance now lives on its own site:

- home: [SyntheticBio Pages](https://mesmerprism.github.io/SyntheticBio/)
- download: [SyntheticBio Download & Install](https://mesmerprism.github.io/SyntheticBio/download.html)
- repo: [MesmerPrism/SyntheticBio](https://github.com/MesmerPrism/SyntheticBio)

The normal workflow is:

1. Start `SyntheticBio`.
2. Start or reuse the synthetic named-pipe session.
3. Launch `PolarH10` with synthetic transport.
4. Scan and connect to one of the virtual demo devices inside the PolarH10 app.

If you only need the short explanation, stay here. If you want the installer, release assets, or the full integration guide, continue on the SyntheticBio Pages site.
