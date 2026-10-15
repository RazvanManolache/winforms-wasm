extern alias WfDraw;

using WfWasm.Shim;

/// <summary>
/// Covers the Paint.NET boot-gap surface: handle tokens, dialogs, text
/// measuring, control battery, collections and classic painters. Each test
/// pins behavior the transitive queue requires, not just presence.
/// </summary>
public sealed class BootGapTests
{
    [Fact]
    public void NativeWindow_Issues_Unique_Handles()
    {
        var a = new NativeWindow();
        var b = new NativeWindow();
        a.CreateHandle(new CreateParams());
        b.CreateHandle(new CreateParams());
        Assert.NotEqual(IntPtr.Zero, a.Handle);
        Assert.NotEqual(a.Handle, b.Handle);
        Assert.Null(NativeWindow.FromHandle(a.Handle));
        a.DestroyHandle();
        Assert.Equal(IntPtr.Zero, a.Handle);
    }

    [Fact]
    public void TextRenderer_Measures_Text()
    {
        using var font = new DrawingFont("Microsoft Sans Serif", 8.25f);
        WfDraw::System.Drawing.Size size = TextRenderer.MeasureText("Hello", font);
        Assert.True(size.Width > 0 && size.Height > 0);
        Assert.Equal(WfDraw::System.Drawing.Size.Empty, TextRenderer.MeasureText("", font));
    }

    [Fact]
    public void FileDialogs_Dismiss_Without_Picker()
    {
        using var open = new OpenFileDialog();
        Assert.True(open.CheckFileExists);
        Assert.Equal(DialogResult.Cancel, open.ShowDialog());
        Assert.Equal(string.Empty, open.FileName);
        Assert.Empty(open.FileNames);
        open.CustomPlaces.Add("C:\\Temp");
        open.CustomPlaces.Add(Guid.NewGuid());
        Assert.Equal(2, open.CustomPlaces.Count);
        open.Reset();
        Assert.Empty(open.CustomPlaces);
        Assert.Equal(1, open.FilterIndex);

        using var save = new SaveFileDialog();
        Assert.True(save.OverwritePrompt);
        Assert.Equal(DialogResult.Cancel, save.ShowDialog());

        using var color = new ColorDialog();
        Assert.True(color.AllowFullOpen);
        Assert.Equal(DialogResult.Cancel, color.ShowDialog());
    }

    [Fact]
    public void FileDialog_OpenFile_Reads_Real_File()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "data");
            using var open = new OpenFileDialog { FileName = path };
            using (Stream stream = open.OpenFile())
                Assert.Equal("data", new StreamReader(stream).ReadToEnd());
            Assert.Equal(Path.GetFileName(path), open.SafeFileName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Control_IsMnemonic_Handles_Escapes()
    {
        Assert.True(Control.IsMnemonic('f', "&File"));
        Assert.True(Control.IsMnemonic('F', "&File"));
        Assert.False(Control.IsMnemonic('x', "&File"));
        Assert.False(Control.IsMnemonic('&', "Save && Close"));
        Assert.False(Control.IsMnemonic('f', ""));
    }

    [Fact]
    public void Buttons_Share_ButtonBase_FlatStyle()
    {
        var button = new Button();
        var check = new CheckBox();
        Assert.IsAssignableFrom<ButtonBase>(button);
        Assert.IsAssignableFrom<ButtonBase>(check);
        button.FlatStyle = FlatStyle.Flat;
        Assert.Equal(FlatStyle.Flat, ((ButtonBase)button).FlatStyle);
        Assert.False(check.UseCompatibleTextRendering);
    }

    [Fact]
    public void TabControl_Pages_And_Rect()
    {
        var tabs = new TabControl();
        Assert.Equal(TabAlignment.Top, tabs.Alignment);
        Assert.Equal(0, tabs.TabCount);
        Assert.Equal(WfDraw::System.Drawing.Rectangle.Empty, tabs.GetTabRect(0));
        tabs.TabPages.Add("First");
        tabs.TabPages.Add("key", "Second");
        Assert.Equal(2, tabs.TabCount);
        Assert.True(tabs.TabPages.ContainsKey("key"));
        Assert.Equal(1, tabs.TabPages.IndexOfKey("key"));
        Assert.NotEqual(WfDraw::System.Drawing.Rectangle.Empty, tabs.GetTabRect(0));
    }

    [Fact]
    public void ListBox_SelectedIndices_Track_Selection()
    {
        var list = new ListBox { Width = 100 };
        list.Items.Add("a");
        list.Items.Add("b");
        Assert.Empty(list.SelectedIndices);
        list.SelectedIndex = 1;
        Assert.Equal(new[] { 1 }, list.SelectedIndices.ToArray());
        Assert.Contains(1, list.SelectedIndices);
        Assert.Equal(new WfDraw::System.Drawing.Rectangle(2, 2 + list.ItemHeight, list.Width - 4, list.ItemHeight), list.GetItemRectangle(1));
        Assert.Equal(1, list.IndexFromPoint(4, 2 + list.ItemHeight + 1));
        Assert.Equal(-1, list.IndexFromPoint(4, 100000));
    }

    [Fact]
    public void Padding_Empty_And_Addition()
    {
        Assert.Equal(new Padding(0, 0, 0, 0), Padding.Empty);
        Assert.Equal(new Padding(3, 5, 7, 9), new Padding(1, 2, 3, 4) + new Padding(2, 3, 4, 5));
    }

    [Fact]
    public void DrawItemEventArgs_Carries_Colors()
    {
        using var bitmap = new WfDraw::System.Drawing.Bitmap(8, 8);
        using var graphics = WfDraw::System.Drawing.Graphics.FromImage(bitmap);
        using var font = new DrawingFont("Microsoft Sans Serif", 8.25f);
        var args = new DrawItemEventArgs(graphics, font, new DrawingRectangle(0, 0, 8, 8), 0, DrawItemState.Selected, Color.Red, Color.Blue);
        Assert.Equal(Color.Red, args.ForeColor);
        Assert.Equal(Color.Blue, args.BackColor);
        args.DrawBackground();
        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        args.DrawFocusRectangle();
    }

    [Fact]
    public void ControlPaint_Painters_Mark_Bitmap()
    {
        using var bitmap = new WfDraw::System.Drawing.Bitmap(24, 24);
        using var graphics = WfDraw::System.Drawing.Graphics.FromImage(bitmap);
        var rect = new DrawingRectangle(2, 2, 20, 20);
        ControlPaint.DrawBorder3D(graphics, rect, Border3DStyle.Raised);
        Assert.NotEqual(0, bitmap.GetPixel(2, 2).ToArgb());
        ControlPaint.DrawCheckBox(graphics, rect, ButtonState.Checked);
        ControlPaint.DrawRadioButton(graphics, rect, ButtonState.Checked);
        ControlPaint.DrawFocusRectangle(graphics, rect);
        ControlPaint.DrawScrollButton(graphics, rect, ScrollButton.Down, ButtonState.Normal);
        ControlPaint.DrawBorder3D(graphics, 0, 0, 24, 24, Border3DStyle.Sunken, Border3DSide.All);

        using var group = new WfDraw::System.Drawing.Bitmap(24, 24);
        using var gg = WfDraw::System.Drawing.Graphics.FromImage(group);
        GroupBoxRenderer.DrawGroupBox(gg, rect, GroupBoxState.Normal);
        GroupBoxRenderer.DrawGroupBox(gg, rect, "T", null, GroupBoxState.Disabled);
    }

    [Fact]
    public void Small_Event_Types_Hold_Data()
    {
        var control = new Control("c");
        var cea = new ControlEventArgs(control);
        Assert.Same(control, cea.Control);
        Assert.Equal(new LinkArea(1, 2), new LinkArea(1, 2));
        Assert.True(new LinkArea(0, 0).IsEmpty);
        var up = new UpDownEventArgs(3);
        Assert.Equal(3, up.ButtonID);
        var accel = new NumericUpDownAcceleration(2, 5m);
        Assert.Equal(2, accel.Seconds);
        Assert.Equal(5m, accel.Increment);
        var accels = new NumericUpDownAccelerationCollection { accel };
        Assert.Single(accels);
        Assert.False(accels.IsReadOnly);
        var handled = new HandledMouseEventArgs(MouseButtons.Left, 1, 2, 3, 0, true);
        Assert.True(handled.Handled);
    }

    [Fact]
    public void Container_ActiveControl_And_TextBox_Metrics()
    {
        var container = new ContainerControl();
        var child = new Control("kid");
        container.ActiveControl = child;
        Assert.Same(child, container.ActiveControl);

        var text = new TextBox();
        Assert.Equal(BorderStyle.Fixed3D, text.BorderStyle);
        Assert.True(text.PreferredHeight > 0);
        ((TextBoxBase)text).BorderStyle = BorderStyle.FixedSingle;
        Assert.Equal(BorderStyle.FixedSingle, text.BorderStyle);
    }

    [Fact]
    public void Theme_Surface_Reports_No_Engine()
    {
        Assert.False(VisualStyleInformation.IsSupportedByOS);
        Assert.False(VisualStyleInformation.IsEnabledByUser);
        Assert.False(VisualStyleRenderer.IsSupported);
        Assert.False(VisualStyleRenderer.IsElementDefined(VisualStyleElement.Button.PushButton.Normal));
        var renderer = new VisualStyleRenderer("BUTTON", 1, 1);
        Assert.Equal(Color.Black, renderer.GetColor(ColorProperty.TextColor));
        Assert.Equal(WfDraw::System.Drawing.Point.Empty, renderer.GetPoint(PointProperty.Offset));
        Assert.Equal(ThemeSizeType.MinimumSize, (ThemeSizeType)0);
        var table = new ProfessionalColorTable();
        Assert.True(table.UseSystemColors);
    }

    [Fact]
    public void Cursor_And_SystemFacts()
    {
        var cursor = new Cursor(new IntPtr(42)) { Tag = "x" };
        Assert.Equal("x", cursor.Tag);
        Assert.Equal(new IntPtr(42), cursor.CopyHandle());
        Assert.Equal(new WfDraw::System.Drawing.Size(2, 2), SystemInformation.Border3DSize);
        Assert.Equal(500, SystemInformation.DoubleClickTime);
    }
}
