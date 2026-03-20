---
title: Output Formats
description: Session folder layout, file meanings, and the difference between per-device session files and multi-device run manifests.
summary: Use this page when the capture finished and you want to know what each file contains and when additional manifest files appear.
nav_label: Output Formats
nav_group: Task Guides
nav_order: 30
---

# Output Formats

The repo writes a small set of stable capture files so sessions can be inspected,
replayed, or post-processed without guessing.

## Standard Session Files

### `session.json`

Session metadata for one device capture.

Key fields include:

- `SchemaVersion`
- `SessionId`
- `DeviceName`
- `DeviceAddress`
- `DeviceAlias`
- `StartedAtUtc`
- `SavedAtUtc`
- `HrRrSampleCount`
- `EcgFrameCount`
- `AccFrameCount`
- `TranscriptEntryCount`

### `hr_rr.csv`

Heart-rate and RR-interval output derived from the standard Heart Rate Service.

Use it when you need:

- BPM values over time
- RR interval timing for beat-to-beat analysis
- a lightweight export without parsing the protocol transcript

### `ecg.csv`

Decoded ECG frames written as timestamped sample data in microvolts.

Use it when you need:

- the high-resolution ECG stream
- replay or offline waveform analysis
- a CSV export instead of parsing PMD payloads yourself

### `acc.csv`

Decoded accelerometer samples for X, Y, and Z in milliG.

Use it when you need:

- motion review
- breathing or posture-related downstream work
- a plain sensor export instead of raw compressed PMD frames

### `protocol.jsonl`

Line-delimited protocol transcript entries.

Typical entries include:

- CCCD writes
- PMD get-settings requests and responses
- PMD start and stop traffic
- incoming and outgoing control-point messages

Use it when you need to debug startup, settings negotiation, or transport failures.

## Multi-Device Run Manifest

### `run.json`

When a parent recording contains multiple device subfolders, the repo can also
write `run.json`.

This manifest contains:

- `RunId`
- run start and stop timestamps
- one entry per device session
- device address and alias
- the subfolder name for each device
- per-device sample counts

## Session Folder Naming

Per-device session folders use a deterministic timestamp plus device tag format:

```text
yyyyMMdd-HHmmssZ_alias-or-address
```

Spaces and invalid filename characters are normalized so the folder is safe to
write on disk.

## Related Pages

- [First Recording](first-recording.md)
- [CLI Reference](cli.md)
- [Protocol Overview](protocol/overview.md)
