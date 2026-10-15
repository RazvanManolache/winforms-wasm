using WfWasm.Renderer;
using WfWasm.Shim;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

/// <summary>Rule tests for the WinForms rendering port (edges, glyphs, states).</summary>
public class RenderingRulesTests
{
    private static bool Classic()
    {
        bool prev = RenderingConfig.UseVisualStyles;
        RenderingConfig.UseVisualStyles = false;
        return prev;
    }

    private static void Restore(bool prev) => RenderingConfig.UseVisualStyles = prev;

    [Fact]
    public void Edge_Raised_UsesLightOuterDarkInnerPairs()
    {
        var r = new RecordingRenderer();
        ControlPaintEdges.DrawEdge(r, 10, 10, 40, 30,
            ControlPaintEdges.EdgeStyle.Raised);
        var fills = r.Commands.OfType<FillRectCommand>().ToList();

        // Outer top-left is LightLight, outer bottom-right is DarkDark.
        Assert.Contains(fills, c => c.Y == 10 && c.Height == 1 && c.Rgba == SystemColors.ControlLightLight);
        Assert.Contains(fills, c => c.Y == 39 && c.Height == 1 && c.Rgba == SystemColors.ControlDarkDark);
        // Inner pair sits one pixel in: Light over Dark.
        Assert.Contains(fills, c => c.Y == 11 && c.Height == 1 && c.Rgba == SystemColors.ControlLight);
        Assert.Contains(fills, c => c.Y == 38 && c.Height == 1 && c.Rgba == SystemColors.ControlDark);
    }

    [Fact]
    public void Edge_Sunken_IsRaisedInverted()
    {
        var r = new RecordingRenderer();
        ControlPaintEdges.DrawEdge(r, 0, 0, 20, 20,
            ControlPaintEdges.EdgeStyle.Sunken);
        var fills = r.Commands.OfType<FillRectCommand>().ToList();

        Assert.Contains(fills, c => c.Y == 0 && c.Height == 1 && c.Rgba == SystemColors.ControlDarkDark);
        Assert.Contains(fills, c => c.Y == 19 && c.Height == 1 && c.Rgba == SystemColors.ControlLightLight);
    }

    [Fact]
    public void Edge_Etched_MixesSunkenOuterRaisedInner()
    {
        var r = new RecordingRenderer();
        ControlPaintEdges.DrawEdge(r, 0, 0, 20, 20,
            ControlPaintEdges.EdgeStyle.Etched);
        var fills = r.Commands.OfType<FillRectCommand>().ToList();

        Assert.Contains(fills, c => c.Y == 0 && c.Rgba == SystemColors.ControlDarkDark);
        Assert.Contains(fills, c => c.Y == 1 && c.Rgba == SystemColors.ControlLight);
    }

    [Fact]
    public void Edge_Sides_RestrictPaintedLines()
    {
        var r = new RecordingRenderer();
        ControlPaintEdges.DrawEdge(r, 0, 0, 20, 20,
            ControlPaintEdges.EdgeStyle.Raised, ControlPaintEdges.EdgeSides.Top);
        var fills = r.Commands.OfType<FillRectCommand>().ToList();

        // Only the two top lines (outer at 0, inner at 1) are painted.
        Assert.Equal(2, fills.Count);
        Assert.Contains(fills, c => c.Y == 0 && c.Width == 20);
        Assert.Contains(fills, c => c.Y == 1 && c.Width == 18);
        Assert.DoesNotContain(fills, c => c.Y == 19);
    }

    [Fact]
    public void Border_InsetOutset_FollowComplexRules()
    {
        var r = new RecordingRenderer();
        ControlPaintEdges.DrawBorder(r, 0, 0, 10, 10,
            SystemColors.Control, ControlPaintEdges.LineBorderStyle.Outset);
        var fills = r.Commands.OfType<FillRectCommand>().ToList();

        // Outset stock: outer TL LightLight, BR DarkDark; inner TL face, BR Dark.
        Assert.Contains(fills, c => c.Y == 0 && c.Rgba == SystemColors.ControlLightLight);
        Assert.Contains(fills, c => c.Y == 9 && c.Rgba == SystemColors.ControlDarkDark);
        Assert.Contains(fills, c => c.Y == 1 && c.Rgba == SystemColors.Control);
        Assert.Contains(fills, c => c.Y == 8 && c.Rgba == SystemColors.ControlDark);
    }

    [Fact]
    public void Border_Dotted_DrawsRunsNotLines()
    {
        var r = new RecordingRenderer();
        ControlPaintEdges.DrawBorder(r, 0, 0, 10, 6,
            SystemColors.Black, ControlPaintEdges.LineBorderStyle.Dotted);
        var fills = r.Commands.OfType<FillRectCommand>().ToList();

        Assert.True(fills.Count > 4);
        Assert.All(fills, c => Assert.True(c.Width <= 1 || c.Height <= 1));
    }

    [Fact]
    public void Checkbox_Cycles_TwoAndThreeState()
    {
        var box = new CheckBox("c");
        Assert.Equal(CheckState.Unchecked, box.CheckState);
        box.PerformClick();
        Assert.Equal(CheckState.Checked, box.CheckState);
        box.PerformClick();
        Assert.Equal(CheckState.Unchecked, box.CheckState);

        box.ThreeState = true;
        box.PerformClick();
        Assert.Equal(CheckState.Checked, box.CheckState);
        box.PerformClick();
        Assert.Equal(CheckState.Indeterminate, box.CheckState);
        box.PerformClick();
        Assert.Equal(CheckState.Unchecked, box.CheckState);
    }

    [Fact]
    public void Radio_ExclusiveWithinParent_NeverUnchecks()
    {
        var parent = new Control("p");
        var first = new RadioButton("r1") { CheckState = CheckState.Checked };
        var second = new RadioButton("r2");
        parent.AddChild(first);
        parent.AddChild(second);

        second.PerformClick();
        Assert.Equal(CheckState.Checked, second.CheckState);
        Assert.Equal(CheckState.Unchecked, first.CheckState);
        second.PerformClick();
        Assert.Equal(CheckState.Checked, second.CheckState);
    }

    [Fact]
    public void Checkbox_Classic_PaintsDarkBorderAndCheck()
    {
        bool prev = Classic();
        try
        {
            var box = new CheckBox("c")
            {
                X = 5, Y = 5, Width = 100, Height = 22, Text = "",
                CheckState = CheckState.Checked,
            };
            var r = new RecordingRenderer();
            box.Render(r);
            var fills = r.Commands.OfType<FillRectCommand>().ToList();

            // MiddleLeft glyph in a 22px box at y=5: glyph top is 5+11-6=10.
            Assert.Contains(fills, c => c.X == 5 && c.Y == 10 && c.Width == 13 && c.Height == 1
                && c.Rgba == SystemColors.ControlDarkDark);
            // Check ink inside the glyph.
            Assert.Contains(fills, c => c.Rgba == SystemColors.ControlText
                && c.X > 5 && c.X < 18 && c.Y > 10 && c.Y < 23);
        }
        finally
        {
            Restore(prev);
        }
    }

    [Fact]
    public void Mnemonic_ParsesSingleAmpersand()
    {
        Assert.Equal(("Save", 0), TextRules.ParseMnemonic("&Save"));
        Assert.Equal(("a&b", -1), TextRules.ParseMnemonic("a&&b"));
        Assert.Equal(("Exit", 1), TextRules.ParseMnemonic("E&xit"));
    }

    [Fact]
    public void Align_MapsNineWays()
    {
        Assert.Equal((TextHAlign.Left, TextVAlign.Top),
            TextRules.ToCanvasAlign(DrawingContentAlignment.TopLeft));
        Assert.Equal((TextHAlign.Center, TextVAlign.Middle),
            TextRules.ToCanvasAlign(DrawingContentAlignment.MiddleCenter));
        Assert.Equal((TextHAlign.Right, TextVAlign.Bottom),
            TextRules.ToCanvasAlign(DrawingContentAlignment.BottomRight));
    }

    [Fact]
    public void ScrollBar_MaximumRule_ReservesOnePage()
    {
        var bar = new ScrollBar("s") { Minimum = 0, Maximum = 100, LargeChange = 10 };
        Assert.Equal(91, bar.EffectiveMaximum);
        bar.Value = 500;
        Assert.Equal(91, bar.Value);
    }

    [Fact]
    public void ScrollBar_Click_Zones_PageAndStep()
    {
        var bar = new ScrollBar("s")
        {
            Width = 17, Height = 117, Vertical = true,
            Minimum = 0, Maximum = 100, LargeChange = 10, SmallChange = 1, Value = 50,
        };
        (int arrow, int track, int thumb, int len, _) = bar.ComputeLayout();
        Assert.Equal(17, arrow);

        bar.HandleClickLocal(8, 8);
        Assert.Equal(49, bar.Value);
        bar.HandleClickLocal(8, 116);
        Assert.Equal(50, bar.Value);
        // Track above thumb pages up by LargeChange.
        bar.HandleClickLocal(8, arrow + 1);
        Assert.Equal(40, bar.Value);
        _ = track;
        _ = thumb;
        _ = len;
    }

    [Fact]
    public void Form_Styles_FrameAndCaptionRules()
    {
        var dialog = new Form("d")
        {
            Width = 300, Height = 200, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false,
        };
        Assert.Equal((3, 3, 3, 3), dialog.FrameThickness);
        Assert.Equal(28, dialog.CaptionHeight);
        // Close only (no min/max).
        Assert.Single(dialog.CaptionButtons());
        Assert.Equal(CaptionButton.Close, dialog.CaptionButtons()[0].Button);

        var tool = new Form("t")
        {
            Width = 300, Height = 200, FormBorderStyle = FormBorderStyle.FixedToolWindow,
        };
        Assert.Equal(19, tool.CaptionHeight);

        var none = new Form("n")
        {
            Width = 300, Height = 200, FormBorderStyle = FormBorderStyle.None,
        };
        Assert.Equal(0, none.CaptionHeight);
        Assert.Empty(none.CaptionButtons());
    }

    [Fact]
    public void Form_CloseBox_HidesAndRaisesClosed()
    {
        var form = new Form("f") { Width = 400, Height = 300 };
        bool closed = false;
        form.Closed += (_, _) => closed = true;
        (CaptionButton _, int bx, int by, int size) = form.CaptionButtons()[0];
        form.HandleClickLocal(bx + form.FrameThickness.Left + 1, by + form.FrameThickness.Top + 1);
        Assert.False(form.Visible);
        Assert.True(closed);
        _ = size;
    }

    [Fact]
    public void Form_Maximize_RestoresBounds()
    {
        var form = new Form("f")
        {
            X = 10, Y = 10, Width = 400, Height = 300, MaximizeExtent = (1000, 750),
        };
        (CaptionButton _, int bx, int by, int size) = form.CaptionButtons()[1];
        form.HandleClickLocal(bx + 3 + 1, by + 3 + 1);
        Assert.Equal(FormWindowState.Maximized, form.WindowState);
        Assert.Equal(1000, form.Width);
        (CaptionButton _, int rx, int ry, int rs) = form.CaptionButtons()[1];
        form.HandleClickLocal(rx + 3 + 1, ry + 3 + 1);
        Assert.Equal(FormWindowState.Normal, form.WindowState);
        Assert.Equal(400, form.Width);
        _ = size;
        _ = rs;
    }

    [Fact]
    public void TextBox_Password_MasksDisplayText()
    {
        var box = new TextBox("t") { Text = "secret", UseSystemPasswordChar = true };
        Assert.Equal("●●●●●●", box.DisplayText);
        box.UseSystemPasswordChar = false;
        box.PasswordChar = '*';
        Assert.Equal("******", box.DisplayText);
    }

    [Fact]
    public void ListBox_Click_SelectsAndRaises()
    {
        var list = new ListBox("l") { Width = 150, Height = 60 };
        list.Items.AddRange(new[] { "a", "b", "c", "d" });
        bool raised = false;
        list.SelectedIndexChanged += (_, _) => raised = true;
        list.HandleClickLocal(10, 2 + (2 * list.ItemHeight));
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("c", list.SelectedItem);
        Assert.True(raised);
    }

    [Fact]
    public void ComboBox_Button_OpensAndSelects()
    {
        var combo = new ComboBox("c") { Width = 160, Height = 23 };
        combo.Items.AddRange(new[] { "Apple", "Banana" });
        combo.HandleClickLocal(150, 10);
        Assert.True(combo.DroppedDown);
        // First item row in the drop-down.
        combo.HandleClickLocal(20, 23 + 1 + (1 * combo.ItemHeight));
        Assert.False(combo.DroppedDown);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("Banana", combo.FieldText);
    }

    [Fact]
    public void TabControl_Click_SwitchesPage()
    {
        var tabs = new TabControl("t") { Width = 400, Height = 200 };
        tabs.AddTabPage(new TabPage("p1") { Text = "One" });
        tabs.AddTabPage(new TabPage("p2") { Text = "Two" });
        Assert.Equal(0, tabs.SelectedIndex);

        var bounds = tabs.TabBounds();
        tabs.HandleClickLocal(bounds[1].X + 2, 10);
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.True(tabs.TabPages[1].Visible);
        Assert.False(tabs.TabPages[0].Visible);
    }

    [Fact]
    public void MenuStrip_Click_OpensAndFiresItem()
    {
        var menu = new MenuStrip("m") { Width = 400, Height = 24 };
        var file = new ToolStripMenuItem("&File");
        var open = new ToolStripMenuItem("&Open");
        bool fired = false;
        open.Click += (_, _) => fired = true;
        file.DropDownItems.Add(open);
        menu.Items.Add(file);

        var top = menu.TopBounds();
        menu.HandleClickLocal(top[0].X + 2, 10);
        Assert.True(menu.IsOpen);
        (int X, int Y, int Width, int Height)? bounds = menu.DropDownBounds();
        Assert.NotNull(bounds);
        menu.HandleClickLocal(bounds.Value.X + 40, bounds.Value.Y + 2 + (0 * MenuStrip.ItemHeight) + 5);
        Assert.True(fired);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void Grid_HeaderClick_SortsAndGlyphs()
    {
        var grid = new DataGridView("g") { Width = 400, Height = 200 };
        grid.Columns.Add(new DataGridViewColumn("Name", 200));
        grid.Rows.Add(new List<string> { "b" });
        grid.Rows.Add(new List<string> { "a" });

        int hh = 23;
        // First click sorts ascending... starting direction toggles to descending
        // from neutral? No: neutral (0) -> descending (-1) first? Rule: >=0 -> -1.
        grid.HandleClickLocal(100, 10);
        Assert.Equal(-1, grid.Columns[0].SortDirection);
        Assert.Equal("b", grid.Rows[0][0]);
        grid.HandleClickLocal(100, 10);
        Assert.Equal(1, grid.Columns[0].SortDirection);
        Assert.Equal("a", grid.Rows[0][0]);
        _ = hh;
    }

    [Fact]
    public void Glyphs_PointTheRightWay()
    {
        static List<FillRectCommand> ArrowRuns(ScrollDirection direction)
        {
            var r = new RecordingRenderer();
            ControlPaintGlyphs.DrawScrollButton(r, 0, 0, 17, 17,
                direction, pressed: false, disabled: false, themed: false);
            // Skip the button face/border: keep runs inside the glyph box.
            return r.Commands.OfType<FillRectCommand>()
                .Where(c => ((c.Width < 17 && c.Height == 1) || (c.Height < 17 && c.Width == 1))
                    && c.X >= 3 && c.X + c.Width <= 14 && c.Y >= 3 && c.Y + c.Height <= 14)
                .ToList();
        }

        var up = ArrowRuns(ScrollDirection.Up);
        Assert.True(up.Min(c => c.Y) < up.Max(c => c.Y));
        Assert.True(up.Where(c => c.Y == up.Min(d => d.Y)).Min(c => c.Width)
            < up.Where(c => c.Y == up.Max(d => d.Y)).Max(c => c.Width));

        var down = ArrowRuns(ScrollDirection.Down);
        Assert.True(down.Where(c => c.Y == down.Min(d => d.Y)).Max(c => c.Width)
            > down.Where(c => c.Y == down.Max(d => d.Y)).Min(c => c.Width));

        var left = ArrowRuns(ScrollDirection.Left);
        Assert.True(left.Where(c => c.X == left.Min(d => d.X)).Min(c => c.Height)
            < left.Where(c => c.X == left.Max(d => d.X)).Max(c => c.Height));

        var right = ArrowRuns(ScrollDirection.Right);
        Assert.True(right.Where(c => c.X == right.Min(d => d.X)).Max(c => c.Height)
            > right.Where(c => c.X == right.Max(d => d.X)).Min(c => c.Height));
    }

    [Fact]
    public void App_PaintMenu_OpensAndFiresItem()
    {
        static T Find<T>(Control node) where T : Control
        {
            if (node is T match)
                return match;
            foreach (Control child in node.Children)
            {
                try
                {
                    return Find<T>(child);
                }
                catch (InvalidOperationException)
                {
                }
            }

            throw new InvalidOperationException($"No {typeof(T).Name} in scene.");
        }

        // Paint form at (20,20) + (1,29) chrome; File item then its drop item.
        var app = new CanvasApp();
        app.KeyChar('3');
        MenuStrip menu = Find<MenuStrip>(app.CurrentSceneRoot);
        app.ClickAt(50, 61);
        Assert.True(menu.IsOpen);

        ToolStripMenuItem? picked = null;
        menu.ItemClicked += (_, e) => picked = e.ClickedItem as ToolStripMenuItem;
        app.ClickAt(80, 84);
        Assert.False(menu.IsOpen);
        Assert.Equal("&Clear canvas", picked?.Text);
    }

    [Fact]
    public void Grid_CellClick_SelectsRow()
    {
        var grid = new DataGridView("g") { Width = 400, Height = 200 };
        grid.Columns.Add(new DataGridViewColumn("Name", 200));
        grid.Rows.Add(new List<string> { "a" });
        grid.Rows.Add(new List<string> { "b" });

        grid.HandleClickLocal(41 + 10, 23 + 22 + 5);
        Assert.Equal(1, grid.SelectedRow);
        Assert.Equal(1, grid.CurrentRow);
        Assert.Equal(0, grid.CurrentColumn);
    }
}
