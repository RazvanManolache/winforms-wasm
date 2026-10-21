# Building winforms-wasm

## Requirements

- Windows and PowerShell.
- .NET 9 SDK and its browser WebAssembly workload. Package restore needs internet access on the first build.
- A current browser. The browser runtime has been exercised in Chromium; other browsers need their own validation.

The vendored WinForms sources are already present. You do not need the Windows Forms upstream build infrastructure, a commercial control suite, or a prebuilt application.

## Build

Run from the repository root:

```powershell
dotnet workload restore src/WfWasm.CanvasHost/WfWasm.CanvasHost.csproj
./scripts/build.ps1
```

The order matters. First build the fork and drawing layer, then migrate the fork's drawing references with the rewriter, and finally compile the browser host against that migrated runtime. `Directory.Build.props` points the host and host tests to `.artifacts/runtime/WfWasm.WinForms.dll`.

Equivalent individual steps:

```powershell
dotnet build src/WfWasm.WinForms -p:UseAppHost=false
dotnet build src/WfWasm.Rewrite.Tool -p:UseAppHost=false
New-Item -ItemType Directory .artifacts/runtime -Force
dotnet src/WfWasm.Rewrite.Tool/bin/Debug/net9.0/WfWasm.Rewrite.Tool.dll `
  --app src/WfWasm.WinForms/bin/Debug/net9.0/WfWasm.WinForms.dll `
  --forms src/WfWasm.WinForms/bin/Debug/net9.0/WfWasm.WinForms.dll `
  --drawing src/WfWasm.Drawing/bin/Debug/net9.0/WfWasm.Drawing.dll `
  --out .artifacts/runtime/WfWasm.WinForms.dll
dotnet build src/WfWasm.Host.Server
```

The rewriter must complete without unresolved gaps. A generated output file alone does not establish a successful migration. Build warnings currently remain in the compatibility sources.

## Run

```powershell
dotnet run --no-build --project src/WfWasm.Host.Server -- --urls http://127.0.0.1:5210
```

Open the address in your browser. The smoke form is part of the base project. Its controls exercise the runtime without downloading another application's files.

To use another port:

```powershell
./scripts/build.ps1 -Run -Port 5211
```

## Tests

After the build, focused suites that do not require a third-party application can be run with:

```powershell
./scripts/test-base.ps1
```

This runs the drawing suite, focused host tests, and selected rewriter tests using synthetic fixtures. Some other integration tests refer to external applications, browser automation, or the original development corpus. The complete solution test run is not currently a clean-checkout acceptance gate; it has known failures and hangs. Use a bounded test run and review the browser console and `/errors` alongside the visible result.

## Locally supplied applications

Applications and converted assemblies are intentionally absent from the source distribution. Obtain any app and its dependencies separately under their own terms. Keep binaries and assets in ignored local folders.

The current catalog is configured in `src/WfWasm.Host.Server/AppTransform.cs`; this is not yet a general upload UI. The calculator entry supports a convenient local directory override:

```powershell
$env:WFWASM_CALCULATOR_DIR = 'D:/LocalApps/Calculator'
dotnet run --no-build --project src/WfWasm.Host.Server -- --urls http://127.0.0.1:5210
```

That directory must contain the separately built `Calculator.dll` expected by the existing adapter. This override changes where the server reads files; it does not build, download, or convert an app into the repository. Restart the server after changing its configuration. Reload the browser to refresh the available app list.

For other applications, add or adapt an `AppSpec`: entry assembly, source folder, optional assembly search roots, and any loose content mappings. The existing scene host also selects known form types, so adding an arbitrary new application currently requires host integration. Similar framework references do not guarantee compatibility.

The manifest requests the entry assembly only. Further DLLs are resolved by their runtime assembly names. Search roots are lookup locations, not a list of files to preload. Rewriting and its cache happen on demand.

Specialized compatibility experiments may require a full locally supplied `WindowsBase.dll`. They are optional and are disabled in the base build. When needed, set the MSBuild property `WfWasmWindowsBaseSource` to its absolute path; the existing staging script transforms a temporary copy. The project does not redistribute that input or claim all its APIs work in a browser.

## Generated files and cache

`bin/`, `obj/`, and `.artifacts/` are ignored. The default transformation cache is `.artifacts/transform-cache`; set `WFWASM_CACHE` to put it elsewhere. `WFWASM_ROOT` can point the server at the repository when running from an unusual deployment directory.

Re-run the build script after changing the fork, drawing layer, or rewriter. Those inputs participate in cache invalidation. Browser HTTP caching and in-session reuse are separate; a warm return is not a cold-start measurement.

## New test and platform sources

See [testing](testing.md) for the rendering, shell, platform and gallery suites and their required inputs. The separate platform demo can be started with a private writable data directory outside any web root:

```powershell
dotnet run --project src/WfWasm.Platform.Demo -- --Platform:DataDirectory C:/LocalData/wfwasm-platform --urls http://127.0.0.1:5384
```

That demo is scaffolding for file/registry/print integration; it does not mean existing apps already use every service. Keep it local. Node is needed for module checks, and the optional browser-gallery runner needs separately installed Python browser/image dependencies.

## Optional Syncfusion corpus

`scripts/prepare-syncfusion.ps1` reads the declared catalog, obtains the upstream sample repository and builds selected samples into ignored `external-apps/syncfusion`. Its default source checkout is `external-apps/syncfusion-sources`; evidence goes to `.artifacts/syncfusion-evidence`. Supply the vendor prerequisites and any valid licensing yourself; preparation neither bypasses licensing nor establishes browser compatibility.

Set `WF_SYNCFUSION_APPS_ROOT` if the built samples live elsewhere. Loose Diagram sample content also uses its declared source mapping; keep the source checkout if exercising that case. These inputs are never checked in. Only available entry assemblies appear in the outer library of a source-only build.
