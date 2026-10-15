// Legacy-menu overlay: working ContextMenu (upstream stub throws from
// the constructors). See Menu.Wasm.cs for the rationale. Show records the
// request and raises Popup; there is no native TrackPopupMenu in the
// browser, so nothing displays yet (LastShowRequest lets the host bridge
// a visible popup later).
using System.ComponentModel;

namespace System.Windows.Forms;

[DefaultEvent(nameof(Popup))]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public class ContextMenu : Menu
{
    private RightToLeft _rightToLeft = RightToLeft.No;
    private Control? _sourceControl;

    public ContextMenu()
        : base(items: null)
    {
    }

    public ContextMenu(MenuItem[]? menuItems)
        : base(menuItems)
    {
    }

    internal static (ContextMenu Menu, Control? Control, Point Position)? LastShowRequest { get; private set; }

    [Localizable(true)]
    [DefaultValue(RightToLeft.No)]
    public virtual RightToLeft RightToLeft
    {
        get => _rightToLeft;
        set => _rightToLeft = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? SourceControl => _sourceControl;

    public event EventHandler? Popup;

    public event EventHandler? Collapse;

    protected internal virtual void OnCollapse(EventArgs e) => Collapse?.Invoke(this, e);

    protected internal virtual void OnPopup(EventArgs e) => Popup?.Invoke(this, e);

    protected internal virtual bool ProcessCmdKey(ref Message msg, Keys keyData, Control? control) => false;

    public void Show(Control? control, Point pos) => Show(control, pos, LeftRightAlignment.Left);

    public void Show(Control? control, Point pos, LeftRightAlignment alignment)
    {
        _sourceControl = control;
        LastShowRequest = (this, control, pos);
        OnPopup(EventArgs.Empty);
    }
}
