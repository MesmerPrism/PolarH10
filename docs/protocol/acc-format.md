---
title: Accelerometer Data Format
description: Uncompressed and compressed accelerometer frame layout, units, and delta-decoding notes for PMD output.
summary: This is the accelerometer frame reference for X/Y/Z sample encoding and compressed delta blocks.
nav_label: ACC Format
nav_group: Internals
nav_order: 50
---

# Accelerometer Data Format

Accelerometer data arrives on the PMD Data characteristic as notification payloads.

## Frame Layout

```
[measurement_type] [timestamp_bytes (8)] [frame_type] [sample_data...]
```

- **measurement_type**: `0x02` (Accelerometer)
- **timestamp_bytes**: 8-byte little-endian nanosecond timestamp
- **frame_type**: `0x01` for uncompressed, `0x02` for compressed delta encoding

## Uncompressed Samples (Frame Type 1)

Each sample consists of three 16-bit signed little-endian integers representing
acceleration in milliG along the X, Y, and Z axes:

```
[x_lo] [x_hi] [y_lo] [y_hi] [z_lo] [z_hi]   // 6 bytes per sample
```

Total samples per frame = `(payload_length - 10) / 6` (subtracting the measurement type
byte, 8 timestamp bytes, and frame type byte).

## Compressed Delta Encoding (Frame Type 2)

This format packs more samples into a single notification by encoding deltas relative
to a reference sample.

### Structure

1. **Reference sample**: 3 × 16-bit signed LE values (X, Y, Z) = 6 bytes
2. **Delta block header**:
   - Number of samples (variable)
   - Bit width for each axis
3. **Bit-packed deltas**: each delta is a signed integer of the specified bit width,
   packed sequentially

### Decoding

1. Read the reference sample (first X, Y, Z values).
2. For each subsequent sample, read signed deltas of the declared bit width and add
   them to the previous sample's values.
3. Clamp each axis value to the `Int16` range (−32768 to 32767).

## Units and Conversion

| Parameter   | Value           |
|-------------|-----------------|
| Unit        | milliG (mg)     |
| Conversion  | ÷ 1000 → G     |
| Sample rate | Typically 25 Hz |
| Range       | Typically ±2 G  |
| Resolution  | 16 bits         |

## Notes

- The accelerometer range affects the scaling. At ±2 G the full 16-bit range maps to
  ±2000 mG.
- Compressed frames are more common at higher sample rates (50 Hz, 100 Hz) where the
  device needs to fit more samples per notification.
