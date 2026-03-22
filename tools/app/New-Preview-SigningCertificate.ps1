<#
.SYNOPSIS
    Creates a stable self-signed code-signing certificate for PolarH10 research preview releases.

.DESCRIPTION
    Exports a PFX, public CER, and Base64 text file suitable for the GitHub
    Actions secrets used by the release workflow.
#>
[CmdletBinding()]
param(
    [string]$Subject = 'CN=MesmerPrism',
    [string]$FriendlyName = 'PolarH10 Research Preview Signing',
    [int]$ValidYears = 3,
    [string]$OutputRelativePath = 'artifacts\preview-signing',
    [string]$PfxFileName = 'PolarH10-preview-signing.pfx',
    [string]$CerFileName = 'PolarH10.cer',
    [string]$Base64FileName = 'PolarH10-preview-signing.base64.txt',
    [SecureString]$Password
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $PSBoundParameters.ContainsKey('Password')) {
    $Password = Read-Host 'Enter a password for the exported PFX' -AsSecureString
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$notAfter = (Get-Date).Date.AddYears($ValidYears)
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -FriendlyName $FriendlyName `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -HashAlgorithm 'SHA256' `
    -KeyAlgorithm 'RSA' `
    -KeyLength 4096 `
    -KeyExportPolicy Exportable `
    -NotAfter $notAfter

$pfxPath = Join-Path $outputPath $PfxFileName
$cerPath = Join-Path $outputPath $CerFileName
$base64Path = Join-Path $outputPath $Base64FileName

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $Password | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

$pfxBytes = Get-Content -Path $pfxPath -Encoding Byte
[Convert]::ToBase64String($pfxBytes) | Set-Content -Path $base64Path -Encoding utf8

$plainPassword = [System.Net.NetworkCredential]::new('', $Password).Password

Write-Host "Created self-signed research preview certificate." -ForegroundColor Green
Write-Host "PFX : $pfxPath"
Write-Host "CER : $cerPath"
Write-Host "B64 : $base64Path"
Write-Host ''
Write-Host 'Configure these GitHub repository secrets:' -ForegroundColor Cyan
Write-Host "  WINDOWS_PACKAGE_CERTIFICATE_BASE64 = <contents of $base64Path>"
Write-Host "  WINDOWS_PACKAGE_CERTIFICATE_PASSWORD = <the password you just entered>"
Write-Host "  WINDOWS_PACKAGE_PUBLISHER = $Subject"
Write-Host ''
Write-Host "Password reminder: $plainPassword"
Write-Host ''
Write-Host 'The release workflow will publish the CER alongside the MSIX so users can trust it before installing the preview.'
