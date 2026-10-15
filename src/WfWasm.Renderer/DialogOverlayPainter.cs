using WfWasm.Shim;

namespace WfWasm.Renderer;

/// <summary>
/// Paints a <see cref="DialogPageModel"/> as a centered overlay window.
/// First real stage-2-to-3 binding: dialog state in, canvas commands out.
/// </summary>
public static class DialogOverlayPainter
{
    private const uint Backdrop = 0x00000080;
    private const uint WindowFill = 0xFFFFFFFF;
    private const uint TitleFill = 0x0078D7FF;
    private const uint TitleText = 0xFFFFFFFF;
    private const uint BodyText = 0x000000FF;
    private const uint ButtonFill = 0xE1E1E1FF;
    private const uint DefaultButtonFill = 0x0078D7FF;
    private const uint ButtonText = 0x000000FF;
    private const uint DefaultButtonText = 0xFFFFFFFF;
    private const uint TrackFill = 0xCCCCCCFF;
    private const uint BarFill = 0x0078D7FF;

    public static void Paint(ICanvasRenderer canvas, DialogPageModel page, string title, int width = 800, int height = 450)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(title);

        const int windowWidth = 480;
        const int windowHeight = 260;
        int left = (width - windowWidth) / 2;
        int top = (height - windowHeight) / 2;

        canvas.BeginFrame(width, height);
        canvas.FillRectangle(0, 0, width, height, Backdrop);
        canvas.FillRectangle(left, top, windowWidth, windowHeight, WindowFill);
        canvas.FillRectangle(left, top, windowWidth, 28, TitleFill);
        canvas.DrawText(title, left + 10, top + 19, "13px sans-serif", TitleText);

        int y = top + 52;
        if (!string.IsNullOrEmpty(page.Heading))
        {
            canvas.DrawText(page.Heading, left + 16, y, "bold 16px sans-serif", BodyText);
            y += 26;
        }

        if (!string.IsNullOrEmpty(page.Text))
        {
            foreach (string line in page.Text.Split('\n'))
            {
                canvas.DrawText(line.TrimEnd('\r'), left + 16, y, "13px sans-serif", BodyText);
                y += 20;
            }
        }

        if (!string.IsNullOrEmpty(page.VerificationText))
        {
            string box = page.VerificationChecked ? "[x] " : "[ ] ";
            canvas.DrawText(box + page.VerificationText, left + 16, y + 4, "13px sans-serif", BodyText);
            y += 22;
        }

        if (page.ProgressState != DialogProgressState.None)
        {
            int barWidth = windowWidth - 32;
            canvas.FillRectangle(left + 16, y + 4, barWidth, 14, TrackFill);
            int fill = page.ProgressState == DialogProgressState.Marquee
                ? barWidth / 2
                : (int)(barWidth * Math.Clamp(page.ProgressValue, 0, 100) / 100.0);
            if (fill > 0)
                canvas.FillRectangle(left + 16, y + 4, fill, 14, BarFill);
            y += 24;
        }

        if (!string.IsNullOrEmpty(page.ExpanderText))
            canvas.DrawText(page.ExpanderText, left + 16, y + 4, "12px sans-serif", BodyText);

        var visible = page.Buttons.Where(b => b.Visible).ToList();
        const int buttonWidth = 90;
        const int buttonHeight = 26;
        int bx = left + windowWidth - 16 - visible.Count * (buttonWidth + 8) + 8;
        int by = top + windowHeight - 16 - buttonHeight;
        foreach (DialogButtonModel button in visible)
        {
            bool highlighted = button.IsDefault && button.Enabled;
            canvas.FillRectangle(bx, by, buttonWidth, buttonHeight,
                highlighted ? DefaultButtonFill : ButtonFill);
            canvas.DrawText(button.Text, bx + 8, by + 17, "13px sans-serif",
                highlighted ? DefaultButtonText : ButtonText);
            bx += buttonWidth + 8;
        }

        canvas.EndFrame();
    }
}
