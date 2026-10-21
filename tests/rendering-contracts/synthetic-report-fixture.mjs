// Unit-test data only. Never emitted as a browser acceptance artifact.
import {GPU_CASES} from './gpu-contracts.mjs';
export function syntheticGpuReport(buildId = 'build', seed = 42) {
    const observed = () => ({drawCalls:1,readbacks:0,presentationReadbacks:0,canvasReadbacks:0,uploads:0,
        uploadedPixels:0,fullSurfaceUploads:0,unknownUploads:0,diagnosticLogs:0,contextLosses:0,contextRestores:0,webglErrors:0});
    const managed = () => ({controlPaints:1,cpuRasterPixels:0,readbackBytes:0,fallbacks:0});
    const surface = () => ({surfaceId:'fixture-surface',contentVersion:1,generation:1,width:128,height:96});
    const resources = () => ({Texture:1,Framebuffer:1,Buffer:1,Renderbuffer:0,Program:1,Shader:2});
    return {version:3,source:'browser-webgl-audit',buildId,seed,webglErrors:0,
        generatedAt:new Date().toISOString(),auditedContexts:1,outcome:'passed',
        results:GPU_CASES.map((name,index) => {
            let m={kind:'device-frame',observed:observed(),managed:managed()};
            if(index===2) m={...m,kind:'placement',managed:{...managed(),controlPaints:0},before:surface(),after:surface()};
            if(index===3) m={...m,kind:'idle',observed:{...observed(),drawCalls:0},managed:{...managed(),controlPaints:0}};
            if(index===4) m={...m,kind:'cpu-read',observed:{...observed(),drawCalls:0,readbacks:1},managed:{...managed(),controlPaints:0,readbackBytes:4},pixel:[0,0,255,255]};
            if(index===5) m={...m,kind:'cpu-write',managed:{...managed(),controlPaints:0}};
            if(index===19 || index===20) m={kind:index===19?'resize':'context-recovery',before:surface(),
                after:{...surface(),generation:2,...(index===19?{width:144,height:112}:{})},oldHandleValid:false};
            if(index===21) m={kind:'resource-plateau',before:resources(),after:resources(),cycles:10};
            const whole = {...observed(),...(m.observed ?? {}),
                ...(![3,4,21].includes(index)?{readbacks:1,presentationReadbacks:1}:{}),
                ...(index===20?{contextLosses:1,contextRestores:1}:{})};
            return {name,outcome:'passed',evidence:{durationMs:1,observed:whole,measurements:[m]}};
        })};
}
