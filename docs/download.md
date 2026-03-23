---
title: Download & Install
description: Install the PolarH10 Windows research preview from the latest public release, including the first-time certificate trust step required for the self-signed package.
summary: Download the self-signed research preview certificate, trust it once on the local machine, and then open the published App Installer file or raw MSIX package.
nav_label: Download & Install
nav_group: Start Here
nav_order: 20
full_width: true
---

# Download & Install

Use this page when you want the packaged Windows app instead of building the repo from source.

<div class="caption-card">
  <span class="caption-card-title">Research Preview</span>
  <p>This installer is meant for research and developer use. It is self-signed, not backed by a public CA, so the first install on each Windows machine needs one extra certificate-trust step before App Installer will accept the package.</p>
</div>

<div class="action-row">
  <a class="button primary" href="ms-appinstaller:?source=https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.appinstaller">Install with App Installer</a>
  <a class="button" href="https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.cer">Download certificate</a>
  <a class="button" href="https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.appinstaller">Download appinstaller</a>
  <a class="button" href="https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.msix">Download MSIX</a>
  <a class="button" href="https://github.com/MesmerPrism/PolarH10/releases">Open releases</a>
</div>

## Fast path

<div class="step-grid">
  <div class="step-card tone-cool">
    <div class="step-no">01</div>
    <h3>Download the certificate</h3>
    <p>Get <code>PolarH10.cer</code> from the latest release. You only need to trust it once per machine.</p>
  </div>
  <div class="step-card tone-violet">
    <div class="step-no">02</div>
    <h3>Trust it in Windows</h3>
    <p>Import the certificate into <code>Local Machine &gt; Trusted People</code>. This step requires local administrator rights.</p>
  </div>
  <div class="step-card tone-signal">
    <div class="step-no">03</div>
    <h3>Open the installer link</h3>
    <p>Use the <code>Install with App Installer</code> button, or open the downloaded <code>PolarH10.appinstaller</code> file manually.</p>
  </div>
  <div class="step-card tone-warm">
    <div class="step-no">04</div>
    <h3>Install and launch</h3>
    <p>Windows should now trust the package, install the app, and place it in the Start menu like a normal installed app.</p>
  </div>
</div>

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

## Common install problems

<div class="card-grid">
  <a class="path-card tone-cool" href="https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.appinstaller">
    <h3>Browser does not hand off to App Installer</h3>
    <p>Download <code>PolarH10.appinstaller</code> directly, then open it from the Downloads folder after the certificate import is complete.</p>
  </a>
  <a class="path-card tone-signal" href="https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.cer">
    <h3>Windows says the package is untrusted</h3>
    <p>Re-import <code>PolarH10.cer</code> into <code>Local Machine &gt; Trusted People</code>. Importing into the current-user store is usually not enough for this flow.</p>
  </a>
  <a class="path-card tone-violet" href="getting-started.md">
    <h3>No preview asset yet or the release link is missing</h3>
    <p>Use the source-build path from Getting Started on Windows until the next public preview package is published.</p>
  </a>
  <a class="path-card tone-warm" href="troubleshooting.md">
    <h3>The app installed but Bluetooth or device discovery still fails</h3>
    <p>The package trust step only affects install. For BLE, adapter, and H10 scan issues, move straight to the main troubleshooting guide.</p>
  </a>
</div>

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
