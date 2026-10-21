// These validate the enforcement harness, not a GPU implementation.
import test from "node:test";
import assert from "node:assert/strict";
import { assertDeviceFrame, assertRetainedPlacement, assertPixels, assertFixtureContent,
    counterDelta, installGpuAudit, runGpuContracts, GPU_CASES } from "./gpu-contracts.mjs";
import { verifyGpuReport } from "./verify-gpu-report.mjs";
import { syntheticGpuReport } from './synthetic-report-fixture.mjs';

const validFrame = () => ({ drawCalls: 4, readbacks: 0, canvasReadbacks: 0,
    fullSurfaceUploads: 0, uploads: 0, uploadedPixels: 0, unknownUploads: 0, diagnosticLogs: 0 });
const validManaged = () => ({ controlPaints: 1, cpuRasterPixels: 0, readbackBytes: 0, fallbacks: 0 });

test("clean device observation is accepted", () => assertDeviceFrame(validFrame(), validManaged()));
for (const [name, change, error] of [
    ["backend label without draws", { drawCalls: 0 }, /No WebGL/],
    ["synchronous GPU readback", { readbacks: 1 }, /read pixels back/],
    ["Canvas2D readback", { canvasReadbacks: 1 }, /Canvas2D/],
    ["CPU-painted surface upload", { fullSurfaceUploads: 1 }, /CPU-painted/],
    ["CPU upload split into tiles", { uploadedPixels: 4096 }, /tiled CPU/]
]) test(`rejects ${name}`, () => assert.throws(() => assertDeviceFrame({ ...validFrame(), ...change }, validManaged()), error));

for (const [name, change, error] of [
    ["bypassed OnPaint", { controlPaints: 0 }, /OnPaint/],
    ["CPU rasterization", { cpuRasterPixels: 1024 }, /CPU rasterization/],
    ["managed readback", { readbackBytes: 4 }, /read back/],
    ["silent fallback", { fallbacks: 1 }, /fell back/]
]) test(`rejects ${name}`, () => assert.throws(() => assertDeviceFrame(validFrame(), { ...validManaged(), ...change }), error));

test("counter resets and missing values fail rather than hiding work", () => {
    assert.throws(() => counterDelta({ readbacks: 3 }, { readbacks: 0 }), /reset/);
    assert.throws(() => counterDelta({ readbacks: 0 }, {}), /Invalid/);
    assert.deepEqual(counterDelta({ drawCalls: 2 }, { drawCalls: 5 }), { drawCalls: 3 });
});

test("placement rejects repaint, resource replacement and pixel transfers", () => {
    const idle = { controlPaints: 0, cpuRasterPixels: 0, readbackBytes: 0, fallbacks: 0 };
    const surface = { surfaceId: "s1", contentVersion: 4 };
    assertRetainedPlacement(validFrame(), idle, surface, surface);
    assert.throws(() => assertRetainedPlacement(validFrame(), validManaged(), surface, surface), /repainted/);
    assert.throws(() => assertRetainedPlacement(validFrame(), idle, surface, { ...surface, surfaceId: "s2" }), /replaced/);
    assert.throws(() => assertRetainedPlacement({ ...validFrame(), uploads: 1 }, idle, surface, surface), /transferred/);
});

test("blank and stale output cannot pass the pixel contract", () => {
    const samples = [{ x: 0, y: 0, rgba: [255, 0, 0, 255] }];
    assert.throws(() => assertPixels({ width: 1, height: 1, rgba: new Uint8Array(4) }, samples), /mismatch/);
    assertPixels({ width: 1, height: 1, rgba: new Uint8Array([255, 0, 0, 255]) }, samples);
    assert.throws(() => assertPixels({ width: 1, height: 1, rgba: [] }, samples), /Invalid/);
});

function syntheticFixture() {
    const frame = { width: 160, height: 128, rgba: new Uint8Array(160 * 128 * 4).fill(255) };
    const set = (x, y, rgba) => frame.rgba.set(rgba, (y * frame.width + x) * 4);
    const red = [255, 0, 0, 255], black = [0, 0, 0, 255];
    set(20, 20, red); set(76, 56, red);
    set(65, 20, red); set(110, 20, [0, 0, 255, 255]);
    set(100, 44, red); set(101, 44, [0, 0, 255, 255]); set(100, 45, [0, 255, 0, 255]);
    for (let x = 64; x < 70; x++) set(x, 44, black);
    for (let x = 4; x < 10; x++) set(x, 73, black);
    return { frame, set };
}

test("fixture validator checks all drawing categories, not just a rectangle", () => {
    assertFixtureContent(syntheticFixture().frame, [255, 0, 0, 255]);
});
for (const category of ["gradient", "path", "text", "texture"]) {
    test(`fixture validator rejects missing ${category}`, () => {
        const { frame, set } = syntheticFixture(), white = [255, 255, 255, 255];
        if (category === "gradient") set(65, 20, [128, 0, 128, 255]);
        if (category === "path") for (let x = 64; x < 70; x++) set(x, 44, white);
        if (category === "text") for (let x = 4; x < 10; x++) set(x, 73, white);
        if (category === "texture") set(101, 44, white);
        assert.throws(() => assertFixtureContent(frame, [255, 0, 0, 255]));
    });
}

test("real browser fixture adapter is mandatory, never a skipped acceptance", async () => {
    await assert.rejects(() => runGpuContracts(undefined, {}), /adapter v2 is missing/);
});

test("audit wraps and restores calls; null allocations are not pixel uploads", () => {
    class GL {
        drawArrays() {}
        readPixels() {}
        texImage2D() {}
        texSubImage2D() {}
    }
    class Canvas { getContext() { return new GL(); } }
    class C2D { getImageData() { return {}; } }
    const realm = { WebGLRenderingContext: GL, HTMLCanvasElement: Canvas, CanvasRenderingContext2D: C2D };
    const original = GL.prototype.drawArrays;
    const audit = installGpuAudit(realm);
    try {
        audit.watchSurface(16, 16);
        const gl = new Canvas().getContext("webgl");
        gl.drawArrays();
        gl.texImage2D(0, 0, 0, 16, 16, 0, 0, 0, null);
        assert.equal(audit.snapshot().uploads, 0);
        gl.texImage2D(0, 0, 0, 16, 16, 0, 0, 0, new Uint8Array(1024));
        gl.texSubImage2D(0, 0, 0, 0, 8, 8, 0, 0, new Uint8Array(256));
        gl.readPixels();
        new C2D().getImageData();
        assert.equal(audit.contextCount(), 1);
        assert.deepEqual(audit.snapshot(), { drawCalls: 1, readbacks: 1, presentationReadbacks: 1, canvasReadbacks: 1,
            uploads: 2, uploadedPixels: 320, fullSurfaceUploads: 1, unknownUploads: 0,
            diagnosticLogs: 0, contextLosses: 0, contextRestores: 0, webglErrors: 0 });
    } finally { audit.restore(); }
    assert.equal(GL.prototype.drawArrays, original);
});

test("unrelated WebGL effects do not count as presentation drawing", () => {
    class GL { constructor(canvas) { this.canvas = canvas; } drawArrays() {} readPixels() {} }
    class Canvas { getContext() { return new GL(this); } }
    const realm = { WebGLRenderingContext: GL, HTMLCanvasElement: Canvas };
    const audit = installGpuAudit(realm);
    try {
        const display = new Canvas(), effects = new Canvas();
        const displayGl = display.getContext("webgl"), effectGl = effects.getContext("webgl");
        audit.watchSurface(16, 16, display);
        effectGl.drawArrays();
        effectGl.readPixels();
        assert.equal(audit.snapshot().drawCalls, 0);
        assert.equal(audit.snapshot().presentationReadbacks, 0);
        assert.equal(audit.snapshot().readbacks, 1);
        displayGl.drawArrays();
        assert.equal(audit.snapshot().drawCalls, 1);
        assert.equal(audit.contextCount(), 1);
    } finally { audit.restore(); }
});

test("CI rejects missing, stale, skipped and incomplete browser acceptance", () => {
    const report = syntheticGpuReport('build-42/run-7');
    assert.equal(verifyGpuReport(report, "build-42/run-7").renderingBehavior, "passed");
    assert.throws(() => verifyGpuReport(null, "build-42/run-7"), /Missing real browser/);
    assert.throws(() => verifyGpuReport(report, "build-43/run-8"), /different build/);
    assert.throws(() => verifyGpuReport({ ...report, results: [] }, "build-42/run-7"), /missing/);
    assert.throws(() => verifyGpuReport({ ...report, results: report.results.map((r, i) =>
        i === 0 ? { ...r, outcome: "skipped" } : r) }, "build-42/run-7"), /skipped/);
});

test('runner preserves normal-frame counters separately from correctness capture', async () => {
    // Deliberately incomplete in-memory adapter: this tests report transport,
    // not rendering. The overall run must fail; no browser receipt is emitted.
    const {frame,set}=syntheticFixture();
    const totals={drawCalls:0,readbacks:0,presentationReadbacks:0,canvasReadbacks:0,uploads:0,uploadedPixels:0,
        fullSurfaceUploads:0,unknownUploads:0,diagnosticLogs:0,contextLosses:0,contextRestores:0,webglErrors:0};
    const managed={controlPaints:0,cpuRasterPixels:0,readbackBytes:0,fallbacks:0};
    const fixture={paintSource:'WinForms.OnPaint',canvas:{},
        surface:()=>({surfaceId:'unit-only',contentVersion:1,generation:1,width:128,height:96}),
        counters:()=>({...managed}),async warmup(){},async dispose(){},async place(){totals.drawCalls++;},async idleTicks(){},
        async repaint({red}){totals.drawCalls++;managed.controlPaints++;set(20,20,red);set(76,56,red);},
        async capture(){totals.readbacks++;totals.presentationReadbacks++;return frame;},
        async readPixel(){totals.readbacks++;managed.readbackBytes+=4;return [0,0,255,255];},
        async cpuWritePixel(x,y,color){set(x,y,color);},async compose(){totals.drawCalls++;}};
    const audit={snapshot:()=>({...totals}),watchSurface(){},contextCount:()=>1,assertNoErrors(){},
        resources:()=>({Texture:0,Framebuffer:0,Buffer:0,Renderbuffer:0,Program:0,Shader:0})};
    try {
        await runGpuContracts({version:2,buildId:'unit-only',createFixture:async()=>fixture,async drainDisposals(){}},audit,{seed:42});
        assert.fail('Incomplete adapter must never pass acceptance');
    } catch(error) {
        assert.equal(error.report?.version,3);
        assert.equal(error.report.outcome,'failed');
        const first=error.report.results[0];assert.equal(first.outcome,'passed');
        assert.equal(first.evidence.observed.presentationReadbacks,1);
        assert.equal(first.evidence.measurements[0].observed.readbacks,0);
        assert.equal(first.evidence.measurements[0].managed.controlPaints,1);
        assert.throws(()=>verifyGpuReport(error.report,'unit-only',42),/did not pass/);
    }
});
