# PMD Control Point Commands

The PMD Control Point characteristic (`FB005C81-...`) accepts write requests and returns
responses via indications. Each command starts with an operation byte followed by a
measurement type and optional parameters.

## Operation Codes

| Op Code | Name             | Direction      |
|---------|------------------|----------------|
| `0x01`  | Get Settings     | Write → Indicate |
| `0x02`  | Start Measurement| Write → Indicate |
| `0x03`  | Stop Measurement | Write → Indicate |

## Get Settings Request

Queries the device for available settings (sample rates, ranges, resolutions) for a
given measurement type.

```
[0x01] [measurement_type]
```

Example: query ECG settings → `0x01 0x00`

### Get Settings Response

The response indication begins with:

```
[0xF0] [op_code] [measurement_type] [error_code] [settings_data...]
```

- `0xF0` = response marker
- `error_code` = `0x00` for success
- Settings data is a sequence of TLV (type-length-value) entries:
  - **Type byte**: identifies the setting (e.g., `0x00` = sample rate, `0x01` = resolution,
    `0x02` = range)
  - **Length byte**: number of value bytes
  - **Value bytes**: little-endian encoded setting values

> Some firmware versions insert the settings payload starting at byte offset 4,
> others at offset 5. Our parser tries both offsets to handle this variation.

## Start Measurement Request

```
[0x02] [measurement_type] [setting_tlv_1] [setting_tlv_2] ...
```

Each setting TLV follows the same type-length-value format as the response.

### ECG Start

Typical parameters for ECG at 130 Hz, 14-bit resolution:

```
0x02 0x00
  0x00 0x01 0x82 0x00    // sample rate = 130
  0x01 0x01 0x0E 0x00    // resolution = 14
```

### ACC Start

Typical parameters for accelerometer at 25 Hz, 16-bit resolution, 2 G range.
Note the range TLV appears before the sample rate for ACC:

```
0x02 0x02
  0x02 0x01 0x02 0x00    // range = 2 (G)
  0x00 0x01 0x19 0x00    // sample rate = 25
  0x01 0x01 0x10 0x00    // resolution = 16
```

## Stop Measurement Request

```
[0x03] [measurement_type]
```

## Error Handling

If `error_code` in the response is nonzero, the command failed. Common causes:

- Feature not supported on the current firmware
- Invalid parameter combination
- Stream already active

## MTU Considerations

PMD data frames, especially ECG at 130 Hz, can exceed the default 23-byte ATT MTU.
Request a larger MTU (our implementation tries 232, then falls back through ordered
candidates) before starting high-rate streams. If the negotiated MTU is too small,
frames will be truncated and decoding will fail.
