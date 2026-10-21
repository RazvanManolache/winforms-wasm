using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsKeys = System.Windows.Forms.Keys;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;
using FormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;

// Desktop click/wheel/hover/modifier parity for the canvas host input path:
// Click pairs on release (never on press), DoubleClick replaces the second
// Click inside the SystemInformation window, wheel notches emit 120-unit
// steps, hover fires after a still rest, and modifiers reach Control statics.
public sealed class MouseParityTests
{
    private static FormsButton FindFirstButton(FormsControl root)
    {
        foreach (FormsControl c in root.Controls)
        {
            if (c is FormsButton btn)
                return btn;
            if (c.Controls.Count > 0)
            {
                try { return FindFirstButton(c); }
                catch (InvalidOperationException) { }
            }
        }

        throw new InvalidOperationException("No button found.");
    }

    private static (int X, int Y) ButtonCenter(FormsForm form, FormsButton button)
    {
        int absX = form.Location.X;
        int absY = form.Location.Y + 30;
        for (FormsControl? current = button; current is not null && current is not FormsForm; current = current.Parent)
        {
            absX += current.Left;
            absY += current.Top;
        }

        return (absX + (button.Width / 2), absY + (button.Height / 2));
    }

    private static WinFormsAppHost SmokeHost(out FormsForm form, out FormsButton button, out int cx, out int cy)
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        form = host.GetOrCreateForm(AppScene.Smoke)!;
        Assert.NotNull(form);
        button = FindFirstButton(form);
        (cx, cy) = ButtonCenter(form, button);
        return host;
    }

    [Fact]
    public void Click_Fires_On_Release_Not_On_Press()
    {
        using var host = SmokeHost(out _, out FormsButton button, out int cx, out int cy);
        int clicks = 0;
        FormsMouseEventArgs? mouseClick = null;
        button.Click += (_, _) => clicks++;
        button.MouseClick += (_, e) => mouseClick = e;

        host.OnMouseDown(cx, cy);
        Assert.Equal(0, clicks);
        Assert.Null(mouseClick);

        host.OnMouseUp(cx, cy);
        Assert.Equal(1, clicks);
        Assert.NotNull(mouseClick);
        Assert.Equal(FormsMouseButtons.Left, mouseClick.Button);
        Assert.Equal(1, mouseClick.Clicks);
    }

    [Fact]
    public void Click_Does_Not_Fire_After_Drag_Off()
    {
        using var host = SmokeHost(out _, out FormsButton button, out int cx, out int cy);
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        host.OnMouseDown(cx, cy);
        host.OnMouseMove(cx + 60, cy + 60);
        host.OnMouseUp(cx + 60, cy + 60);
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void DoubleClick_Replaces_Second_Click_Inside_Window()
    {
        using var host = SmokeHost(out _, out FormsButton button, out int cx, out int cy);
        long now = 1000;
        host.TickNow = () => now;
        int clicks = 0;
        int doubles = 0;
        FormsMouseEventArgs? mouseDouble = null;
        button.Click += (_, _) => clicks++;
        button.DoubleClick += (_, _) => doubles++;
        button.MouseDoubleClick += (_, e) => mouseDouble = e;

        host.OnMouseDown(cx, cy);
        host.OnMouseUp(cx, cy);
        Assert.Equal(1, clicks);

        now = 1000 + System.Windows.Forms.SystemInformation.DoubleClickTime / 2;
        host.OnMouseDown(cx, cy);
        host.OnMouseUp(cx, cy);
        Assert.Equal(1, clicks);
        Assert.Equal(1, doubles);
        Assert.NotNull(mouseDouble);
        Assert.Equal(2, mouseDouble.Clicks);
        Assert.Equal(FormsMouseButtons.Left, mouseDouble.Button);

        // Chain resets: a third quick pair is a fresh single Click.
        now += System.Windows.Forms.SystemInformation.DoubleClickTime / 2;
        host.OnMouseDown(cx, cy);
        host.OnMouseUp(cx, cy);
        Assert.Equal(2, clicks);
        Assert.Equal(1, doubles);
    }

    [Fact]
    public void DoubleClick_Expires_Outside_Window()
    {
        using var host = SmokeHost(out _, out FormsButton button, out int cx, out int cy);
        long now = 1000;
        host.TickNow = () => now;
        int clicks = 0;
        int doubles = 0;
        button.Click += (_, _) => clicks++;
        button.DoubleClick += (_, _) => doubles++;

        host.OnMouseDown(cx, cy);
        host.OnMouseUp(cx, cy);
        now = 1000 + System.Windows.Forms.SystemInformation.DoubleClickTime + 1;
        host.OnMouseDown(cx, cy);
        host.OnMouseUp(cx, cy);
        Assert.Equal(2, clicks);
        Assert.Equal(0, doubles);
    }

    [Fact]
    public void Right_Press_Release_Pairs_Click_As_Right()
    {
        using var host = SmokeHost(out _, out FormsButton button, out int cx, out int cy);
        int clicks = 0;
        FormsMouseEventArgs? mouseClick = null;
        button.Click += (_, _) => clicks++;
        button.MouseClick += (_, e) => mouseClick = e;

        host.OnMouseDown(cx, cy, FormsMouseButtons.Right);
        Assert.Equal(0, clicks);
        host.OnMouseUp(cx, cy, FormsMouseButtons.Right);
        Assert.Equal(1, clicks);
        Assert.NotNull(mouseClick);
        Assert.Equal(FormsMouseButtons.Right, mouseClick.Button);
    }

    [Fact]
    public void XButton_Press_Release_Pairs_Click_As_XButton1()
    {
        using var host = SmokeHost(out _, out FormsButton button, out int cx, out int cy);
        FormsMouseEventArgs? mouseClick = null;
        button.MouseClick += (_, e) => mouseClick = e;

        host.OnMouseDown(cx, cy, FormsMouseButtons.XButton1);
        host.OnMouseUp(cx, cy, FormsMouseButtons.XButton1);
        Assert.NotNull(mouseClick);
        Assert.Equal(FormsMouseButtons.XButton1, mouseClick.Button);
    }

    [Fact]
    public void Modifiers_Reach_Control_Statics_And_Clear()
    {
        using var host = SmokeHost(out _, out _, out int cx, out int cy);
        try
        {
            host.OnMouseDown(cx, cy, FormsMouseButtons.Left, shift: true, ctrl: true);
            Assert.Equal(FormsKeys.Shift | FormsKeys.Control, FormsControl.ModifierKeys);
            Assert.Equal(FormsKeys.Shift | FormsKeys.Control, System.Windows.Forms.WasmMouseState.Modifiers);

            host.OnMouseUp(cx, cy, FormsMouseButtons.Left);
            Assert.Equal(FormsKeys.None, FormsControl.ModifierKeys);
        }
        finally
        {
            System.Windows.Forms.WasmMouseState.Reset();
        }
    }

    [Fact]
    public void Wheel_Emits_120_Unit_Steps()
    {
        using var host = SmokeHost(out _, out FormsButton button, out int cx, out int cy);
        var deltas = new List<int>();
        button.MouseWheel += (_, e) => deltas.Add(e.Delta);

        host.OnMouseWheelRaw(cx, cy, 0, -100, 0);
        Assert.Equal(new[] { 120 }, deltas);

        host.OnMouseWheelRaw(cx, cy, 0, 150, 0);
        Assert.Equal(new[] { 120, -120 }, deltas);
    }

    [Fact]
    public void Wheel_Accumulates_Fractional_Deltas()
    {
        using var host = SmokeHost(out _, out FormsButton button, out int cx, out int cy);
        var deltas = new List<int>();
        button.MouseWheel += (_, e) => deltas.Add(e.Delta);

        host.OnMouseWheelRaw(cx, cy, 0, -30, 0);
        host.OnMouseWheelRaw(cx, cy, 0, -30, 0);
        Assert.Empty(deltas);
        host.OnMouseWheelRaw(cx, cy, 0, -50, 0);
        Assert.Equal(new[] { 120 }, deltas);
    }

    [Fact]
    public void Wheel_Line_Mode_Uses_Three_Lines_Per_Notch()
    {
        using var host = SmokeHost(out _, out FormsButton button, out int cx, out int cy);
        var deltas = new List<int>();
        button.MouseWheel += (_, e) => deltas.Add(e.Delta);

        host.OnMouseWheelRaw(cx, cy, 0, -3, 1);
        Assert.Equal(new[] { 120 }, deltas);
    }

    [Fact]
    public void Chords_Accumulate_And_Clear_Per_Button()
    {
        using var host = SmokeHost(out _, out _, out int cx, out int cy);
        try
        {
            host.OnMouseDown(cx, cy, FormsMouseButtons.Left);
            host.OnMouseDown(cx, cy, FormsMouseButtons.Right);
            Assert.Equal(FormsMouseButtons.Left | FormsMouseButtons.Right, System.Windows.Forms.WasmMouseState.Buttons);

            host.OnMouseUp(cx, cy, FormsMouseButtons.Left);
            Assert.Equal(FormsMouseButtons.Right, System.Windows.Forms.WasmMouseState.Buttons);

            host.OnMouseUp(cx, cy, FormsMouseButtons.Right);
            Assert.Equal(FormsMouseButtons.None, System.Windows.Forms.WasmMouseState.Buttons);
        }
        finally
        {
            System.Windows.Forms.WasmMouseState.Reset();
        }
    }

    [Fact]
    public void MapDomButton_Maps_All_Five_Buttons()
    {
        Assert.Equal(FormsMouseButtons.Left, WinFormsAppHost.MapDomButton(0));
        Assert.Equal(FormsMouseButtons.Middle, WinFormsAppHost.MapDomButton(1));
        Assert.Equal(FormsMouseButtons.Right, WinFormsAppHost.MapDomButton(2));
        Assert.Equal(FormsMouseButtons.XButton1, WinFormsAppHost.MapDomButton(3));
        Assert.Equal(FormsMouseButtons.XButton2, WinFormsAppHost.MapDomButton(4));
        Assert.Equal(FormsMouseButtons.Left, WinFormsAppHost.MapDomButton(99));
    }

    [Fact]
    public void Hover_Fires_Once_Per_Rest_After_HoverTime()
    {
        using var host = SmokeHost(out _, out FormsButton button, out int cx, out int cy);
        long now = 1000;
        host.TickNow = () => now;
        int hovers = 0;
        button.MouseHover += (_, _) => hovers++;

        host.OnMouseMove(cx, cy);
        host.CheckHover(now + System.Windows.Forms.SystemInformation.MouseHoverTime - 1);
        Assert.Equal(0, hovers);
        host.CheckHover(now + System.Windows.Forms.SystemInformation.MouseHoverTime);
        Assert.Equal(1, hovers);
        host.CheckHover(now + 5000);
        Assert.Equal(1, hovers);

        // Moving away and resting again re-arms.
        now += 5000;
        host.OnMouseMove(cx + 200, cy + 200);
        host.OnMouseMove(cx, cy);
        host.CheckHover(now + System.Windows.Forms.SystemInformation.MouseHoverTime);
        Assert.Equal(2, hovers);
    }
}
