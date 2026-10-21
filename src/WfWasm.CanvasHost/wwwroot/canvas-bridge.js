// Canvas2D bridge: replays one frame of WfWasm paint commands onto a
// <canvas> element. Commands arrive as one JSON array per frame to keep
// interop calls to one per frame: [op, ...args] where op is
// 1 = fillRect [x, y, w, h, cssColor], 2 = roundRect [x, y, w, h, r, cssColor],
// 3 = fillText [text, x, y, fontCss, cssColor, hAlign, vAlign, mnemonicIndex],
// 4 = fillGradientV [x, y, w, h, topCss, bottomCss],
// 5 = strokeLine [x1, y1, x2, y2, width, cssColor],
// 6 = ellipse [x, y, w, h, lineWidth, outlineCss, fillCss|null].
// 7 = drawImage [x, y, dw, dh, imgW, imgH, base64Rgba] (raw row-major RGBA,
// put synchronously via ImageData; no async decode).
// Alignment is resolved with real canvas font metrics (measureText); vAlign
// "baseline" preserves the original contract (y is the alphabetic baseline).
// One canvas element, no DOM UI.
window.wfwasm = window.wfwasm || {};
window.wfwasm._frames = 0;
window.wfwasm._fts = [];
// v1 JS-side input-timeline instrumentation. STRICTLY inert unless
// wfwasm.perfEnable() runs: no listeners, no rAF, no extra clocks.
window.wfwasm._perfOn = false;
window.wfwasm._perfIn = { buf: new Array(1024), i: 0, n: 0 };
window.wfwasm._fms = { buf: new Array(600), i: 0, n: 0 };
window.wfwasm._perfLastMove = null;
window.wfwasmPerfLastMove = function () { return window.wfwasm._perfLastMove; };
window.wfwasm._perfSeq = 0;
window.wfwasm._perfTsOffset = 0;
window.wfwasm._perfTsNorm = false;
window.wfwasm._perfTsSeen = {};
window.wfwasmNow = function () { return performance.now(); };
window.wfwasm._perfPush = function (ring, slot) {
    ring.buf[ring.i] = slot;
    ring.i++;
    if (ring.i >= ring.buf.length) ring.i = 0;
    if (ring.n < ring.buf.length) ring.n++;
};
window.wfwasm._perfUnwrap = function (ring) {
    var out = new Array(ring.n);
    var len = ring.buf.length;
    var start = ring.i - ring.n;
    if (start < 0) start += len;
    for (var j = 0; j < ring.n; j++) {
        var k = start + j;
        if (k >= len) k -= len;
        out[j] = ring.buf[k];
    }
    return out;
};
window.wfwasm._perfNormTs = function (e, kind, tJsIn) {
    // e.timeStamp basis varies (epoch vs navigation-start); detect once
    // per kind by comparing against performance.now() in this handler.
    if (!window.wfwasm._perfTsNorm && !window.wfwasm._perfTsSeen[kind]) {
        window.wfwasm._perfTsSeen[kind] = 1;
        var diff = e.timeStamp - tJsIn;
        if (diff > 5000 || diff < -5000) {
            window.wfwasm._perfTsOffset = tJsIn - e.timeStamp;
            window.wfwasm._perfTsNorm = true;
        }
    }
    return e.timeStamp + window.wfwasm._perfTsOffset;
};
window.wfwasm._perfKeyHash = function (key) {
    var s = String(key === null || key === undefined ? "" : key);
    var h = 0;
    for (var i = 0; i < s.length; i++) h = ((h << 5) - h + s.charCodeAt(i)) | 0;
    return h;
};
window.wfwasm._perfHookCanvas = function (canvas) {
    if (!canvas || canvas._wfPerfHook) return;
    canvas._wfPerfHook = true;
    var W = window.wfwasm;
    function rec(kind, e, x, y, extra) {
        if (!W._perfOn) return;
        var tJsIn = performance.now();
        var tEvent = W._perfNormTs(e, kind, tJsIn);
        var jsSeq = ++W._perfSeq;
        W._perfPush(W._perfIn, [jsSeq, kind, tEvent, tJsIn, x, y, extra]);
        if (kind === 3) W._perfLastMove = [jsSeq, tEvent, tJsIn, x, y];
    }
    canvas.addEventListener("mousedown", function (e) { rec(1, e, e.clientX, e.clientY, e.buttons | 0); }, { passive: true });
    canvas.addEventListener("mouseup", function (e) { rec(2, e, e.clientX, e.clientY, e.buttons | 0); }, { passive: true });
    canvas.addEventListener("mousemove", function (e) { rec(3, e, e.clientX, e.clientY, e.buttons | 0); }, { passive: true });
    canvas.addEventListener("wheel", function (e) { rec(4, e, e.clientX, e.clientY, e.buttons | 0); }, { passive: true });
    canvas.addEventListener("keydown", function (e) { rec(5, e, 0, 0, W._perfKeyHash(e.key)); }, { passive: true });
    canvas.addEventListener("keyup", function (e) { rec(6, e, 0, 0, W._perfKeyHash(e.key)); }, { passive: true });
};
window.wfwasm._perfWrapFetch = function () {
    if (window.wfwasm._perfFetchWrapped) return;
    if (typeof window.wfwasmFetchAssembly !== "function") return;
    window.wfwasm._perfFetchWrapped = true;
    var orig = window.wfwasmFetchAssembly;
    window.wfwasmFetchAssembly = function (app, name) {
        if (!window.wfwasm._perfOn) return orig(app, name);
        var t0 = performance.now();
        try {
            var r = orig(app, name);
            window.wfwasm._perfPush(window.wfwasm._perfIn, [8, t0, performance.now(), r ? r.length : 0]);
            return r;
        } catch (e) {
            window.wfwasm._perfPush(window.wfwasm._perfIn, [8, t0, performance.now(), 0]);
            throw e;
        }
    };
};
window.wfwasm.perfEnable = function () {
    window.wfwasm._perfOn = true;
    window.wfwasm._perfWrapFetch();
    window.wfwasm._perfHookCanvas(document.getElementById("wfcanvas"));
    return "ok";
};
window.wfwasm.perfDump = function () {
    var c = document.getElementById("wfcanvas");
    return JSON.stringify({
        header: {
            dpr: window.devicePixelRatio || 1,
            canvasW: c ? c.width : 0,
            canvasH: c ? c.height : 0,
            frames: window.wfwasm._frames,
            timeStampNormalized: window.wfwasm._perfTsNorm,
            tsOffset: window.wfwasm._perfTsOffset,
            userAgent: navigator.userAgent
        },
        inputs: window.wfwasm._perfUnwrap(window.wfwasm._perfIn),
        frames: window.wfwasm._perfUnwrap(window.wfwasm._fms)
    });
};
window.wfwasm._perfWrapFetch();
window.wfwasm.paint = function (canvasId, commandsJson, frameSeq) {
    var perf = window.wfwasm._perfOn && frameSeq > 0;
    var tEntry = perf ? performance.now() : 0;
    window.wfwasm._frames++;
    var fts = window.wfwasm._fts;
    fts.push(Math.round(performance.now()));
    if (fts.length > 600) fts.splice(0, fts.length - 600);
    var canvas = document.getElementById(canvasId);
    if (!canvas) return "no-canvas";
    var ctx = canvas.getContext("2d");
    if (!ctx) return "no-2d";
    var tParse0 = perf ? performance.now() : 0;
    var commands = JSON.parse(commandsJson);
    var tAfterParse = perf ? performance.now() : 0;
    var hAligns = ["left", "center", "right"];
    var vAligns = ["alphabetic", "top", "middle", "bottom"];
    for (var i = 0; i < commands.length; i++) {
        var c = commands[i];
        if (c[0] === 1) {
            ctx.fillStyle = c[5];
            ctx.fillRect(c[1], c[2], c[3], c[4]);
        } else if (c[0] === 2) {
            ctx.fillStyle = c[6];
            ctx.beginPath();
            ctx.roundRect(c[1], c[2], c[3], c[4], c[5]);
            ctx.fill();
        } else if (c[0] === 3) {
            ctx.fillStyle = c[5];
            ctx.font = c[4];
            ctx.textAlign = hAligns[c[6]] || "left";
            ctx.textBaseline = vAligns[c[7]] || "alphabetic";
            ctx.fillText(c[1], c[2], c[3]);
            if (c[8] >= 0 && c[8] < c[1].length) {
                // Mnemonic underline: measure the prefix with the same font.
                var px = c[2];
                var full = ctx.measureText(c[1]).width;
                var pre = ctx.measureText(c[1].substring(0, c[8])).width;
                var glyph = Math.max(1, ctx.measureText(c[1][c[8]]).width - 1);
                if (ctx.textAlign === "center") px = c[2] - full / 2 + pre;
                else if (ctx.textAlign === "right") px = c[2] - full + pre;
                else px = c[2] + pre;
                var m = ctx.measureText(c[1].substring(0, c[8] + 1));
                var ascent = m.actualBoundingBoxAscent || 10;
                var baseY = c[3];
                if (ctx.textBaseline === "top") baseY = c[3] + ascent;
                else if (ctx.textBaseline === "middle") baseY = c[3] + ascent / 2;
                else if (ctx.textBaseline === "bottom") baseY = c[3] + (m.actualBoundingBoxDescent ? -m.actualBoundingBoxDescent : 0);
                ctx.fillRect(px, baseY + 1, glyph, 1);
            }
            ctx.textAlign = "left";
            ctx.textBaseline = "alphabetic";
        } else if (c[0] === 4) {
            var g = ctx.createLinearGradient(0, c[2], 0, c[2] + c[4]);
            g.addColorStop(0, c[5]);
            g.addColorStop(1, c[6]);
            ctx.fillStyle = g;
            ctx.fillRect(c[1], c[2], c[3], c[4]);
        } else if (c[0] === 5) {
            ctx.strokeStyle = c[6];
            ctx.lineWidth = Math.max(1, c[5]);
            ctx.lineCap = "round";
            ctx.beginPath();
            ctx.moveTo(c[1], c[2]);
            ctx.lineTo(c[3], c[4]);
            ctx.stroke();
        } else if (c[0] === 6) {
            ctx.beginPath();
            ctx.ellipse(c[1] + c[3] / 2, c[2] + c[4] / 2, Math.abs(c[3] / 2), Math.abs(c[4] / 2), 0, 0, Math.PI * 2);
            if (c[7]) {
                ctx.fillStyle = c[7];
                ctx.fill();
            }
            ctx.strokeStyle = c[6];
            ctx.lineWidth = Math.max(1, c[5]);
            ctx.stroke();
        } else if (c[0] === 7) {
            var iw = c[5] | 0, ih = c[6] | 0;
            var bin = atob(c[7]);
            if (iw > 0 && ih > 0 && bin.length >= iw * ih * 4) {
                var px = new Uint8ClampedArray(iw * ih * 4);
                for (var k = 0; k < px.length; k++) px[k] = bin.charCodeAt(k);
                var scratch = window.wfwasm._img || (window.wfwasm._img = document.createElement("canvas"));
                if (scratch.width !== iw || scratch.height !== ih) { scratch.width = iw; scratch.height = ih; }
                scratch.getContext("2d").putImageData(new ImageData(px, iw, ih), 0, 0);
                ctx.imageSmoothingEnabled = true;
                ctx.drawImage(scratch, c[1], c[2], c[3], c[4]);
            }
        } else if (c[0] === 8) {
            // defBitmap marker: pixels travel out-of-band via
            // wfwasmTexDef (PaintAsync strips defs, so this runs only
            // for hand-built streams). No-op by design.
        } else if (c[0] === 9) {
            var layer = window.wfwasm._tex ? window.wfwasm._tex.get(String(c[1])) : null;
            if (layer) {
                ctx.imageSmoothingEnabled = true;
                ctx.drawImage(layer, c[2], c[3], c[4], c[5]);
            }
        } else if (c[0] === 10) {
            if (window.wfwasm._tex && c[1]) {
                var live = {};
                for (var li = 0; li < c[1].length; li++) live[String(c[1][li])] = 1;
                window.wfwasm._tex.forEach(function (v, k) { if (!live[k]) window.wfwasm._tex.delete(k); });
            }
        } else if (c[0] === 11) {
            // patchBitmap marker: pixels travel out-of-band via
            // wfwasmTexPatch (PaintAsync strips patches, so this runs
            // only for hand-built streams). No-op by design.
        }
    }
    if (perf) {
        var tDone = performance.now();
        var slot = [frameSeq, tAfterParse - tParse0, tDone - tAfterParse, commands.length, tEntry, tDone, 0, 0];
        window.wfwasm._perfPush(window.wfwasm._fms, slot);
        requestAnimationFrame(function (t1) {
            slot[6] = t1;
            requestAnimationFrame(function (t2) { slot[7] = t2; });
        });
    }
    return "ok:" + commands.length;
};
// Binary layer upload (bound via [JSImport], synchronous): stores RGBA
// bytes as a retained canvas addressed by later blit ops. Returns a
// short status string; failures keep the previous layer (if any).
window.wfwasmTexDef = function (layerId, bytes, w, h) {
    try {
        w |= 0; h |= 0;
        var src = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
        if (!layerId || w <= 0 || h <= 0 || src.length < w * h * 4) return "badargs";
        if (!window.wfwasm._tex) window.wfwasm._tex = new Map();
        var c = document.createElement("canvas");
        c.width = w; c.height = h;
        var g = c.getContext("2d", { alpha: true });
        var img = g.createImageData(w, h);
        img.data.set(src.subarray(0, w * h * 4));
        g.putImageData(img, 0, 0);
        window.wfwasm._tex.set(String(layerId), c);
        return "ok";
    } catch (e) {
        return "error:" + ((e && e.message) || e);
    }
};
// Region-repaint splice: writes a sub-rect of RGBA bytes into an
// existing retained layer (region raster ran on the managed side over
// the retained pixels). Missing/layer-size mismatch keeps the old
// layer (managed side Defs first, so this is a backstop only).
window.wfwasmTexPatch = function (layerId, bytes, x, y, w, h) {
    try {
        x |= 0; y |= 0; w |= 0; h |= 0;
        var src = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
        if (!layerId || w <= 0 || h <= 0 || src.length < w * h * 4) return "badargs";
        var layer = window.wfwasm._tex ? window.wfwasm._tex.get(String(layerId)) : null;
        if (!layer || x < 0 || y < 0 || x + w > layer.width || y + h > layer.height) return "nolayer";
        var g = layer.getContext("2d", { alpha: true });
        var img = g.createImageData(w, h);
        img.data.set(src.subarray(0, w * h * 4));
        g.putImageData(img, x, y);
        return "ok";
    } catch (e) {
        return "error:" + ((e && e.message) || e);
    }
};
window.wfwasm.fitCanvas = function (canvasId, dotNet) {
    var canvas = document.getElementById(canvasId);
    if (!canvas) return "no-canvas";
    window.wfwasm._dotNet = dotNet;
    // Re-fitting the same canvas (re-render) must not stack observers:
    // release any previous fitting first (idempotent).
    window.wfwasm.releaseCanvas(canvasId);
    var apply = function () {
        var w = Math.max(1, Math.floor(canvas.clientWidth));
        var h = Math.max(1, Math.floor(canvas.clientHeight));
        if (canvas.width !== w || canvas.height !== h) {
            canvas.width = w;
            canvas.height = h;
            if (dotNet) dotNet.invokeMethodAsync("OnCanvasResized", w, h);
        }
    };
    apply();
    var fit = window.wfwasm._fit || (window.wfwasm._fit = {});
    if (window.ResizeObserver) {
        var ro = new ResizeObserver(apply);
        ro.observe(canvas);
        fit[canvasId] = { observer: ro };
    } else {
        window.addEventListener("resize", apply);
        fit[canvasId] = { windowHandler: apply };
    }
    // Both paths stash their handle in fit[] so releaseCanvas can undo them.
    // Keyboard delivery needs DOM focus on the canvas (Blazor @onkeydown is
    // bound to the element). Synthesized and some real pointer presses do
    // not move focus by default, so take it explicitly on pointer down;
    // scoped to the canvas so page inputs (search) keep their own keys.
    if (!canvas._wfFocusHook) {
        canvas._wfFocusHook = true;
        canvas.addEventListener("pointerdown", function (e) {
            // Press-drag-release outside the canvas must still deliver its
            // move/up (strokes, scrollbar/modal drags): capture the pointer
            // to the canvas for the gesture (released implicitly on up).
            try { if (e && e.pointerId !== undefined && canvas.setPointerCapture) canvas.setPointerCapture(e.pointerId); } catch (err) { }
            if (document.activeElement !== canvas) {
                try { canvas.focus({ preventScroll: true }); } catch (e) { try { canvas.focus(); } catch (e2) {} }
            }
        });
    }
    if (window.wfwasm._perfOn) window.wfwasm._perfHookCanvas(canvas);
    return "ok";
};
window.wfwasm.releaseCanvas = function (canvasId) {
    try {
        var fit = window.wfwasm._fit;
        if (!fit || !fit[canvasId]) return "unknown";
        var entry = fit[canvasId];
        delete fit[canvasId];
        if (entry.observer) {
            try { entry.observer.disconnect(); } catch (e) {}
        }
        if (entry.windowHandler) {
            try { window.removeEventListener("resize", entry.windowHandler); } catch (e) {}
        }
        return "ok";
    } catch (e) {
        return "error:" + ((e && e.message) || e);
    }
};
// VFS bootstrap decode: the managed PNG codec cannot read JPEG, so
// loose JPEG content is decoded by the browser here. Input is the
// raw file bytes as base64; the result is base64 of an 8-byte
// little-endian [width, height] header followed by row-major RGBA.
window.wfwasm.decodeImage = async function (base64) {
    var bin = atob(base64);
    var raw = new Uint8Array(bin.length);
    for (var i = 0; i < bin.length; i++) raw[i] = bin.charCodeAt(i);
    var bmp = await createImageBitmap(new Blob([raw]));
    try {
        var c = document.createElement("canvas");
        c.width = bmp.width; c.height = bmp.height;
        var g = c.getContext("2d", { willReadFrequently: true });
        g.drawImage(bmp, 0, 0);
        var px = g.getImageData(0, 0, bmp.width, bmp.height).data;
        var out = new Uint8Array(8 + px.length);
        out[0] = bmp.width & 255; out[1] = (bmp.width >> 8) & 255;
        out[2] = (bmp.width >> 16) & 255; out[3] = (bmp.width >> 24) & 255;
        out[4] = bmp.height & 255; out[5] = (bmp.height >> 8) & 255;
        out[6] = (bmp.height >> 16) & 255; out[7] = (bmp.height >> 24) & 255;
        out.set(px, 8);
        var s = "";
        for (var o = 0; o < out.length; o += 8192)
            s += String.fromCharCode.apply(null, out.subarray(o, o + 8192));
        return btoa(s);
    } finally {
        bmp.close();
    }
};
// Binary JS interop avoids two large base64 strings and their managed copies.
window.wfwasm.decodeImageBytes = async function (bytes) {
    const bmp = await createImageBitmap(new Blob([bytes]));
    try {
        const c = document.createElement("canvas");
        c.width = bmp.width; c.height = bmp.height;
        const g = c.getContext("2d", { willReadFrequently: true });
        g.drawImage(bmp, 0, 0);
        const pixels = g.getImageData(0, 0, bmp.width, bmp.height).data;
        const result = new Uint8Array(8 + pixels.length);
        const header = new DataView(result.buffer);
        header.setInt32(0, bmp.width, true); header.setInt32(4, bmp.height, true);
        result.set(pixels, 8);
        return result;
    } finally { bmp.close(); }
};
// CPU text raster for System.Drawing.Graphics.DrawString (bound via
// [JSImport], synchronous): draws white text on a scratch canvas and
// returns an 8-byte [width, height] header + row-major RGBA. C# tints
// by the brush color using the alpha channel only, so one raster
// serves every color. Null on any failure (caller keeps throwing).
window.wfwasmTextCaps = function () { return "ok:1"; };
window.wfwasmTextRaster = function (text, fontCss, fontPx, maxW) {
    try {
        text = String(text === null || text === undefined ? "" : text);
        if (!text) return null;
        fontPx = Math.max(1, fontPx | 0);
        maxW = Math.max(1, maxW | 0);
        var c = window.wfwasm._txt || (window.wfwasm._txt = document.createElement("canvas"));
        var g = c.getContext("2d", { willReadFrequently: true });
        g.font = String(fontCss);
        var paras = text.split("\n");
        // Greedy word wrap at maxW (GDI+ rect semantics): explicit
        // newlines break first, then words; a lone word wider than maxW
        // breaks mid-word so the raster never exceeds maxW. Without this
        // wrapped Telerik text measured one clipped line while layout
        // expected many (BookShelf panorama titles: 1 line over void).
        var lines = [];
        for (var pi = 0; pi < paras.length; pi++) {
            var words = paras[pi].split(" ");
            var cur = "";
            for (var wi = 0; wi < words.length; wi++) {
                var trial = cur ? cur + " " + words[wi] : words[wi];
                if (!cur || g.measureText(trial).width <= maxW) cur = trial;
                else { lines.push(cur); cur = words[wi]; }
            }
            while (cur && g.measureText(cur).width > maxW) {
                var k = cur.length - 1;
                while (k > 1 && g.measureText(cur.slice(0, k)).width > maxW) k--;
                if (k < 1) k = 1; // lone over-wide char: emit whole, always progress
                lines.push(cur.slice(0, k));
                cur = cur.slice(k);
            }
            lines.push(cur || "");
        }
        var w = 1;
        for (var i = 0; i < lines.length; i++)
            w = Math.max(w, Math.ceil(g.measureText(lines[i] || " ").width));
        w = Math.min(w, maxW);
        var lh = Math.ceil(fontPx * 1.25);
        var h = Math.max(1, lh * lines.length);
        c.width = w; c.height = h;
        g.font = String(fontCss);
        g.textBaseline = "top";
        g.fillStyle = "#fff";
        for (var l = 0; l < lines.length; l++)
            g.fillText(lines[l], 0, l * lh);
        var px = g.getImageData(0, 0, w, h).data;
        var out = new Uint8Array(8 + px.length);
        out[0] = w & 255; out[1] = (w >> 8) & 255;
        out[2] = (w >> 16) & 255; out[3] = (w >> 24) & 255;
        out[4] = h & 255; out[5] = (h >> 8) & 255;
        out[6] = (h >> 16) & 255; out[7] = (h >> 24) & 255;
        out.set(px, 8);
        return out;
    } catch (e) {
        return null;
    }
};
// Fractional advance twin of wfwasmTextRaster (same word-wrap, no
// pixels): GDI+ Typographic measures are fractional (oracle "The"
// = 19.35) but raster widths ceil (20), drifting TPF layouts
// ~+0.6px/word. Kept separate so the raster hot path is untouched.
window.wfwasmTextMeasure = function (text, fontCss, fontPx, maxW) {
    try {
        text = String(text === null || text === undefined ? "" : text);
        if (!text) return 0;
        fontPx = Math.max(1, fontPx | 0);
        maxW = Math.max(1, maxW | 0);
        var c = window.wfwasm._txt || (window.wfwasm._txt = document.createElement("canvas"));
        var g = c.getContext("2d", { willReadFrequently: true });
        g.font = String(fontCss);
        var paras = text.split("\n");
        var lines = [];
        for (var pi = 0; pi < paras.length; pi++) {
            var words = paras[pi].split(" ");
            var cur = "";
            for (var wi = 0; wi < words.length; wi++) {
                var trial = cur ? cur + " " + words[wi] : words[wi];
                if (!cur || g.measureText(trial).width <= maxW) cur = trial;
                else { lines.push(cur); cur = words[wi]; }
            }
            while (cur && g.measureText(cur).width > maxW) {
                var k = cur.length - 1;
                while (k > 1 && g.measureText(cur.slice(0, k)).width > maxW) k--;
                if (k < 1) k = 1;
                lines.push(cur.slice(0, k));
                cur = cur.slice(k);
            }
            lines.push(cur || "");
        }
        var w = 0;
        for (var i = 0; i < lines.length; i++)
            w = Math.max(w, g.measureText(lines[i] || " ").width);
        return Math.min(w, maxW);
    } catch (e) {
        return 0;
    }
};
window.wfwasm.savePng = function (canvasId, filename) {
    var canvas = document.getElementById(canvasId);
    if (!canvas) return "no-canvas";
    var a = document.createElement("a");
    a.href = canvas.toDataURL("image/png");
    a.download = filename || "painting.png";
    document.body.appendChild(a);
    a.click();
    a.remove();
    return "saved";
};
// Browser error reporting: forwards window errors and unhandled promise
// rejections (including Blazor/WASM engine failures such as ExitStatus)
// to the host server's /api/client-errors endpoint, where they land in
// logs/client-errors.jsonl and the /errors crash inventory. Batching keeps
// error storms cheap; failures are swallowed so reporting never breaks the
// app (e.g. under the plain dev-server, which has no such endpoint).
// Signature scheme matches the in-WASM BrowserErrorReporter:
// ExceptionType|TopFrame|FirstMessageLine (truncated server-side).
window.wfwasm.installErrorReporting = function () {
    if (window.wfwasm._errInstalled) return;
    window.wfwasm._errInstalled = true;
    var queue = [];
    var timer = 0;
    // Flight recorder: the WASM engine can die instantly (null-function
    // calli traps) with no managed trace. Keep the last console lines so
    // fatal posts carry the pre-death context (e.g. which .cctor ran last).
    var ring = [];
    function ringPush(text) {
        ring.push(String(text).slice(0, 300));
        if (ring.length > 300) ring.splice(0, ring.length - 300);
    }
    ["log", "info", "warn", "debug", "error"].forEach(function (level) {
        var orig = console[level] ? console[level].bind(console) : null;
        if (!orig) return;
        console[level] = function () {
            var text = "";
            try { text = Array.prototype.map.call(arguments, String).join(" "); } catch (e) { }
            try { ringPush(text); } catch (e) { }
            // Benign Mono loader chatter, kept in the ring above but not
            // echoed: every WASM P/Invoke probe first tries a dynamic
            // load (meaningless without dlopen), logs this "error", then
            // resolves via the statically-linked __Internal exports (the
            // "found via __Internal" line right after). Real failures
            // still surface as managed DllNotFound/EntryPointNotFound.
            if (text.indexOf("[MONO] DllImport error loading library '__Internal'") === 0) return;
            if (level === "error" && /Aborted\(|MONO.*Assertion|crit: Microsoft\.AspNetCore\.Components\.WebAssembly\.Rendering/.test(text))
                push("runtime-console", "RuntimeError", text, null, true);
            return orig.apply(null, arguments);
        };
    });
    function topOf(stack) {
        if (!stack) return "";
        var lines = String(stack).split("\n");
        for (var i = 0; i < lines.length; i++) {
            var m = /^\s*at\s+([^\s(]+)/.exec(lines[i]);
            if (m) {
                // Anonymous frames contain the navigation URL, whose session
                // query must never split identical crashes into unique rows.
                if (/^https?:\/\//.test(m[1])) {
                    var site = /^(.*?)(:\d+:\d+)$/.exec(m[1]);
                    try { return new URL(site ? site[1] : m[1]).pathname + (site ? site[2] : ""); } catch (e) { }
                }
                return m[1].split(".").slice(-2).join(".");
            }
        }
        return "";
    }
    function firstLine(v) {
        v = String(v == null ? "" : v);
        var nl = v.indexOf("\n");
        return (nl < 0 ? v : v.substring(0, nl)).trim();
    }
    function flush() {
        timer = 0;
        if (!queue.length) return;
        var batch = queue;
        queue = [];
        try {
            fetch("/api/client-errors", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(batch),
                keepalive: true
            }).catch(function () { });
        } catch (e) { /* reporting must never break the app */ }
    }
    function appSlug() {
        try {
            if (window.wfwasmRuntime) return window.wfwasmRuntime.app;
            var m = /^\/app\/([^\/?#]+)/.exec(location.pathname);
            return m ? decodeURIComponent(m[1]) : null;
        } catch (e) { return null; }
    }
    function push(source, name, message, stack, fatal) {
        name = String(name == null || name === "" ? "Error" : name);
        queue.push({
            signature: name + "|" + topOf(stack) + "|" + firstLine(message),
            message: firstLine(message),
            stack: stack == null ? null : String(stack),
            source: source,
            time: new Date().toISOString(),
            context: fatal ? ring.join("\n").slice(-8000) : null,
            app: appSlug(),
            appLabel: window.wfwasmRuntime?.label ?? null,
            sessionId: window.wfwasmRuntime?.session ?? null
        });
        if (fatal) window.wfwasmRuntime?.fail(firstLine(message));
        if (fatal) { if (timer) { clearTimeout(timer); timer = 0; } flush(); }
        else if (queue.length >= 10) { if (timer) { clearTimeout(timer); timer = 0; } flush(); }
        else if (!timer) timer = setTimeout(flush, 1000);
    }
    window.addEventListener("error", function (ev) {
        var err = ev.error;
        push("window.onerror", (err && err.name) || "Error", (err && err.message) || ev.message, err && err.stack, true);
    });
    window.addEventListener("unhandledrejection", function (ev) {
        var r = ev.reason;
        var name = (r && r.name) || "UnhandledRejection";
        var msg = (r && r.message) || String(r);
        push("unhandledrejection", name, msg, r && r.stack, true);
    });
    window.wfwasm.reportRuntimeFailure = function (error) {
        push("runtime-start", error?.name || "Error", error?.message || String(error), error?.stack, true);
    };
};
