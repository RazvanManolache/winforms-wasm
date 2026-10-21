using System.Collections.Concurrent;
using System.Drawing;

namespace System.Windows.Forms;

// FromHwnd overlay bridge: desktop painters (Telerik spell squiggles,
// adorner layers) draw onto a window surface via Graphics.FromHwnd,
// but headless windows have no surface. The Drawing side paints onto a
// per-window overlay bitmap instead; the host composites it after the
// control. Each committed overlay replaces the previous one for that
// window (painters repaint their whole layer), and size-mismatched or
// orphaned overlays are dropped on read.
public static class WasmOverlayBridge
{
    private static readonly ConcurrentDictionary<nint, Bitmap> s_overlays = new();

    public static Size OverlaySizeFor(IntPtr hwnd)
    {
        try
        {
            Control? control = Control.FromHandle(hwnd);
            if (control is null || control.IsDisposed)
                return Size.Empty;
            Size size = control.ClientSize;
            return size.Width > 0 && size.Height > 0 ? size : Size.Empty;
        }
        catch
        {
            return Size.Empty;
        }
    }

    public static void CommitOverlay(IntPtr hwnd, Bitmap bitmap)
    {
        try
        {
            Control? control = Control.FromHandle(hwnd);
            if (control is null || control.IsDisposed)
            {
                bitmap.Dispose();
                s_overlays.TryRemove((nint)hwnd, out _);
                return;
            }

            if (s_overlays.TryGetValue((nint)hwnd, out Bitmap? prev) && !ReferenceEquals(prev, bitmap))
            {
                if (prev is not null && OverlayBytesEqual(prev, bitmap))
                {
                    // Identical re-commit: subclass painters (spell
                    // squiggles) repaint their whole layer on every
                    // synthetic WM_PAINT, usually pixel-identical to the
                    // stored one. Keep the stored bitmap, drop the
                    // duplicate, and do NOT invalidate: invalidating here
                    // re-marks the control dirty, which re-pumps a frame,
                    // which re-delivers WM_PAINT — a perpetual repaint
                    // loop that saturates the browser main thread (the
                    // spellcheck "hang": ~1fps forever, screenshots and
                    // input starved, no crash and no error).
                    try { bitmap.Dispose(); } catch { }
                    return;
                }

                try { prev?.Dispose(); } catch { }
            }

            s_overlays[(nint)hwnd] = bitmap;
            control.Invalidate();
        }
        catch
        {
        }
    }

    // Byte-exact overlay compare. Any failure (or absurd size)
    // answers "changed": over-invalidating is always safe, while a
    // false "equal" would freeze stale pixels on screen.
    private static bool OverlayBytesEqual(Bitmap a, Bitmap b)
    {
        try
        {
            if (a.Width != b.Width || a.Height != b.Height)
                return false;
            if (a.Width <= 0 || a.Height <= 0)
                return true;
            if ((long)a.Width * a.Height * 4 > 4L * 1024 * 1024)
                return false;
            byte[] ba = a.ToRgbaBytes();
            byte[] bb = b.ToRgbaBytes();
            return ba.AsSpan().SequenceEqual(bb);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetOverlay(IntPtr hwnd, Size clientSize, out Bitmap? bitmap)
    {
        bitmap = null;
        try
        {
            if (!s_overlays.TryGetValue((nint)hwnd, out Bitmap? found) || found is null)
                return false;
            Control? control = Control.FromHandle(hwnd);
            if (control is null || control.IsDisposed || found.Width != clientSize.Width || found.Height != clientSize.Height)
            {
                if (s_overlays.TryRemove((nint)hwnd, out Bitmap? stale))
                {
                    try { stale?.Dispose(); } catch { }
                }

                return false;
            }

            bitmap = found;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

// Edit-control layout answers for EM_POSFROMCHAR/EM_CHARFROMPOS and
// the line queries. Geometry mirrors the renderer's single-run paint
// (text drawn from x=5 on one vertically centered line): callers get
// positions that land on the pixels the host actually draws. Newlines
// flatten to spaces for positioning (the base multiline paint bug is
// separate); line APIs still report logical lines.
public static class WasmEditLayout
{
    private const int TextMarginX = 5;

    private static string[] LogicalLines(TextBoxBase box) =>
        (box.Text ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.None);

    private static int LineHeight(Font font)
    {
        try
        {
            float em = font.Unit == GraphicsUnit.Pixel ? font.Size : font.Size * 96f / 72f;
            return Math.Max(1, (int)Math.Ceiling(em * FontFamily.GdiLineBoxFactor(font.Name)));
        }
        catch
        {
            return 16;
        }
    }

    private static float PrefixWidth(string prefix, Font font)
    {
        if (string.IsNullOrEmpty(prefix))
            return 0;
        try
        {
            using var scratch = new Bitmap(1, 1);
            using var graphics = Graphics.FromImage(scratch);
            float em = font.Unit == GraphicsUnit.Pixel ? font.Size : font.Size * 96f / 72f;
            if (prefix.IndexOf((char)10) < 0 && prefix.IndexOf((char)13) < 0
                && GdiAbcTable.TryMeasureString(font.Name, em, prefix, out int abc))
                return abc;
            return graphics.MeasureStringTight(prefix, font).Width;
        }
        catch
        {
            return prefix.Length * font.SizeInPoints;
        }
    }

    // Flattened run the renderer paints, with a map from flattened
    // offsets back to source offsets (\r\n counts two source chars).
    private static (string run, int[] source) FlattenRun(string text)
    {
        var run = new System.Text.StringBuilder(text.Length);
        var source = new List<int>(text.Length + 1) { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                run.Append(' ');
                i++;
                source.Add(i + 1);
            }
            else if (text[i] == '\n')
            {
                run.Append(' ');
                source.Add(i + 1);
            }
            else
            {
                run.Append(text[i]);
                source.Add(i + 1);
            }
        }

        return (run.ToString(), source.ToArray());
    }

    public static Point PositionFromChar(TextBoxBase box, int index)
    {
        try
        {
            string text = box.Text ?? string.Empty;
            if (index < 0)
                index = 0;
            if (index > text.Length)
                index = text.Length;
            (string run, int[] source) = FlattenRun(text);
            int flat = 0;
            while (flat < run.Length && source[flat] < index)
                flat++;
            float x = TextMarginX + PrefixWidth(run.Substring(0, flat), box.Font);
            int lineH = LineHeight(box.Font);
            int y = (box.ClientSize.Height - lineH) / 2;
            return new Point((int)Math.Round(x), Math.Max(0, y));
        }
        catch
        {
            return Point.Empty;
        }
    }

    public static int CharFromPos(TextBoxBase box, int x, int y)
    {
        try
        {
            string text = box.Text ?? string.Empty;
            if (text.Length == 0)
                return 0;
            int lineH = LineHeight(box.Font);
            int top = (box.ClientSize.Height - lineH) / 2;
            if (y < top)
                return 0;
            (string run, int[] source) = FlattenRun(text);
            float rel = x - TextMarginX;
            if (rel <= 0)
                return 0;
            int best = run.Length;
            for (int flat = 1; flat <= run.Length; flat++)
            {
                float w = PrefixWidth(run.Substring(0, flat), box.Font);
                if (w >= rel)
                {
                    float prev = flat == 1 ? 0 : PrefixWidth(run.Substring(0, flat - 1), box.Font);
                    best = (w - rel) < (rel - prev) ? flat : flat - 1;
                    break;
                }
            }

            return source[Math.Min(best, source.Length - 1)];
        }
        catch
        {
            return 0;
        }
    }

    public static int LineIndex(TextBoxBase box, int line)
    {
        try
        {
            string text = box.Text ?? string.Empty;
            if (line < 0)
                return -1;
            int offset = 0;
            for (int i = 0; i < line; i++)
            {
                int nl = text.IndexOf('\n', offset);
                if (nl < 0)
                    return -1;
                offset = nl + 1;
            }

            return offset <= text.Length ? offset : -1;
        }
        catch
        {
            return -1;
        }
    }

    public static int LineCount(TextBoxBase box)
    {
        try
        {
            return LogicalLines(box).Length;
        }
        catch
        {
            return 1;
        }
    }

    public static int LineFromChar(TextBoxBase box, int index)
    {
        try
        {
            string text = box.Text ?? string.Empty;
            if (index < 0)
                return 0;
            if (index > text.Length)
                index = text.Length;
            int line = 0;
            for (int i = 0; i < index; i++)
            {
                if (text[i] == '\n')
                    line++;
            }

            return line;
        }
        catch
        {
            return 0;
        }
    }
}
