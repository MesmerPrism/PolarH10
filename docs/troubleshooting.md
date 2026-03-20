---
title: Troubleshooting
description: Fast checks for the failure cases most likely to block first-time Windows users during scan, connect, stream, or record.
summary: Start here when the strap is not visible, the connection fails, data does not move, or Windows blocks the normal app launch path.
nav_label: Troubleshooting
nav_group: Troubleshooting
nav_order: 10
---

# Troubleshooting

Use this page for the shortest recovery path before diving into deeper protocol
or transport investigation.

## The Strap Does Not Show Up During Scan

- Confirm the strap is being worn and the electrodes are wet.
- Close Polar Beat, Polar Flow, and any other BLE client that might already own the connection.
- Move the strap closer to the adapter and rescan.
- Use the CLI scan path to confirm the problem is not only in the WPF surface:

  ```powershell
  dotnet run --project src/PolarH10.Cli -- scan
  ```

## The Device Appears But Connect Fails

- Run a direct connectivity check:

  ```powershell
  dotnet run --project src/PolarH10.Cli -- doctor --device <ADDRESS>
  ```

- If Windows reports `AccessDenied` during service or characteristic access, see the Windows BLE note in [Platform Guides](platform-guides/index.md).
- Disconnect any other client that could still be holding the GATT session.

## HR Connects But ECG Or ACC Never Starts Moving

- Check whether PMD startup actually succeeded in the diagnostics log or `protocol.jsonl`.
- Confirm notifications were enabled for the PMD control point and PMD data characteristic.
- Retry with a fresh connection before assuming the strap firmware or decoder is wrong.

## The Breathing Page Looks Wrong

- Do not calibrate until ACC is already flowing.
- If the chart moves but inhale and exhale are reversed, use `Flip inhale/exhale` instead of recalibrating.
- If the status never becomes calibrated, rerun the full [Breathing Workflow](breathing-workflow.md) without touching the strap during the calibration window.

## Coherence, HRV, Or Entropy Values Stay Unavailable

- Coherence depends on accepted RR intervals, not just a heart-rate number. If RR is not clean yet, the coherence window can stay unavailable.
- The HRV tab also depends on accepted RR intervals and, by default, a nearly full five-minute short-term RR window. A live heart-rate number alone is not enough.
- A low-confidence or warming-up coherence state is expected until the RR window has enough stable coverage.
- If the HRV tab stays on `Warming up`, keep the session running longer before treating it as a failure.
- Breathing-dynamics entropy depends on successful breathing calibration plus enough accepted breaths after calibration.
- If the breathing-dynamics plots stay flat at zero during warmup, that is expected until entropy becomes available.
- Use [Coherence Workflow](coherence-workflow.md), [HRV Workflow](hrv-workflow.md), and [Breathing Dynamics Workflow](breathing-dynamics-workflow.md) for the operator-side readiness checks.

## Windows Blocks The Normal App Launch

On this machine, Windows application-control policy can block the normal
multi-file .NET app launch path.

Use the single-file publish workaround:

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

## The Session Folder Exists But Looks Empty

- Open `session.json` and check whether the sample counts are zero.
- Inspect `protocol.jsonl` to see whether PMD start commands failed or the session stopped too early.
- Record a longer test session so HR, ECG, and ACC each have time to populate.

## If You Need More Detail

- [Platform Guides](platform-guides/index.md)
- [Output Formats](output-formats.md)
- [FAQ](faq.md)
