using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Text.Json;

namespace WfWasm.CanvasHost.Tests.Fixtures;

// Link this test-only fixture into a dedicated browser test build. Do not
// replace its OnPaint path with JS drawing; the acceptance harness relies on
// real WinForms Graphics calls traversing the production drawing adapter.
public sealed class GpuContractControl : Control
{
    private Color _accent = Color.Red;
    private readonly Bitmap _tile = new(2, 2);
    private JsonElement? _program;
    public int ApplicationPaintCount { get; private set; }

    public GpuContractControl()
    {
        Width = 128;
        Height = 96;
        _tile.SetPixel(0, 0, Color.Red);
        _tile.SetPixel(1, 0, Color.Blue);
        _tile.SetPixel(0, 1, Color.Lime);
        _tile.SetPixel(1, 1, Color.White);
    }

    public void SetAccent(byte r, byte g, byte b, byte a)
    {
        _program = null;
        _accent = Color.FromArgb(a, r, g, b);
        Invalidate();
    }

    public void SetProgram(string json)
    {
        using var doc = JsonDocument.Parse(json);
        _program = doc.RootElement.Clone();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ApplicationPaintCount++;
        Graphics g = e.Graphics;
        if (_program is JsonElement program)
        {
            PaintProgram(g, program);
            return;
        }
        g.Clear(Color.White);
        using var accent = new SolidBrush(_accent);
        g.FillRectangle(accent, new Rectangle(12, 12, 32, 32));
        using var gradient = new LinearGradientBrush(new Rectangle(64, 12, 48, 20), Color.Red, Color.Blue, 0f);
        g.FillRectangle(gradient, new Rectangle(64, 12, 48, 20));
        using var path = new GraphicsPath();
        path.AddEllipse(new Rectangle(64, 44, 24, 24));
        g.FillPath(accent, path);
        using var pen = new Pen(Color.Black, 1);
        g.DrawPath(pen, path);
        using var texture = new TextureBrush(_tile);
        var saved = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.SetClip(new Rectangle(100, 44, 16, 16));
        g.FillRectangle(texture, new Rectangle(96, 40, 24, 24));
        g.Restore(saved);
        using var font = new Font("sans-serif", 10);
        using var ink = new SolidBrush(Color.Black);
        g.DrawString("GPU", font, ink, new PointF(4, 72));
    }

    private static void PaintProgram(Graphics g, JsonElement program)
    {
        var states = new Stack<GraphicsState>();
        static float[] Numbers(JsonElement value) => value.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        static Color Rgba(JsonElement value)
        {
            int[] c = value.EnumerateArray().Select(x => x.GetInt32()).ToArray();
            return Color.FromArgb(c[3], c[0], c[1], c[2]);
        }
        static RectangleF Rect(JsonElement value)
        {
            float[] a = Numbers(value);
            return new RectangleF(a[0], a[1], a[2], a[3]);
        }
        foreach (var command in program.EnumerateArray())
        {
            switch (command.GetProperty("op").GetString())
            {
                case "clear": g.Clear(Rgba(command.GetProperty("color"))); break;
                case "fill":
                    using (var brush = new SolidBrush(Rgba(command.GetProperty("color"))))
                        g.FillRectangle(brush, Rect(command.GetProperty("rect")));
                    break;
                case "save": states.Push(g.Save()); break;
                case "restore": g.Restore(states.Pop()); break;
                case "clip": g.SetClip(Rect(command.GetProperty("rect"))); break;
                case "exclude": g.ExcludeClip(Rect(command.GetProperty("rect"))); break;
                case "clipUnion":
                    var rectangles = command.GetProperty("rects").EnumerateArray().Select(Rect).ToArray();
                    using (var region = new Region(rectangles[0]))
                    {
                        foreach (var rectangle in rectangles.Skip(1)) region.Union(rectangle);
                        g.SetClip(region, CombineMode.Replace);
                    }
                    break;
                case "transform":
                    float[] m = Numbers(command.GetProperty("matrix"));
                    using (var transform = new Matrix(m[0], m[1], m[2], m[3], m[4], m[5])) g.Transform = transform;
                    break;
                case "composite":
                    g.CompositingMode = command.GetProperty("value").GetString() == "copy"
                        ? CompositingMode.SourceCopy : CompositingMode.SourceOver;
                    break;
                case "brushMutation":
                    using (var mutable = new SolidBrush(Color.Red))
                    {
                        g.FillRectangle(mutable, new Rectangle(8, 8, 16, 16));
                        mutable.Color = Color.Blue;
                        g.FillRectangle(mutable, new Rectangle(40, 8, 16, 16));
                    }
                    break;
                case "imageMutation":
                    using (var image = new Bitmap(2, 2))
                    {
                        for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) image.SetPixel(x, y, Color.Red);
                        using var clone = (Bitmap)image.Clone();
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.DrawImage(image, new Rectangle(8, 8, 16, 16));
                        for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) image.SetPixel(x, y, Color.Blue);
                        g.DrawImage(image, new Rectangle(40, 8, 16, 16));
                        g.DrawImage(clone, new Rectangle(72, 8, 16, 16));
                    }
                    break;
                default: throw new InvalidOperationException("Unknown contract drawing operation");
            }
        }
        if (states.Count != 0) throw new InvalidOperationException("Unbalanced test drawing state");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tile.Dispose();
        base.Dispose(disposing);
    }
}
