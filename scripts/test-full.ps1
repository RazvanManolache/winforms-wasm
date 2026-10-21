# Full test gate — deliberately sequential per-project (never
# `dotnet test WfWasm.sln` blindly: some suites have historically hung,
# and parallel project builds fight over obj/). Run at publish/pass
# milestones ONLY; day-to-day use scripts/test-targeted.ps1.
#
# Order: fast suites first, slow last, so a wedge surfaces late with
# most evidence already collected. Prints one summary line per suite
# plus a total. Known parked failures: Rewrite.Tests x2 WheelBitmap
# (reported separately; exit stays 1 while they fail, with the note).
[CmdletBinding()]
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$ws = Split-Path $PSScriptRoot -Parent

$Suites = @(
    'WfWasm.ErrorLog.Tests',
    'WfWasm.Host.Server.Tests',
    'WfWasm.Analyzer.Tests',
    'WfWasm.Packager.Tests',
    'WfWasm.Skia.Tests',
    'WfWasm.Renderer.Tests',
    'WfWasm.Rendering.Tests',
    'WfWasm.Rendering.Gles.Tests',
    'WfWasm.PdnCpu.Tests',
    'WfWasm.WinForms.Tests',
    'WfWasm.CanvasHost.Tests',
    'WfWasm.Shim.Tests',
    'WfWasm.Drawing.Tests',
    'WfWasm.Rewrite.Tests'
)

$failedSuites = @()
$sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($suite in $Suites) {
    $proj = Join-Path $ws "tests/$suite/$suite.csproj"
    $targs = @('test', $proj, '-c', 'Debug', '--nologo', '-v', 'q')
    if ($NoBuild) { $targs += '--no-build' }
    $t0 = $sw.Elapsed
    $out = dotnet @targs 2>&1
    $code = $LASTEXITCODE
    $sum = $out | Select-String -Pattern 'Passed!|Failed!' | Select-Object -Last 1 -ExpandProperty Line
    if (-not $sum) { $sum = ($out | Select-Object -Last 1) }
    Write-Host ("[{0}] {1}  ({2:n0}s)" -f $suite, $sum.Trim(), ($sw.Elapsed - $t0).TotalSeconds)
    if ($code -ne 0) {
        $failedSuites += $suite
    }
}
& node --test (Join-Path $ws 'tests/shell-isolation/sessions.test.mjs')
if ($LASTEXITCODE -ne 0) { $failedSuites += 'ShellIsolation' }
& (Join-Path $PSScriptRoot 'test-gpu.ps1')
if ($LASTEXITCODE -ne 0) { $failedSuites += 'GpuRenderingAcceptance' }
Write-Host ("FULL GATE total: {0:n0}s; failed suites: {1}" -f $sw.Elapsed.TotalSeconds,
    $(if ($failedSuites) { $failedSuites -join ", " } else { "none" }))
if ($failedSuites) { exit 1 }
