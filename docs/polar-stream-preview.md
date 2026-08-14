---
title: Polar Stream Recorded Preview
description: Preview Polar Stream without BLE hardware by looping an anonymized 60-second real Polar H10 ECG and accelerometer recording.
summary: Use the Mock Data button to drive Polar Stream's browser preview and waveform assets from one real recorded fixture instead of generated sensor data.
nav_label: Polar Stream Preview
nav_group: Task Guides
nav_order: 15
---

# Polar Stream Recorded Preview

Polar Stream's hardware-free preview loops one anonymized 60-second recording
captured from a real Polar H10. It contains 7,800 ECG samples at 130 Hz, 12,000
three-axis accelerometer samples at 200 Hz, and the HR/RR events received during
the same interval.

![Real Polar H10 ECG and accelerometer preview loop](assets/polar-stream-recorded-preview.svg)

## Try the interface without a strap

Serve the frontend from the repository root:

```bash
npx browser-sync start --server apps/polar-stream/ui --no-open --no-ui
```

Open `http://127.0.0.1:3000` and choose **Mock Data**. The recording then loops
through the normal browser-preview ingestion path, feeding the raw readings,
charts, derived ACC views, and inline ECG sparkline. The native Tauri app keeps
its normal live Polar H10 scan and never enters recorded-preview mode.

## Replace the canonical recording

Wear a moistened, awake strap and run:

```bash
cargo run -p capture-preview-fixture
```

The capture tool writes both shared preview sources:

- `apps/polar-stream/ui/data/preview-recording.json` for looped playback;
- `docs/assets/polar-stream-recorded-preview.svg` for static documentation.

The fixture schema rejects generated, truncated, malformed, or incorrectly
sampled data instead of filling gaps with a simulation. Capture output excludes
the sensor's BLE address and serial number.

## Verify a change

```bash
npm run preview:fixture:test
npm run pages:build
```

See the [Polar Stream README](https://github.com/MesmerPrism/PolarH10/tree/main/apps/polar-stream)
for the native build and run commands.
