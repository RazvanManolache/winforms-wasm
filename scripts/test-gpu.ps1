# Acceptance, not a unit-test total. The browser job supplies these inputs.
# Copy the bootstrap into trusted CI configuration for independent enforcement.
[CmdletBinding()]
param(
    [string]$Report = $env:WF_GPU_REPORT,
    [string]$ArtifactRoot = $env:WF_GPU_ARTIFACT_ROOT,
    [string]$Policy = $env:WF_GPU_POLICY,
    [string]$Pins = $env:WF_GPU_TEST_PINS,
    [string]$ExpectedBuild = $env:WF_GPU_BUILD_ID,
    [string]$Seed = $env:WF_GPU_TEST_SEED,
    [string]$ExpectedPinsSha256 = $env:WF_EXPECTED_TEST_PINS_SHA256,
    [string]$ExpectedPolicySha256 = $env:WF_EXPECTED_PERFORMANCE_POLICY_SHA256
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $repo 'tests'
try {
    if (-not $Report -or -not (Test-Path -LiteralPath $Report -PathType Leaf)) {
        throw 'Real browser GPU report is missing (WF_GPU_REPORT). Passing helper tests or software-rejection checks cannot satisfy GPU acceptance.'
    }
    foreach ($inputField in @(@('ArtifactRoot',$ArtifactRoot), @('Policy',$Policy), @('Pins',$Pins), @('ExpectedBuild',$ExpectedBuild))) {
        if ([string]::IsNullOrWhiteSpace($inputField[1])) { throw ('Missing browser job input: ' + $inputField[0]) }
    }
    [uint32]$trustedSeed = 0
    if (-not [uint32]::TryParse($Seed, [ref]$trustedSeed) -or $trustedSeed -eq 0) { throw 'A nonzero trusted browser job seed is required.' }
    function Assert-Hash([string]$Path, [string]$Expected) {
        if ($Expected -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
            throw "Trusted file hash mismatch: $Path"
        }
    }
    Assert-Hash $Pins $ExpectedPinsSha256
    Assert-Hash $Policy $ExpectedPolicySha256
    $manifest = Get-Content -LiteralPath $Pins -Raw | ConvertFrom-Json
    if ($manifest.version -ne 1 -or $manifest.files.Count -lt 5) { throw 'Invalid test pin manifest.' }
    $requiredCheckers = @('rendering-contracts/gpu-contracts.mjs','rendering-contracts/gpu-scenarios.mjs',
        'rendering-contracts/verify-gpu-report.mjs','rendering-contracts/verify-quality.mjs',
        'rendering-contracts/quality-evidence.mjs','rendering-contracts/gpu-contracts.test.mjs',
        'rendering-contracts/quality-evidence.test.mjs','rendering-contracts/gpu-receipt-integrity.test.mjs',
        'rendering-contracts/gpu-gate-wiring.test.mjs','rendering-contracts/synthetic-report-fixture.mjs',
        'rendering-contracts/binding-pixel-cases.mjs','rendering-contracts/verify-binding-report.mjs',
        'rendering-contracts/binding-report.test.mjs','rendering-contracts/binding-fixture.html',
        'rendering-contracts/serve-binding-contracts.mjs',
        'WfWasm.Drawing.Tests/DisplayGraphicsContractTests.cs','WfWasm.CanvasHost.Tests/PaintFailureReportingTests.cs')
    foreach ($checker in $requiredCheckers) {
        if (@($manifest.files | Where-Object path -eq $checker).Count -ne 1) { throw "Required checker is not pinned: $checker" }
    }
    $prefix = [IO.Path]::GetFullPath($testRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    foreach ($file in $manifest.files) {
        if ([IO.Path]::IsPathRooted($file.path) -or $file.path.Contains(':') -or $file.path.Contains('\') -or
            ($file.path.Split('/') -contains '..')) { throw 'Invalid pinned test path.' }
        $path = [IO.Path]::GetFullPath((Join-Path $testRoot $file.path))
        if (-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Pinned test path escapes tests.' }
        Assert-Hash $path $file.sha256
    }
    $oldSeed = $env:WF_GPU_TEST_SEED
    $oldPins = $env:WF_EXPECTED_TEST_PINS_SHA256
    $oldPolicy = $env:WF_EXPECTED_PERFORMANCE_POLICY_SHA256
    try {
        $env:WF_GPU_TEST_SEED = [string]$trustedSeed
        $env:WF_EXPECTED_TEST_PINS_SHA256 = $ExpectedPinsSha256
        $env:WF_EXPECTED_PERFORMANCE_POLICY_SHA256 = $ExpectedPolicySha256
        $checks = Join-Path $testRoot 'rendering-contracts'
        & node --test (Join-Path $checks 'gpu-contracts.test.mjs') (Join-Path $checks 'quality-evidence.test.mjs') (Join-Path $checks 'gpu-receipt-integrity.test.mjs') (Join-Path $checks 'gpu-gate-wiring.test.mjs')
        if ($LASTEXITCODE -ne 0) { throw 'GPU checker self-tests failed; no acceptance result.' }
        Write-Host 'Checker self-tests passed. Real GPU acceptance is still required.'
        & node (Join-Path $checks 'verify-quality.mjs') $Report $Policy $ExpectedBuild $ArtifactRoot $Pins $testRoot
        if ($LASTEXITCODE -ne 0) { throw 'Real GPU rendering/performance evidence failed acceptance.' }
    }
    finally {
        $env:WF_GPU_TEST_SEED = $oldSeed
        $env:WF_EXPECTED_TEST_PINS_SHA256 = $oldPins
        $env:WF_EXPECTED_PERFORMANCE_POLICY_SHA256 = $oldPolicy
    }
    & (Join-Path $PSScriptRoot 'test-gpu-binding.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Production WebGL binding acceptance failed.' }
    Write-Host 'GPU ACCEPTANCE PASSED: matching browser rendering, hardware, correctness and performance evidence, including binding pixels.'
    exit 0
}
catch {
    Write-Host ('GPU ACCEPTANCE FAILED: ' + $_.Exception.Message)
    exit 1
}
