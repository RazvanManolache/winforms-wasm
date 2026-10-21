[CmdletBinding()]
param(
    [string]$Report = $env:WF_GPU_BINDING_REPORT,
    [string]$RunId = $env:WF_GPU_BINDING_RUN_ID
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
try {
    & node --test (Join-Path $repo 'tests/rendering-contracts/binding-report.test.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'Binding checker self-tests failed.' }
    if (!$Report -or !(Test-Path -LiteralPath $Report -PathType Leaf)) { throw 'Real browser binding report is missing (WF_GPU_BINDING_REPORT).' }
    if ([string]::IsNullOrWhiteSpace($RunId)) { throw 'Trusted binding run identity is missing (WF_GPU_BINDING_RUN_ID).' }
    & node (Join-Path $repo 'tests/rendering-contracts/verify-binding-report.mjs') $Report $RunId
    if ($LASTEXITCODE -ne 0) { throw 'Actual production WebGL binding pixel contracts failed.' }
    exit 0
}
catch {
    Write-Host ('GPU BINDING ACCEPTANCE FAILED: ' + $_.Exception.Message)
    exit 1
}
