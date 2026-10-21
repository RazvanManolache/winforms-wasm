import test from 'node:test';
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {mkdtempSync,writeFileSync,readFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {BINDING_CASES} from './binding-pixel-cases.mjs';
const root=fileURLToPath(new URL('../../',import.meta.url));
const shell=process.platform==='win32'?'pwsh.exe':'pwsh';
function run(command,env={}) {
    const clean={...process.env};
    for(const key of Object.keys(clean)) if(key.startsWith('WF_GPU_') || key.startsWith('WF_EXPECTED_')) delete clean[key];
    return spawnSync(shell,['-NoLogo','-NoProfile','-NonInteractive','-Command',command],
        {cwd:root,env:{...clean,...env},encoding:'utf8',timeout:30000});
}
test('GPU acceptance fails without a real browser report, no skip/pass substitution',()=>{
    const r=run('& ./scripts/test-gpu.ps1');
    assert.equal(r.status,1,r.error?.message); assert.match(r.stdout,/GPU ACCEPTANCE FAILED.*report is missing/);
    assert.doesNotMatch(r.stdout,/GPU ACCEPTANCE PASSED/);
});
test('helper-only success JSON is not sufficient browser job evidence',()=>{
    const dir=mkdtempSync(join(tmpdir(),'wf-gpu-gate-'));
    try {
        const report=join(dir,'helper.json'); writeFileSync(report,JSON.stringify({passed:100,outcome:'passed'}));
        const r=run('& ./scripts/test-gpu.ps1',{WF_GPU_REPORT:report});
        assert.equal(r.status,1); assert.match(r.stdout,/Missing browser job input/);
    } finally {rmSync(dir,{recursive:true,force:true});}
});
test('changed checker hashes fail before any candidate checker is executed',()=>{
    const dir=mkdtempSync(join(tmpdir(),'wf-gpu-pins-'));
    const required=['gpu-contracts.mjs','gpu-scenarios.mjs','verify-gpu-report.mjs','verify-quality.mjs','quality-evidence.mjs',
        'gpu-contracts.test.mjs','quality-evidence.test.mjs','gpu-receipt-integrity.test.mjs','gpu-gate-wiring.test.mjs','synthetic-report-fixture.mjs',
        'binding-pixel-cases.mjs','verify-binding-report.mjs','binding-report.test.mjs','binding-fixture.html','serve-binding-contracts.mjs']
        .map(name=>'rendering-contracts/'+name).concat('WfWasm.Drawing.Tests/DisplayGraphicsContractTests.cs','WfWasm.CanvasHost.Tests/PaintFailureReportingTests.cs');
    try {
        const report=join(dir,'report.json'), pins=join(dir,'pins.json'), policy=join(dir,'policy.json');
        const pinBody=JSON.stringify({version:1,files:required.map(path=>({path,sha256:'0'.repeat(64)}))});
        writeFileSync(report,'{}');writeFileSync(policy,'{}');writeFileSync(pins,pinBody);
        const hash=s=>createHash('sha256').update(s).digest('hex');
        const r=run('& ./scripts/test-gpu.ps1',{WF_GPU_REPORT:report,WF_GPU_ARTIFACT_ROOT:dir,WF_GPU_POLICY:policy,
            WF_GPU_TEST_PINS:pins,WF_GPU_BUILD_ID:'synthetic-unit-check',WF_GPU_TEST_SEED:'42',
            WF_EXPECTED_TEST_PINS_SHA256:hash(pinBody),WF_EXPECTED_PERFORMANCE_POLICY_SHA256:hash('{}')});
        assert.equal(r.status,1);assert.match(r.stdout,/Trusted file hash mismatch/);
        assert.doesNotMatch(r.stdout,/Checker self-tests passed/);
    } finally {rmSync(dir,{recursive:true,force:true});}
});
test('FULL gate stays red even when every native unit suite claims success',()=>{
    // Fake only the native runner: this proves its totals cannot bypass the
    // real Node checks and required GPU receipt. It runs no native suites.
    const r=run("function dotnet { 'Passed! - synthetic unit-only result'; $global:LASTEXITCODE=0 }; & ./scripts/test-full.ps1 -NoBuild");
    assert.equal(r.status,1,r.error?.message);
    assert.match(r.stdout,/GPU ACCEPTANCE FAILED/);
    assert.match(r.stdout,/failed suites: GpuRenderingAcceptance/);
    assert.match(r.stdout,/WfWasm.Rendering.Gles.Tests/);
});

test('binding acceptance fails without a real browser receipt',()=>{
    const r=run('& ./scripts/test-gpu-binding.ps1');
    assert.equal(r.status,1,r.error?.message);
    assert.match(r.stdout,/GPU BINDING ACCEPTANCE FAILED.*report is missing/);
    assert.doesNotMatch(r.stdout,/GPU BINDING ACCEPTANCE PASSED/);
});

test('binding gate ignores a claimed pass when measured pixels disagree',()=>{
    const dir=mkdtempSync(join(tmpdir(),'wf-binding-gate-'));
    try {
        const hash=path=>createHash('sha256').update(readFileSync(join(root,path))).digest('hex');
        const report={version:1,runId:'gate-unit-rejection',createdUtc:new Date().toISOString(),
            bindingSha256:hash('src/WfWasm.CanvasHost/wwwroot/gpu-bind.js'),
            testSha256:hash('tests/rendering-contracts/binding-pixel-cases.mjs'),
            hardware:{hardwareAccelerated:true,contextLost:false,api:'WebGL 2.0',renderer:'synthetic gate rejection fixture'},
            checks:BINDING_CASES.map(s=>({id:s.id,actual:[...s.expected],passed:true,error:null})),passed:true};
        report.checks.find(c=>c.id==='retained-alpha-cascade').actual=[64,0,127,255];
        const path=join(dir,'report.json');writeFileSync(path,JSON.stringify(report));
        const r=run('& ./scripts/test-gpu-binding.ps1', {WF_GPU_BINDING_REPORT:path,WF_GPU_BINDING_RUN_ID:report.runId});
        assert.equal(r.status,1);assert.match(r.stdout+r.stderr,/retained-alpha-cascade/);
        assert.doesNotMatch(r.stdout,/GPU BINDING ACCEPTANCE PASSED/);
    } finally {rmSync(dir,{recursive:true,force:true});}
});

test('GLES targeted area executes device tests and disclaims hardware acceptance',()=>{
    const r=run("function dotnet { 'Passed! - synthetic unit-only result'; $global:LASTEXITCODE=0 }; & ./scripts/test-targeted.ps1 -Areas WfWasm.Rendering.Gles -NoBuild");
    assert.equal(r.status,0,r.stderr);assert.match(r.stdout,/WfWasm.Rendering.Gles.Tests/);
    assert.match(r.stdout,/GPU rendering acceptance was NOT evaluated/);
});
test('explicit targeted GPU gate cannot pass on policy/unit tests alone',()=>{
    const r=run("function dotnet { 'Passed! - synthetic unit-only result'; $global:LASTEXITCODE=0 }; & ./scripts/test-targeted.ps1 -Areas WfWasm.CanvasHost -NoBuild -GpuAcceptance");
    assert.equal(r.status,1,r.error?.message); assert.match(r.stdout,/GPU ACCEPTANCE FAILED/);
});
test('ordinary targeted results explicitly say GPU acceptance was not evaluated',()=>{
    const r=run("function dotnet { 'Passed! - synthetic unit-only result'; $global:LASTEXITCODE=0 }; & ./scripts/test-targeted.ps1 -Areas WfWasm.CanvasHost -NoBuild");
    assert.equal(r.status,0,r.stderr); assert.match(r.stdout,/GPU rendering acceptance was NOT evaluated/);
});
