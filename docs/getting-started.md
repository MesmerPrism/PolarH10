# Getting Started on Windows

## Prerequisites

- Windows 10 version 1903 (build 19041) or later
- .NET 8.0 SDK
- A Polar H10 chest strap
- Bluetooth Low Energy (BLE) adapter

## Build from source

```powershell
git clone https://github.com/<your-org>/polar-h10-direct.git
cd polar-h10-direct
dotnet build
```

## Quick start with the CLI

```powershell
# Scan for nearby Polar H10 devices
dotnet run --project src/PolarH10.Cli -- scan

# Monitor a connected device
dotnet run --project src/PolarH10.Cli -- monitor --device <ADDRESS>

# Record a session to disk
dotnet run --project src/PolarH10.Cli -- record --device <ADDRESS> --out ./my-session
```

## Quick start with the GUI

```powershell
dotnet run --project src/PolarH10.App
```

The GUI provides:
1. **Device rail** — scan nearby straps, assign aliases, and choose the current control target
2. **Live** tab — real-time HR, RR, ECG, and ACC with a tracked-device dropdown for parallel charting
3. **Overlay** tab — dense single-device view for the currently selected strap
4. **Record** tab — save sessions to CSV
5. **Diagnostics** tab — raw PMD control messages for debugging

If you are working with more than one strap:

1. scan until both Polar H10 units appear in the device rail
2. connect each device from the left-hand control flow
3. open `Tracked devices` on the Live tab
4. leave `Follow selected device` enabled for normal one-device work, or disable it and tick multiple straps for parallel research tracking

## Package identity for Bluetooth

Windows GATT access requires Bluetooth capabilities declared in a package manifest.
For development, you can run the apps directly. For distribution, you should package
with MSIX or use packaging with external location to provide the required identity.

See Microsoft's documentation on
[specifying device capabilities for Bluetooth](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/how-to-specify-device-capabilities-for-bluetooth)
and [calling WinRT APIs from desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-enhance).
