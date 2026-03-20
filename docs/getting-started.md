---
title: Getting Started on Windows
description: Build the repo, run the WPF app or CLI, and verify the first working Polar H10 session on Windows.
summary: Clone the real repo, build once, verify Bluetooth access, and choose either the WPF app or CLI for your first session.
nav_label: Getting Started
nav_group: Start Here
nav_order: 30
---

# Getting Started on Windows

## Prerequisites

- Windows 10 version 1903 (build 19041) or later
- .NET 8.0 SDK
- A Polar H10 chest strap
- Bluetooth Low Energy (BLE) adapter

## Build from source

```powershell
git clone https://github.com/MesmerPrism/PolarH10.git
cd PolarH10
dotnet build PolarH10.sln
```

## Quick start with the CLI

```powershell
# Scan for nearby Polar H10 devices
dotnet run --project src/PolarH10.Cli -- scan

# Monitor a connected device
dotnet run --project src/PolarH10.Cli -- monitor --device <ADDRESS>

# Record a session to disk
dotnet run --project src/PolarH10.Cli -- record --device <ADDRESS> --out ./my-session
```

## Quick start with the GUI

```powershell
dotnet run --project src/PolarH10.App
```

The GUI provides:
1. **Device rail** — scan nearby straps, assign aliases, and choose the current control target
2. **Live** tab — real-time HR, RR, ECG, and ACC with a tracked-device dropdown for parallel charting
3. **Live tab views** — raw telemetry plus coherence, HRV, and breathing-dynamics tabs for focused review
4. **Overlay** tab — dense single-device view for the currently selected strap
5. **Breathing** tab — ACC breathing calibration, live state, telemetry, and tuning
6. **Record** tab — save sessions to CSV
7. **Diagnostics** tab — raw PMD control messages for debugging

If you are working with more than one strap:

1. scan until both Polar H10 units appear in the device rail
2. connect each device from the left-hand control flow
3. open `Tracked devices` on the Live tab
4. leave `Follow selected device` enabled for normal one-device work, or disable it and tick multiple straps for parallel research tracking

## Manual breathing test

Use the `Breathing` tab once the strap is connected and ACC is flowing.

1. connect the intended H10 and confirm HR plus ACC counters are moving
2. open `Breathing`
3. press `Start calibration`
4. breathe normally for the full calibration window
5. confirm the breathing chart moves and the state changes between inhale/exhale/pause
6. use `Flip inhale/exhale` if the state direction is reversed for your strap orientation

For the full operator checklist, see [Breathing Workflow](breathing-workflow.md).

## Manual coherence test

Use the `Coherence window` from the Live tab once HR and RR are already updating.

1. connect the intended H10 and confirm heart rate plus RR intervals are moving
2. open `Coherence window`
3. confirm coherence stays unavailable during the early RR warmup
4. keep the session running until the coherence state moves from unavailable to live tracking
5. verify the coherence and confidence values start updating for the selected device
6. optionally switch a shared telemetry plot to `Coherence` or `Coherence confidence`

For the full operator checklist, see [Coherence Workflow](coherence-workflow.md).

## Manual HRV test

Use the `HRV` tab once heart rate and RR intervals are already updating.

1. connect the intended H10 and confirm heart rate plus RR intervals are moving
2. open `HRV`
3. confirm RMSSD stays unavailable during the early RR warmup
4. keep the session running until the short-term RR window fills
5. verify the RMSSD headline, SDNN, and pNN50 values start updating for the selected device
6. optionally switch a shared telemetry plot to `HRV (RMSSD)`

For the full operator checklist, see [HRV Workflow](hrv-workflow.md).

If you are testing through the sibling `SyntheticBio` harness, the synthetic
transport now exposes PMD ECG alongside the HRS RR stream. Coherence and HRV
still solve from RR, but the synthetic ECG pane should now track the same beat
schedule instead of staying blank. Synthetic breathing remains a direct
telemetry feed rather than PMD ACC emulation.

## Canonical workspace desktop build

If you want a stable repo-local executable instead of `dotnet run`, build the
workspace app into the canonical output that sibling tooling also uses:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Build-Workspace-App.ps1
.\out\workspace-app\PolarH10.App.exe
```

`SyntheticBio` and the WPF preview capture script both target this
`out\workspace-app\PolarH10.App.exe` path so they cannot drift to an older
`src\PolarH10.App\bin\...` executable.

## Manual breathing-dynamics test

Use the `Dynamics window` from the Live tab after breathing calibration is complete.

1. connect the intended H10 and confirm HR plus ACC counters are moving
2. open `Breathing` and complete calibration until tracking is live
3. return to `Live` and open `Dynamics window`
4. confirm interval and amplitude entropy stay unavailable during the warmup phase
5. keep breathing normally until the readiness text switches from basic stats to entropy-ready
6. verify the interval and amplitude entropy tiles, feature bundles, and shared telemetry plots update for the selected device

For the full operator checklist, see [Breathing Dynamics Workflow](breathing-dynamics-workflow.md).

## Manual single-file app run

If Windows application-control policy blocks the normal multi-file workspace
build on this machine, publish a single-file build for manual testing:

```powershell
dotnet publish src/PolarH10.App/PolarH10.App.csproj `
  -c Release `
  -r win-x64 `
  -p:PublishSingleFile=true `
  -p:SelfContained=false `
  -o out/app-single
```

Then run:

```powershell
.\out\app-single\PolarH10.App.exe
```

## Package identity for Bluetooth

Windows GATT access requires Bluetooth capabilities declared in a package manifest.
For development, you can run the apps directly. For distribution, you should package
with MSIX or use packaging with external location to provide the required identity.

See Microsoft's documentation on
[specifying device capabilities for Bluetooth](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/how-to-specify-device-capabilities-for-bluetooth)
and [calling WinRT APIs from desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-enhance).
