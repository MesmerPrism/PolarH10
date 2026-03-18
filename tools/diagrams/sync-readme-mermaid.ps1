<#
.SYNOPSIS
    Synchronises Mermaid diagram blocks into README.md between marker comments.
.DESCRIPTION
    Scans README.md for pairs of marker comments like:
        <!-- MERMAID:BEGIN repo-structure -->
        <!-- MERMAID:END repo-structure -->
    and replaces the content between them with a ```mermaid fenced block
    containing the .mmd source from docs/diagrams/<id>.mmd.
    The <id> must match a diagram id in manifest.json.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir    = $PSScriptRoot
$repoRoot     = Resolve-Path (Join-Path $scriptDir '..\..')
$readmePath   = Join-Path $repoRoot 'README.md'
$diagramDir   = Join-Path $repoRoot 'docs\diagrams'
$manifestPath = Join-Path $diagramDir 'manifest.json'

if (-not (Test-Path $readmePath))   { Write-Error "README.md not found"; return }
if (-not (Test-Path $manifestPath)) { Write-Error "manifest.json not found"; return }

$manifest   = Get-Content $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
$sourceMap  = @{}
foreach ($entry in $manifest.diagrams) {
    $srcPath = Join-Path $diagramDir $entry.source
    if (Test-Path $srcPath) {
        $sourceMap[$entry.id] = (Get-Content $srcPath -Raw -Encoding utf8).TrimEnd()
    }
}

$readme  = Get-Content $readmePath -Raw -Encoding utf8
$changed = $false

# Match BEGIN/END marker pairs and replace content between them
$pattern = '(?ms)(<!-- MERMAID:BEGIN (\S+) -->)\r?\n.*?\r?\n(<!-- MERMAID:END \2 -->)'

$readme = [regex]::Replace($readme, $pattern, {
    param($m)
    $id = $m.Groups[2].Value
    if ($sourceMap.ContainsKey($id)) {
        $mmdContent = $sourceMap[$id]
        # Strip leading comment lines (lines starting with %%) for cleaner README display
        $lines = $mmdContent -split "`n"
        $filtered = $lines | Where-Object { $_ -notmatch '^\s*%%' }
        $clean = ($filtered -join "`n").TrimStart("`r`n").TrimStart("`n")
        $changed = $true
        @(
            $m.Groups[1].Value
            ''
            '```mermaid'
            $clean
            '```'
            ''
            $m.Groups[3].Value
        ) -join "`n"
    } else {
        Write-Warning "No source found for marker id '$id'"
        $m.Value
    }
})

if ($readme -ne (Get-Content $readmePath -Raw -Encoding utf8)) {
    # Write with UTF-8 no BOM
    [System.IO.File]::WriteAllText($readmePath, $readme, [System.Text.UTF8Encoding]::new($false))
    Write-Host "README.md updated with Mermaid blocks." -ForegroundColor Green
} else {
    Write-Host "README.md already up to date." -ForegroundColor Cyan
}
