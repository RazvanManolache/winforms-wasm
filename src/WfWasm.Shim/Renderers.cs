extern alias WfDraw;

namespace WfWasm.Shim;

using Draw = WfDraw::System.Drawing;

/// <summary>
/// Themed push-button painter (cf. System.Windows.Forms.ButtonRenderer).
/// Shapes are probe-verified against the framework; the core fill/border
/// uses the CPU rasterizer, text goes through Graphics.DrawString and the
/// image through Graphics.DrawImage.
/// </summary>
public sealed class ButtonRenderer
{
    private ButtonRenderer()
    {
    }

    public static bool RenderMatchingApplicationState { get; set; } = true;

    public static bool IsBackgroundPartiallyTransparent(PushButtonState state) => false;

    public static void DrawParentBackground(Draw.Graphics graphics, Draw.Rectangle bounds, Control? control)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        Draw.Color fill = control?.Parent?.BackColor ?? control?.BackColor
            ?? Draw.Color.FromArgb(0xF0, 0xF0, 0xF0);
        using var brush = new Draw.SolidBrush(fill);
        graphics.FillRectangle(brush, bounds);
    }

    public static void DrawButton(Draw.Graphics graphics, Draw.Rectangle bounds, PushButtonState state) =>
        DrawButton(graphics, bounds, focused: false, state);

    public static void DrawButton(Draw.Graphics graphics, Draw.Rectangle bounds, bool focused, PushButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        bool disabled = state == PushButtonState.Disabled;
        bool pressed = state == PushButtonState.Pressed;
        bool hot = state == PushButtonState.Hot;
        uint fill = disabled ? SystemColors.ThemedButtonDisabledFill
            : pressed ? SystemColors.ThemedButtonPressedFill
            : hot ? SystemColors.ThemedButtonHotFill
            : SystemColors.ThemedButtonFill;
        uint edge = disabled ? SystemColors.ThemedButtonDisabledBorder
            : pressed ? SystemColors.ThemedButtonPressedBorder
            : hot || state == PushButtonState.Default ? SystemColors.ThemedButtonHotBorder
            : SystemColors.ThemedButtonBorder;
        using (var brush = new Draw.SolidBrush(ToColor(fill)))
            graphics.FillRectangle(brush, bounds);
        using (var pen = new Draw.Pen(ToColor(edge), 1f))
            graphics.DrawRectangle(pen, bounds);
        if (focused && !disabled)
        {
            using var focus = new Draw.Pen(Draw.Color.Black, 1f)
            {
                DashStyle = WfDraw::System.Drawing.Drawing2D.DashStyle.Dot,
            };
            graphics.DrawRectangle(focus,
                bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6);
        }
    }

    public static void DrawButton(Draw.Graphics graphics, Draw.Rectangle bounds, string? text, Draw.Font? font, bool focused, PushButtonState state)
    {
        DrawButton(graphics, bounds, focused, state);
        if (!string.IsNullOrEmpty(text))
            DrawCenteredText(graphics, text, font, bounds, state == PushButtonState.Disabled);
    }

    public static void DrawButton(Draw.Graphics graphics, Draw.Rectangle bounds, string? text, Draw.Font? font, TextFormatFlags flags, bool focused, PushButtonState state)
    {
        // flags only steer alignment on native; the rasterizer centers (see DrawString).
        DrawButton(graphics, bounds, text, font, focused, state);
    }

    public static void DrawButton(Draw.Graphics graphics, Draw.Rectangle bounds, Draw.Image? image, Draw.Rectangle imageBounds, bool focused, PushButtonState state)
    {
        DrawButton(graphics, bounds, focused, state);
        if (image is not null)
            graphics.DrawImage(image, imageBounds);
    }

    public static void DrawButton(Draw.Graphics graphics, Draw.Rectangle bounds, string? text, Draw.Font? font, Draw.Image? image, Draw.Rectangle imageBounds, bool focused, PushButtonState state)
    {
        DrawButton(graphics, bounds, image, imageBounds, focused, state);
        if (!string.IsNullOrEmpty(text))
            DrawCenteredText(graphics, text, font, bounds, state == PushButtonState.Disabled);
    }

    public static void DrawButton(Draw.Graphics graphics, Draw.Rectangle bounds, string? text, Draw.Font? font, TextFormatFlags flags, Draw.Image? image, Draw.Rectangle imageBounds, bool focused, PushButtonState state)
    {
        DrawButton(graphics, bounds, text, font, image, imageBounds, focused, state);
    }

    private static void DrawCenteredText(Draw.Graphics graphics, string text, Draw.Font? font, Draw.Rectangle bounds, bool disabled)
    {
        Draw.Font face = font ?? Control.DefaultFont;
        using var brush = new Draw.SolidBrush(disabled ? Draw.Color.Gray : Draw.Color.Black);
        graphics.DrawString(text, face, brush, new Draw.RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    internal static Draw.Color ToColor(uint rgba) =>
        Draw.Color.FromArgb((int)(rgba & 0xFF), (int)((rgba >> 24) & 0xFF), (int)((rgba >> 16) & 0xFF), (int)((rgba >> 8) & 0xFF));
}

/// <summary>
/// Themed check-box painter (cf. System.Windows.Forms.CheckBoxRenderer).
/// Glyph is 13x13 (probe-verified); states map 1-4 unchecked, 5-8 checked,
/// 9-12 mixed, every fourth state disabled.
/// </summary>
public sealed class CheckBoxRenderer
{
    private CheckBoxRenderer()
    {
    }

    public const int GlyphSize = 13;

    public static bool RenderMatchingApplicationState { get; set; } = true;

    public static bool IsBackgroundPartiallyTransparent(CheckBoxState state) => false;

    public static Draw.Size GetGlyphSize(Draw.Graphics graphics, CheckBoxState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        return new Draw.Size(GlyphSize, GlyphSize);
    }

    public static void DrawParentBackground(Draw.Graphics graphics, Draw.Rectangle bounds, Control? control)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        Draw.Color fill = control?.Parent?.BackColor ?? control?.BackColor
            ?? Draw.Color.FromArgb(0xF0, 0xF0, 0xF0);
        using var brush = new Draw.SolidBrush(fill);
        graphics.FillRectangle(brush, bounds);
    }

    public static void DrawCheckBox(Draw.Graphics graphics, Draw.Point glyphLocation, CheckBoxState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        bool disabled = ((int)state % 4) == 0;
        bool @checked = (int)state is >= 5 and <= 8;
        bool mixed = (int)state >= 9;
        int x = glyphLocation.X;
        int y = glyphLocation.Y;
        using (var fill = new Draw.SolidBrush(disabled
                   ? Draw.Color.FromArgb(0xF0, 0xF0, 0xF0) : Draw.Color.White))
            graphics.FillRectangle(fill, x, y, GlyphSize, GlyphSize);
        using (var edge = new Draw.Pen(disabled ? Draw.Color.Gray : Draw.Color.Black, 1f))
            graphics.DrawRectangle(edge, x, y, GlyphSize, GlyphSize);
        if (mixed)
        {
            using var mix = new Draw.SolidBrush(disabled ? Draw.Color.Gray : Draw.Color.Black);
            graphics.FillRectangle(mix, x + 4, y + 4, GlyphSize - 8, GlyphSize - 8);
        }
        else if (@checked)
        {
            using var mark = new Draw.Pen(disabled ? Draw.Color.Gray : Draw.Color.Black, 2f);
            graphics.DrawLine(mark, x + 3, y + 7, x + 6, y + 10);
            graphics.DrawLine(mark, x + 6, y + 10, x + 11, y + 3);
        }
    }

    public static void DrawCheckBox(Draw.Graphics graphics, Draw.Point glyphLocation, Draw.Rectangle textBounds, string? text, Draw.Font? font, bool focused, CheckBoxState state)
    {
        DrawCheckBox(graphics, glyphLocation, state);
        if (!string.IsNullOrEmpty(text))
        {
            Draw.Font face = font ?? Control.DefaultFont;
            bool disabled = ((int)state % 4) == 0;
            using var brush = new Draw.SolidBrush(disabled ? Draw.Color.Gray : Draw.Color.Black);
            graphics.DrawString(text, face, brush,
                new Draw.RectangleF(textBounds.X, textBounds.Y, textBounds.Width, textBounds.Height));
        }
    }

    public static void DrawCheckBox(Draw.Graphics graphics, Draw.Point glyphLocation, Draw.Rectangle textBounds, string? text, Draw.Font? font, TextFormatFlags flags, bool focused, CheckBoxState state)
    {
        DrawCheckBox(graphics, glyphLocation, textBounds, text, font, focused, state);
    }

    public static void DrawCheckBox(Draw.Graphics graphics, Draw.Point glyphLocation, Draw.Rectangle textBounds, string? text, Draw.Font? font, Draw.Image? image, Draw.Rectangle imageBounds, bool focused, CheckBoxState state)
    {
        if (image is not null)
            graphics.DrawImage(image, imageBounds);
        DrawCheckBox(graphics, glyphLocation, textBounds, text, font, focused, state);
    }

    public static void DrawCheckBox(Draw.Graphics graphics, Draw.Point glyphLocation, Draw.Rectangle textBounds, string? text, Draw.Font? font, TextFormatFlags flags, Draw.Image? image, Draw.Rectangle imageBounds, bool focused, CheckBoxState state)
    {
        DrawCheckBox(graphics, glyphLocation, textBounds, text, font, image, imageBounds, focused, state);
    }
}

/// <summary>
/// Classic-style painters (cf. System.Windows.Forms.ControlPaint).
/// DrawButton draws the beveled classic face (raised normal, sunken pushed,
/// flat otherwise); the border, check, radio, scroll-button and focus-rect
/// painters below draw the matching classic glyphs on the CPU rasterizer.
/// </summary>
public static class ControlPaint
{
    /// <summary>3-D border (cf. DrawBorder3D(Graphics, Rectangle)).</summary>
    public static void DrawBorder3D(Draw.Graphics graphics, Draw.Rectangle rectangle) =>
        DrawBorder3D(graphics, rectangle, Border3DStyle.Raised);

    /// <summary>3-D border in a style (cf. DrawBorder3D(Graphics, Rectangle, Border3DStyle)).</summary>
    public static void DrawBorder3D(Draw.Graphics graphics, Draw.Rectangle rectangle, Border3DStyle style) =>
        DrawBorder3D(graphics, rectangle, style, Border3DSide.All);

    /// <summary>3-D border on sides (cf. DrawBorder3D(Graphics, Rectangle, Border3DStyle, Border3DSide)).</summary>
    public static void DrawBorder3D(Draw.Graphics graphics, Draw.Rectangle rectangle, Border3DStyle style, Border3DSide sides)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        bool sunken = style is Border3DStyle.Sunken or Border3DStyle.SunkenOuter or Border3DStyle.SunkenInner or Border3DStyle.Etched;
        bool flat = style is Border3DStyle.Flat;
        Draw.Color outerTl = flat ? ButtonRenderer.ToColor(SystemColors.ControlDark)
            : ButtonRenderer.ToColor(sunken ? SystemColors.ControlDarkDark : SystemColors.ControlLightLight);
        Draw.Color innerTl = flat ? ButtonRenderer.ToColor(SystemColors.ControlDark)
            : ButtonRenderer.ToColor(sunken ? SystemColors.ControlDark : SystemColors.ControlLight);
        Draw.Color outerBr = flat ? ButtonRenderer.ToColor(SystemColors.ControlDark)
            : ButtonRenderer.ToColor(sunken ? SystemColors.ControlLightLight : SystemColors.ControlDarkDark);
        Draw.Color innerBr = flat ? ButtonRenderer.ToColor(SystemColors.ControlDark)
            : ButtonRenderer.ToColor(sunken ? SystemColors.ControlLight : SystemColors.ControlDark);
        int x = rectangle.X;
        int y = rectangle.Y;
        int r = x + rectangle.Width - 1;
        int b = y + rectangle.Height - 1;
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
            return;
        if ((sides & Border3DSide.Middle) != 0)
        {
            using var middle = new Draw.SolidBrush(ButtonRenderer.ToColor(SystemColors.Control));
            graphics.FillRectangle(middle, rectangle);
        }
        using (var outerTop = new Draw.Pen(outerTl, 1f))
        using (var outerBottom = new Draw.Pen(outerBr, 1f))
        {
            if ((sides & Border3DSide.Top) != 0)
                graphics.DrawLine(outerTop, x, y, r, y);
            if ((sides & Border3DSide.Left) != 0)
                graphics.DrawLine(outerTop, x, y, x, b);
            if ((sides & Border3DSide.Bottom) != 0)
                graphics.DrawLine(outerBottom, x, b, r, b);
            if ((sides & Border3DSide.Right) != 0)
                graphics.DrawLine(outerBottom, r, y, r, b);
        }

        if (flat || rectangle.Width < 3 || rectangle.Height < 3)
            return;
        using (var innerTop = new Draw.Pen(innerTl, 1f))
        using (var innerBottom = new Draw.Pen(innerBr, 1f))
        {
            if ((sides & Border3DSide.Top) != 0)
                graphics.DrawLine(innerTop, x + 1, y + 1, r - 1, y + 1);
            if ((sides & Border3DSide.Left) != 0)
                graphics.DrawLine(innerTop, x + 1, y + 1, x + 1, b - 1);
            if ((sides & Border3DSide.Bottom) != 0)
                graphics.DrawLine(innerBottom, x + 1, b - 1, r - 1, b - 1);
            if ((sides & Border3DSide.Right) != 0)
                graphics.DrawLine(innerBottom, r - 1, y + 1, r - 1, b - 1);
        }
    }

    /// <summary>3-D border by coordinates (cf. DrawBorder3D(Graphics, int, int, int, int)).</summary>
    public static void DrawBorder3D(Draw.Graphics graphics, int x, int y, int width, int height) =>
        DrawBorder3D(graphics, new Draw.Rectangle(x, y, width, height));

    /// <summary>Styled 3-D border by coordinates (cf. DrawBorder3D(Graphics, int, int, int, int, Border3DStyle)).</summary>
    public static void DrawBorder3D(Draw.Graphics graphics, int x, int y, int width, int height, Border3DStyle style) =>
        DrawBorder3D(graphics, new Draw.Rectangle(x, y, width, height), style);

    /// <summary>Sided 3-D border by coordinates (cf. DrawBorder3D(Graphics, int, int, int, int, Border3DStyle, Border3DSide)).</summary>
    public static void DrawBorder3D(Draw.Graphics graphics, int x, int y, int width, int height, Border3DStyle style, Border3DSide sides) =>
        DrawBorder3D(graphics, new Draw.Rectangle(x, y, width, height), style, sides);

    /// <summary>Check-box glyph (cf. DrawCheckBox(Graphics, Rectangle, ButtonState)).</summary>
    public static void DrawCheckBox(Draw.Graphics graphics, Draw.Rectangle rectangle, ButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        DrawBoxFace(graphics, rectangle, state);
        if ((state & ButtonState.Checked) != 0)
        {
            Draw.Color mark = ButtonRenderer.ToColor(
                (state & ButtonState.Inactive) != 0 ? SystemColors.GrayText : SystemColors.ControlText);
            using var pen = new Draw.Pen(mark, 1f);
            int x = rectangle.X + 3;
            int y = rectangle.Y + rectangle.Height / 2;
            int w = rectangle.Width - 6;
            graphics.DrawLines(pen, new[]
            {
                new Draw.Point(x, y),
                new Draw.Point(x + w / 3, y + w / 3),
                new Draw.Point(x + w, y - w / 3),
            });
        }
    }

    /// <summary>Check-box glyph by coordinates (cf. DrawCheckBox(Graphics, int, int, int, int, ButtonState)).</summary>
    public static void DrawCheckBox(Draw.Graphics graphics, int x, int y, int width, int height, ButtonState state) =>
        DrawCheckBox(graphics, new Draw.Rectangle(x, y, width, height), state);

    /// <summary>Radio-button glyph (cf. DrawRadioButton(Graphics, Rectangle, ButtonState)).</summary>
    public static void DrawRadioButton(Draw.Graphics graphics, Draw.Rectangle rectangle, ButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        Draw.Color edge = ButtonRenderer.ToColor(
            (state & ButtonState.Flat) != 0 ? SystemColors.ControlDark : SystemColors.ControlDarkDark);
        using (var outline = new Draw.Pen(edge, 1f))
            graphics.DrawEllipse(outline, rectangle);
        using (var face = new Draw.SolidBrush(ButtonRenderer.ToColor(SystemColors.Window)))
            graphics.FillEllipse(face, Draw.Rectangle.Inflate(rectangle, -1, -1));
        if ((state & ButtonState.Checked) != 0)
        {
            Draw.Color dot = ButtonRenderer.ToColor(
                (state & ButtonState.Inactive) != 0 ? SystemColors.GrayText : SystemColors.ControlText);
            using var fill = new Draw.SolidBrush(dot);
            int d = Math.Max(2, Math.Min(rectangle.Width, rectangle.Height) / 2);
            graphics.FillEllipse(fill, new Draw.Rectangle(
                rectangle.X + (rectangle.Width - d) / 2,
                rectangle.Y + (rectangle.Height - d) / 2, d, d));
        }
    }

    /// <summary>Radio-button glyph by coordinates (cf. DrawRadioButton(Graphics, int, int, int, int, ButtonState)).</summary>
    public static void DrawRadioButton(Draw.Graphics graphics, int x, int y, int width, int height, ButtonState state) =>
        DrawRadioButton(graphics, new Draw.Rectangle(x, y, width, height), state);

    /// <summary>Focus rectangle (cf. DrawFocusRectangle(Graphics, Rectangle)).</summary>
    public static void DrawFocusRectangle(Draw.Graphics graphics, Draw.Rectangle rectangle) =>
        DrawFocusRectangle(graphics, rectangle, ButtonRenderer.ToColor(SystemColors.ControlText), ButtonRenderer.ToColor(SystemColors.Window));

    /// <summary>Focus rectangle in colors (cf. DrawFocusRectangle(Graphics, Rectangle, Color, Color)).</summary>
    public static void DrawFocusRectangle(Draw.Graphics graphics, Draw.Rectangle rectangle, Draw.Color foreColor, Draw.Color backColor)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        // Dotted by hand: the rasterizer stores dash styles without applying them.
        using var pen = new Draw.Pen(foreColor, 1f);
        DottedLine(graphics, pen, rectangle.X, rectangle.Y, rectangle.Right - 1, rectangle.Y);
        DottedLine(graphics, pen, rectangle.X, rectangle.Bottom - 1, rectangle.Right - 1, rectangle.Bottom - 1);
        DottedLine(graphics, pen, rectangle.X, rectangle.Y, rectangle.X, rectangle.Bottom - 1);
        DottedLine(graphics, pen, rectangle.Right - 1, rectangle.Y, rectangle.Right - 1, rectangle.Bottom - 1);
    }

    /// <summary>Scroll arrow button (cf. DrawScrollButton(Graphics, Rectangle, ScrollButton, ButtonState)).</summary>
    public static void DrawScrollButton(Draw.Graphics graphics, Draw.Rectangle rectangle, ScrollButton button, ButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        DrawButton(graphics, rectangle, state);
        bool inactive = (state & ButtonState.Inactive) != 0;
        Draw.Color arrow = ButtonRenderer.ToColor(inactive ? SystemColors.GrayText : SystemColors.ControlText);
        using var pen = new Draw.Pen(arrow, 1f);
        int cx = rectangle.X + rectangle.Width / 2;
        int cy = rectangle.Y + rectangle.Height / 2;
        int s = Math.Max(2, Math.Min(rectangle.Width, rectangle.Height) / 4);
        Draw.Point[] glyph = button switch
        {
            ScrollButton.Up => new[] { new Draw.Point(cx - s, cy + s / 2), new Draw.Point(cx, cy - s / 2), new Draw.Point(cx + s, cy + s / 2) },
            ScrollButton.Down => new[] { new Draw.Point(cx - s, cy - s / 2), new Draw.Point(cx, cy + s / 2), new Draw.Point(cx + s, cy - s / 2) },
            ScrollButton.Left => new[] { new Draw.Point(cx + s / 2, cy - s), new Draw.Point(cx - s / 2, cy), new Draw.Point(cx + s / 2, cy + s) },
            ScrollButton.Right => new[] { new Draw.Point(cx - s / 2, cy - s), new Draw.Point(cx + s / 2, cy), new Draw.Point(cx - s / 2, cy + s) },
            _ => new[] { new Draw.Point(cx - s, cy - s / 2), new Draw.Point(cx, cy + s / 2), new Draw.Point(cx + s, cy - s / 2) },
        };
        graphics.DrawLines(pen, glyph);
    }

    /// <summary>Scroll arrow button by coordinates (cf. DrawScrollButton(Graphics, int, int, int, int, ScrollButton, ButtonState)).</summary>
    public static void DrawScrollButton(Draw.Graphics graphics, int x, int y, int width, int height, ScrollButton button, ButtonState state) =>
        DrawScrollButton(graphics, new Draw.Rectangle(x, y, width, height), button, state);

    private static void DrawBoxFace(Draw.Graphics graphics, Draw.Rectangle rectangle, ButtonState state)
    {
        using (var fill = new Draw.SolidBrush(ButtonRenderer.ToColor(SystemColors.Window)))
            graphics.FillRectangle(fill, rectangle);
        Draw.Color edge = ButtonRenderer.ToColor(
            (state & ButtonState.Flat) != 0 ? SystemColors.ControlDark : SystemColors.ControlDarkDark);
        using var outline = new Draw.Pen(edge, 1f);
        graphics.DrawRectangle(outline, rectangle);
        if ((state & ButtonState.Pushed) != 0)
        {
            using var sunken = new Draw.Pen(ButtonRenderer.ToColor(SystemColors.ControlDark), 1f);
            graphics.DrawRectangle(sunken, Draw.Rectangle.Inflate(rectangle, -1, -1));
        }
    }

    private static void DottedLine(Draw.Graphics graphics, Draw.Pen pen, int x1, int y1, int x2, int y2)
    {
        int dx = Math.Sign(x2 - x1);
        int dy = Math.Sign(y2 - y1);
        int x = x1;
        int y = y1;
        bool on = true;
        while (true)
        {
            if (on)
                graphics.DrawLine(pen, x, y, x, y);
            if (x == x2 && y == y2)
                break;
            x += dx;
            y += dy;
            on = !on;
        }
    }

    public static void DrawButton(Draw.Graphics graphics, Draw.Rectangle rectangle, ButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        using (var fill = new Draw.SolidBrush(ButtonRenderer.ToColor(SystemColors.Control)))
            graphics.FillRectangle(fill, rectangle);
        bool pushed = (state & ButtonState.Pushed) != 0;
        bool flat = (state & ButtonState.Flat) != 0
            || (state & (ButtonState.Inactive | ButtonState.Checked)) != 0;
        int x = rectangle.X;
        int y = rectangle.Y;
        int r = x + rectangle.Width - 1;
        int b = y + rectangle.Height - 1;
        if (flat)
        {
            using var edge = new Draw.Pen(Draw.Color.Gray, 1f);
            graphics.DrawRectangle(edge, rectangle);
            return;
        }

        Draw.Color light = ButtonRenderer.ToColor(SystemColors.ControlLightLight);
        Draw.Color dark = ButtonRenderer.ToColor(SystemColors.ControlDarkDark);
        Draw.Color tl = pushed ? dark : light;
        Draw.Color br = pushed ? light : dark;
        using (var top = new Draw.Pen(tl, 1f))
        {
            graphics.DrawLine(top, x, y, r, y);
            graphics.DrawLine(top, x, y, x, b);
        }

        using (var bottom = new Draw.Pen(br, 1f))
        {
            graphics.DrawLine(bottom, x, b, r, b);
            graphics.DrawLine(bottom, r, y, r, b);
        }
    }
}

/// <summary>
/// Visual-style part painter (cf. System.Windows.Forms.VisualStyles.VisualStyleRenderer).
/// No theme engine exists on web, so IsSupported is false (apps fall back to
/// the ControlPaint/glyph path) and DrawBackground is a documented no-op:
/// an IDeviceContext carries only an Hdc with no canvas surface to paint to.
/// </summary>
public class VisualStyleRenderer
{
    public VisualStyleRenderer(string className, int part, int state)
    {
        SetParameters(className, part, state);
    }

    public static bool IsSupported => false;

    public string Class { get; private set; } = "";

    public int Part { get; private set; }

    public int State { get; private set; }

    public IntPtr Handle => IntPtr.Zero;

    public void SetParameters(string className, int part, int state)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        Class = className;
        Part = part;
        State = state;
    }

    public void DrawBackground(WfDraw::System.Drawing.IDeviceContext dc, Draw.Rectangle bounds)
    {
    }

    /// <summary>
    /// Theme background painter (cf. DrawBackground(IDeviceContext, Rectangle, Rectangle)).
    /// No-op: no theme engine exists.
    /// </summary>
    public void DrawBackground(WfDraw::System.Drawing.IDeviceContext dc, Draw.Rectangle bounds, Draw.Rectangle clipRectangle)
    {
    }

    /// <summary>
    /// Paints the parent background (cf. DrawParentBackground). No-op: no
    /// theme engine exists, matching the DrawBackground contract above.
    /// </summary>
    public void DrawParentBackground(WfDraw::System.Drawing.IDeviceContext dc, Draw.Rectangle bounds, Control? control)
    {
    }

    /// <summary>Content bounds (cf. GetBackgroundContentRectangle). Identity.</summary>
    public Draw.Rectangle GetBackgroundContentRectangle(WfDraw::System.Drawing.IDeviceContext dc, Draw.Rectangle bounds) =>
        bounds;

    /// <summary>Theme color (cf. GetColor). Falls back to black: no theme data.</summary>
    public Draw.Color GetColor(ColorProperty prop) => Draw.Color.Black;

    /// <summary>Theme part size (cf. GetPartSize(IDeviceContext, ThemeSizeType)). Empty: no theme metrics.</summary>
    public Draw.Size GetPartSize(WfDraw::System.Drawing.IDeviceContext dc, ThemeSizeType type) =>
        Draw.Size.Empty;

    /// <summary>Theme part size in bounds (cf. GetPartSize(IDeviceContext, Rectangle, ThemeSizeType)). Identity.</summary>
    public Draw.Size GetPartSize(WfDraw::System.Drawing.IDeviceContext dc, Draw.Rectangle bounds, ThemeSizeType type) =>
        bounds.Size;

    /// <summary>Theme point (cf. GetPoint). Origin: no theme data.</summary>
    public Draw.Point GetPoint(PointProperty prop) => Draw.Point.Empty;

    /// <summary>Whether the element is defined (cf. IsElementDefined). Always false: no theme engine.</summary>
    public static bool IsElementDefined(VisualStyleElement element) => false;
}

/// <summary>
/// Group-box painter (cf. System.Windows.Forms.GroupBoxRenderer). Classic
/// frame + label gap; themed bitmaps await the renderer theming milestone.
/// </summary>
public static class GroupBoxRenderer
{
    /// <summary>Whether to match the app's themed state (cf. RenderMatchingApplicationState).</summary>
    public static bool RenderMatchingApplicationState { get; set; } = true;

    /// <summary>Whether the background is translucent (cf. IsBackgroundPartiallyTransparent). False.</summary>
    public static bool IsBackgroundPartiallyTransparent(GroupBoxState state) => false;

    /// <summary>Paints the parent background (cf. DrawParentBackground). No-op.</summary>
    public static void DrawParentBackground(Draw.Graphics graphics, Draw.Rectangle bounds, Control control)
    {
    }

    /// <summary>Draws an empty group frame (cf. DrawGroupBox(Graphics, Rectangle, GroupBoxState)).</summary>
    public static void DrawGroupBox(Draw.Graphics graphics, Draw.Rectangle bounds, GroupBoxState state) =>
        DrawGroupBox(graphics, bounds, null, null, state);

    /// <summary>Draws a group frame with text (cf. DrawGroupBox(Graphics, Rectangle, string, Font, GroupBoxState)).</summary>
    public static void DrawGroupBox(Draw.Graphics graphics, Draw.Rectangle bounds, string? text, Draw.Font? font, GroupBoxState state) =>
        DrawGroupBox(graphics, bounds, text, font, ButtonRenderer.ToColor(SystemColors.ControlText), state);

    /// <summary>Draws a group frame with colored text (cf. DrawGroupBox(Graphics, Rectangle, string, Font, Color, GroupBoxState)).</summary>
    public static void DrawGroupBox(Draw.Graphics graphics, Draw.Rectangle bounds, string? text, Draw.Font? font, Draw.Color textColor, GroupBoxState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        Draw.Color edge = ButtonRenderer.ToColor(
            state == GroupBoxState.Disabled ? SystemColors.GrayText : SystemColors.ControlDark);
        using var pen = new Draw.Pen(edge, 1f);
        int top = bounds.Y + 7;
        graphics.DrawLine(pen, bounds.X, top, bounds.Right - 1, top);
        graphics.DrawLine(pen, bounds.X, top, bounds.X, bounds.Bottom - 1);
        graphics.DrawLine(pen, bounds.X, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
        graphics.DrawLine(pen, bounds.Right - 1, top, bounds.Right - 1, bounds.Bottom - 1);
    }

    /// <summary>Draws a group frame with format flags (cf. DrawGroupBox(Graphics, Rectangle, string, Font, TextFormatFlags, GroupBoxState)).</summary>
    public static void DrawGroupBox(Draw.Graphics graphics, Draw.Rectangle bounds, string? text, Draw.Font? font, TextFormatFlags flags, GroupBoxState state) =>
        DrawGroupBox(graphics, bounds, text, font, ButtonRenderer.ToColor(SystemColors.ControlText), flags, state);

    /// <summary>Draws a group frame with color and flags (cf. DrawGroupBox(Graphics, Rectangle, string, Font, Color, TextFormatFlags, GroupBoxState)).</summary>
    public static void DrawGroupBox(Draw.Graphics graphics, Draw.Rectangle bounds, string? text, Draw.Font? font, Draw.Color textColor, TextFormatFlags flags, GroupBoxState state) =>
        DrawGroupBox(graphics, bounds, text, font, textColor, state);
}
