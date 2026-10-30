# Gallery contract tests: real-browser app workflows with independent oracles.
# One immutable generation per run (hashes + TransformRevision pinned before
# any case runs; the verifier rejects mismatched generations afterwards).
# Runs against the DEDICATED gallery server (:5213), never the main :5210.
#
# Usage:
#   ./scripts/test-gallery.ps1 [-Cases smoke,todo] [-Port 5213]
#   ./scripts/test-gallery.ps1 -Negative -Cases smoke
#   ./scripts/test-gallery.ps1 -ListCases
param(
    [string]$Cases = 'all',
    [int]$Port = 5213,
    [switch]$Negative,
    [switch]$ListCases
)
$ErrorActionPreference = 'Stop'
$ws = Split-Path $PSScriptRoot -Parent
$gal = Join-Path $ws 'tests\gallery-contracts'

if ($ListCases) {
    Write-Host 'smoke-click-keys todo-add-done barcode-qr-update mdi-file-menu failed-ctor-contained paint-stroke-undo'
    exit 0
}

Write-Host '== gallery preflight'
python -c "import playwright, PIL, cv2; print('deps ok')"
if ($LASTEXITCODE -ne 0) { throw 'missing python deps (playwright/PIL/cv2)' }
$cfg = curl.exe -s "http://127.0.0.1:$Port/api/preview-config"
if ($LASTEXITCODE -ne 0 -or !$cfg) { throw "gallery server :$Port unreachable" }
Write-Host "  server :$Port ok"

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = "E:/tmp/gal-$stamp"
Write-Host "== gallery run -> $out"
$negFlag = @()
if ($Negative) { $negFlag += '--negative' }
# Run the report even when cases fail: a failing report must still be
# verified (and must still fail acceptance). $runFailed gates the exit.
python (Join-Path $gal 'run.py') --out $out --cases $Cases --port $Port @negFlag
$runFailed = $LASTEXITCODE

Write-Host '== gallery verify'
# NOTE: --expect-cases is NOT auto-derived from -Cases here: -Cases names
# case-function stems while row ids differ ('smoke-click-keys', ...).
# Required-case enforcement lives in explicit verify invocations + selftests.
$verifyArgs = @('--run', $out)
if ($Negative) { $verifyArgs += '--expect-negative' }
python (Join-Path $gal 'verify.py') @verifyArgs
$verifyFailed = $LASTEXITCODE

Write-Host '== gallery checker self-tests'
python (Join-Path $gal 'selftest.py')
$selftestFailed = $LASTEXITCODE

$failed = ($runFailed -ne 0) -or ($verifyFailed -ne 0) -or ($selftestFailed -ne 0)
Write-Host "out=$out run=$runFailed verify=$verifyFailed selftest=$selftestFailed"
if ($failed) { throw 'gallery acceptance failed (see stage above)' }
exit 0
