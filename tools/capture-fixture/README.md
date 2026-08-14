# Capture Preview Fixture Tool

Connects directly to an awake Polar H10 and records one anonymized real-data
loop for every hardware-free Polar Stream preview. The default capture is
exactly 60 seconds: 7,800 ECG samples at 130 Hz and 12,000 three-axis
accelerometer samples at 200 Hz. Heart-rate and RR events received during the
same interval are retained for the metric previews.

The command writes both shared sources of truth:

- `apps/polar-stream/ui/data/preview-recording.json` for browser playback,
- `docs/assets/polar-stream-recorded-preview.svg` for static waveform previews.

The fixture does not store the BLE address or strap serial number.

## Usage

```bash
cargo run -p capture-preview-fixture
```

Wear the moistened strap and keep it close to the computer before running the
command. If more than one Polar sensor is visible, select one from the printed
scan results:

```bash
cargo run -p capture-preview-fixture -- --device <ID>
```

To rebuild the SVG after editing or replacing the JSON fixture:

```bash
cargo run -p capture-preview-fixture -- --render-only
```

Use `--out` or `--svg-out` only when intentionally generating non-canonical
assets. Run `cargo run -p capture-preview-fixture -- --help` for all options.

## Preview contract

The browser validates the schema, real-recording source marker, rates, duration,
units, and exact sample counts before offering the preview sensor. Missing,
short, malformed, or generated fixtures are rejected rather than padded or
silently replaced with simulated data.

The app loops the recording in memory. All charts, derived ACC previews, and the
inline ECG SVG sparkline receive their input through the normal ingestion path.
