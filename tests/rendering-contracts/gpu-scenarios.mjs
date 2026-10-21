// The adapter executes these programs through the supplied C# OnPaint fixture.
// Expected pixels are test-owned, independent of the candidate adapter.
const white = [255, 255, 255, 255], red = [255, 0, 0, 255];
const blue = [0, 0, 255, 255], green = [0, 255, 0, 255];
const clear = { op: "clear", color: white };
const fill = (rect, color) => ({ op: "fill", rect, color });
const sample = (x, y, rgba, tolerance = 0) => ({ x, y, rgba, tolerance });

export const DRAWING_SCENARIOS = [
    { name: "source-over preserves translucent color", program: [clear, fill([8, 8, 32, 32], [255, 0, 0, 128])],
        pixels: [sample(20, 20, [255, 127, 127, 255], 1), sample(60, 20, white)] },
    { name: "source-copy transparent pixels remove previous content", program: [clear,
        fill([8, 8, 48, 32], red), { op: "composite", value: "copy" }, fill([24, 8, 16, 32], [0, 0, 0, 0])],
        pixels: [sample(16, 20, red), sample(30, 20, white), sample(48, 20, red)] },
    { name: "excluded clip hole stays untouched", program: [clear, { op: "clip", rect: [8, 8, 64, 40] },
        { op: "exclude", rect: [24, 16, 16, 16] }, fill([0, 0, 128, 96], red)],
        pixels: [sample(16, 20, red), sample(30, 20, white), sample(80, 20, white)] },
    { name: "disjoint clips keep their gap", program: [clear,
        { op: "clipUnion", rects: [[8, 8, 16, 24], [56, 8, 16, 24]] }, fill([0, 0, 128, 96], blue)],
        pixels: [sample(16, 20, blue), sample(40, 20, white), sample(64, 20, blue)] },
    { name: "nested save restore preserves clip and transform", program: [clear, { op: "save" },
        { op: "clip", rect: [8, 8, 64, 40] }, { op: "save" },
        { op: "transform", matrix: [1, 0, 0, 1, 32, 0] }, fill([8, 8, 16, 16], red),
        { op: "restore" }, fill([8, 8, 16, 16], blue), { op: "restore" }, fill([88, 8, 16, 16], green)],
        pixels: [sample(16, 16, blue), sample(48, 16, red), sample(96, 16, green), sample(80, 16, white)] },
    { name: "world clip remains in device space after transform reset", program: [clear,
        { op: "transform", matrix: [1, 0, 0, 1, 20, 10] }, { op: "clip", rect: [0, 0, 16, 16] },
        { op: "transform", matrix: [1, 0, 0, 1, 0, 0] }, fill([0, 0, 80, 60], red)],
        pixels: [sample(24, 14, red), sample(4, 4, white), sample(44, 14, white)] },
    { name: "fractional clip keeps interior and rejects outside", program: [clear,
        { op: "clip", rect: [8.25, 8.25, 24.5, 24.5] }, fill([0, 0, 128, 96], red)],
        pixels: [sample(10, 10, red), sample(4, 10, white), sample(40, 10, white)] },
    { name: "overlapping opaque drawing preserves painter order", program: [clear,
        fill([8, 8, 48, 40], red), fill([24, 8, 48, 40], blue)],
        pixels: [sample(16, 20, red), sample(32, 20, blue), sample(64, 20, blue)] },
    { name: "deferred drawing snapshots a mutable disposed brush", program: [clear, { op: "brushMutation" }],
        pixels: [sample(16, 16, red), sample(48, 16, blue)] },
    { name: "image mutation clone and disposal preserve earlier draws", program: [clear, { op: "imageMutation" }],
        pixels: [sample(16, 16, red), sample(48, 16, blue), sample(80, 16, red)] },
    { name: "clear preserves the established ignore-clip contract", program: [clear,
        { op: "clip", rect: [8, 8, 16, 16] }, { op: "clear", color: green }],
        pixels: [sample(16, 16, green), sample(100, 80, green)] },
    { name: "large offscreen geometry remains clipped on the device", program: [clear,
        { op: "clip", rect: [8, 8, 8, 8] }, fill([-2048, -2048, 4096, 4096], red)],
        pixels: [sample(12, 12, red), sample(20, 20, white), sample(100, 80, white)] }
];

// Repeatable changing colors ensure an adapter cannot reuse a single screenshot.
// Caller owns the seed; a candidate cannot choose its own expected pixels.
export function seededScenario(seed) {
    if (!Number.isSafeInteger(seed) || seed < 1 || seed > 0xffffffff) throw new Error("Invalid scenario seed");
    let state = seed >>> 0;
    const next = () => { state ^= state << 13; state ^= state >>> 17; state ^= state << 5; return state >>> 0; };
    const program = [clear], pixels = [];
    for (let i = 0; i < 24; i++) {
        const x = 4 + (i % 8) * 15, y = 4 + Math.floor(i / 8) * 22;
        const color = [next() & 255, next() & 255, next() & 255, 255];
        program.push(fill([x, y, 10, 12], color));
        pixels.push(sample(x + 4, y + 4, color));
    }
    return { name: "seeded changing rectangle grid", seed, program, pixels };
}

export const LIFECYCLE_CASES = [
    "resize invalidates old surface generation and renders correctly",
    "real context loss restores content and advances device generation",
    "repeated fixture disposal reaches a stable resource plateau"
];

export function assertNewGeneration(old, current) {
    if (!Number.isSafeInteger(old?.generation) || old.generation < 0 ||
        !Number.isSafeInteger(current?.generation) || current.generation <= old.generation)
        throw new Error("Surface generation was missing or stale");
}

export async function runExtraContracts(probe, fixture, audit, helpers, seed) {
    const { check, capture, assertPixels, assertDeviceFrame, counterDelta, managedSnapshot, record } = helpers;
    await fixture.place({ x: 0, y: 0 });
    for (const scenario of [...DRAWING_SCENARIOS, seededScenario(seed)]) {
        await check(scenario.name, async () => {
            const before = audit.snapshot(), managed = managedSnapshot(fixture);
            await fixture.paintProgram(scenario.program);
            assertDeviceFrame(counterDelta(before, audit.snapshot()), counterDelta(managed, managedSnapshot(fixture)));
            assertPixels(await capture(), scenario.pixels);
        });
    }
    await check(LIFECYCLE_CASES[0], async () => {
        const old = fixture.surface();
        await fixture.resize(144, 112);
        const current = fixture.surface();
        assertNewGeneration(old, current);
        if (current.width !== 144 || current.height !== 112)
            throw new Error("Resize reused a stale surface generation");
        if (await fixture.isSurfaceHandleValid(old)) throw new Error("Old surface handle still valid after resize");
        record("resize", { before: old, after: current, oldHandleValid: false });
        await fixture.paintProgram([clear, fill([8, 8, 16, 16], blue)]);
        assertPixels(await capture(), [sample(16, 16, blue), sample(130, 100, white)]);
        await fixture.resize(128, 96);
    });
    await check(LIFECYCLE_CASES[1], async () => {
        await fixture.paintProgram([clear, fill([8, 8, 16, 16], green)]);
        const old = fixture.surface();
        await audit.forceContextCycle();
        await fixture.waitForRecovery();
        const restored = fixture.surface();
        assertNewGeneration(old, restored);
        if (await fixture.isSurfaceHandleValid(old))
            throw new Error("Context restore retained stale resource handles");
        record("context-recovery", { before: old, after: restored, oldHandleValid: false });
        assertPixels(await capture(), [sample(16, 16, green), sample(100, 80, white)]);
    });
    await check(LIFECYCLE_CASES[2], async () => {
        // Warm backend/font caches before the plateau comparison. The outer
        // fixture remains alive; each sibling must explicitly dispose itself.
        async function cycle() {
            const sibling = await probe.createFixture({ kind: "procedural-winforms-control", requireGpu: true });
            try {
                if (sibling.paintSource !== "WinForms.OnPaint") throw new Error("Sibling fixture bypasses real app painting");
                await sibling.warmup();
            } finally { await sibling.dispose(); }
            await probe.drainDisposals();
        }
        await cycle(); await cycle();
        const before = audit.resources();
        for (let i = 0; i < 10; i++) await cycle();
        const after = audit.resources();
        for (const key of Object.keys(before))
            if (after[key] > before[key]) throw new Error(`GPU ${key} grows across disposed fixture cycles: ${before[key]} -> ${after[key]}`);
        record("resource-plateau", { before, after, cycles: 10 });
    });
}
