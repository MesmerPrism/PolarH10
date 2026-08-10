# PolarH10 for macOS

This directory contains the native macOS research-preview app. It uses SwiftUI
for the operator surface and CoreBluetooth for direct Polar H10 access; it does
not require the Polar SDK.

The initial macOS surface includes:

- Polar sensor scan, connect, disconnect, and battery status
- live HR, RR, ECG, and accelerometer telemetry
- rolling RMSSD, SDNN, pNN50, and RR-derived coherence
- session capture to `session.json`, `hr_rr.csv`, `ecg.csv`, `acc.csv`, and
  `protocol.jsonl`
- a Universal release artifact for Apple silicon and Intel Macs

## Requirements

- macOS 13 Ventura or later
- Xcode 15 or later for source builds
- Bluetooth permission for PolarH10

## Develop

```bash
swift test --package-path macos
bash tools/macos/build-app.sh 0.1.0
open artifacts/macos/PolarH10.app
```

The app bundle is the supported live-development launch path because it carries
the Bluetooth privacy strings from `Info.plist`.

The packaging script compiles both `arm64` and `x86_64`, combines them with
`lipo`, creates `PolarH10.app`, applies an ad-hoc signature, and creates
`PolarH10-macOS-universal.zip` for release upload.
