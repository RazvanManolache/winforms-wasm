# Bundle consistency gate: every fingerprinted .wasm the boot manifest
# references must exist on disk (a desync 404s the platform boot), AND the
# Paint.NET A1 invariant must hold: _framework/WindowsBase.*.wasm must be
# the FULL assembly (>=100KB, not the ~6KB browser-wasm pack facade), its
# blazor.boot.json integrity must match its recomputed sha256, and it must
# carry System.Windows.DependencyObject (else PDN bootstrap dies with
# TypeLoadException token 01000023: the bundled facade wins the default ALC
# and the real fetched copy can never displace it).
# Usage: check-bundle.ps1 [-Root src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot/_framework]
param([string]$Root = "src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot/_framework")
$ErrorActionPreference = 'Stop'
$ws = Split-Path $PSScriptRoot -Parent
if (![IO.Path]::IsPathRooted($Root)) { $Root = Join-Path $ws $Root }
$failed = $false

$bootPath = Join-Path $Root 'blazor.boot.json'
if (!(Test-Path $bootPath)) { Write-Host "  MISSING blazor.boot.json in $Root"; exit 1 }
$boot = Get-Content $bootPath -Raw
$names = [regex]::Matches($boot, '"([A-Za-z0-9_.-]+\.[a-z0-9]{8,}\.wasm)"') |
    ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
$missing = @($names | Where-Object { !(Test-Path (Join-Path $Root $_)) })
Write-Host "referenced=$($names.Count) missing=$($missing.Count)"
$missing | ForEach-Object { Write-Host "  MISSING $_"; $failed = $true }

$manifest = (Get-Content $bootPath -Raw) | ConvertFrom-Json
foreach ($wb in Get-ChildItem (Join-Path $Root 'WindowsBase.*.wasm') -ErrorAction SilentlyContinue) {
    if ($wb.Name -like '*.gz') { continue }
    $sizeOk = $wb.Length -ge 100KB
    Write-Host "WindowsBase: $($wb.Name) $($wb.Length) bytes"
    if (!$sizeOk) { Write-Host '  FAIL: facade-sized (<100KB) — full stub-transformed copy missing'; $failed = $true }

    $entry = $manifest.resources.assembly.PSObject.Properties[$wb.Name]
    if ($null -eq $entry) {
        Write-Host '  FAIL: no blazor.boot.json integrity entry'; $failed = $true
    }
    else {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = 'sha256-' + [Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($wb.FullName))) }
        finally { $sha.Dispose() }
        if ($actual -ne $entry.Value) {
            Write-Host "  FAIL: integrity mismatch manifest=$($entry.Value) actual=$actual"; $failed = $true
        }
        else { Write-Host '  integrity OK' }
    }

    $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($wb.FullName))
    if ($text -like '*DependencyObject*') { Write-Host '  DependencyObject present' }
    else { Write-Host '  FAIL: System.Windows.DependencyObject absent (TypeLoadException token 01000023)'; $failed = $true }
}

# System.Configuration.ConfigurationManager must be the FULL implementation
# (>=100KB): the fork's package reference does not flow through the plain
# rewritten-small <Reference/>, so without the CanvasHost package reference
# only the ~8KB facade ships and every settings touch TypeLoads (rev35).
$cfg = @(Get-ChildItem (Join-Path $Root 'System.Configuration.ConfigurationManager.*.wasm') -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike '*.gz' })
if ($cfg.Count -eq 0) { Write-Host '  FAIL: System.Configuration.ConfigurationManager missing from bundle'; $failed = $true }
foreach ($c in $cfg) {
    Write-Host "ConfigManager: $($c.Name) $($c.Length) bytes"
    if ($c.Length -lt 100KB) { Write-Host '  FAIL: facade-sized (<100KB) — full implementation missing'; $failed = $true }
}

if ($failed) { exit 1 }
Write-Host 'bundle gate PASS'
