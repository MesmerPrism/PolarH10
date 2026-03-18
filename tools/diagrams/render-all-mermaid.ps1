<#
.SYNOPSIS
    Renders all Mermaid diagrams listed in docs/diagrams/manifest.json to SVG.
.DESCRIPTION
    Reads the manifest, invokes @mermaid-js/mermaid-cli (mmdc) for each .mmd
    source, and writes .svg files alongside the sources.
    Requires: npm install (installs mmdc as a local dev dependency).
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot    = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
# If invoked from the Tools/diagrams folder, repoRoot may need adjustment.
# Prefer explicit resolution relative to this script.
$scriptDir   = $PSScriptRoot
# Script lives at Tools/diagrams/render-all-mermaid.ps1
# Repo root is two levels up from Tools/diagrams
$repoRoot    = Resolve-Path (Join-Path $scriptDir '..\..')
$diagramDir  = Join-Path $repoRoot 'docs\diagrams'
$manifestPath = Join-Path $diagramDir 'manifest.json'
$configPath  = Join-Path $diagramDir 'mermaid.config.json'
$puppeteerConfigPath = Join-Path $scriptDir 'puppeteer.config.json'

if (-not (Test-Path $manifestPath)) {
    Write-Error "Manifest not found at $manifestPath"
    return
}

$mmdc = Join-Path $repoRoot 'node_modules\.bin\mmdc.cmd'
if (-not (Test-Path $mmdc)) {
    $mmdc = Join-Path $repoRoot 'node_modules\.bin\mmdc'
}
if (-not (Test-Path $mmdc)) {
    Write-Error "mmdc not found. Run 'npm install' in the repo root first."
    return
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$total    = $manifest.diagrams.Count
$ok       = 0
$fail     = 0

Write-Host "Rendering $total diagram(s)..." -ForegroundColor Cyan

foreach ($entry in $manifest.diagrams) {
    $src = Join-Path $diagramDir $entry.source
    $out = [System.IO.Path]::ChangeExtension($src, '.svg')

    if (-not (Test-Path $src)) {
        Write-Warning "Source not found: $($entry.source) - skipping"
        $fail++
        continue
    }

    Write-Host "  $($entry.id): $($entry.source) -> $(Split-Path $out -Leaf)" -NoNewline

    $mmdcArgs = @('-i', $src, '-o', $out)
    if (Test-Path $configPath) {
        $mmdcArgs += @('-c', $configPath)
    }
    $runningOnLinux = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Linux
    )
    if ($runningOnLinux -and (Test-Path $puppeteerConfigPath)) {
        $mmdcArgs += @('-p', $puppeteerConfigPath)
    }
    $mmdcArgs += @('--quiet')

    try {
        & $mmdc @mmdcArgs
        if ($LASTEXITCODE -eq 0) {
            Write-Host " OK" -ForegroundColor Green
            $ok++
        } else {
            Write-Host " FAIL (exit $LASTEXITCODE)" -ForegroundColor Red
            $fail++
        }
    } catch {
        Write-Host " ERROR: $_" -ForegroundColor Red
        $fail++
    }
}

Write-Host ""
$color = if ($fail -eq 0) { 'Green' } else { 'Yellow' }
Write-Host "Done: $ok succeeded, $fail failed out of $total." -ForegroundColor $color

if ($fail -gt 0) { exit 1 }
