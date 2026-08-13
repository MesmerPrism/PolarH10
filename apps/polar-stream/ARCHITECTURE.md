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
   ├────────► polar-h10-output ─────► LSL outlets
   │                    └───────────► OSC/UDP
   │
   └────────► Tauri channel ────────► bounded JS ring buffers
                                      │ requestAnimationFrame
                                      ├────► Canvas 2D tiles
                                      └────► BroadcastChannel ──► detached views
```

Rules enforced by the crate graph:

- Input does not know whether the consumer is a UI, recorder, LSL, or OSC.
- Output does not know where samples came from.
- Protocol decoding can be unit-tested without an adapter or runtime.
- The HTML layer never republishes scientific data.
- The opt-in ACC breathing classifier runs in `polar-h10-core`; the HTML layer
  receives the same derived waveform and phase values that native LSL/OSC
  publishers receive. Browser preview uses a synthetic-only mirror.
- Adding a custom metric means registering its descriptor and feeding a
  `MetricValue`; it does not change BLE acquisition.
- UI preferences are isolated in `ui/preferences.js`; Bluetooth and output
  crates remain free of WebView storage concerns.

## Stable discovery names

`polar-h10-output` owns the metric suffix catalog and the single canonical name
function used by both publishers. A normalized base such as `participant_07`
produces `participant_07_rawECG`, `participant_07_rawACC`, and one equivalent
name per optional metric. LSL uses that full string as its outlet name; OSC uses
the same string as its address with a leading slash.

The UI receives each suffix through the bootstrap catalog so its previews are
descriptions of the native contract, not an independent naming scheme.

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

The `acc_breathing` exception produces a two-channel derived stream directly
from each ACC batch after applying its selected axes, rolling dominant-axis
projection, smoothing, adaptive normalization, and phase threshold. It remains
explicitly experimental in both the bootstrap descriptor and the UI.

Raw input and output destinations remain unchanged.
