# Vendor benchmark preparation — 22 October 2026

All five previously pending vendor entries now have prepared local application files, verified managed entry identities and exact real form types. They remain experimental browser compatibility probes. Preparation does not certify that a form's complete startup path or interactions work.

| Library entry | Official input / build | Real startup form |
| --- | --- | --- |
| .NET Reflector 11.1 | [Official trial download](https://www.red-gate.com/products/reflector/trial/thank-you/), package version 11.1.0.2167. Extract desktop payload and original notices from its installer as data. | `Reflector.Application.UserInterface.ApplicationWindow`, in `Reflector.exe` |
| Fiddler Classic 6.0 | [Official vendor download](https://www.telerik.com/download/fiddler), version 6.0.20262.10021. SHA-256 agrees with the [Microsoft winget manifest](https://github.com/microsoft/winget-pkgs/blob/master/manifests/t/Telerik/Fiddler/Classic/6.0.20262.10021/Telerik.Fiddler.Classic.installer.yaml). Extract both NSIS container layers as data. | `Fiddler.frmViewer`, in `Fiddler.exe` |
| DevExpress Editors demo 26.1.5 | [Official demo source](https://github.com/DevExpress/demos-winforms/tree/752f889cb6e4f6f59caebececd2196d2e967916d), EditorsMainDemo for .NET 8, using public DevExpress NuGet packages. | `DevExpress.XtraEditors.Demos.frmMain`, in `EditorsMainDemo.dll` |
| ComponentOne FlexGrid Showcase | [Official sample source](https://github.com/GrapeCity/ComponentOne-WinForms-Samples/tree/46618e95139935ec942e4f40ea1ee50a40292c48/Core/FlexGrid/CS/Showcase), .NET 10, public C1 packages, original sample resources. | `Showcase.MainForm`, in `Showcase.dll` |
| Infragistics IGPaint trial | [Official sample source](https://github.com/Infragistics/winforms-samples/tree/4e3736b743445b5f857e534002769ac3d72e2038/Applications/IGPaint), external build overlay selecting .NET 8 and public `.Trial` packages 26.1.22. | `IGPaint.frmImageEditor`, in `IGPaint.dll` |

Original tracked source in all three checkouts remains unchanged. The Infragistics overlay changes the package selection at the MSBuild preparation boundary; it does not patch controls, remove trial checks, supply a product key or remove watermarks. The initial licensed-package restore failed; the successful public-trial build is recorded separately. DevExpress evaluation diagnostics are retained in the build logs.

Neither installer was run. No system proxy/certificate settings, trial activation or vendor account registration was performed. The five binary inputs and converted results remain excluded from source publication.

## Reproduce preparation

Install Python 3 and 7-Zip for the two installer-container extractions. The three source targets use normal .NET builds; ComponentOne requires SDK 10. From the project root:

```powershell
./scripts/prepare-desktop-benchmarks.ps1 -Apps reflector,fiddler-classic
./scripts/prepare-desktop-benchmarks.ps1 -Apps devexpress,componentone,infragistics
./scripts/inspect-desktop-benchmarks.ps1 -Apps reflector,fiddler-classic,devexpress,componentone,infragistics
```

The script supports `-SourceRoot`, `-OutputRoot`, `-EvidenceRoot` and `-SevenZipPath`. For custom outputs, set `WFWASM_BENCHMARK_APPS_ROOT` in the server process before startup, then reload the library. Existing app inputs are separate and must remain connected; do not replace the main library with a benchmark-only preview.

`DesktopBenchmarks.json` records exact source commits and downloaded-container SHA-256 values. `vendor_archives.py` uses 7-Zip only to read containers and parses the pinned Reflector data format with bounded decompression, path validation, range checks and original per-file checksums. It does not import or execute vendor application/installer assemblies. Unsupported container changes fail preparation instead of being guessed. The whole-package SHA pin is checked before extraction.

Infragistics uses `scripts/benchmark-overlays/infragistics-trial.targets`. ComponentOne's source currently specifies a package version range; generated `.deps.json` files retain the actual resolved runtime package versions. The source pin alone does not freeze a future NuGet restore of that range.

## Verification and remaining work

The five prepared entries passed static metadata inspection, including entry assembly identity, exact form type and public parameterless constructor. The full catalog remains **89 entries**: 62 original apps plus 27 benchmarks. There are now **25 prepared benchmark inputs**, with OpenPDN and mRemoteNG still build-blocked.

Focused regression checks cover all five concrete recipes, routes and startup metadata; entry-only manifests still request dependency DLLs dynamically. Synthetic pending candidates remain unable to launch or become ready through an arbitrary file/receipt. Archive tests cover exact output bytes, traversal, duplicate paths, invalid ranges, checksums, truncation and decompression limits.

Real startup may still require Program/Main initialization, service adapters, resources, drawing/runtime APIs or vendor trial UI. Preserve those failures and diagnostics. Do not bypass licensing logic or substitute an unrelated window for a failed app. For Fiddler use synthetic offline sessions; network capture is a separate capability. See [the per-app behavior tasks](desktop-benchmarks-round2.md) for the required input, output and data checks.

### Observed startup boundaries

- Manifest probes returned HTTP 200 for Reflector, DevExpress, ComponentOne and Infragistics, each with one initial assembly. Dependency requests remain dynamic.
- Fiddler's original entry is prepared and statically valid, but the host rewriter currently fails while writing transformed IL: `NullReferenceException` in Cecil `CodeWriter.ComputeStackDelta` / `HasImplicitThis`. Reproduce with `/api/appmanifest?app=benchmark-fiddler-classic`. Diagnose the transformed instruction/signature operand; do not alter the original application, suppress the error or substitute a window.
- The DevExpress source explicitly disables copying package runtime DLLs. Preparation now overrides `CopyLocalLockFileAssemblies=true` and checks for the required Data/Editors assemblies. This corrected its initial missing-dependency manifest failure. Its next real browser attempt transferred the application/dependencies, then failed real-window creation with a `TargetInvocationException` under `form-onload`. Constructor/initialization/runtime compatibility still needs investigation.
- These observations establish preparation and specific remaining failures, not five working browser applications. The catalog deliberately continues to show **Prepared · unverified**.

Validation: 36 focused catalog/loader/scene cases, four server input cases, 33 shell-isolation cases and seven archive extraction cases passed (80 total). These tests do not waive the observed startup failures.
