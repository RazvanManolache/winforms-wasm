// Test-owned acceptance rules. No renderer, scheduler or loader implementation.
const demand = (condition, message) => { if (!condition) throw new Error(message); };
const finite = value => typeof value === "number" && Number.isFinite(value);

export function summarizeLatency(samples, expectedCount, minimum) {
    demand(Array.isArray(samples) && Number.isInteger(expectedCount) && expectedCount >= minimum &&
        samples.length === expectedCount, "Incomplete/insufficient input samples");
    const ids = new Set(), durations = [];
    for (const sample of samples) {
        demand(typeof sample.inputId === "string" && sample.inputId.length > 0 && !ids.has(sample.inputId), "Missing/duplicate input identity");
        ids.add(sample.inputId);
        demand(sample.responded === true, "Input had no visible response");
        demand(sample.clock === "browser-monotonic" && sample.evidenceKind === "browser-trace+pixel-change",
            "Latency needs correlated browser timing and visible-change evidence");
        demand(finite(sample.inputMs) && sample.inputMs >= 0 && finite(sample.visibleMs) && sample.visibleMs >= sample.inputMs,
            "Invalid or mixed-clock latency timestamps");
        durations.push(sample.visibleMs - sample.inputMs);
    }
    durations.sort((a, b) => a - b);
    const rank = fraction => durations[Math.ceil(durations.length * fraction) - 1];
    return { count: durations.length, p50Ms: rank(0.5), p95Ms: rank(0.95), maxMs: durations.at(-1) };
}

export function verifyQualityEvidence(evidence, policy, expectedBuild) {
    demand(policy?.version === 1 && policy.calibrated === true, "Performance policy must be calibrated on the reference hardware");
    demand(Number.isInteger(policy.minimumSamples) && policy.minimumSamples >= 120, "At least 120 samples per workload are required");
    demand(finite(policy.maxP95Ratio) && policy.maxP95Ratio > 0 && policy.maxP95Ratio < 1, "A measured p95 improvement is required");
    demand(evidence?.candidate?.buildId === expectedBuild && typeof expectedBuild === "string" && expectedBuild.length > 0,
        "Quality evidence build/run does not match");
    demand(evidence.hardware?.accelerated === true && typeof evidence.hardware.renderer === "string" &&
        !/swiftshader|llvmpipe|software|unknown/i.test(evidence.hardware.renderer), "Hardware acceleration is unverified or software-only");
    const baseline = evidence.baseline, candidate = evidence.candidate;
    for (const key of ["browserVersion", "gpuId", "viewport", "dpr", "appHash", "sceneHash", "configuration"])
        demand(baseline?.environment?.[key] !== undefined &&
            JSON.stringify(baseline.environment[key]) === JSON.stringify(candidate.environment?.[key]), `Comparison environment differs: ${key}`);
    for (const key of ["appHash", "sceneHash"])
        demand(/^[a-f0-9]{64}$/i.test(candidate.environment[key]), `Missing content digest: ${key}`);
    demand(finite(candidate.environment.dpr) && candidate.environment.dpr > 0 &&
        Array.isArray(candidate.environment.viewport) && candidate.environment.viewport.length === 2 &&
        candidate.environment.viewport.every(value => Number.isSafeInteger(value) && value > 0), "Invalid viewport/DPR evidence");
    demand(candidate.diagnosticsEnabled === false && baseline.diagnosticsEnabled === false, "Diagnostics must be off in both runs");
    demand(candidate.measurement === "input-to-visible" && baseline.measurement === "input-to-visible", "Bridge/RAF timing is not visible-response evidence");
    demand(candidate.newClientErrors === 0 && candidate.newConsoleErrors === 0 && candidate.webglErrors === 0,
        "Candidate introduced client/console/WebGL errors");
    const summaries = {};
    for (const name of ["click", "drag"]) {
        const a = baseline.workloads?.[name], b = candidate.workloads?.[name], budget = policy.workloads?.[name];
        demand(finite(budget?.p95Ms) && budget.p95Ms > 0 && finite(budget?.maxMs) && budget.maxMs >= budget.p95Ms,
            `Missing calibrated budget: ${name}`);
        const reference = summarizeLatency(a?.samples, a?.expectedInputs, policy.minimumSamples);
        const current = summarizeLatency(b?.samples, b?.expectedInputs, policy.minimumSamples);
        demand(current.p95Ms <= budget.p95Ms && current.maxMs <= budget.maxMs, `${name} exceeded absolute response budget`);
        demand(current.p95Ms <= reference.p95Ms * policy.maxP95Ratio, `${name} did not achieve required p95 improvement`);
        summaries[name] = { baseline: reference, candidate: current };
    }
    for (const name of ["cold", "httpWarm", "inSession"]) {
        const before = baseline.loading?.[name], after = candidate.loading?.[name];
        demand(finite(before?.firstUsableMs) && before.firstUsableMs > 0 && finite(after?.firstUsableMs) && after.firstUsableMs > 0,
            `Missing loading case: ${name}`);
        demand(Number.isSafeInteger(before.transferredBytes) && before.transferredBytes >= 0 &&
            Number.isSafeInteger(after.transferredBytes) && after.transferredBytes >= 0,
            `Missing wire-byte measurement: ${name}`);
        if (name === "cold") demand(before.transferredBytes > 0 && after.transferredBytes > 0, "Cold startup had no measured transfer");
        demand(after.firstUsableMs <= before.firstUsableMs * policy.maxLoadRatio, `${name} loading regressed beyond budget`);
    }
    demand(finite(policy.maxLoadRatio) && policy.maxLoadRatio >= 1 && policy.maxLoadRatio <= 1.1, "Loading regression policy is too weak");
    demand(candidate.dynamicLoading?.separateAppAssemblies === true && candidate.dynamicLoading?.wholeFolderPreload === false &&
        candidate.dynamicLoading?.staticallyBuiltApp === false, "Dynamic application loading contract was broken");
    return summaries;
}

export const REQUIRED_ARTIFACT_KINDS = ["browser-trace", "browser-console", "gpu-diagnostics",
    "client-errors-before", "client-errors-after", "screenshots", "native-tests"];

export function verifyArtifactInventory(artifacts) {
    demand(Array.isArray(artifacts), "Evidence artifacts are missing");
    for (const kind of REQUIRED_ARTIFACT_KINDS)
        demand(artifacts.some(item => item.kind === kind), `Missing evidence artifact: ${kind}`);
    const paths = new Set();
    for (const artifact of artifacts) {
        demand(typeof artifact.path === "string" && artifact.path.length > 0 && !paths.has(artifact.path), "Invalid/duplicate evidence path");
        paths.add(artifact.path);
        demand(/^[a-f0-9]{64}$/i.test(artifact.sha256), "Missing artifact SHA256");
    }
}
