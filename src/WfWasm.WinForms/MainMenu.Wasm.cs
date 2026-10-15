// Legacy-menu overlay: working MainMenu (upstream stub throws from the
// constructors). See Menu.Wasm.cs for the rationale.
using System.ComponentModel;

namespace System.Windows.Forms;

[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ToolboxItemFilter("System.Windows.Forms.MainMenu")]
public class MainMenu : Menu
{
    private RightToLeft _rightToLeft = RightToLeft.Inherit;

    public MainMenu()
        : base(items: null)
    {
    }

    public MainMenu(IContainer? container)
        : this()
    {
        container?.Add(this);
    }

    public MainMenu(MenuItem[]? items)
        : base(items)
    {
    }

    public event EventHandler? Collapse;

    [Localizable(true)]
    [AmbientValue(RightToLeft.Inherit)]
    public virtual RightToLeft RightToLeft
    {
        get => _rightToLeft;
        set => _rightToLeft = value;
    }

    public virtual MainMenu CloneMenu()
    {
        MainMenu clone = new();
        clone.CloneMenu(this);
        return clone;
    }

    public Form? GetForm() => null;

    protected internal virtual void OnCollapse(EventArgs e) => Collapse?.Invoke(this, e);
}
