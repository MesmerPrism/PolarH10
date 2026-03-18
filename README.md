# PolarH10

> **Unofficial** Polar H10 reference connector for .NET.
> Not affiliated with or endorsed by Polar Electro.

A protocol-first .NET 8 library and toolset for direct BLE/GATT communication with the
[Polar H10](https://www.polar.com/en/sensors/h10-heart-rate-sensor) chest strap on
Windows. Streams ECG, accelerometer, and heart rate data without the Polar SDK.

## Features

- **Protocol layer** - pure C# decoders for ECG (24-bit uV), accelerometer (mG with
  compressed deltas), and standard BLE heart rate / RR intervals
- **PMD command builder** - construct get-settings, start, and stop commands for the
  Polar Measurement Data service
- **Windows BLE transport** - WinRT-based scanner, connection, GATT service/characteristic
  handles with notification support
- **CLI** - scan, monitor, record, stream, doctor, replay, and protocol reference commands
- **WPF reference app** - live dashboard with connect, record, and diagnostics tabs
- **Session recorder** - save HR/RR, ECG, and ACC data to CSV with JSONL protocol
  transcripts

## Project Structure

<!-- MERMAID:BEGIN repo-structure -->

```mermaid
flowchart TB
    R["PolarH10"]

    subgraph Source["Source (src/)"]
        P1["PolarH10.Protocol\ndecoders - builders - recording"]
        P2["PolarH10.Transport.Abstractions\nBLE interfaces"]
        P3["PolarH10.Transport.Windows\nWinRT GATT - session - coordinator"]
        P4["PolarH10.Cli\nSystem.CommandLine CLI"]
        P5["PolarH10.App\nWPF reference monitor"]
    end

    subgraph Tests["Tests (tests/)"]
        T1["Protocol.Tests\nunit decoders"]
        T2["Playback.Tests\nsession & legacy compat"]
        T3["Transport.Windows.Tests\nsmoke tests"]
    end

    subgraph Docs["Documentation (docs/)"]
        D1["protocol/\nGATT map - PMD - ECG - ACC - HR"]
        D2["getting-started.md - cli.md"]
        D3["diagrams/\nMermaid sources & SVGs"]
    end

    subgraph Tooling["Tooling"]
        TL1["tools/\ncapture-fixture - csv-validate"]
        TL2["package.json\nMermaid CLI scripts"]
        TL3[".github/workflows/"]
    end

    subgraph Samples["Samples"]
        S1["protocol-transcripts/"]
        S2["sample-sessions/"]
    end

    R --> Source
    R --> Tests
    R --> Docs
    R --> Tooling
    R --> Samples
    TL2 --> D3
```

<!-- MERMAID:END repo-structure -->

## Architecture

<!-- MERMAID:BEGIN code-architecture -->

```mermaid
flowchart LR
    subgraph Protocol["Protocol Layer (platform-independent)"]
        EC["PolarEcgDecoder\n24-bit uV samples"]
        AC["PolarAccDecoder\nmG + compressed deltas"]
        HR["PolarHrRrDecoder\nBLE HR/RR parsing"]
        PMD["PolarPmdCommandBuilder\nsettings - start - stop"]
        CP["PolarPmdControlPointParser\nresponse decoding"]
        GID["PolarGattIds\nservice & char UUIDs"]
    end

    subgraph Transport["Transport Abstractions"]
        IS["IBleScanner"]
        IC["IBleConnection"]
        IG["IGattServiceHandle\nIGattCharacteristicHandle"]
    end

    subgraph Windows["Windows Transport (WinRT)"]
        WS["WindowsBleScanner\nadvertisement watcher"]
        WC["WindowsBleConnection\nGATT session"]
        WG["WindowsGattServiceHandle\nWindowsGattCharacteristicHandle"]
        SE["PolarH10Session\nPMD lifecycle - streaming"]
        CO["PolarMultiDeviceCoordinator\nmulti-device orchestration"]
    end

    subgraph Recording["Recording & Playback"]
        RE["PolarSessionRecorder\nCSV + JSONL export"]
        SD["SessionDiscovery\nscan runs & sessions"]
        MA["CaptureRunManifest\nrun.json multi-device"]
        DR["PolarDeviceRegistry\naddress <-> alias"]
    end

    subgraph CLI["CLI (System.CommandLine)"]
        C1["scan - monitor - doctor"]
        C2["record - stream - replay"]
        C3["sessions - protocol"]
    end

    subgraph GUI["WPF App"]
        G1["MainWindow\nmulti-device dashboard"]
        G2["WaveformChart\nlive ECG / ACC"]
    end

    PMD --> SE
    CP --> SE
    EC --> SE
    AC --> SE
    HR --> SE
    GID --> SE
    IS -.-> WS
    IC -.-> WC
    IG -.-> WG
    WC --> SE
    WG --> SE
    SE --> CO
    SE --> RE
    RE --> SD
    RE --> MA
    CO --> C1
    CO --> C2
    SE --> C1
    SE --> C2
    SD --> C3
    CO --> G1
    SE --> G1
    G1 --> G2
    DR --> RE
    DR --> G1
```

<!-- MERMAID:END code-architecture -->

## Data Flow

<!-- MERMAID:BEGIN data-flow -->

```mermaid
flowchart TD
    H10["Polar H10 chest strap"]

    subgraph BLE["BLE / GATT"]
        ADV["Advertisement\ndevice name - address - RSSI"]
        HRS["Heart Rate Service\n0x180D"]
        PMD["PMD Service\nFB005C80-..."]
        CTRL["PMD Control Point\nget-settings - start - stop"]
        DATA["PMD Data\nECG frames - ACC frames"]
    end

    subgraph Decode["Decoders"]
        DHR["HrRrDecoder\n-> bpm + RR intervals"]
        DECG["EcgDecoder\n-> uV samples @ 130 Hz"]
        DACC["AccDecoder\n-> x/y/z mG @ 25-200 Hz"]
    end

    subgraph Session["PolarH10Session"]
        SHR["HeartRateReceived event"]
        SECG["EcgSamplesReceived event"]
        SACC["AccSamplesReceived event"]
    end

    subgraph Output["Consumers"]
        REC["PolarSessionRecorder\nhr.csv - ecg.csv - acc.csv\nprotocol.jsonl"]
        CHART["WaveformChart\nlive waveform rendering"]
        LOG["RuntimeLogManager / HUD"]
    end

    H10 --> ADV
    H10 --> HRS
    H10 --> PMD
    PMD --> CTRL
    PMD --> DATA

    HRS --> DHR --> SHR
    DATA --> DECG --> SECG
    DATA --> DACC --> SACC

    SHR --> REC
    SECG --> REC
    SACC --> REC
    SHR --> LOG
    SECG --> CHART
    SACC --> CHART
```

<!-- MERMAID:END data-flow -->

## Prerequisites

- Windows 10 version 1903 or later
- .NET 8.0 SDK
- Bluetooth LE adapter
- Polar H10 chest strap (firmware 3.x+)

## Build

```powershell
dotnet build PolarH10.sln
```

## Test

```powershell
dotnet test PolarH10.sln
```

## Quick Start - CLI

```powershell
# Scan for nearby Polar devices
dotnet run --project src/PolarH10.Cli -- scan

# Monitor live data
dotnet run --project src/PolarH10.Cli -- monitor --device <ADDRESS>

# Record a 60-second session
dotnet run --project src/PolarH10.Cli -- record --device <ADDRESS> --duration 60

# Verify connectivity
dotnet run --project src/PolarH10.Cli -- doctor --device <ADDRESS>
```

## Quick Start - WPF App

```powershell
dotnet run --project src/PolarH10.App
```

Enter a device address (or scan to find one), click **Connect**, and start recording.

## Documentation

See the [docs/](docs/) folder:

- [Getting Started](docs/getting-started.md)
- [CLI Reference](docs/cli.md)
- [Protocol Overview](docs/protocol/overview.md)
- [GATT Map](docs/protocol/gatt-map.md)
- [PMD Commands](docs/protocol/pmd-commands.md)
- [ECG Format](docs/protocol/ecg-format.md)
- [ACC Format](docs/protocol/acc-format.md)
- [HR Measurement](docs/protocol/hr-measurement.md)
- [Platform Guides](docs/platform-guides/index.md)
- [References](docs/references.md)
- [Diagrams](docs/diagrams/) - interactive Mermaid viewer and SVG renders

## Diagram Toolchain

The repo includes a Mermaid-based diagram pipeline. Diagrams live as `.mmd`
source files in `docs/diagrams/` and are registered in
`docs/diagrams/manifest.json`. Pre-rendered SVGs are generated by the Mermaid
CLI; the browser-based viewer (`docs/diagrams/viewer.html`) can also live-render
from source.

```powershell
npm install                   # first time - installs mmdc, browser-sync, etc.
npm run diagram:render:all    # render all .mmd -> .svg
npm run diagram:sync:readme   # inject .mmd content into README marker blocks
npm run diagram:dev           # watch + live browser preview
```

## GitHub Pages

The repository now includes a GitHub Pages workflow that:

- renders all Mermaid diagrams to SVG
- builds a static site from the Markdown docs
- deploys the generated `site/` artifact via GitHub Actions

```powershell
npm run pages:build           # build the full Pages site into ./site
npm run pages:serve           # preview the generated site locally
npm run pages:dev             # watch docs + diagrams and auto-rebuild the site
```

## References

- [Polar BLE SDK](https://github.com/polarofficial/polar-ble-sdk) (MIT License) -
  technical documentation at
  [tag 4.0.0](https://github.com/polarofficial/polar-ble-sdk/tree/4.0.0/technical_documentation/)
- Siecinski, S. et al., "The Newer, the More Secure? Comparing the Polar Verity Sense
  and H10 Heart Rate Sensors," *Sensors*, vol. 25, no. 7, 2025.
  [DOI: 10.3390/s25072005](https://doi.org/10.3390/s25072005)

## License

[MIT](LICENSE)

## Disclaimer

This project communicates directly with the Polar H10 via standard Bluetooth Low Energy.
It is not affiliated with, endorsed by, or certified by Polar Electro Oy. Use at your
own risk. Always consult a medical professional before using ECG data for health
decisions.
