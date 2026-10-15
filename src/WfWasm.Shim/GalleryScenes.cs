namespace WfWasm.Shim;

/// <summary>
/// Paint scene (cf. one Form per demo).
/// The builder returns a scene root for CanvasApp.
/// </summary>
public static class GalleryScenes
{

    public static Control BuildPaint(Action onSavePng)
    {
        ArgumentNullException.ThrowIfNull(onSavePng);
        var root = new Control("paint") { X = 0, Y = 0, Width = 1000, Height = 750 };
        var form = new Form("paintForm")
        {
            X = 20, Y = 20, Width = 960, Height = 700, Text = "Paint",
        };
        root.AddChild(form);

        var canvas = new PaintCanvas("paper") { X = 160, Y = 36, Width = 780, Height = 520 };
        var status = new Label("paintStatus")
            { X = 12, Y = 640, Width = 700, Height = 20, Text = "Tool: Pencil | Strokes: 0" };
        void RefreshStatus() =>
            status.Text = $"Tool: {canvas.Tool} | Strokes: {canvas.Strokes.Count}";
        canvas.StrokesChanged += (_, _) => RefreshStatus();

        var menu = new MenuStrip("paintMenu") { X = 0, Y = 0, Width = 948, Height = 24 };
        var file = new ToolStripMenuItem("&File");
        var clear = new ToolStripMenuItem("&Clear canvas");
        clear.Click += (_, _) => canvas.ClearPaper();
        var save = new ToolStripMenuItem("&Save PNG");
        save.Click += (_, _) => onSavePng();
        file.DropDownItems.Add(clear);
        file.DropDownItems.Add(save);
        var edit = new ToolStripMenuItem("&Edit");
        var undo = new ToolStripMenuItem("&Undo");
        undo.Click += (_, _) => canvas.Undo();
        edit.DropDownItems.Add(undo);
        menu.Items.Add(file);
        menu.Items.Add(edit);

        var tools = new GroupBox("gbTools")
        {
            X = 12, Y = 36, Width = 140, Height = 560, Text = "Tools",
        };
        string[] names = { "Pencil", "Brush", "Eraser", "Line", "Rect", "FillRect", "Ellipse", "FillEllipse" };
        PaintTool[] values =
        {
            PaintTool.Pencil, PaintTool.Brush, PaintTool.Eraser, PaintTool.Line,
            PaintTool.Rectangle, PaintTool.FilledRectangle, PaintTool.Ellipse, PaintTool.FilledEllipse,
        };
        for (int i = 0; i < names.Length; i++)
        {
            var radio = new RadioButton("tool" + names[i])
            {
                X = 12, Y = 24 + (i * 36), Width = 116, Height = 30,
                Text = names[i], Appearance = Appearance.Button,
            };
            PaintTool tool = values[i];
            if (tool == PaintTool.Pencil)
                radio.CheckState = CheckState.Checked;
            radio.Click += (_, _) =>
            {
                canvas.Tool = tool;
                RefreshStatus();
            };
            tools.AddChild(radio);
        }

        uint[] palette =
        {
            0x000000FF, 0xFFFFFFFF, 0xFF0000FF, 0x00FF00FF, 0x0000FFFF, 0xFFFF00FF,
            0xFF00FFFF, 0x00FFFFFF, 0xFF8000FF, 0x800080FF, 0x804000FF, 0x808080FF,
        };
        var swatches = new List<ColorSwatch>();
        for (int i = 0; i < palette.Length; i++)
        {
            var swatch = new ColorSwatch("sw" + i, palette[i])
            {
                X = 160 + (i * 34), Y = 566, Width = 28, Height = 28,
                Selected = i == 0,
            };
            uint color = palette[i];
            swatch.Click += (_, _) =>
            {
                canvas.PaintColor = color;
                foreach (ColorSwatch other in swatches)
                    other.Selected = ReferenceEquals(other, swatch);
            };
            swatches.Add(swatch);
            form.AddChild(swatch);
        }

        float[] sizes = { 1, 3, 5, 8 };
        for (int i = 0; i < sizes.Length; i++)
        {
            var size = new RadioButton("size" + sizes[i])
            {
                X = 580 + (i * 48), Y = 566, Width = 42, Height = 28,
                Text = sizes[i].ToString("0"), Appearance = Appearance.Button,
            };
            float width = sizes[i];
            if (width == 3)
                size.CheckState = CheckState.Checked;
            size.Click += (_, _) => canvas.BrushWidth = width;
            form.AddChild(size);
        }

        canvas.BrushWidth = 3;
        form.AddChild(tools);
        form.AddChild(canvas);
        form.AddChild(status);
        // Last so open menus float above every sibling.
        form.AddChild(menu);
        return root;
    }
}
