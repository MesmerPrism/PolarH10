# Sample Protocol Transcripts

This folder contains JSONL protocol transcripts captured from real Polar H10 sessions.
Each line is a JSON object representing a single BLE operation.

## Format

```json
{
  "timestampUtc": "2025-01-15T10:30:00.000Z",
  "direction": "out",
  "label": "PMD_START_ECG",
  "characteristicUuid": "fb005c81-02e7-f387-1cad-8acd2d8df0c8",
  "payloadHex": "020000018200010e00"
}
```

| Field                | Description                                     |
|----------------------|-------------------------------------------------|
| `timestampUtc`       | ISO 8601 timestamp                              |
| `direction`          | `out` = host → device, `in` = device → host     |
| `label`              | Human-readable operation label                   |
| `characteristicUuid` | GATT characteristic UUID                         |
| `payloadHex`         | Raw payload as hex string                        |

## Files

- See `../sample-sessions/demo-10s/protocol.jsonl` for a complete example.
