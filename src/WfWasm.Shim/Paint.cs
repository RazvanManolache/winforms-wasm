namespace WfWasm.Shim;

/// <summary>Paint tools (cf. Paint.NET Tools window subset).</summary>
public enum PaintTool
{
    Pencil,
    Brush,
    Eraser,
    Line,
    Rectangle,
    FilledRectangle,
    Ellipse,
    FilledEllipse,
}

/// <summary>
/// One committed paint mark. Freehand tools store polylines; shape tools
/// store a normalized box. Repaints deterministically from this model, so
/// undo is just dropping the last stroke (cf. history).
/// </summary>
public sealed class PaintStroke
{
    public PaintTool Tool { get; set; }
    public uint Color { get; set; }
    public float Width { get; set; } = 1;

    /// <summary>Polyline points (freehand) or start/end (shapes).</summary>
    public List<(float X, float Y)> Points { get; } = new();

    public void Paint(ICanvas canvas, uint paper)
    {
        uint ink = Tool == PaintTool.Eraser ? paper : Color;
        switch (Tool)
        {
            case PaintTool.Pencil:
            case PaintTool.Brush:
            case PaintTool.Eraser:
                PaintPolyline(canvas, ink);
                break;
            case PaintTool.Line:
                if (Points.Count >= 2)
                    canvas.DrawLine(Points[0].X, Points[0].Y,
                        Points[^1].X, Points[^1].Y, Width, ink);
                break;
            case PaintTool.Rectangle:
                PaintRectOutline(canvas, ink);
                break;
            case PaintTool.FilledRectangle:
                PaintRectFill(canvas, ink);
                break;
            case PaintTool.Ellipse:
            case PaintTool.FilledEllipse:
                PaintOval(canvas, ink);
                break;
        }
    }

    private void PaintPolyline(ICanvas canvas, uint ink)
    {
        if (Points.Count == 0)
            return;
        if (Points.Count == 1)
        {
            // Round caps turn a zero-length line into a dot.
            canvas.DrawLine(Points[0].X, Points[0].Y, Points[0].X, Points[0].Y, Width, ink);
            return;
        }

        for (int i = 1; i < Points.Count; i++)
            canvas.DrawLine(Points[i - 1].X, Points[i - 1].Y,
                Points[i].X, Points[i].Y, Width, ink);
    }

    private (float X, float Y, float W, float H) Box()
    {
        float x1 = Points[0].X;
        float y1 = Points[0].Y;
        float x2 = Points.Count > 1 ? Points[^1].X : x1;
        float y2 = Points.Count > 1 ? Points[^1].Y : y1;
        return (Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    private void PaintRectOutline(ICanvas canvas, uint ink)
    {
        if (Points.Count == 0)
            return;
        (float x, float y, float w, float h) = Box();
        float t = Math.Max(1, Width / 2);
        canvas.FillRectangle(x - t, y - t, w + (2 * t), Width, ink);
        canvas.FillRectangle(x - t, y + h + t - Width, w + (2 * t), Width, ink);
        canvas.FillRectangle(x - t, y, Width, h, ink);
        canvas.FillRectangle(x + w + t - Width, y, Width, h, ink);
    }

    private void PaintRectFill(ICanvas canvas, uint ink)
    {
        if (Points.Count == 0)
            return;
        (float x, float y, float w, float h) = Box();
        canvas.FillRectangle(x, y, Math.Max(1, w), Math.Max(1, h), ink);
        PaintRectOutline(canvas, ink);
    }

    private void PaintOval(ICanvas canvas, uint ink)
    {
        if (Points.Count == 0)
            return;
        (float x, float y, float w, float h) = Box();
        bool filled = Tool == PaintTool.FilledEllipse;
        canvas.DrawEllipse(x, y, Math.Max(1, w), Math.Max(1, h),
            Math.Max(1, Width), ink, filled ? ink : null);
    }
}

/// <summary>A palette swatch: filled square with a selection ring.</summary>
public class ColorSwatch : Control
{
    public ColorSwatch(string name = "", uint color = 0x000000FF)
        : base(name)
    {
        SwatchColor = color;
    }

    public uint SwatchColor { get; set; }
    public bool Selected { get; set; }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        canvas.FillRectangle(ax, ay, Width, Height, SwatchColor);
        ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
            SystemColors.ControlDarkDark, ControlPaintEdges.LineBorderStyle.Solid);
        if (Selected)
            ControlPaintEdges.DrawBorder(canvas, ax - 2, ay - 2, Width + 4, Height + 4,
                SystemColors.Highlight, ControlPaintEdges.LineBorderStyle.Solid);
    }
}

/// <summary>
/// Paint canvas (cf. Paint.NET document window): paper plus committed
/// strokes plus the in-progress stroke. The host routes press/move/release
/// into BeginStroke/AddPoint/EndStroke.
/// </summary>
public class PaintCanvas : Control
{
    public PaintCanvas(string name = "")
        : base(name)
    {
    }

    public uint Paper { get; set; } = SystemColors.Window;
    public PaintTool Tool { get; set; } = PaintTool.Pencil;
    public uint PaintColor { get; set; } = SystemColors.Black;
    public float BrushWidth { get; set; } = 1;

    public List<PaintStroke> Strokes { get; } = new();
    public PaintStroke? ActiveStroke { get; private set; }

    public bool IsPainting => ActiveStroke is not null;

    public event EventHandler? StrokesChanged;

    public void BeginStroke(float x, float y)
    {
        x = Math.Clamp(x, 0, Width);
        y = Math.Clamp(y, 0, Height);
        float width = Tool switch
        {
            PaintTool.Pencil => 1,
            PaintTool.Eraser => Math.Max(8, BrushWidth),
            _ => Math.Max(1, BrushWidth),
        };
        ActiveStroke = new PaintStroke
        {
            Tool = Tool,
            Color = PaintColor,
            Width = width,
        };
        ActiveStroke.Points.Add((x, y));
    }

    public void AddPoint(float x, float y)
    {
        if (ActiveStroke is null)
            return;
        if (Tool is PaintTool.Line or PaintTool.Rectangle or PaintTool.FilledRectangle
            or PaintTool.Ellipse or PaintTool.FilledEllipse)
        {
            // Rubber band: start anchored, end follows.
            if (ActiveStroke.Points.Count == 1)
                ActiveStroke.Points.Add((x, y));
            else
                ActiveStroke.Points[^1] = (x, y);
            return;
        }

        (float lx, float ly) = ActiveStroke.Points[^1];
        if (Math.Abs(x - lx) + Math.Abs(y - ly) >= 1)
            ActiveStroke.Points.Add((x, y));
    }

    public void EndStroke()
    {
        if (ActiveStroke is null)
            return;
        Strokes.Add(ActiveStroke);
        ActiveStroke = null;
        StrokesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelStroke() => ActiveStroke = null;

    public bool Undo()
    {
        if (Strokes.Count == 0)
            return false;
        Strokes.RemoveAt(Strokes.Count - 1);
        StrokesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ClearPaper()
    {
        Strokes.Clear();
        CancelStroke();
        StrokesChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void HandleClickLocal(int localX, int localY)
    {
        if (!Enabled)
            return;
        // Press starts a stroke; the host feeds moves and releases.
        BeginStroke(localX, localY);
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        canvas.FillRectangle(ax, ay, Width, Height, Paper);
        foreach (PaintStroke stroke in Strokes)
            PaintAt(canvas, stroke, ax, ay);
        if (ActiveStroke is not null)
            PaintAt(canvas, ActiveStroke, ax, ay);
        ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
            SystemColors.ControlDarkDark, ControlPaintEdges.LineBorderStyle.Solid);
    }

    private void PaintAt(ICanvas canvas, PaintStroke stroke, int ax, int ay)
    {
        // Strokes paint in canvas-local space; shift into absolute space.
        var shifted = new PaintStroke
        {
            Tool = stroke.Tool,
            Color = stroke.Color,
            Width = stroke.Width,
        };
        foreach ((float x, float y) in stroke.Points)
            shifted.Points.Add((x + ax, y + ay));
        shifted.Paint(canvas, Paper);
    }
}
