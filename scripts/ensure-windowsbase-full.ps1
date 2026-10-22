# Stages the FULL stub-transformed WindowsBase for the CanvasHost Blazor bundle.
#
# Background: RAR resolves the WindowsBase reference to the browser-wasm
# runtime-pack facade (~16KB ref -> ~6KB WebCIL in _framework). Mono loads
# that facade into the default ALC first, so the real copy fetched on demand
# can never displace it and Paint.NET bootstrap dies with TypeLoadException
# token 01000023 (System.Windows.DependencyObject is missing from the facade).
#
# This script reproduces the proven-good bytes (same passes as the batch
# stubpass.ps1 and AppTransform.StubbedCopy: --stub-pinvokes then
# --patch-probes over the pristine Paint.NET closure) into the project's
# intermediate dir, where the UseFullWindowsBase target in
# WfWasm.CanvasHost.csproj swaps it in before the bundle is computed — so
# WebCIL conversion, fingerprinting, blazor.boot.json integrity and .gz
# files are all produced by the pipeline itself, never hand-edited.
#
# Usage: ensure-windowsbase-full.ps1 [-IntermediateDir <obj/Debug/net9.0>]
param([string]$IntermediateDir = "obj/Debug/net9.0", [Parameter(Mandatory=$true)][string]$SourceDll)
$ErrorActionPreference = 'Stop'
$ws = Split-Path $PSScriptRoot -Parent
$IntermediateDir = $IntermediateDir.TrimEnd('\', '/', '"')
if (![IO.Path]::IsPathRooted($IntermediateDir)) { $IntermediateDir = Join-Path (Get-Location) $IntermediateDir }

$pristine = [IO.Path]::GetFullPath($SourceDll)
$pristineDir = Split-Path $pristine -Parent
if (!(Test-Path $pristine)) { throw "pristine WindowsBase not found: $pristine" }
$outDir = Join-Path $IntermediateDir 'full-windowsbase'
$outDll = Join-Path $outDir 'WindowsBase.dll'
$sidecar = $outDll + '.pristine.sha256'

$sha = [Security.Cryptography.SHA256]::Create()
try { $pristineHash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($pristine))).Replace('-', '') }
finally { $sha.Dispose() }
$stageRev = "$pristineHash|refonly1"
if ((Test-Path $outDll) -and (Test-Path $sidecar) -and ((Get-Content $sidecar -Raw).Trim() -eq $stageRev)) {
    Write-Host "WindowsBase full copy up to date ($outDll)"
    return
}

$toolProj = Join-Path $ws 'src/WfWasm.Rewrite.Tool/WfWasm.Rewrite.Tool.csproj'
Write-Host '== rebuild rewriter tool (stale binary silently skips new rules)'
dotnet build $toolProj --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'rewriter tool build failed' }
$tool = Join-Path $ws 'src/WfWasm.Rewrite.Tool/bin/Debug/net9.0/WfWasm.Rewrite.Tool.dll'

# Transient dir on the SAME volume as the pristine tree (hardlinks cannot
# cross volumes). Siblings are hardlinked read-only-style, mirroring
# AppTransform.StubbedCopy: the stub resolver searches the file's own
# directory, and the file itself is a byte copy (never a link — the passes
# overwrite it in place, which through a link would corrupt pristine).
$tmpDir = Join-Path (Join-Path $ws '.artifacts/tmp') ('stub-build-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null
    foreach ($sib in Get-ChildItem (Join-Path $pristineDir '*.dll')) {
        if ($sib.Name -eq 'WindowsBase.dll') { continue }
        try { New-Item -ItemType HardLink -Path (Join-Path $tmpDir $sib.Name) -Value $sib.FullName | Out-Null } catch { }
    }
    $tmp = Join-Path $tmpDir 'WindowsBase.dll'
    Copy-Item $pristine $tmp -Force
    dotnet $tool --stub-pinvokes $tmp $tmp
    if ($LASTEXITCODE -ne 0) { throw "stub-pinvokes failed for WindowsBase" }
    dotnet $tool --patch-probes $tmp $tmp
    if ($LASTEXITCODE -ne 0) { throw "patch-probes failed for WindowsBase" }
    dotnet $tool --refonly $tmp $tmp
    if ($LASTEXITCODE -ne 0) { throw "refonly failed for WindowsBase" }
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    Copy-Item $tmp $outDll -Force
    Set-Content $sidecar $stageRev -NoNewline
    $len = (Get-Item $outDll).Length
    Write-Host "WindowsBase full copy staged: $outDll ($len bytes)"
}
finally {
    $cleanupRoot = [IO.Path]::GetFullPath((Join-Path $ws '.artifacts/tmp')) + [IO.Path]::DirectorySeparatorChar
    $cleanupTarget = [IO.Path]::GetFullPath($tmpDir)
    if ($cleanupTarget.StartsWith($cleanupRoot, [StringComparison]::OrdinalIgnoreCase)) {
        try { Remove-Item -LiteralPath $cleanupTarget -Recurse -Force } catch { }
    }
}
