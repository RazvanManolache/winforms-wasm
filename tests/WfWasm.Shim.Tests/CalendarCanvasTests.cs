using WfWasm.Renderer;
using WfWasm.Shim;

public class ChromeTests
{
    [Fact]
    public void Form_PaintsTitleBarBorderAndOffsetsChildren()
    {
        var form = new Form("form") { X = 10, Y = 20, Width = 800, Height = 450, Text = "Form1" };
        var label = new Label("label1") { X = 180, Y = 169, Width = 200, Height = 40, Text = "Hello .NET" };
        form.AddChild(label);

        var renderer = new RecordingRenderer();
        form.Render(renderer);

        var commands = renderer.Commands;
        // Sizable frame (3px) + themed white caption with icon + client area.
        Assert.Contains(commands.OfType<FillRectCommand>(),
            c => c.X == 13 && c.Y == 23 && c.Width == 794 && c.Height == 28
                && c.Rgba == SystemColors.ThemedCaptionActive);
        Assert.Contains(commands.OfType<DrawTextCommand>(),
            c => c.Text == "Form1" && c.X == 37 && c.Y == 37 && c.VAlign == TextVAlign.Middle);
        Assert.Contains(commands.OfType<FillRectCommand>(),
            c => c.X == 13 && c.Y == 51 && c.Width == 794 && c.Height == 416 && c.Rgba == 0xFFFFFFFF);
        Assert.Contains(commands.OfType<DrawTextCommand>(),
            c => c.Text == "Hello .NET" && c.X == 193 && c.Y == 220 && c.VAlign == TextVAlign.Top);
    }

    [Fact]
    public void Form_HitTest_RoutesToClientChildren()
    {
        var form = new Form { X = 0, Y = 0, Width = 400, Height = 300, Text = "T" };
        var button = new Button("ok") { X = 10, Y = 10, Width = 90, Height = 26, Text = "OK" };
        form.AddChild(button);

        Assert.Same(button, form.HitTest(15, 45));
        Assert.Same(form, form.HitTest(200, 10));
        Assert.Null(form.HitTest(500, 500));
    }

    [Fact]
    public void TextBox_PaintsBorderAndText()
    {
        var box = new TextBox("txt") { X = 5, Y = 5, Width = 120, Height = 24, Text = "2026" };

        var renderer = new RecordingRenderer();
        box.Render(renderer);

        // Themed Fixed3D: 1px visual-style border lines, middle-anchored text.
        Assert.Contains(renderer.Commands.OfType<FillRectCommand>(),
            c => c.X == 5 && c.Y == 5 && c.Width == 120 && c.Height == 1 && c.Rgba == 0xABADB3FF);
        Assert.Contains(renderer.Commands.OfType<FillRectCommand>(),
            c => c.X == 5 && c.Y == 5 && c.Width == 1 && c.Height == 24 && c.Rgba == 0xABADB3FF);
        Assert.Contains(renderer.Commands.OfType<DrawTextCommand>(),
            c => c.Text == "2026" && c.X == 9 && c.Y == 17
                && c.HAlign == TextHAlign.Left && c.VAlign == TextVAlign.Middle);
    }
}

public class MonthGridTests
{
    private static MonthCalendar October2026()
    {
        var grid = new MonthCalendar("grid");
        grid.GoTo("10", "2026");
        grid.Today = new DateTime(2026, 10, 5);
        return grid;
    }

    [Fact]
    public void Paint_October2026_HasFourBlanksThenDayOne()
    {
        var grid = October2026();

        var renderer = new RecordingRenderer();
        grid.Render(renderer);

        var texts = renderer.Commands.OfType<DrawTextCommand>().ToList();
        Assert.Contains(texts, c => c.Text == "Su" && c.Rgba == 0xFF8080FF);
        Assert.Contains(texts, c => c.Text == "Sa" && c.Rgba == 0x1E90FFFF);
        Assert.Contains(texts, c => c.Text == "1" && c.X == 408 && c.Y == 46);
        Assert.Contains(renderer.Commands.OfType<FillRoundedRectCommand>(),
            c => c.Width == 100 && c.Height == 80 && c.Radius == 15);
    }

    [Fact]
    public void Paint_WeekendTodayAndEventStyling()
    {
        var grid = October2026();
        grid.Events.Set(new DateTime(2026, 10, 6), "Dentist");

        var renderer = new RecordingRenderer();
        grid.Render(renderer);

        var rects = renderer.Commands.OfType<FillRoundedRectCommand>().ToList();
        Assert.Contains(rects, c => c.Rgba == 0xFFD2DCFF);
        Assert.Contains(rects, c => c.Rgba == 0xC8E1FFFF);
        Assert.Contains(rects, c => c.Rgba == 0xD3D3D3FF);
        var texts = renderer.Commands.OfType<DrawTextCommand>().ToList();
        Assert.Contains(texts, c => c.Text == "6" && c.Rgba == 0x006400FF);
        Assert.Contains(texts, c => c.Text == "5" && c.FontCss.StartsWith("bold"));
    }

    [Fact]
    public void DateAt_MapsPointsAndRejectsBlanks()
    {
        var grid = October2026();

        Assert.Equal(new DateTime(2026, 10, 1), grid.DateAt(450, 60));
        Assert.Equal(new DateTime(2026, 10, 31), grid.DateAt(650, 400));
        Assert.Null(grid.DateAt(50, 60));
        Assert.Null(grid.DateAt(-5, 60));
        Assert.Null(grid.DateAt(50, 600));
    }

    [Fact]
    public void ClickDayAt_RaisesDayClicked()
    {
        var grid = October2026();
        DateTime? clicked = null;
        grid.DayClicked += (_, d) => clicked = d;

        Assert.True(grid.ClickDayAt(450, 60));
        Assert.Equal(new DateTime(2026, 10, 1), clicked);
        Assert.False(grid.ClickDayAt(50, 60));
    }

    [Fact]
    public void Navigation_WrapsYears()
    {
        var grid = new MonthCalendar();
        grid.GoTo("12", "2025");
        grid.MoveNext();
        Assert.Equal((1, 2026), (grid.Month, grid.Year));
        grid.MovePrevious();
        grid.MovePrevious();
        Assert.Equal((11, 2025), (grid.Month, grid.Year));
    }

    [Fact]
    public void GoTo_AcceptsNamesNumbersAndRejectsInvalid()
    {
        var grid = new MonthCalendar();

        Assert.True(grid.GoTo("February", "2024"));
        Assert.Equal((2, 2024), (grid.Month, grid.Year));

        var renderer = new RecordingRenderer();
        grid.Render(renderer);
        Assert.Contains(renderer.Commands.OfType<DrawTextCommand>(), c => c.Text == "29");

        Assert.True(grid.GoTo("13", "2024"));
        Assert.Equal(12, grid.Month);
        Assert.False(grid.GoTo("Smarch", "2024"));
        Assert.False(grid.GoTo("2", "next year"));
    }

    [Fact]
    public void Events_JsonRoundTrip()
    {
        var events = new CalendarEvents();
        events.Set(new DateTime(2026, 10, 6), "Dentist");
        events.Set(new DateTime(2026, 10, 6, 15, 30, 0), "Overwritten");

        string json = events.ToJson();
        var reloaded = new CalendarEvents();
        reloaded.LoadJson(json);

        Assert.Equal(1, reloaded.Count);
        Assert.True(reloaded.TryGet(new DateTime(2026, 10, 6), out string? text));
        Assert.Equal("Overwritten", text);
        Assert.True(reloaded.Remove(new DateTime(2026, 10, 6)));
        Assert.Equal(0, reloaded.Count);
    }
}
