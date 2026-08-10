# Contributing to PolarH10

PolarH10 uses GitHub Issues as the public feedback path. Open an issue for
setup friction, docs gaps, Windows or macOS BLE edge cases, protocol questions, data
surprises, or feature requests. Small doc fixes are welcome.

## Before You Open A PR

- Search existing issues and pull requests first.
- Keep user-facing copy consistent: prefer `PolarH10`, `Mac app`, `WPF app`,
  and `Windows and macOS Polar H10 telemetry toolkit`.
- Keep `docs/diagrams/*.mmd` as the source of truth and commit regenerated
  `.svg` output when a diagram changes.
- Keep docs front matter valid YAML. GitHub preview and the Pages build should
  agree on every doc page.

## Local Validation

Run the .NET checks for code changes:

```powershell
dotnet build PolarH10.sln
dotnet test PolarH10.sln
```

Run the Swift checks for Mac app or shared Mac decoder changes on macOS:

```bash
swift test --package-path macos
bash tools/macos/build-app.sh 0.0.0
```

Run the site checks for docs, diagrams, or search changes:

```powershell
npm ci
npm run diagram:render:all
npm run pages:build
```

## Good Issue Reports

The most useful reports include:

- Windows or macOS version and Bluetooth hardware details
- Polar H10 firmware version if known
- Exact repro steps
- App or CLI command used
- Logs, screenshots, or exported session snippets when relevant

## Pull Request Notes

- Keep changes focused.
- Mention any docs, search, or diagram pages you verified.
- Call out behavior changes that affect saved output, protocol decoding, or
  operator workflow.
