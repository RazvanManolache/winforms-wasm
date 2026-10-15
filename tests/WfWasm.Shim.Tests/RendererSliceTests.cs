extern alias WfDraw;

using WfWasm.Shim;
using DrawBitmap = WfDraw::System.Drawing.Bitmap;
using DrawGraphics = WfDraw::System.Drawing.Graphics;
using DrawImage = WfDraw::System.Drawing.Image;
using DrawPoint = WfDraw::System.Drawing.Point;
using DrawRectangle = WfDraw::System.Drawing.Rectangle;
using DrawSize = WfDraw::System.Drawing.Size;

public sealed class RendererSliceTests
{
    private static DrawGraphics OnBitmap(int width, int height, out DrawBitmap bitmap)
    {
        bitmap = new DrawBitmap(width, height);
        return DrawGraphics.FromImage(bitmap);
    }

    [Fact]
    public void CheckBox_GetGlyphSize_Is13x13()
    {
        using DrawGraphics g = OnBitmap(32, 32, out _);
        Assert.Equal(new DrawSize(13, 13), CheckBoxRenderer.GetGlyphSize(g, CheckBoxState.UncheckedNormal));
        Assert.Equal(new DrawSize(13, 13), CheckBoxRenderer.GetGlyphSize(g, CheckBoxState.MixedDisabled));
    }

    [Fact]
    public void Button_DrawButton_Stateless_AllStates_NoThrow()
    {
        using DrawGraphics g = OnBitmap(64, 32, out _);
        var bounds = new DrawRectangle(0, 0, 64, 32);
        foreach (PushButtonState state in new[] { PushButtonState.Normal, PushButtonState.Hot, PushButtonState.Pressed, PushButtonState.Disabled, PushButtonState.Default })
        {
            ButtonRenderer.DrawButton(g, bounds, state);
            ButtonRenderer.DrawButton(g, bounds, focused: true, state);
        }
    }

    [Fact]
    public void Button_DrawButton_Text_Requires_TextRaster_Milestone()
    {
        using DrawGraphics g = OnBitmap(64, 32, out _);
        var bounds = new DrawRectangle(0, 0, 64, 32);
        // Empty text skips DrawString and paints the face.
        ButtonRenderer.DrawButton(g, bounds, "", Control.DefaultFont, focused: false, PushButtonState.Normal);
        Assert.Throws<NotSupportedException>(() =>
            ButtonRenderer.DrawButton(g, bounds, "OK", Control.DefaultFont, focused: false, PushButtonState.Normal));
    }

    [Fact]
    public void Button_DrawButton_ImageOverloads_NoThrow()
    {
        using DrawGraphics g = OnBitmap(64, 32, out _);
        using var image = new DrawBitmap(8, 8);
        var bounds = new DrawRectangle(0, 0, 64, 32);
        var imageBounds = new DrawRectangle(4, 4, 8, 8);
        ButtonRenderer.DrawButton(g, bounds, image, imageBounds, focused: false, PushButtonState.Normal);
        ButtonRenderer.DrawButton(g, bounds, "", Control.DefaultFont, image, imageBounds, focused: true, PushButtonState.Hot);
        ButtonRenderer.DrawParentBackground(g, bounds, new Control("c"));
    }

    [Fact]
    public void CheckBox_DrawCheckBox_AllStates_NoThrow()
    {
        using DrawGraphics g = OnBitmap(64, 64, out _);
        var at = new DrawPoint(2, 2);
        for (int s = 1; s <= 12; s++)
            CheckBoxRenderer.DrawCheckBox(g, at, (CheckBoxState)s);
        var textBounds = new DrawRectangle(18, 0, 40, 16);
        CheckBoxRenderer.DrawCheckBox(g, at, textBounds, "", Control.DefaultFont, focused: false, CheckBoxState.CheckedNormal);
        Assert.Throws<NotSupportedException>(() =>
            CheckBoxRenderer.DrawCheckBox(g, at, textBounds, "Pick", Control.DefaultFont, focused: false, CheckBoxState.CheckedNormal));
        CheckBoxRenderer.DrawParentBackground(g, textBounds, null);
    }

    [Fact]
    public void ControlPaint_DrawButton_NoThrow()
    {
        using DrawGraphics g = OnBitmap(48, 24, out _);
        var bounds = new DrawRectangle(0, 0, 48, 24);
        ControlPaint.DrawButton(g, bounds, ButtonState.Normal);
        ControlPaint.DrawButton(g, bounds, ButtonState.Pushed);
        ControlPaint.DrawButton(g, bounds, ButtonState.Inactive);
        ControlPaint.DrawButton(g, bounds, ButtonState.Flat | ButtonState.Checked);
    }

    [Fact]
    public void ButtonState_Has_Framework_Flag_Values()
    {
        Assert.Equal(0, (int)ButtonState.Normal);
        Assert.Equal(256, (int)ButtonState.Inactive);
        Assert.Equal(512, (int)ButtonState.Pushed);
        Assert.Equal(1024, (int)ButtonState.Checked);
        Assert.Equal(16384, (int)ButtonState.Flat);
    }

    [Fact]
    public void VisualStyleRenderer_Is_Documented_NoOp()
    {
        Assert.False(VisualStyleRenderer.IsSupported);
        var renderer = new VisualStyleRenderer("Button", 1, 1);
        Assert.Equal("Button", renderer.Class);
        Assert.Equal(IntPtr.Zero, renderer.Handle);
        renderer.DrawBackground(null!, new DrawRectangle(0, 0, 10, 10));
        renderer.DrawBackground(null!, new DrawRectangle(0, 0, 10, 10), new DrawRectangle(0, 0, 5, 5));
    }

    [Fact]
    public void PictureBox_Stores_Image_And_SizeMode()
    {
        var box = new PictureBox { Width = 64, Height = 48 };
        Assert.Null(box.Image);
        Assert.Equal(PictureBoxSizeMode.Normal, box.SizeMode);
        using var image = new DrawBitmap(8, 8);
        box.Image = image;
        box.SizeMode = PictureBoxSizeMode.StretchImage;
        Assert.Same(image, box.Image);
    }

    [Fact]
    public void ToolStripItem_Image_RoundTrips()
    {
        var button = new ToolStripButton("b");
        Assert.Null(button.Image);
        Assert.Equal(-1, button.ImageIndex);
        using var image = new DrawBitmap(16, 16);
        button.Image = image;
        Assert.Same(image, button.Image);

        var strip = new ToolStrip();
        ToolStripItem added = strip.Items.Add(image);
        Assert.Same(image, added.Image);
        bool clicked = false;
        ToolStripItem withHandler = strip.Items.Add("t", image, (_, _) => clicked = true);
        withHandler.PerformClick();
        Assert.True(clicked);
    }

    [Fact]
    public void MessageBox_Show_Overloads_Return_Ok()
    {
        Assert.Equal(DialogResult.OK, MessageBox.Show("t", "c", MessageBoxButtons.YesNo));
        Assert.Equal(DialogResult.OK, MessageBox.Show("t", "c", MessageBoxButtons.YesNo, MessageBoxIcon.Question));
        Assert.Equal(DialogResult.OK, MessageBox.Show("t", "c", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2));
        Assert.Equal(DialogResult.OK, MessageBox.Show(new Control("owner"), "t", "c"));
        Assert.Equal("t", MessageBox.LastText);
        Assert.Equal("c", MessageBox.LastCaption);
        Assert.Equal(MessageBoxDefaultButton.Button2, (MessageBoxDefaultButton)256);
    }

    [Fact]
    public void MenuStrip_And_StatusStrip_Gap_Members()
    {
        var menu = new MenuStrip();
        menu.ShowItemToolTips = true;
        Assert.True(menu.ShowItemToolTips);

        var label = new ToolStripStatusLabel("ready");
        Assert.False(label.Spring);
        label.Spring = true;

        var strip = new PadStrip();
        Padding pad = strip.Expose();
        Assert.Equal((1, 0, 14, 0), (pad.Left, pad.Top, pad.Right, pad.Bottom));
    }

    [Fact]
    public void RenderEventArgs_Carry_Graphics()
    {
        using DrawGraphics g = OnBitmap(16, 16, out _);
        var strip = new ToolStrip();
        var item = new ToolStripButton("i");

        var stripArgs = new ToolStripRenderEventArgs(g, strip);
        Assert.Same(g, stripArgs.Graphics);

        var itemArgs = new ToolStripItemRenderEventArgs(g, item);
        Assert.Same(g, itemArgs.Graphics);

        var arrowArgs = new ToolStripArrowRenderEventArgs(g, item, new DrawRectangle(0, 0, 8, 8), ArrowDirection.Down);
        Assert.Same(g, arrowArgs.Graphics);

        var imageArgs = new ToolStripItemImageRenderEventArgs(g, item, new DrawRectangle(0, 0, 8, 8));
        Assert.Same(g, imageArgs.Graphics);
        Assert.Same(item.Image, imageArgs.Image);

        var panelArgs = new ToolStripPanelRenderEventArgs(g, new ToolStripPanel());
        Assert.Same(g, panelArgs.Graphics);
        Assert.NotNull(panelArgs.ToolStripPanel);
        panelArgs.Handled = true;
        strip.Renderer.DrawToolStripPanelBackground(panelArgs);

        var paintArgs = new PaintEventArgs(g, new DrawRectangle(0, 0, 8, 8));
        Assert.Same(g, paintArgs.Graphics);
        var drawArgs = new DrawItemEventArgs(g, Control.DefaultFont, new DrawRectangle(0, 0, 8, 8), 0, DrawItemState.None);
        Assert.Same(g, drawArgs.Graphics);
    }

    [Fact]
    public void CreateDisabledImage_Returns_Grayscale_Copy()
    {
        using var source = new DrawBitmap(4, 4);
        source.SetPixel(0, 0, WfDraw::System.Drawing.Color.FromArgb(255, 200, 100, 50));
        var renderer = new ProbeRenderer();
        DrawImage gray = renderer.Expose(source);
        WfDraw::System.Drawing.Color c = ((DrawBitmap)gray).GetPixel(0, 0);
        Assert.Equal(c.R, c.G);
        Assert.Equal(c.G, c.B);
    }

    private sealed class PadStrip : StatusStrip
    {
        public Padding Expose() => DefaultPadding;
    }

    private sealed class ProbeRenderer : ToolStripRenderer
    {
        public DrawImage Expose(DrawImage image) => CreateDisabledImage(image);
    }
}
