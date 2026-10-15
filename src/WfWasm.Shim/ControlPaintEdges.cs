namespace WfWasm.Shim;

/// <summary>
/// Classic 3D edge and border painting (cf. ControlPaint.DrawBorder3D,
/// DrawBorder, and the Win32 DrawEdge rules they encode). All output is
/// single-pixel fills, so dotted/dashed styles are drawn as rect runs.
/// </summary>
public static class ControlPaintEdges
{
    /// <summary>3D edge style (cf. Border3DStyle).</summary>
    public enum EdgeStyle
    {
        Raised,
        Sunken,
        Etched,
        Bump,
        RaisedOuter,
        RaisedInner,
        SunkenOuter,
        SunkenInner,
        Flat,
    }

    /// <summary>Sides to paint (cf. Border3DSide).</summary>
    [Flags]
    public enum EdgeSides
    {
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
        All = Left | Top | Right | Bottom,
    }

    /// <summary>Single-line border style (cf. ButtonBorderStyle).</summary>
    public enum LineBorderStyle
    {
        Dotted,
        Dashed,
        Solid,
        Inset,
        Outset,
    }

    /// <summary>
    /// Two-pixel 3D edge. Outer pair at offset 0, inner pair at offset 1
    /// (cf. DrawEdge EDGE_RAISED/SUNKEN/ETCHED/BUMP color assignments).
    /// </summary>
    public static void DrawEdge(
        ICanvas canvas, int x, int y, int width, int height,
        EdgeStyle style, EdgeSides sides = EdgeSides.All)
    {
        uint face = SystemColors.Control;
        switch (style)
        {
            case EdgeStyle.Raised:
                EdgePair(canvas, x, y, width, height, 0, sides,
                    SystemColors.ControlLightLight, SystemColors.ControlDarkDark);
                EdgePair(canvas, x, y, width, height, 1, sides,
                    SystemColors.ControlLight, SystemColors.ControlDark);
                break;
            case EdgeStyle.Sunken:
                EdgePair(canvas, x, y, width, height, 0, sides,
                    SystemColors.ControlDarkDark, SystemColors.ControlLightLight);
                EdgePair(canvas, x, y, width, height, 1, sides,
                    SystemColors.ControlDark, SystemColors.ControlLight);
                break;
            case EdgeStyle.Etched:
                EdgePair(canvas, x, y, width, height, 0, sides,
                    SystemColors.ControlDarkDark, SystemColors.ControlLightLight);
                EdgePair(canvas, x, y, width, height, 1, sides,
                    SystemColors.ControlLight, SystemColors.ControlDark);
                break;
            case EdgeStyle.Bump:
                EdgePair(canvas, x, y, width, height, 0, sides,
                    SystemColors.ControlLightLight, SystemColors.ControlDarkDark);
                EdgePair(canvas, x, y, width, height, 1, sides,
                    SystemColors.ControlDark, SystemColors.ControlLight);
                break;
            case EdgeStyle.RaisedOuter:
                EdgePair(canvas, x, y, width, height, 0, sides,
                    SystemColors.ControlLightLight, SystemColors.ControlDarkDark);
                break;
            case EdgeStyle.RaisedInner:
                EdgePair(canvas, x, y, width, height, 0, sides,
                    SystemColors.ControlLight, SystemColors.ControlDark);
                break;
            case EdgeStyle.SunkenOuter:
                EdgePair(canvas, x, y, width, height, 0, sides,
                    SystemColors.ControlDarkDark, SystemColors.ControlLightLight);
                break;
            case EdgeStyle.SunkenInner:
                EdgePair(canvas, x, y, width, height, 0, sides,
                    SystemColors.ControlDark, SystemColors.ControlLight);
                break;
            default:
                // Flat: a single flat 1px line (cf. BF_FLAT).
                if (sides.HasFlag(EdgeSides.Top))
                    canvas.FillRectangle(x, y, width, 1, SystemColors.ControlDarkDark);
                if (sides.HasFlag(EdgeSides.Left))
                    canvas.FillRectangle(x, y, 1, height, SystemColors.ControlDarkDark);
                if (sides.HasFlag(EdgeSides.Right))
                    canvas.FillRectangle(x + width - 1, y, 1, height, SystemColors.ControlDarkDark);
                if (sides.HasFlag(EdgeSides.Bottom))
                    canvas.FillRectangle(x, y + height - 1, width, 1, SystemColors.ControlDarkDark);
                break;
        }

        _ = face;
    }

    /// <summary>
    /// Single-line border (cf. ControlPaint.DrawBorder). Inset/Outset follow
    /// the DrawBorderComplex line rules, including the stock-Control color
    /// special cases.
    /// </summary>
    public static void DrawBorder(
        ICanvas canvas, int x, int y, int width, int height,
        uint color, LineBorderStyle style)
    {
        switch (style)
        {
            case LineBorderStyle.Solid:
                canvas.FillRectangle(x, y, width, 1, color);
                canvas.FillRectangle(x, y + height - 1, width, 1, color);
                canvas.FillRectangle(x, y, 1, height, color);
                canvas.FillRectangle(x + width - 1, y, 1, height, color);
                break;
            case LineBorderStyle.Dotted:
                DashedRect(canvas, x, y, width, height, color, on: 1, off: 1);
                break;
            case LineBorderStyle.Dashed:
                DashedRect(canvas, x, y, width, height, color, on: 4, off: 2);
                break;
            case LineBorderStyle.Inset:
                DrawComplex(canvas, x, y, width, height, color, sunken: true);
                break;
            default:
                DrawComplex(canvas, x, y, width, height, color, sunken: false);
                break;
        }
    }

    private static void EdgePair(
        ICanvas canvas, int x, int y, int width, int height, int inset,
        EdgeSides sides, uint topLeft, uint bottomRight)
    {
        if (sides.HasFlag(EdgeSides.Top))
            canvas.FillRectangle(x + inset, y + inset, width - (2 * inset), 1, topLeft);
        if (sides.HasFlag(EdgeSides.Left))
            canvas.FillRectangle(x + inset, y + inset, 1, height - (2 * inset), topLeft);
        if (sides.HasFlag(EdgeSides.Right))
            canvas.FillRectangle(x + width - 1 - inset, y + inset, 1, height - (2 * inset), bottomRight);
        if (sides.HasFlag(EdgeSides.Bottom))
            canvas.FillRectangle(x + inset, y + height - 1 - inset, width - (2 * inset), 1, bottomRight);
    }

    private static void DashedRect(
        ICanvas canvas, int x, int y, int width, int height,
        uint color, int on, int off)
    {
        DashRun(canvas, x, y, width, horizontal: true, color, on, off);
        DashRun(canvas, x, y + height - 1, width, horizontal: true, color, on, off);
        DashRun(canvas, x, y, height, horizontal: false, color, on, off);
        DashRun(canvas, x + width - 1, y, height, horizontal: false, color, on, off);
    }

    private static void DashRun(
        ICanvas canvas, int x, int y, int length, bool horizontal,
        uint color, int on, int off)
    {
        int pos = 0;
        while (pos < length)
        {
            int run = Math.Min(on, length - pos);
            if (horizontal)
                canvas.FillRectangle(x + pos, y, run, 1, color);
            else
                canvas.FillRectangle(x, y + pos, 1, run, color);
            pos += on + off;
        }
    }

    private static void DrawComplex(
        ICanvas canvas, int x, int y, int width, int height,
        uint color, bool sunken)
    {
        bool stock = color == SystemColors.Control;
        if (sunken)
        {
            // Outer: top-left DarkDark, bottom-right LightLight.
            canvas.FillRectangle(x, y, width, 1, SystemColors.DarkDark(color));
            canvas.FillRectangle(x, y, 1, height, SystemColors.DarkDark(color));
            canvas.FillRectangle(x, y + height - 1, width, 1, SystemColors.LightLight(color));
            canvas.FillRectangle(x + width - 1, y, 1, height, SystemColors.LightLight(color));
            // Inner: top-left Light(0.5).
            uint inner = SystemColors.Light(color);
            canvas.FillRectangle(x + 1, y + 1, width - 2, 1, inner);
            canvas.FillRectangle(x + 1, y + 1, 1, height - 2, inner);
            // Inner bottom-right: ControlLight for stock Control.
            uint innerBr = stock ? SystemColors.ControlLight : SystemColors.Light(color);
            canvas.FillRectangle(x + 1, y + height - 2, width - 2, 1, innerBr);
            canvas.FillRectangle(x + width - 2, y + 1, 1, height - 2, innerBr);
        }
        else
        {
            // Outer: top-left LightLight, bottom-right DarkDark.
            uint outerTl = stock ? SystemColors.ControlLightLight : SystemColors.LightLight(color);
            uint outerBr = stock ? SystemColors.ControlDarkDark : SystemColors.DarkDark(color);
            canvas.FillRectangle(x, y, width, 1, outerTl);
            canvas.FillRectangle(x, y, 1, height, outerTl);
            canvas.FillRectangle(x, y + height - 1, width, 1, outerBr);
            canvas.FillRectangle(x + width - 1, y, 1, height, outerBr);
            // Inner: top-left face, bottom-right Dark.
            uint innerTl = stock ? SystemColors.Control : color;
            uint innerBr = stock ? SystemColors.ControlDark : SystemColors.Dark(color);
            canvas.FillRectangle(x + 1, y + 1, width - 2, 1, innerTl);
            canvas.FillRectangle(x + 1, y + 1, 1, height - 2, innerTl);
            canvas.FillRectangle(x + 1, y + height - 2, width - 2, 1, innerBr);
            canvas.FillRectangle(x + width - 2, y + 1, 1, height - 2, innerBr);
        }
    }
}
