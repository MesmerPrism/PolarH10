# Polar Stream

Polar Stream is the condensed successor UI for this fork. It has exactly three
working areas:

1. **Input** — scan, connect, connection state, and battery.
2. **Output** — raw ECG/ACC readings, stream base name, LSL/OSC switches, an
   add-one-at-a-time output library with recorded outcome previews, and a custom
   math module. Each formula is bound to one source clock and emits one
   processed scalar stream. The ACC family intentionally labels its breathing
   classifier as experimental.
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
cargo test -p polar-h10-core -p polar-h10-input -p polar-h10-math -p polar-h10-output
```

## Preview the interface

The browser preview loops one anonymized 60-second recording captured from a
real Polar H10. The same fixture feeds the raw readings, visualization buffers,
derived ACC views, and inline ECG SVG sparkline. There is no generated ECG or
ACC fallback, and recorded-preview mode is never entered by the native app.

Capture or replace the canonical recording while wearing the strap:

```bash
cargo run -p capture-preview-fixture
```

That command also renders the static waveform SVG from the same fixture. The
browser rejects a missing, truncated, malformed, or non-recorded fixture with an
actionable message.

![Real Polar H10 ECG and accelerometer preview loop](../../docs/assets/polar-stream-recorded-preview.svg)

The same fixture drives the output library's per-metric charts. Settings such
as the RR time window, ACC axes, smoothing, sensitivity, normalization, and
direction update the selected outcome preview before the output is added.

![Polar Stream add-one-at-a-time output library with an RMSSD preview](../../docs/assets/polar-stream-output-library.png)

```bash
npx browser-sync start --server apps/polar-stream/ui --no-open --no-ui
```

Open `http://127.0.0.1:3000` and click **Mock Data**.

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

Open **Add output** to focus one metric at a time, inspect its recorded outcome,
read its concise scientific context and primary citations, then explicitly add
or remove it. The responsive dialog expands to 1,280 px on a large display and
collapses to a single-column browser/preview layout on smaller screens.

The built-in ECG family includes raw ECG, heart rate, RR interval, mean NN,
mean heart rate, RMSSD, lnRMSSD, SDNN, pNN50, Poincaré SD1, and the experimental
Excite-O-Meter excitement level. The ACC family includes raw X/Y/Z,
three-dimensional magnitude, experimental breathing magnitude, and experimental
breathing phase. RR-derived windows are independently configurable from 10 to
300 seconds and are stored with profiles.

The excitement output is a causal rolling-baseline adaptation of the
[Excite-O-Meter](https://sites.google.com/view/exciteometer/eom) proposal, not an
exact reproduction of its post-session standardization. The original
four-person feasibility result did not significantly distinguish the tested
high- and low-arousal videos, so the app labels the metric experimental and
links both the paper and project page beside its preview.

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

## Custom math outputs

Open **Add output**, then use **New formula** or **Use as custom formula** in a
metric's preview. A formula has a stable UUID, stream suffix, source, expression,
unit, and enabled flag. Its discoverable name is
`<base>_<formula suffix>` in both LSL and OSC. Enabled formulas are also
available as live visualization sources, including detached visualization
windows.

![Guided custom formula card with insert keyboard and recorded before/after preview](../../docs/assets/polar-stream-formula-lab.png)

The editor includes a variable map, source-aware insert keyboard with
hover/focus explanations, and templates for every scalar built-in metric. Every
valid draft gets a recorded before/after chart that overlays its source signal
and computed result, so changing the expression or source shows the outcome
before the formula is applied. Time is the automatic x-axis and the expression
computes one output y-value per source sample or event; this browser-side
preview uses a restricted expression parser over the same canonical fixture,
while the native bounded Rust engine remains authoritative for validation and
live publishing.

The four source clocks deliberately expose only their own variables:

| Source | Variables | Nominal output rate |
| --- | --- | --- |
| ECG | `ecg` in µV | 130 Hz |
| Accelerometer | `x`, `y`, `z` in mg | 200 Hz |
| Heart rate | `hr` in bpm | device event rate |
| RR interval | `rr` in ms | one value per accepted beat interval |

Expressions support arithmetic, comparisons, Boolean operators, `pi`, `e`, and
bounded functions including `abs`, `sqrt`, trigonometry, `min`, `max`, `clamp`,
and lazy `if`. Stateful DSP includes time- or sample-count moving mean/RMS/
standard-deviation/z-score, delay, EMA, low/high/band-pass filters, derivative,
integral, beat-count RMSSD/pNN50, duration-based RR metrics (`rr_mean`,
`rr_mean_hr`, `rr_rmssd`, `rr_ln_rmssd`, `rr_sdnn`, `rr_pnn50`, `rr_sd1`), the
experimental `excitement` adaptation, and the existing experimental ACC
breathing magnitude/phase classifier. The editor displays the executable
expression used by every scalar built-in metric. Raw ACC is documented as
`channels(x, y, z)` because that built-in is three-channel, while a custom
scalar ACC formula starts from one axis or a combination such as
`sqrt(x*x + y*y + z*z)`.

Formulas are parsed by `polar-h10-math`; they are not JavaScript, Rust, or shell
code. There are no statements, assignments, loops, strings, filesystem calls,
or network calls. Validation caps expression/AST depth, stateful call count,
window sizes, total retained state, and work per sample. A formula is isolated
from every other formula. Repeated non-finite results fault only that formula
after ten consecutive failures, stop its stream values, and surface the fault
in the UI.

Example formulas:

```text
moving_mean(ecg, 0.20)
lowpass(sqrt(x*x + y*y + z*z) / 1000, 4)
zscore_n(rr, 20)
if(abs(ecg) > 500, ecg, 0)
```

## Workspace profiles and Last session

The native app stores a versioned **Last session** plus up to 50 named profiles
in its fixed app-configuration directory. A profile includes the preferred
sensor, complete output configuration, custom formula drafts, destination
switches, pane proportions, and up to 16 visualization tiles. Last session is
updated after accepted output or layout changes and restored on launch. Loading
a named profile applies outputs and layout first, then offers to reconnect its
saved sensor. The scanner prefers an exact device-ID match, falls back to one
unambiguous name match, and otherwise leaves the results for manual selection.

Writes use a temporary file plus a previous-file fallback. Oversized, corrupt,
or unknown-version settings are preserved beside the settings file as a recovery
copy rather than being silently overwritten. The WebView's old local
preferences remain only as migration/fallback data for the stream name and last
sensor.

## Android

Tauri 2 provides the Android shell and the Rust crates are Android-compatible.
`btleplug` 0.12 supports Android GATT operations, but its Java/JNI module and
Android 12+ Bluetooth permission flow must be included when generating the
Android Studio project. Keep that platform glue inside the input crate or a
Tauri input plugin; do not move BLE packets through the HTML layer.

This is an unofficial research tool and is not a medical device.
