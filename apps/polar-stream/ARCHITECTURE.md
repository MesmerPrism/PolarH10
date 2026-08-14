# Architecture

## Decision

Use a **Rust core with a Tauri 2 system-WebView shell**.

Rust and C++ can both meet the sensor rates involved here. Rust is the better
repository choice because it combines native performance with memory safety,
has a current cross-platform BLE library, and lets the same domain crates build
for desktop and Android. Tauri supplies the requested HTML interface without
shipping a second browser engine.

Primary evidence:

- [Tauri supports HTML/CSS/JS frontends and Rust/Swift/Kotlin backend logic](https://v2.tauri.app/start/).
- [Tauri uses the platform WebView rather than bundling a browser](https://v2.tauri.app/reference/webview-versions/).
- [`btleplug` 0.12 implements scan, GATT connect, write, subscribe, and notifications on Windows, macOS/iOS, Linux, and Android](https://docs.rs/crate/btleplug/latest).
- [liblsl supports Windows, Linux, macOS, Android, and iOS](https://labstreaminglayer.readthedocs.io/info/intro.html).
- [Tauri channels are intended for ordered, high-throughput native-to-frontend data](https://v2.tauri.app/develop/calling-rust/).

Qt/C++ remains a credible native-UI alternative, especially for an application
that must avoid a WebView entirely. It is not the preferred fit here because the
requested frontend is explicitly HTML-driven and C++ would add manual lifetime
and FFI complexity without reducing the dominant BLE/radio latency.

## Module boundaries

```text
Polar H10
   │ BLE notifications
   ▼
polar-h10-input
   │ typed InputEvent values
   ▼
apps/polar-stream (thin coordinator)
   ├────────► polar-h10-output ─────► built-in LSL/OSC streams
   │                    │
   │                    └──► polar-h10-math ──► custom scalar LSL/OSC streams
   │
   ├────────► Tauri channel ────────► bounded JS ring buffers
                                      │ requestAnimationFrame
                                      ├────► Canvas 2D tiles
                                      └────► BroadcastChannel ──► detached views
   │
   └────────► fixed app-config JSON ─► Last session + named workspace profiles
```

Rules enforced by the crate graph:

- Input does not know whether the consumer is a UI, recorder, LSL, or OSC.
- Output does not know where samples came from.
- Protocol decoding can be unit-tested without an adapter or runtime.
- The HTML layer never republishes scientific data.
- Custom expressions are parsed and evaluated directly in Rust. They never enter
  a general-purpose interpreter or the WebView's JavaScript evaluator.
- Every custom formula belongs to exactly one source clock. No implicit
  resampling or cross-clock variable access occurs.
- The opt-in ACC breathing classifier runs in `polar-h10-core`; the HTML layer
  receives the same derived waveform and phase values that native LSL/OSC
  publishers receive. Browser preview uses a synthetic-only mirror.
- Adding a built-in metric means registering its descriptor and feeding a
  `MetricValue`; user-defined scalar metrics use `polar-h10-math` and do not
  change BLE acquisition.
- UI preferences are isolated in `ui/preferences.js`; Bluetooth and output
  crates remain free of WebView storage concerns. Versioned full profiles are
  validated and persisted by the native coordinator to a fixed app-config file.

## Stable discovery names

`polar-h10-output` owns the metric suffix catalog and the single canonical name
function used by both publishers. A normalized base such as `participant_07`
produces `participant_07_rawECG`, `participant_07_rawACC`, and one equivalent
name per optional metric. LSL uses that full string as its outlet name; OSC uses
the same string as its address with a leading slash.

The UI receives each suffix through the bootstrap catalog so its previews are
descriptions of the native contract, not an independent naming scheme.

Custom formulas use the same canonical base and a normalized user suffix. A
stable formula UUID becomes the LSL source ID; the expression, formula UUID,
source, label, unit, and stream type are recorded in LSL metadata. Each custom
output is scalar and inherits the nominal rate of ECG or ACC, or uses an
irregular rate for HR/RR events.

## Formula execution and isolation

`polar-h10-math` implements a small expression grammar with numeric/Boolean
operators, pure math functions, and explicitly registered stateful DSP
functions. Parsing, source-variable checks, constant filter/window validation,
AST/state allocation, and operation limits happen before a configuration is
accepted. The router compiles enabled formulas when the output configuration is
applied, preserving state only when the formula UUID, source, and expression
are unchanged.

At acquisition time the output router evaluates formula-major batches, appends
successful scalar values to that formula's LSL outlet and OSC path, and returns
the same values to the UI for visualization. Histories belong to individual
call sites inside individual compiled formulas. A transient non-finite result is
dropped; ten consecutive non-finite results fault only that formula. Editing its
source/expression or reconnecting resets fault/runtime state. Aggregate retained
DSP state is bounded across all formulas.

## Latency policy

1. Decode each BLE notification once in Rust.
2. Publish LSL/OSC directly from the native coordinator.
3. Cross the WebView boundary in notification-sized batches, not per sample.
4. Store chart values in fixed-size typed ring buffers.
5. Repaint all open tiles from the same buffers on `requestAnimationFrame`;
   acquisition continues when rendering is paused or throttled. Detached views
   receive incremental display data and an initial bounded snapshot from the
   main WebView rather than opening another BLE connection.
6. Never block input on an animation frame.

After ECG starts, the Input panel reports an explicitly approximate latency.
The input crate combines the ECG batch fill time (`samples / 130 Hz`) with a
rolling median of host notification intervals and uses the larger value. This
captures MTU-dependent batching and persistent platform/adapter buffering while
filtering isolated scheduler stalls. The H10 and host clocks are not synchronized,
so the estimate is a lower-bound-style operational indicator rather than an exact
end-to-end measurement; fixed radio or OS delay may remain.

This keeps visualization work proportional to display pixels and refresh rate,
not to the lifetime of the recording.

## Adding outputs

1. Add the output ID and sample metadata in
   `crates/polar-h10-output/src/config.rs`.
2. Produce the value in the coordinator or a future independent metrics crate.
3. Add its label to the bootstrap catalog in `apps/polar-stream/src/lib.rs`.
4. Add a visualization definition only if the value should be chartable.

The two ACC-breathing outputs are derived once per ACC batch after applying the
selected axes, rolling dominant-axis projection, smoothing, adaptive
normalization, and phase threshold. `acc_breathing_magnitude` publishes the
continuous projection, while `acc_breathing_phase` publishes only the
three-state result. Both remain explicitly experimental in the bootstrap
descriptors and the UI.

Raw input and output destinations remain unchanged.

## Profile persistence

`apps/polar-stream/src/profiles.rs` owns a schema-versioned JSON document in the
Tauri app-config directory. It contains one automatic Last session and named
profiles. The Rust boundary validates the output configuration, profile name,
sensor strings, pane fractions, tile count/dimensions, formula limits, and file
size. The WebView has narrow typed commands to validate formulas and
save/list/load/delete profiles; it has no general filesystem capability.

Writes create and sync a temporary file, move the previous main file to a
fallback, and then replace the main file. A failed replacement attempts a
rollback. Corrupt, oversized, or unsupported documents are renamed to a
timestamped recovery file and startup continues with defaults.
