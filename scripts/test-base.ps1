$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $checks = @(
        @{ Project = 'tests/WfWasm.Drawing.Tests'; Filter = $null },
        @{ Project = 'tests/WfWasm.CanvasHost.Tests'; Filter = 'FullyQualifiedName~DemandAssemblyPayloadTests|FullyQualifiedName~WinFormsExecutionTests|FullyQualifiedName~AppAssemblyLoaderTests|FullyQualifiedName~SceneEntriesTests' },
        @{ Project = 'tests/WfWasm.Rewrite.Tests'; Filter = 'FullyQualifiedName~ConfigRedirectTests.Redirect_Synthetic|FullyQualifiedName~RewriteTests.Rewrite_Renames_Framework_References_To_Shim|FullyQualifiedName~RewriteTests.Rewritten_Fixture_Boots_Against_Shim|FullyQualifiedName~RewriteTests.Rewrite_Accepts_Assemblies_With_ILOnly_Cleared' }
    )
    foreach ($check in $checks) {
        $testArgs = @('test', $check.Project, '--nologo', '-v:q')
        if ($check.Filter) { $testArgs += @('--filter', $check.Filter) }
        dotnet @testArgs
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $($check.Project)" }
    }
}
finally { Pop-Location }
