$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..\..'))
$sourceRoot = Join-Path $repoRoot 'docs\formula-sheets-latex'
$outputRoot = Join-Path $repoRoot 'docs\assets\formula-sheets'
$buildRoot = Join-Path $repoRoot '.codex-build\formula-sheets'

if (-not (Test-Path $sourceRoot)) {
    throw "Formula sheet LaTeX source directory not found: $sourceRoot"
}

$latexmk = Get-Command latexmk -ErrorAction SilentlyContinue
$pdflatex = Get-Command pdflatex -ErrorAction SilentlyContinue
if (-not $latexmk -and -not $pdflatex) {
    throw 'Neither latexmk nor pdflatex was found on PATH. Install MiKTeX or TeX Live before rebuilding the formula sheets.'
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
if (Test-Path $buildRoot) {
    Remove-Item $buildRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null

$sources = Get-ChildItem $sourceRoot -Filter *.tex | Sort-Object Name
if ($sources.Count -eq 0) {
    throw "No .tex files found under $sourceRoot"
}

foreach ($source in $sources) {
    if ($latexmk) {
        & $latexmk.Source `
            -pdf `
            -interaction=nonstopmode `
            -halt-on-error `
            "-outdir=$buildRoot" `
            $source.FullName | Out-Null
    }
    else {
        & $pdflatex.Source `
            -interaction=nonstopmode `
            -halt-on-error `
            -output-directory $buildRoot `
            $source.FullName | Out-Null
    }

    $pdfPath = Join-Path $buildRoot ($source.BaseName + '.pdf')
    if (-not (Test-Path $pdfPath)) {
        throw "Expected PDF was not produced for $($source.Name)"
    }

    Copy-Item $pdfPath (Join-Path $outputRoot ($source.BaseName + '.pdf')) -Force
}

Write-Host "Rendered formula sheet PDFs to $outputRoot"
