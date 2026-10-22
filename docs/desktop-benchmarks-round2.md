# Additional application probes and architecture follow-up

Update: the five vendor placeholders below were subsequently prepared. See [the concrete vendor input recipes](vendor-benchmarks-preparation.md). Earlier preparation counts and browser observations describe the preceding snapshot.

This extends [the setup/status guide](desktop-benchmarks.md). Sources were inspected on 22 October 2026; exact runnable-target pins are in `DesktopBenchmarks.json`. Application binaries, conversions and data remain local. No GitHub publication is part of this update.

## Classification corrections

- Current [Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit) and [ShareX](https://github.com/ShareX/ShareX) use Avalonia in their inspected primary UI projects. Our targets explicitly use WinForms releases 4.0.16 and 17.1.0 respectively. This avoids claiming that modern application names imply modern WinForms payloads.
- Current NAPS2 has Eto forms with a WinForms backend. Its desktop form needs many services and is not directly a `System.Windows.Forms.Form`. The registered historical 6.1.2 target has the real `NAPS2.WinForms.FDesktop`, whose constructor still needs application services.
- ReaLTaiizor's current head requests a .NET 11 SDK. The recorded stable material demo uses v3.7.8.9 and its supported .NET 7 target. This gives one real theme probe, not coverage of the entire suite.
- AdvancedDataGridView's maintained repository is [davidegironi/advanceddatagridview](https://github.com/davidegironi/advanceddatagridview). Be.HexEditor's [SourceForge page](https://sourceforge.net/projects/hexbox/) directs users to [bernharde/hexbox](https://github.com/bernharde/hexbox).
- RegexBuddy's supplied demo page offers videos. Its purchased download requires customer identification; no managed WinForms payload was verified. HxD is a reference rather than an IL-rewriter target. Be.HexEditor supplies the managed hex-grid case.

## Work order for compatibility development

Finish any current fix and its regression checks before starting another subsystem. Do not weaken assertions, swap a failing application for a placeholder form, suppress a fatal startup failure or call an unresponsive canvas ready.

Start with the small probes below. Reproduce one behavior with real input, retain the first failure and requested assembly/native import, fix the shared boundary, then recheck the same behavior and a neighboring app. Each result needs source pin, runtime/host revision, fixture hash, action sequence and observed outcome. Distinguish build, metadata inspection, browser launch, successful input, model/data changes and performance.

| Probe | Minimal deterministic scenario | Required independent evidence |
| --- | --- | --- |
| Cyotek | Drag HSV wheel and alpha slider; enter known RGB values | Control color values plus wheel/slider pixels and pointer coordinates at multiple scales. No hue jump after switching controls. |
| AdvancedDataGridView | Bind 20 fixed rows, sort descending, filter to a known subset, clear filter | Ordered row IDs and counts; popup bounds after scrolling/resizing; reopening restores expected filter state. |
| FastColoredTextBox | Type a short program, move caret, select, replace, undo/redo, scroll | Exported text and selection indices; caret rectangle and syntax pixels after input; no lost characters or IME hang. |
| Calculator | Enter `12.5 + 7.25`, keyboard equals, clear, repeat by buttons | Exact displayed result `19.75` under the selected culture, focus/tab behavior and stable layout on resize. |
| VGAPainter | Draw a known indexed-color pattern, zoom, change palette, save/reopen | TGRV dimensions, exact pixel indices/palette and reopened pixels. File picking uses declared virtual files. |
| Be.HexEditor | Load bytes `00..FF`, edit one nibble, select a range, save/reopen | Exact changed offset/value and unchanged remaining bytes; selection bounds after zoom/scroll. |
| ReaLTaiizor | Toggle controls, open menus, resize the material form | Actual values and event counts plus paint/clip/alpha captures. Add independent Metro/Night/Cyber targets later. |
| Subtitle Edit | Import a tiny UTF-8 SRT, edit text/time, move a cue, export | Reparse exported SRT and compare cue text/timing. Use generated waveform samples; native media playback is a separate adapter. |
| ShareX | Open task settings/effect editor over a synthetic image; change a property and apply an effect | Edited bitmap hash/pixel regions and property values. Global capture/upload/tray integration stays separate. |
| NAPS2 | Implement the real constructor/startup services, import three generated pages, reorder, rotate and export | Page IDs/order, dimensions and independently rendered PDF pages. Do not silently mock a scanner success. |
| mRemoteNG | Supply the matching full MSBuild/SDK/COM toolchain; load an offline synthetic connection tree | Exact tree nodes, docking/tab state and configuration roundtrip. No real hosts, passwords, network sessions or registry credentials. |

For vendor candidates, first select a concrete legitimately obtained version and record archive hash, managed assembly identity, form type, referenced libraries, imports, required startup initialization and applicable notices. Only then replace `build:manual` metadata with a verified input/preparation recipe. The current guards intentionally prevent arbitrary copied files or a `prepared:true` receipt from launching an unverified candidate. Do not activate trials or redistribute payloads as part of host development.

## Native-call triage

Run `scripts/inspect-desktop-benchmarks.ps1` before changing native boundaries. It reads metadata without executing applications and records constructors, references and static P/Invokes. The current inventory includes Windows drawing/input APIs in Cyotek, FastColoredTextBox and ReaLTaiizor; Subtitle Edit adds media/audio libraries; NAPS2 adds TWAIN/WIA. These are not automatically all startup-critical calls.

Classify each call by its actual caller and result use:

1. Display/platform queries: implement meaningful browser equivalents where possible; document unsupported flags and DPI semantics.
2. Drawing, capture, input and windowing: connect to the existing runtime boundary with correct ownership and coordinates; verify pixels and events.
3. Files, registry and printing: use declared mounts, scoped storage and browser services; preserve cancellation and write acknowledgement.
4. COM, native processes, scanners, media and network proxies: provide explicit capability/service adapters or a visible unsupported result. A stub returning success can corrupt application state or hide the next failure.

Static import absence is not a completeness guarantee: COM, reflection, dynamically chosen libraries and missing dependencies require separate inspection. Native code is never made portable merely by copying its DLL into an assembly lookup directory.

## LibreWinForms architecture observations

Inspected upstream pin: `63d27a4975aad6a7066e90e487103b3c432bfbb4`. Its [README](https://github.com/wieslawsoltes/LibreWinForms/blob/63d27a4975aad6a7066e90e487103b3c432bfbb4/README.md) describes a source/SDK port with typed ProGPU/Silk.NET seams. This is a useful comparison for platform ownership and retained drawing; it is not a drop-in WebGL backend for our .NET 9 WASM runtime. Its native dispatcher and GPU stack require a browser-specific design rather than a direct copy.

Useful contracts to adapt, with separate tests:

- [Render retirement](https://github.com/wieslawsoltes/LibreWinForms/blob/63d27a4975aad6a7066e90e487103b3c432bfbb4/docs/window-render-retirement.md): closing during paint must prevent stale presentation, retain resources until the active frame unwinds, preserve the original failure and make failed cleanup observable. Add a test that closes/replaces an app during a frame and rejects old surface presentation.
- [Drag input lifetime](https://github.com/wieslawsoltes/LibreWinForms/blob/63d27a4975aad6a7066e90e487103b3c432bfbb4/docs/drag-input-lifetime.md): each drag owns a revocable lease; queued events from the previous drag cannot complete a later one. Test pointer cancellation, capture loss, reentrant drop and switching apps mid-drag.
- [Modal completion](https://github.com/wieslawsoltes/LibreWinForms/blob/63d27a4975aad6a7066e90e487103b3c432bfbb4/docs/message-box-modal-lifetime.md): results and cleanup belong to one exact modal generation, with owner restoration only after its release. In a browser, do not block the shell/event loop by copying a native nested dispatcher loop. Test nested dialogs, close during callbacks and late old-session completion.
- [Clip and pixel mapping](https://github.com/wieslawsoltes/LibreWinForms/blob/63d27a4975aad6a7066e90e487103b3c432bfbb4/docs/portable-drawing-clip-parity.md): preserve source transforms, coordinate frames, alpha and fractional pixel mapping. Use independent Windows reference captures with fixed fonts/DPI and explicit color/alpha rules; a successful GPU submit does not prove equivalent output.
- [Application isolation](https://github.com/wieslawsoltes/LibreWinForms/blob/63d27a4975aad6a7066e90e487103b3c432bfbb4/docs/librewinforms/application-isolation.md): pass primitive versioned messages across an ownership boundary. Our equivalent remains an isolated browser runtime/session; its native child-process mechanism is not available as-is in WASM.

The live upstream issues reviewed were [#174, DataGridView header clipping and scrollbar paint](https://github.com/wieslawsoltes/LibreWinForms/issues/174) and [#160, release qualification](https://github.com/wieslawsoltes/LibreWinForms/issues/160). They reinforce the distinction between implementation tests, package/runtime identity and final pixel/input parity. Their reported CI results belong to upstream; none was rerun or adopted as evidence for this project.

Keep current CPU painting as the measured baseline. Use these probes to locate allocation, text, clip, invalidation and input bottlenecks before replacing rendering/scheduling. GPU acceptance still requires actual WebGL execution, independent pixel checks, resource-lifetime/error behavior and measured input-to-visible response. Architectural similarity and passing mock contracts are not acceleration evidence.

## Current local evidence

- Ten new source inputs built/prepared; their original tracked source stayed unchanged. LINQPad 5.52.1's official xcopy ZIP was also downloaded, hash-pinned and inspected without executing it. Combined with the first batch, 20 declared entry identities and startup form types passed static inspection.
- The remaining automatic inputs are OpenPDN (uncompilable upstream placeholders) and mRemoteNG (COM/toolchain boundary).
- Five vendor candidates are visible with manual-input notes and have no registered server payload.
- Focused catalog/loader/scene checks: 31 passed. Server payload/preparation checks: four passed. Shell-isolation checks: 33 passed. These are integration regressions, not whole-app behavior certification.
- Browser observations for this batch must be recorded separately from build/preflight outcomes. The earlier Minesweeper launch still has an unresolved Welcome/input issue.

### Browser check on the isolated preview

The refreshed preview returned 28 library entries: Smoke plus 27 benchmarks, with 20 prepared and five manual vendor candidates. Selecting .NET Reflector while Cyotek's startup screen was present removed the runtime and displayed the specific local-input notice with zero payload metrics. Selecting the new calculator afterward created a fresh session and rendered the real calculator form.

The calculator displayed `0`. Four canvas clicks on `1`, `+`, `2`, `=` did not visibly change that display. This is a retained input failure, not a passed arithmetic test; investigate shared canvas coordinate/focus/event delivery alongside the earlier Minesweeper issue. No whole-app interaction claim is made from the rendered form or ready event. The live screenshot and detailed static reports remain local evidence outside the source distribution.
