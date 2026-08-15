---
title: Docs Home
description: Start here if you want to use a Polar H10 on Windows or macOS without the Polar SDK, then drop into protocol and transport internals only when needed.
summary: Use the native Mac app, Windows WPF app, or CLI to get from nearby strap to saved session before moving into protocol docs and diagrams.
nav_label: Docs Home
nav_group: Start Here
nav_order: 10
---

# PolarH10 Developer Reference

Use a Polar H10 on Windows or macOS without the Polar SDK. Scan nearby straps,
inspect live HR, ECG, and ACC data, review RR-derived coherence and short-term
HRV, and record reusable sessions from native desktop apps. The Windows surface
also provides multi-device comparison and the full breathing-dynamics workflow.

## Quick Start

- macOS 13+ for the native SwiftUI app, or Windows 10 version 1903+
- Xcode 15+ for Mac source builds; .NET 8.0 SDK for Windows source builds
- Bluetooth LE adapter
- Polar H10 chest strap

If you want a packaged desktop app instead of building from source, start with [Download & Install](download.md). The Research Preview includes a Universal Mac ZIP and a guided Windows installer.

```powershell
git clone https://github.com/MesmerPrism/PolarH10.git
cd PolarH10
dotnet build PolarH10.sln
dotnet run --project src/PolarH10.App
```

If you want the stable repo-local desktop executable that sibling launchers and
preview tooling use, build:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Build-Workspace-App.ps1
.\out\workspace-app\PolarH10.App.exe
```

If you want the terminal path first instead of the desktop app, run:

```powershell
dotnet run --project src/PolarH10.Cli -- scan
```

## What This Project Is

- A direct BLE/GATT workflow for the Polar H10 on Windows and macOS.
- A native SwiftUI/CoreBluetooth Mac app for single-sensor live telemetry, RR analysis, and session capture.
- A practical WPF app for scanning, connecting, inspecting live telemetry, reviewing coherence, HRV, and breathing-dynamics tabs, and recording sessions.
- A CLI for scripted scan, doctor, monitor, record, replay, and session review work.
- A protocol and transport reference once you need PMD, GATT, or decoder internals.

## What This Project Is Not

- It is not an official Polar SDK or a project endorsed by Polar Electro Oy.
- It is not a cross-platform mobile stack.
- It is not a medical device or a substitute for clinical interpretation.

## Choose Your Path

### Preview Polar Stream without hardware

- [Polar Stream Recorded Preview](polar-stream-preview.md)
- Open the browser interface and choose **Mock Data** to loop a real 60-second
  Polar H10 ECG and accelerometer recording.
- Open **Add output** to inspect each ECG/ACC metric's recorded outcome,
  scientific context, and citations before adding it; settings update the
  selected preview immediately.
- Start custom output from a built-in metric, then use the variable map, insert
  keyboard, and recorded before/after chart to edit it without memorizing the
  expression language.
- Build the native Tauri app when you want live Polar H10 input with LSL or OSC
  output; recorded-preview mode remains browser-only.

### Use the macOS app

- [Download & Install](download.md)
- [Getting Started on macOS](platform-guides/macos.md)
- [Output Formats](output-formats.md)

### Use the WPF app

- [Download & Install](download.md)
- [App Overview](app-overview.md)
- [Getting Started on Windows](getting-started.md)
- [First Recording](first-recording.md)
- [Coherence Workflow](coherence-workflow.md)
- [HRV Workflow](hrv-workflow.md)
- [Breathing Workflow](breathing-workflow.md)
- [Breathing Dynamics Workflow](breathing-dynamics-workflow.md)
- [Formula Sheets](formula-sheets.md)
- [Synthetic Showcase](synthetic-showcase/index.md)

### Use the CLI

- [Getting Started on Windows](getting-started.md)
- [CLI Reference](cli.md)
- [First Recording](first-recording.md)
- [Output Formats](output-formats.md)

### Study the protocol or library internals

- [Protocol Overview](protocol/overview.md)
- [GATT Service & Characteristic Map](protocol/gatt-map.md)
- [PMD Control Point Command Flow](protocol/pmd-commands.md)
- [Diagram Viewer](diagrams/viewer.html)

## Read These First

- [Polar Stream Recorded Preview](polar-stream-preview.md)
- [Getting Started on macOS](platform-guides/macos.md)
- [Getting Started on Windows](getting-started.md)
- [First Recording](first-recording.md)
- [Troubleshooting](troubleshooting.md)
- [FAQ](faq.md)

## When You Need More Detail

- [WPF UI Preview](ui-preview.md)
- [Formula Sheets](formula-sheets.md)
- [Synthetic Showcase](synthetic-showcase/index.md)
- [Output Formats](output-formats.md)
- [Platform Guides](platform-guides/index.md)
- [ECG Frame Format](protocol/ecg-format.md)
- [ACC Frame Format](protocol/acc-format.md)
- [Heart Rate Measurement Decoding](protocol/hr-measurement.md)
- [Citations & References](references.md)

## Feedback and Contributions

Use GitHub Issues for onboarding friction, device compatibility notes, protocol
questions, and doc fixes.

- [Open an issue](https://github.com/MesmerPrism/PolarH10/issues)
- [Read the contributing guide](https://github.com/MesmerPrism/PolarH10/blob/main/CONTRIBUTING.md)
