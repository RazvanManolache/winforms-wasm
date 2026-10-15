namespace WfWasm.Shim;

/// <summary>
/// Glyph painters (cf. ControlPaint.DrawCheckBox/DrawRadioButton/
/// DrawMixedCheckBox/DrawComboButton/DrawCaptionButton/DrawScrollButton/
/// DrawMenuGlyph/DrawSizeGrip/DrawFocusRectangle/DrawStringDisabled).
/// Classic glyphs reproduce the Win32 DrawFrameControl bitmaps with fills;
/// circles use full-radius rounded rects (the canvas has no arc primitive).
/// </summary>
public static class ControlPaintGlyphs
{
    /// <summary>
    /// Classic 3D checkbox (cf. DFC_BUTTON/DFCS_BUTTONCHECK): white box with
    /// a dark single border; check is a 2px polyline, mixed a gray block.
    /// Flat follows the DrawFlatCheckBox rules (Window fill, ControlDark border).
    /// </summary>
    public static void DrawCheckBox(
        ICanvas canvas, int x, int y, int size,
        CheckState state, bool flat, bool disabled)
    {
        uint fill = disabled ? SystemColors.Control : (flat ? SystemColors.Window : SystemColors.Window);
        canvas.FillRectangle(x, y, size, size, fill);
        uint border = flat || disabled
            ? (disabled ? SystemColors.ControlDark : SystemColors.ControlDark)
            : SystemColors.ControlDarkDark;
        // Single-pixel border ring.
        canvas.FillRectangle(x, y, size, 1, border);
        canvas.FillRectangle(x, y + size - 1, size, 1, border);
        canvas.FillRectangle(x, y, 1, size, border);
        canvas.FillRectangle(x + size - 1, y, 1, size, border);

        if (!flat && !disabled)
        {
            // Classic sunken inner hint: shadow top-left inside the border.
            canvas.FillRectangle(x + 1, y + 1, size - 2, 1, SystemColors.ControlDark);
            canvas.FillRectangle(x + 1, y + 1, 1, size - 2, SystemColors.ControlDark);
        }

        if (state == CheckState.Checked)
        {
            uint ink = disabled ? SystemColors.ControlDark : SystemColors.ControlText;
            DrawCheckMark(canvas, x, y, size, ink);
        }
        else if (state == CheckState.Indeterminate)
        {
            int m = size / 4;
            canvas.FillRectangle(x + m, y + m, size - (2 * m), size - (2 * m),
                disabled ? SystemColors.ControlDark : SystemColors.GrayText);
        }
    }

    /// <summary>
    /// Classic radio button (cf. DFC_BUTTON/DFCS_BUTTONRADIO): white disc,
    /// dark ring, centered dot when checked.
    /// </summary>
    public static void DrawRadioButton(
        ICanvas canvas, int x, int y, int size,
        bool @checked, bool disabled)
    {
        uint ring = disabled ? SystemColors.ControlDark : SystemColors.ControlDarkDark;
        canvas.FillRoundedRectangle(x, y, size, size, size / 2f, ring);
        canvas.FillRoundedRectangle(x + 1, y + 1, size - 2, size - 2, (size - 2) / 2f,
            disabled ? SystemColors.Control : SystemColors.Window);
        if (@checked)
        {
            int d = Math.Max(3, size - 7);
            int o = (size - d) / 2;
            uint ink = disabled ? SystemColors.ControlDark : SystemColors.ControlText;
            canvas.FillRoundedRectangle(x + o, y + o, d, d, d / 2f, ink);
        }
    }

    /// <summary>
    /// Combo drop-down button (cf. DFCS_SCROLLCOMBOBOX): raised face with a
    /// centered down triangle; pressed sinks and shifts the glyph.
    /// </summary>
    public static void DrawComboButton(
        ICanvas canvas, int x, int y, int width, int height,
        bool pressed, bool disabled, bool themed)
    {
        if (themed)
            ThemedButtonFace(canvas, x, y, width, height,
                pressed ? ButtonState.Pushed : ButtonState.Normal);
        else if (pressed)
            ControlPaintEdges.DrawBorder(canvas, x, y, width, height,
                SystemColors.Control, ControlPaintEdges.LineBorderStyle.Inset);
        else
            ControlPaintEdges.DrawBorder(canvas, x, y, width, height,
                SystemColors.Control, ControlPaintEdges.LineBorderStyle.Outset);

        int shift = pressed ? 1 : 0;
        uint ink = disabled ? SystemColors.ControlDark : SystemColors.ControlText;
        int w = Math.Min(9, width - 6);
        int h = Math.Max(3, w / 2);
        int gx = x + ((width - w) / 2) + shift;
        int gy = y + ((height - h) / 2) + shift;
        DrawTriangleDown(canvas, gx, gy, w, h, ink);
    }

    /// <summary>
    /// Caption button (cf. DFC_CAPTION): classic raised face with a black
    /// glyph; themed is flat with hover/pressed washes (close goes red).
    /// </summary>
    public static void DrawCaptionButton(
        ICanvas canvas, int x, int y, int width, int height,
        CaptionButton button, bool pressed, bool hot, bool themed, bool active)
    {
        if (themed)
        {
            if (pressed)
                canvas.FillRectangle(x, y, width, height,
                    button == CaptionButton.Close ? 0xF1707AFF : 0xCCE4F7FF);
            else if (hot)
                canvas.FillRectangle(x, y, width, height,
                    button == CaptionButton.Close ? 0xE81123FF : 0xE5E5E5FF);
            uint ink = !active ? SystemColors.GrayText
                : (hot && button == CaptionButton.Close) || pressed
                    ? SystemColors.White : SystemColors.Black;
            DrawCaptionGlyph(canvas, x, y, width, height, button, ink);
        }
        else
        {
            if (pressed)
                ControlPaintEdges.DrawBorder(canvas, x, y, width, height,
                    SystemColors.Control, ControlPaintEdges.LineBorderStyle.Inset);
            else
                ControlPaintEdges.DrawBorder(canvas, x, y, width, height,
                    SystemColors.Control, ControlPaintEdges.LineBorderStyle.Outset);
            int shift = pressed ? 1 : 0;
            uint ink = active ? SystemColors.Black : SystemColors.ControlDark;
            DrawCaptionGlyph(canvas, x + shift, y + shift, width, height, button, ink);
        }
    }

    /// <summary>Scroll arrow button (cf. DFCS_SCROLLUP/DOWN/LEFT/RIGHT).</summary>
    public static void DrawScrollButton(
        ICanvas canvas, int x, int y, int width, int height,
        ScrollDirection direction, bool pressed, bool disabled, bool themed)
    {
        if (themed)
        {
            uint fill = pressed ? SystemColors.ThemedScrollThumbPressed
                : SystemColors.ThemedScrollTrack;
            canvas.FillRectangle(x, y, width, height, fill);
        }
        else if (pressed)
        {
            canvas.FillRectangle(x, y, width, height, SystemColors.Control);
            ControlPaintEdges.DrawBorder(canvas, x, y, width, height,
                SystemColors.Control, ControlPaintEdges.LineBorderStyle.Inset);
        }
        else
        {
            canvas.FillRectangle(x, y, width, height, SystemColors.Control);
            ControlPaintEdges.DrawBorder(canvas, x, y, width, height,
                SystemColors.Control, ControlPaintEdges.LineBorderStyle.Outset);
        }

        int shift = (!themed && pressed) ? 1 : 0;
        uint ink = disabled
            ? SystemColors.ControlDark
            : themed && pressed ? SystemColors.White : themed
                ? SystemColors.ThemedArrowGlyph : SystemColors.Black;
        int size = Math.Min(9, Math.Min(width, height) - 6);
        int gx = x + ((width - size) / 2) + shift;
        int gy = y + ((height - (direction is ScrollDirection.Up or ScrollDirection.Down ? size / 2 : size)) / 2) + shift;
        switch (direction)
        {
            case ScrollDirection.Up:
                DrawTriangleUp(canvas, gx, gy, size, size / 2, ink);
                break;
            case ScrollDirection.Down:
                DrawTriangleDown(canvas, gx, gy, size, size / 2, ink);
                break;
            case ScrollDirection.Left:
                DrawTriangleLeft(canvas, gx, gy, size / 2, size, ink);
                break;
            default:
                DrawTriangleRight(canvas, gx, gy, size / 2, size, ink);
                break;
        }
    }

    /// <summary>Menu glyphs (cf. DFCS_MENUARROW/MENUCHECK/MENUBULLET).</summary>
    public static void DrawMenuGlyph(
        ICanvas canvas, int x, int y, int width, int height,
        MenuGlyph glyph, uint ink)
    {
        switch (glyph)
        {
            case MenuGlyph.Arrow:
                DrawTriangleRight(canvas, x + ((width - 4) / 2), y + ((height - 7) / 2), 4, 7, ink);
                break;
            case MenuGlyph.Checkmark:
                DrawCheckMark(canvas, x - 2, y - 2, 13, ink);
                break;
            default:
                int d = 5;
                canvas.FillRoundedRectangle(x + ((width - d) / 2), y + ((height - d) / 2), d, d, d / 2f, ink);
                break;
        }
    }

    /// <summary>Classic size-grip ridges bottom-right (cf. DrawSizeGrip).</summary>
    public static void DrawSizeGrip(ICanvas canvas, int x, int y, int width, int height)
    {
        for (int k = 0; k < 3; k++)
        {
            int o = k * 4;
            // Shadow ridge then highlight ridge, one pixel apart.
            canvas.FillRectangle(x + width - 2 - o, y + height - 1, 1, 1, SystemColors.ControlDark);
            Diagonal(canvas, x + width - 1 - o, y + height - 2, 10 - o, SystemColors.ControlDark);
            Diagonal(canvas, x + width - 2 - o, y + height - 2, 10 - o, SystemColors.ControlLightLight);
        }
    }

    /// <summary>Dotted focus rectangle (cf. DrawFocusRectangle).</summary>
    public static void DrawFocusRectangle(ICanvas canvas, int x, int y, int width, int height)
    {
        // Alternating 1px dots, shrunk by one like the reference.
        for (int i = 0; i < width - 1; i += 2)
        {
            canvas.FillRectangle(x + i, y, 1, 1, SystemColors.Black);
            canvas.FillRectangle(x + i, y + height - 2, 1, 1, SystemColors.Black);
        }

        for (int i = 0; i < height - 1; i += 2)
        {
            canvas.FillRectangle(x, y + i, 1, 1, SystemColors.Black);
            canvas.FillRectangle(x + width - 2, y + i, 1, 1, SystemColors.Black);
        }
    }

    /// <summary>Grayed text with white shadow (cf. DrawStringDisabled).</summary>
    public static void DrawStringDisabled(
        ICanvas canvas, string text, float x, float y, string fontCss)
    {
        canvas.DrawText(text, x + 1, y + 1, fontCss, SystemColors.White);
        canvas.DrawText(text, x, y, fontCss, SystemColors.GrayText);
    }

    /// <summary>Themed single-pixel border (cf. DrawVisualStyleBorder).</summary>
    public static void DrawVisualStyleBorder(ICanvas canvas, int x, int y, int width, int height) =>
        ControlPaintEdges.DrawBorder(canvas, x, y, width, height,
            SystemColors.ThemedEditBorder, ControlPaintEdges.LineBorderStyle.Solid);

    /// <summary>Themed push-button face for normal/pressed (hot/default handled by callers).</summary>
    public static void ThemedButtonFace(
        ICanvas canvas, int x, int y, int width, int height, ButtonState state)
    {
        bool pushed = (state & ButtonState.Pushed) != 0;
        uint fill = pushed
            ? SystemColors.ThemedButtonPressedFill : SystemColors.ThemedButtonFill;
        uint border = pushed
            ? SystemColors.ThemedButtonPressedBorder : SystemColors.ThemedButtonBorder;
        canvas.FillRectangle(x, y, width, height, fill);
        ControlPaintEdges.DrawBorder(canvas, x, y, width, height,
            border, ControlPaintEdges.LineBorderStyle.Solid);
    }

    private static void DrawCheckMark(ICanvas canvas, int x, int y, int size, uint ink)
    {
        // 2px polyline: down-right then up-right, scaled to the box.
        int s = size / 13;
        if (s < 1)
            s = 1;
        // Segment lengths in 13px-box units.
        CheckRun(canvas, x + (3 * s), y + (6 * s), 1, 1, s, ink);
        CheckRun(canvas, x + (4 * s), y + (7 * s), 1, 1, s, ink);
        CheckRun(canvas, x + (5 * s), y + (6 * s), 1, -1, s, ink);
        CheckRun(canvas, x + (6 * s), y + (5 * s), 1, -1, s, ink);
        CheckRun(canvas, x + (7 * s), y + (4 * s), 1, -1, s, ink);
        CheckRun(canvas, x + (8 * s), y + (3 * s), 1, -1, s, ink);
    }

    private static void CheckRun(
        ICanvas canvas, int x, int y, int dx, int dy, int thick, uint ink)
    {
        for (int i = 0; i < 2; i++)
            canvas.FillRectangle(x + (dx * i), y + (dy * i), thick, thick, ink);
    }

    private static void DrawCaptionGlyph(
        ICanvas canvas, int x, int y, int width, int height,
        CaptionButton button, uint ink)
    {
        switch (button)
        {
            case CaptionButton.Close:
                // X across the button, 2px thick.
                for (int i = 0; i < 8; i++)
                {
                    canvas.FillRectangle(x + width / 2 - 4 + i, y + height / 2 - 4 + i, 2, 2, ink);
                    canvas.FillRectangle(x + width / 2 + 3 - i, y + height / 2 - 4 + i, 2, 2, ink);
                }

                break;
            case CaptionButton.Minimize:
                canvas.FillRectangle(x + width / 2 - 4, y + height - 6, 8, 2, ink);
                break;
            case CaptionButton.Maximize:
                canvas.FillRectangle(x + width / 2 - 4, y + height / 2 - 4, 8, 2, ink);
                canvas.FillRectangle(x + width / 2 - 4, y + height / 2 - 4, 2, 8, ink);
                canvas.FillRectangle(x + width / 2 + 2, y + height / 2 - 4, 2, 8, ink);
                canvas.FillRectangle(x + width / 2 - 4, y + height / 2 + 2, 8, 2, ink);
                break;
            case CaptionButton.Restore:
                // Two overlapping squares.
                canvas.FillRectangle(x + width / 2 - 1, y + height / 2 - 5, 6, 2, ink);
                canvas.FillRectangle(x + width / 2 + 3, y + height / 2 - 5, 2, 5, ink);
                canvas.FillRectangle(x + width / 2 - 1, y + height / 2 - 5, 2, 2, ink);
                canvas.FillRectangle(x + width / 2 - 5, y + height / 2 - 2, 8, 2, ink);
                canvas.FillRectangle(x + width / 2 - 5, y + height / 2 - 2, 2, 7, ink);
                canvas.FillRectangle(x + width / 2 + 1, y + height / 2 - 2, 2, 7, ink);
                canvas.FillRectangle(x + width / 2 - 5, y + height / 2 + 3, 8, 2, ink);
                break;
            default:
                // Help: "?" via text (Marlett is unavailable on canvas).
                canvas.DrawText("?", x + (width / 2f) - 4, y + height - 5, "bold 12px sans-serif", ink);
                break;
        }
    }

    private static void DrawTriangleUp(ICanvas canvas, int x, int y, int w, int h, uint ink)
    {
        // Apex at the top: narrow first row, full-width last row.
        for (int row = 0; row < h; row++)
        {
            int inset = ((h - 1 - row) * (w / 2)) / h;
            canvas.FillRectangle(x + inset, y + row, w - (2 * inset), 1, ink);
        }
    }

    private static void DrawTriangleDown(ICanvas canvas, int x, int y, int w, int h, uint ink)
    {
        // Apex at the bottom: full-width first row, narrow last row.
        for (int row = 0; row < h; row++)
        {
            int inset = (row * (w / 2)) / h;
            canvas.FillRectangle(x + inset, y + row, w - (2 * inset), 1, ink);
        }
    }

    private static void DrawTriangleLeft(ICanvas canvas, int x, int y, int w, int h, uint ink)
    {
        // Apex at the left: short first column, full-height last column.
        for (int col = 0; col < w; col++)
        {
            int inset = ((w - 1 - col) * (h / 2)) / w;
            canvas.FillRectangle(x + col, y + inset, 1, h - (2 * inset), ink);
        }
    }

    private static void DrawTriangleRight(ICanvas canvas, int x, int y, int w, int h, uint ink)
    {
        // Apex at the right: full-height first column, short last column.
        for (int col = 0; col < w; col++)
        {
            int inset = (col * (h / 2)) / w;
            canvas.FillRectangle(x + col, y + inset, 1, h - (2 * inset), ink);
        }
    }

    private static void Diagonal(ICanvas canvas, int x, int y, int length, uint ink)
    {
        for (int i = 0; i < length && x - i >= 0 && y + i >= 0; i++)
            canvas.FillRectangle(x - i, y + i, 1, 1, ink);
    }
}
