param(
    [string]$SourceRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'external-apps/syncfusion-sources'),
    [string]$OutputRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'external-apps/syncfusion'),
    [string]$EvidenceRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) '.artifacts/syncfusion-evidence')
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$catalog = Get-Content -LiteralPath (Join-Path $repoRoot 'src/WfWasm.CanvasHost/SyncfusionDemos.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $SourceRoot '.git'))) {
    if (Test-Path -LiteralPath $SourceRoot) { throw 'SourceRoot already exists and is not a Git checkout.' }
    & git clone --depth 1 https://github.com/syncfusion/winforms-demos.git $SourceRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch Syncfusion source.' }
}
$origin = & git -C $SourceRoot remote get-url origin
if ($LASTEXITCODE -ne 0 -or $origin -notmatch '^https://github\.com/syncfusion/winforms-demos(?:\.git)?$') { throw 'SourceRoot is not the expected upstream checkout.' }
$revision = & git -C $SourceRoot rev-parse HEAD
$results = @()
foreach ($demo in $catalog) {
    $project = Join-Path $SourceRoot $demo.project
    $output = Join-Path $OutputRoot $demo.slug
    $log = Join-Path $EvidenceRoot ($demo.slug + '-build.log')
    if (!(Test-Path -LiteralPath $project)) { throw "Missing upstream sample: $($demo.project). Check sparse checkout configuration." }
    # Preserve upstream source/project/packages. Skip only its machine-specific installer HintPath target.
    # Telemetry and licensing implementation remain unmodified; no key is copied into artifacts.
    & dotnet build $project --nologo -v:q -o $output -p:ImportDirectoryBuildTargets=false -p:EnableUnsafeBinaryFormatterSerialization=true *> $log
    $code = $LASTEXITCODE
    $entry = Join-Path $output ($demo.assembly + '.dll')
    $results += [pscustomobject]@{ slug=$demo.slug; project=$demo.project; revision=$revision; exitCode=$code; entry=$entry; exists=(Test-Path -LiteralPath $entry); log=$log }
    Write-Host "$($demo.slug): $(if ($code -eq 0) {'built'} else {'FAILED'})"
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceRoot 'build-results.json')
if (@($results | Where-Object { $_.exitCode -ne 0 -or !$_.exists }).Count) { throw 'One or more Syncfusion demos did not build. See build-results.json and individual logs.' }
Write-Host 'Native demos prepared. Browser compatibility must be tested separately; vendor licensing still applies.'
