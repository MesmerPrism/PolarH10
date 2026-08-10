---
title: Platform Guides
description: Windows and macOS BLE transport notes, access pitfalls, source builds, and application-control workarounds.
summary: Choose the native macOS/CoreBluetooth guide or the Windows WinRT notes when transport behavior, permissions, or app launch needs platform-specific help.
nav_label: Platform Guides
nav_group: Platform Guides
nav_order: 30
---

# Platform Transport Guides

This project separates platform-specific BLE transport code from the protocol logic.
The `PolarH10.Transport.Abstractions` library defines interfaces; platform-specific
projects implement them.

## Available Transports

| Transport                     | Platform              | BLE Stack                    |
|-------------------------------|-----------------------|------------------------------|
| `PolarH10.Transport.Windows`  | Windows 10+           | WinRT (Windows.Devices.Bluetooth) |
| `PolarH10Mac`                 | macOS 13+             | Apple CoreBluetooth          |

## macOS

Use [Getting Started on macOS](macos.md) for the Universal download, first-launch
trust and Bluetooth permissions, source builds, CoreBluetooth identifiers, and
the current native Mac feature scope.

The Mac client is a native Swift package. It follows the same GATT/PMD protocol
and output schema, while its CoreBluetooth lifecycle stays in the `macos/`
target instead of implementing the .NET transport abstractions.

## Writing a New .NET Transport

To add another platform to the .NET CLI/runtime path (for example Linux/BlueZ or Android):

1. Create a new project targeting the appropriate TFM.
2. Reference `PolarH10.Transport.Abstractions`.
3. Implement the following interfaces:
   - `IBleScanner` — discover devices via advertisements
   - `IBleConnection` — connect, negotiate MTU, discover services
   - `IGattServiceHandle` — enumerate characteristics within a service
   - `IGattCharacteristicHandle` — read, write, and subscribe to notifications
   - `IBleAdapterFactory` — factory to create scanner and connection instances

### IBleScanner

- Raise `DeviceFound` event with `BleDeviceFound` records (address, name, RSSI).
- Implement `StartScanAsync` and `StopScan`.

### IBleConnection

- Implement `IAsyncDisposable`.
- `ConnectAsync` should establish the connection and discover primary services.
- `RequestMtuAsync` should negotiate the ATT MTU size.
- Raise `StateChanged` events on connect/disconnect.

### IGattCharacteristicHandle

- `EnableNotificationsAsync` must write the CCCD descriptor (`0x2902`) with value
  `0x01 0x00` (notifications) or `0x02 0x00` (indications).
- `WriteAsync` sends commands (e.g., PMD start/stop).
- The `ValueChanged` event delivers incoming notification payloads.

## Windows Transport Details

The Windows transport uses:

- `BluetoothLEAdvertisementWatcher` for scanning (active mode)
- `BluetoothLEDevice.FromBluetoothAddressAsync` for connecting
- `GattSession` for MTU negotiation (`MaxPduSize`)
- `GattCharacteristic.WriteValueWithResultAsync` for reliable writes
- `DataReader` for reading notification payloads

## Windows BLE Notes

- On this Windows 11 machine, direct WinRT characteristic discovery can return
  `AccessDenied` even when the service lookup succeeds.
- The durable fix in the Windows transport is to call
  `GattDeviceService.RequestAccessAsync()` before characteristic discovery and
  retry short-lived `AccessDenied` or `Unreachable` results.
- In this repo, that access-request and retry pattern lives in
  `src/PolarH10.Transport.Windows/WindowsGattServiceHandle.cs`.

## Windows Application-Control Note

- Local `dotnet test` and normal multi-file local .NET runs can still be
  blocked by Windows application-control policy on this machine.
- The canonical repo-local operator build is:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Build-Workspace-App.ps1
.\out\workspace-app\PolarH10.App.exe
```

- For manual operator validation, a practical workaround is a single-file
  publish if Windows still blocks that multi-file workspace build:

```powershell
dotnet publish src/PolarH10.App/PolarH10.App.csproj `
  -c Release `
  -r win-x64 `
  -p:PublishSingleFile=true `
  -p:SelfContained=false `
  -o out/app-single
```
