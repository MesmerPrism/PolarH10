---
title: Download & Install
description: Install the PolarH10 Windows research preview from the latest public release, including the first-time certificate trust step required for the self-signed package.
summary: Download the self-signed research preview certificate, trust it once on the local machine, and then open the published App Installer file or raw MSIX package.
nav_label: Download & Install
nav_group: Start Here
nav_order: 20
---

# Download & Install

Use this page when you want the packaged Windows app instead of building the repo from source.

The current public installer channel is a **Research Preview**. It is signed with the project's own self-signed certificate because this repo does not currently have a paid public CA certificate. That means the first install requires one extra Windows trust step.

## Install the latest research preview

- [Install PolarH10 for Windows](ms-appinstaller:?source=https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.appinstaller)
- [Download the preview certificate](https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.cer)
- [Download the App Installer file](https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.appinstaller)
- [Download the raw MSIX package](https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.msix)
- [Open the Releases page](https://github.com/MesmerPrism/PolarH10/releases)

## First-time trust step

Before the first preview install on a machine:

1. Download [PolarH10.cer](https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.cer).
2. Open the certificate file and choose `Install Certificate`.
3. Select `Local Machine`.
4. Choose `Place all certificates in the following store`.
5. Select `Trusted People`.
6. Finish the import, then open `PolarH10.appinstaller`.

This import path requires local administrator rights on the machine.

Windows App Installer checks the machine certificate store when deciding whether the package is trusted. Until the project has a certificate from a publicly trusted CA, this step is required for the preview installer.

If the `Install PolarH10 for Windows` link does not launch App Installer from the browser, download `PolarH10.appinstaller` and open it from your Downloads folder after importing `PolarH10.cer`.

If the direct download links return `404`, there is no public preview release yet. Use the source-build path in [Getting Started on Windows](getting-started.md) until the first preview release is published.

## Requirements

- Windows 10 version 19041 or later
- Bluetooth Low Energy (BLE) adapter
- Polar H10 chest strap

The packaged app keeps the Bluetooth package identity required for the Windows GATT path used by this repo. The preview certificate trust step is separate from the app itself and only affects whether Windows accepts the package as trusted.

## Default save location

Installed builds default new recordings to:

```text
Documents\PolarH10\Sessions
```

Repo-local developer builds still default to the repo `session\` folder.

## Update behavior

The release pipeline publishes `PolarH10.msix`, `PolarH10.appinstaller`, and `PolarH10.cer`. The App Installer file is configured to check for updates on launch and prompt before activation when a newer preview release is available.

## Source-build fallback

If you are validating the code, changing the app, or no preview release is available yet, use [Getting Started on Windows](getting-started.md) and run the repo build directly.
