# Heart Rate Measurement Format

Heart rate data uses the standard Bluetooth SIG Heart Rate Measurement characteristic
(`0x2A37`) on the Heart Rate Service (`0x180D`). This is not Polar-specific — any BLE
heart rate monitor uses this format.

## Payload Layout

```
[flags] [heart_rate ...] [rr_interval ...] [rr_interval ...]
```

### Flags Byte (byte 0)

| Bit | Meaning                                      |
|-----|----------------------------------------------|
| 0   | HR format: 0 = UINT8, 1 = UINT16            |
| 4   | RR intervals present: 0 = no, 1 = yes        |

Other flag bits (sensor contact, energy expended) are defined by the Bluetooth SIG spec
but are not used in our implementation.

### Heart Rate Value

- If flag bit 0 = 0: a single `uint8` at byte 1 → HR in BPM
- If flag bit 0 = 1: a `uint16` little-endian at bytes 1–2 → HR in BPM

### RR Intervals

If flag bit 4 is set, the remaining bytes after the heart rate value contain one or more
`uint16` little-endian RR interval values.

Each value is in units of **1/1024 seconds**. To convert to milliseconds:

```
rr_ms = raw_value × 1000.0 / 1024.0
```

Multiple RR intervals can appear in a single notification when multiple beats occurred
between transmissions (the Polar H10 typically sends HR notifications once per second).

## Typical Behavior

The Polar H10 sends heart rate notifications approximately once per second. The heart
rate value reflects the current calculated BPM, while the RR intervals provide the
precise inter-beat timing for the beats that occurred since the last notification.

## Notes

- The HR format flag (8-bit vs 16-bit) can change between notifications. Always check
  the flags byte for each notification.
- An empty notification (no RR intervals, HR = 0) can occur during the initial
  connection phase before the sensor locks onto a reliable signal.
