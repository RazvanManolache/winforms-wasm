using System.Reflection;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;

namespace WfWasm.WinForms.Tests;

// Top-level popups (Telerik menus, combo dropdowns) are parentless non-Form
// controls shown via ShowWindow: the fork tracks them in WasmPopupHost so
// the canvas host can paint them above dialogs and route input to the
// topmost one. ToolStripDropDown keeps its own host layer and is excluded.
public sealed class WasmPopupHostTests
{


    [Fact]
    public void Show_Tracks_Parentless_Popup()
    {
        using var popup = new PopupControl { Name = "menu", Width = 120, Height = 80 };
        popup.Show();
        System.Windows.Forms.WasmPopupHost.NoteShowWindow(popup.Handle, true);
        try
        {
            Assert.True(popup.Visible);
            Assert.Contains(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, popup));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(popup);
        }
    }

    [Fact]
    public void Hide_Untracks_Popup()
    {
        using var popup = new PopupControl { Name = "menu", Width = 120, Height = 80 };
        popup.Show();
        System.Windows.Forms.WasmPopupHost.NoteShowWindow(popup.Handle, true);
        Assert.Contains(System.Windows.Forms.WasmPopupHost.Snapshot(),
            c => ReferenceEquals(c, popup));
        try
        {
            popup.Hide();
            System.Windows.Forms.WasmPopupHost.NoteShowWindow(popup.Handle, false);
            Assert.DoesNotContain(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, popup));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(popup);
        }
    }

    [Fact]
    public void Snapshot_Prunes_Hidden_And_Disposed()
    {
        using var live = new PopupControl { Name = "live", Width = 60, Height = 40 };
        live.Show();
        System.Windows.Forms.WasmPopupHost.NoteShowWindow(live.Handle, true);
        var hidden = new FormsControl { Name = "hidden", Width = 60, Height = 40 };
        try
        {
            System.Windows.Forms.WasmPopupHost.Track(hidden);
            var snap = System.Windows.Forms.WasmPopupHost.Snapshot();
            Assert.Contains(snap, c => ReferenceEquals(c, live));
            Assert.DoesNotContain(snap, c => ReferenceEquals(c, hidden));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(live);
            System.Windows.Forms.WasmPopupHost.Untrack(hidden);
            hidden.Dispose();
        }

        var gone = new FormsControl { Name = "gone", Width = 60, Height = 40 };
        gone.Show();
        System.Windows.Forms.WasmPopupHost.NoteShowWindow(gone.Handle, true);
        System.Windows.Forms.WasmPopupHost.Track(gone);
        gone.Dispose();
        Assert.DoesNotContain(System.Windows.Forms.WasmPopupHost.Snapshot(),
            c => ReferenceEquals(c, gone));
    }

    [Fact]
    public void Form_Show_Does_Not_Track_As_Popup()
    {
        // Dialogs own Forms (WasmDialogHost): the popup registry must not
        // double-track them.
        using var form = new FormsForm { Text = "Dlg" };
        form.Show();
        try
        {
            Assert.DoesNotContain(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, form));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    [Fact]
    public void Parented_Control_Does_Not_Track()
    {
        using var parent = new FormsControl { Name = "parent", Width = 200, Height = 200 };
        using var child = new FormsControl { Name = "child", Width = 60, Height = 40 };
        parent.Controls.Add(child);
        parent.Show();
        try
        {
            child.Visible = true;
            System.Windows.Forms.WasmPopupHost.NoteShowWindow(child.Handle, true);
            Assert.DoesNotContain(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, child));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(parent);
            System.Windows.Forms.WasmPopupHost.Untrack(child);
        }
    }

    [Fact]
    public void Zero_Size_Entries_Are_Pruned()
    {
        // Pre-created dropdown shells / shaped-form helpers track at handle
        // creation with 0x0 bounds and are never shown; a 0-area window
        // paints and hit-tests nothing on desktop, so Snapshot drops them
        // (eval8 ERP ShapedFormControl / hotel RadDateTimePickerDropDown).
        using var zero = new FormsControl { Width = 0, Height = 0 };
        zero.Visible = true;
        _ = zero.Handle;
        using var sized = new FormsControl { Width = 60, Height = 40 };
        sized.Visible = true;
        _ = sized.Handle;
        System.Windows.Forms.WasmPopupHost.Track(zero);
        System.Windows.Forms.WasmPopupHost.Track(sized);
        try
        {
            var snap = System.Windows.Forms.WasmPopupHost.Snapshot();
            Assert.DoesNotContain(snap, c => ReferenceEquals(c, zero));
            Assert.Contains(snap, c => ReferenceEquals(c, sized));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(zero);
            System.Windows.Forms.WasmPopupHost.Untrack(sized);
        }
    }

    [Fact]
    public void Zero_Width_Nonzero_Height_Entries_Are_Kept()
    {
        // The real bookshelf dropdown tracks mid-show at 0x106 (sized by
        // SetBoundsCore after tracking); only a 0x0 entry is a never-laid-
        // out phantom. Pruning on either dimension kills the milestone.
        using var thin = new FormsControl { Width = 0, Height = 106 };
        thin.Visible = true;
        _ = thin.Handle;
        System.Windows.Forms.WasmPopupHost.Track(thin);
        try
        {
            var snap = System.Windows.Forms.WasmPopupHost.Snapshot();
            Assert.Contains(snap, c => ReferenceEquals(c, thin));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(thin);
        }
    }


    // Real popups carry WS_POPUP (bookshelf dropdown: style 0x82000000);
    // plain controls seed WS_CHILD and must be rejected by the funnels.
    private sealed class PopupControl : FormsControl
    {
        protected override System.Windows.Forms.CreateParams CreateParams
        {
            get
            {
                System.Windows.Forms.CreateParams cp = base.CreateParams;
                cp.Style |= unchecked((int)0x80000000);
                cp.Style &= ~0x40000000;
                return cp;
            }
        }
    }

    [Fact]
    public void WsChild_Controls_Rejected_By_All_Funnels()
    {
        // The eval8 RTE editor phantom (style 0x56010000, WS_CHILD) must
        // never track: WS_CHILD windows are clipped to their parent.
        using var child = new FormsControl { Width = 200, Height = 200 };
        child.Visible = true;
        _ = child.Handle;
        System.Windows.Forms.WasmPopupHost.NoteHandleCreated(child.Handle);
        System.Windows.Forms.WasmPopupHost.NoteShowWindow(child.Handle, true);
        System.Windows.Forms.WasmPopupHost.NoteVisibleTransition(child, true);
        try
        {
            Assert.DoesNotContain(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, child));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(child);
        }
    }

    [Fact]
    public void WsPopup_Controls_Tracked_By_Funnels()
    {
        using var popup = new PopupControl { Width = 120, Height = 80 };
        popup.Visible = true;
        _ = popup.Handle;
        System.Windows.Forms.WasmPopupHost.NoteShowWindow(popup.Handle, true);
        try
        {
            Assert.Contains(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, popup));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(popup);
        }
    }

    [Fact]
    public void Handleless_Controls_Rejected_Until_Created()
    {
        // The style is unknowable before handle creation, so the visible
        // funnel must not provisionally track: the RTE phantom rode exactly
        // this handleless window (style=nohandle at Track).
        using var late = new FormsControl { Width = 120, Height = 80 };
        System.Windows.Forms.WasmPopupHost.NoteVisibleTransition(late, true);
        try
        {
            Assert.DoesNotContain(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, late));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(late);
        }
    }

    [Fact]
    public void Parented_Controls_Rejected_By_All_Funnels()
    {
        // A control parented after tracking (PDN create-then-parent boot)
        // must leave the snapshot, and every funnel must reject parented
        // controls at entry.
        using var parent = new FormsControl();
        using var child = new FormsControl { Width = 50, Height = 30 };
        child.Visible = true;
        _ = child.Handle;
        parent.Controls.Add(child);
        System.Windows.Forms.WasmPopupHost.NoteVisibleTransition(child, true);
        System.Windows.Forms.WasmPopupHost.NoteShowWindow(child.Handle, true);
        try
        {
            Assert.DoesNotContain(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, child));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(child);
        }
    }

    [Fact]
    public void Framework_Nested_Helpers_Do_Not_Track()
    {
        // ToolStripScrollButton+StickyLabel is parentless+visible framework
        // machinery, never a popup. Tracking it poisoned snapshots and made
        // the host swallow all mouse input (PDN "can't draw": brush drag
        // committed no ink). All funnels must reject it.
        var t = typeof(FormsControl).Assembly.GetType(
            "System.Windows.Forms.ToolStripScrollButton+StickyLabel");
        Assert.NotNull(t);
        var label = (FormsControl)Activator.CreateInstance(t,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null, args: new object[] { false }, culture: null)!;
        try
        {
            System.Windows.Forms.WasmPopupHost.NoteVisibleTransition(label, true);
            System.Windows.Forms.WasmPopupHost.NoteShowWindow(label.Handle, true);
            Assert.DoesNotContain(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, label));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(label);
            label.Dispose();
        }
    }

    [Fact]
    public void Track_Is_Idempotent_Per_Control()
    {
        using var popup = new PopupControl { Name = "menu", Width = 120, Height = 80 };
        popup.Show();
        System.Windows.Forms.WasmPopupHost.NoteShowWindow(popup.Handle, true);
        try
        {
            System.Windows.Forms.WasmPopupHost.Track(popup);
            System.Windows.Forms.WasmPopupHost.Track(popup);
            Assert.Single(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, popup));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(popup);
        }
    }
}
