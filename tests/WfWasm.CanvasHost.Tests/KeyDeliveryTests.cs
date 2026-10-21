using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsKeys = System.Windows.Forms.Keys;
using FormsTextBox = System.Windows.Forms.TextBox;

namespace WfWasm.CanvasHost.Tests;

public sealed class KeyDeliveryTests
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

    private static (int X, int Y) CenterOn(FormsForm form, FormsControl control)
    {
        int absX = form.Location.X;
        int absY = form.Location.Y + 30;
        for (FormsControl? current = control; current is not null && current is not FormsForm; current = current.Parent)
        {
            absX += current.Left;
            absY += current.Top;
        }

        return (absX + (control.Width / 2), absY + (control.Height / 2));
    }

    private static FormsButton ClickSmokeButton(WinFormsAppHost host)
    {
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        FormsButton button = FindFirstButton(form);
        var (x, y) = CenterOn(form, button);
        host.OnMouseDown(x, y);
        // Subscribe on whichever control the host actually focused: the
        // smoke form stacks two same-text buttons, so the found instance
        // is not necessarily the hit one.
        FieldInfo? field = typeof(WinFormsAppHost).GetField("_focusedControl", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var focused = (FormsControl?)field.GetValue(host);
        Assert.NotNull(focused);
        return Assert.IsType<FormsButton>(focused);
    }

    [Fact]
    public void KeyDown_Reaches_Clicked_Button_With_KeyCode()
    {
        // FIX2 core: keys previously returned early unless a TextBox was
        // focused; a clicked button must now receive KeyDown.
        using var host = new WinFormsAppHost();
        FormsButton button = ClickSmokeButton(host);
        FormsKeys? code = null;
        button.KeyDown += (_, e) => code = e.KeyCode;
        host.OnKeyDown("ArrowRight");
        Assert.Equal(FormsKeys.Right, code);
    }

    [Fact]
    public void KeyDown_Modifiers_In_KeyData()
    {
        using var host = new WinFormsAppHost();
        FormsButton button = ClickSmokeButton(host);
        FormsKeys? data = null;
        button.KeyDown += (_, e) => data = e.KeyData;
        host.OnKeyDown("ArrowDown", shift: true, ctrl: true);
        Assert.NotNull(data);
        Assert.Equal(FormsKeys.Down, data.Value & FormsKeys.KeyCode);
        Assert.True((data.Value & FormsKeys.Shift) != 0);
        Assert.True((data.Value & FormsKeys.Control) != 0);
    }

    [Fact]
    public void KeyUp_Raises_With_Same_Key()
    {
        using var host = new WinFormsAppHost();
        FormsButton button = ClickSmokeButton(host);
        FormsKeys? down = null;
        FormsKeys? up = null;
        button.KeyDown += (_, e) => down = e.KeyCode;
        button.KeyUp += (_, e) => up = e.KeyCode;
        host.OnKeyDown("F5");
        host.OnKeyUp("F5");
        Assert.Equal(FormsKeys.F5, down);
        Assert.Equal(FormsKeys.F5, up);
    }

    [Fact]
    public void Escape_Raises_On_Button_Without_TextBox()
    {
        // Escape duties used to be trapped behind the TextBox gate.
        using var host = new WinFormsAppHost();
        FormsButton button = ClickSmokeButton(host);
        FormsKeys? code = null;
        button.KeyDown += (_, e) => code = e.KeyCode;
        host.OnKeyDown("Escape");
        Assert.Equal(FormsKeys.Escape, code);
    }

    [Fact]
    public void CharKey_Raises_KeyPress_And_Types_Once()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var textBox = new FormsTextBox();
        textBox.SetBounds(20, 20, 220, 30);
        form.Controls.Add(textBox);
        var (x, y) = CenterOn(form, textBox);
        host.OnMouseDown(x, y);

        char? pressed = null;
        textBox.KeyPress += (_, e) => pressed = e.KeyChar;
        host.OnKeyDown("a");
        Assert.Equal('a', pressed);
        Assert.Equal("a", textBox.Text);
        host.OnKeyDown("b");
        Assert.Equal("ab", textBox.Text);
    }

    [Fact]
    public void Handled_KeyDown_Skips_KeyPress_And_Edit()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var textBox = new FormsTextBox();
        textBox.SetBounds(20, 20, 220, 30);
        form.Controls.Add(textBox);
        var (x, y) = CenterOn(form, textBox);
        host.OnMouseDown(x, y);

        bool pressSeen = false;
        textBox.KeyDown += (_, e) => e.Handled = true;
        textBox.KeyPress += (_, _) => pressSeen = true;
        host.OnKeyDown("a");
        Assert.False(pressSeen);
        Assert.Equal("", textBox.Text);
    }
}
