using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsTextBox = System.Windows.Forms.TextBox;

namespace WfWasm.CanvasHost.Tests;

// The host owns no message loop, so it synthesizes key messages and pumps
// Application message filters before delivering keystrokes (desktop order).
// Vendors' menu/shortcut managers live in filters (Syncfusion
// MainFrameBarManager consumes Alt+letter as WM_SYSCHAR).
public sealed class KeyFilterPumpTests
{
    private sealed class RecordingFilter : System.Windows.Forms.IMessageFilter
    {
        public readonly List<(int Msg, int WParam)> Seen = new();
        public bool Swallow;
        public bool PreFilterMessage(ref System.Windows.Forms.Message m)
        {
            Seen.Add((m.Msg, m.WParam.ToInt32()));
            return Swallow;
        }
    }

    [Fact]
    public void KeyDown_Pumps_SysKeyDown_And_SysChar_To_Filters()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var filter = new RecordingFilter();
        System.Windows.Forms.Application.AddMessageFilter(filter);
        try
        {
            host.OnKeyDown("f", alt: true);
            Assert.Contains(filter.Seen, s => s.Msg == 0x0104 && s.WParam == 70);
            Assert.Contains(filter.Seen, s => s.Msg == 0x0106 && s.WParam == 102);
        }
        finally
        {
            System.Windows.Forms.Application.RemoveMessageFilter(filter);
        }
    }

    [Fact]
    public void Swallowing_Filter_Suppresses_Key_Delivery()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke)!;
        var textBox = new FormsTextBox();
        textBox.SetBounds(20, 20, 220, 30);
        form.Controls.Add(textBox);
        int absX = form.Location.X, absY = form.Location.Y + 30;
        for (FormsControl? c = textBox; c is not null && c is not FormsForm; c = c.Parent) { absX += c.Left; absY += c.Top; }
        host.OnMouseDown(absX + textBox.Width / 2, absY + textBox.Height / 2);
        var filter = new RecordingFilter { Swallow = true };
        System.Windows.Forms.Application.AddMessageFilter(filter);
        try
        {
            host.OnKeyDown("a");
            Assert.Equal("", textBox.Text);
        }
        finally
        {
            System.Windows.Forms.Application.RemoveMessageFilter(filter);
        }
    }
}
