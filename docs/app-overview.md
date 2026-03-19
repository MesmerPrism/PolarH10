# App Overview

`PolarH10.App` is the Windows operator surface in this repo. It exists to make
the Polar H10 visible as a live telemetry source, not just as a protocol or BLE
exercise. The current app shell is built around a real research workflow:
control the selected strap on the left, then compare one or several tracked
straps in parallel in the live telemetry surface.

![PolarH10 WPF monitor preview](assets/brutal-tdr-preview.png)

## What The App Is For

- Scan nearby Polar H10 straps and pick the one you actually want to work with.
- Open the BLE/GATT link and confirm the runtime path is healthy before capture.
- Inspect live HR, RR, ECG, and ACC data in one operator-facing monitor.
- Track multiple Polar H10 units in parallel on the live charts when you need
  direct strap-to-strap comparison.
- Run diagnostics and inspect logs without dropping straight into debugger-only workflows.
- Record sessions to disk so you can replay or analyze them later.

## Main Surfaces

### Device rail

The left rail is the entry point for discovery and device selection. Use it to:

- scan for advertisements
- identify the correct strap by alias or address
- switch the selected device without losing the rest of the app context
- choose which device the detail panel, connect/disconnect actions, recording controls,
  and overlay tab currently refer to

### Live tab

The Live tab is the core runtime surface. It is meant to answer a simple
question fast: *is the link alive and is the telemetry useful right now?*

Expect to find:

- a dominant heart-rate readout
- supporting metrics such as RR timing
- waveform panels for live ECG and ACC
- a `Tracked devices` picker that follows the selected device by default
- optional parallel tracking for multiple straps at the same time on the live charts
- a chart treatment optimized for monitoring, not dashboard decoration

### Multi-device tracking

Researchers often need to compare two or more active straps without juggling
separate windows or mental context. The tracked-device control on the Live tab
exists for that case.

- leave `Follow selected device` on when you want the charts to follow the left rail
- turn it off when you want to pin multiple devices into the live charts
- keep using the left rail for alias editing, connect/disconnect, recording, and overlay inspection
- use the overlay tab for the currently selected device when you need a denser
  single-device view of HR, RR, ECG, and ACC together

### Diagnostics

Diagnostics exist so you can verify the session path rather than guessing.
Depending on what you are doing, that means:

- checking that GATT services and characteristics are present
- validating PMD settings and stream startup
- inspecting runtime logs when a session stalls or times out

### Recording flow

Once the stream is stable, the app and CLI can write:

- heart-rate output
- ECG output
- accelerometer output
- protocol/runtime logs
- run metadata and capture manifests

## Typical Operator Flow

1. Scan for nearby straps.
2. Select the intended device and establish the link.
3. Confirm the live telemetry surface is populated and believable.
4. If you are comparing subjects or strap placement, open `Tracked devices` and enable multiple straps.
5. Run diagnostics if the session looks wrong.
6. Start capture when the stream is stable.
7. Stop cleanly and inspect the exported files.

## When To Use The CLI Instead

Use the CLI when you need:

- a quick doctor-style validation path
- scripted capture or replay
- direct command output instead of the WPF operator surface
- a smaller surface area while debugging BLE/session issues

## Related Pages

- [Getting Started](getting-started.md)
- [WPF UI Preview](ui-preview.md)
- [CLI Reference](cli.md)
- [Protocol Overview](protocol/overview.md)
- [Diagram Viewer](../diagrams/viewer.html)
