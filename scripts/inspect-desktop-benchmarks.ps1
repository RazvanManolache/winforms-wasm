param(
    [string[]]$Apps = @(),
    [string]$InputRoot = '',
    [string]$OutputPath = '',
    [string]$CecilPath = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$InputRoot) { $InputRoot = Join-Path $repoRoot '.artifacts/benchmark-apps' }
if (!$OutputPath) { $OutputPath = Join-Path $repoRoot '.artifacts/benchmark-evidence/native-preflight.json' }
if (!$CecilPath) {
    $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $CecilPath = Join-Path $packageRoot 'mono.cecil/0.11.6/lib/netstandard2.0/Mono.Cecil.dll'
}
if (!(Test-Path -LiteralPath $CecilPath)) { throw 'Restore/build WfWasm.Rewrite first, or supply -CecilPath for Mono.Cecil 0.11.6.' }
Add-Type -Path $CecilPath
$catalog = @(Get-Content -LiteralPath (Join-Path $repoRoot 'src/WfWasm.CanvasHost/DesktopBenchmarks.json') -Raw | ConvertFrom-Json)
foreach ($name in $Apps) {
    if (!($catalog | Where-Object { $_.slug -eq $name -or $_.sourceFolder -eq $name })) { throw "Unknown benchmark: $name" }
}
if ($Apps.Count) { $catalog = @($catalog | Where-Object { $Apps -contains $_.slug -or $Apps -contains $_.sourceFolder }) }
$reports = foreach ($benchmark in $catalog) {
    $folder = Join-Path $InputRoot $benchmark.slug
    if ($benchmark.build -eq 'manual') {
        [pscustomobject]@{ app=$benchmark.slug; status='manual-input-pending'; notes=$benchmark.notes }
        continue
    }
    $entry = Join-Path $folder $benchmark.entryFile
    if (!(Test-Path -LiteralPath $entry)) {
        [pscustomobject]@{ app=$benchmark.slug; status='missing-entry'; notes=$benchmark.notes }
        continue
    }
    $assemblies = @()
    $nativeFiles = @()
    $errors = @()
    $form = $null
    $entryIdentity = $null
    # This is static metadata inspection, not an assembly download/preload list.
    foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object { $_.Extension -in @('.exe', '.dll') }) {
        $relative = [IO.Path]::GetRelativePath($folder, $file.FullName)
        $module = $null
        try {
            # Cecil reads metadata without Assembly.Load, constructors or entry-point execution.
            $module = [Mono.Cecil.ModuleDefinition]::ReadModule($file.FullName)
            if ($file.FullName -eq [IO.Path]::GetFullPath($entry)) { $entryIdentity = $module.Assembly.Name.Name }
            $imports = @()
            foreach ($type in $module.GetTypes()) {
                if ($module.Assembly.Name.Name -eq $benchmark.formAssembly -and $type.FullName -eq $benchmark.formType) {
                    $form = [pscustomobject]@{
                        type=$type.FullName; baseType=$type.BaseType.FullName
                        constructors=@($type.Methods | Where-Object { $_.IsConstructor -and !$_.IsStatic } | ForEach-Object { $_.FullName })
                        publicParameterless=[bool]($type.Methods | Where-Object { $_.IsConstructor -and $_.IsPublic -and !$_.IsStatic -and $_.Parameters.Count -eq 0 })
                    }
                }
                foreach ($method in $type.Methods) {
                    if ($method.IsPInvokeImpl -and $method.PInvokeInfo) {
                        $imports += [pscustomobject]@{ method=$method.FullName; library=$method.PInvokeInfo.Module.Name; entryPoint=$method.PInvokeInfo.EntryPoint }
                    }
                }
            }
            $assemblies += [pscustomobject]@{
                file=$relative; identity=$module.Assembly.Name.FullName
                references=@($module.AssemblyReferences | ForEach-Object { $_.FullName })
                nativeImports=$imports
            }
        } catch {
            if ($_.Exception.ToString() -match 'BadImageFormatException') { $nativeFiles += $relative }
            else { $errors += [pscustomobject]@{file=$relative; error=$_.Exception.Message} }
        } finally { if ($module) { $module.Dispose() } }
    }
    [pscustomobject]@{
        app=$benchmark.slug; status=$(if ($errors.Count) { 'inspection-incomplete' } elseif (!$form -or $entryIdentity -ne $benchmark.assembly) { 'startup-metadata-mismatch' } else { 'metadata-inspected' })
        revision=$benchmark.revision; entrySha256=(Get-FileHash -LiteralPath $entry -Algorithm SHA256).Hash.ToLowerInvariant()
        entryIdentity=$entryIdentity; expectedEntryIdentity=$benchmark.assembly; identityMatches=($entryIdentity -eq $benchmark.assembly)
        declaredForm=$form; assemblies=$assemblies; nativeOrNonManagedFiles=$nativeFiles; errors=$errors
        compatibility='unverified'
        limitations='Imports are an inventory, not reachability or compatibility proof. COM, dynamic loading, process launching and native calls in missing dependencies may be absent. A native file classification can also include an invalid binary. No application code was executed.'
    }
}
$parent = Split-Path ([IO.Path]::GetFullPath($OutputPath)) -Parent
New-Item -ItemType Directory -Path $parent -Force | Out-Null
ConvertTo-Json -InputObject @($reports) -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Static preflight written to $OutputPath"
$reports | Select-Object app,status,identityMatches,@{Name='formFound';Expression={ $null -ne $_.declaredForm }} | Format-Table -AutoSize
if (@($reports | Where-Object { $_.status -in @('inspection-incomplete', 'startup-metadata-mismatch') }).Count) {
    throw 'Static preflight found invalid startup metadata or incomplete inspection. See the retained JSON report.'
}
