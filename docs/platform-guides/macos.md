---
title: Getting Started on macOS
description: Install, build, and use the native PolarH10 Mac research preview with CoreBluetooth.
summary: Download the Universal app or build the Swift package, grant Bluetooth access, connect a strap, inspect live telemetry, and save a session.
nav_label: macOS Guide
nav_group: Platform Guides
nav_order: 10
---

# Getting Started on macOS

`macos/` contains a native SwiftUI application backed by Apple's CoreBluetooth
framework. It talks directly to the Polar H10 Heart Rate and PMD services and
does not require the Polar SDK or the .NET runtime.

## Requirements

- macOS 13 Ventura or later
- an Apple silicon or Intel Mac with Bluetooth Low Energy
- a Polar H10 strap
- Xcode 15 or later only when building from source

## Install the release build

1. Open [Download & Install](../download.md) and download the Universal Mac ZIP.
2. Unzip it and move `PolarH10.app` to Applications.
3. Control-click the app, choose `Open`, and confirm the first launch.
4. If it remains blocked, use `System Settings > Privacy & Security > Open Anyway`.
5. Allow Bluetooth when macOS asks.

The public research preview is ad-hoc signed but not Apple-notarized. That is
why the first release build requires an explicit Gatekeeper override. Do not
disable Gatekeeper system-wide.

## First live session

1. Put on the H10 and wet both electrode areas.
2. Select `Scan` in the app toolbar.
3. Click the intended Polar device in the left rail.
4. Wait for the status strip to show `PMD live`.
5. Check the Heart rate, ECG, and ACC magnitude chart modes.
6. Let RR analysis warm up before interpreting HRV or coherence.
7. Select `Start recording`, then `Stop and save` when finished.
8. Use `Show in Finder` to inspect the saved session.

Recordings default to:

```text
~/Documents/PolarH10/Sessions
```

Each session contains `session.json`, `hr_rr.csv`, `ecg.csv`, `acc.csv`, and
`protocol.jsonl`, following the same output family as the Windows app.

## Build from source

From the repository root:

```bash
swift test --package-path macos
bash tools/macos/build-app.sh 0.1.0
open artifacts/macos/PolarH10.app
```

The packaging script compiles `arm64` and `x86_64` binaries, combines them,
creates the app bundle and icon, applies an ad-hoc signature, verifies it, and
archives the result. Launch the app bundle for live development so macOS can
read its Bluetooth privacy usage description from `Info.plist`.

## Bluetooth identifiers on macOS

CoreBluetooth intentionally represents peripherals with app-scoped UUIDs on
macOS. The device rail and exported `DeviceAddress` field therefore contain the
CoreBluetooth UUID rather than the hardware MAC address shown by the Windows
transport.

## Current Mac feature scope

The native Mac preview includes the core single-sensor operator path:

- scan, connect, disconnect, and battery status
- live HR, RR, ECG, and ACC magnitude
- RMSSD, SDNN, pNN50, mean NN/HR, and RR-derived coherence
- compatible session-file capture

The fuller Windows WPF surface still includes multi-device overlays, its
calibrated ACC breathing workflow, breathing-dynamics entropy, and the full
diagnostic/tuning panels. These are not presented as complete Mac parity yet.

## Troubleshooting

### No devices appear

- Confirm Bluetooth is on.
- Wear the strap; the H10 normally advertises when its electrodes detect contact.
- Wet the electrode areas and scan again.
- In `System Settings > Privacy & Security > Bluetooth`, confirm PolarH10 is enabled.
- Disconnect the strap from another app or phone that may already own the link.

### Heart rate works but ECG or ACC does not

HR uses the standard Heart Rate Service. ECG and ACC use Polar's PMD service.
Disconnect, reconnect, and wait for `PMD live`. Older firmware or a competing
connection can prevent PMD stream startup even while HR continues.

### The app is blocked after replacement

Control-click the newly downloaded app and choose `Open`. If necessary, approve
only PolarH10 through `Privacy & Security > Open Anyway`. Replacing the app with
a new research-preview build can require this step again.

## Related pages

- [Download & Install](../download.md)
- [Output Formats](../output-formats.md)
- [Heart Rate Measurement](../protocol/hr-measurement.md)
- [ECG Frame Format](../protocol/ecg-format.md)
- [ACC Frame Format](../protocol/acc-format.md)
