# Desktop application benchmarks

The **Benchmarks** library filter contains **27 candidates**: 27 targets with declared managed inputs and verified startup metadata. They are compatibility probes, not supported ports. Existing Paint.NET entries remain separate from historical OpenPDN.

## Preparation snapshot — 22 October 2026

Twenty-five inputs were prepared locally. Static assembly inspection confirms their declared entry identities and startup form types. OpenPDN and mRemoteNG remain blocked. A native build or extracted package does **not** establish browser compatibility. All five previously pending vendor entries are now prepared; see [their concrete recipes and limits](vendor-benchmarks-preparation.md).

| Input / upstream | Prepared input | What it exercises / remaining boundary |
| --- | --- | --- |
| [OpenPDN](https://github.com/rivy/OpenPDN) | **Blocked** | Floating tools, color picker and layers. Pristine `src/Resources/InvariantStrings.cs` contains uncompilable branding placeholders. A reviewed build overlay is needed before a real input exists. |
| [PKHeX](https://github.com/kwsch/PKHeX) | Built with SDK 10.0.402 | Nested tabs, numeric editors, sprites and binding. The browser host remains .NET 9; newer BCL/runtime requirements need compatibility work. |
| [KeePass 2.61.1](https://keepass.info/download.html) | Official portable ZIP, SHA-256 checked | Lists, modal dialogs and crypto. Use invented values and a disposable database. Secure desktop, auto-type and Windows credential protections are not supplied by this integration. |
| [Git Extensions](https://github.com/gitextensions/gitextensions) | Built with SDK 10.0.402 and pinned submodules | Graphs, split panes and owner drawing. Correct application initialization and virtual/proxied Git access still need validation. The presence of a generated parameterless constructor does not establish startup parity. |
| [BizHawk](https://github.com/TASEmulators/BizHawk) | Built, .NET Framework 4.8 frontend | Menus, tool windows, invalidation and input mapping. MainForm requires startup services; native emulator cores, graphics/audio, firmware and ROMs are separate. |
| [DockPanel Suite](https://github.com/dockpanelsuite/dockpanelsuite) | DockSample built with explicit .NET Framework 4.8 override | Floating windows, docking, tabbed documents and reparenting. This revision contains `DockSample`, not `WinFormsTest`. |
| [Krypton Toolkit](https://github.com/Krypton-Suite/Standard-Toolkit) | TestForm built for `net9.0-windows` | Custom renderers and non-client drawing. Startup is `TestForm.StartScreen`; optional native WebView2 dependencies need separate handling. |
| [Minesweeper](https://github.com/ManuelKlaer/windows-forms-minesweeper) | Built | Dynamic controls, mouse buttons, timer and dialogs. The real main window and Welcome modal rendered in the browser. Start playing did not visibly dismiss the modal during the initial check; gameplay is unverified. |
| [DS-step](https://github.com/klzk-myy/DS-step) | Built | Disk treemap, tree/grid navigation and recursive traversal. MainForm needs a view model and services; SQLite and native MFT scanning require adapters. Use a declared fixture folder. |
| [ChocolateCHIP](https://github.com/JOBBIN9422/ChocolateCHIP) | Original managed executable copied locally | Bitmap blits, timer invalidation and keys. Upstream has no project or license file at the pinned revision. Local experiment only; no redistribution. |
| [Subtitle Edit 4.0.16](https://github.com/SubtitleEdit/subtitleedit/tree/4.0.16) | Built with full Visual Studio MSBuild | Historical WinForms UI: subtitle list, audio waveform, timeline and encodings. Current head uses Avalonia. Media/audio/OCR engines require separate services. |
| [ShareX 17.1](https://github.com/ShareX/ShareX/tree/v17.1.0) | Built with full Visual Studio MSBuild | Historical WinForms UI: custom controls, property grids and image effects. Current head uses Avalonia. Native capture, tray/hotkeys and uploads remain outside this probe. Restore reports advisories in the pinned SSH.NET dependency; retain an offline synthetic-image workflow. |
| [NAPS2 6.1.2](https://github.com/cyanfish/naps2/tree/v6.1.2) | Built, Standalone/x86 with explicit Framework 4.8 override | Historical direct WinForms form. Current head uses Eto with a WinForms backend; that is a separate adapter target. FDesktop needs constructor services; scanner drivers and PDF native engines remain separate. |
| [mRemoteNG](https://github.com/mRemoteNG/mRemoteNG) | **Blocked** | Current source combines .NET 10, WinForms, WPF and COM/native protocols. Core MSBuild fails ResolveComReference; installed Visual Studio MSBuild selects SDK 9 and rejects its .NET 10 target. A matching full-framework/SDK toolchain and real ActiveX interop are needed; no fake COM assemblies were supplied. |
| [ReaLTaiizor v3.7.8.9](https://github.com/Taiizor/ReaLTaiizor/tree/v3.7.8.9) | Material demo built for net7.0-windows | Exact form `ReaLTaiizor.UI.Form17`. Stable release avoids current head's .NET 11 SDK requirement. Material is one theme; Metro/Night/Cyber require additional targets. |
| [Cyotek Color Picker](https://github.com/cyotek/Cyotek.Windows.Forms.ColorPicker) | Built with full Visual Studio MSBuild | Color wheel/grid and alpha sliders. Disable the old SourceLink metadata query for modern Git repositories; application source is unchanged. |
| [AdvancedDataGridView](https://github.com/davidegironi/advanceddatagridview) | Sample built for net8.0-windows | Maintained successor to the missing Zuby repository. Tests filtering/sorting, bound row values and popup placement. |
| [Ruben Roy Calculator](https://github.com/rubendavidroy/Calculator) | Built with explicit Framework 4.8 override | Separate real calculator fixture. Arithmetic, keyboard input and layout need behavioral checks. |
| [FastColoredTextBox](https://github.com/PavelTorgashov/FastColoredTextBox) | Tester built with explicit Framework 4.8 override | Text measurement, caret/selection, syntax paint and keyboard shortcuts. Native IME/caret calls remain dependencies despite its small UI. |
| [VGAPainter](https://github.com/cable-car-games/VGAPainter) | Built with explicit Framework 4.8 override | Palette indices, zoom, brush tracking and TGRV/image export. |
| [Be.HexEditor](https://github.com/bernharde/hexbox) | Built with .NET 10 | Official GitHub successor linked by SourceForge. Exact form `Be.HexEditor.FormHexEditor`. Hex bytes, cursor mapping, selection and file export need independent fixture assertions. |
| [LINQPad 5.52.1 Standard](https://www.linqpad.net/Download.aspx) | Official xcopy ZIP downloaded and hash-pinned | Exact real form `LINQPad.UI.MainForm` requires `(string, bool)` startup arguments and initialization. Dynamic compilation, processes and native/HTML result hosts need adapters. No application code or EULA acceptance ran during preparation. |

The requested TreeSize-WinForms link and both suggested CHIP-8 links returned 404 during discovery. DS-step and ChocolateCHIP are verified WinForms replacements. BizHawk's canonical organization is TASEmulators.

Exact source revisions, projects, entry names, form types, license notes and the KeePass download hash live in [`DesktopBenchmarks.json`](../src/WfWasm.CanvasHost/DesktopBenchmarks.json). In particular, ChocolateCHIP's file is named `ChocolateCHIP.exe` upstream but its assembly identity is `WindowsFormsApp1`; preparation preserves its bytes under the resolver-compatible filename. BizHawk's built entry is `BizHawk.Client.EmuHawk.exe`.

### Vendor inputs and references

Reflector, Fiddler Classic, DevExpress Editors, ComponentOne FlexGrid Showcase and Infragistics IGPaint trial now have concrete payloads and verified startup forms. The earlier manual-input placeholders have been replaced. See [official inputs, preparation instructions and remaining compatibility work](vendor-benchmarks-preparation.md).

[RegexBuddy's supplied demo link](https://www.regexbuddy.com/demo.html) contains videos; no managed payload was verified. [HxD](https://mh-nexus.de/en/hxd/) remains a reference, with Be.HexEditor as the managed target. [LibreWinForms](https://github.com/wieslawsoltes/LibreWinForms) is an architecture reference, covered in [the follow-up notes](desktop-benchmarks-round2.md).

## Prepare locally

Install the .NET SDKs required by upstream projects, including .NET 10 for current PKHeX and Git Extensions. Legacy projects also require Visual Studio MSBuild and appropriate .NET Framework targeting packs. Restore requires network access.

From the repository root in PowerShell:

```powershell
# Start with a smaller input.
./scripts/prepare-desktop-benchmarks.ps1 -Apps minesweeper

# Modern large applications; Git Extensions initializes its pinned submodules.
./scripts/prepare-desktop-benchmarks.ps1 -Apps pkhex,gitextensions

# Explicitly opt into retargeting the legacy DockSample build.
./scripts/prepare-desktop-benchmarks.ps1 -Apps dockpanel -LegacyFramework v4.8

# New managed probes. These select their documented upstream target frameworks.
./scripts/prepare-desktop-benchmarks.ps1 -Apps realtaiizor-stable,advanceddatagridview,hexbox
./scripts/prepare-desktop-benchmarks.ps1 -Apps subtitleedit-winforms,sharex-winforms,cyotek
./scripts/prepare-desktop-benchmarks.ps1 -Apps benchmark-linqpad5

# Legacy originals: opt into the recorded Framework 4.8 retargeting separately.
./scripts/prepare-desktop-benchmarks.ps1 -Apps naps2-winforms,fastcoloredtextbox,external-calculator,vgapainter -LegacyFramework v4.8

# With no -Apps argument, attempt every registered input, recording each result.
./scripts/prepare-desktop-benchmarks.ps1
```

Defaults:

- Source checkouts / downloaded packages: `external-apps/benchmark-sources/`.
- Prepared managed applications: `.artifacts/benchmark-apps/<slug>/`.
- Logs and aggregate receipts: `.artifacts/benchmark-evidence/`.

These directories are ignored by Git. The script refuses modified tracked source, an unexpected origin or a different revision instead of resetting existing work. It does not patch application source. `-LegacyFramework v4.8` is an explicit build-property override, not a claim that the original framework version was reproduced.

For an existing external source cache or output directory:

```powershell
$benchmarkSources = Join-Path (Get-Location) 'external-apps/benchmark-sources'
$benchmarkOutputs = Join-Path (Get-Location) '.artifacts/benchmark-apps'
./scripts/prepare-desktop-benchmarks.ps1 -Apps minesweeper `
  -SourceRoot $benchmarkSources `
  -OutputRoot $benchmarkOutputs
$env:WFWASM_BENCHMARK_APPS_ROOT = $benchmarkOutputs
./scripts/build.ps1
dotnet run --no-launch-profile --project src/WfWasm.Host.Server -- --urls http://127.0.0.1:5210
```

Set the environment variable in the server's process before starting it. Reload the library after preparing new files. The local development workspace can also use an ignored directory junction from the default output folder to a separate input cache.

An attempted preparation writes `prepared:false` before starting and a final `preparation.json` afterward. Interrupted or failed preparation cannot advertise stale output as ready. An aggregate failure leaves successfully prepared entries available. OpenPDN currently fails even with the legacy framework override because of its upstream source placeholders.

## Integration and isolation

`DesktopBenchmarks` supplies catalog metadata; `SceneState` supplies deep links. `DesktopBenchmarkInputs` registers server lookup roots and **only the entry assembly** in the initial application manifest. Dependencies continue to be requested dynamically by the existing resolver. An extra search folder is a lookup location, not permission to preload every DLL.

`DesktopBenchmarkForms` resolves the exact declared form and requires a compatible public parameterless constructor. Missing types, incompatible forms and missing startup services produce an explicit error. No unrelated form or built-in smoke screen substitutes for a failed benchmark. Applications whose Program/Main creates services need a narrowly scoped startup adapter; do not manufacture a constructor that skips essential initialization.

The shell lists missing inputs with a preparation notice and creates no runtime for that selection. Prepared entries say **Prepared · unverified**. They use the existing independent application sessions, error identity, timing and transfer metrics. Loading files successfully does not prove the app can start, respond or save data correctly.

Inspect declared forms, managed references and native imports without executing the originals:

```powershell
dotnet restore src/WfWasm.Rewrite
./scripts/inspect-desktop-benchmarks.ps1
# Or select inputs and a custom cache:
./scripts/inspect-desktop-benchmarks.ps1 -Apps benchmark-cyotek,benchmark-sharex `
  -InputRoot $env:WFWASM_BENCHMARK_APPS_ROOT
```

The static preflight reads Cecil metadata and writes a local JSON inventory, including entry hashes, constructor signatures, P/Invoke modules and non-managed files. Inspection is separate from resolver loading: it never sends those files to the browser. Missing dependencies, COM, reflective loading and process execution can hide additional native requirements; zero imports is not proof of browser support.

## Next compatibility checks

Use independent outcomes rather than a ready event or nonempty screenshot alone:

1. Minesweeper: dismiss Welcome, generate a board, reveal and flag known cells, reset, and verify timer/state changes. Diagnose the initial modal/input issue first.
2. DockPanel/Krypton: open tools, drag/dock/undock, resize, change tabs and themes; verify layout geometry and pixels after interaction.
3. PKHeX: use a synthetic save fixture; edit fields and tabs, then export and independently check the changed bytes. Resolve .NET 10 BCL calls against the host deliberately.
4. Git Extensions: reproduce upstream startup using a disposable fixture repository through an explicit service; verify selected commits, graph and diff content. Do not run arbitrary native commands from browser data.
5. DS-step: supply its view model/services and declared virtual folder; compare totals and treemap weights to known fixture sizes. Browser dialogs must use the platform-service boundary.
6. KeePass: create/open a disposable test database and verify a synthetic entry survives save/reopen. Keep native security features explicitly unsupported until implemented and tested.
7. BizHawk/CHIP-8: first separate frontend behavior from native cores. Use a redistributable deterministic test program to measure bitmap updates, key responses and frame timing. Obtain no commercial ROMs or firmware.
8. OpenPDN: review branding/resource exceptions, create a documented build overlay outside pristine inputs, then validate the real form, tool windows, layers and edited image bytes.

For every failing app, retain app/session identity, phase, exception and requested assembly identity. Confirm switching to Smoke works after failure and during load. Profile input-to-visible response before changing scheduling or drawing architecture.

## Verification and distribution

The isolated source checkout built successfully. Focused catalog/loader/scene checks passed **36 tests**, server input checks passed **4**, and shell isolation checks passed **33**. The tests cover catalog wiring, unique routes, pinned metadata, exact-form failure, entry-only manifests and rejection of failed preparation receipts. Vendor candidates cannot publish payload specs or bypass manual status with an arbitrary file/receipt. Static preflight verified all 25 prepared entries and declared form types. Seven archive extraction tests also passed. These checks do not certify working browser applications.

Application inputs, converted assemblies, binaries, databases, ROMs and build caches remain local and excluded from source publication. Only host code, catalog metadata, setup instructions and tests belong in the base repository. Upstream licenses vary: OpenPDN has resource/artwork and GPC exceptions; BizHawk has mixed component licenses; ChocolateCHIP has no license file at this revision. Consult original notices before redistribution.
