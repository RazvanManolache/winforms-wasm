using System.Drawing;
using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsTextBox = System.Windows.Forms.TextBox;

public sealed class TextBoxMultilinePaintTests
{
    [Fact]
    public void Multiline_Text_Paints_Lines_From_Top()
    {
        // Native multiline EDIT paints lines top-left; the host painted
        // every TextBox (single-line AND multiline) as one vertically
        // centered run (spellcheck boxes: text sits mid-box in browser,
        // top-left native). Multiline must emit one Top-anchored run per
        // line starting at the top edge.
        var draws = PaintTextBox(multiline: true, text: "first\r\nsecond");
        var runs = draws.FindAll(d => d.Text is "first" or "second");
        Assert.Equal(2, runs.Count);
        foreach (var run in runs)
            Assert.Equal(TextVAlign.Top, run.VAlign);
        Assert.True(runs[0].Y < 30f, $"first line near top, got y={runs[0].Y}");
        Assert.True(runs[1].Y > runs[0].Y, "lines stack downward");
    }

    [Fact]
    public void Singleline_Text_Stays_Vertically_Centered()
    {
        // Single-line native EDIT centers the run: pin existing behavior.
        var draws = PaintTextBox(multiline: false, text: "hello");
        var runs = draws.FindAll(d => d.Text == "hello");
        Assert.Single(runs);
        Assert.Equal(TextVAlign.Middle, runs[0].VAlign);
    }

    private static List<DrawTextCommand> PaintTextBox(bool multiline, string text)
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var form = new System.Windows.Forms.Form();
        using var box = new FormsTextBox
        {
            Multiline = multiline,
            Text = text,
            Font = new Font("Segoe UI", 9f),
            Bounds = new Rectangle(10, 20, 200, 60),
        };
        form.Controls.Add(box);
        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        form.Left = 0;
        form.Top = 0;
        form.Show();
        try
        {
            var recorder = new RecordingRenderer();
            recorder.BeginFrame(400, 400);
            MethodInfo render = typeof(WinFormsAppHost).GetMethod(
                "RenderControls",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.NotNull(render);
            render.Invoke(host, [recorder, form.Controls, 0, 0]);
            recorder.EndFrame();
            var draws = new List<DrawTextCommand>();
            foreach (DrawCommand cmd in recorder.Commands)
                if (cmd is DrawTextCommand t)
                    draws.Add(t);
            return draws;
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }
}
