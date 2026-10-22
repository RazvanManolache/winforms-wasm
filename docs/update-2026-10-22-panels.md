# Paint.NET panel and thumbnail update — 22 October 2026

This source update improves the Paint.NET showcase and includes the previously prepared desktop benchmark catalog. It remains an experiment. Application executables, converted assemblies, proprietary demo sources, loose assets, local caches and logs are excluded from the public tree.

## Visible progress

Fresh browser captures of the separately supplied Paint.NET **5.1.12** show readable RGB/HSV/opacity labels and values in Colors, named Paintbrush entries in History, and a Layers preview. Four real brush drags produce a green W. Four Ctrl+Z actions clear it. Four clicks on the visible Redo toolbar button restore it.

Independent analysis of the document rectangle `(380,300)–(580,480)` counts 1,152 green pixels before undo, zero after undo and 1,151 after toolbar redo. The captures are unaltered PNGs at 1093×884; the sidebar was closed and floating panels moved using normal UI input. These are live observations, not desktop reference images or exact pixel-parity acceptance. See [capture provenance](screenshots/README.md).

The automated redo shortcut attempt did not visibly restore the document; toolbar redo did. Do not use this update as evidence that every redo key chord is correct. The colour-wheel click changed RGB/HSV values and subsequent brush colour; precise wheel-coordinate/colour parity was not established.

The Layers preview is now visible and changes, but observed refresh can lag an edit. The CPU fallback can substitute the document composite for an unavailable layer preview; that is an approximation for multiple layers, not correct independent layer rendering. Remaining blue toolbar blocks, clipped/partial controls, effect dialogs, save/load workflows, memory bounds and performance remain work in progress.

## Source changes

- Managed half-float/BGRA thumbnail conversion and upload, inline thumbnail production and guarded delivery.
- Effect input tracking and bounded effect-graph traversal so thumbnail/composite inputs remain reachable through adapted device wrappers.
- A host feed for the latest document composite/blank state and thumbnail re-invalidation.
- CPU text raster translation/axis-scale handling and optional unexpected-raster-failure diagnostics. Rotation/shear remain unsupported by that blit path.
- Popup visibility transitions forwarded to the fork's popup registry.
- WindowsBase preparation now neutralizes ReflectionOnly guards and invalidates older staged copies for that pass.
- Pinned desktop benchmark recipes/catalog, an archive-inspection helper and regression tests. There are 27 candidates, 25 locally prepared inputs and two build-blocked inputs. Prepared means input preparation, not successful browser execution. See [desktop benchmarks](desktop-benchmarks.md), [additional candidates](desktop-benchmarks-round2.md) and [vendor preparation](vendor-benchmarks-preparation.md).

The public export retains portable paths, optional external-app filtering, patched host package versions and strict failure gates. Local-only diagnostic logging was not copied merely because it was present in the working tree. The normal application renderer still uses CPU drawing; this update does not establish broad GPU acceleration or ordinary-app file/registry/print integration.

## Validation of the exported snapshot

| Check | Result |
|---|---|
| `scripts/build.ps1` | Passed; 313 warnings in the final host build, zero errors |
| Clean staged-source archive build, no app inputs | Passed; 326 warnings in its final host build, zero errors |
| Clean `scripts/test-base.ps1`, serial run | 455 Drawing + 25 host + 4 rewriter checks passed |
| Clean browser startup | Built-in form renders; 27 optional benchmark entries correctly need preparation; no external app files |
| Drawing | 455 passed |
| CPU painting | 91 passed |
| Base loader/execution/catalog/benchmark host contracts | 36 passed |
| Server | 154 passed |
| Shell isolation | 33 passed |
| Vendor archive reader | 7 passed |
| Rewriter, unfiltered with locally available fixtures | **216 passed, 3 failed** |
| Browser Paint.NET | Four-stroke drawing, visible history, undo-to-blank and toolbar redo observed; thumbnail limits above |
| Hardware GPU/performance, full app matrix, platform integration | Not rerun; no pass claimed |

The three rewriter failures remain visible:

1. `CpuDrawingContext_Synthesized_Methods_Are_Stack_Balanced`: its fixture helper expects the fork DLL under `net9.0-windows`, while the portable base build emits `net9.0`. This run stops before its stack checks.
2. `Rewrite_CpuDrawing_Synthesizes_Headless_Context`: a stale assertion expects System.Object as the synthesized context's base; the current implementation uses RefTrackedObject. Reconcile the intended lifetime/type contract and retain strong assertions.
3. `Rewrite_WheelBitmap_Empty_Source`: the external input's zero-sized allocation throws ArgumentOutOfRangeException in its bitmap pool path.

No failure is relabelled as passing. Several integration tests return early when optional application files are missing; their test-run totals alone do not establish external-app coverage. The configured development library and browser captures require locally supplied inputs; a clean checkout does not include those applications.

An initial clean-base run overlapped another checkout's rewriter tests and one shared fixture boot failed. The case passed alone and the complete base gate passed when rerun serially. That transient first run is not counted as acceptance. Keep test runs that share fixed temporary fixture names separate until their artifact names are isolated. The clean browser capture establishes startup/rendering; its coordinate-driven click attempt did not reach the app input log, so no new clean-browser click pass is claimed.

## Next work

Make thumbnail updates immediate, accurately represent each layer independently, and bound/correctly scope the latest-composite retention. Finish toolbar/editor drawing and input alignment; then validate dialogs, effects and open/edit/save using actual output bytes. Repair the rewriter fixture setup/contract issues and empty-source behaviour without weakening gates. Preserve the observed brush/history roundtrip while connecting real retained/GPU and platform-service paths described in [integration bridges](integration-bridges.md).
