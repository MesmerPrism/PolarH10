# WPF UI Preview

`PolarH10.App` is the operator-facing desktop monitor in this repo. This page shows the current shell, telemetry surfaces, and diagnostics layout used in the reference app preview.

![PolarH10 WPF preview](assets/brutal-tdr-preview.png)

## Applied Direction

- warm paper surfaces with darker control text and restrained accent color
- a flatter shell that matches the Pages site instead of a separate app theme
- simplified tab, button, and panel treatment with stronger text contrast
- light chart canvases with clearer traces for live ECG and ACC reading
- an operator-first layout that stays direct without decorative dashboard chrome

## Notes

- The screenshot is generated from the current local build of `PolarH10.App` using the scripted preview mode in `tools/site/capture-wpf-preview.ps1`.
- This preview focuses on the main shell, tab system, telemetry panels, and chart language.
- Runtime data states will populate once a device is connected.
