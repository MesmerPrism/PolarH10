# Polar Stream

Polar Stream is the condensed successor UI for this fork. It has exactly three
working areas:

1. **Input** — scan, connect, connection state, and battery.
2. **Output** — raw ECG/ACC readings, stream base name, LSL/OSC switches, and an
   extensible output list split into red ECG and blue ACC families. The ACC
   family intentionally stays small and labels its breathing classifier as
   experimental.
3. **Visualization** — a resizable tile workspace whose views independently
   select from the active outputs. Raw acceleration uses one selection with X,
   Y, and Z shown as three vertically stacked traces. The experimental breathing
   output adds a derived curve and a phase-driven circle as separate choices.

The vertical dividers between Input, Output, and Visualization can be dragged
or adjusted with the arrow keys, and the proportions are remembered locally.
Visualization tiles can be added, resized from their lower-right corner,
reordered by their dotted handle, or detached into live system windows. Drag a
tile into another Polar Stream window to merge it there, or use its dock button
to return it to the main workspace. Pane-local container rules compact and
rearrange controls as a pane narrows instead of relying only on outer-window
breakpoints.

The frontend is plain HTML, CSS, and JavaScript. It has no framework runtime.
The native code is a Rust workspace split into protocol, input, output, and app
crates; see [ARCHITECTURE.md](ARCHITECTURE.md).

## Validate the reusable crates

```bash
cargo test -p polar-h10-core -p polar-h10-input -p polar-h10-output
```

## Preview the interface

The frontend includes a browser-only synthetic signal so its complete workflow
can be reviewed without BLE hardware. Synthetic mode is never entered by the
native application.

```bash
npx browser-sync start --server apps/polar-stream/ui --no-open --no-ui
```

Open `http://127.0.0.1:3000`, scan, and select the preview H10.

## Run the native desktop app

Install the current [Tauri system prerequisites](https://v2.tauri.app/start/prerequisites/).
On Debian/Ubuntu this includes WebKitGTK 4.1 and libsoup 3 development packages.
Then run:

```bash
cargo run -p polar-stream
```

Linux Bluetooth access uses BlueZ/D-Bus. macOS packages must include a Bluetooth
usage description. Windows uses the system WinRT BLE implementation through
`btleplug`.

## Outputs

Every selected output has one discoverable name shared across protocols. For a
base name of `participant_07`, raw ECG is `participant_07_rawECG` and raw
accelerometer is `participant_07_rawACC`. Additional metrics follow the same
rule, for example `participant_07_heartRate`. Spaces and protocol-unsafe
characters in the user-entered base are collapsed to underscores.

ACC breathing is limited to two explicitly experimental, independently
selectable streams. `participant_07_accBreathingMagnitude` retains the
continuous configured ACC projection so downstream tools can inspect the curve
or estimate breathing rate. `participant_07_accBreathingPhase` contains only
the three-state classifier (`1` inhale, `-1` exhale, `0` pause/not ready). They
share the same included axes (X + Z by default), smoothing window, phase
sensitivity, 0–1 normalization, and direction-inversion settings. Neither is
validated as a respiratory measurement; check both against a reference sensor
before interpreting them.

- **LSL:** `liblsl` is loaded dynamically. The app still starts if it is absent;
  enabling LSL reports the missing library beside the switch. ECG, ACC, and each
  asynchronous metric get separate outlets so differing sample rates are not
  mixed into an invalid fixed-rate stream. The outlet name is the discoverable
  name above.
- **OSC:** UDP packets go to `127.0.0.1:9000`. The address is the same
  discoverable name with a leading slash, such as
  `/participant_07_rawECG`. Every message starts with the sensor timestamp as an
  OSC `int64`, followed by float samples.

The fixed OSC destination is deliberate: changing it is an integration concern,
not a lever needed in the primary UI. It can later be injected through app
configuration without touching acquisition or visualization code.

## Remembered preferences

The WebView's app-local storage retains the last accepted stream base name and
the last successfully connected sensor ID and name. On the next launch, Polar
Stream applies the name immediately and scans automatically for that sensor. It
prefers an exact device-ID match, falls back to a single unambiguous name match,
and otherwise leaves all scan results available for manual selection. A failed
connection never replaces the remembered sensor.

## Android

Tauri 2 provides the Android shell and the Rust crates are Android-compatible.
`btleplug` 0.12 supports Android GATT operations, but its Java/JNI module and
Android 12+ Bluetooth permission flow must be included when generating the
Android Studio project. Keep that platform glue inside the input crate or a
Tauri input plugin; do not move BLE packets through the HTML layer.

This is an unofficial research tool and is not a medical device.
