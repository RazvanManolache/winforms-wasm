extern alias WfDraw;

namespace WfWasm.Shim;

/// <summary>
/// Image display box (cf. System.Windows.Forms.PictureBox). Image/SizeMode/
/// BorderStyle are stored and laid out; raster blitting waits for an image
/// path on ICanvas, so PaintSelf currently draws the frame and background.
/// </summary>
public class PictureBox : Control
{
    public PictureBox(string name = "")
        : base(name)
    {
    }

    public PictureBox()
        : this("")
    {
    }

    public WfDraw::System.Drawing.Image? Image { get; set; }

    public PictureBoxSizeMode SizeMode { get; set; } = PictureBoxSizeMode.Normal;

    public BorderStyle BorderStyle { get; set; } = BorderStyle.None;

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        canvas.FillRectangle(ax, ay, Width, Height, SystemColors.Window);
        switch (BorderStyle)
        {
            case BorderStyle.FixedSingle:
                ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
                    SystemColors.ControlDarkDark, ControlPaintEdges.LineBorderStyle.Solid);
                break;
            case BorderStyle.Fixed3D:
                ControlPaintEdges.DrawEdge(canvas, ax, ay, Width, Height,
                    ControlPaintEdges.EdgeStyle.Sunken);
                break;
        }
    }
}
