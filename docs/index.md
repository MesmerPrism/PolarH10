---
title: Docs Home
description: Start here if you want to use a Polar H10 on Windows without the Polar SDK, then drop into protocol and transport internals only when you need them.
summary: Use the WPF app or CLI to get from nearby strap to saved session first; protocol docs and Mermaid maps come after the operator path is clear.
nav_label: Docs Home
nav_group: Start Here
nav_order: 10
---

# PolarH10 App + Protocol Reference

Use a Polar H10 on Windows without the Polar SDK. Scan nearby straps, inspect
live HR, ECG, and ACC data, review RR-derived coherence and breathing-dynamics
entropy, compare multiple active straps, and record reusable sessions from a
WPF app or CLI.

## Quick Start

- Windows 10 version 1903 or later
- .NET 8.0 SDK
- Bluetooth LE adapter
- Polar H10 chest strap

```powershell
git clone https://github.com/MesmerPrism/PolarH10.git
cd PolarH10
dotnet build PolarH10.sln
dotnet run --project src/PolarH10.App
```

If you want the terminal path first instead of the desktop app, run:

```powershell
dotnet run --project src/PolarH10.Cli -- scan
```

## What This Project Is

- A direct BLE/GATT workflow for the Polar H10 on Windows.
- A practical WPF app for scanning, connecting, inspecting live telemetry, reviewing coherence and breathing-dynamics windows, and recording sessions.
- A CLI for scripted scan, doctor, monitor, record, replay, and session review work.
- A protocol and transport reference once you need PMD, GATT, or decoder internals.

## What This Project Is Not

- It is not an official Polar SDK or a project endorsed by Polar Electro Oy.
- It is not a cross-platform mobile stack.
- It is not a medical device or a substitute for clinical interpretation.

## Choose Your Path

### Use the WPF app

- [App Overview](app-overview.md)
- [Getting Started on Windows](getting-started.md)
- [First Recording](first-recording.md)
- [Coherence Workflow](coherence-workflow.md)
- [Breathing Workflow](breathing-workflow.md)
- [Breathing Dynamics Workflow](breathing-dynamics-workflow.md)

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

- [Getting Started on Windows](getting-started.md)
- [First Recording](first-recording.md)
- [Troubleshooting](troubleshooting.md)
- [FAQ](faq.md)

## When You Need More Detail

- [WPF UI Preview](ui-preview.md)
- [Output Formats](output-formats.md)
- [Platform Guides](platform-guides/index.md)
- [ECG Frame Format](protocol/ecg-format.md)
- [ACC Frame Format](protocol/acc-format.md)
- [Heart Rate Measurement Decoding](protocol/hr-measurement.md)
- [Citations & References](references.md)
