<#
.SYNOPSIS
    Regenerates the MSIX package logo assets from the canonical PolarH10 mark.
#>
[CmdletBinding()]
param(
    [string]$SourceRelativePath = 'docs\assets\polarh10-stripe-mark.png',
    [string]$OutputRelativePath = 'src\PolarH10.App.Package\Images'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$sourcePath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $SourceRelativePath))
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))

if (-not (Test-Path $sourcePath)) {
    throw "Source image not found at $sourcePath"
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$targets = @(
    @{ Name = 'StoreLogo.png'; Size = 50 },
    @{ Name = 'Square44x44Logo.png'; Size = 44 },
    @{ Name = 'Square150x150Logo.png'; Size = 150 }
)

$source = [System.Drawing.Image]::FromFile($sourcePath)

try {
    foreach ($target in $targets) {
        $size = [int]$target.Size
        $bitmap = New-Object System.Drawing.Bitmap($size, $size)
        $bitmap.SetResolution($source.HorizontalResolution, $source.VerticalResolution)

        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, 0, 0, $size, $size)
        }
        finally {
            $graphics.Dispose()
        }

        try {
            $targetPath = Join-Path $outputPath $target.Name
            $bitmap.Save($targetPath, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "Wrote $targetPath" -ForegroundColor Green
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}
