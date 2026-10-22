param(
    [string[]]$Apps = @(),
    [string]$SourceRoot = '',
    [string]$OutputRoot = '',
    [string]$EvidenceRoot = '',
    [string]$MsBuildPath = '',
    [string]$SevenZipPath = '',
    [ValidateSet('', 'v4.8', 'v4.8.1')][string]$LegacyFramework = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$SourceRoot) { $SourceRoot = Join-Path $repoRoot 'external-apps/benchmark-sources' }
if (!$OutputRoot) { $OutputRoot = Join-Path $repoRoot '.artifacts/benchmark-apps' }
if (!$EvidenceRoot) { $EvidenceRoot = Join-Path $repoRoot '.artifacts/benchmark-evidence' }
$catalog = @(Get-Content -LiteralPath (Join-Path $repoRoot 'src/WfWasm.CanvasHost/DesktopBenchmarks.json') -Raw | ConvertFrom-Json)
foreach ($name in $Apps) {
    if (!($catalog | Where-Object { $_.slug -eq $name -or $_.sourceFolder -eq $name })) { throw "Unknown benchmark: $name" }
}
if ($Apps.Count) { $catalog = @($catalog | Where-Object { $Apps -contains $_.slug -or $Apps -contains $_.sourceFolder }) }
foreach ($directory in @($SourceRoot, $OutputRoot, $EvidenceRoot)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$results = @()
foreach ($benchmark in $catalog) {
    $source = Join-Path $SourceRoot $benchmark.sourceFolder
    $output = Join-Path $OutputRoot $benchmark.slug
    $entry = Join-Path $output $benchmark.entryFile
    $log = Join-Path $EvidenceRoot ($benchmark.slug + '-build.log')
    $code = 0
    $failure = $null
    try {
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        '{"prepared":false}' | Set-Content -LiteralPath (Join-Path $output 'preparation.json') -Encoding utf8
        if ($benchmark.build -eq 'manual') { throw "Application files and verified startup information are not prepared. $($benchmark.notes)" }
        if ($benchmark.build -in @('archive', 'installer-archive')) {
            $extension = if ($benchmark.build -eq 'archive') { '.zip' } else { '.exe' }
            $archive = Join-Path $SourceRoot ($benchmark.slug + '-' + $benchmark.revision + $extension)
            if (!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $benchmark.downloadUrl -UserAgent 'winforms-wasm-benchmark-preparation' -OutFile $archive }
            if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $benchmark.sha256) { throw 'Download SHA-256 does not match the pinned official package.' }
            if ($benchmark.build -eq 'installer-archive') {
                if (!$SevenZipPath) {
                    $command = Get-Command 7z -ErrorAction SilentlyContinue
                    if ($command) { $SevenZipPath = $command.Source }
                    elseif (Test-Path -LiteralPath "$env:ProgramFiles/7-Zip/7z.exe") { $SevenZipPath = "$env:ProgramFiles/7-Zip/7z.exe" }
                }
                if (!$SevenZipPath) { throw '7-Zip and Python 3 are required to extract this package as data.' }
                & python (Join-Path $PSScriptRoot 'vendor_archives.py') --kind $benchmark.archiveType --installer $archive --destination $output --sevenzip $SevenZipPath *> $log
                if ($LASTEXITCODE -ne 0) { throw 'Vendor container extraction failed; see the retained log. The installer was not executed.' }
            } else {
            # Validate archive paths before extracting a separately supplied package.
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = [IO.Compression.ZipFile]::OpenRead($archive)
            try {
                $boundary = [IO.Path]::GetFullPath($output).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
                foreach ($item in $zip.Entries) {
                    $target = [IO.Path]::GetFullPath((Join-Path $output $item.FullName))
                    if (!$target.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe path in benchmark archive.' }
                }
            } finally { $zip.Dispose() }
            Expand-Archive -LiteralPath $archive -DestinationPath $output -Force
            }
        } else {
            if (!(Test-Path -LiteralPath (Join-Path $source '.git'))) {
                if (Test-Path -LiteralPath $source) { throw 'Source folder exists and is not a Git checkout.' }
                & git clone --depth 1 --filter=blob:none --no-checkout ($benchmark.sourceUrl + '.git') $source *> $log
                if ($LASTEXITCODE -ne 0) { throw 'Could not fetch upstream source.' }
                & git -C $source fetch --depth 1 origin $benchmark.revision *>> $log
                if ($LASTEXITCODE -ne 0) { throw 'Could not fetch the pinned upstream revision.' }
                & git -C $source checkout --detach $benchmark.revision *>> $log
                if ($LASTEXITCODE -ne 0) { throw 'Could not check out the pinned upstream revision.' }
            }
            $origin = & git -C $source remote get-url origin
            if ($LASTEXITCODE -ne 0 -or ($origin -replace '\.git$', '') -ne ($benchmark.sourceUrl -replace '\.git$', '')) { throw 'Source checkout does not match the declared upstream.' }
            $revision = & git -C $source rev-parse HEAD
            if ($LASTEXITCODE -ne 0 -or $revision -ne $benchmark.revision) { throw 'Existing source is a different revision; use a fresh source folder. No checkout was reset.' }
            if (Test-Path -LiteralPath (Join-Path $source '.gitmodules')) {
                & git -C $source submodule update --init --recursive --depth 1 *>> $log
                if ($LASTEXITCODE -ne 0) { throw 'Could not initialize pinned upstream submodules.' }
            }
            $changed = & git -C $source diff --name-only HEAD
            if ($LASTEXITCODE -ne 0 -or $changed) { throw 'Upstream tracked source was modified; refusing to describe it as the pinned input.' }
            if ($benchmark.build -eq 'managed-binary') {
                $sourceEntry = if ($benchmark.sourceEntryFile) { $benchmark.sourceEntryFile } else { $benchmark.entryFile }
                Copy-Item -LiteralPath (Join-Path $source $sourceEntry) -Destination $entry
            } else {
                $project = Join-Path $source $benchmark.project
                if (!(Test-Path -LiteralPath $project)) { throw 'Declared upstream build project is missing.' }
                Push-Location $source
                try {
                    if ($benchmark.build -eq 'msbuild') {
                        if (!$MsBuildPath) {
                            $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
                            if (Test-Path -LiteralPath $vswhere) { $MsBuildPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1 }
                        }
                        if (!$MsBuildPath) { throw 'Visual Studio MSBuild and the upstream .NET Framework targeting packs are required.' }
                        $legacyArguments = @()
                        if ($LegacyFramework) {
                            $legacyArguments += "/p:TargetFrameworkVersion=$LegacyFramework", '/p:TargetFrameworkProfile='
                            if ($benchmark.sourceFolder -eq 'dockpanel') {
                                $tfm = 'net' + ($LegacyFramework.TrimStart('v').Replace('.', ''))
                                $legacyArguments += "/p:TargetFramework=$tfm", "/p:TargetFrameworks=$tfm"
                            }
                        }
                        & $MsBuildPath $project /restore /t:Build /p:RestorePackagesConfig=true /p:Configuration=Release "/p:SolutionDir=$source/" "/p:OutputPath=$output/" "/p:OutDir=$output/" /p:RunCodeAnalysis=false /p:TreatWarningsAsErrors=false /p:PostBuildEvent= /v:minimal /nologo @legacyArguments @($benchmark.buildArguments) *> $log
                    } else {
                        $extraArguments = @($benchmark.buildArguments)
                        if ($benchmark.buildOverlay) {
                            if ($benchmark.buildOverlay -ne [IO.Path]::GetFileName($benchmark.buildOverlay)) { throw 'Build overlay must name a bundled file.' }
                            $overlay = Join-Path $PSScriptRoot ('benchmark-overlays/' + $benchmark.buildOverlay)
                            if (!(Test-Path -LiteralPath $overlay)) { throw 'Declared benchmark build overlay is missing.' }
                            $extraArguments += "-p:DirectoryBuildTargetsPath=$overlay"
                        }
                        if ($benchmark.sourceFolder -eq 'gitextensions') {
                            $pluginRoot = Join-Path $output 'Plugins'
                            New-Item -ItemType Directory -Path $pluginRoot -Force | Out-Null
                            $extraArguments += "-p:ArtifactsPluginsDir=$pluginRoot"
                        }
                        & dotnet build $project --nologo -v:q -c Release -o $output -p:UseAppHost=false @extraArguments *> $log
                    }
                    $code = $LASTEXITCODE
                    if ($code -ne 0) { throw "Upstream build failed (exit $code). See the build log; no application files were fabricated." }
                } finally { Pop-Location }
            }
            foreach ($license in @('LICENSE', 'LICENSE.md', 'LICENSE.txt', 'License.txt', 'license.txt')) {
                if (Test-Path -LiteralPath (Join-Path $source $license)) { Copy-Item -LiteralPath (Join-Path $source $license) -Destination (Join-Path $output ('upstream-' + $license)) }
            }
        }
        if (!(Test-Path -LiteralPath $entry)) { throw "Build/package did not provide the declared managed entry: $($benchmark.entryFile)" }
        foreach ($required in @($benchmark.requiredFiles)) {
            if (!$required) { continue }
            if ($required -ne [IO.Path]::GetFileName($required) -or !(Test-Path -LiteralPath (Join-Path $output $required))) {
                throw "Prepared input is missing a required runtime dependency: $required"
            }
        }
    } catch {
        if ($code -eq 0) { $code = 1 }
        $failure = $_.Exception.Message
    }
    $results += [pscustomobject]@{
        slug=$benchmark.slug; source=$benchmark.sourceUrl; revision=$benchmark.revision
        exitCode=$code; prepared=($code -eq 0); entryExists=(Test-Path -LiteralPath $entry)
        browserCompatibility='unverified'; legacyFrameworkOverride=$LegacyFramework; failure=$failure; log=$log
    }
    $results[-1] | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'preparation.json') -Encoding utf8
    $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceRoot 'build-results.json') -Encoding utf8
    Write-Host "$($benchmark.label): $(if ($code -eq 0) { 'prepared; browser unverified' } else { $failure })"
}
Write-Host "Prepared root: $OutputRoot"
Write-Host 'If using a custom output folder, set WFWASM_BENCHMARK_APPS_ROOT to that folder before starting the server.'
if (@($results | Where-Object { !$_.prepared }).Count) { throw 'Some benchmarks need prerequisites or build fixes. See build-results.json. Prepared entries remain available.' }
