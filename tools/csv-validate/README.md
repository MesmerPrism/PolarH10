# CSV Validate Tool

Validates that CSV files produced by `PolarSessionRecorder` conform to the expected
schema (correct headers, data types, and ranges).

## Usage

```powershell
dotnet run --project tools/csv-validate -- samples/sample-sessions/demo-10s/
```

## Checks

- `hr_rr.csv`: Headers match `TimestampMs,HeartRateBpm,RrIntervalMs`; HR in 30–220 BPM;
  RR > 0
- `ecg.csv`: Headers match `TimestampNs,MicroVolts`; timestamps monotonically increasing
- `acc.csv`: Headers match `TimestampNs,X_mG,Y_mG,Z_mG`; values within ±16000 mG

> **TODO**: Implement as a standalone console app or script.
