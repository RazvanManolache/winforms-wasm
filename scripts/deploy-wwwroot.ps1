# Deploy wwwroot to the file the server actually serves, then byte-verify.
# Drift here is silent and user-visible (stale css/app.css shipped the old
# top bar for a day while src had the rail). Usage:
#   powershell -File scripts/deploy-wwwroot.ps1 [-ServerRoot <wwwroot>] [-VerifyOnly]
param(
    [string]$ServerRoot = "src/WfWasm.Host.Server/bin/Debug/net9.0/wwwroot",
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
$ws = Split-Path $PSScriptRoot -Parent
if (![IO.Path]::IsPathRooted($ServerRoot)) { $ServerRoot = Join-Path $ws $ServerRoot }
$src = Join-Path $ws 'src/WfWasm.CanvasHost/wwwroot'
$bin = Join-Path $ws 'src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot'

if (!(Test-Path $src)) { Write-Host "  MISSING src wwwroot: $src"; exit 1 }

if (!$VerifyOnly) {
    $fw = Join-Path $bin '_framework'
    if (Test-Path $fw) {
        robocopy $fw (Join-Path $ServerRoot '_framework') /MIR /NJH /NJS | Out-Null
    }
    # Statics come from src (css/js/eval/groundtruth/lib/...). No /MIR: the
    # server dir keeps build-generated extras (scoped styles css).
    robocopy $src $ServerRoot /E /NJH /NJS /XD _framework | Out-Null
    # Scoped-blazor css is generated at build time, not in src.
    Get-ChildItem $bin -Filter '*.styles.css' -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $ServerRoot $_.Name) -Force }
    # NB: host-bundle ReflectionOnly neutralize (paint MONO Fix 3) runs at
    # BUILD time (ensure-windowsbase-full.ps1 --refonly stage), not here:
    # the browser executes the WebCIL bundle (WindowsBase.*.wasm), which the
    # pipeline fingerprints from the staged DLL — post-hoc edits would break
    # boot-manifest integrity. check-bundle (below) gates the served bytes.
}

# Byte-verify: every src static must equal the served bytes.
$failed = $false
$srcPrefix = $src.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
Get-ChildItem $src -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($srcPrefix.Length)
    $dst = Join-Path $ServerRoot $rel
    if (!(Test-Path $dst)) { Write-Host "  MISSING served: $rel"; $failed = $true; return }
    $a = [IO.File]::ReadAllBytes($_.FullName)
    $b = [IO.File]::ReadAllBytes($dst)
    if ($a.Length -ne $b.Length) { Write-Host "  SIZE-DRIFT $rel src=$($a.Length) served=$($b.Length)"; $failed = $true; return }
    for ($i = 0; $i -lt $a.Length; $i += 65536) {
        $n = [Math]::Min(65536, $a.Length - $i)
        for ($j = 0; $j -lt $n; $j++) {
            if ($a[$i + $j] -ne $b[$i + $j]) { Write-Host "  BYTE-DRIFT $rel at $i"; $failed = $true; break }
        }
        if ($failed) { break }
    }
}
if ($failed) { exit 1 }
Write-Host "wwwroot in sync: $ServerRoot"
# Bundle gate wired into deploy (was manual-only): the SERVED _framework
# must satisfy the boot/bundle invariants (fingerprinted wasm present,
# full WindowsBase, stub wasm present).
& (Join-Path $PSScriptRoot 'check-bundle.ps1') -Root (Join-Path $ServerRoot '_framework')
# NB: a .ps1 call does not set $LASTEXITCODE (it still holds robocopy's 1 =
# "files copied"); $? is the real gate signal.
if (!$?) { exit 1 }
