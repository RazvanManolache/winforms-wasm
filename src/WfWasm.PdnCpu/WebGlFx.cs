using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

namespace WfWasm.PdnCpu;

// WebGL effect emulation (browser half of the missing Direct2D stack):
// maps a D2D effect + its property bag onto the PixiJS filter bridge
// (wwwroot/webgl-fx.js) and back. The JS call is synchronous, so the
// paint path stays sync. Every entry point is total: null/false on any
// failure (no browser, no WebGL, unmapped effect, bad pixels), and the
// caller keeps its CPU fallback. PDN pixels are BGRA uints; the bridge
// wants RGBA bytes, hence the swap pair.
public static partial class WebGlFx
{
    [JSImport("globalThis.wfwasmFxCaps")]
    internal static partial string GetCaps();

    [JSImport("globalThis.wfwasmFxApply")]
    internal static partial byte[]? ApplyEffect(byte[] rgba, int w, int h, string filter, string paramsJson);

    private static int s_caps = 0; // 0 = unprobed, 1 = available, -1 = unavailable
    private static string? s_capsText;

    public static bool IsAvailable
    {
        get
        {
            if (s_caps != 0) return s_caps > 0;
            try
            {
                s_capsText = GetCaps();
                s_caps = s_capsText is not null && s_capsText.StartsWith("ok:", StringComparison.Ordinal) ? 1 : -1;
            }
            catch
            {
                s_caps = -1;
            }

            return s_caps > 0;
        }
    }

    public static string CapsText
    {
        get
        {
            _ = IsAvailable;
            return s_capsText ?? "unprobed";
        }
    }

    // Effect identity is substring-matched: PDN wraps D2D effects under
    // versioned proxy typenames, so "GaussianBlur" matches
    // "…Effects.GaussianBlurEffect", "…GaussianBlurEffectProxy", etc.
    public static (string Filter, string ParamsJson)? MapEffect(string effectName, IReadOnlyDictionary<string, object?> props)
    {
        if (string.IsNullOrEmpty(effectName)) return null;
        if (effectName.Contains("GaussianBlur", StringComparison.OrdinalIgnoreCase))
            return ("kawase", JsonSerializer.Serialize(new { radius = PropFloat(props, 4f, "Radius", "StandardDeviation", "BlurRadius") }));
        if (effectName.Contains("ColorMatrix", StringComparison.OrdinalIgnoreCase))
        {
            float[]? m = PropMatrix(props, "Matrix", "ColorMatrix", "TransformMatrix");
            if (m is null || m.Length != 20) return null;
            return ("colormatrix", JsonSerializer.Serialize(new { matrix = m }));
        }

        if (effectName.Contains("Brightness", StringComparison.OrdinalIgnoreCase))
            return ("brightness", JsonSerializer.Serialize(new { value = PropFloat(props, 1f, "Value", "Brightness", "WhitePoint") }));
        if (effectName.Contains("Contrast", StringComparison.OrdinalIgnoreCase))
            return ("contrast", JsonSerializer.Serialize(new { value = PropFloat(props, 1f, "Value", "Contrast") }));
        if (effectName.Contains("Saturation", StringComparison.OrdinalIgnoreCase))
            return ("saturate", JsonSerializer.Serialize(new { value = PropFloat(props, 1f, "Value", "Saturation") }));
        if (effectName.Contains("HueRotation", StringComparison.OrdinalIgnoreCase))
            return ("hue", JsonSerializer.Serialize(new { degrees = PropFloat(props, 0f, "Angle", "Degrees", "Value") }));
        if (effectName.Contains("Grayscale", StringComparison.OrdinalIgnoreCase) || effectName.Contains("GreyScale", StringComparison.OrdinalIgnoreCase))
            return ("grayscale", "{}");
        if (effectName.Contains("Sepia", StringComparison.OrdinalIgnoreCase))
            return ("sepia", "{}");
        if (effectName.Contains("Invert", StringComparison.OrdinalIgnoreCase))
            return ("invert", "{}");
        if (effectName.Contains("Bloom", StringComparison.OrdinalIgnoreCase))
            return ("bloom", JsonSerializer.Serialize(new
            {
                strength = PropFloat(props, 1f, "Strength", "Intensity", "BloomScale"),
                radius = PropFloat(props, 8f, "Radius", "Blur"),
                threshold = PropFloat(props, 0.5f, "Threshold"),
            }));
        if (effectName.Contains("DropShadow", StringComparison.OrdinalIgnoreCase))
            return ("dropshadow", JsonSerializer.Serialize(new
            {
                blur = PropFloat(props, 4f, "BlurRadius", "Blur", "Radius"),
                distance = PropFloat(props, 8f, "Distance"),
                angle = PropFloat(props, (float)(Math.PI / 4), "Angle"),
                alpha = PropFloat(props, 0.5f, "Opacity", "Alpha"),
            }));
        if (effectName.Contains("ConvolveMatrix", StringComparison.OrdinalIgnoreCase) || effectName.Contains("Convolution", StringComparison.OrdinalIgnoreCase))
        {
            float[]? k = PropMatrix(props, "Kernel", "KernelMatrix", "Matrix");
            if (k is null || (k.Length != 9 && k.Length != 25)) return null;
            int s = k.Length == 9 ? 3 : 5;
            return ("convolution", JsonSerializer.Serialize(new { matrix = k, width = s, height = s }));
        }

        return null;
    }

    // Full render: BGRA uints in, BGRA uints out (fresh array), null on
    // any failure. The swaps are plain u32 rotates; SIMD later.
    public static uint[]? TryRender(string effectName, IReadOnlyDictionary<string, object?> props, uint[] bgra, int w, int h)
    {
        try
        {
            if (!IsAvailable) return null;
            var mapped = MapEffect(effectName, props);
            if (mapped is null) return null;
            if (bgra is null || w <= 0 || h <= 0 || bgra.Length != w * h) return null;
            byte[] rgba = new byte[bgra.Length * 4];
            BgraToRgba(bgra, rgba);
            byte[]? back = ApplyEffect(rgba, w, h, mapped.Value.Filter, mapped.Value.ParamsJson);
            if (back is null || back.Length != rgba.Length) return null;
            uint[] outBgra = new uint[bgra.Length];
            RgbaToBgra(back, outBgra);
            return outBgra;
        }
        catch
        {
            return null;
        }
    }

    public static void BgraToRgba(uint[] bgra, byte[] rgba)
    {
        for (int i = 0; i < bgra.Length; i++)
        {
            uint p = bgra[i];
            int o = i * 4;
            rgba[o] = (byte)(p >> 16);
            rgba[o + 1] = (byte)(p >> 8);
            rgba[o + 2] = (byte)p;
            rgba[o + 3] = (byte)(p >> 24);
        }
    }

    public static void RgbaToBgra(byte[] rgba, uint[] bgra)
    {
        for (int i = 0; i < bgra.Length; i++)
        {
            int o = i * 4;
            bgra[i] = (uint)(rgba[o + 2] | (rgba[o + 1] << 8) | (rgba[o] << 16) | (rgba[o + 3] << 24));
        }
    }

    internal static float PropFloat(IReadOnlyDictionary<string, object?> props, float dflt, params string[] names)
    {
        foreach (string n in names)
        {
            if (props.TryGetValue(n, out object? v) && v is not null)
            {
                try { return Convert.ToSingle(v); }
                catch { }
            }
        }

        return dflt;
    }

    internal static float[]? PropMatrix(IReadOnlyDictionary<string, object?> props, params string[] names)
    {
        foreach (string n in names)
        {
            if (!props.TryGetValue(n, out object? v) || v is null) continue;
            if (v is float[] fa) return (float[])fa.Clone();
            if (v is double[] da) return da.Select(d => (float)d).ToArray();
            if (v is Array a)
            {
                try
                {
                    float[] f = new float[a.Length];
                    for (int i = 0; i < a.Length; i++) f[i] = Convert.ToSingle(a.GetValue(i));
                    return f;
                }
                catch { }
            }
        }

        return null;
    }
}
