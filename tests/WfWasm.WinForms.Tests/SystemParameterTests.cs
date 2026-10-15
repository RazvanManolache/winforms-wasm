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
}
