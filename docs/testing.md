# Testing real behavior

Build with `./scripts/build.ps1` first. `./scripts/test-base.ps1` is the focused source-only starting point. Native contracts, browser output, application workflows, isolation and performance are separate evidence layers.

## Useful commands

```powershell
./scripts/test-base.ps1
dotnet test tests/WfWasm.Rendering.Tests
dotnet test tests/WfWasm.Rendering.Gles.Tests
./scripts/test-platform.ps1
node --test tests/shell-isolation/sessions.test.mjs tests/rendering-contracts/*.test.mjs
python tests/gallery-contracts/selftest.py
```

`test-platform.ps1` additionally needs Node. The gallery verifier self-test is synthetic rejection coverage, not real application acceptance. The gallery runner additionally needs Python with Playwright, Pillow and OpenCV, an installed browser and a separately running configured host. See [the runner's current coverage and limitations](../tests/gallery-contracts/README.md). Do not point a fixture test at another developer's server or clear their cache/data.

External-app suites deliberately assert their required inputs. Those binaries/assets are absent from the source release, so an unfiltered run is not a clean-checkout green gate. Supply applications under their own licenses and document their paths/versions when running integration cases. Do not silently skip missing required inputs and call full compatibility verified.

## Test design

For a bug, write setup → real browser action → independent outcome. Pair a small root-cause contract test with an actual affected application workflow. Do not use `App ready`, callback completion or nonempty pixels as the only oracle. Check exact values/models where available, visible output, independently decoded files/images and new session errors.

Start with counter/arithmetic, Todo selection/Done, Paint stroke/Undo/Redo, game reset, calendar event editing, QR decode/update, map selection, diagram movement and MDI documents. The runner still lacks some model/menu oracles; report those dimensions blocked rather than treating geometry changes as proof of correct data.

Use a pinned server/client/runtime/rewriter generation and distinct run/app/session IDs. Keep original app inputs immutable, record cache/viewport/browser settings and correlate action/frame/error timelines. Prove important tests fail when a fixture callback is removed, repaint is suppressed, resources are emptied or stale evidence is replayed. Keep synthetic success data out of real browser receipts.

## GPU and performance

`scripts/test-gpu.ps1` requires real report/artifact/build/seed/policy/test-pin inputs. Without them it fails closed. GLES unit tests and synthetic checker tests do not establish hardware rendering or faster application interaction. Do not regenerate trusted pins automatically to make a changed checker pass.

Use actual input and independently observed output for latency, with repeated raw samples and p50/p95/max. RAF callbacks, managed completion, submission and readback are different milestones. Automation screenshots do not establish physical input-to-photon latency. Keep timing runs comparable and routine diagnostics off; record probe overhead and missing responses.

The full test script retains failure status, including unresolved integration and GPU requirements. See [dated validation results](progress-2026-10.md) rather than assuming every included test or app is green.
