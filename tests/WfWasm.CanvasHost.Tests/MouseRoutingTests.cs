using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;

public sealed class MouseRoutingTests
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

    [Fact]
    public void Mousedown_Reaches_Control_Handler_With_Local_Coords()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        FormsButton button = FindFirstButton(form);
        int absX = form.Location.X;
        int absY = form.Location.Y + 30;
        for (FormsControl? current = button; current is not null && current is not FormsForm; current = current.Parent)
        {
            absX += current.Left;
            absY += current.Top;
        }

        FormsMouseEventArgs? received = null;
        button.MouseDown += (_, e) => received = e;
        host.OnMouseDown(absX + (button.Width / 2), absY + (button.Height / 2));

        Assert.NotNull(received);
        Assert.Equal(System.Windows.Forms.MouseButtons.Left, received.Button);
        Assert.Equal(button.Width / 2, received.X);
        Assert.Equal(button.Height / 2, received.Y);
    }

    [Fact]
    public void MouseUp_Without_Prior_Down_Does_Not_Throw()
    {
        // OnMouseUp logs unconditionally (tracing doc-stroke completion);
        // it must stay safe with no active form, target, or drawing flag.
        using var host = new WinFormsAppHost();
        var ex = Record.Exception(() => host.OnMouseUp(10, 10));
        Assert.Null(ex);
    }

    [Fact]
    public void Right_Mousedown_Reaches_Handler_As_Right()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        FormsButton button = FindFirstButton(form);
        int absX = form.Location.X;
        int absY = form.Location.Y + 30;
        for (FormsControl? current = button; current is not null && current is not FormsForm; current = current.Parent)
        {
            absX += current.Left;
            absY += current.Top;
        }

        FormsMouseEventArgs? received = null;
        button.MouseDown += (_, e) => received = e;
        host.OnMouseDown(absX + (button.Width / 2), absY + (button.Height / 2),
            System.Windows.Forms.MouseButtons.Right);

        Assert.NotNull(received);
        Assert.Equal(System.Windows.Forms.MouseButtons.Right, received.Button);
    }
}
