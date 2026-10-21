using FormsSystemInformation = System.Windows.Forms.SystemInformation;

namespace WfWasm.WinForms.Tests;

// The browser has no system-parameters store: scalar queries must answer
// desktop defaults. Hover time feeds ToolStrip's MouseHoverTimer, and a
// 0 answer throws in Timer.set_Interval on the first toolbar mouse-move.
public sealed class SystemParameterTests
{
    [Fact]
    public void MouseHoverTime_Answers_Desktop_Default()
    {
        Assert.Equal(400, FormsSystemInformation.MouseHoverTime);
    }

    [Fact]
    public void MouseHoverSize_Answers_Desktop_Default()
    {
        // (Compared by component: System.Drawing.Size exists in both the
        // framework and WfWasm.Drawing assemblies.)
        var size = FormsSystemInformation.MouseHoverSize;
        Assert.Equal(4, size.Width);
        Assert.Equal(4, size.Height);
    }

    [Fact]
    public void MenuFont_Answers_Segoe_UI_9pt()
    {
        // Zeroed NONCLIENTMETRICS made this come back with an empty name
        // at 0.75px, which the Syncfusion bars measured every menu against
        // (masked by a host font-heal). Desktop Win11 default is Segoe UI.
        var font = FormsSystemInformation.MenuFont;
        Assert.Equal("Segoe UI", font.Name);
        Assert.Equal(9f, font.SizeInPoints, precision: 2);
    }

    [Fact]
    public void MouseWheelScrollLines_Answers_Desktop_Default()
    {
        // Zero here silently disables every wheel-scroll consumer (host
        // ListView scroll multiplies its line delta by this).
        Assert.Equal(3, FormsSystemInformation.MouseWheelScrollLines);
    }

    [Fact]
    public void WorkingArea_Answers_Primary_Monitor_Work_Rect()
    {
        // Syncfusion MenuGridHost clamps popups against this: empty parked
        // the File menu at Y=0. Must match GetMonitorInfo.rcWork and
        // SM_CXFULLSCREEN/SM_CYFULLSCREEN (1920x1040).
        var area = FormsSystemInformation.WorkingArea;
        Assert.Equal(0, area.X);
        Assert.Equal(0, area.Y);
        Assert.Equal(1920, area.Width);
        Assert.Equal(1040, area.Height);
    }
}
