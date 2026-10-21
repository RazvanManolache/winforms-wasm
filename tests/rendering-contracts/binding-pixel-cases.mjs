// Test-owned pixel oracles. No candidate-supplied expected values are trusted.
// Exercise the real production binding; these are not a software GL emulator.
const R=[255,0,0,255], G=[0,255,0,255], B=[0,0,255,255], Z=[0,0,0,0];
export const BINDING_CASES=Object.freeze([
    ['opaque-fill',R,0], ['top-left-read',R,0], ['offset-read',G,0],
    ['top-first-upload',R,0], ['offset-upload',[...R,...B],0], ['padded-stride-upload',R,0],
    ['retained-texture-orientation',R,0], ['straight-transparent-over',[255,0,0,128],1],
    ['retained-alpha-cascade',[128,0,127,255],1], ['cpu-image-alpha',[128,0,127,255],1],
    ['source-copy-transparent',Z,0], ['rectangular-scissor',[...R,...B],0],
    ['opaque-destination-over',[128,0,127,255],1], ['two-transparent-layers',[85,0,170,192],1],
    ['read-uses-target-height',R,0], ['colored-alpha-image',[100,50,152,255],1]
].map(([id,expected,tolerance])=>Object.freeze({id,expected:Object.freeze(expected),tolerance})));

export function comparePixels(actual,spec) {
    if(!Array.isArray(actual)||actual.length!==spec.expected.length) return false;
    return actual.every((v,i)=>Number.isInteger(v)&&v>=0&&v<=255&&Math.abs(v-spec.expected[i])<=spec.tolerance);
}

export function runBindingPixelCases(api,canvasId) {
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
        const blit=(from,to)=>{bind(to);api.blit(id,0,0,to.w,0,to.w,to.h,0,to.h,
            from.t,0,0,from.w,from.h,from.w,from.h,false,1);};
        const uniform=(w,h,color,stride=w*4)=>{
            const bytes=new Uint8Array(stride*h);
            for(let y=0;y<h;y++) for(let x=0;x<w;x++) bytes.set(color,y*stride+x*4);
            return bytes;
        };
        const uploaded=(top=R,bottom=G,stride=32)=>{
            const s=surface(); const bytes=uniform(8,8,bottom,stride);
            for(let y=0;y<4;y++) for(let x=0;x<8;x++) bytes.set(top,y*stride+x*4);
            api.upload(id,s.t,0,0,8,8,bytes,stride);return s;
        };
        const actions={
            'opaque-fill':()=>{const s=surface();clear(s);fill(0,0,8,2,R);return full(s);},
            'top-left-read':()=>{const s=surface();clear(s);fill(0,0,8,2,R);return part(s);},
            'offset-read':()=>{const s=surface();clear(s);fill(0,2,8,1,G);return part(s,3,2);},
            'top-first-upload':()=>full(uploaded()),
            'offset-upload':()=>{const s=surface();clear(s);api.upload(id,s.t,2,1,2,2,uniform(2,2,R),8);return[...full(s,2,1),...full(s,2,6)];},
            'padded-stride-upload':()=>full(uploaded(R,G,40)),
            'retained-texture-orientation':()=>{const a=surface(),b=surface();clear(a);fill(0,0,8,2,R);clear(b);blit(a,b);return full(b);},
            'straight-transparent-over':()=>{const s=surface();clear(s,Z);fill(0,0,8,8,[255,0,0,128]);return full(s);},
            'retained-alpha-cascade':()=>{const a=surface(),b=surface();clear(a,Z);fill(0,0,8,8,[255,0,0,128]);clear(b);blit(a,b);return full(b);},
            'cpu-image-alpha':()=>{const a=uploaded([255,0,0,128],[255,0,0,128]),b=surface();clear(b);blit(a,b);return full(b);},
            'source-copy-transparent':()=>{const s=surface();clear(s,R);api.setSourceOver(id,false);fill(0,0,8,8,Z);return full(s);},
            'rectangular-scissor':()=>{const s=surface();clear(s);api.setScissor(id,true,2,1,3,2);fill(0,0,8,8,R);return[...full(s,3,1),...full(s,3,6)];},
            'opaque-destination-over':()=>{const s=surface();clear(s);fill(0,0,8,8,[255,0,0,128]);return full(s);},
            'two-transparent-layers':()=>{const s=surface();clear(s,Z);fill(0,0,8,8,[255,0,0,128]);fill(0,0,8,8,[0,0,255,128]);return full(s);},
            'read-uses-target-height':()=>{const small=surface(4,4),big=surface();clear(small);fill(0,0,4,1,R);clear(big,G);return part(small);},
            'colored-alpha-image':()=>{const a=uploaded([200,100,50,128],[200,100,50,128]),b=surface();clear(b);blit(a,b);return full(b);}
        };
        for(const spec of BINDING_CASES) {
            let actual=null,error=null;
            try {actual=actions[spec.id]();if(gl.getError()!==gl.NO_ERROR)throw new Error('WebGL error during '+spec.id);}
            catch(e){error=String(e.message||e);}
            checks.push({id:spec.id,actual,error,passed:!error&&comparePixels(actual,spec)});
        }
        return {hardware,checks};
    } finally {if(id)api.del(id);}
}
