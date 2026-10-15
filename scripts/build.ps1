param([switch]$Run, [int]$Port = 5210)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    foreach ($project in @('WfWasm.WinForms', 'WfWasm.Rewrite.Tool')) {
        dotnet build "src/$project/$project.csproj" -p:UseAppHost=false --nologo -v:q
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    $runtimeDir = Join-Path $repoRoot '.artifacts/runtime'
    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
    $forms = Join-Path $repoRoot 'src/WfWasm.WinForms/bin/Debug/net9.0/WfWasm.WinForms.dll'
    $drawing = Join-Path $repoRoot 'src/WfWasm.Drawing/bin/Debug/net9.0/WfWasm.Drawing.dll'
    $tool = Join-Path $repoRoot 'src/WfWasm.Rewrite.Tool/bin/Debug/net9.0/WfWasm.Rewrite.Tool.dll'
    dotnet $tool --app $forms --forms $forms --drawing $drawing --out (Join-Path $runtimeDir 'WfWasm.WinForms.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Runtime migration reported unresolved gaps.' }
    dotnet build src/WfWasm.Host.Server/WfWasm.Host.Server.csproj --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Browser host build failed.' }
    Write-Host "Build complete. Start with: ./scripts/build.ps1 -Run -Port $Port"
    if ($Run) {
        dotnet run --no-build --project src/WfWasm.Host.Server -- --urls "http://127.0.0.1:$Port"
        if ($LASTEXITCODE -ne 0) { throw 'Browser host stopped with an error.' }
    }
}
finally { Pop-Location }
