namespace WfWasm.Shim;

/// <summary>
/// Static message box (cf. System.Windows.Forms.MessageBox): records the
/// request and returns OK. The browser host mirrors the request into a
/// canvas modal via CanvasApp.ShowMessage; headless/boot runs observe it
/// through LastText/LastCaption/ShowCount.
/// </summary>
public static class MessageBox
{
    public static string LastText { get; private set; } = "";
    public static string LastCaption { get; private set; } = "";
    public static int ShowCount { get; private set; }

    public static DialogResult Show(string text) => Show(text, "");

    public static DialogResult Show(string text, string caption)
    {
        LastText = text;
        LastCaption = caption;
        ShowCount++;
        return DialogResult.OK;
    }

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons) =>
        Show(text, caption);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        Show(text, caption);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) =>
        Show(text, caption);

    public static DialogResult Show(IWin32Window owner, string text) => Show(text, "");

    public static DialogResult Show(IWin32Window owner, string text, string caption) =>
        Show(text, caption);

    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons) =>
        Show(text, caption);

    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        Show(text, caption);

    internal static void Reset()
    {
        LastText = "";
        LastCaption = "";
        ShowCount = 0;
    }
}

/// <summary>
/// Blocking-style message box as a canvas overlay (cf. MessageBox.Show):
/// dimmed backdrop, centered dialog, OK button. The owning CanvasApp keeps
/// it above the scene and routes all input to it until dismissed.
/// </summary>
public sealed class MessageBoxOverlay
{
    public const int DialogWidth = 440;
    public const int MinDialogHeight = 160;

    public Form Window { get; }

    public Button OkButton { get; }

    internal MessageBoxOverlay(string text, string caption)
    {
        List<string> lines = Wrap(text, 52);
        int height = Math.Max(MinDialogHeight, 48 + (lines.Count * 18) + 64);
        Window = new Form("messageBox")
        {
            X = (CanvasApp.ViewWidth - DialogWidth) / 2,
            Y = (CanvasApp.ViewHeight - height) / 2,
            Width = DialogWidth,
            Height = height,
            Text = caption,
        };
        int ly = 48;
        foreach (string line in lines)
        {
            Window.AddChild(new Label("mbText")
            {
                X = 16, Y = ly, Width = DialogWidth - 32, Height = 18, Text = line,
            });
            ly += 18;
        }

        OkButton = new Button("mbOk")
        {
            X = (DialogWidth - 90) / 2, Y = height - 28 - 20,
            Width = 90, Height = 28, Text = "OK", IsDefault = true,
        };
        Window.AddChild(OkButton);
    }

    internal static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            string rest = paragraph;
            while (rest.Length > width)
            {
                int cut = rest.LastIndexOf(' ', width);
                if (cut <= 0)
                    cut = width;
                lines.Add(rest[..cut]);
                rest = rest[cut..].TrimStart();
            }

            lines.Add(rest);
        }

        return lines;
    }
}
