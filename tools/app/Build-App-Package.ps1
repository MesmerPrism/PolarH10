<#
.SYNOPSIS
    Builds the PolarH10 MSIX package and optional App Installer file.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64')]
    [string]$Platform = 'x64',
    [string]$Version = '0.1.0.0',
    [string]$PackageId = 'MesmerPrism.PolarH10',
    [string]$Publisher = 'CN=MesmerPrism',
    [string]$DisplayName = 'PolarH10',
    [string]$PublisherDisplayName = 'Mesmer Prism',
    [string]$OutputRelativePath = 'artifacts\windows-installer',
    [string]$PackageFileName = 'PolarH10.msix',
    [string]$AppInstallerFileName = 'PolarH10.appinstaller',
    [string]$CertificateFileName = 'PolarH10.cer',
    [string]$AppInstallerUri,
    [string]$MainPackageUri,
    [string]$PackageCertificatePath,
    [string]$PackageCertificatePassword,
    [string]$PackageCertificateTimestampUrl,
    [switch]$Unsigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-MSBuild {
    $command = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $matches = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe'
        if ($LASTEXITCODE -eq 0 -and $matches) {
            return $matches | Select-Object -First 1
        }
    }

    throw 'MSBuild.exe was not found. Install Visual Studio Build Tools with the MSIX/Desktop Bridge workload, or run this in GitHub Actions on windows-latest.'
}

function Set-ManifestValue {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$Manifest,
        [Parameter(Mandatory = $true)]
        [string]$XPath,
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlNamespaceManager]$NamespaceManager
    )

    $node = $Manifest.SelectSingleNode($XPath, $NamespaceManager)
    if ($null -eq $node) {
        throw "Manifest node not found: $XPath"
    }

    $node.Value = $Value
}

if (([string]::IsNullOrWhiteSpace($AppInstallerUri)) -xor ([string]::IsNullOrWhiteSpace($MainPackageUri))) {
    throw 'AppInstallerUri and MainPackageUri must either both be provided or both be omitted.'
}

if (-not $Unsigned) {
    if ([string]::IsNullOrWhiteSpace($PackageCertificatePath) -or [string]::IsNullOrWhiteSpace($PackageCertificatePassword)) {
        throw 'Signed package builds require PackageCertificatePath and PackageCertificatePassword.'
    }

    $PackageCertificatePassword = $PackageCertificatePassword.TrimEnd("`r", "`n")
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$entryProjectPath = Join-Path $repoRoot 'src\PolarH10.App\PolarH10.App.csproj'
$packageProjectDir = Join-Path $repoRoot 'src\PolarH10.App.Package'
$packageProjectPath = Join-Path $packageProjectDir 'PolarH10.App.Package.wapproj'
$manifestPath = Join-Path $packageProjectDir 'Package.appxmanifest'
$appInstallerTemplatePath = Join-Path $packageProjectDir 'Package.appinstaller.template'
$appPackagesRoot = Join-Path $packageProjectDir 'AppPackages'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))

if (-not (Test-Path $packageProjectPath)) {
    throw "Packaging project not found at $packageProjectPath"
}

if (-not (Test-Path $entryProjectPath)) {
    throw "Entry project not found at $entryProjectPath"
}

if (-not (Test-Path $manifestPath)) {
    throw "Package manifest not found at $manifestPath"
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
if (Test-Path $appPackagesRoot) {
    Remove-Item -Recurse -Force $appPackagesRoot
}

$originalManifest = Get-Content $manifestPath -Raw

try {
    [xml]$manifest = $originalManifest
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
    $namespaceManager.AddNamespace('appx', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $namespaceManager.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')

    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Identity/@Name' -Value $PackageId -NamespaceManager $namespaceManager
    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Identity/@Publisher' -Value $Publisher -NamespaceManager $namespaceManager
    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Identity/@Version' -Value $Version -NamespaceManager $namespaceManager
    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Properties/appx:DisplayName/text()' -Value $DisplayName -NamespaceManager $namespaceManager
    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Properties/appx:PublisherDisplayName/text()' -Value $PublisherDisplayName -NamespaceManager $namespaceManager
    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Applications/appx:Application/uap:VisualElements/@DisplayName' -Value $DisplayName -NamespaceManager $namespaceManager
    $manifest.Save($manifestPath)

    Write-Host "Restoring PolarH10.App for win-$Platform runtime packs..." -ForegroundColor Cyan
    dotnet restore $entryProjectPath -r "win-$Platform" | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for $entryProjectPath with exit code $LASTEXITCODE"
    }

    $msbuildPath = Find-MSBuild
    $msbuildArgs = @(
        $packageProjectPath,
        '/restore',
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        '/p:UapAppxPackageBuildMode=SideLoadOnly',
        '/p:AppxBundle=Never',
        "/p:AppxPackageDir=$appPackagesRoot\",
        "/p:RuntimeIdentifier=win-$Platform",
        "/p:Version=$Version",
        "/p:AssemblyVersion=$Version",
        "/p:FileVersion=$Version",
        "/p:InformationalVersion=$Version",
        "/p:GenerateAppInstallerFile=False",
        "/p:AppxPackageSigningEnabled=$([bool](-not $Unsigned))"
    )

    if (-not $Unsigned) {
        $resolvedCertificatePath = [System.IO.Path]::GetFullPath($PackageCertificatePath)
        $msbuildArgs += "/p:PackageCertificateKeyFile=$resolvedCertificatePath"
        $msbuildArgs += "/p:PackageCertificatePassword=$PackageCertificatePassword"

        if (-not [string]::IsNullOrWhiteSpace($PackageCertificateTimestampUrl)) {
            $msbuildArgs += "/p:PackageCertificateTimestampUrl=$PackageCertificateTimestampUrl"
        }
    }

    Write-Host "Building MSIX package with $msbuildPath" -ForegroundColor Cyan
    & $msbuildPath @msbuildArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "MSIX package build failed with exit code $LASTEXITCODE"
    }

    $builtPackage = Get-ChildItem -Path $appPackagesRoot -Recurse -Filter *.msix |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $builtPackage) {
        throw "No MSIX package was produced under $appPackagesRoot"
    }

    $packageOutputPath = Join-Path $outputPath $PackageFileName
    Copy-Item $builtPackage.FullName $packageOutputPath -Force
    Write-Host "Copied package to $packageOutputPath" -ForegroundColor Green

    if (-not [string]::IsNullOrWhiteSpace($AppInstallerUri)) {
        $template = Get-Content $appInstallerTemplatePath -Raw
        $appInstaller = $template.
            Replace('{AppInstallerUri}', $AppInstallerUri).
            Replace('{Version}', $Version).
            Replace('{Name}', $PackageId).
            Replace('{Publisher}', $Publisher).
            Replace('{MainPackageUri}', $MainPackageUri)

        $appInstallerOutputPath = Join-Path $outputPath $AppInstallerFileName
        Set-Content -Path $appInstallerOutputPath -Value $appInstaller -Encoding utf8
        Write-Host "Wrote App Installer file to $appInstallerOutputPath" -ForegroundColor Green
    }

    if (-not $Unsigned -and -not [string]::IsNullOrWhiteSpace($CertificateFileName)) {
        $resolvedCertificatePath = [System.IO.Path]::GetFullPath($PackageCertificatePath)
        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $resolvedCertificatePath,
            $PackageCertificatePassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)

        try {
            $certificateBytes = $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
            $certificateOutputPath = Join-Path $outputPath $CertificateFileName
            [System.IO.File]::WriteAllBytes($certificateOutputPath, $certificateBytes)
            Write-Host "Exported public certificate to $certificateOutputPath" -ForegroundColor Green
        }
        finally {
            $certificate.Dispose()
        }
    }
}
finally {
    Set-Content -Path $manifestPath -Value $originalManifest -Encoding utf8
}
