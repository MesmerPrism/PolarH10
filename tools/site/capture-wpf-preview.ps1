<#
.SYNOPSIS
    Generates the WPF preview screenshot used by the Pages documentation.
#>
[CmdletBinding()]
param(
    [string]$OutputPath = 'docs/assets/brutal-tdr-preview.png',
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$buildScriptPath = Join-Path $repoRoot 'tools\app\Build-Workspace-App.ps1'
$workspaceOutputPath = 'out\workspace-app'
$exePath = Join-Path $repoRoot "$workspaceOutputPath\PolarH10.App.exe"
$resolvedOutputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))

if (-not (Test-Path $buildScriptPath)) {
    throw "Workspace build script not found at $buildScriptPath"
}

Write-Host "Building PolarH10.App workspace build ($Configuration)..." -ForegroundColor Cyan
& $buildScriptPath -Configuration $Configuration -OutputRelativePath $workspaceOutputPath | Out-Host

if (-not (Test-Path $exePath)) {
    throw "Built executable not found at $exePath"
}

$env:POLARH10_PREVIEW = '1'
$env:POLARH10_CAPTURE_PATH = $resolvedOutputPath

try {
    Write-Host "Capturing preview to $resolvedOutputPath" -ForegroundColor Cyan
    $process = Start-Process -FilePath $exePath -PassThru
    $process.WaitForExit()

    if ($process.ExitCode -ne 0) {
        throw "Preview capture exited with code $($process.ExitCode)"
    }

    if (-not (Test-Path $resolvedOutputPath)) {
        throw "Preview capture did not produce $resolvedOutputPath"
    }

    Write-Host "Preview capture complete." -ForegroundColor Green
}
finally {
    Remove-Item Env:\POLARH10_PREVIEW -ErrorAction SilentlyContinue
    Remove-Item Env:\POLARH10_CAPTURE_PATH -ErrorAction SilentlyContinue
}
