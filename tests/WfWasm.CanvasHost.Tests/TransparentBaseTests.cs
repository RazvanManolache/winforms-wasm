using WfWasm.CanvasHost;
using FormsLabel = System.Windows.Forms.Label;
using FormsPanel = System.Windows.Forms.Panel;

// Transparent-BackColor controls over owner-painted ancestors (Telerik
// theme wood, BackgroundImages) must keep a transparent self-paint
// bitmap so the layer blit composites the real beneath-pixels.
// Clearing with EffectiveBackColor paints a wrong flat rect (black
// Telerik labels over BookShelf's wood). Plain flat ancestors keep
// the old flat Clear (better text AA over a known base).
public sealed class TransparentBaseTests
{
    private sealed class OwnerPaintPanel : FormsPanel
    {
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
        }
    }

    [Fact]
    public void Plain_Ancestors_Keep_Flat_Clear()
    {
        using var panel = new FormsPanel();
        using var label = new FormsLabel { BackColor = System.Drawing.Color.Transparent };
        panel.Controls.Add(label);

        Assert.False(WinFormsAppHost.HasPaintedAncestor(label));
    }

    [Fact]
    public void BackgroundImage_Ancestor_Needs_Transparency()
    {
        using var image = new System.Drawing.Bitmap(4, 4);
        using var panel = new FormsPanel { BackgroundImage = image };
        using var label = new FormsLabel { BackColor = System.Drawing.Color.Transparent };
        panel.Controls.Add(label);

        Assert.True(WinFormsAppHost.HasPaintedAncestor(label));
    }

    [Fact]
    public void OwnerPaint_Ancestor_Needs_Transparency()
    {
        using var panel = new OwnerPaintPanel();
        using var label = new FormsLabel { BackColor = System.Drawing.Color.Transparent };
        panel.Controls.Add(label);

        Assert.True(WinFormsAppHost.HasPaintedAncestor(label));
    }

    [Fact]
    public void Plain_Control_Grandchild_Of_Painted_Ancestor_Needs_Transparency()
    {
        using var outer = new OwnerPaintPanel();
        using var inner = new FormsPanel();
        using var label = new FormsLabel { BackColor = System.Drawing.Color.Transparent };
        outer.Controls.Add(inner);
        inner.Controls.Add(label);

        Assert.True(WinFormsAppHost.HasPaintedAncestor(label));
    }
}
