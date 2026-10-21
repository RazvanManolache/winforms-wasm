// GPU REGRESSION (binding), second wave. Test-owned pixel oracles against
// the real production gpu-bind.js; not a software GL emulator.
//
// INTEGRATOR WIRING (not yet done, deliberately): this module is intentionally
// a NEW file. Folding these cases into binding-pixel-cases.mjs changes
// BINDING_CASES.length and the binding testSha256 pin, and
// binding-report.test.mjs hardcodes `.passed === 16`, so the fold needs the
// integrator's coordinated pin/test update. Until then, serve this file next
// to /binding.js (same allowlist pattern as serve-binding-contracts.mjs) and
// run it in the same hardware browser session; do NOT merge its oracles into
// binding-pixel-cases.mjs unilaterally.
//
// Every case below is HARDWARE-GATED: no receipt exists yet. Expected values
// are computed from the straight-alpha contract (independent StraightOver
// math in E:/tmp/gpu-regression-oracles.mjs), never from observed pixels.
import {comparePixels} from './binding-pixel-cases.mjs';

const R=[255,0,0,255], G=[0,255,0,255], B=[0,0,255,255], Z=[0,0,0,0];
export const BINDING_REGRESSION_CASES=Object.freeze([
    ['layer-opacity-half',[128,0,127,255],1],
    ['source-copy-half-over-transparent',[255,0,0,128],0],
    ['clipped-half-over-opaque',[...[128,0,127,255],...B],1],
    ['empty-scissor-suppresses-fill',B,0],
    ['tall-target-partial-read',[...R,...B],0],
    ['offset-subimage-write-readback',[...R,...B],0],
    ['remove-layer-no-ghost',G,0],
    ['move-layer-no-stale',[...R,...Z],0],
    ['cascade-quarter-then-half',[51,0,204,160],1],
    ['padded-stride-subimage',[...R,...B],0]
].map(([id,expected,tolerance])=>Object.freeze({id,expected:Object.freeze(expected),tolerance})));

export function runBindingRegressionCases(api,canvasId) {
    const checks=[]; let id=0;
    try {
        id=api.create(canvasId,8,8);
        if(!id) throw new Error('Real WebGL2 context creation failed');
        const canvas=document.getElementById(canvasId), gl=canvas.getContext('webgl2');
        if(!gl||!(gl instanceof WebGL2RenderingContext)) throw new Error('Not a real WebGL2 context');
        const dbg=gl.getExtension('WEBGL_debug_renderer_info');
        const renderer=dbg?gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL):'';
        const hardware={api:gl.getParameter(gl.VERSION),renderer,contextLost:gl.isContextLost(),
            hardwareAccelerated:!!renderer&&!/swiftshader|llvmpipe|softpipe|software|warp|basic render/i.test(renderer)};
        const surface=(w=8,h=8)=>{const t=api.createTexture(id,w,h);return{t,f:api.createFramebuffer(id,t),w,h};};
        const bind=s=>{api.bindFramebuffer(id,s.f);api.setViewport(id,s.w,s.h);api.setScissor(id,false,0,0,0,0);api.setSourceOver(id,true);};
        const clear=(s,color=B)=>{bind(s);api.clear(id,...color.map(v=>v/255));};
        const fill=(x,y,w,h,color)=>api.fillQuad(id,x,y,x+w,y,x+w,y+h,x,y+h,
            ((color[0]<<24)|(color[1]<<16)|(color[2]<<8)|color[3])>>>0);
        const full=(s,x=0,y=0)=>Array.from(api.read(id,s.f,0,0,s.w,s.h).slice((y*s.w+x)*4,(y*s.w+x)*4+4));
        const part=(s,x=0,y=0)=>Array.from(api.read(id,s.f,x,y,1,1));
        const blit=(from,to,op=1)=>{bind(to);api.blit(id,0,0,to.w,0,to.w,to.h,0,to.h,
            from.t,0,0,from.w,from.h,from.w,from.h,false,op);};
        const uniform=(w,h,color,stride=w*4)=>{
            const bytes=new Uint8Array(stride*h);
            for(let y=0;y<h;y++) for(let x=0;x<w;x++) bytes.set(color,y*stride+x*4);
            return bytes;
        };
        const uploaded=(top=R,bottom=G,stride=32,w=8,h=8)=>{
            const s=surface(w,h); const bytes=uniform(w,h,bottom,stride);
            for(let y=0;y<h/2;y++) for(let x=0;x<w;x++) bytes.set(top,y*stride+x*4);
            api.upload(id,s.t,0,0,w,h,bytes,stride);return s;
        };
        const actions={
            // Image/layer opacity: half-strength red over opaque blue must hit
            // the straight-over midpoint, not darken twice.
            'layer-opacity-half':()=>{const a=uploaded(R,R),b=surface();clear(b);blit(a,b,0.5);return full(b);},
            // SourceCopy writes exact bits even for half-transparent paint.
            'source-copy-half-over-transparent':()=>{const s=surface();clear(s,Z);api.setSourceOver(id,false);fill(0,0,8,8,[255,0,0,128]);return full(s);},
            // Clip + alpha combined: inside the scissor the midpoint lands,
            // outside the clear color survives.
            'clipped-half-over-opaque':()=>{const s=surface();clear(s);api.setScissor(id,true,2,1,3,2);fill(0,0,8,8,[255,0,0,128]);return[...full(s,3,1),...full(s,3,6)];},
            // Zero-area scissor draws nothing at all.
            'empty-scissor-suppresses-fill':()=>{const s=surface();clear(s);api.setScissor(id,true,2,2,0,0);fill(0,0,8,8,R);return full(s);},
            // Non-square target: partial reads must use the TARGET height for
            // the y flip, not a stale 8x8 assumption.
            'tall-target-partial-read':()=>{const s=surface(8,16);clear(s);fill(0,0,8,2,R);return[...part(s,0,0),...part(s,0,15)];},
            // Subimage write at a nonzero offset, read back at and off it.
            'offset-subimage-write-readback':()=>{const s=surface();clear(s);api.upload(id,s.t,5,6,2,2,uniform(2,2,R),8);return[full(s,5,6),full(s,4,5)].flat();},
            // Removing a layer erases it: re-clear then blit only green.
            'remove-layer-no-ghost':()=>{const a=uploaded(R,R),c=uploaded(G,G),b=surface();clear(b);blit(a,b);clear(b);blit(c,b);return full(b);},
            // Moving a layer leaves no stale pixels: erase, then blit the red
            // texture shifted to the right half only.
            'move-layer-no-stale':()=>{const a=uploaded(R,R),b=surface();clear(b,Z);blit(a,b);clear(b,Z);
                bind(b);api.blit(id,4,0,8,0,8,8,4,8,a.t,0,0,a.w,a.h,a.w,a.h,false,1);
                return[full(b,6,4),full(b,1,4)].flat();},
            // Two-step transparent cascade with distinct alphas: 25% red,
            // then 50% blue. Straight math: [51,0,204,160].
            'cascade-quarter-then-half':()=>{const s=surface();clear(s,Z);fill(0,0,8,8,[255,0,0,64]);fill(0,0,8,8,[0,0,255,128]);return full(s);},
            // Padded-stride subimage write: stride padding must not shear rows.
            'padded-stride-subimage':()=>{const s=surface();clear(s);api.upload(id,s.t,3,4,2,2,uniform(2,2,R,16),16);return[full(s,3,4),full(s,5,5)].flat();}
        };
        for(const spec of BINDING_REGRESSION_CASES) {
            if(!actions[spec.id]) throw new Error('Missing regression action '+spec.id);
            let actual=null,error=null;
            try {actual=actions[spec.id]();if(gl.getError()!==gl.NO_ERROR)throw new Error('WebGL error during '+spec.id);}
            catch(e){error=String(e.message||e);}
            checks.push({id:spec.id,actual,error,passed:!error&&comparePixels(actual,spec)});
        }
        return {hardware,checks};
    } finally {if(id)api.del(id);}
}
