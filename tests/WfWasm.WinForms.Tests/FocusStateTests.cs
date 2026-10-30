extern alias Fork;

using FormsButton = System.Windows.Forms.Button;
using FormsForm = System.Windows.Forms.Form;
using ForkFocus = Fork::Windows.Win32.PInvoke;
using ForkHwnd = Fork::Windows.Win32.Foundation.HWND;

namespace WfWasm.WinForms.Tests;

// Win32 focus backing: GetFocus/SetFocus used to be null stubs, so
// Control.Focused/ContainsFocus were always false and focus-gated UI
// (Syncfusion combo dropdowns, menus) silently never opened.
public sealed class FocusStateTests
{
    [Fact]
    public void SetFocus_GetFocus_RoundTrip()
    {
        using var form = new FormsForm();
        form.Show();
        try
        {
            ForkHwnd before = ForkFocus.GetFocus();
            bool ok = form.Focus();
            Assert.True(ok);
            Assert.Equal(form.Handle, (IntPtr)ForkFocus.GetFocus().Value);
            Assert.NotEqual(
                ((IntPtr)before.Value).ToInt64(),
                ((IntPtr)ForkFocus.GetFocus().Value).ToInt64());
        }
        finally
        {
            form.Hide();
        }
    }

    [Fact]
    public void Focused_And_ContainsFocus_Follow_Child_Focus()
    {
        using var form = new FormsForm { Text = "Focus" };
        form.Show();
        using var button = new FormsButton { Text = "B" };
        form.Controls.Add(button);
        using var other = new FormsButton { Text = "O" };
        form.Controls.Add(other);
        try
        {
            Assert.True(button.Focus());
            Assert.True(button.Focused);
            Assert.True(form.ContainsFocus);
            Assert.True(button.ContainsFocus);
            Assert.False(other.Focused);
            Assert.True(other.Focus());
            Assert.True(other.Focused);
            Assert.False(button.Focused);
            Assert.True(form.ContainsFocus);
        }
        finally
        {
            form.Hide();
        }
    }

    [Fact]
    public void SetFocus_Null_Preserves_Current_Focus()
    {
        using var form = new FormsForm();
        form.Show();
        try
        {
            Assert.True(form.Focus());
            ForkHwnd current = ForkFocus.GetFocus();
            Assert.Equal(form.Handle, (IntPtr)current.Value);
            ForkHwnd afterNull = ForkFocus.SetFocus(default);
            Assert.Equal(
                ((IntPtr)current.Value).ToInt64(),
                ((IntPtr)afterNull.Value).ToInt64());
        }
        finally
        {
            form.Hide();
        }
    }
}
