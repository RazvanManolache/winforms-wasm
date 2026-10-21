import { readFileSync, realpathSync, statSync } from "node:fs";
import { resolve, relative, isAbsolute } from "node:path";
import { pathToFileURL } from "node:url";
import { createHash } from "node:crypto";
import { verifyGpuReport } from "./verify-gpu-report.mjs";
import { verifyQualityEvidence, verifyArtifactInventory } from "./quality-evidence.mjs";

export function checkedFile(root, path, hash) {
    if (typeof path !== "string" || isAbsolute(path) || path.includes(":") || path.includes("\\"))
        throw new Error("Evidence path must be relative and use forward slashes");
    const base = realpathSync(root), candidate = realpathSync(resolve(base, path));
    const location = relative(base, candidate);
    if (location === ".." || location.startsWith("../") || location.startsWith("..\\") || isAbsolute(location))
        throw new Error("Evidence path escapes its root");
    if (!statSync(candidate).isFile()) throw new Error("Evidence path is not a file");
    const content = readFileSync(candidate);
    if (!content.length || createHash("sha256").update(content).digest("hex") !== hash?.toLowerCase())
        throw new Error("Evidence file is empty or its hash changed: " + path);
    return content;
}

export function verifyFiles(report, artifactRoot, pinnedTests, testRoot) {
    verifyArtifactInventory(report.artifacts);
    const native = [], inventories = {};
    for (const artifact of report.artifacts) {
        const content = checkedFile(artifactRoot, artifact.path, artifact.sha256);
        if (artifact.kind === "native-tests") native.push(content.toString("utf8"));
        if (["client-errors-before", "client-errors-after"].includes(artifact.kind)) inventories[artifact.kind] = JSON.parse(content);
        if (artifact.kind === "browser-console") {
            const console = JSON.parse(content);
            if (!Array.isArray(console.entries) || console.entries.some(entry =>
                !["debug", "info", "log", "warning", "warn"].includes(entry.level)))
                throw new Error("Console evidence is missing or contains errors");
        }
        if (artifact.kind === "gpu-diagnostics") {
            const gpu = JSON.parse(content);
            if (gpu.hardwareAccelerated !== true || gpu.renderer !== report.hardware?.renderer)
                throw new Error("GPU diagnostics do not confirm the reported hardware");
        }
        if (artifact.kind === "screenshots" && (content.length < 32 ||
            content.subarray(0, 8).toString("hex") !== "89504e470d0a1a0a" ||
            content.readUInt32BE(16) < 128 || content.readUInt32BE(20) < 96))
            throw new Error("Screenshot evidence is not a full-sized PNG");
    }
    verifyErrorInventories(inventories["client-errors-before"], inventories["client-errors-after"]);
    verifyNativeResults(native);
    if (pinnedTests?.version !== 1 || !Array.isArray(pinnedTests.files) || pinnedTests.files.length < 5)
        throw new Error("Independent test hash manifest is missing");
    if (new Set(pinnedTests.files.map(file => file.path)).size !== pinnedTests.files.length)
        throw new Error("Test pin manifest repeats paths");
    for (const file of pinnedTests.files) checkedFile(testRoot, file.path, file.sha256);
}

export function verifyErrorInventories(before, after) {
    function counts(inventory) {
        if (!Array.isArray(inventory?.entries)) throw new Error("Normalized client error inventory is missing");
        const values = new Map();
        for (const entry of inventory.entries) {
            if (typeof entry.signature !== "string" || !entry.signature || !Number.isSafeInteger(entry.count) || entry.count < 0)
                throw new Error("Invalid client error signature/count");
            values.set(entry.signature, (values.get(entry.signature) ?? 0) + entry.count);
        }
        return values;
    }
    const a = counts(before), b = counts(after);
    for (const [name, count] of a) if ((b.get(name) ?? 0) < count) throw new Error("Client error inventory was reset/truncated");
    for (const [name, count] of b) if (count > (a.get(name) ?? 0)) throw new Error("New client error occurrences: " + name);
}

export function verifyNativeResults(documents) {
    const required = { RasterWorkBoundsTests: 26, OptionalPaintDiagnosticsTests: 5,
        GpuRenderingPolicyTests: 2, GpuContractFixtureTests: 6, ArcConnectionTests: 6, GradientAlphaTests: 3,
        DisplayGraphicsContractTests: 12, PaintFailureReportingTests: 3 };
    const counts = Object.fromEntries(Object.keys(required).map(name => [name, 0]));
    const identities = new Set();
    const attributes = text => Object.fromEntries(Array.from(text.matchAll(/([A-Za-z]+)="([^"]*)"/g), match => [match[1], match[2]]));
    for (const document of documents) {
        const match = document.match(/<Counters\b([^>]*)\/?\s*>/);
        if (!match) throw new Error("Native TRX summary is missing");
        const totals = attributes(match[1]);
        const results = Array.from(document.matchAll(/<UnitTestResult\b([^>]*)>/g), item => attributes(item[1]));
        if (!results.length || Number(totals.total) !== results.length || Number(totals.passed) !== results.length ||
            results.some(result => result.outcome !== "Passed")) throw new Error("Native tests failed, skipped or were omitted");
        for (const result of results) {
            if (identities.has(result.testName)) throw new Error("Native test receipt contains duplicate cases");
            identities.add(result.testName);
            for (const name of Object.keys(required)) if (result.testName?.startsWith(name + ".")) counts[name]++;
        }
    }
    for (const [name, minimum] of Object.entries(required))
        if (counts[name] < minimum) throw new Error(`Required native coverage missing: ${name}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
    try {
        const [, , reportPath, policyPath, expectedBuild, artifactRoot, pinsPath, testRoot] = process.argv;
        if (!testRoot) throw new Error("Usage: node verify-quality.mjs <report> <policy> <build-run-id> <artifact-root> <test-pins> <test-root>");
        const parse = path => JSON.parse(readFileSync(path, "utf8"));
        const report = parse(reportPath);
        if (!process.env.WF_GPU_TEST_SEED) throw new Error("Trusted driver seed is required");
        verifyGpuReport(report.rendering, expectedBuild, Number(process.env.WF_GPU_TEST_SEED));
        const policyBytes = readFileSync(policyPath);
        if (!process.env.WF_EXPECTED_PERFORMANCE_POLICY_SHA256 || createHash("sha256").update(policyBytes).digest("hex") !==
            process.env.WF_EXPECTED_PERFORMANCE_POLICY_SHA256.toLowerCase()) throw new Error("Performance policy is not the trusted CI policy");
        const summaries = verifyQualityEvidence(report, JSON.parse(policyBytes), expectedBuild);
        const pinsBytes = readFileSync(pinsPath);
        if (!process.env.WF_EXPECTED_TEST_PINS_SHA256 || createHash("sha256").update(pinsBytes).digest("hex") !==
            process.env.WF_EXPECTED_TEST_PINS_SHA256.toLowerCase()) throw new Error("Test pin manifest is not the trusted CI manifest");
        verifyFiles(report, artifactRoot, JSON.parse(pinsBytes), testRoot);
        console.log(JSON.stringify({ outcome: "passed", summaries }));
    } catch (error) {
        console.error("Rendering quality acceptance FAILED: " + error.message);
        process.exitCode = 1;
    }
}
