import test from 'node:test';
import assert from 'node:assert/strict';
import {BINDING_CASES} from './binding-pixel-cases.mjs';
import {verifyBindingReport} from './verify-binding-report.mjs';
const identity={runId:'unit-test-only',bindingSha256:'a'.repeat(64),testSha256:'b'.repeat(64)};
// SYNTHETIC checker self-tests, never GPU evidence.
const valid=()=>({version:1,...identity,createdUtc:'2026-10-19T00:00:00Z',
    hardware:{hardwareAccelerated:true,contextLost:false,api:'WebGL 2.0',renderer:'synthetic checker fixture'},
    checks:BINDING_CASES.map(s=>({id:s.id,actual:[...s.expected],passed:true,error:null}))});
test('synthetic valid receipt exercises the checker only',()=>assert.equal(verifyBindingReport(valid(),identity).passed,16));
for(const key of ['runId','bindingSha256','testSha256'])test('reject stale '+key,()=>{
    const report=valid();report[key]='stale';assert.throws(()=>verifyBindingReport(report,identity),/Stale or mismatched/);
});
test('missing receipt cannot pass',()=>assert.throws(()=>verifyBindingReport(null,identity)));
test('missing case cannot pass',()=>{const r=valid();r.checks.pop();assert.throws(()=>verifyBindingReport(r,identity),/Missing/);});
test('duplicate case cannot replace another case',()=>{const r=valid();r.checks[1]=r.checks[0];assert.throws(()=>verifyBindingReport(r,identity),/Duplicate/);});
test('renamed case cannot replace required case',()=>{const r=valid();r.checks[0].id='other';assert.throws(()=>verifyBindingReport(r,identity),/Missing/);});
for(const renderer of ['SwiftShader','llvmpipe','Microsoft Basic Render Driver','WARP',''])test('reject software/unknown renderer '+renderer,()=>{
    const r=valid();r.hardware.renderer=renderer;assert.throws(()=>verifyBindingReport(r,identity),/hardware/);
});
test('reject lost context',()=>{const r=valid();r.hardware.contextLost=true;assert.throws(()=>verifyBindingReport(r,identity),/hardware/);});
test('reject WebGL1',()=>{const r=valid();r.hardware.api='WebGL 1.0';assert.throws(()=>verifyBindingReport(r,identity),/hardware/);});
test('self-reported pass cannot hide incorrect pixels',()=>{const r=valid();r.checks[0].actual=[0,0,0,0];r.passed=true;assert.throws(()=>verifyBindingReport(r,identity),/pixel failures/);});
test('candidate expected pixels cannot override test-owned expectations',()=>{const r=valid();r.checks[0].actual=[0,0,0,0];r.checks[0].expected=[0,0,0,0];assert.throws(()=>verifyBindingReport(r,identity),/pixel failures/);});
test('noninteger or truncated pixels cannot pass',()=>{const r=valid();r.checks[0].actual=[255,0,0,254.5];assert.throws(()=>verifyBindingReport(r,identity));r.checks[0].actual=[255];assert.throws(()=>verifyBindingReport(r,identity));});
test('a browser error cannot be labeled a pixel pass',()=>{const r=valid();r.checks[0].error='context error';assert.throws(()=>verifyBindingReport(r,identity),/pixel failures/);});
