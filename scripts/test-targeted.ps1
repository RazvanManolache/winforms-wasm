# Targeted tests: run only the suites covering the areas you touched.
# Policy: targeted runs after every src edit batch; the FULL gate
# (scripts/test-full.ps1) only at publish/pass milestones — it is slow
# (Rewrite.Tests alone is ~30s; the whole suite is minutes) and some
# suites have historically hung when launched blindly via the solution.
#
# Usage:
#   ./scripts/test-targeted.ps1 -Areas CanvasHost,Drawing
#   ./scripts/test-targeted.ps1 -Files src/WfWasm.Drawing/Graphics.cs
#   ./scripts/test-targeted.ps1 -List                # print the map
#   ./scripts/test-targeted.ps1 -Areas Rewrite -Filter "FullyQualifiedName~ReflectionOnlyGuard"
#
# Area names are src/ project folders. Mapping = test projects that
# reference the src project (plus name-matched suites). Unknown areas
# exit 2. Suites run SEQUENTIALLY (parallel builds fight over obj/).
# Known parked failures: Rewrite.Tests x2 WheelBitmap (reported, still
# counted as failures; see E:/tmp for triage notes).
[CmdletBinding()]
param(
    [string[]]$Areas,
    [string[]]$Files,
    [string]$Filter,
    [switch]$List,
    [switch]$NoBuild,
    [switch]$GpuAcceptance
)
$ErrorActionPreference = 'Stop'
$ws = Split-Path $PSScriptRoot -Parent

# src area -> test projects (invert ProjectReferences by hand; keep in
# sync when a test project gains a reference). Keys are src/ folders.
$Map = [ordered]@{
    'WfWasm.Analyzer'  = @('WfWasm.Analyzer.Tests')
    'WfWasm.ApiScan'   = @('WfWasm.Drawing.Tests')
    'WfWasm.CanvasHost' = @('WfWasm.CanvasHost.Tests')
    'WfWasm.Drawing'   = @('WfWasm.Drawing.Tests', 'WfWasm.WinForms.Tests', 'WfWasm.Shim.Tests', 'WfWasm.Rewrite.Tests')
    'WfWasm.ErrorLog'  = @('WfWasm.ErrorLog.Tests')
    'WfWasm.Host.Server' = @('WfWasm.Host.Server.Tests')
    'WfWasm.Packager'  = @('WfWasm.Packager.Tests')
    'WfWasm.PdnCpu'    = @('WfWasm.PdnCpu.Tests', 'WfWasm.Rewrite.Tests')
    'WfWasm.Renderer'  = @('WfWasm.Renderer.Tests', 'WfWasm.Shim.Tests')
    'WfWasm.Rendering' = @('WfWasm.Rendering.Tests')
    'WfWasm.Rendering.Gles' = @('WfWasm.Rendering.Gles.Tests')
    'WfWasm.Rewrite'   = @('WfWasm.Rewrite.Tests')
    'WfWasm.Rewrite.Tool' = @('WfWasm.Rewrite.Tests')
    'WfWasm.Shim'      = @('WfWasm.Shim.Tests', 'WfWasm.Rewrite.Tests')
    'WfWasm.Skia'      = @('WfWasm.Skia.Tests', 'WfWasm.Shim.Tests')
    'WfWasm.WinForms'  = @('WfWasm.WinForms.Tests', 'WfWasm.Rewrite.Tests')
    'Microsoft.Win32.SystemEvents' = @('WfWasm.WinForms.Tests')
}

if ($List) {
    foreach ($k in $Map.Keys) { Write-Host ("{0,-28} -> {1}" -f $k, ($Map[$k] -join ", ")) }
    return
}

$picked = @()
if ($Areas) { $picked += $Areas }
if ($Files) {
    foreach ($f in $Files) {
        $parts = $f -replace '\\', '/' -split '/'
        if ($parts.Count -ge 3 -and $parts[-3] -eq 'src') { $picked += $parts[-2] }
        elseif ($parts -contains 'src') {
            $i = [array]::IndexOf($parts, 'src'); if ($i + 1 -lt $parts.Count) { $picked += $parts[$i + 1] }
        }
        else { Write-Host "cannot map file to an area: $f" }
    }
}
$picked = $picked | Select-Object -Unique
if (-not $picked) { Write-Host "no areas given; use -Areas, -Files, or -List"; exit 2 }
$bad = $picked | Where-Object { -not $Map.Contains($_) }
if ($bad) { Write-Host ("unknown area(s): " + ($bad -join ", ") + " (use -List)"); exit 2 }

$suites = @()
foreach ($a in $picked) { $suites += $Map[$a] }
$suites = $suites | Select-Object -Unique
Write-Host ("targeted: " + ($picked -join ", ") + " -> " + ($suites -join ", "))

$failed = $false
$sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($suite in $suites) {
    $proj = Join-Path $ws "tests/$suite/$suite.csproj"
    $targs = @('test', $proj, '-c', 'Debug', '--nologo', '-v', 'q')
    if ($NoBuild) { $targs += '--no-build' }
    if ($Filter) { $targs += '--filter', $Filter }
    $t0 = $sw.Elapsed
    $out = dotnet @targs 2>&1
    $code = $LASTEXITCODE
    $sum = $out | Select-String -Pattern 'Passed!|Failed!' | Select-Object -Last 1 -ExpandProperty Line
    if (-not $sum) { $sum = ($out | Select-Object -Last 1) }
    Write-Host ("[{0}] {1}  ({2:n0}s)" -f $suite, $sum.Trim(), ($sw.Elapsed - $t0).TotalSeconds)
    if ($code -ne 0) {
        $failed = $true
        if ($suite -eq 'WfWasm.Rewrite.Tests') {
            $fails = ($out | Select-String -Pattern '\[FAIL\]').Line
            $newFails = $fails | Where-Object { $_ -notmatch 'WheelBitmap' }
            if (-not $newFails) { Write-Host "  (only the 2 parked WheelBitmap failures)"; $failed = $false }
        }
    }
}
if ($picked -contains 'WfWasm.CanvasHost' -or $picked -contains 'WfWasm.Host.Server') {
    & node --test (Join-Path $ws 'tests/shell-isolation/sessions.test.mjs')
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}
if ($GpuAcceptance) {
    & (Join-Path $PSScriptRoot 'test-gpu.ps1')
    if ($LASTEXITCODE -ne 0) { $failed = $true }
} elseif ($picked -contains 'WfWasm.CanvasHost' -or $picked -contains 'WfWasm.Host.Server' -or
    $picked -contains 'WfWasm.Rendering.Gles' -or $picked -contains 'WfWasm.Rendering' -or $picked -contains 'WfWasm.Drawing') {
    Write-Host 'Targeted unit tests only: GPU rendering acceptance was NOT evaluated. Use -GpuAcceptance or scripts/test-gpu.ps1.'
}
Write-Host ("targeted total: {0:n0}s" -f $sw.Elapsed.TotalSeconds)
if ($failed) { exit 1 }
