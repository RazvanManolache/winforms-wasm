// GDI+ effects: exact .NET member shapes; the CPU subset (matrix, levels,
// LUT, tint, balance, blur, sharpen, curves) applies in DrawImage/ConvertFormat.
namespace System.Drawing.Imaging.Effects
{
    /// <summary>Effect base (cf. System.Drawing.Imaging.Effects.Effect).</summary>
    public abstract class Effect : IDisposable
    {
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        ~Effect()
        {
            Dispose(false);
        }

        internal virtual void ApplyTo(Bitmap bitmap, Rectangle region)
        {
        }

        internal void ApplyTo(Bitmap bitmap) => ApplyTo(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
    }

    /// <summary>Matrix effect (cf. System.Drawing.Imaging.Effects.ColorMatrixEffect).</summary>
    public class ColorMatrixEffect : Effect
    {
        public ColorMatrixEffect(ColorMatrix matrix)
        {
            ArgumentNullException.ThrowIfNull(matrix);
            Matrix = matrix;
        }

        public ColorMatrix Matrix { get; }

        internal override void ApplyTo(Bitmap bitmap, Rectangle region)
        {
            for (int y = Math.Max(0, region.Y); y < Math.Min(bitmap.Height, region.Bottom); y++)
                for (int x = Math.Max(0, region.X); x < Math.Min(bitmap.Width, region.Right); x++)
                    bitmap.SetPixel(x, y, Matrix.Apply(bitmap.GetPixel(x, y)));
        }
    }

    /// <summary>Grayscale effect (cf. System.Drawing.Imaging.Effects.GrayScaleEffect).</summary>
    public sealed class GrayScaleEffect : ColorMatrixEffect
    {
        public GrayScaleEffect()
            : base(new ColorMatrix(new float[][]
            {
                new float[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
                new float[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
                new float[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
                new float[] { 0f, 0f, 0f, 1f, 0f },
                new float[] { 0f, 0f, 0f, 0f, 1f },
            }))
        {
        }
    }

    /// <summary>Invert effect (cf. System.Drawing.Imaging.Effects.InvertEffect).</summary>
    public sealed class InvertEffect : ColorMatrixEffect
    {
        public InvertEffect()
            : base(new ColorMatrix(new float[][]
            {
                new float[] { -1f, 0f, 0f, 0f, 0f },
                new float[] { 0f, -1f, 0f, 0f, 0f },
                new float[] { 0f, 0f, -1f, 0f, 0f },
                new float[] { 0f, 0f, 0f, 1f, 0f },
                new float[] { 1f, 1f, 1f, 1f, 1f },
            }))
        {
        }
    }

    /// <summary>Sepia effect (cf. System.Drawing.Imaging.Effects.SepiaEffect).</summary>
    public sealed class SepiaEffect : ColorMatrixEffect
    {
        public SepiaEffect()
            : base(new ColorMatrix(new float[][]
            {
                new float[] { 0.393f, 0.349f, 0.272f, 0f, 0f },
                new float[] { 0.769f, 0.686f, 0.534f, 0f, 0f },
                new float[] { 0.189f, 0.168f, 0.131f, 0f, 0f },
                new float[] { 0f, 0f, 0f, 1f, 0f },
                new float[] { 0f, 0f, 0f, 0f, 1f },
            }))
        {
        }
    }

    /// <summary>Vivid effect (cf. System.Drawing.Imaging.Effects.VividEffect).</summary>
    public sealed class VividEffect : ColorMatrixEffect
    {
        public VividEffect()
            : base(new ColorMatrix(new float[][]
            {
                new float[] { 1.2f, -0.1f, -0.1f, 0f, 0f },
                new float[] { -0.1f, 1.2f, -0.1f, 0f, 0f },
                new float[] { -0.1f, -0.1f, 1.2f, 0f, 0f },
                new float[] { 0f, 0f, 0f, 1f, 0f },
                new float[] { 0f, 0f, 0f, 0f, 1f },
            }))
        {
        }
    }

    /// <summary>Brightness/contrast (cf. System.Drawing.Imaging.Effects.BrightnessContrastEffect).</summary>
    public sealed class BrightnessContrastEffect : Effect
    {
        public BrightnessContrastEffect(int brightnessLevel, int contrastLevel)
        {
            BrightnessLevel = brightnessLevel;
            ContrastLevel = contrastLevel;
        }

        public int BrightnessLevel { get; }
        public int ContrastLevel { get; }

        internal override void ApplyTo(Bitmap bitmap, Rectangle region)
        {
            float scale = 1f + (ContrastLevel / 100f);
            float offset = (BrightnessLevel * 255f / 100f) + (128f * (1f - scale));
            for (int y = Math.Max(0, region.Y); y < Math.Min(bitmap.Height, region.Bottom); y++)
            {
                for (int x = Math.Max(0, region.X); x < Math.Min(bitmap.Width, region.Right); x++)
                {
                    Color c = bitmap.GetPixel(x, y);
                    bitmap.SetPixel(x, y, Color.FromArgb(c.A,
                        Clamp((c.R * scale) + offset),
                        Clamp((c.G * scale) + offset),
                        Clamp((c.B * scale) + offset)));
                }
            }
        }

        private static int Clamp(float v) => v < 0f ? 0 : v > 255f ? 255 : (int)(v + 0.5f);
    }

    /// <summary>Channel balance (cf. System.Drawing.Imaging.Effects.ColorBalanceEffect).</summary>
    public sealed class ColorBalanceEffect : Effect
    {
        public ColorBalanceEffect(int cyanRed, int magentaGreen, int yellowBlue)
        {
            CyanRed = cyanRed;
            MagentaGreen = magentaGreen;
            YellowBlue = yellowBlue;
        }

        public int CyanRed { get; }
        public int MagentaGreen { get; }
        public int YellowBlue { get; }

        internal override void ApplyTo(Bitmap bitmap, Rectangle region)
        {
            float dr = -(CyanRed * 255f / 100f);
            float dg = -(MagentaGreen * 255f / 100f);
            float db = -(YellowBlue * 255f / 100f);
            for (int y = Math.Max(0, region.Y); y < Math.Min(bitmap.Height, region.Bottom); y++)
            {
                for (int x = Math.Max(0, region.X); x < Math.Min(bitmap.Width, region.Right); x++)
                {
                    Color c = bitmap.GetPixel(x, y);
                    bitmap.SetPixel(x, y, Color.FromArgb(c.A,
                        Clamp(c.R + dr), Clamp(c.G + dg), Clamp(c.B + db)));
                }
            }
        }

        private static int Clamp(float v) => v < 0f ? 0 : v > 255f ? 255 : (int)(v + 0.5f);
    }

    /// <summary>Levels stretch (cf. System.Drawing.Imaging.Effects.LevelsEffect).</summary>
    public sealed class LevelsEffect : Effect
    {
        public LevelsEffect(int highlight, int midtone, int shadow)
        {
            Highlight = highlight;
            Midtone = midtone;
            Shadow = shadow;
        }

        public int Highlight { get; }
        public int Midtone { get; }
        public int Shadow { get; }

        internal override void ApplyTo(Bitmap bitmap, Rectangle region)
        {
            float lo = Math.Clamp(Shadow, 0, 255);
            float hi = Math.Clamp(Highlight, 0, 255);
            if (hi <= lo)
                return;
            float mid = Math.Clamp((Midtone - lo) / (hi - lo), 0.001f, 0.999f);
            float gamma = MathF.Log(0.5f) / MathF.Log(mid);
            for (int y = Math.Max(0, region.Y); y < Math.Min(bitmap.Height, region.Bottom); y++)
            {
                for (int x = Math.Max(0, region.X); x < Math.Min(bitmap.Width, region.Right); x++)
                {
                    Color c = bitmap.GetPixel(x, y);
                    bitmap.SetPixel(x, y, Color.FromArgb(c.A,
                        Map(c.R, lo, hi, gamma),
                        Map(c.G, lo, hi, gamma),
                        Map(c.B, lo, hi, gamma)));
                }
            }
        }

        private static int Map(float v, float lo, float hi, float gamma)
        {
            float t = Math.Clamp((v - lo) / (hi - lo), 0f, 1f);
            return Math.Clamp((int)(MathF.Pow(t, gamma) * 255f + 0.5f), 0, 255);
        }
    }
    /// <summary>Channel lookup tables (cf. System.Drawing.Imaging.Effects.ColorLookupTableEffect).</summary>
    public sealed class ColorLookupTableEffect : Effect
    {
        public ColorLookupTableEffect(byte[] redLookupTable, byte[] greenLookupTable, byte[] blueLookupTable, byte[] alphaLookupTable)
        {
            ArgumentNullException.ThrowIfNull(redLookupTable);
            ArgumentNullException.ThrowIfNull(greenLookupTable);
            ArgumentNullException.ThrowIfNull(blueLookupTable);
            ArgumentNullException.ThrowIfNull(alphaLookupTable);
            RedLookupTable = redLookupTable;
            GreenLookupTable = greenLookupTable;
            BlueLookupTable = blueLookupTable;
            AlphaLookupTable = alphaLookupTable;
        }

        public ColorLookupTableEffect(ReadOnlySpan<byte> redLookupTable, ReadOnlySpan<byte> greenLookupTable, ReadOnlySpan<byte> blueLookupTable, ReadOnlySpan<byte> alphaLookupTable)
            : this(redLookupTable.ToArray(), greenLookupTable.ToArray(), blueLookupTable.ToArray(), alphaLookupTable.ToArray())
        {
        }

        public ReadOnlyMemory<byte> RedLookupTable { get; }
        public ReadOnlyMemory<byte> GreenLookupTable { get; }
        public ReadOnlyMemory<byte> BlueLookupTable { get; }
        public ReadOnlyMemory<byte> AlphaLookupTable { get; }

        internal override void ApplyTo(Bitmap bitmap, Rectangle region)
        {
            for (int y = Math.Max(0, region.Y); y < Math.Min(bitmap.Height, region.Bottom); y++)
            {
                for (int x = Math.Max(0, region.X); x < Math.Min(bitmap.Width, region.Right); x++)
                {
                    Color c = bitmap.GetPixel(x, y);
                    bitmap.SetPixel(x, y, Color.FromArgb(Lookup(AlphaLookupTable.Span, c.A), Lookup(RedLookupTable.Span, c.R), Lookup(GreenLookupTable.Span, c.G), Lookup(BlueLookupTable.Span, c.B)));
                }
            }
        }

        private static int Lookup(ReadOnlySpan<byte> table, int v) => table.IsEmpty ? v : table[Math.Clamp(v, 0, table.Length - 1)];
    }

    /// <summary>Color tint (cf. System.Drawing.Imaging.Effects.TintEffect).</summary>
    public sealed class TintEffect : Effect
    {
        public TintEffect(Color color, int amount)
        {
            Hue = ColorToHue(color);
            Amount = amount;
        }

        public TintEffect(int hue, int amount)
        {
            Hue = hue;
            Amount = amount;
        }

        public int Hue { get; }
        public int Amount { get; }

        internal override void ApplyTo(Bitmap bitmap, Rectangle region)
        {
            Color tint = HueToColor(Hue);
            float t = Math.Clamp(Amount / 100f, 0f, 1f);
            for (int y = Math.Max(0, region.Y); y < Math.Min(bitmap.Height, region.Bottom); y++)
            {
                for (int x = Math.Max(0, region.X); x < Math.Min(bitmap.Width, region.Right); x++)
                {
                    Color c = bitmap.GetPixel(x, y);
                    bitmap.SetPixel(x, y, Color.FromArgb(c.A, Lerp(c.R, tint.R, t), Lerp(c.G, tint.G, t), Lerp(c.B, tint.B, t)));
                }
            }
        }

        private static int Lerp(int a, int b, float t) => Math.Clamp((int)(a + ((b - a) * t) + 0.5f), 0, 255);

        private static int ColorToHue(Color color)
        {
            float r = color.R / 255f, g = color.G / 255f, b = color.B / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            if (max == min)
                return 0;
            float h = max == r ? (g - b) / (max - min) : max == g ? 2f + ((b - r) / (max - min)) : 4f + ((r - g) / (max - min));
            h *= 60f;
            return (int)(h < 0f ? h + 360f : h);
        }

        private static Color HueToColor(int hue)
        {
            float h = ((hue % 360) + 360) % 360 / 60f;
            float c = 1f;
            float x = c * (1f - Math.Abs((h % 2f) - 1f));
            float r = 0f, g = 0f, b = 0f;
            if (h < 1f) { r = c; g = x; }
            else if (h < 2f) { r = x; g = c; }
            else if (h < 3f) { g = c; b = x; }
            else if (h < 4f) { g = x; b = c; }
            else if (h < 5f) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromArgb(255, (int)(r * 255f), (int)(g * 255f), (int)(b * 255f));
        }
    }
    /// <summary>Box-blur effect (cf. System.Drawing.Imaging.Effects.BlurEffect).</summary>
    public class BlurEffect : Effect
    {
        public BlurEffect(float radius, bool expandEdge)
        {
            Radius = radius;
            ExpandEdge = expandEdge;
        }

        public float Radius { get; }
        public bool ExpandEdge { get; }

        internal override void ApplyTo(Bitmap bitmap, Rectangle region)
        {
            int r = (int)Math.Ceiling(Math.Max(0f, Radius));
            if (r > 0)
                BoxBlur(bitmap, region, r);
        }

        internal static void BoxBlur(Bitmap bitmap, Rectangle region, int r)
        {
            int w = bitmap.Width;
            int h = bitmap.Height;
            int x0 = Math.Max(0, region.X);
            int y0 = Math.Max(0, region.Y);
            int x1 = Math.Min(w, region.Right);
            int y1 = Math.Min(h, region.Bottom);
            if (x1 <= x0 || y1 <= y0)
                return;
            int ey0 = Math.Max(0, y0 - r);
            int ey1 = Math.Min(h, y1 + r);
            int[] src = bitmap.Pixels;
            int[] tmp = new int[src.Length];
            Array.Copy(src, tmp, src.Length);
            for (int y = ey0; y < ey1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    long a = 0, rr = 0, g = 0, b = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int p = src[y * w + Math.Clamp(x + k, 0, w - 1)];
                        a += (p >> 24) & 0xFF;
                        rr += (p >> 16) & 0xFF;
                        g += (p >> 8) & 0xFF;
                        b += p & 0xFF;
                    }
                    int n = 2 * r + 1;
                    tmp[y * w + x] = ((int)(a / n) << 24) | ((int)(rr / n) << 16) | ((int)(g / n) << 8) | (int)(b / n);
                }
            }
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    long a = 0, rr = 0, g = 0, b = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int p = tmp[Math.Clamp(y + k, 0, h - 1) * w + x];
                        a += (p >> 24) & 0xFF;
                        rr += (p >> 16) & 0xFF;
                        g += (p >> 8) & 0xFF;
                        b += p & 0xFF;
                    }
                    int n = 2 * r + 1;
                    src[y * w + x] = ((int)(a / n) << 24) | ((int)(rr / n) << 16) | ((int)(g / n) << 8) | (int)(b / n);
                }
            }
        }
    }

    /// <summary>Unsharp-mask effect (cf. System.Drawing.Imaging.Effects.SharpenEffect).</summary>
    public class SharpenEffect : Effect
    {
        public SharpenEffect(float radius, float amount)
        {
            Radius = radius;
            Amount = amount;
        }

        public float Radius { get; }
        public float Amount { get; }

        internal override void ApplyTo(Bitmap bitmap, Rectangle region)
        {
            int r = (int)Math.Ceiling(Math.Max(0f, Radius));
            float amt = Math.Max(0f, Amount) / 100f;
            if (r <= 0 || amt <= 0f)
                return;
            int w = bitmap.Width;
            int h = bitmap.Height;
            int x0 = Math.Max(0, region.X);
            int y0 = Math.Max(0, region.Y);
            int x1 = Math.Min(w, region.Right);
            int y1 = Math.Min(h, region.Bottom);
            if (x1 <= x0 || y1 <= y0)
                return;
            int[] src = (int[])bitmap.Pixels.Clone();
            var blurred = new Bitmap(w, h);
            Array.Copy(src, blurred.Pixels, src.Length);
            BlurEffect.BoxBlur(blurred, region, r);
            int[] px = bitmap.Pixels;
            int[] bp = blurred.Pixels;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int i = y * w + x;
                    int s = src[i];
                    int bl = bp[i];
                    int a = (s >> 24) & 0xFF;
                    int nr = SharpenChannel((s >> 16) & 0xFF, (bl >> 16) & 0xFF, amt);
                    int ng = SharpenChannel((s >> 8) & 0xFF, (bl >> 8) & 0xFF, amt);
                    int nb = SharpenChannel(s & 0xFF, bl & 0xFF, amt);
                    px[i] = (a << 24) | (nr << 16) | (ng << 8) | nb;
                }
            }
        }

        private static int SharpenChannel(int src, int blurred, float amt) =>
            Math.Clamp((int)(src + amt * (src - blurred)), 0, 255);
    }

    /// <summary>Per-channel curve base (cf. System.Drawing.Imaging.Effects.ColorCurveEffect).</summary>
    public abstract class ColorCurveEffect : Effect
    {
        protected ColorCurveEffect(CurveChannel channel)
        {
            Channel = channel;
        }

        public CurveChannel Channel { get; }

        internal virtual float Map(float v) => v;

        internal override void ApplyTo(Bitmap bitmap, Rectangle region)
        {
            bool useR = Channel is CurveChannel.All or CurveChannel.Red;
            bool useG = Channel is CurveChannel.All or CurveChannel.Green;
            bool useB = Channel is CurveChannel.All or CurveChannel.Blue;
            for (int y = Math.Max(0, region.Y); y < Math.Min(bitmap.Height, region.Bottom); y++)
            {
                for (int x = Math.Max(0, region.X); x < Math.Min(bitmap.Width, region.Right); x++)
                {
                    Color c = bitmap.GetPixel(x, y);
                    int r = useR ? MapToByte(c.R) : c.R;
                    int g = useG ? MapToByte(c.G) : c.G;
                    int b = useB ? MapToByte(c.B) : c.B;
                    bitmap.SetPixel(x, y, Color.FromArgb(c.A, r, g, b));
                }
            }
        }

        private int MapToByte(byte v) => Math.Clamp((int)(Map(v / 255f) * 255f), 0, 255);
    }

    /// <summary>Contrast curve (cf. System.Drawing.Imaging.Effects.ContrastCurveEffect).</summary>
    public class ContrastCurveEffect : ColorCurveEffect
    {
        public ContrastCurveEffect(CurveChannel channel, int contrast)
            : base(channel)
        {
            Contrast = contrast;
        }

        public int Contrast { get; }

        internal override float Map(float v)
        {
            float c = Math.Clamp(Contrast, -100, 100);
            float f = (259f * (c + 255f)) / (255f * (259f - c));
            return f * (v - 0.5f) + 0.5f;
        }
    }

    /// <summary>Density curve (cf. System.Drawing.Imaging.Effects.DensityCurveEffect).</summary>
    public class DensityCurveEffect : ColorCurveEffect
    {
        public DensityCurveEffect(CurveChannel channel, int density)
            : base(channel)
        {
            Density = density;
        }

        public int Density { get; }

        internal override float Map(float v) =>
            (float)(v / Math.Pow(2.0, Math.Clamp(Density, -100, 100) / 100.0));
    }

    /// <summary>Exposure curve (cf. System.Drawing.Imaging.Effects.ExposureCurveEffect).</summary>
    public class ExposureCurveEffect : ColorCurveEffect
    {
        public ExposureCurveEffect(CurveChannel channel, int exposure)
            : base(channel)
        {
            Exposure = exposure;
        }

        public int Exposure { get; }

        internal override float Map(float v) =>
            (float)(v * Math.Pow(2.0, Math.Clamp(Exposure, -100, 100) / 100.0));
    }

    /// <summary>Black-saturation curve (cf. System.Drawing.Imaging.Effects.BlackSaturationCurveEffect).</summary>
    public class BlackSaturationCurveEffect : ColorCurveEffect
    {
        public BlackSaturationCurveEffect(CurveChannel channel, int blackSaturation)
            : base(channel)
        {
            BlackSaturation = blackSaturation;
        }

        public int BlackSaturation { get; }

        internal override float Map(float v)
        {
            float b = Math.Clamp(BlackSaturation, 0, 100) / 100f;
            return b >= 1f ? 0f : (v - b) / (1f - b);
        }
    }

    /// <summary>White-saturation curve (cf. System.Drawing.Imaging.Effects.WhiteSaturationCurveEffect).</summary>
    public class WhiteSaturationCurveEffect : ColorCurveEffect
    {
        public WhiteSaturationCurveEffect(CurveChannel channel, int whiteSaturation)
            : base(channel)
        {
            WhiteSaturation = whiteSaturation;
        }

        public int WhiteSaturation { get; }

        internal override float Map(float v)
        {
            float wp = 1f - (Math.Clamp(WhiteSaturation, 0, 100) / 100f);
            return wp <= 0f ? 1f : v / wp;
        }
    }

    /// <summary>Shadow curve (cf. System.Drawing.Imaging.Effects.ShadowCurveEffect).</summary>
    public class ShadowCurveEffect : ColorCurveEffect
    {
        public ShadowCurveEffect(CurveChannel channel, int shadow)
            : base(channel)
        {
            Shadow = shadow;
        }

        public int Shadow { get; }

        internal override float Map(float v)
        {
            float s = Math.Clamp(Shadow, -100, 100) / 100f;
            return v + (s * v * (1f - v));
        }
    }

    /// <summary>Midtone curve (cf. System.Drawing.Imaging.Effects.MidtoneCurveEffect).</summary>
    public class MidtoneCurveEffect : ColorCurveEffect
    {
        public MidtoneCurveEffect(CurveChannel channel, int midtone)
            : base(channel)
        {
            Midtone = midtone;
        }

        public int Midtone { get; }

        internal override float Map(float v) =>
            (float)Math.Pow(Math.Max(0f, v), Math.Pow(2.0, -Math.Clamp(Midtone, -100, 100) / 100.0));
    }

    /// <summary>Highlight curve (cf. System.Drawing.Imaging.Effects.HighlightCurveEffect).</summary>
    public class HighlightCurveEffect : ColorCurveEffect
    {
        public HighlightCurveEffect(CurveChannel channel, int highlight)
            : base(channel)
        {
            Highlight = highlight;
        }

        public int Highlight { get; }

        internal override float Map(float v)
        {
            float h = Math.Clamp(Highlight, -100, 100) / 100f;
            return v + (h * v * v * (1f - v) * 2f);
        }
    }
}
