# Polar Stream preview data

`preview-recording.json` is intentionally created only from a live Polar H10.
Generate or replace it from the repository root with:

```bash
cargo run -p capture-preview-fixture
```

The browser preview does not fall back to a generated waveform when this file
is absent or invalid. See `tools/capture-fixture/README.md` for the schema,
privacy behavior, device selection, and SVG generation details.
