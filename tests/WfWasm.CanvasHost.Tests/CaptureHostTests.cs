using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;

namespace WfWasm.CanvasHost.Tests;

public sealed class CaptureHostTests
{
    private static List<FormsButton> AllButtons(FormsControl root)
    {
        var found = new List<FormsButton>();
        void Walk(FormsControl current)
        {
            foreach (FormsControl c in current.Controls)
            {
                if (c is FormsButton btn)
                    found.Add(btn);
                if (c.Controls.Count > 0)
                    Walk(c);
            }
        }

        Walk(root);
        return found;
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

    [Fact]
    public void Click_Raises_MouseCaptureChanged()
    {
        // F1: upstream WmMouseDown/WmMouseUp set/release capture around the
        // handlers; the host performs the same transitions, so a plain
        // click notifies the button (calculator "2" depends on it).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        List<FormsButton> buttons = AllButtons(form);
        Assert.NotEmpty(buttons);
        int fired = 0;
        foreach (FormsButton btn in buttons)
            btn.MouseCaptureChanged += (_, _) => fired++;

        var (x, y) = CenterOn(form, buttons[0]);
        host.OnMouseDown(x, y);
        Assert.Equal(0, fired); // capture acquired, not yet lost
        host.OnMouseUp(x, y);
        Assert.True(fired >= 1, $"expected MouseCaptureChanged from click, got {fired}");
    }
}
