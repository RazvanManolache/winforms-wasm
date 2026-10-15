namespace WfWasm.Shim;

/// <summary>
/// Scroll bar (cf. System.Windows.Forms.ScrollBar / HScrollBar / VScrollBar):
/// arrow buttons, proportional thumb, track paging. Value follows the
/// WinForms rule: the thumb's topmost position is Maximum - LargeChange + 1.
/// Thumb dragging works through <see cref="DragTo"/> (the host routes moves).
/// </summary>
public class ScrollBar : Control
{
    public const int ButtonSize = 17;
    public const int MinThumb = 8;

    public ScrollBar(string name = "")
        : base(name)
    {
    }

    public ScrollBar()
        : this("")
    {
    }

    public bool Vertical { get; set; } = true;
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public int SmallChange { get; set; } = 1;
    public int LargeChange { get; set; } = 10;

    public bool IsHot { get; set; }
    public bool IsDragging { get; private set; }

    public override void HandleHoverLocal(int localX, int localY) =>
        IsHot = localX >= 0 && Enabled;

    private int _value;
    private int _dragOffset;

    public int Value
    {
        get => _value;
        set => SetValue(value);
    }

    public event EventHandler? ValueChanged;
    public event EventHandler? Scroll;

    /// <summary>Highest Value that still shows the last line/page (cf. Maximum - LargeChange + 1).</summary>
    public int EffectiveMaximum => Math.Max(Minimum, Maximum - LargeChange + 1);

    private void SetValue(int value)
    {
        int clamped = Math.Clamp(value, Minimum, EffectiveMaximum);
        if (clamped == _value)
            return;
        _value = clamped;
        ValueChanged?.Invoke(this, EventArgs.Empty);
        Scroll?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Layout: arrows, track, thumb (all in local space).</summary>
    public (int Arrow1, int Track, int ThumbStart, int ThumbLength, int Arrow2) ComputeLayout()
    {
        int length = Vertical ? Height : Width;
        int arrow = Math.Min(ButtonSize, length / 2);
        int trackStart = arrow;
        int trackEnd = length - arrow;
        int trackLen = Math.Max(0, trackEnd - trackStart);
        int range = Math.Max(1, EffectiveMaximum - Minimum + LargeChange);
        int thumbLen = Math.Clamp((trackLen * LargeChange) / range, MinThumb, trackLen);
        if (trackLen <= 0 || EffectiveMaximum <= Minimum)
            return (arrow, trackStart, trackStart, trackLen, arrow);
        int travel = trackLen - thumbLen;
        int thumbStart = trackStart + ((travel * (_value - Minimum)) / (EffectiveMaximum - Minimum));
        return (arrow, trackStart, thumbStart, thumbLen, arrow);
    }

    public override void HandleClickLocal(int localX, int localY)
    {
        if (!Enabled)
            return;
        int pos = Vertical ? localY : localX;
        (int arrow, int track, int thumbStart, int thumbLen, _) = ComputeLayout();
        if (pos < arrow)
        {
            SetValue(_value - SmallChange);
        }
        else if (pos >= (Vertical ? Height : Width) - arrow)
        {
            SetValue(_value + SmallChange);
        }
        else if (pos < thumbStart)
        {
            SetValue(_value - LargeChange);
        }
        else if (pos >= thumbStart + thumbLen)
        {
            SetValue(_value + LargeChange);
        }
        else
        {
            // Thumb press: capture drag offset (cf. mouse capture).
            IsDragging = true;
            _dragOffset = pos - thumbStart;
        }

        _ = track;
    }

    /// <summary>Move a captured thumb (host routes mouse moves here).</summary>
    public void DragTo(int localPos)
    {
        if (!IsDragging || !Enabled)
            return;
        (int arrow, int track, _, int thumbLen, _) = ComputeLayout();
        int trackLen = ((Vertical ? Height : Width) - (2 * arrow)) - thumbLen;
        if (trackLen <= 0 || EffectiveMaximum <= Minimum)
            return;
        int rel = localPos - arrow - _dragOffset;
        SetValue(Minimum + ((rel * (EffectiveMaximum - Minimum)) / trackLen));
        _ = track;
    }

    public void EndDrag() => IsDragging = false;

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        int length = Vertical ? Height : Width;
        int breadth = Vertical ? Width : Height;
        if (length <= 0 || breadth <= 0)
            return;

        (int arrow, int track, int thumbStart, int thumbLen, _) = ComputeLayout();
        bool disabled = !Enabled;

        if (themed)
        {
            canvas.FillRectangle(ax, ay, Width, Height, SystemColors.ThemedScrollTrack);
            PaintThemedThumb(canvas, ax, ay, track, thumbStart, thumbLen, disabled);
            PaintThemedArrowGlyphs(canvas, ax, ay, arrow, length, disabled);
        }
        else
        {
            canvas.FillRectangle(ax, ay, Width, Height,
                disabled ? SystemColors.Control : SystemColors.ScrollBar);
            // Thumb: raised face.
            if (Vertical)
            {
                canvas.FillRectangle(ax, ay + thumbStart, breadth, thumbLen,
                    disabled ? SystemColors.Control : SystemColors.Control);
                if (!disabled)
                    ControlPaintEdges.DrawBorder(canvas, ax, ay + thumbStart, breadth, thumbLen,
                        SystemColors.Control, ControlPaintEdges.LineBorderStyle.Outset);
            }
            else
            {
                canvas.FillRectangle(ax + thumbStart, ay, thumbLen, breadth,
                    disabled ? SystemColors.Control : SystemColors.Control);
                if (!disabled)
                    ControlPaintEdges.DrawBorder(canvas, ax + thumbStart, ay, thumbLen, breadth,
                        SystemColors.Control, ControlPaintEdges.LineBorderStyle.Outset);
            }

            // Arrow buttons.
            if (Vertical)
            {
                ControlPaintGlyphs.DrawScrollButton(canvas, ax, ay, breadth, arrow,
                    ScrollDirection.Up, pressed: false, disabled, themed: false);
                ControlPaintGlyphs.DrawScrollButton(canvas, ax, ay + length - arrow, breadth, arrow,
                    ScrollDirection.Down, pressed: false, disabled, themed: false);
            }
            else
            {
                ControlPaintGlyphs.DrawScrollButton(canvas, ax, ay, arrow, breadth,
                    ScrollDirection.Left, pressed: false, disabled, themed: false);
                ControlPaintGlyphs.DrawScrollButton(canvas, ax + length - arrow, ay, arrow, breadth,
                    ScrollDirection.Right, pressed: false, disabled, themed: false);
            }
        }

        _ = track;
    }

    private void PaintThemedThumb(
        ICanvas canvas, int ax, int ay,
        int track, int thumbStart, int thumbLen, bool disabled)
    {
        uint fill = disabled ? SystemColors.ThemedScrollTrack
            : IsDragging ? SystemColors.ThemedScrollThumbPressed
            : IsHot ? SystemColors.ThemedScrollThumbHot
            : SystemColors.ThemedScrollThumb;
        if (Vertical)
            canvas.FillRectangle(ax, ay + thumbStart, Width, thumbLen, fill);
        else
            canvas.FillRectangle(ax + thumbStart, ay, thumbLen, Height, fill);
        _ = track;
    }

    private void PaintThemedArrowGlyphs(
        ICanvas canvas, int ax, int ay, int arrow, int length, bool disabled)
    {
        // Themed Win10 arrows are flat glyphs on the track (no button faces).
        uint ink = disabled ? SystemColors.ControlDark : SystemColors.ThemedArrowGlyph;
        int breadth = Vertical ? Width : Height;
        int size = Math.Min(7, arrow - 6);
        if (size < 3)
            return;
        if (Vertical)
        {
            int gx = ax + ((breadth - size) / 2);
            DrawUpGlyph(canvas, gx, ay + ((arrow - (size / 2)) / 2), size, ink);
            DrawDownGlyph(canvas, gx, ay + length - arrow + ((arrow - (size / 2)) / 2), size, ink);
        }
        else
        {
            int gy = ay + ((breadth - size) / 2);
            DrawLeftGlyph(canvas, ax + ((arrow - (size / 2)) / 2), gy, size, ink);
            DrawRightGlyph(canvas, ax + length - arrow + ((arrow - (size / 2)) / 2), gy, size, ink);
        }
    }

    private static void DrawUpGlyph(ICanvas canvas, int x, int y, int size, uint ink)
    {
        int h = size / 2;
        for (int row = 0; row < h; row++)
        {
            int inset = ((h - 1 - row) * (size / 2)) / h;
            canvas.FillRectangle(x + inset, y + row, size - (2 * inset), 1, ink);
        }
    }

    private static void DrawDownGlyph(ICanvas canvas, int x, int y, int size, uint ink)
    {
        int h = size / 2;
        for (int row = 0; row < h; row++)
        {
            int inset = (row * (size / 2)) / h;
            canvas.FillRectangle(x + inset, y + row, size - (2 * inset), 1, ink);
        }
    }

    private static void DrawLeftGlyph(ICanvas canvas, int x, int y, int size, uint ink)
    {
        int w = size / 2;
        for (int col = 0; col < w; col++)
        {
            int inset = ((w - 1 - col) * (size / 2)) / w;
            canvas.FillRectangle(x + col, y + inset, 1, size - (2 * inset), ink);
        }
    }

    private static void DrawRightGlyph(ICanvas canvas, int x, int y, int size, uint ink)
    {
        int w = size / 2;
        for (int col = 0; col < w; col++)
        {
            int inset = (col * (size / 2)) / w;
            canvas.FillRectangle(x + col, y + inset, 1, size - (2 * inset), ink);
        }
    }
}

/// <summary>Horizontal scroll bar (cf. System.Windows.Forms.HScrollBar).</summary>
public class HScrollBar : ScrollBar
{
    public HScrollBar(string name = "")
        : base(name)
    {
        Vertical = false;
    }

    public HScrollBar()
        : this("")
    {
    }
}

/// <summary>Vertical scroll bar (cf. System.Windows.Forms.VScrollBar).</summary>
public class VScrollBar : ScrollBar
{
    public VScrollBar(string name = "")
        : base(name)
    {
        Vertical = true;
    }

    public VScrollBar()
        : this("")
    {
    }
}
