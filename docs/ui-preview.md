# WPF UI Preview

`PolarH10.App` is the operator-facing desktop monitor in this repo. This page shows the current brutal tDR-inspired pass applied to the shell, telemetry surfaces, and diagnostics layout.

![Brutal tDR-inspired WPF preview](assets/brutal-tdr-preview.png)

## Applied Direction

- hard black-and-cream shell with warning-yellow and signal-red accents
- condensed industrial typography in place of default WPF UI chrome
- indexed tab strip and denser labeling for a more authored telemetry feel
- dark instrument-style chart windows with sharper trace treatment
- restrained layout that keeps the app usable as a monitoring tool
- emphasis on live signal reading over generic dashboard cards or decorative chrome

## Notes

- The screenshot is generated from the current local build of `PolarH10.App` using the scripted preview mode in `tools/site/capture-wpf-preview.ps1`.
- This preview focuses on the main shell, tab system, telemetry panels, and chart language.
- Runtime data states will populate once a device is connected.
