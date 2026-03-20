<#
.SYNOPSIS
    Builds PolarH10.App into the canonical repo-local workspace output used by
    companion tooling and cross-repo launchers.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputRelativePath = 'out\workspace-app'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projectPath = Join-Path $repoRoot 'src\PolarH10.App\PolarH10.App.csproj'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
$exePath = Join-Path $outputPath 'PolarH10.App.exe'

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

Write-Host "Building PolarH10.App into $outputPath ($Configuration)..." -ForegroundColor Cyan
dotnet build $projectPath -c $Configuration -p:OutputPath="$outputPath\" | Out-Host

if (-not (Test-Path $exePath)) {
    throw "Built executable not found at $exePath"
}

Write-Host "Workspace build ready at $exePath" -ForegroundColor Green
Write-Output $exePath
