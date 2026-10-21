# WfWasm.Rendering.Gles

Experimental retained rendering device and compositor against `IGlesContext`. This is a bounded implementation and browser-probe foundation, not the default painting backend for normal applications.

The device owns surfaces/framebuffers, command translation, image resource caching, retained composition, CPU-access synchronization, counters and disposal/context-generation behavior. Unsupported operations must remain explicit. Browser wiring lives in `WfWasm.CanvasHost/GpuProbe.cs` and `wwwroot/gpu-bind.js`; primitive binding fixtures are under `tests/rendering-contracts`.

Native protocol/contract tests:

```powershell
dotnet test tests/WfWasm.Rendering.Gles.Tests
```

These test-owned driver doubles and semantic checks do not prove real browser pixels, hardware acceleration or production app performance. `scripts/test-gpu-binding.ps1` verifies real binding evidence; `scripts/test-gpu.ps1` additionally requires the full trusted app/correctness/performance report inputs and fails when they are missing.

Remaining milestone: route a real existing application control through the normal host into this backend, preserve one paint callback per content update, reuse retained placement, handle unsupported/CPU-access boundaries honestly, recover lost contexts and prove visible correctness plus input-response timings. Whole-control bitmap uploads or a separate lookalike probe do not establish accelerated application drawing.

See [architecture](../../docs/architecture.md), [testing](../../docs/testing.md) and [current validation limits](../../docs/progress-2026-10.md).
