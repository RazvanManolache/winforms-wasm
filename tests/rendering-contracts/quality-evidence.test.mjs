// Synthetic inputs test the gates; they are never browser acceptance receipts.
import test from "node:test";
import assert from "node:assert/strict";
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { createHash } from "node:crypto";
import { verifyQualityEvidence, summarizeLatency, verifyArtifactInventory, REQUIRED_ARTIFACT_KINDS } from "./quality-evidence.mjs";
import { checkedFile, verifyNativeResults, verifyErrorInventories } from "./verify-quality.mjs";
import { DRAWING_SCENARIOS, seededScenario, assertNewGeneration } from "./gpu-scenarios.mjs";
import { GPU_CASES, installGpuAudit, assertDeviceFrame, runGpuContracts } from "./gpu-contracts.mjs";
import { verifyGpuReport } from "./verify-gpu-report.mjs";
import { syntheticGpuReport } from './synthetic-report-fixture.mjs';

function fixture() {
    const samples = latency => Array.from({ length: 120 }, (_, i) => ({ inputId: `event-${i}`,
        responded: true, inputMs: i * 100, visibleMs: i * 100 + latency,
        clock: "browser-monotonic", evidenceKind: "browser-trace+pixel-change" }));
    const environment = { browserVersion: "test-browser-1", gpuId: "test-gpu-1", viewport: [160, 128],
        dpr: 1, appHash: "a".repeat(64), sceneHash: "b".repeat(64), configuration: "Release" };
    const run = (buildId, latency) => ({ buildId, environment: structuredClone(environment), diagnosticsEnabled: false,
        measurement: "input-to-visible", newClientErrors: 0, newConsoleErrors: 0, webglErrors: 0,
        workloads: { click: { expectedInputs: 120, samples: samples(latency) }, drag: { expectedInputs: 120, samples: samples(latency) } },
        loading: Object.fromEntries(["cold", "httpWarm", "inSession"].map(name => [name, { firstUsableMs: 100, transferredBytes: 1000 }])),
        dynamicLoading: { separateAppAssemblies: true, wholeFolderPreload: false, staticallyBuiltApp: false } });
    return { evidence: { baseline: run("baseline", 40), candidate: run("candidate/run-1", 20),
        hardware: { accelerated: true, renderer: "test physical GPU" } },
        policy: { version: 1, calibrated: true, minimumSamples: 120, maxP95Ratio: 0.9, maxLoadRatio: 1.1,
            workloads: { click: { p95Ms: 30, maxMs: 60 }, drag: { p95Ms: 30, maxMs: 60 } } } };
}

test("raw sample comparison reports p50 p95 and worst response", () => {
    const { evidence, policy } = fixture();
    assert.deepEqual(verifyQualityEvidence(evidence, policy, "candidate/run-1").click.candidate,
        { count: 120, p50Ms: 20, p95Ms: 20, maxMs: 20 });
});

const mutations = [
    ["uncalibrated policy", (e, p) => p.calibrated = false],
    ["too few samples", (e, p) => p.minimumSamples = 10],
    ["no required improvement", (e, p) => p.maxP95Ratio = 1],
    ["wrong build", e => e.candidate.buildId = "old-build"],
    ["software GPU", e => e.hardware.renderer = "Google SwiftShader"],
    ["unverified GPU", e => e.hardware.accelerated = false],
    ["different viewport", e => e.candidate.environment.viewport = [320, 256]],
    ["different app", e => e.candidate.environment.appHash = "other-app"],
    ["different browser", e => e.candidate.environment.browserVersion = "other-version"],
    ["different build configuration", e => e.candidate.environment.configuration = "Debug"],
    ["diagnostic overhead", e => e.candidate.diagnosticsEnabled = true],
    ["bridge completion posing as response", e => e.candidate.measurement = "bridge-complete"],
    ["new console error", e => e.candidate.newConsoleErrors = 1],
    ["new client error", e => e.candidate.newClientErrors = 1],
    ["new WebGL error", e => e.candidate.webglErrors = 1],
    ["missing workload", e => delete e.candidate.workloads.drag],
    ["missing response", e => e.candidate.workloads.click.samples[0].responded = false],
    ["dropped slow sample", e => e.candidate.workloads.click.samples.pop()],
    ["replayed duplicate event", e => e.candidate.workloads.click.samples[1].inputId = "event-0"],
    ["mixed clocks", e => e.candidate.workloads.click.samples[0].clock = "managed-stopwatch"],
    ["RAF proxy posing as presentation", e => e.candidate.workloads.click.samples[0].evidenceKind = "next-raf"],
    ["negative duration", e => e.candidate.workloads.click.samples[0].visibleMs = -1],
    ["NaN duration", e => e.candidate.workloads.click.samples[0].visibleMs = NaN],
    ["one huge tail hidden by average", e => e.candidate.workloads.click.samples[0].visibleMs += 1000],
    ["slow p95 hidden by fast median", e => { for (let i = 0; i < 8; i++) e.candidate.workloads.drag.samples[i].visibleMs += 30; }],
    ["regressed startup", e => e.candidate.loading.cold.firstUsableMs = 1000],
    ["missing warm startup", e => delete e.candidate.loading.httpWarm],
    ["missing byte measurement", e => delete e.candidate.loading.cold.transferredBytes],
    ["static app build", e => e.candidate.dynamicLoading.staticallyBuiltApp = true],
    ["whole DLL folder preload", e => e.candidate.dynamicLoading.wholeFolderPreload = true],
    ["missing absolute budget", (e, p) => p.workloads.drag.p95Ms = null],
    ["unbounded loading regression", (e, p) => p.maxLoadRatio = 100]
];
for (const [name, mutate] of mutations) test(`quality gate rejects ${name}`, () => {
    const { evidence, policy } = fixture(); mutate(evidence, policy);
    assert.throws(() => verifyQualityEvidence(evidence, policy, "candidate/run-1"));
});

test("percentile calculation uses raw samples and nearest rank", () => {
    const { evidence } = fixture(), samples = evidence.candidate.workloads.click.samples;
    samples[119].visibleMs += 40;
    assert.deepEqual(summarizeLatency(samples, 120, 120), { count: 120, p50Ms: 20, p95Ms: 20, maxMs: 60 });
});

test("artifact omissions, duplicate paths and missing hashes fail", () => {
    const artifacts = REQUIRED_ARTIFACT_KINDS.map(kind => ({ kind, path: kind, sha256: "a".repeat(64) }));
    verifyArtifactInventory(artifacts);
    assert.throws(() => verifyArtifactInventory(artifacts.slice(1)), /Missing evidence/);
    assert.throws(() => verifyArtifactInventory([...artifacts, artifacts[0]]), /duplicate/);
    assert.throws(() => verifyArtifactInventory(artifacts.map((a, i) => i ? a : { ...a, sha256: "" })), /SHA256/);
});

test("artifact files cannot escape roots or silently change after hashing", () => {
    const scratch = join(tmpdir(), "wfwasm-quality-gate-self-tests"), root = join(scratch, "allowed");
    mkdirSync(root, { recursive: true });
    writeFileSync(join(root, "evidence.txt"), "test evidence");
    writeFileSync(join(scratch, "outside.txt"), "outside");
    const hash = createHash("sha256").update("test evidence").digest("hex");
    assert.equal(checkedFile(root, "evidence.txt", hash).toString(), "test evidence");
    assert.throws(() => checkedFile(root, "../outside.txt", hash), /escapes/);
    assert.throws(() => checkedFile(root, "evidence.txt", "0".repeat(64)), /hash changed/);
});

function nativeTrx() {
    const cases = Object.entries({ RasterWorkBoundsTests: 26, OptionalPaintDiagnosticsTests: 5,
        GpuRenderingPolicyTests: 2, GpuContractFixtureTests: 6, ArcConnectionTests: 6, GradientAlphaTests: 3,
        DisplayGraphicsContractTests: 12, PaintFailureReportingTests: 3 })
        .flatMap(([name, n]) => Array.from({ length: n }, (_, i) => `<UnitTestResult testName="${name}.case${i}" outcome="Passed" />`));
    return `<TestRun>${cases.join("")}<Counters total="${cases.length}" passed="${cases.length}" /></TestRun>`;
}
test("native receipts require all critical suites with no failed or skipped tests", () => {
    const trx = nativeTrx(); verifyNativeResults([trx]);
    assert.throws(() => verifyNativeResults([trx.replace('outcome="Passed"', 'outcome="Failed"')]), /failed/);
    assert.throws(() => verifyNativeResults([trx.replace('outcome="Passed"', 'outcome="NotExecuted"')]), /skipped/);
    assert.throws(() => verifyNativeResults([trx.replaceAll("GpuRenderingPolicyTests", "UnrelatedTests")]), /coverage missing/);
    assert.throws(() => verifyNativeResults([trx, trx]), /duplicate/);
});

for (const suite of ["DisplayGraphicsContractTests", "PaintFailureReportingTests"])
    test("native acceptance cannot omit " + suite, () => {
        assert.throws(() => verifyNativeResults([nativeTrx().replaceAll(suite, "UnrelatedCoverage")]), /coverage missing/);
    });

test("drawing scenario seeds are reproducible but change expected output", () => {
    assert.deepEqual(seededScenario(42), seededScenario(42));
    assert.notDeepEqual(seededScenario(42).pixels, seededScenario(43).pixels);
    assert.equal(seededScenario(42).pixels.length, 24);
    assert.throws(() => seededScenario(0));
    assert.equal(new Set(GPU_CASES).size, GPU_CASES.length);
    assert.equal(DRAWING_SCENARIOS.length, 12);
    assert.equal(GPU_CASES.length, 22);
});

test("resource audit sees native creates deletes and unknown uploads", () => {
    class GL { createTexture() { return {}; } deleteTexture() {} texImage2D() {} }
    const audit = installGpuAudit({ WebGLRenderingContext: GL });
    try {
        const gl = new GL(), texture = gl.createTexture();
        assert.equal(audit.resources().Texture, 1);
        gl.deleteTexture(texture);
        assert.equal(audit.resources().Texture, 0);
        gl.texImage2D(0, 0, 0, 0, 0, {});
        assert.equal(audit.snapshot().unknownUploads, 1);
    } finally { audit.restore(); }
});

test("old receipt format, missing observations and wrong seed cannot pass", () => {
    const report = syntheticGpuReport();
    verifyGpuReport(report, "build", 42);
    assert.throws(() => verifyGpuReport({ ...report, version: 1 }, "build", 42));
    assert.throws(() => verifyGpuReport(report, "build", 43), /seed/);
    assert.throws(() => verifyGpuReport({ ...report, results: report.results.map(r => ({ ...r, evidence: null })) }, "build", 42), /evidence/);
});

test("device frame cannot hide unknown uploads or routine diagnostic logs", () => {
    const observed = { drawCalls: 1, readbacks: 0, canvasReadbacks: 0, fullSurfaceUploads: 0,
        uploadedPixels: 0, unknownUploads: 0, diagnosticLogs: 0 };
    const managed = { controlPaints: 1, cpuRasterPixels: 0, readbackBytes: 0, fallbacks: 0 };
    assert.throws(() => assertDeviceFrame({ ...observed, unknownUploads: 1 }, managed));
    assert.throws(() => assertDeviceFrame({ ...observed, diagnosticLogs: 1 }, managed));
});

test("surface generations cannot be missing or silently reused", () => {
    assertNewGeneration({ generation: 1 }, { generation: 2 });
    assert.throws(() => assertNewGeneration({}, {}));
    assert.throws(() => assertNewGeneration({ generation: 1 }, { generation: 1 }));
    assert.throws(() => assertNewGeneration({ generation: 1 }, { generation: NaN }));
});

test("actual context events are observed and live resources invalidated", async () => {
    class Canvas extends EventTarget { getContext() { return new GL(this); } }
    class GL {
        constructor(canvas) { this.canvas = canvas; }
        createTexture() { return {}; }
        getExtension() { return {
            loseContext: () => queueMicrotask(() => this.canvas.dispatchEvent(new Event("webglcontextlost", { cancelable: true }))),
            restoreContext: () => queueMicrotask(() => this.canvas.dispatchEvent(new Event("webglcontextrestored")))
        }; }
    }
    const audit = installGpuAudit({ WebGLRenderingContext: GL, HTMLCanvasElement: Canvas });
    try {
        const canvas = new Canvas(), gl = canvas.getContext("webgl");
        canvas.addEventListener("webglcontextlost", event => event.preventDefault());
        audit.watchSurface(16, 16, canvas);
        gl.createTexture();
        await audit.forceContextCycle();
        assert.equal(audit.snapshot().contextLosses, 1);
        assert.equal(audit.snapshot().contextRestores, 1);
        assert.equal(audit.resources().Texture, 0);
    } finally { audit.restore(); }
});

test("rejected browser fixture is disposed instead of leaked", async () => {
    let disposed = false;
    const probe = { version: 2, buildId: "build", async createFixture() {
        return { paintSource: "fake-js-picture", async dispose() { disposed = true; } };
    } };
    await assert.rejects(() => runGpuContracts(probe, { snapshot() { return {}; } }), /bypasses/);
    assert.equal(disposed, true);
});

test("error inventories cannot ignore repeats or hide a reset", () => {
    const before = { entries: [{ signature: "old", count: 2 }] };
    verifyErrorInventories(before, before);
    assert.throws(() => verifyErrorInventories(before, { entries: [{ signature: "old", count: 3 }] }), /New/);
    assert.throws(() => verifyErrorInventories(before, { entries: [] }), /reset/);
    assert.throws(() => verifyErrorInventories(before, { entries: [...before.entries, { signature: "new", count: 1 }] }), /New/);
});

test("application code cannot consume a GL error to hide it from acceptance", () => {
    class GL { getError() { return this.error ?? 0; } isContextLost() { return false; } }
    const audit = installGpuAudit({ WebGLRenderingContext: GL });
    try {
        const gl = new GL(); gl.error = 0x0502;
        gl.getError(); gl.error = 0;
        assert.equal(audit.snapshot().webglErrors, 1);
        assert.throws(() => audit.assertNoErrors(), /consumed/);
    } finally { audit.restore(); }
});
