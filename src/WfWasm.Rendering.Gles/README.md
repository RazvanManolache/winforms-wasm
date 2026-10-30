# WfWasm.Rendering.Gles — bounded GPU milestone 1

Status: device-side logic + driver seam + protocol tests. **Not wired to production.
GPU acceptance is explicitly not claimed; the GPU gate stays red.**

The device translates `DrawingBatch` commands into explicit driver primitives
through `IGlesContext`. It never rasterizes on the CPU. What it does own:

- Context creation via an injected driver factory (probe creates nothing).
- GPU-backed surfaces (texture + framebuffer pairs, no managed pixel authority).
- Basic primitives: clear, solid rect fills, textured blits, rect clips,
  SourceOver/SourceCopy blend, retained composition.
- Explicit disposal (all driver resources deleted, idempotent) and context-loss
  behavior (no driver deletes after loss, recovery identity published).
- Synchronous CPU leases (read drains queued drawing, then flushes + reads back;
  write commit drains queued drawing first so committed pixels land on top, then
  commits a subregion once; uncommitted staging is discarded).
- Pinned-image deferred release, LRU budget eviction, measured counters.

## Files

`src/WfWasm.Rendering.Gles/` (new project, NOT in `WfWasm.sln`):

- `WfWasm.Rendering.Gles.csproj` — net9.0; references only `WfWasm.Rendering`.
- `IGlesContext.cs` — proposed driver seam (`IGlesContextFactory`, `IGlesContext`).
- `GlesRenderDeviceFactory.cs` — session-scoped factory, honest feature subset.
- `GlesRenderDevice.cs` — `IRenderDevice`: queue, submit, loss, dispose, counters.
- `GlesSurfaceStore.cs` — `ISurfaceStore`: id/generation tables, resize, budget.
- `GlesImageResources.cs` — `IImageResources`: versioned import cache, pinning.
- `GlesCpuSurfaceAccess.cs` — `ICpuSurfaceAccess` + lease implementation.
- `GlesCommandTranslator.cs` — batch-to-driver-op translation, support validation.
- `GlesRetainedCompositor.cs` — `IRetainedCompositor`: drain queue, clear the presentation target to transparent black (no stale pixels on move/remove), compose layers.
- `GlesDeviceCounters.cs` — measured counters record.
- `GlesTextService.cs` — explicit not-implemented text service.

`tests/WfWasm.Rendering.Gles.Tests/` (new project, NOT in `WfWasm.sln`, 30 tests):

- `FakeGlesContext.cs` — test-owned protocol double (logs calls, simulates Clear
  fills + transfer memory; does NOT rasterize fills/blits; proves no pixels).
- `GlesMilestoneTests.cs` — lifecycle, translation, leases, composition,
  disposal, loss/recovery, budget, counters, and RequireGpu-rejection tests.

## Proposed interface surface (owned here; shared contracts unchanged)

`IGlesContextFactory`: `IsSupported` (pure query, binds no canvas),
`SupportDetail`, `Create(presentationTargetId, presentation)`.

`IGlesContext`: `ContextLost` event, `IsLost`, `MakeCurrent`,
`CreateTexture`/`DeleteTexture`, `CreateFramebuffer`/`DeleteFramebuffer`,
`BindFramebuffer` (0 = presentation), `SetViewport`, `SetScissor`,
`SetSourceOver`, `Clear`, `FillQuad`, `DrawTextureQuad`,
`UploadSubImage`, `ReadPixels`, `Flush`.

Real-binding mapping: `FillQuad` = solid-quad program over two triangles;
`DrawTextureQuad` = textured-quad program with source-rect sampling + opacity;
layer composition = one `DrawTextureQuad` per layer in commit order. All coords
are device pixels, top-left origin; all transfers are straight sRGB RGBA with
row 0 = top. The binding flips Y and centers texels internally.

`GlesRenderDeviceFactory.MilestoneFeatures` =
`Images | SourceCopy | CpuPixelAccess | RetainedComposition` — deliberately not
`DrawingFeatures.Display`, so `RenderSessionFactory` with `RequireGpu` rejects
this device before creating anything (proven by test).

## Build and test (for the sequencing agent)

Do NOT add either project to `WfWasm.sln` yet. Build/test directly:

```powershell
dotnet test E:/AI/Code/WindowsForms/tests/WfWasm.Rendering.Gles.Tests/WfWasm.Rendering.Gles.Tests.csproj
```

The milestone author did not run builds (sequenced by the main agent), so treat
the first run as compile verification. Targeted/solution scripts will not
discover these projects until they are wired in; that is intentional.

## Fixed assumptions (coordinate before changing)

- A1 DPI: command transforms already include DPI exactly once; the device never
  multiplies by `DpiScale`.
- A2 `DrawSurfaceImage.Source` is in image device pixels (top-left origin) and
  must lie within the imported image.
- A3 `Release` of an unknown/stale handle throws; each handle is released once.
- A4 Live surfaces always report `PixelAuthority.Device` (leases are transient
  copies, not retained shadows); `IsValid` never throws and fails closed.
- A5 `Resize` discards pixels and resets the content version to 0.
- A6 Batches whose target died after enqueue are discarded at submit (the host
  was notified via `Invalidated`); images referenced by queued draws are pinned
  until the queue drains.
- A7 The device is single-threaded; the host serializes submissions.
- A8 Composition samples with `Linear`; identity quads must be texel-exact in
  the real binding.

## Proposals for the main agent (not implemented here)

- Keep the coarse `DrawingFeatures` flags; the milestone documents its rect-only
  subset in the probe reason instead of adding finer flags.
- Recovery = `CreateAsync` again on the same session-scoped factory (next
  generation); consider a future explicit `CreateRecoveryAsync(lostIdentity)`.
- Counters stay device-specific (`GlesRenderDevice.Counters`) until a shared
  reporting contract is designed.
- Still open: `Graphics.ForDisplay` adapter, mid-paint CPU barrier, real
  `IGlesContext` over WebGL2, presentation framebuffer-0 vs offscreen-texture
  decision, calibrated budgets, shader programs, perf measurement.

## Unresolved issues

- No strokes, gradients, non-rect paths, complex clips, or text (all fail fast
  with explicit reasons).
- No browser evidence: no real context, no pixels, no timings. The 22 browser
  contracts and `scripts/test-gpu.ps1` are untouched and must still fail.
- Pixel-coverage rule v0 (floor/ceil scissor) needs conformance tightening.
- Premultiplied and non-sRGB inputs are rejected, not converted (except
  Bgra8→Rgba8 channel swap at import).
