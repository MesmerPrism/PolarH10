---
title: CLI Reference
description: Use the command-line path for scanning, doctor-style validation, recording, replay, and protocol output.
summary: The CLI is the direct path when you want a scriptable session flow, rawer diagnostics, or replay without the WPF app.
nav_label: CLI Guide
nav_group: Start Here
nav_order: 40
---

# CLI Reference

## Commands

### `polarh10 scan`

Discover nearby Polar H10 devices.

| Option        | Default | Description                |
|---------------|---------|----------------------------|
| `--duration`  | 10      | Scan duration in seconds   |
| `--json`      | false   | Output as JSON             |

### `polarh10 monitor`

Live terminal dashboard showing HR, RR, ECG, and ACC data.

| Option        | Default            | Description                              |
|---------------|--------------------|------------------------------------------|
| `--device`    | *(required)*       | Bluetooth address of the Polar H10       |
| `--channels`  | `hr,rr,ecg,acc`   | Comma-separated channels to display      |
| `--verbose`   | false              | Show PMD control point messages          |

### `polarh10 record`

Record a session to disk.

| Option        | Default     | Description                              |
|---------------|-------------|------------------------------------------|
| `--device`    | *(required)*| Bluetooth address of the Polar H10       |
| `--out`       | `./session` | Output folder                            |
| `--format`    | `csv`       | Output format                            |
| `--duration`  | *(none)*    | Duration in seconds (omit = indefinite)  |

Output files: `session.json`, `hr_rr.csv`, `ecg.csv`, `acc.csv`, `protocol.jsonl`

### `polarh10 stream ecg|acc`

Stream a single measurement to stdout for piping.

| Option        | Default      | Description                            |
|---------------|--------------|----------------------------------------|
| `--device`    | *(required)* | Bluetooth address of the Polar H10     |
| `--json`      | false        | Output as JSON lines                   |
| `--raw-hex`   | false        | Include raw hex payload                |

### `polarh10 doctor`

Verify connectivity: connect, discover services, enable notifications, start streams,
and confirm data arrives.

| Option        | Default      | Description                            |
|---------------|--------------|----------------------------------------|
| `--device`    | *(required)* | Bluetooth address of the Polar H10     |

### `polarh10 replay <session-path>`

Replay a previously recorded session from disk without requiring hardware.

### `polarh10 protocol markdown`

Print a concise Markdown protocol reference to stdout.
