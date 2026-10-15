extern alias WfDraw;

namespace WfWasm.Shim;

using Draw = WfDraw::System.Drawing;

/// <summary>
/// Window-handle, painting and layout members of Control (cf.
/// System.Windows.Forms.Control). The web backend has no HWNDs: handle members
/// work with synthetic tokens, painting helpers route to the canvas recorder,
/// and <c>CreateGraphics</c> hands out a 1x1 scratch surface for measuring.
/// </summary>
public partial class Control
{
    private bool _disposing;
    private int? _fontHeight;
    private ContextMenuStrip? _contextMenuStrip;
    private AccessibleRole _accessibleRole = AccessibleRole.Default;
    private bool _useWaitCursor;

    /// <summary>True while <c>Dispose</c> runs (cf. Disposing).</summary>
    public bool Disposing => _disposing;

    /// <summary>Accessibility role (cf. AccessibleRole).</summary>
    public AccessibleRole AccessibleRole
    {
        get => _accessibleRole;
        set => _accessibleRole = value;
    }

    /// <summary>Shortcut menu (cf. ContextMenuStrip).</summary>
    public virtual ContextMenuStrip? ContextMenuStrip
    {
        get => _contextMenuStrip;
        set
        {
            if (!ReferenceEquals(_contextMenuStrip, value))
            {
                _contextMenuStrip = value;
                OnContextMenuStripChanged(EventArgs.Empty);
            }
        }
    }

    /// <summary>Shows the wait cursor (cf. UseWaitCursor). Stored only.</summary>
    public bool UseWaitCursor
    {
        get => _useWaitCursor;
        set => _useWaitCursor = value;
    }

    /// <summary>Whether the control can take focus (cf. CanSelect).</summary>
    public bool CanSelect => Enabled && Visible;

    /// <summary>Raised when a child is added (cf. ControlAdded).</summary>
    public event ControlEventHandler? ControlAdded;

    /// <summary>Raised when a child is removed (cf. ControlRemoved).</summary>
    public event ControlEventHandler? ControlRemoved;

    /// <summary>Raised when the cursor changes (cf. CursorChanged).</summary>
    public event EventHandler? CursorChanged;

    /// <summary>Raised when the shortcut menu changes (cf. ContextMenuStripChanged).</summary>
    public event EventHandler? ContextMenuStripChanged;

    /// <summary>
    /// Finds the control for a live handle (cf. FromHandle). Always null:
    /// no real HWNDs exist on the web backend.
    /// </summary>
    /// <param name="handle">Handle (ignored).</param>
    /// <returns>Null.</returns>
    public static Control? FromHandle(IntPtr handle) => null;

    /// <summary>
    /// Finds the control for a live child handle (cf. FromChildHandle).
    /// Always null: no real HWNDs exist on the web backend.
    /// </summary>
    /// <param name="handle">Handle (ignored).</param>
    /// <returns>Null.</returns>
    public static Control? FromChildHandle(IntPtr handle) => null;

    /// <summary>
    /// Scratch drawing surface for measuring (cf. CreateGraphics). The web
    /// backend has no window device contexts, so this returns a 1x1 bitmap
    /// surface: measuring works, drawing to it is discarded.
    /// </summary>
    /// <returns>Graphics over a 1x1 scratch bitmap.</returns>
    public Draw.Graphics CreateGraphics() =>
        Draw.Graphics.FromImage(new Draw.Bitmap(1, 1));

    /// <summary>Releases the synthetic handle (cf. DestroyHandle).</summary>
    protected virtual void DestroyHandle()
    {
    }

    /// <summary>Recreates the synthetic handle (cf. RecreateHandle). No-op.</summary>
    protected void RecreateHandle()
    {
    }

    /// <summary>Pushes style changes (cf. UpdateStyles). No-op.</summary>
    protected void UpdateStyles()
    {
    }

    /// <summary>Default size (cf. DefaultSize). Borderless: empty.</summary>
    protected virtual Draw.Size DefaultSize => Draw.Size.Empty;

    /// <summary>Cached font height (cf. FontHeight).</summary>
    protected int FontHeight
    {
        get => _fontHeight ?? Font.Height;
        set => _fontHeight = value;
    }

    /// <summary>Focus cues (cf. ShowFocusCues). Always false: no OS theme.</summary>
    protected virtual bool ShowFocusCues => false;

    /// <summary>Keyboard cues (cf. ShowKeyboardCues). Always false.</summary>
    protected virtual bool ShowKeyboardCues => false;

    /// <summary>Auto-size behavior (cf. GetAutoSizeMode).</summary>
    /// <returns><c>GrowOnly</c>.</returns>
    protected AutoSizeMode GetAutoSizeMode() => AutoSizeMode.GrowOnly;

    /// <summary>Raises <c>Paint</c> on another control (cf. InvokePaint).</summary>
    /// <param name="c">Control to paint.</param>
    /// <param name="e">Paint data.</param>
    protected void InvokePaint(Control c, PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(e);
        c.OnPaint(e);
    }

    /// <summary>Paints another control's background (cf. InvokePaintBackground).</summary>
    /// <param name="c">Control to paint.</param>
    /// <param name="e">Paint data.</param>
    protected void InvokePaintBackground(Control c, PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(e);
        c.OnPaintBackground(e);
    }

    /// <summary>Mnemonic test (cf. IsMnemonic).</summary>
    /// <param name="charCode">Mnemonic character.</param>
    /// <param name="text">Text with &amp; markers.</param>
    /// <returns>True when <paramref name="text"/> marks <paramref name="charCode"/>.</returns>
    public static bool IsMnemonic(char charCode, string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        char lower = char.ToLowerInvariant(charCode);
        for (int i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] == '&')
            {
                if (text[i + 1] == '&')
                {
                    i++;
                    continue;
                }
                return char.ToLowerInvariant(text[i + 1]) == lower;
            }
        }
        return false;
    }

    /// <summary>Mnemonic processing (cf. ProcessMnemonic). No-op: false.</summary>
    /// <param name="charCode">Character (ignored).</param>
    /// <returns>False.</returns>
    protected virtual bool ProcessMnemonic(char charCode) => false;

    /// <summary>Outer size for a client size (cf. SizeFromClientSize). Borderless: identity.</summary>
    /// <param name="clientSize">Client size.</param>
    /// <returns>The same size (no non-client chrome here).</returns>
    protected virtual Draw.Size SizeFromClientSize(Draw.Size clientSize) => clientSize;

    /// <summary>Mirrors alignment for RTL (cf. RtlTranslateAlignment).</summary>
    protected LeftRightAlignment RtlTranslateLeftRight(LeftRightAlignment align) =>
        RightToLeft == RightToLeft.Yes
            ? align == LeftRightAlignment.Left ? LeftRightAlignment.Right
            : align == LeftRightAlignment.Right ? LeftRightAlignment.Left : align
            : align;

    /// <summary>Mirrors alignment for RTL (cf. RtlTranslateAlignment).</summary>
    protected HorizontalAlignment RtlTranslateAlignment(HorizontalAlignment align) =>
        RightToLeft == RightToLeft.Yes
            ? align == HorizontalAlignment.Left ? HorizontalAlignment.Right
            : align == HorizontalAlignment.Right ? HorizontalAlignment.Left : align
            : align;

    /// <summary>Mirrors content alignment for RTL (cf. RtlTranslateContent).</summary>
    protected Draw.ContentAlignment RtlTranslateContent(Draw.ContentAlignment align) =>
        RightToLeft == RightToLeft.Yes ? MirrorContent(align) : align;

    /// <summary>Mirrors content alignment for RTL (cf. RtlTranslateAlignment).</summary>
    protected Draw.ContentAlignment RtlTranslateAlignment(Draw.ContentAlignment align) =>
        RtlTranslateContent(align);

    /// <summary>Mirrors alignment for RTL (cf. RtlTranslateHorizontal).</summary>
    protected HorizontalAlignment RtlTranslateHorizontal(HorizontalAlignment align) =>
        RtlTranslateAlignment(align);

    private static Draw.ContentAlignment MirrorContent(Draw.ContentAlignment align) => align switch
    {
        Draw.ContentAlignment.TopLeft => Draw.ContentAlignment.TopRight,
        Draw.ContentAlignment.TopRight => Draw.ContentAlignment.TopLeft,
        Draw.ContentAlignment.MiddleLeft => Draw.ContentAlignment.MiddleRight,
        Draw.ContentAlignment.MiddleRight => Draw.ContentAlignment.MiddleLeft,
        Draw.ContentAlignment.BottomLeft => Draw.ContentAlignment.BottomRight,
        Draw.ContentAlignment.BottomRight => Draw.ContentAlignment.BottomLeft,
        _ => align,
    };

    /// <summary>Raises <c>ControlAdded</c> (cf. OnControlAdded).</summary>
    protected virtual void OnControlAdded(ControlEventArgs e) =>
        ControlAdded?.Invoke(this, e);

    /// <summary>Raises <c>ControlRemoved</c> (cf. OnControlRemoved).</summary>
    protected virtual void OnControlRemoved(ControlEventArgs e) =>
        ControlRemoved?.Invoke(this, e);

    /// <summary>Raises <c>CursorChanged</c> (cf. OnCursorChanged).</summary>
    protected virtual void OnCursorChanged(EventArgs e) =>
        CursorChanged?.Invoke(this, e);

    /// <summary>Raises <c>ContextMenuStripChanged</c> (cf. OnContextMenuStripChanged).</summary>
    protected virtual void OnContextMenuStripChanged(EventArgs e) =>
        ContextMenuStripChanged?.Invoke(this, e);

    /// <summary>Raises <c>ParentChanged</c> (cf. OnParentChanged).</summary>
    protected virtual void OnParentChanged(EventArgs e)
    {
    }

    /// <summary>Raises <c>MouseClick</c> (cf. OnMouseClick).</summary>
    protected virtual void OnMouseClick(MouseEventArgs e)
    {
    }

    /// <summary>Raises <c>HelpRequested</c> (cf. OnHelpRequested).</summary>
    protected virtual void OnHelpRequested(HelpEventArgs e)
    {
    }

    internal void NotifyChildAdded(Control child) =>
        OnControlAdded(new ControlEventArgs(child));

    internal void NotifyChildRemoved(Control child) =>
        OnControlRemoved(new ControlEventArgs(child));

    internal void MarkDisposing() => _disposing = true;
}
