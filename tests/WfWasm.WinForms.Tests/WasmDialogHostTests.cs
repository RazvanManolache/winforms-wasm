using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsForm = System.Windows.Forms.Form;

namespace WfWasm.WinForms.Tests;

// The browser thread cannot run a nested RunDialog pump, so Show and
// ShowDialog present through WasmDialogHost (visible + tracked) instead
// of throwing or hanging. The canvas host paints tracked windows above
// everything and captures input for the topmost modal one.
public sealed class WasmDialogHostTests
{
    [Fact]
    public void ShowDialog_Returns_None_And_Tracks_Modal()
    {
        // The headless branch keys off WasmDialogHost.IsHeadlessSession,
        // not UserInteractive (the session is interactive but queue-less;
        // see MessagePumpTests).
        Assert.True(System.Windows.Forms.SystemInformation.UserInteractive);
        using var form = new FormsForm { Text = "Dlg" };
        Assert.Equal(FormsDialogResult.None, form.ShowDialog());
        try
        {
            Assert.True(form.Visible);
            Assert.True(form.Modal);
            Assert.True(System.Windows.Forms.WasmDialogHost.TopModal(out FormsForm? top));
            Assert.Same(form, top);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    [Fact]
    public void Show_Tracks_Modeless_Without_Modal_Capture()
    {
        using var form = new FormsForm { Text = "Tool" };
        form.Show();
        try
        {
            Assert.True(form.Visible);
            Assert.False(form.Modal);
            Assert.Contains(System.Windows.Forms.WasmDialogHost.Snapshot(),
                w => ReferenceEquals(w.Form, form) && !w.IsModal);
            Assert.False(System.Windows.Forms.WasmDialogHost.TopModal(out _));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    [Fact]
    public void Snapshot_Prunes_Hidden_And_Disposed()
    {
        using var visible = new FormsForm { Text = "Live" };
        visible.Show();
        var hidden = new FormsForm { Text = "Hidden" };
        try
        {
            System.Windows.Forms.WasmDialogHost.Track(hidden, modal: true);
            var snap = System.Windows.Forms.WasmDialogHost.Snapshot();
            Assert.Contains(snap, w => ReferenceEquals(w.Form, visible));
            Assert.DoesNotContain(snap, w => ReferenceEquals(w.Form, hidden));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(visible);
            System.Windows.Forms.WasmDialogHost.Untrack(hidden);
            hidden.Dispose();
        }

        var gone = new FormsForm { Text = "Gone" };
        gone.Show();
        System.Windows.Forms.WasmDialogHost.Track(gone, modal: true);
        gone.Dispose();
        Assert.DoesNotContain(System.Windows.Forms.WasmDialogHost.Snapshot(),
            w => ReferenceEquals(w.Form, gone));
    }

    [Fact]
    public void Close_Hides_Modal_With_Cancel_Without_Disposing()
    {
        // Close() posts WM_CLOSE, which never dispatches without an OS
        // queue: on WASM it runs the handler synchronously (same Closing/
        // Closed events, Cancel result and hide as the desktop path,
        // without disposing the modal).
        using var form = new FormsForm { Text = "Dlg" };
        bool closed = false;
        form.FormClosed += (_, _) => closed = true;
        form.ShowDialog();
        form.Close();
        try
        {
            Assert.False(form.Visible);
            Assert.Equal(FormsDialogResult.Cancel, form.DialogResult);
            Assert.False(form.IsDisposed);
            Assert.True(closed);
            Assert.False(System.Windows.Forms.WasmDialogHost.TopModal(out _));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    [Fact]
    public void TopModal_Prefers_Youngest_Modal()
    {
        using var first = new FormsForm { Text = "First" };
        using var second = new FormsForm { Text = "Second" };
        first.Show();
        second.Show();
        System.Windows.Forms.WasmDialogHost.Track(first, modal: true);
        System.Windows.Forms.WasmDialogHost.Track(second, modal: true);
        try
        {
            Assert.True(System.Windows.Forms.WasmDialogHost.TopModal(out FormsForm? top));
            Assert.Same(second, top);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(first);
            System.Windows.Forms.WasmDialogHost.Untrack(second);
        }
    }

    // ShowDialog returns None immediately on WASM, so PDN's modal flows
    // abort synchronously and dispose the dialog in the presenting turn.
    // Effect config dialogs survive that abort (deferred disposal) so the
    // user can drive them; closing one later replays with the recorded
    // result. Regression: Adjustments/Levels click flashes and dies
    // (configDialog.Dispose on None in RunEffectImpl).
    [Fact]
    public void AbortDispose_SameTurn_Survives_For_Effect_Dialogs()
    {
        using var form = new PaintDotNet.Effects.EffectConfigDialog { Text = "Levels Adjustment" };
        Assert.Equal(FormsDialogResult.None, form.ShowDialog());
        try
        {
            form.Dispose();
            Assert.False(form.IsDisposed);
            Assert.True(form.Visible);
            Assert.True(System.Windows.Forms.WasmDialogHost.IsDeferred(form));
            Assert.Contains(System.Windows.Forms.WasmDialogHost.Snapshot(),
                w => ReferenceEquals(w.Form, form) && w.IsModal);
            System.Windows.Forms.WasmDialogHost.AdvanceTurn();
            form.Dispose();
            Assert.True(form.IsDisposed);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    // GPU effects (Levels) derive from EffectConfigForm, not
    // EffectConfigDialog: the policy must match both roots.
    [Fact]
    public void AbortDispose_SameTurn_Survives_For_Gpu_Effect_Forms()
    {
        using var form = new PaintDotNet.Effects.LevelsGpuEffectFormStandIn { Text = "Levels Adjustment" };
        Assert.Equal(FormsDialogResult.None, form.ShowDialog());
        try
        {
            form.Dispose();
            Assert.False(form.IsDisposed);
            Assert.True(System.Windows.Forms.WasmDialogHost.IsDeferred(form));
            System.Windows.Forms.WasmDialogHost.AdvanceTurn();
            form.Dispose();
            Assert.True(form.IsDisposed);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    // PDN posts ecf.Close() to its sync context: it lands in a later
    // turn with no result chosen. That posted abort-Close defers; a user
    // close (OK/Cancel set at entry) stays real.
    [Fact]
    public void AbortClose_Posted_With_None_Stays_Open()
    {
        using var form = new PaintDotNet.Effects.LevelsGpuEffectFormStandIn { Text = "Levels Adjustment" };
        Assert.Equal(FormsDialogResult.None, form.ShowDialog());
        try
        {
            form.Dispose();
            Assert.False(form.IsDisposed);
            System.Windows.Forms.WasmDialogHost.AdvanceTurn();
            form.Close();
            Assert.False(form.IsDisposed);
            Assert.True(form.Visible);
            Assert.Contains(System.Windows.Forms.WasmDialogHost.Snapshot(),
                w => ReferenceEquals(w.Form, form) && w.IsModal);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.AdvanceTurn();
            try { form.Dispose(); } catch { }
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    [Fact]
    public void UserClose_With_Result_Closes_Deferred_Dialog()
    {
        using var form = new PaintDotNet.Effects.LevelsGpuEffectFormStandIn { Text = "Levels Adjustment" };
        Assert.Equal(FormsDialogResult.None, form.ShowDialog());
        try
        {
            form.Dispose();
            Assert.False(form.IsDisposed);
            System.Windows.Forms.WasmDialogHost.AdvanceTurn();
            form.DialogResult = FormsDialogResult.OK;
            form.Close();
            Assert.False(form.Visible);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    // The sync abort also hides (configDialog.Hide()): same-turn hides
    // defer, later user closes hide for real.
    [Fact]
    public void AbortHide_SameTurn_Stays_Visible()
    {
        using var form = new PaintDotNet.Effects.LevelsGpuEffectFormStandIn { Text = "Levels Adjustment" };
        Assert.Equal(FormsDialogResult.None, form.ShowDialog());
        try
        {
            form.Hide();
            Assert.True(form.Visible);
            Assert.True(System.Windows.Forms.WasmDialogHost.IsDeferred(form));
            System.Windows.Forms.WasmDialogHost.AdvanceTurn();
            form.Hide();
            Assert.False(form.Visible);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    [Fact]
    public void AbortDispose_SameTurn_Disposes_NonEffect_Dialogs()
    {
        using var form = new FormsForm { Text = "Progress" };
        Assert.Equal(FormsDialogResult.None, form.ShowDialog());
        try
        {
            form.Dispose();
            Assert.True(form.IsDisposed);
            Assert.False(System.Windows.Forms.WasmDialogHost.IsDeferred(form));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    [Fact]
    public void Represent_Supersedes_Prior_Orphan()
    {
        using var first = new PaintDotNet.Effects.EffectConfigDialog { Text = "Levels" };
        using var second = new PaintDotNet.Effects.EffectConfigDialog { Text = "Levels" };
        Assert.Equal(FormsDialogResult.None, first.ShowDialog());
        first.Dispose();
        Assert.False(first.IsDisposed);
        try
        {
            Assert.Equal(FormsDialogResult.None, second.ShowDialog());
            Assert.True(first.IsDisposed);
            Assert.False(second.IsDisposed);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.AdvanceTurn();
            try { second.Dispose(); } catch { }
            System.Windows.Forms.WasmDialogHost.Untrack(first);
            System.Windows.Forms.WasmDialogHost.Untrack(second);
        }
    }
}
