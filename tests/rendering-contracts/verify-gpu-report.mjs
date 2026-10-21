// CI gate: helper unit tests alone cannot certify the rendering acceptance.
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { GPU_CASES, assertDeviceFrame, assertRetainedPlacement } from "./gpu-contracts.mjs";
import { assertNewGeneration } from "./gpu-scenarios.mjs";

const observedKeys = ['drawCalls','readbacks','presentationReadbacks','canvasReadbacks','uploads','uploadedPixels',
    'fullSurfaceUploads','unknownUploads','diagnosticLogs','contextLosses','contextRestores','webglErrors'];
const managedKeys = ['controlPaints','cpuRasterPixels','readbackBytes','fallbacks'];
const resourceKeys = ['Texture','Framebuffer','Buffer','Renderbuffer','Program','Shader'];
function demand(condition, message) { if (!condition) throw new Error(message); }
function counters(values, keys) {
    demand(values && keys.every(key => Number.isSafeInteger(values[key]) && values[key] >= 0), 'Missing measured counter evidence');
    if (keys === observedKeys) demand(values.presentationReadbacks <= values.readbacks &&
        values.fullSurfaceUploads <= values.uploads && values.unknownUploads <= values.uploads,
        'Contradictory measured counter evidence');
}
function surface(value) {
    demand(typeof value?.surfaceId === 'string' && value.surfaceId.length > 0 &&
        ['contentVersion','generation','width','height'].every(key => Number.isSafeInteger(value[key]) && value[key] >= 0) &&
        value.width > 0 && value.height > 0, 'Missing surface identity/generation evidence');
}
function verifyMeasurement(result, index) {
    const kind = index === 2 ? 'placement' : index === 3 ? 'idle' : index === 4 ? 'cpu-read' : index === 5 ? 'cpu-write' :
        index === 19 ? 'resize' : index === 20 ? 'context-recovery' : index === 21 ? 'resource-plateau' : 'device-frame';
    const measurements = result.evidence.measurements;
    demand(Array.isArray(measurements) && measurements.length === 1 && measurements[0].kind === kind,
        `Missing measured ${kind} evidence: ${result.name}`);
    const m = measurements[0];
    if (['device-frame','placement','idle','cpu-read','cpu-write'].includes(kind)) {
        counters(m.observed, observedKeys); counters(m.managed, managedKeys);
        demand(observedKeys.every(key => m.observed[key] <= result.evidence.observed[key]), 'Frame counters exceed whole-case counters');
        demand(m.observed.webglErrors === 0 && m.observed.diagnosticLogs === 0, 'Errors/diagnostics during measured drawing');
    }
    if (kind === 'device-frame') assertDeviceFrame(m.observed, m.managed);
    if (kind === 'placement') {
        surface(m.before); surface(m.after);
        assertRetainedPlacement(m.observed, m.managed, m.before, m.after);
    }
    if (kind === 'idle') demand([...observedKeys.map(key => m.observed[key]), ...managedKeys.map(key => m.managed[key])]
        .every(value => value === 0) && observedKeys.every(key => result.evidence.observed[key] === 0), 'Idle performed graphics work');
    if (kind === 'cpu-read') {
        demand(m.observed.readbacks > 0 && m.managed.readbackBytes >= 4 && m.managed.fallbacks === 0 &&
            m.managed.controlPaints === 0 && m.managed.cpuRasterPixels === 0 &&
            Array.isArray(m.pixel) && m.pixel.length === 4 && m.pixel.every((v,i) => v === [0,0,255,255][i]),
            'CPU read lacks actual device readback or returned stale pixels');
    }
    if (kind === 'cpu-write') {
        demand(m.observed.drawCalls > 0 && m.observed.readbacks === 0 && m.observed.canvasReadbacks === 0 &&
            m.observed.fullSurfaceUploads === 0 && m.observed.unknownUploads === 0 && m.observed.uploadedPixels <= 1024 &&
            managedKeys.every(key => m.managed[key] === 0), 'CPU mutation repainted, read back, fell back, or did not compose');
    }
    if (['resize','context-recovery'].includes(kind)) {
        surface(m.before); surface(m.after); assertNewGeneration(m.before, m.after);
        demand(m.oldHandleValid === false, 'Old surface handle remains valid');
        if (kind === 'resize') demand(m.after.width === 144 && m.after.height === 112, 'Resize kept stale dimensions');
    }
    if (kind === 'resource-plateau') {
        counters(m.before, resourceKeys); counters(m.after, resourceKeys);
        demand(Number.isSafeInteger(m.cycles) && m.cycles >= 10 && resourceKeys.some(key => m.before[key] > 0) &&
            resourceKeys.every(key => m.after[key] <= m.before[key]), 'Missing disposal cycles or growing/unaudited GPU resources');
    }
}

export function verifyGpuReport(report, expectedBuild, expectedSeed) {
    function check(condition, message) { if (!condition) throw new Error(message); }
    check(typeof expectedBuild === "string" && expectedBuild.length > 0, "Expected build/run identity is required");
    check(report?.version === 3 && report.source === "browser-webgl-audit", "Missing real browser audit report v3");
    check(report.buildId === expectedBuild, "Browser report belongs to a different build/run");
    check(Number.isFinite(Date.parse(report.generatedAt)), "Report timestamp is missing");
    const age = Date.now() - Date.parse(report.generatedAt);
    check(age >= -120000 && age <= 3600000, 'Browser report is stale or from the future');
    check(Number.isSafeInteger(report.auditedContexts) && report.auditedContexts > 0, "No audited presentation context");
    check(Number.isSafeInteger(report.seed) && report.seed > 0 && report.seed <= 0xffffffff, "Randomized case seed is missing");
    if (expectedSeed !== undefined) check(report.seed === expectedSeed, "Report used a different test seed");
    check(report.outcome === "passed", "Browser rendering contracts did not pass");
    check(report.webglErrors === 0, "Browser observed WebGL errors");
    check(Array.isArray(report.results) && report.results.length === GPU_CASES.length, "Required browser cases are missing");
    for (const [index, name] of GPU_CASES.entries()) {
        const matches = report.results.filter(result => result.name === name);
        check(matches.length === 1 && matches[0].outcome === "passed", `Browser case missing/failed/skipped: ${name}`);
        const evidence = matches[0].evidence;
        check(Number.isFinite(evidence?.durationMs) && evidence.durationMs >= 0, `Missing measured case evidence: ${name}`);
        counters(evidence.observed, observedKeys);
        if (![3,4,21].includes(index)) check(evidence.observed.presentationReadbacks > 0,
            `No audited correctness pixel capture: ${name}`);
        verifyMeasurement(matches[0], index);
    }
    const recovery = report.results.find(result => result.name.startsWith("real context loss")).evidence.observed;
    check(recovery.contextLosses > 0 && recovery.contextRestores > 0, "No actual context-loss/restoration events observed");
    return { renderingBehavior: "passed", hardwareAcceleration: "requires-separate-hardware-evidence" };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
    try {
        const [, , reportPath, expectedBuild, seed] = process.argv;
        if (!reportPath || !seed) throw new Error("Usage: node verify-gpu-report.mjs <browser-report.json> <expected-build-run-id> <driver-seed>");
        const report = JSON.parse(readFileSync(reportPath, "utf8"));
        console.log(JSON.stringify(verifyGpuReport(report, expectedBuild, Number(seed))));
    } catch (error) {
        console.error("GPU rendering acceptance FAILED: " + error.message);
        process.exitCode = 1;
    }
}
