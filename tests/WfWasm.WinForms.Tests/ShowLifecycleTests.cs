namespace WfWasm.WinForms.Tests;

// Show lifecycle: native ShowWindow delivers WM_SHOWWINDOW, which is what
// runs CreateControl (OnCreateControl for the whole visible subtree) and
// Form.Load. The fork's ShowWindow stub used to swallow the call, so none
// of that ran: load-gated controls (Telerik RadControl.IsLoaded) never
// loaded and RadDock never activated its first document on show.
public sealed class ShowLifecycleTests
{
    private sealed class TrackingControl : System.Windows.Forms.Control
    {
        public int CreateControlCount;
        public int HandleCreatedCount;

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            CreateControlCount++;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            HandleCreatedCount++;
        }
    }

    [Fact]
    public void Show_Runs_CreateControl_For_Visible_Subtree_And_Fires_Load()
    {
        using var form = new System.Windows.Forms.Form();
        var child = new TrackingControl();
        var grandchild = new TrackingControl();
        child.Controls.Add(grandchild);
        form.Controls.Add(child);
        int loadFired = 0;
        form.Load += (_, _) => loadFired++;

        form.Show();

        Assert.True(form.Created);
        Assert.True(child.Created);
        Assert.True(grandchild.Created);
        Assert.Equal(1, child.CreateControlCount);
        Assert.Equal(1, grandchild.CreateControlCount);
        Assert.Equal(1, loadFired);
    }

    // Native parity (see NativeWindow.DispatchCreateWindow): creating a
    // handle delivers WM_CREATE, which raises OnHandleCreated. Apps gate
    // real work on this (Telerik RadDock parents its document container
    // there); environment assumptions inside those handlers are handled by
    // coherent fork stubs, not by suppressing the notification.
    [Fact]
    public void Show_Fires_OnHandleCreated_For_Created_Controls()
    {
        using var form = new System.Windows.Forms.Form();
        var child = new TrackingControl();
        form.Controls.Add(child);

        form.Show();

        Assert.True(child.IsHandleCreated);
        Assert.NotEqual(IntPtr.Zero, child.Handle);
        Assert.Equal(1, child.HandleCreatedCount);
    }

    [Fact]
    public void Show_Skips_Hidden_Children_Until_Made_Visible()
    {
        using var form = new System.Windows.Forms.Form();
        var child = new TrackingControl { Visible = false };
        form.Controls.Add(child);

        form.Show();
        Assert.False(child.Created);

        child.Visible = true;
        Assert.True(child.Created);
        Assert.Equal(1, child.CreateControlCount);
    }
}
