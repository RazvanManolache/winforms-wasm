using WfWasm.Renderer;
using WfWasm.Shim;

public class ControlTreeTests
{
    [Fact]
    public void Defaults_AreEnabledVisibleAndEmpty()
    {
        var control = new Control("form");
        Assert.Equal("form", control.Name);
        Assert.Equal("", control.Text);
        Assert.True(control.Enabled);
        Assert.True(control.Visible);
        Assert.Empty(control.Children);
        Assert.Equal("", new Control().Name);
    }

    [Fact]
    public void Paint_NestedOffsetsAccumulateThroughTree()
    {
        var root = new Control("form") { X = 10, Y = 20, Width = 800, Height = 450 };
        var panel = new Control("panel") { X = 5, Y = 5, Width = 200, Height = 100 };
        var button = new Button("ok") { X = 3, Y = 4, Width = 90, Height = 26, Text = "OK" };
        panel.AddChild(button);
        root.AddChild(panel);

        var renderer = new RecordingRenderer();
        root.Render(renderer);

        // Themed System button: face fill, 1px border, centered text.
        Assert.Contains(renderer.Commands.OfType<FillRectCommand>(),
            c => c.X == 18 && c.Y == 29 && c.Width == 90 && c.Height == 26
                && c.Rgba == SystemColors.ThemedButtonFill);
        Assert.Contains(renderer.Commands.OfType<FillRectCommand>(),
            c => c.X == 18 && c.Y == 29 && c.Width == 90 && c.Height == 1
                && c.Rgba == SystemColors.ThemedButtonBorder);

        var text = Assert.IsType<DrawTextCommand>(renderer.Commands.OfType<DrawTextCommand>().Single());
        Assert.Equal("OK", text.Text);
        Assert.Equal(TextHAlign.Center, text.HAlign);
        Assert.Equal(TextVAlign.Middle, text.VAlign);
        // Center alignment records the center anchor: 22 + 82 / 2.
        Assert.Equal(63f, text.X);
    }

    [Fact]
    public void Paint_LabelDrawsTextAtAccumulatedOffset()
    {
        var root = new Control("form") { X = 180, Y = 169, Width = 800, Height = 450 };
        root.AddChild(new Label("label1") { X = 0, Y = 0, Text = "Hello .NET" });

        var renderer = new RecordingRenderer();
        root.Render(renderer);

        var text = Assert.IsType<DrawTextCommand>(renderer.Commands.OfType<DrawTextCommand>().Single());
        Assert.Equal("Hello .NET", text.Text);
        Assert.Equal(180f, text.X);
        Assert.Equal(169f, text.Y);
    }

    [Fact]
    public void Paint_InvisibleSubtreeEmitsNothing()
    {
        var root = new Control("form") { Width = 800, Height = 450 };
        var hidden = new Control("hidden") { X = 1, Y = 1, Width = 50, Height = 50, Visible = false };
        hidden.AddChild(new Button("buried") { X = 2, Y = 2, Width = 10, Height = 10, Text = "X" });
        root.AddChild(hidden);

        var renderer = new RecordingRenderer();
        root.Render(renderer);

        Assert.Empty(renderer.Commands);
    }

    [Fact]
    public void Paint_ButtonHighlightFollowsDefaultAndEnabled()
    {
        var plain = new Button("a") { Width = 90, Height = 26, Text = "A" };
        var starred = new Button("b") { Width = 90, Height = 26, Text = "B", IsDefault = true };
        var starredOff = new Button("c") { Width = 90, Height = 26, Text = "C", IsDefault = true, Enabled = false };

        (uint face, uint border) PaintOf(Button button)
        {
            var renderer = new RecordingRenderer();
            button.Render(renderer);
            var fills = renderer.Commands.OfType<FillRectCommand>().ToList();
            return (fills[0].Rgba, fills[1].Rgba);
        }

        // Themed faces share the fill; the border carries default/disabled.
        Assert.Equal((SystemColors.ThemedButtonFill, SystemColors.ThemedButtonBorder), PaintOf(plain));
        Assert.Equal((SystemColors.ThemedButtonFill, SystemColors.ThemedButtonHotBorder), PaintOf(starred));
        Assert.Equal((SystemColors.ThemedButtonDisabledFill, SystemColors.ThemedButtonDisabledBorder), PaintOf(starredOff));
    }

    [Fact]
    public void HitTest_ReturnsDeepestChildElseSelfOrNull()
    {
        var root = new Control("form") { Width = 800, Height = 450 };
        var panel = new Control("panel") { X = 10, Y = 10, Width = 200, Height = 200 };
        var button = new Button("ok") { X = 20, Y = 20, Width = 90, Height = 26 };
        panel.AddChild(button);
        root.AddChild(panel);

        Assert.Same(button, root.HitTest(35, 35));
        Assert.Same(panel, root.HitTest(15, 150));
        Assert.Same(root, root.HitTest(700, 400));
        Assert.Null(root.HitTest(801, 10));
        Assert.Null(root.HitTest(-1, 0));
    }

    [Fact]
    public void HitTest_LastChildIsOnTop()
    {
        var root = new Control("form") { Width = 800, Height = 450 };
        var first = new Button("first") { X = 0, Y = 0, Width = 100, Height = 100 };
        var second = new Button("second") { X = 0, Y = 0, Width = 100, Height = 100 };
        root.AddChild(first);
        root.AddChild(second);

        Assert.Same(second, root.HitTest(50, 50));
    }

    [Fact]
    public void HitTest_SkipsDisabledAndInvisibleChildren()
    {
        var root = new Control("form") { Width = 800, Height = 450 };
        var disabled = new Button("off") { X = 0, Y = 0, Width = 100, Height = 100, Enabled = false };
        var hidden = new Button("hid") { X = 200, Y = 0, Width = 100, Height = 100, Visible = false };
        root.AddChild(disabled);
        root.AddChild(hidden);

        Assert.Same(root, root.HitTest(50, 50));
        Assert.Same(root, root.HitTest(250, 50));
        Assert.Null(hidden.HitTest(210, 10));
    }

    [Fact]
    public void PerformClick_RaisesOnlyWhenEnabledAndVisible()
    {
        var live = new Button("live");
        int liveCount = 0;
        live.Click += (_, _) => liveCount++;
        live.PerformClick();
        Assert.Equal(1, liveCount);

        var disabled = new Button("off") { Enabled = false };
        int disabledCount = 0;
        disabled.Click += (_, _) => disabledCount++;
        disabled.PerformClick();
        Assert.Equal(0, disabledCount);

        var hidden = new Button("hid") { Visible = false };
        int hiddenCount = 0;
        hidden.Click += (_, _) => hiddenCount++;
        hidden.PerformClick();
        Assert.Equal(0, hiddenCount);
    }

    [Fact]
    public void AddRemoveChild_ManagesMembership()
    {
        var root = new Control("form");
        var child = new Button("ok");
        root.AddChild(child);
        Assert.Same(child, Assert.Single(root.Children));
        Assert.True(root.RemoveChild(child));
        Assert.Empty(root.Children);
        Assert.False(root.RemoveChild(child));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var root = new Control("form") { Width = 10, Height = 10 };
        Assert.Throws<ArgumentNullException>(() => root.Render(null!));
        Assert.Throws<ArgumentNullException>(() => root.AddChild(null!));
        Assert.Throws<ArgumentNullException>(() => root.RemoveChild(null!));
        Assert.Throws<ArgumentNullException>(() => new Control(null!));
        Assert.Throws<ArgumentNullException>(() => root.Name = null!);
        Assert.Throws<ArgumentNullException>(() => root.Text = null!);
        Assert.Throws<ArgumentNullException>(() => new Label("l") { FontCss = null! });
    }
}
