---
title: FAQ
description: Short answers to the questions first-time users and contributors are likely to ask before reading the deeper protocol reference.
summary: This page covers the high-friction questions that come up after a first scan, first build, or first saved session.
nav_label: FAQ
nav_group: Troubleshooting
nav_order: 20
---

# FAQ

## Do I Need The Official Polar SDK?

No. This repo talks to the Polar H10 directly over standard BLE/GATT on Windows.

## Is This Project Official?

No. It is an unofficial open-source implementation and is not endorsed by or
affiliated with Polar Electro Oy.

## What Runs On Windows Right Now?

The repo currently targets a Windows-first workflow:

- `PolarH10.App` for the WPF operator surface
- `PolarH10.Cli` for direct command-line use
- `PolarH10.Transport.Windows` for the WinRT BLE transport

## Which Signals Can I Inspect Or Record?

- Heart rate and RR intervals
- ECG
- Accelerometer data
- RR-derived coherence and coherence confidence in the WPF app
- Breath interval entropy and breath amplitude entropy in the WPF app once breathing dynamics is warm
- Protocol transcript output for control-point and notification debugging

## Can I Compare More Than One Strap At Once?

Yes. The WPF app supports parallel multi-strap telemetry tracking on the Live
surface while keeping one selected control target for connect, record, and
diagnostics actions.

## Why Do Coherence Or Entropy Values Start At `--`?

Because both derived modules have a warmup phase:

- coherence needs enough accepted RR coverage to build a stable resonance window
- breathing dynamics needs successful breathing calibration plus enough accepted breaths for entropy metrics

That is expected behavior, not an automatic failure.

## What Should I Read First If I Only Want One Working Session?

Start with:

1. [Getting Started on Windows](getting-started.md)
2. [First Recording](first-recording.md)
3. [Troubleshooting](troubleshooting.md) if the first attempt is not clean

## Where Do I Learn The Wire Format?

Use the protocol docs:

- [Protocol Overview](protocol/overview.md)
- [GATT Service & Characteristic Map](protocol/gatt-map.md)
- [PMD Control Point Commands](protocol/pmd-commands.md)
- [ECG Data Format](protocol/ecg-format.md)
- [Accelerometer Data Format](protocol/acc-format.md)
- [Heart Rate Measurement Format](protocol/hr-measurement.md)
