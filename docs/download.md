---
title: Download & Install
description: Install the PolarH10 research preview on Windows or macOS from the latest public release.
summary: Choose the guided Windows installer or the Universal macOS app, then complete the platform-specific first-launch trust step.
nav_label: Download & Install
nav_group: Start Here
nav_order: 20
full_width: true
---

# Download & Install

Choose your platform. Both desktop apps connect directly to the Polar H10 over
Bluetooth Low Energy and do not require the Polar SDK.

<div class="card-grid logo-sequence">
  <div class="path-card">
    <h3>macOS 13+</h3>
    <p>Universal SwiftUI app for Apple silicon and Intel Macs. Includes live HR, RR, ECG, ACC, rolling HRV/coherence, and session capture.</p>
    <div class="action-row">
      <a class="button primary" href="https://github.com/GeorgeFejer91/PolarH10/releases/latest/download/PolarH10-macOS-universal.zip">Download for Mac</a>
      <a class="button" href="platform-guides/macos.md">Mac guide</a>
    </div>
  </div>
  <div class="path-card">
    <h3>Windows 10+</h3>
    <p>Full WPF operator app with live telemetry, multi-device tracking, derived views, diagnostics, and session capture.</p>
    <div class="action-row">
      <a class="button primary" href="https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10-Preview-Setup.exe">Guided Windows setup</a>
      <a class="button" href="#windows-install">Windows options</a>
    </div>
  </div>
</div>

<div class="caption-card">
  <span class="caption-card-title">Research Preview</span>
  <p>These builds are intended for research and developer use. The Windows package uses a self-signed preview certificate. The Mac app is ad-hoc signed and is not yet notarized, so both platforms require an explicit first-launch trust decision.</p>
</div>

## Install on macOS

<div class="step-grid logo-sequence">
  <div class="step-card">
    <div class="step-no">01</div>
    <h3>Download and unzip</h3>
    <p>Download <code>PolarH10-macOS-universal.zip</code> and double-click it to reveal <code>PolarH10.app</code>.</p>
  </div>
  <div class="step-card">
    <div class="step-no">02</div>
    <h3>Move to Applications</h3>
    <p>Drag <code>PolarH10.app</code> into your Applications folder, then Control-click it and choose <strong>Open</strong>.</p>
  </div>
  <div class="step-card">
    <div class="step-no">03</div>
    <h3>Approve first launch</h3>
    <p>If macOS blocks the preview, open <strong>System Settings → Privacy &amp; Security</strong> and choose <strong>Open Anyway</strong> for PolarH10.</p>
  </div>
  <div class="step-card">
    <div class="step-no">04</div>
    <h3>Allow Bluetooth</h3>
    <p>Accept the Bluetooth prompt. Wear the H10, wet the electrodes, select <strong>Scan</strong>, then click the sensor to connect.</p>
  </div>
</div>

The Mac build runs natively on both Apple silicon and Intel. Recordings default
to `Documents/PolarH10/Sessions`. Read the full [macOS guide](platform-guides/macos.md)
for source builds, Gatekeeper recovery, permissions, and current feature scope.

## Windows install

The guided setup helper is the recommended Windows path. It prompts for admin
rights, trusts the published preview certificate, and opens App Installer.

<div class="action-row">
  <a class="button primary" href="https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10-Preview-Setup.exe">Guided setup (recommended)</a>
  <a class="button" href="ms-appinstaller:?source=https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.appinstaller">Install with App Installer</a>
  <a class="button" href="https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.cer">Download certificate</a>
  <a class="button" href="https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.appinstaller">Download appinstaller</a>
  <a class="button" href="https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.msix">Download MSIX</a>
  <a class="button" href="https://github.com/MesmerPrism/PolarH10/releases">Open releases</a>
</div>

### Windows fast path

1. Download and run `PolarH10-Preview-Setup.exe`.
2. Accept the administrator prompt.
3. Let the helper trust `PolarH10.cer` in `Local Machine > Trusted People`.
4. Complete the installation in App Installer and launch PolarH10 from Start.

The helper may still trigger Windows browser or SmartScreen friction because it
is not backed by a publicly trusted code-signing certificate.

### Windows manual fallback

Use this path if the guided setup helper is blocked by your environment.

1. Download [PolarH10.cer](https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.cer).
2. Open the certificate and choose `Install Certificate`.
3. Select `Local Machine`.
4. Choose `Place all certificates in the following store`.
5. Select `Trusted People` and finish the import.
6. Open the downloaded `PolarH10.appinstaller`.

This import requires local administrator rights. If an App Installer link does
not launch from the browser, download the `.appinstaller` file and open it after
importing the certificate.

## Requirements

| Platform | Operating system | Hardware | First-launch permission |
|---|---|---|---|
| macOS | macOS 13 Ventura or later; Apple silicon or Intel | Bluetooth LE adapter and Polar H10 | Gatekeeper approval for the preview and Bluetooth access |
| Windows | Windows 10 version 19041 or later | Bluetooth LE adapter and Polar H10 | Admin-approved preview certificate trust |

## Release files and updates

The release automation publishes these platform assets:

- macOS: `PolarH10-macOS-universal.zip` and its SHA-256 checksum
- Windows: `PolarH10-Preview-Setup.exe`, `PolarH10.msix`,
  `PolarH10.appinstaller`, `PolarH10.cer`, and checksums

The Windows App Installer channel checks for updates at launch. The Mac research
preview currently uses manual updates: download the latest ZIP and replace the
old app in Applications.

If a direct link returns `404`, that platform asset has not been attached to a
public release yet. Use the [Windows source-build guide](getting-started.md) or
the [Mac source-build guide](platform-guides/macos.md) until the next release.
