# Final change and validation note — 30 October 2026

Standalone `winforms-wasm` development ends at this snapshot. Future work
continues at [desktop-wasm](https://github.com/RazvanManolache/desktop-wasm).

## What changed since 26 October

Source overlay onto the portable scaffold (457 tracked paths: 340 added,
112 modified, 5 removed; the 2 publication-doc paths on top are counted
separately in the READY manifest, not here):

- `src` (+12/±38): RichText scroll watch, `WasmGdiBitmap`, `WasmScrollWindow`
  (Escape-wedge retarget), 9 ground-truth reference images, plus engine
  fixes from the four days since the 26 October snapshot.
- `tests` (+49/±60/−5): scroll-shift epoch/parity suites, paint pressure and
  trigger probes, popup/capture/vendor-grid coverage, PdnCpu converter and
  setting suites, rewriter redirect suites; removed 5 obsolete `TestPaths.cs`
  publication helpers (no code references remain; all five affected test
  projects compile clean; selected source-only gate below passed).
- `scripts` (±6): bundle-gate strictness (ReflectionOnly guard, OleDb
  presence), otherwise the portable `build.ps1`/`test-base.ps1` flow is
  unchanged.
- `runtimes/winforms/src` (+279/±8): newer upstream fork vintage for the
  five compile-referenced trees (`System.Windows.Forms{,.Primitives}`,
  `System.Private.Windows.Core`, `Common`, `System.Windows.Forms.Analyzers`);
  other upstream trees, `winforms/LICENSE.TXT` and
  `winforms/THIRD-PARTY-NOTICES.TXT` untouched.
- Publication-only wiring (recorded, no behavior change): rewritten-runtime
  references resolve to the generated `.artifacts/runtime/` copy via
  `$(WfWasmMigratedForms)`; full-WindowsBase staging and the strict bundle
  gate stay opt-in behind `$(WfWasmWindowsBaseSource)`.

Excluded from this snapshot (deliberate, unchanged policy): converted
third-party application binaries, `targets/` input closures, Telerik and
vendor payloads, credentials, local runtime dumps/logs, coordination and
agent records, generated build outputs and caches, upstream `.git`.

## Current verified evidence (this snapshot, source-only, 30 October 2026)

- `scripts/build.ps1`: **0 errors** (602 inherited style/analyzer warnings).
  Fork compiles from the overlaid runtime sources; the rewriter regenerates
  `.artifacts/runtime/WfWasm.WinForms.dll` (8,934,400 bytes); Host.Server
  builds. No `E:/tmp` or absolute machine paths involved.
- `scripts/test-base.ps1`: **578/578 passed, 0 failed**
  (Drawing 549, CanvasHost targeted 25, Rewrite targeted 4).
- The 26 October unfiltered native run (2,091 passes / 23 failures) is
  historical and is not this snapshot's result.

## Limitations and incomplete flows

- Browser editing and performance work remain incomplete; full native-suite
  recounts and headed browser proofs are recorded as open verifications,
  not claims.
- Paint.NET-class applications need the optional full-WindowsBase
  configuration (`-p:WfWasmWindowsBaseSource=<WindowsBase.dll>` from a
  locally provided Paint.NET closure, which is not redistributed here);
  without it the source-only bundle ships the runtime-pack facade and the
  strict gate stays idle by design.
- Converted application binaries are excluded; catalog entries that need
  them remain unavailable until prepared from permitted inputs.
- Broader framework compatibility is unfinished work, documented as a
  limitation, not a blocker for this source snapshot.
