# October 2026 source update

This snapshot expands runtime compatibility, the app-session shell, platform-service scaffolding and rendering/test infrastructure. The goal remains to use AI to adapt the platform around existing business logic, with explicit behavior tests rather than a risky wholesale rewrite.

## Included in the source update

- Searchable/filterable app shell, independent app frames, app/session-attributed errors and separate loading/byte/cache measurements.
- A declarative 42-sample Syncfusion catalog and a local preparation script. Sample binaries and assets are not redistributed. With the existing everyday/Telerik entries this gives a 62-entry local test catalog, not 62 compatible apps.
- Runtime overlays for additional native/COM boundaries; resource/image-list, rich-text, cursor/dialog, layout and input compatibility changes; additional rewriter and regression fixtures.
- `WfWasm.Rendering` and `WfWasm.Rendering.Gles`: command recording, surfaces/resources, retained composition, CPU access contracts and WebGL binding/probe work. The normal application path is still CPU-rendered. No whole-application GPU performance pass is claimed here.
- Separate platform server/browser/demo sources for bounded file mounts, scoped registry persistence, user-selected files and print flows. Complete legacy-app integration is still open.
- Shell isolation, browser binding pixel, platform browser, gallery evidence/checker and native regression tests. The unfiltered integration suites still require external inputs and are not a clean-checkout gate.
- Portable public-checkout runtime and external-input paths, a smoke-only default catalog, and a full-test script that preserves failure status instead of counting parked failures as passes.
- Blazor hosting package references updated to 9.0.21, the patched .NET 9 version listed in [Microsoft's advisory](https://github.com/advisories/GHSA-qjpx-jqm5-h2c4). Use a supported, patched SDK/runtime too; this is not a security certification or a production hosting release.

## Freshly checked for this update

| Check | Observed result |
| --- | --- |
| Public source build using `scripts/build.ps1` | Passed; compatibility/compiler warnings remain |
| Drawing native suite | 447 passed |
| Focused base CanvasHost suite | 25 passed |
| Focused base rewriter fixtures | 4 passed |
| Rendering native contracts | 33 passed |
| GLES native protocol/contract suite | 55 passed; this is not hardware-browser acceptance |
| Platform native storage/HTTP suite | 41 passed |
| Platform browser-module contracts under Node | 17 passed |
| Shell and rendering/evidence checker tests under Node | 166 passed |
| Gallery verifier self-tests | Passed; synthetic rejection tests only |
| Unfiltered Host.Server integration suite | 13 passed, 137 failed in the source-only checkout; external app/sample inputs are absent. A reproduced Diagram asset assertion fails because the separately supplied image is missing. This suite is not presented as green. |
| Full 62-app browser matrix, calibrated performance and whole-app hardware GPU acceptance | Not rerun or certified for this publication |

The focused tests above retain their behavioral assertions. No vendor inputs were copied into the source checkout to make the integration counts appear green. Native/checker success and the screenshots cover different boundaries.

## Fresh browser captures

Captured on 21 October 2026 from a local development build with separately supplied apps: Paint.NET brush drawing; Map Selection highlighting Brazil; Barcode generating a QR that independently decodes to `/wasm`; Diagram Nodes rendering its real node/rich-text gallery. These captures are development-build observations, not hashes or performance receipts for every future checkout. The separate base-project capture is from the public source-only checkout built for this update.

See [capture provenance](screenshots/README.md). Session load counters in images are single observations affected by cache state, not comparable cold-start or interaction benchmarks.

## Remaining work

Reachable/responsive form layout, popup ownership, text metrics, command/input fidelity, retained-pixel invalidation, real file/save/print integration, registry adapter wiring, inaccessible controls and startup stalls remain active compatibility areas. The gallery runner still has blocked model/data oracles and untested performance/backend dimensions. Resource/API semantics and rewritten IL need positive real-value/browser tests, not just no-throw fallbacks.

The next GPU milestone is a real existing control using the normal host's accelerated drawing path with correct output, retained reuse, context recovery and actual input-to-observable-output evidence. A WebGL context or a binding test does not establish that milestone.
