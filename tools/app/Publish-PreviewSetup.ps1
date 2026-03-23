<#
.SYNOPSIS
    Publishes the guided PolarH10 research preview setup bootstrapper.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Version = '0.1.0.0',
    [string]$OutputRelativePath = 'artifacts\windows-installer',
    [string]$FileName = 'PolarH10-Preview-Setup.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projectPath = Join-Path $repoRoot 'src\PolarH10.PreviewInstaller\PolarH10.PreviewInstaller.csproj'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
$publishPath = Join-Path $outputPath 'preview-setup-publish'

if (-not (Test-Path $projectPath)) {
    throw "Preview setup project not found at $projectPath"
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
if (Test-Path $publishPath) {
    Remove-Item -Recurse -Force $publishPath
}

$publishArgs = @(
    'publish',
    $projectPath,
    '--configuration', $Configuration,
    '--runtime', $RuntimeIdentifier,
    '/p:PublishSingleFile=true',
    '/p:SelfContained=true',
    '/p:PublishTrimmed=false',
    '/p:EnableCompressionInSingleFile=true',
    '/p:DebugType=None',
    '/p:DebugSymbols=false',
    "/p:Version=$Version",
    "/p:AssemblyVersion=$Version",
    "/p:FileVersion=$Version",
    "/p:InformationalVersion=$Version",
    '--output', $publishPath
)

Write-Host 'Publishing preview setup bootstrapper...' -ForegroundColor Cyan
dotnet @publishArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for $projectPath with exit code $LASTEXITCODE"
}

$publishedExe = Join-Path $publishPath 'PolarH10.PreviewInstaller.exe'
if (-not (Test-Path $publishedExe)) {
    throw "Published bootstrapper not found at $publishedExe"
}

$finalPath = Join-Path $outputPath $FileName
Copy-Item $publishedExe $finalPath -Force
Remove-Item -Recurse -Force $publishPath
Write-Host "Copied preview setup bootstrapper to $finalPath" -ForegroundColor Green
