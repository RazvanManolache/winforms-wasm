using WfWasm.Renderer;
using WfWasm.Shim;

public class PaintTests
{
    [Fact]
    public void Pencil_Stroke_PaintsSegments()
    {
        var canvas = new PaintCanvas("p") { Width = 100, Height = 100 };
        canvas.BeginStroke(10, 10);
        canvas.AddPoint(20, 10);
        canvas.AddPoint(20, 20);
        canvas.EndStroke();

        Assert.Single(canvas.Strokes);
        var r = new RecordingRenderer();
        canvas.Render(r);
        var lines = r.Commands.OfType<DrawLineCommand>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(1, l.Width));
        Assert.Equal((10f, 10f, 20f, 10f), (lines[0].X1, lines[0].Y1, lines[0].X2, lines[0].Y2));
    }

    [Fact]
    public void Dot_Stroke_PaintsSingleDot()
    {
        var canvas = new PaintCanvas("p") { Width = 100, Height = 100 };
        canvas.BeginStroke(50, 50);
        canvas.EndStroke();

        var r = new RecordingRenderer();
        canvas.Render(r);
        var dot = Assert.IsType<DrawLineCommand>(r.Commands.OfType<DrawLineCommand>().Single());
        Assert.Equal((50f, 50f, 50f, 50f), (dot.X1, dot.Y1, dot.X2, dot.Y2));
    }

    [Fact]
    public void Shape_RubberBands_NormalizesBox()
    {
        var canvas = new PaintCanvas("p")
        {
            Width = 100, Height = 100, Tool = PaintTool.Rectangle, PaintColor = 0xFF0000FF,
        };
        canvas.BeginStroke(80, 70);
        canvas.AddPoint(60, 50);
        canvas.AddPoint(40, 30);
        canvas.EndStroke();

        var r = new RecordingRenderer();
        canvas.Render(r);
        // Outline extends from the normalized min corner.
        Assert.Contains(r.Commands.OfType<FillRectCommand>(),
            c => c.X <= 41 && c.Y <= 31 && c.Rgba == 0xFF0000FF);
    }

    [Fact]
    public void Eraser_PaintsPaperColor()
    {
        var canvas = new PaintCanvas("p") { Width = 100, Height = 100, Tool = PaintTool.Eraser };
        canvas.BeginStroke(10, 10);
        canvas.EndStroke();

        var r = new RecordingRenderer();
        canvas.Render(r);
        var line = Assert.IsType<DrawLineCommand>(r.Commands.OfType<DrawLineCommand>().Single());
        Assert.Equal((uint)SystemColors.Window, line.Rgba);
    }

    [Fact]
    public void Undo_And_Clear_RemoveStrokes()
    {
        var canvas = new PaintCanvas("p") { Width = 100, Height = 100 };
        Assert.False(canvas.Undo());
        canvas.BeginStroke(1, 1);
        canvas.EndStroke();
        canvas.BeginStroke(2, 2);
        canvas.EndStroke();
        Assert.True(canvas.Undo());
        Assert.Single(canvas.Strokes);
        canvas.ClearPaper();
        Assert.Empty(canvas.Strokes);
    }

    [Fact]
    public void App_PaintScene_StrokeAndSave()
    {
        var app = new CanvasApp();
        app.KeyChar('3');
        Assert.Equal(AppScene.Paint, app.Current);

        static PaintCanvas Find(Control node)
        {
            if (node is PaintCanvas canvas)
                return canvas;
            foreach (Control child in node.Children)
            {
                try
                {
                    return Find(child);
                }
                catch (InvalidOperationException)
                {
                }
            }

            throw new InvalidOperationException("No paint canvas.");
        }

        PaintCanvas paper = Find(app.CurrentSceneRoot);
        // Paper at form (20,20) + frame/client (3,31) + offset (160,36).
        int px = 20 + 3 + 160;
        int py = 20 + 31 + 36;
        bool saved = false;
        app.SaveRequested += (_, _) => saved = true;

        app.MouseDownAt(px + 10, py + 10);
        app.ClickAt(px + 10, py + 10);
        app.MouseMoveAt(px + 30, py + 10);
        app.MouseUpAt(px + 30, py + 10);
        Assert.Single(paper.Strokes);

        // File menu at form client (0,0): open it, then click Save PNG (row 1).
        app.ClickAt(23 + 30, 51 + 12);
        app.ClickAt(23 + 4 + 60, 51 + 24 + 2 + 22 + 11);
        Assert.True(saved);
    }
}
