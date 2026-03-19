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
flowchart LR
    R["POLARH10<br/>WINDOWS H10 WORKSPACE"]

    subgraph Source["src/ // runtime code"]
        P1["Protocol<br/>decoders · PMD · recording"]
        P2["Transport.Abstractions<br/>BLE contracts"]
        P3["Transport.Windows<br/>scanner · GATT · session"]
        P4["Cli<br/>diagnostics · capture"]
        P5["App<br/>WPF telemetry monitor"]
    end

    subgraph Tests["tests/ // verification"]
        T1["Protocol.Tests<br/>unit decoders"]
        T2["Playback.Tests<br/>session compatibility"]
        T3["Transport.Windows.Tests<br/>smoke coverage"]
    end

    subgraph Docs["docs/ // published reference"]
        D1["protocol/<br/>GATT · PMD · ECG · ACC · HR"]
        D2["getting-started.md<br/>cli.md · references.md"]
        D3["diagrams/<br/>mmd sources · svg output"]
    end

    subgraph Tooling["tools/ + workflow"]
        TL1["tools/<br/>fixtures · validators · Pages build"]
        TL2["package.json<br/>Mermaid CLI scripts"]
        TL3[".github/workflows/<br/>Pages deploy"]
    end

    subgraph Samples["sample data"]
        S1["protocol-transcripts/"]
        S2["sample-sessions/"]
    end

    R --> Source
    R --> Tests
    R --> Docs
    R --> Tooling
    R --> Samples
    TL1 --> D3
    TL2 --> D3
    TL3 --> D3

    style Source fill:#FFF4EF,stroke:#F0343C,stroke-width:1.5px;
    style Tests fill:#F1F0FF,stroke:#4E35C5,stroke-width:1.5px;
    style Docs fill:#EEF7FC,stroke:#2E66FF,stroke-width:1.5px;
    style Tooling fill:#FFF6D8,stroke:#FF8A1A,stroke-width:1.5px;
    style Samples fill:#E8FAFF,stroke:#18C2FF,stroke-width:1.5px;

    classDef hub fill:#171B27,stroke:#FFE22B,color:#F7FBFC,stroke-width:2px;
    classDef source fill:#FFE7DD,stroke:#F0343C,color:#171B27,stroke-width:1.5px;
    classDef tests fill:#E7EAFF,stroke:#4E35C5,color:#171B27,stroke-width:1.5px;
    classDef docs fill:#F7FBFC,stroke:#2E66FF,color:#171B27,stroke-width:1.5px;
    classDef tooling fill:#FFF0B8,stroke:#FF8A1A,color:#171B27,stroke-width:1.5px;
    classDef sample fill:#DDF6FF,stroke:#18C2FF,color:#171B27,stroke-width:1.5px;
    class R hub;
    class P1,P2,P3,P4,P5 source;
    class T1,T2,T3 tests;
    class D1,D2,D3 docs;
    class TL1,TL2,TL3 tooling;
    class S1,S2 sample;
    linkStyle default stroke:#4B556D,stroke-width:1.8px;
```

<!-- MERMAID:END repo-structure -->

## Architecture

<!-- MERMAID:BEGIN code-architecture -->

```mermaid
flowchart LR
    subgraph Protocol["PROTOCOL CORE"]
        EC["PolarEcgDecoder<br/>24-bit uV samples"]
        AC["PolarAccDecoder<br/>mG + compressed deltas"]
        HR["PolarHrRrDecoder<br/>HR / RR parsing"]
        PMD["PolarPmdCommandBuilder<br/>settings · start · stop"]
        CP["PolarPmdControlPointParser<br/>response decoding"]
        GID["PolarGattIds<br/>service + characteristic UUIDs"]
    end

    subgraph Transport["TRANSPORT CONTRACTS"]
        IS["IBleScanner"]
        IC["IBleConnection"]
        IG["IGattServiceHandle<br/>IGattCharacteristicHandle"]
    end

    subgraph Windows["WINDOWS BLE"]
        WS["WindowsBleScanner<br/>advertisement watcher"]
        WC["WindowsBleConnection<br/>GATT session"]
        WG["WindowsGattServiceHandle<br/>WindowsGattCharacteristicHandle"]
        SE["PolarH10Session<br/>PMD lifecycle + streaming"]
        CO["PolarMultiDeviceCoordinator<br/>device orchestration"]
    end

    subgraph Recording["RECORDING + STATE"]
        RE["PolarSessionRecorder<br/>CSV + JSONL export"]
        SD["SessionDiscovery<br/>scan runs + sessions"]
        MA["CaptureRunManifest<br/>run.json multi-device"]
        DR["PolarDeviceRegistry<br/>address / alias registry"]
    end

    subgraph Surfaces["OPERATOR SURFACES"]
        C1["CLI<br/>scan · doctor · record · replay"]
        G1["WPF monitor<br/>selection · tabs · diagnostics"]
        G2["WaveformChart<br/>live telemetry rendering"]
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
    DR --> RE
    DR --> G1
    CO --> C1
    CO --> G1
    SE --> C1
    SE --> G1
    G1 --> G2

    style Protocol fill:#FFF1EB,stroke:#F0343C,stroke-width:1.5px;
    style Transport fill:#F2EFFF,stroke:#4E35C5,stroke-width:1.5px;
    style Windows fill:#E8F8FF,stroke:#18C2FF,stroke-width:1.5px;
    style Recording fill:#FFF6D8,stroke:#FF8A1A,stroke-width:1.5px;
    style Surfaces fill:#EEF7FC,stroke:#2E66FF,stroke-width:1.5px;

    classDef core fill:#FFE7DD,stroke:#F0343C,color:#171B27,stroke-width:1.5px;
    classDef contracts fill:#EAE6FF,stroke:#4E35C5,color:#171B27,stroke-width:1.5px;
    classDef windows fill:#DDF6FF,stroke:#18C2FF,color:#171B27,stroke-width:1.5px;
    classDef active fill:#171B27,stroke:#18C2FF,color:#F7FBFC,stroke-width:2px;
    classDef record fill:#FFF0B8,stroke:#FF8A1A,color:#171B27,stroke-width:1.5px;
    classDef surface fill:#E7F2FF,stroke:#2E66FF,color:#171B27,stroke-width:1.5px;
    class EC,AC,HR,PMD,CP,GID core;
    class IS,IC,IG contracts;
    class WS,WC,WG windows;
    class SE,CO active;
    class RE,SD,MA,DR record;
    class C1,G1,G2 surface;
    linkStyle default stroke:#4B556D,stroke-width:1.8px;
```

<!-- MERMAID:END code-architecture -->

## Data Flow

<!-- MERMAID:BEGIN data-flow -->

```mermaid
flowchart LR
    H10["POLAR H10<br/>CHEST STRAP"]

    subgraph Link["BLE / GATT LINK"]
        ADV["Advertisement<br/>name · address · RSSI"]
        HRS["Heart Rate Service<br/>0x180D"]
        PMD["PMD Service<br/>FB005C80-..."]
        CTRL["PMD control point<br/>get-settings · start · stop"]
        DATA["PMD data<br/>ECG frames · ACC frames"]
    end

    subgraph Decode["DECODERS"]
        DHR["HrRrDecoder<br/>bpm + RR intervals"]
        DECG["EcgDecoder<br/>uV samples @ 130 Hz"]
        DACC["AccDecoder<br/>x / y / z mG"]
    end

    subgraph Session["SESSION EVENTS"]
        SHR["HeartRateReceived"]
        SECG["EcgFrameReceived"]
        SACC["AccFrameReceived"]
    end

    subgraph Output["CONSUMERS"]
        REC["PolarSessionRecorder<br/>hr.csv · ecg.csv · acc.csv · protocol.jsonl"]
        CHART["WaveformChart<br/>live telemetry surfaces"]
        LOG["Runtime logs<br/>WPF + CLI diagnostics"]
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
    CTRL --> LOG

    style Link fill:#EAF8FF,stroke:#18C2FF,stroke-width:1.5px;
    style Decode fill:#FFF1EB,stroke:#F0343C,stroke-width:1.5px;
    style Session fill:#F2EFFF,stroke:#4E35C5,stroke-width:1.5px;
    style Output fill:#FFF6D8,stroke:#FF8A1A,stroke-width:1.5px;

    classDef hardware fill:#171B27,stroke:#FFE22B,color:#F7FBFC,stroke-width:2px;
    classDef link fill:#DDF6FF,stroke:#18C2FF,color:#171B27,stroke-width:1.5px;
    classDef decode fill:#FFE7DD,stroke:#F0343C,color:#171B27,stroke-width:1.5px;
    classDef session fill:#EAE6FF,stroke:#4E35C5,color:#171B27,stroke-width:1.5px;
    classDef output fill:#FFF0B8,stroke:#FF8A1A,color:#171B27,stroke-width:1.5px;
    class H10 hardware;
    class ADV,HRS,PMD,CTRL,DATA link;
    class DHR,DECG,DACC decode;
    class SHR,SECG,SACC session;
    class REC,CHART,LOG output;
    linkStyle default stroke:#4B556D,stroke-width:1.8px;
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
