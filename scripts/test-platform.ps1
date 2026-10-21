param([string]$ResultsDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) '.artifacts/platform-results'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    & dotnet test tests/WfWasm.Platform.Tests --nologo -v:q --logger 'trx;LogFileName=platform.trx' --results-directory $ResultsDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Platform storage/HTTP tests failed.' }
    & node --test tests/platform-browser/platform.test.mjs tests/platform-browser/rpc.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Platform browser contracts failed.' }
    & dotnet build src/WfWasm.Platform.Demo --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Platform demo build failed.' }
} finally { Pop-Location }
