// Test infrastructure only. Load in an isolated browser before app bootstrap,
// install the audit, then pass the real app's test adapter to runGpuContracts.
// This file supplies no renderer and does not turn on acceleration.
import { DRAWING_SCENARIOS, LIFECYCLE_CASES, seededScenario, runExtraContracts } from "./gpu-scenarios.mjs";
function requireThat(condition, message) {
    if (!condition) throw new Error(message);
}

export const GPU_CASES = [
    "real OnPaint draws through device without CPU surface uploads/readbacks",
    "changed control state becomes changed pixels",
    "placement reuses surface without repaint/upload/readback",
    "idle ticks perform no rendering or transfers",
    "explicit CPU read observes queued GPU drawing",
    "CPU mutation is visible to subsequent device composition",
    ...DRAWING_SCENARIOS.map(scenario => scenario.name),
    "seeded changing rectangle grid",
    ...LIFECYCLE_CASES
];

export function counterDelta(before, after) {
    const result = {};
    for (const [key, value] of Object.entries(before)) {
        requireThat(Number.isFinite(value) && Number.isFinite(after[key]), `Invalid counter: ${key}`);
        requireThat(after[key] >= value, `Counter reset during measurement: ${key}`);
        result[key] = after[key] - value;
    }
    return result;
}

export function assertDeviceFrame(observed, managed) {
    requireThat(observed.drawCalls > 0, "No WebGL drawing observed; backend label alone is insufficient");
    requireThat(observed.readbacks === 0, "Normal display frame read pixels back to the CPU");
    requireThat(observed.canvasReadbacks === 0, "Normal display frame read Canvas2D pixels back to the CPU");
    requireThat(observed.fullSurfaceUploads === 0, "Procedural UI was uploaded as a CPU-painted surface");
    requireThat(observed.uploadedPixels <= 1024, "Procedural UI used large or tiled CPU pixel uploads");
    requireThat(observed.unknownUploads === 0, "Pixel upload dimensions were not auditable");
    requireThat(observed.diagnosticLogs === 0, "Routine paint diagnostics were enabled during the device frame");
    requireThat(managed.controlPaints > 0, "The real WinForms OnPaint path was not exercised");
    requireThat(managed.cpuRasterPixels === 0, "Display drawing used CPU rasterization");
    requireThat(managed.readbackBytes === 0, "Managed display path read back pixels");
    requireThat(managed.fallbacks === 0, "Strict GPU fixture silently fell back");
}

export function assertRetainedPlacement(observed, managed, before, after) {
    requireThat(after.surfaceId === before.surfaceId, "Placement change replaced the retained surface");
    requireThat(after.contentVersion === before.contentVersion, "Placement change changed surface content");
    requireThat(observed.drawCalls > 0, "Placement change was not drawn by WebGL");
    requireThat(observed.uploads === 0 && observed.readbacks === 0 && observed.canvasReadbacks === 0,
        "Placement-only change transferred pixel data");
    requireThat(managed.controlPaints === 0 && managed.cpuRasterPixels === 0 && managed.readbackBytes === 0,
        "Placement-only change repainted or read back content");
    requireThat(managed.fallbacks === 0, "Placement-only change used a fallback");
}

function rgbaAt(frame, x, y) {
    requireThat(Number.isInteger(frame.width) && Number.isInteger(frame.height) &&
        frame.width > 0 && frame.height > 0 && frame.rgba?.length === frame.width * frame.height * 4,
        "Invalid pixel capture");
    requireThat(x >= 0 && y >= 0 && x < frame.width && y < frame.height, "Pixel outside capture");
    const offset = (y * frame.width + x) * 4;
    return Array.from(frame.rgba.slice(offset, offset + 4));
}

export function assertPixels(frame, samples) {
    requireThat(samples.length > 0, "No visible-output samples specified");
    for (const sample of samples) {
        const pixel = rgbaAt(frame, sample.x, sample.y);
        requireThat(pixel.every((v, i) => Math.abs(v - sample.rgba[i]) <= (sample.tolerance ?? 0)),
            `Visible pixel mismatch at ${sample.x},${sample.y}: ${pixel} != ${sample.rgba}`);
    }
}

export function assertFixtureContent(frame, accent) {
    assertPixels(frame, [
        { x: 20, y: 20, rgba: accent },
        { x: 76, y: 56, rgba: accent }, // Path/ellipse interior.
        { x: 120, y: 80, rgba: [255, 255, 255, 255] },
        { x: 99, y: 43, rgba: [255, 255, 255, 255] }, // Outside texture clip.
        { x: 100, y: 44, rgba: [255, 0, 0, 255] },
        { x: 101, y: 44, rgba: [0, 0, 255, 255] },
        { x: 100, y: 45, rgba: [0, 255, 0, 255] }
    ]);
    const left = rgbaAt(frame, 65, 20), right = rgbaAt(frame, 110, 20);
    requireThat(left[0] > left[2] + 100 && right[2] > right[0] + 100,
        "Gradient was missing, flattened or reversed");
    function darkPixels(x0, y0, x1, y1) {
        let count = 0;
        for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) {
            const p = rgbaAt(frame, x, y);
            if (Math.max(p[0], p[1], p[2]) < 96 && p[3] > 200) count++;
        }
        return count;
    }
    requireThat(darkPixels(64, 44, 89, 69) > 4, "Path stroke was not drawn");
    requireThat(darkPixels(4, 72, 40, 96) > 4, "Text was not drawn");
}

// Observe actual browser API calls instead of trusting an adapter's GPU flag.
// Full-surface uploads are a targeted detector, not a complete CPU-raster proof:
// managed counters and native tests provide the other side of that boundary.
export function installGpuAudit(realm = globalThis) {
    requireThat(!realm.__wfGpuContractAudit, "GPU audit already installed");
    const totals = { drawCalls: 0, readbacks: 0, presentationReadbacks: 0,
        canvasReadbacks: 0, uploads: 0, uploadedPixels: 0, fullSurfaceUploads: 0,
        unknownUploads: 0, diagnosticLogs: 0, contextLosses: 0, contextRestores: 0, webglErrors: 0 };
    const undo = [];
    const contexts = new Set();
    const resourceKinds = ["Texture", "Framebuffer", "Buffer", "Renderbuffer", "Program", "Shader"];
    const resources = new Map(resourceKinds.map(kind => [kind, new Map()]));
    let targetWidth = Infinity, targetHeight = Infinity, watchedCanvas = null;
    function wrap(proto, name, inspect) {
        if (!proto || typeof proto[name] !== "function") return;
        const descriptor = Object.getOwnPropertyDescriptor(proto, name);
        // WebGL2 inherits some WebGL methods; wrap the defining prototype once.
        if (!descriptor) return;
        const original = descriptor.value;
        Object.defineProperty(proto, name, { ...descriptor, value: function (...args) {
            const result = Reflect.apply(original, this, args);
            inspect.call(this, args, result);
            return result;
        } });
        undo.push(() => Object.defineProperty(proto, name, descriptor));
    }
    function recordUpload(args, subImage) {
        let width, height, data;
        if (args.length >= 9) {
            width = args[subImage ? 4 : 3];
            height = args[subImage ? 5 : 4];
            data = args[8];
        } else {
            data = args[args.length - 1];
            width = data?.width ?? data?.videoWidth;
            height = data?.height ?? data?.videoHeight;
        }
        // null allocates storage without importing CPU pixels. A numeric
        // offset may refer to a PBO, so conservatively count it as transfer.
        if (data === null || data === undefined) return;
        totals.uploads++;
        if (Number.isFinite(width) && Number.isFinite(height)) totals.uploadedPixels += width * height;
        else totals.unknownUploads++;
        if (width >= targetWidth && height >= targetHeight) totals.fullSurfaceUploads++;
    }
    for (const name of ["WebGLRenderingContext", "WebGL2RenderingContext"]) {
        const proto = realm[name]?.prototype;
        for (const method of ["drawArrays", "drawElements", "drawArraysInstanced", "drawElementsInstanced"])
            wrap(proto, method, function () {
                if (!watchedCanvas || this.canvas === watchedCanvas) totals.drawCalls++;
            });
        wrap(proto, "readPixels", function () {
            totals.readbacks++;
            if (!watchedCanvas || this.canvas === watchedCanvas) totals.presentationReadbacks++;
        });
        wrap(proto, "texImage2D", args => recordUpload(args, false));
        wrap(proto, "texSubImage2D", args => recordUpload(args, true));
        wrap(proto, "getError", function (args, result) {
            if (result && !(result === 0x9242 && this.isContextLost?.())) totals.webglErrors++;
        });
        for (const kind of resourceKinds) {
            wrap(proto, "create" + kind, function (args, object) {
                if (object) resources.get(kind).set(object, this);
            });
            wrap(proto, "delete" + kind, function (args) { resources.get(kind).delete(args[0]); });
        }
    }
    for (const name of ["CanvasRenderingContext2D", "OffscreenCanvasRenderingContext2D"])
        wrap(realm[name]?.prototype, "getImageData", () => totals.canvasReadbacks++);
    for (const name of ["HTMLCanvasElement", "OffscreenCanvas"]) {
        wrap(realm[name]?.prototype, "getContext", (args, result) => {
                if ((args[0] === "webgl" || args[0] === "webgl2") && result && !contexts.has(result)) {
                    contexts.add(result);
                    const canvas = result.canvas;
                    if (canvas?.addEventListener) {
                        const lost = () => {
                            totals.contextLosses++;
                            for (const entries of resources.values())
                                for (const [object, owner] of entries) if (owner === result) entries.delete(object);
                        };
                        const restored = () => totals.contextRestores++;
                        canvas.addEventListener("webglcontextlost", lost);
                        canvas.addEventListener("webglcontextrestored", restored);
                        undo.push(() => {
                            canvas.removeEventListener("webglcontextlost", lost);
                            canvas.removeEventListener("webglcontextrestored", restored);
                        });
                    }
                }
        });
    }
    for (const method of ["log", "debug", "info"])
        wrap(realm.console, method, args => {
            if (/\[(Paint|CHROME|DOCK-[^\]]*|GRID-[^\]]*)\]/.test(String(args[0]))) totals.diagnosticLogs++;
        });
    async function eventOnce(canvas, name) {
        return new Promise((resolve, reject) => {
            const listener = event => { clearTimeout(timer); canvas.removeEventListener(name, listener); resolve(event); };
            const timer = setTimeout(() => { canvas.removeEventListener(name, listener); reject(new Error(`Timed out waiting for ${name}`)); }, 10000);
            canvas.addEventListener(name, listener);
        });
    }
    const audit = {
        snapshot: () => ({ ...totals }),
        watchSurface(width, height, canvas = null) {
            requireThat(width > 0 && height > 0, "Invalid watched surface size");
            targetWidth = width; targetHeight = height; watchedCanvas = canvas;
        },
        contextCount: () => Array.from(contexts).filter(gl => !watchedCanvas || gl.canvas === watchedCanvas).length,
        resources: () => Object.fromEntries(Array.from(resources, ([kind, entries]) => [kind, entries.size])),
        assertNoErrors() {
            for (const gl of contexts) {
                if (gl.isContextLost?.()) continue;
                const error = gl.getError();
                requireThat(error === 0, `WebGL error after contract action: ${error}`);
            }
            requireThat(totals.webglErrors === 0, "WebGL errors were consumed by application code");
        },
        async forceContextCycle() {
            const gl = Array.from(contexts).find(context => context.canvas === watchedCanvas);
            const extension = gl?.getExtension("WEBGL_lose_context");
            requireThat(extension, "Context-loss test requires WEBGL_lose_context");
            const lost = eventOnce(watchedCanvas, "webglcontextlost");
            extension.loseContext();
            const event = await lost;
            requireThat(event.defaultPrevented, "App did not opt into context restoration");
            const restored = eventOnce(watchedCanvas, "webglcontextrestored");
            extension.restoreContext();
            await restored;
        },
        restore() { while (undo.length) undo.pop()(); delete realm.__wfGpuContractAudit; }
    };
    realm.__wfGpuContractAudit = audit;
    return audit;
}

const managedKeys = ["controlPaints", "cpuRasterPixels", "readbackBytes", "fallbacks"];
function managedSnapshot(fixture) {
    const values = fixture.counters();
    const result = {};
    for (const key of managedKeys) {
        requireThat(Number.isSafeInteger(values[key]) && values[key] >= 0, `Missing/invalid managed counter: ${key}`);
        result[key] = values[key];
    }
    return result;
}

// Adapter v2 is specified in E:/tmp/winforms-wasm-gpu-test-contract.md.
// Missing adapter means FAILED acceptance, never a skip/pass.
export async function runGpuContracts(probe, audit, { seed = 0x514e7a31 } = {}) {
    requireThat(probe?.version === 2 && typeof probe.createFixture === "function",
        "GPU acceptance blocked: real WinForms browser fixture adapter v2 is missing");
    seededScenario(seed); // Validate trusted driver input before app work.
    requireThat(typeof probe.buildId === "string" && probe.buildId.length > 0, "Fixture build identity is missing");
    requireThat(audit && typeof audit.snapshot === "function", "Install the WebGL audit before app bootstrap");
    const results = [];
    let measurements = [];
    const record = (kind, evidence) => measurements.push({ kind, ...evidence });
    function deviceFrame(observed, managed) {
        assertDeviceFrame(observed, managed);
        record("device-frame", { observed, managed });
    }
    async function check(name, action) {
        const started = performance.now(), before = audit.snapshot();
        measurements = [];
        let timer, timedOut = false;
        try {
            await Promise.race([action(), new Promise((_, reject) => {
                timer = setTimeout(() => { timedOut = true; reject(new Error("Browser case exceeded 20 seconds")); }, 20000);
            })]);
            audit.assertNoErrors();
            results.push({ name, outcome: "passed", evidence: { durationMs: performance.now() - started,
                observed: counterDelta(before, audit.snapshot()), measurements } });
        }
        catch (error) {
            results.push({ name, outcome: "failed", message: String(error.message ?? error) });
            if (timedOut) throw error; // Never overlap later cases with a timed-out action.
        }
        finally { clearTimeout(timer); }
    }
    const fixture = await probe.createFixture({ kind: "procedural-winforms-control", requireGpu: true });
    try {
        requireThat(fixture?.paintSource === "WinForms.OnPaint", "Fixture bypasses real app painting");
        await fixture.warmup(); // Fonts/pipelines/resources before measured frames.
        const initial = fixture.surface();
        requireThat(initial?.surfaceId && Number.isSafeInteger(initial.contentVersion), "Missing surface identity/version");
        requireThat(fixture.canvas, "Fixture must identify its actual presentation canvas");
        audit.watchSurface(initial.width, initial.height, fixture.canvas);
        requireThat(audit.contextCount() > 0, "No audited WebGL context for the actual presentation canvas");

        async function capture() {
            const before = audit.snapshot().presentationReadbacks;
            const pixels = await fixture.capture();
            requireThat(audit.snapshot().presentationReadbacks > before,
                "Correctness capture must read actual pixels from the audited presentation context");
            return pixels;
        }

        await check(GPU_CASES[0], async () => {
            const before = audit.snapshot(), managed = managedSnapshot(fixture);
            await fixture.repaint({ red: [255, 0, 0, 255] });
            deviceFrame(counterDelta(before, audit.snapshot()), counterDelta(managed, managedSnapshot(fixture)));
            // Explicit correctness readback is outside normal-frame counters.
            const pixels = await capture();
            assertFixtureContent(pixels, [255, 0, 0, 255]);
        });

        await check(GPU_CASES[1], async () => {
            const before = audit.snapshot(), managed = managedSnapshot(fixture);
            await fixture.repaint({ red: [0, 255, 0, 255] });
            deviceFrame(counterDelta(before, audit.snapshot()), counterDelta(managed, managedSnapshot(fixture)));
            assertFixtureContent(await capture(), [0, 255, 0, 255]);
        });

        await check(GPU_CASES[2], async () => {
            const surface = fixture.surface(), before = audit.snapshot(), managed = managedSnapshot(fixture);
            await fixture.place({ x: 30, y: 10 });
            const observed = counterDelta(before, audit.snapshot()), delta = counterDelta(managed, managedSnapshot(fixture));
            const after = fixture.surface();
            assertRetainedPlacement(observed, delta, surface, after);
            record("placement", { observed, managed: delta, before: surface, after });
            assertPixels(await capture(), [{ x: 20, y: 20, rgba: [255, 255, 255, 255] },
                { x: 50, y: 30, rgba: [0, 255, 0, 255] }]);
        });

        await check(GPU_CASES[3], async () => {
            const before = audit.snapshot(), managed = managedSnapshot(fixture);
            await fixture.idleTicks(5);
            const delta = counterDelta(before, audit.snapshot());
            requireThat(Object.values(delta).every(n => n === 0), "Idle performed graphics work");
            const managedDelta = counterDelta(managed, managedSnapshot(fixture));
            requireThat(Object.values(managedDelta).every(n => n === 0),
                "Idle performed managed painting/fallback work");
            record("idle", { observed: delta, managed: managedDelta });
        });

        await check(GPU_CASES[4], async () => {
            await fixture.place({ x: 0, y: 0 });
            await fixture.repaint({ red: [0, 0, 255, 255] });
            const before = audit.snapshot(), managed = managedSnapshot(fixture);
            const read = await fixture.readPixel(20, 20);
            requireThat(read?.length === 4 && read.every((v, i) => v === [0, 0, 255, 255][i]),
                "CPU read returned stale pixels");
            const observed = counterDelta(before, audit.snapshot()), delta = counterDelta(managed, managedSnapshot(fixture));
            requireThat(observed.readbacks > 0 && delta.readbackBytes >= 4, "CPU read did not exercise device readback");
            record("cpu-read", { observed, managed: delta, pixel: Array.from(read) });
        });

        await check(GPU_CASES[5], async () => {
            const before = audit.snapshot(), managed = managedSnapshot(fixture);
            await fixture.cpuWritePixel(20, 20, [255, 0, 255, 255]);
            await fixture.compose(); // no re-OnPaint: preserve the mutation.
            record("cpu-write", { observed: counterDelta(before, audit.snapshot()), managed: counterDelta(managed, managedSnapshot(fixture)) });
            assertPixels(await capture(), [{ x: 20, y: 20, rgba: [255, 0, 255, 255] }]);
        });
        await runExtraContracts(probe, fixture, audit,
            { check, capture, assertPixels, assertDeviceFrame: deviceFrame, counterDelta, managedSnapshot, record }, seed);
    } finally { if (typeof fixture?.dispose === "function") await fixture.dispose(); }
    const report = { version: 3, source: "browser-webgl-audit", buildId: probe.buildId, seed,
        webglErrors: audit.snapshot().webglErrors,
        generatedAt: new Date().toISOString(), auditedContexts: audit.contextCount(),
        outcome: results.every(r => r.outcome === "passed") ? "passed" : "failed",
        hardwareAcceleration: "not-certified-by-this-test", results };
    if (report.outcome !== "passed") {
        const error = new Error(results.filter(r => r.outcome === "failed")
            .map(r => `${r.name}: ${r.message}`).join("\n"));
        error.report = report;
        throw error;
    }
    return report;
}
