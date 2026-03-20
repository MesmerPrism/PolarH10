---
title: ECG Data Format
description: Frame layout, sample encoding, and runtime notes for Polar H10 ECG data on the PMD stream.
summary: Use this page when you need the exact ECG frame structure and 24-bit signed microvolt sample encoding.
nav_label: ECG Format
nav_group: Internals
nav_order: 40
---

# ECG Data Format

ECG data arrives on the PMD Data characteristic as notification payloads.

## Frame Layout

```
[measurement_type] [timestamp_bytes (8)] [frame_type] [sample_data...]
```

- **measurement_type**: `0x00` (ECG)
- **timestamp_bytes**: 8-byte little-endian nanosecond timestamp
- **frame_type**: frame encoding type (typically `0x00` for uncompressed)
- **sample_data**: sequence of 3-byte ECG samples

## Sample Encoding

Each ECG sample is a **24-bit signed integer** in little-endian byte order,
representing microvolts (µV).

```
byte[0] = LSB
byte[1] = middle byte
byte[2] = MSB (bit 7 = sign)
```

To decode:

1. Read 3 bytes as an unsigned 24-bit value: `raw = b[0] | (b[1] << 8) | (b[2] << 16)`
2. Sign-extend from 24 bits to 32 bits: if bit 23 is set, `raw |= 0xFF000000`
3. The resulting `int` is the sample value in microvolts

## Typical Parameters

| Parameter   | Value    |
|-------------|----------|
| Sample rate | 130 Hz   |
| Resolution  | 14 bits  |
| Unit        | µV       |

At 130 Hz with 3 bytes per sample, each notification frame carries multiple samples.
The exact count depends on the negotiated ATT MTU.

## Notes

- The 14-bit resolution setting at the PMD level does not change the wire encoding;
  samples are still transmitted as 24-bit values. The resolution setting controls the
  internal ADC configuration on the device.
- A negotiated MTU of at least 232 bytes is recommended to avoid truncation.
