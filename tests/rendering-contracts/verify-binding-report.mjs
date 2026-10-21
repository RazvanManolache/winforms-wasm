import {BINDING_CASES,comparePixels} from './binding-pixel-cases.mjs';
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {resolve} from 'node:path';

export function verifyBindingReport(report,identity,{allowPixelFailures=false}={}) {
    if(report?.version!==1)throw new Error('Missing binding report version');
    for(const key of ['runId','bindingSha256','testSha256'])
        if(typeof identity?.[key]!=='string'||!identity[key]||report[key]!==identity[key])throw new Error('Stale or mismatched '+key);
    if(!/^\d{4}-\d\d-\d\dT/.test(report.createdUtc||''))throw new Error('Missing capture timestamp');
    const h=report.hardware;
    if(h?.hardwareAccelerated!==true||h.contextLost!==false||typeof h.renderer!=='string'||!h.renderer.trim()||
        /swiftshader|llvmpipe|softpipe|software|warp|basic render/i.test(h.renderer)||!/WebGL\s*2/i.test(h.api||''))
        throw new Error('Actual hardware WebGL2 diagnostics are missing or rejected');
    if(!Array.isArray(report.checks)||report.checks.length!==BINDING_CASES.length)throw new Error('Missing binding pixel cases');
    const map=new Map();
    for(const check of report.checks){if(map.has(check.id))throw new Error('Duplicate binding case');map.set(check.id,check);}
    const failed=[];
    for(const spec of BINDING_CASES){
        const item=map.get(spec.id);if(!item)throw new Error('Missing binding case '+spec.id);
        // Recompute from independent constants; ignore report.passed and expected.
        if(item.error||!comparePixels(item.actual,spec))failed.push(spec.id);
    }
    if(failed.length&&!allowPixelFailures)throw new Error('GPU pixel failures: '+failed.join(', '));
    return {total:BINDING_CASES.length,passed:BINDING_CASES.length-failed.length,failed};
}

if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href){
    try {
        const [reportPath,runId]=process.argv.slice(2);
        if(!reportPath||!runId)throw new Error('Usage: node verify-binding-report.mjs <report> <trusted-run-id>');
        const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
        const identity={runId,bindingSha256:hash(readFileSync(new URL('../../src/WfWasm.CanvasHost/wwwroot/gpu-bind.js',import.meta.url))),
            testSha256:hash(readFileSync(new URL('./binding-pixel-cases.mjs',import.meta.url)))};
        const verdict=verifyBindingReport(JSON.parse(readFileSync(reportPath,'utf8')),identity);
        console.log('GPU BINDING ACCEPTANCE PASSED: '+verdict.passed+'/'+verdict.total+' actual pixel checks');
    }catch(e){console.error('GPU BINDING ACCEPTANCE FAILED: '+e.message);process.exitCode=1;}
}
