# Capture Fixture Tool

Connects to a Polar H10, runs a short capture session, and saves the output as a
test fixture in the `samples/` directory.

## Usage

```powershell
dotnet run --project tools/capture-fixture -- --device <ADDRESS> --duration 5 --out samples/sample-sessions/new-fixture
```

## Implementation

This tool reuses `PolarH10Session` from the Transport.Windows library and
`PolarSessionRecorder` from the Protocol library to capture and persist data.

> **TODO**: Implement as a standalone console app or script.
