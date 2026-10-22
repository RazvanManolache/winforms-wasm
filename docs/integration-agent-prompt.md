# Agent handoff: connect rendering and platform foundations

Finish your current coherent task and its focused checks first. Then read `docs/integration-bridges.md` and inspect the current source; other work may already have moved the integration boundaries. Preserve current successful workflows and do not redo completed fixes without a reproduction.

## Starting artifacts

- `src/WfWasm.AppIntegration`: optional integration seams, not referenced by production. `ControlLayerBridge` uses existing paint/scene contracts; `PlatformSessionBridge` uses the operation vocabulary of the existing JS broker. Disconnected adapters fail explicitly.
- `tests/WfWasm.AppIntegration.Tests`: lifecycle/protocol acceptance witnesses with test doubles. Build/run directly with `dotnet test tests/WfWasm.AppIntegration.Tests`. They do not prove browser/GPU/legacy-dialog behavior.
- Existing implementations: `WfWasm.Rendering`, `.Rendering.Gles`, `Drawing/DisplayGraphics.cs`, `CanvasHost/GpuProbe.cs`, `wwwroot/gpu-bind.js`, `Platform.Server`, `.Browser`, `.Demo`.
- Production gaps: normal Host painting uses CPU bitmaps and required-GPU mode rejects them; `WasmRegistry` still supplies null/default/no-op behavior; the main shell/server/runtime are not wired to the platform broker. Verify these facts in current code before changing them.

## First: pin one baseline

Use one server/client/fork/Drawing/rewriter generation, isolated app sessions and scoped test data. Do not read transformation inputs from another build's mutable output while measuring or accepting results. Record app/session/generation with failures and artifacts. Leave other agents' servers and original app inputs alone.

## Track A — actual GPU control pilot

1. Choose one existing real WinForms control with an audited paint vocabulary. Add a disabled-by-default pilot flag and an honest per-control backend report. Keep full required-GPU policy unchanged; the current GLES subset does not satisfy complete display features.
2. Implement `IControlPaintAdapter` in CanvasHost using the real control, existing `Graphics.ForDisplay` and the selected device image importer. Pass/apply correct target extent, DPI, clip, transforms and background/paint lifecycle. Do not recreate the UI in a probe or fake app.
3. Connect ordinary host damage/placement/frame scheduling to `ControlLayerBridge`, existing `RetainedScene` and `RetainedFramePresenter`. Start with whole-control repaint on content changes. Placement-only changes must preserve content version and avoid app repaint/upload.
4. Define ownership for root/child/nonclient/overlay drawing and inverse input coordinates. Do not allocate textures for every widget indiscriminately; respect resident memory budgets.
5. Reject unsupported operations before paint wherever possible. Once app paint has run, propagate failure without a second CPU invocation. Keep in-paint CPU read/write barriers explicitly unsupported until incremental state-preserving submission is implemented.
6. Recreate bindings on resize/eviction/loss and prove old handles/disposal cannot remove newer content. Preserve app state and release all resources on stop/restart.
7. Demonstrate actual input, independent browser pixels, callback/transfer counters and context recovery in the normal app path. Keep existing binding tests/gates intact. Report glyph-only CPU work separately; a whole-control upload is not accelerated drawing.

## Track B — connect existing platform services

1. Wire the existing `AddWfPlatform`/`MapWfPlatform` into the real server with explicit private storage, app allowlist and declared mounts. Reuse the SQLite registry and existing identity/session rules. Never accept a user ID or physical mount path from the app as authorization.
2. Mount one shell-owned `PlatformClient` and `bindPlatformHost` for the exact selected child window/origin/session. Dispose it on replace/stop; do not share a global client or send server cookies/tokens into the child.
3. Implement `IPlatformSessionTransport` over existing `PlatformRpcClient`, with camel-case DTO mapping, exact byte-to-Uint8Array conversion, bounded request IDs, cancellation, stale-session rejection and useful app-attributed errors. Keep the new bridge typed and async.
4. Hydrate registry and necessary metadata before app construction. Implement a managed synchronous registry mirror with correct keys/types/enumeration/defaults and durable flush acknowledgement. Connect the rewriter/runtime hooks coherently: virtual keys must be valid across typed locals/fields/returns/generics, not unrelated objects slipped into RegistryKey signatures.
5. Add one real existing-app setting roundtrip. Prove close/reopen persistence and user/app separation. Test denied storage, failed flush and abrupt replacement; never acknowledge failed writes as saved.
6. Map file/environment/drive APIs to declared assets, workspace, server mounts and user-selected grants. Preserve mount identity, case/path policy, stream/close/flush behavior and framework readers that bypass rewritten IO. Keep DLL loading dynamic and separate.

## Track C — synchronous dialog and print milestone

1. Make a small measured execution-model decision for one real `ShowDialog` caller. An async facade is not sufficient for `if (ShowDialog()==OK) Read(...)`.
2. Compare a dedicated app-worker suspension boundary with narrow supported continuation rewriting. Document supported patterns and prove caller-local/exception/finally/modal semantics. No main-thread waits/spins, fake nested pump, early DialogResult.None or blanket no-op rewrites.
3. Use the existing shell dialog's real user-click button for native browser picker activation, before unrelated awaits. Cover server fixture choice, device choice, cancel/denial, stale grant and immediate post-selection file reading.
4. Implement one real open/edit/save workflow with independently verified bytes and explicit persistence/permission failures.
5. Connect PrintDocument BeginPrint/PrintPage/EndPrint and HasMorePages/cancel to bounded page images with correct dimensions, margins/orientation/DPI. Use the existing browser print preview and user-triggered print dialog. Never claim physical printer delivery or successful printing from afterprint alone.

## Tests and completion conditions

For each coherent change, add a narrow contract regression and an affected real-app workflow. Test deliberate callback removal, suppressed repaint, stale registry data, failed persistence and late messages so the oracles demonstrably detect failure. Use independent pixels/values/decoded files, not candidate-reported ready/success or screenshot resemblance alone.

Required evidence includes:

- real control paint exactly once per content update, zero paint on movement/unchanged frames;
- same correct visible output after overlapping placement, resize, loss/recovery and restart;
- no unintended whole-control raster/uploads/readbacks in the GPU pilot;
- registry values survive restart and remain separated across users/apps;
- open/save/cancel/denied capability semantics with actual byte results;
- real print page sequence and honest preview/dialog completion states;
- shell remains interactive, old sessions cannot affect new ones, memory/resource counts plateau;
- comparable p50/p95/max input-to-observable-output timing and load-byte/cache accounting with normal diagnostics off.

Missing browser/hardware/vendor inputs remain visibly blocked; synthetic seam tests cannot satisfy those claims. Do not weaken existing tests, broaden feature flags, skip required evidence, suppress errors or blindly regenerate trusted hashes to make the report green.

## Optional parallel organization

After finishing current work and agreeing interfaces, split three bounded lanes if delegation is available: rendering pilot; platform transport/registry; independent workflow/lifecycle validation. One integrator owns shared `WinFormsAppHost`, bootstrap/shell and rewriter changes. Give each agent exact files/contracts, repro, test oracle and completion criteria; serialize shared-file patches. Prototype the dialog execution model before sending multiple agents into a large worker/rewriter redesign.

Deliver source changes, root causes, actual test results, real-app screenshots/artifacts, measured limits and what remains disconnected. Do not stop at compiling the skeleton or passing its test doubles. Do not publish new app binaries/conversions or push changes without the appropriate user/task authorization.
