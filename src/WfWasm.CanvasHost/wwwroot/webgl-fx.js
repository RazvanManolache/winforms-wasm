// WebGL effect bridge: runs PixiJS filters on raw RGBA pixels using one
// lazily-created offscreen renderer. Input/output are Uint8Array row-major
// RGBA (top-down), same length in and out. Unknown filters answer null so
// the caller keeps its CPU fallback. Synchronous by design: every WebGL
// call below (upload, render, readPixels) is sync, so C# binds it with
// [JSImport] and the paint path stays sync (no async ripple).
// Classic script (like canvas-bridge.js): needs pixi.min.js and
// pixi-filters.js loaded first (see index.html).
window.wfwasm = window.wfwasm || {};
window.wfwasm.fx = (function () {
    var renderer = null;
    var status = null;

    function ensure() {
        if (status) return status;
        try {
            if (!window.PIXI) { status = "no-pixi"; return status; }
            if (!PIXI.utils || !PIXI.utils.isWebGLSupported()) { status = "no-webgl"; return status; }
            renderer = new PIXI.Renderer({
                width: 8, height: 8, backgroundAlpha: 0,
                antialias: false, powerPreference: "high-performance"
            });
            status = "ok:webgl" + ((renderer.context && renderer.context.webGLVersion) || "?");
        } catch (e) {
            status = "error:" + ((e && e.message) || e);
        }
        return status;
    }

    // D2D parameter flavor in, Pixi filter out. Matrix input for
    // "colormatrix" is Direct2D 5x4 row-major (20 floats: R,G,B,A,offset
    // rows x R,G,B,A out cols); Pixi wants 4x5, so it is transposed here.
    function makeFilter(name, p) {
        var F = PIXI.filters;
        switch (name) {
            case "blur":
                return new F.BlurFilter(p.radius || 4, 4);
            case "kawase":
                return new F.KawaseBlurFilter(p.radius || 4, p.quality || 3);
            case "colormatrix": {
                var m = p.matrix;
                if (!m || m.length !== 20) return null;
                var t = new Array(20);
                for (var r = 0; r < 4; r++)
                    for (var c = 0; c < 5; c++)
                        t[r * 5 + c] = m[c * 4 + r];
                var cm = new F.ColorMatrixFilter();
                cm.matrix = t;
                return cm;
            }
            case "adjustment":
                return new F.AdjustmentFilter(p);
            case "bloom":
                return new F.AdvancedBloomFilter({
                    threshold: p.threshold == null ? 0.5 : p.threshold,
                    bloomScale: p.strength == null ? 1.0 : p.strength,
                    brightness: 1.0,
                    blur: p.radius == null ? 8 : p.radius,
                    quality: 5
                });
            case "dropshadow":
                return new F.DropShadowFilter({
                    blur: p.blur == null ? 4 : p.blur,
                    distance: p.distance == null ? 8 : p.distance,
                    angle: p.angle == null ? Math.PI / 4 : p.angle,
                    color: p.color == null ? 0x000000 : p.color,
                    alpha: p.alpha == null ? 0.5 : p.alpha
                });
            case "glow":
                return new F.GlowFilter({
                    distance: p.distance == null ? 10 : p.distance,
                    outerStrength: p.outer == null ? 2 : p.outer,
                    innerStrength: p.inner == null ? 0 : p.inner,
                    color: p.color == null ? 0xffffff : p.color,
                    quality: 0.5
                });
            case "convolution": {
                var k = p.matrix;
                var w = p.width || 3, h = p.height || 3;
                if (!k || k.length !== w * h) return null;
                return new F.ConvolutionFilter(k, w, h);
            }
            case "grayscale":
                return new F.GrayscaleFilter();
            case "sepia": {
                var s = new F.ColorMatrixFilter();
                s.sepia(false);
                return s;
            }
            case "invert": {
                var n = new F.ColorMatrixFilter();
                n.negative(false);
                return n;
            }
            case "brightness": {
                var b = new F.ColorMatrixFilter();
                b.brightness(p.value == null ? 1 : p.value, false);
                return b;
            }
            case "contrast": {
                var c2 = new F.ColorMatrixFilter();
                c2.contrast(p.value == null ? 1 : p.value, false);
                return c2;
            }
            case "saturate": {
                var s2 = new F.ColorMatrixFilter();
                s2.saturate(p.value == null ? 1 : p.value, false);
                return s2;
            }
            case "hue": {
                var h2 = new F.ColorMatrixFilter();
                h2.hue(p.degrees || 0, false);
                return h2;
            }
            default:
                return null;
        }
    }

    function apply(rgbaIn, w, h, name, paramsJson) {
        if (ensure().indexOf("ok:") !== 0) return null;
        if (!rgbaIn || rgbaIn.length < w * h * 4 || w <= 0 || h <= 0) return null;
        var filter;
        try {
            filter = makeFilter(name, JSON.parse(paramsJson || "{}"));
        } catch (e) { filter = null; }
        if (!filter) return null;
        try {
            renderer.resize(w, h);
            var tex = PIXI.Texture.fromBuffer(rgbaIn, w, h);
            // PDN bitmaps (like D2D) are premultiplied: skip Pixi's
            // upload conversion so filter math matches desktop.
            if (tex.baseTexture) tex.baseTexture.alphaMode = PIXI.ALPHA_MODES.NO_PREMULTIPLIED_ALPHA;
            var sprite = new PIXI.Sprite(tex);
            sprite.filters = [filter];
            var stage = new PIXI.Container();
            stage.addChild(sprite);
            var rt = PIXI.RenderTexture.create({ width: w, height: h });
            renderer.render(stage, { renderTexture: rt });
            var px = renderer.extract.pixels(rt);
            var out = new Uint8Array(px.buffer, px.byteOffset, px.byteLength);
            rt.destroy(true);
            tex.destroy(true);
            sprite.destroy();
            stage.destroy();
            return out.length === w * h * 4 ? out : null;
        } catch (e) {
            try { console.warn("[webgl-fx] apply failed: " + ((e && e.message) || e)); } catch (_) { }
            return null;
        }
    }

    return { caps: ensure, apply: apply };
})();
// Flat aliases: [JSImport] binds globals by dotted path; the flat names
// keep the binding one lookup deep.
window.wfwasmFxCaps = function () { return window.wfwasm.fx.caps(); };
window.wfwasmFxApply = function (rgba, w, h, name, params) { return window.wfwasm.fx.apply(rgba, w, h, name, params); };
