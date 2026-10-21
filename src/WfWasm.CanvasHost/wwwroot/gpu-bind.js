// Real WebGL2 driver binding for the GLES milestone device (IGlesContext).
// Raw WebGL2, no libraries: solid-quad + textured-quad programs, per-surface
// texture/FBO pairs. Public contract: straight sRGB RGBA, top-left
// device-pixel origin, row 0 = top on every transfer. Internally the GL
// targets store PREMULTIPLIED RGBA (fixed-function blending cannot produce
// straight RGB on transparent targets in one pass), with explicit boundary
// conversions: uploads/fills/clears premultiply on the way in, readbacks
// un-premultiply on the way out, and the textured shader outputs premultiplied
// texels scaled by layer opacity. Region origins are mapped per-target
// (yGL = targetHeight - (y + h)) using tracked texture/framebuffer sizes.
// Classic script (like canvas-bridge.js/webgl-fx.js): flat wfwasmGpu* globals
// for [JSImport]. All calls are synchronous; the C# side stays sync.
// Presentation framebuffer 0 is the canvas itself.
window.wfwasmGpu = (function () {
    var nextId = 1;
    var ctxs = {};

    var SOLID_VS = "attribute vec2 aPos;uniform vec2 uSize;void main(){vec2 n=vec2((aPos.x/uSize.x)*2.0-1.0,1.0-(aPos.y/uSize.y)*2.0);gl_Position=vec4(n,0.0,1.0);}";
    var SOLID_FS = "precision mediump float;uniform vec4 uColor;void main(){gl_FragColor=uColor;}";
    var TEX_VS = "attribute vec2 aPos;attribute vec2 aUv;uniform vec2 uSize;varying vec2 vUv;void main(){vUv=aUv;vec2 n=vec2((aPos.x/uSize.x)*2.0-1.0,1.0-(aPos.y/uSize.y)*2.0);gl_Position=vec4(n,0.0,1.0);}";
    var TEX_FS = "precision mediump float;uniform sampler2D uTex;uniform float uOpacity;varying vec2 vUv;void main(){vec4 t=texture2D(uTex,vUv);gl_FragColor=vec4(t.rgb*uOpacity,t.a*uOpacity);}";

    function compile(gl, type, src) {
        var s = gl.createShader(type);
        gl.shaderSource(s, src);
        gl.compileShader(s);
        if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) return null;
        return s;
    }

    function program(gl, vs, fs) {
        var a = compile(gl, gl.VERTEX_SHADER, vs);
        var b = compile(gl, gl.FRAGMENT_SHADER, fs);
        if (!a || !b) return null;
        var p = gl.createProgram();
        gl.attachShader(p, a);
        gl.attachShader(p, b);
        gl.linkProgram(p);
        if (!gl.getProgramParameter(p, gl.LINK_STATUS)) return null;
        return p;
    }

    function makeVbo(gl, data) {
        var v = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, v);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(data), gl.STREAM_DRAW);
        return v;
    }

    function caps() {
        try {
            var c = document.createElement("canvas");
            var gl = c.getContext("webgl2", { antialias: false });
            if (!gl) return "no-webgl2";
            var dbg = gl.getExtension("WEBGL_debug_renderer_info");
            var r = dbg ? gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL) : "?";
            var lost = gl.isContextLost();
            return "ok:webgl2 " + r + (lost ? " LOST" : "");
        } catch (e) { return "error:" + ((e && e.message) || e); }
    }

    // Creates the context on an EXISTING canvas (the driver creates the element).
    function create(canvasId, w, h) {
        try {
            var canvas = document.getElementById(canvasId);
            if (!canvas) return 0;
            canvas.width = w; canvas.height = h;
            var gl = canvas.getContext("webgl2", {
                alpha: false, antialias: false, depth: false, stencil: false,
                preserveDrawingBuffer: false, powerPreference: "high-performance"
            });
            if (!gl) return 0;
            var solid = program(gl, SOLID_VS, SOLID_FS);
            var tex = program(gl, TEX_VS, TEX_FS);
            if (!solid || !tex) return 0;
            var st = {
                gl: gl, canvas: canvas, w: w, h: h, lost: false,
                solid: solid, tex: tex,
                vboPos: makeVbo(gl, [0, 0, 0, 0, 0, 0, 0, 0]),
                vboUv: makeVbo(gl, [0, 0, 0, 0, 0, 0, 0, 0]),
                textures: {}, fbos: {}, nextTex: 1, nextFbo: 1,
                texSizes: {}, fboSizes: {},
                boundFbo: -1, vpW: 0, vpH: 0
            };
            canvas.addEventListener("webglcontextlost", function (ev) {
                try { ev.preventDefault(); } catch (_) { }
                st.lost = true;
            });
            var id = nextId++;
            ctxs[id] = st;
            return id;
        } catch (e) { return 0; }
    }

    function get(id) {
        var st = ctxs[id];
        if (!st) throw new Error("unknown gpu context " + id);
        return st;
    }

    function del(id) {
        var st = ctxs[id];
        if (!st) return;
        try {
            var gl = st.gl;
            Object.keys(st.fbos).forEach(function (k) { try { gl.deleteFramebuffer(st.fbos[k]); } catch (_) { } });
            Object.keys(st.textures).forEach(function (k) { try { gl.deleteTexture(st.textures[k]); } catch (_) { } });
            try { gl.deleteBuffer(st.vboPos); } catch (_) { }
            try { gl.deleteBuffer(st.vboUv); } catch (_) { }
            try { gl.deleteProgram(st.solid); } catch (_) { }
            try { gl.deleteProgram(st.tex); } catch (_) { }
            try { gl.getExtension("WEBGL_lose_context"); } catch (_) { }
        } catch (_) { }
        delete ctxs[id];
    }

    function isLost(id) {
        var st = ctxs[id];
        if (!st) return true;
        try { if (st.gl.isContextLost()) st.lost = true; } catch (_) { st.lost = true; }
        return st.lost;
    }

    function bindFbo(st, fbo) {
        if (st.boundFbo === fbo) return;
        st.boundFbo = fbo;
        st.gl.bindFramebuffer(st.gl.FRAMEBUFFER, fbo === 0 ? null : st.fbos[fbo]);
    }

    function setVp(st, w, h) {
        if (st.vpW !== w || st.vpH !== h) {
            st.vpW = w; st.vpH = h;
            st.gl.viewport(0, 0, w, h);
        }
    }

    function drawArrays(st, prog, loc) {
        var gl = st.gl;
        gl.useProgram(prog);
        gl.uniform2f(loc.uSize, st.vpW, st.vpH);
        gl.bindBuffer(gl.ARRAY_BUFFER, st.vboPos);
        gl.enableVertexAttribArray(loc.aPos);
        gl.vertexAttribPointer(loc.aPos, 2, gl.FLOAT, false, 0, 0);
        if (loc.aUv >= 0) {
            gl.bindBuffer(gl.ARRAY_BUFFER, st.vboUv);
            gl.enableVertexAttribArray(loc.aUv);
            gl.vertexAttribPointer(loc.aUv, 2, gl.FLOAT, false, 0, 0);
        } else {
            try { gl.disableVertexAttribArray(1); } catch (_) { }
        }
        gl.drawArrays(gl.TRIANGLE_FAN, 0, 4);
    }

    var api = {
        caps: caps,
        create: create,
        del: del,
        isLost: isLost,
        makeCurrent: function (id) { get(id); },
        createTexture: function (id, w, h) {
            var st = get(id), gl = st.gl;
            var t = gl.createTexture();
            gl.bindTexture(gl.TEXTURE_2D, t);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
            var n = st.nextTex++;
            st.textures[n] = t;
            st.texSizes[n] = { w: w, h: h };
            return n;
        },
        deleteTexture: function (id, t) {
            var st = get(id);
            if (st.textures[t]) { try { st.gl.deleteTexture(st.textures[t]); } catch (_) { } delete st.textures[t]; }
            delete st.texSizes[t];
        },
        createFramebuffer: function (id, t) {
            var st = get(id), gl = st.gl;
            var tex = st.textures[t];
            if (!tex) throw new Error("unknown texture " + t);
            var f = gl.createFramebuffer();
            gl.bindFramebuffer(gl.FRAMEBUFFER, f);
            gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
            if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE)
                throw new Error("incomplete framebuffer");
            st.boundFbo = -1;
            var n = st.nextFbo++;
            st.fbos[n] = f;
            var ts = st.texSizes[t];
            st.fboSizes[n] = ts ? { w: ts.w, h: ts.h } : null;
            return n;
        },
        deleteFramebuffer: function (id, f) {
            var st = get(id);
            if (st.fbos[f]) { try { st.gl.deleteFramebuffer(st.fbos[f]); } catch (_) { } delete st.fbos[f]; }
            delete st.fboSizes[f];
            if (st.boundFbo === f) st.boundFbo = -1;
        },
        bindFramebuffer: function (id, f) { bindFbo(get(id), f); },
        setViewport: function (id, w, h) { setVp(get(id), w, h); },
        setScissor: function (id, on, x, y, w, h) {
            var st = get(id), gl = st.gl;
            if (!on) { gl.disable(gl.SCISSOR_TEST); return; }
            gl.enable(gl.SCISSOR_TEST);
            // Contract rects are top-left; GL scissor wants bottom-left.
            // Use the bound target's tracked height (viewport may belong to
            // another target); fall back to the viewport when unknown.
            var fbH = st.vpH;
            if (st.boundFbo === 0) fbH = st.h;
            else if (st.fboSizes[st.boundFbo]) fbH = st.fboSizes[st.boundFbo].h;
            gl.scissor(x, fbH - (y + h), w, h);
        },
        setSourceOver: function (id, on) {
            var gl = get(id).gl;
            if (on) {
                gl.enable(gl.BLEND);
                // Premultiplied-alpha over: every source reaching the
                // framebuffer is already premultiplied at the boundary
                // (fill/upload/clear convert, the texture shader scales by
                // opacity), so RGB blends with (ONE, 1-SA) while alpha
                // itself accumulates with (ONE, 1-SA).
                gl.blendFuncSeparate(gl.ONE, gl.ONE_MINUS_SRC_ALPHA,
                    gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
            }
            else gl.disable(gl.BLEND);
        },
        clear: function (id, r, g, b, a) {
            var gl = get(id).gl;
            gl.disable(gl.SCISSOR_TEST);
            // Clear writes storage directly (no blending): convert the
            // straight contract color to premultiplied storage here.
            gl.clearColor(r * a, g * a, b * a, a);
            gl.clear(gl.COLOR_BUFFER_BIT);
        },
        fillQuad: function (id, x0, y0, x1, y1, x2, y2, x3, y3, rgba) {
            var st = get(id), gl = st.gl;
            var loc = { aPos: gl.getAttribLocation(st.solid, "aPos"), aUv: -1, uSize: gl.getUniformLocation(st.solid, "uSize") };
            gl.bindBuffer(gl.ARRAY_BUFFER, st.vboPos);
            gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([x0, y0, x1, y1, x2, y2, x3, y3]), gl.STREAM_DRAW);
            gl.useProgram(st.solid);
            gl.uniform2f(loc.uSize, st.vpW, st.vpH);
            // Straight contract color -> premultiplied storage, so both the
            // blend-on and the SourceCopy (blend-off) paths store premultiplied.
            var sr = ((rgba >>> 24) & 255) / 255, sg = ((rgba >>> 16) & 255) / 255,
                sb = ((rgba >>> 8) & 255) / 255, sa = (rgba & 255) / 255;
            gl.uniform4f(gl.getUniformLocation(st.solid, "uColor"),
                sr * sa, sg * sa, sb * sa, sa);
            gl.enableVertexAttribArray(loc.aPos);
            gl.vertexAttribPointer(loc.aPos, 2, gl.FLOAT, false, 0, 0);
            gl.drawArrays(gl.TRIANGLE_FAN, 0, 4);
        },
        blit: function (id, x0, y0, x1, y1, x2, y2, x3, y3, t, sx, sy, sw, sh, tw, th, linear, opacity) {
            var st = get(id), gl = st.gl;
            var tex = st.textures[t];
            if (!tex) throw new Error("unknown texture " + t);
            // Source rect is top-left in a texture whose top row is at GL top
            // (uploads flip into place; draws land natively), so V is flipped.
            var u0 = sx / tw, u1 = (sx + sw) / tw;
            var v1 = 1 - sy / th, v0 = 1 - (sy + sh) / th;
            gl.bindBuffer(gl.ARRAY_BUFFER, st.vboPos);
            gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([x0, y0, x1, y1, x2, y2, x3, y3]), gl.STREAM_DRAW);
            gl.bindBuffer(gl.ARRAY_BUFFER, st.vboUv);
            gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([u0, v1, u1, v1, u1, v0, u0, v0]), gl.STREAM_DRAW);
            var loc = { aPos: gl.getAttribLocation(st.tex, "aPos"), aUv: gl.getAttribLocation(st.tex, "aUv"), uSize: gl.getUniformLocation(st.tex, "uSize") };
            gl.useProgram(st.tex);
            gl.uniform2f(loc.uSize, st.vpW, st.vpH);
            gl.uniform1f(gl.getUniformLocation(st.tex, "uOpacity"), opacity);
            gl.activeTexture(gl.TEXTURE0);
            gl.bindTexture(gl.TEXTURE_2D, tex);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, linear ? gl.LINEAR : gl.NEAREST);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, linear ? gl.LINEAR : gl.NEAREST);
            gl.uniform1i(gl.getUniformLocation(st.tex, "uTex"), 0);
            drawArrays(st, st.tex, loc);
        },
        upload: function (id, t, x, y, w, h, bytes, stride) {
            var st = get(id), gl = st.gl;
            var tex = st.textures[t];
            if (!tex) throw new Error("unknown texture " + t);
            var ts = st.texSizes[t];
            if (!ts) throw new Error("unknown texture size " + t);
            if (stride < w * 4) throw new Error("upload stride too small");
            // Contract: top-left origin, row 0 = top, straight RGBA, padded
            // strides allowed. GL wants a bottom-left subimage origin with
            // bottom-first rows, so flip the rows and map y explicitly
            // (UNPACK_FLIP_Y_WEBGL alone does not fix partial offsets); the
            // same pass premultiplies into storage representation.
            var src = new Uint8Array(bytes);
            var tmp = new Uint8Array(w * h * 4);
            for (var r = 0; r < h; r++) {
                var sOff = r * stride, dOff = (h - 1 - r) * w * 4;
                for (var c = 0; c < w * 4; c += 4) {
                    var a = src[sOff + c + 3];
                    tmp[dOff + c + 3] = a;
                    if (a === 255) {
                        tmp[dOff + c] = src[sOff + c];
                        tmp[dOff + c + 1] = src[sOff + c + 1];
                        tmp[dOff + c + 2] = src[sOff + c + 2];
                    } else if (a === 0) {
                        tmp[dOff + c] = 0; tmp[dOff + c + 1] = 0; tmp[dOff + c + 2] = 0;
                    } else {
                        tmp[dOff + c] = Math.round(src[sOff + c] * a / 255);
                        tmp[dOff + c + 1] = Math.round(src[sOff + c + 1] * a / 255);
                        tmp[dOff + c + 2] = Math.round(src[sOff + c + 2] * a / 255);
                    }
                }
            }
            gl.bindTexture(gl.TEXTURE_2D, tex);
            gl.pixelStorei(gl.UNPACK_ROW_LENGTH, 0);
            gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
            gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
            gl.texSubImage2D(gl.TEXTURE_2D, 0, x, ts.h - (y + h), w, h, gl.RGBA, gl.UNSIGNED_BYTE, tmp);
        },
        read: function (id, f, x, y, w, h) {
            var st = get(id), gl = st.gl;
            gl.bindFramebuffer(gl.FRAMEBUFFER, f === 0 ? null : st.fbos[f]);
            st.boundFbo = f;
            var fbH = f === 0 ? st.h : (st.fboSizes[f] ? st.fboSizes[f].h : st.vpH);
            var raw = new Uint8Array(w * h * 4);
            // Contract region is top-left; gl.readPixels wants bottom-left.
            gl.readPixels(x, fbH - (y + h), w, h, gl.RGBA, gl.UNSIGNED_BYTE, raw);
            // GL rows are bottom-up and premultiplied; contract wants row 0
            // = top, straight RGBA.
            var out = new Uint8Array(raw.length);
            var row = w * 4;
            for (var r = 0; r < h; r++) {
                var sOff = (h - 1 - r) * row, dOff = r * row;
                for (var c = 0; c < row; c += 4) {
                    var a = raw[sOff + c + 3];
                    out[dOff + c + 3] = a;
                    if (a === 0) {
                        out[dOff + c] = 0; out[dOff + c + 1] = 0; out[dOff + c + 2] = 0;
                    } else if (a === 255) {
                        out[dOff + c] = raw[sOff + c];
                        out[dOff + c + 1] = raw[sOff + c + 1];
                        out[dOff + c + 2] = raw[sOff + c + 2];
                    } else {
                        out[dOff + c] = Math.min(255, Math.round(raw[sOff + c] * 255 / a));
                        out[dOff + c + 1] = Math.min(255, Math.round(raw[sOff + c + 1] * 255 / a));
                        out[dOff + c + 2] = Math.min(255, Math.round(raw[sOff + c + 2] * 255 / a));
                    }
                }
            }
            return out;
        },
        flush: function (id) { get(id).gl.flush(); }
    };
    return api;
})();
// Flat aliases for [JSImport] (one lookup deep, like webgl-fx.js).
window.wfwasmGpuCaps = function () { return window.wfwasmGpu.caps(); };
window.wfwasmGpuCreate = function (c, w, h) { return window.wfwasmGpu.create(c, w, h); };
window.wfwasmGpuDelete = function (id) { window.wfwasmGpu.del(id); };
window.wfwasmGpuIsLost = function (id) { return window.wfwasmGpu.isLost(id); };
window.wfwasmGpuMakeCurrent = function (id) { window.wfwasmGpu.makeCurrent(id); };
window.wfwasmGpuCreateTexture = function (id, w, h) { return window.wfwasmGpu.createTexture(id, w, h); };
window.wfwasmGpuDeleteTexture = function (id, t) { window.wfwasmGpu.deleteTexture(id, t); };
window.wfwasmGpuCreateFramebuffer = function (id, t) { return window.wfwasmGpu.createFramebuffer(id, t); };
window.wfwasmGpuDeleteFramebuffer = function (id, f) { window.wfwasmGpu.deleteFramebuffer(id, f); };
window.wfwasmGpuBindFramebuffer = function (id, f) { window.wfwasmGpu.bindFramebuffer(id, f); };
window.wfwasmGpuSetViewport = function (id, w, h) { window.wfwasmGpu.setViewport(id, w, h); };
window.wfwasmGpuSetScissor = function (id, on, x, y, w, h) { window.wfwasmGpu.setScissor(id, on, x, y, w, h); };
window.wfwasmGpuSetSourceOver = function (id, on) { window.wfwasmGpu.setSourceOver(id, on); };
window.wfwasmGpuClear = function (id, r, g, b, a) { window.wfwasmGpu.clear(id, r, g, b, a); };
window.wfwasmGpuFillQuad = function (id, x0, y0, x1, y1, x2, y2, x3, y3, rgba) { window.wfwasmGpu.fillQuad(id, x0, y0, x1, y1, x2, y2, x3, y3, rgba); };
window.wfwasmGpuBlit = function (id, x0, y0, x1, y1, x2, y2, x3, y3, t, sx, sy, sw, sh, tw, th, lin, op) { window.wfwasmGpu.blit(id, x0, y0, x1, y1, x2, y2, x3, y3, t, sx, sy, sw, sh, tw, th, lin, op); };
window.wfwasmGpuUpload = function (id, t, x, y, w, h, bytes, stride) { window.wfwasmGpu.upload(id, t, x, y, w, h, bytes, stride); };
window.wfwasmGpuRead = function (id, f, x, y, w, h) { return window.wfwasmGpu.read(id, f, x, y, w, h); };
window.wfwasmGpuFlush = function (id) { window.wfwasmGpu.flush(id); };
