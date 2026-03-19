# App Overview

`PolarH10.App` is the Windows operator surface in this repo. It exists to make
the Polar H10 visible as a live telemetry source, not just as a protocol or BLE
exercise.

![PolarH10 WPF monitor preview](assets/brutal-tdr-preview.png)

## What The App Is For

- Scan nearby Polar H10 straps and pick the one you actually want to work with.
- Open the BLE/GATT link and confirm the runtime path is healthy before capture.
- Inspect live HR, RR, ECG, and ACC data in one operator-facing monitor.
- Run diagnostics and inspect logs without dropping straight into debugger-only workflows.
- Record sessions to disk so you can replay or analyze them later.

## Main Surfaces

### Device rail

The left rail is the entry point for discovery and device selection. Use it to:

- scan for advertisements
- identify the correct strap by alias or address
- switch the active device without losing the rest of the app context

### Live tab

The Live tab is the core runtime surface. It is meant to answer a simple
question fast: *is the link alive and is the telemetry useful right now?*

Expect to find:

- a dominant heart-rate readout
- supporting metrics such as RR timing
- waveform panels for live ECG and ACC
- a chart treatment optimized for monitoring, not dashboard decoration

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
4. Run diagnostics if the session looks wrong.
5. Start capture when the stream is stable.
6. Stop cleanly and inspect the exported files.

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
