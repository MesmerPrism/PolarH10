---
title: First Recording
description: The first end-to-end Polar H10 session on Windows, from scan and connection check to saved files on disk.
summary: Follow this page when you want one clean first capture, whether you prefer the WPF operator surface or the CLI, and use the derived windows only after the raw stream is already healthy.
nav_label: First Recording
nav_group: Task Guides
nav_order: 10
---

# First Recording

This page is the shortest reliable path from a nearby Polar H10 to a saved
session folder on disk.

## Before You Start

- Wear the strap and wet the electrodes.
- Make sure Polar Beat, Polar Flow, or another BLE client is not already connected to the same H10.
- Confirm Bluetooth is enabled on the Windows machine.
- Build the repo once with `dotnet build PolarH10.sln`.

## WPF App Path

1. Start the app:

   ```powershell
   dotnet run --project src/PolarH10.App
   ```

2. Scan for nearby straps and identify the intended device by alias or address.
3. Connect the device.
4. Confirm the session is healthy before recording:
   - HR updates begin
   - ACC counters move
   - ECG or ACC charts start updating on the Live tab
5. If you want RR-derived coherence, open [Coherence Workflow](coherence-workflow.md) only after HR and RR are already live.
6. If you want short-term HRV, open [HRV Workflow](hrv-workflow.md) only after HR and RR are already live and let the RR window run long enough for the first solve.
7. If you need breathing output or breathing-dynamics entropy, open [Breathing Workflow](breathing-workflow.md) first and [Breathing Dynamics Workflow](breathing-dynamics-workflow.md) only after calibration is complete.
8. Open the Record tab, choose the output folder, and start the capture.
9. Let the session run long enough to confirm real data is arriving, then stop cleanly.
10. Open the saved folder and verify that `session.json`, `hr_rr.csv`, `ecg.csv`, `acc.csv`, and `protocol.jsonl` exist.

## CLI Path

1. Scan for the strap:

   ```powershell
   dotnet run --project src/PolarH10.Cli -- scan
   ```

2. Run a quick connectivity check against the chosen Bluetooth address:

   ```powershell
   dotnet run --project src/PolarH10.Cli -- doctor --device <ADDRESS>
   ```

3. Record a short session:

   ```powershell
   dotnet run --project src/PolarH10.Cli -- record --device <ADDRESS> --duration 30 --out .\session\first-run
   ```

4. Confirm the output folder contains the expected files.
5. If you want to inspect the data without hardware, replay the saved session:

   ```powershell
   dotnet run --project src/PolarH10.Cli -- replay .\session\first-run
   ```

## What Success Looks Like

- `session.json` contains a session ID, timestamps, and nonzero sample counts.
- `hr_rr.csv` contains heart-rate rows.
- `ecg.csv` and `acc.csv` contain timestamped sensor samples.
- `protocol.jsonl` contains PMD and CCCD traffic that matches a clean start and stop sequence.
- If you open the coherence, HRV, or breathing-dynamics tabs, their values should move off `--` only after the relevant warmup and stabilization phases.

## If The First Attempt Fails

- Use [Troubleshooting](troubleshooting.md) for the fast failure checklist.
- Use [Platform Guides](platform-guides/index.md) if Windows BLE access or local application-control policy gets in the way.
- Use [Output Formats](output-formats.md) if files were written but you are not sure what they mean.
