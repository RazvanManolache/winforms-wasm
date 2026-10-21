# Architecture

## Execution path

```mermaid
flowchart LR
    A[Locally supplied app files] --> B[Local assembly server]
    B --> C[IL transformation and content cache]
    C -->|Entry assembly, then requested dependencies| D[.NET WebAssembly runtime]
    D --> E[Adapted Windows Forms runtime]
    E --> F[Managed drawing and browser canvas]
    G[Browser mouse and keyboard input] --> E
```

Original application files are read as inputs. Transformed copies are generated separately. The server prepares and serves code; managed application execution and rendering happen in the browser.

## Components

| Component | Role |
| --- | --- |
| `runtimes/winforms` | Vendored upstream WinForms sources and notices. |
| `src/WfWasm.WinForms` | Runtime overlays and managed replacements for Windows-specific behavior. |
| `src/WfWasm.Drawing` | Managed implementation of the supported drawing surface and virtual file access. |
| `src/WfWasm.Rewrite` and `.Tool` | Assembly analysis, reference remapping, and compatibility transformations using Mono.Cecil. |
| `src/WfWasm.CanvasHost` | Browser application, canvas presentation, input, app loading, and measurements. |
| `src/WfWasm.Host.Server` | Development server, assembly delivery, transformation cache, and error inventory. |
| `src/WfWasm.PdnCpu` | Optional managed helpers used by the Paint.NET compatibility experiment; no Paint.NET binary is bundled. |

Other source projects contain analysis tools, supporting shims, and earlier rendering experiments. The build script follows the active canvas host path.

## Dynamic loading

The shared runtime is built once for the host. Each external app starts with a manifest describing its entry assembly and declared loose content. An assembly resolution callback requests missing assembly names from the same local server. The server performs exact file lookups in configured roots and applies the configured transformation policy. It does not enumerate those roots into a download bundle.

Returned assembly payloads are checked against SHA-256 values before loading. The server cache keys include the relevant transformation inputs. The HTTP cache and client memory reuse reduce repeat delivery. The current synchronous resolution bridge and eager loose-content loading remain areas for improvement.

## Rendering and measurement

The adapted controls paint through the managed drawing layer. The canvas bridge presents the output and translates browser input into host events. A first canvas frame ends the displayed app load timer; it does not prove that every application workflow or background operation is ready.

Application data reports uncompressed payload bytes. Network transfer reports measured HTTP transfer separately. Browser resource timing and cache behavior affect these values, so screenshots of warm sessions should not be treated as cold network benchmarks.

## Compatibility boundaries

Rewriting a reference or providing a matching API signature does not reproduce all Windows behavior. Native services, synchronous UI assumptions, file persistence, accessibility, advanced controls, and graphics require their own behavior tests. Some implementations are partial or stubs. An app rendering successfully is one checkpoint in a longer compatibility investigation.

## App shell and failure boundaries

The outer library mounts a fresh runtime frame with app/session identity for each selection. Runtime messages carry that identity; late messages from disposed sessions must not overwrite the new app. Loopback development uses app-specific runtime origins. Site/renderer behavior needs browser testing: state-machine unit tests alone do not prove a blocked child leaves the menu interactive.

## Retained rendering and WebGL work

`WfWasm.Rendering` defines recorded drawing, surface/resource identity, retained scenes, presentation and CPU-access boundaries. `WfWasm.Rendering.Gles` implements a bounded device/compositor against an `IGlesContext` driver seam. `CanvasHost/GpuProbe.cs`, `wwwroot/gpu-bind.js` and the binding fixtures exercise the browser side. Normal app painting still uses managed CPU bitmaps. Probe/contract coverage is a foundation for integrating real application controls, not a claim that the whole UI is GPU accelerated.

## Platform-service experiment

`WfWasm.Platform.Server`, `.Browser` and `.Demo` are a separate service/demo path: explicit mounts and bounded file operations, session/app identity, virtual registry state, user file selection and print presentation. Existing WinForms file/registry/print adapters are not fully wired through this service. Multi-user production authentication, permission policy and complete application semantics remain separate work.

## Validation boundaries

Native tests, browser primitive pixels, real-app workflows, lifecycle/isolation, performance and hardware backend evidence are distinct checks. The gallery runner is an early selected-workflow harness; blocked model oracles and untested dimensions stay visible. See [testing](testing.md) and [this update's results](progress-2026-10.md).
