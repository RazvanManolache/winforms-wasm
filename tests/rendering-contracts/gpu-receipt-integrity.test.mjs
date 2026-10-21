import test from 'node:test';
import assert from 'node:assert/strict';
import {syntheticGpuReport} from './synthetic-report-fixture.mjs';
import {verifyGpuReport} from './verify-gpu-report.mjs';

// Passing these rejection tests certifies the checker only, never acceleration.
test('complete synthetic measurement exercises the receipt validator', () => verifyGpuReport(syntheticGpuReport(),'build',42));
const mutations = [
    ['old format',r=>r.version=2],
    ['stale receipt',r=>r.generatedAt='2000-01-01T00:00:00Z'],
    ['future receipt',r=>r.generatedAt='2100-01-01T00:00:00Z'],
    ['helper totals posing as browser evidence',r=>r.results.forEach(c=>c.evidence={durationMs:1,observed:{contextLosses:1,contextRestores:1}})],
    ['missing aggregate counters',r=>delete r.results[0].evidence.observed.drawCalls],
    ['no real correctness capture',r=>r.results[0].evidence.observed.presentationReadbacks=0],
    ['contradictory readbacks',r=>r.results[0].evidence.observed.readbacks=0],
    ['frame exceeds aggregate work',r=>r.results[0].evidence.measurements[0].observed.drawCalls=20],
    ['missing frame interval',r=>delete r.results[0].evidence.measurements],
    ['duplicate measurement',r=>r.results[0].evidence.measurements.push(r.results[0].evidence.measurements[0])],
    ['wrong interval kind',r=>r.results[0].evidence.measurements[0].kind='idle'],
    ['no GPU draw',r=>r.results[0].evidence.measurements[0].observed.drawCalls=0],
    ['CPU raster',r=>r.results[0].evidence.measurements[0].managed.cpuRasterPixels=1],
    ['bypassed OnPaint',r=>r.results[0].evidence.measurements[0].managed.controlPaints=0],
    ['silent fallback',r=>r.results[0].evidence.measurements[0].managed.fallbacks=1],
    ['normal-frame readback',r=>r.results[0].evidence.measurements[0].observed.readbacks=1],
    ['full CPU surface upload',r=>r.results[0].evidence.measurements[0].observed.fullSurfaceUploads=1],
    ['tiled CPU upload',r=>r.results[0].evidence.measurements[0].observed.uploadedPixels=2048],
    ['missing managed instrumentation',r=>delete r.results[0].evidence.measurements[0].managed],
    ['placement repaint',r=>r.results[2].evidence.measurements[0].managed.controlPaints=1],
    ['replacement surface on placement',r=>r.results[2].evidence.measurements[0].after.surfaceId='replacement'],
    ['idle GPU work',r=>r.results[3].evidence.measurements[0].observed.drawCalls=1],
    ['idle outer work hidden outside inner interval',r=>r.results[3].evidence.observed.drawCalls=1],
    ['CPU mirror posing as readback',r=>r.results[4].evidence.measurements[0].observed.readbacks=0],
    ['stale CPU pixel',r=>r.results[4].evidence.measurements[0].pixel=[255,0,0,255]],
    ['CPU mutation triggers repaint',r=>r.results[5].evidence.measurements[0].managed.controlPaints=1],
    ['resize reuses generation',r=>r.results[19].evidence.measurements[0].after.generation=1],
    ['resize keeps dimensions',r=>r.results[19].evidence.measurements[0].after.width=128],
    ['context recovery retains old handle',r=>r.results[20].evidence.measurements[0].oldHandleValid=true],
    ['no actual context events',r=>r.results[20].evidence.observed.contextRestores=0],
    ['resource leak',r=>r.results[21].evidence.measurements[0].after.Texture=2],
    ['no disposal cycles',r=>r.results[21].evidence.measurements[0].cycles=0],
    ['no resource audit',r=>Object.keys(r.results[21].evidence.measurements[0].before).forEach(k=>r.results[21].evidence.measurements[0].before[k]=0)]
];
for(const [name,mutate] of mutations) test('reject '+name,()=>{
    const report=syntheticGpuReport(); mutate(report);
    assert.throws(()=>verifyGpuReport(report,'build',42));
});
