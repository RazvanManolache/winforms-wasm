# Next compatibility and performance work — 26 October 2026

Finish and verify your current coherent change first. Then work through the
priorities below. A completed roadmap or a green synthetic suite is not a
stopping condition while real application workflows remain broken. Produce
implementation changes with reproducible evidence, rather than repeated sweeps
of the same startup markers.

## 1. Repair focus and window handling without breaking existing workflows

The first publication candidate included three interacting changes: native
focus storage, calling Control.Focus on mouse-down, and suppressing every
owned, borderless, childless modeless form from painting and hit testing.
That candidate failed 23 CanvasHost tests. The curated update omits that slice
and the related reentrant bounds queue; CanvasHost returns to 11 failures.
The development workspace still contains the held-back implementation. Do not
blindly re-export it or conclude that its new focused tests prove compatibility.

Reproduce and reduce the failures in ModalClickOriginTests, DropdownEscapeTests,
DropdownClickTests, ModalDialogTests, FocusCommitTests, EvalModalCancelTests and
OptionalPaintDiagnosticsTests. The broad run included ObjectDisposedException
from old Button/TextBox controls and missed click/close callbacks. These are
observations, not proof that one particular change is the sole cause.

Implement a coherent focus lifecycle: SetFocus returns the previous handle,
null clears focus, destroyed handles cannot remain focused, and focus changes
dispatch the expected loss/gain and validation behavior. Scope/reset state at
the appropriate runtime-session boundary. Test two sequential forms/sessions,
disposed controls, dropdown open/selection/Escape, modal OK/Cancel, tab order
and edit validation. Run the full host suite in one process, not just new tests
in isolation. Preserve the synchronous disposal contract for ordinary dialogs
and separately verify resumable effect dialogs.

Do not classify a window as a shadow solely because it is owned, borderless
and has no child controls. Owner-painted popups and legitimate content windows
can have that shape. If shadow exclusion is needed, represent the actual
window role/style and test a visible owner-painted popup against it. Do not
silently blacklist a control type from painting to make its errors disappear.

## 2. Prove Paint.NET ownership and editing in the browser

The update adds scratch allocation identities, serialized ownership transfer,
stale-ID guards and native tests against rewritten original command-list code,
including GC-pressure cycles. Native survival is useful but is not WASM
finalizer-race acceptance or complete editing compatibility.

Use an isolated server/cache and record the exact source revision, runtime
assembly hashes, pristine input hashes and browser version. Verify the full
WindowsBase integration documented in building.md; the default base build uses
a facade which cannot provide DependencyObject/Freezable for Paint.NET. Keep
framework staging optional and portable. Never patch a running host's
fingerprinted assets or substitute unrelated app binaries.

Exercise palette choice, repeated strokes, new layers, two independent colors,
layer visibility/reordering, undo/redo, closing and reopening dialogs, and
opening another app after failure. Assert canvas/document/per-layer pixels,
not just a ready label or the presence of a thumbnail. Run long enough to
exercise GC and command-list cleanup, track live allocation counts and memory,
and verify cancellation/recovery after a real runtime exit. Do not swallow
finalizer errors or turn stale resources into unexplained blank output.

## 3. Convert the remaining failures into actionable, reproducible groups

Run all 17 native test projects unfiltered with bounded hang diagnostics.
The curated 26 October run finished at 2,091 passes, 23 failures, zero skipped.
Keep the full result visible alongside the documented focused base gate.
Separate missing inputs from real behavior and resolver defects. Existing
groups include missing upstream baselines/small-app fixtures, absent generated
evaluation reports, framework-output path assumptions, Cecil drawing dependency
resolution, WindowsBase binding, Paint startup/strokes and empty-source
WheelBitmap behavior. Read the current receipts rather than assuming these
counts never change.

Make fixture generation repeatable from declared inputs and independent of one
developer's absolute paths. Resolve reference assemblies from the actual
declared dependency closure. Preserve failing assertions; do not add blanket
waivers, fabricate evaluation results, or replace integration tests with
source-text assertions. A missing optional input is not a successful app run.

Also test the new private-font bridge under delayed loading and transient JS
registration failure: the current kick-start marker is recorded before the JS
call succeeds, so a thrown call can prevent the promised later retry. Verify
real icon glyphs and cache invalidation after the font becomes available.

## 4. Measure and improve rich-text scrolling

Start with Telerik richtexteditor, diagnostics off. Capture a fixed document,
viewport, scale, browser, CPU/GPU environment and wheel/drag input trace.
Measure input arrival to the first frame containing the expected visible
change; report p50/p95/p99, long tasks, rasterized pixels, uploaded bytes,
allocations and frame cadence. Separate cold dependency loads from steady
interaction. Diagnostic counters must remain optional and bounded.

Profile before redesigning scheduling. Check whether clip/gradient/text work
is repeated for unchanged regions, whether retained scroll-shift actually
reduces raster/upload work, and whether dependencies are synchronously fetched
during input. Fix the measured dominant cost, then replay the same trace and
compare pixel correctness and latency. Test scrolling with selection, overlays,
transparency, resizing and content changes so stale retained pixels cannot
masquerade as a speedup. WebGL contract tests do not prove GPU-rendered apps.

## 5. Make original benchmark workflows work

Choose one tractable original benchmark from the current preparation reports
and one layout/control stress case, such as a locally prepared KeePass UI or
DockPanel sample. Verify their actual framework/dependency requirements first.
Get each through startup and a useful multi-step workflow. For each blocker,
capture the first relevant exception/API boundary and implement a reusable
compatibility fix with a behavioral regression test. Keep the original app's
business logic and controls; a replacement lookalike is not acceptance.

Record prepared, starts, renders, interacts and workflow-passed separately.
Broaden component coverage after fixing the chosen blockers. Avoid collecting
more catalog entries while the same startup failure prevents useful testing.

## Coordination and completion

If using multiple agents, give them disjoint work: focus/modal lifecycle,
Paint.NET resource/browser verification, and rich-text profiling. One
coordinator owns integration, source export and server lifecycle. Each agent
should report a failing reproduction, root cause, implementation, meaningful
tests and remaining limits. Build/deploy immutable snapshots for browser runs;
do not mutate an active server's asset map. Stop only processes you own.

Finish with an updated failure table, before/after browser evidence, measured
performance results where applicable, and concrete unresolved blockers. Keep
all third-party inputs, converted applications, private logs and caches out of
the public source tree. Do not mark a workflow done from a screenshot, a
console-count plateau or a synthetic test total alone.
