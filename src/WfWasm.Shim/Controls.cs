extern alias WfDraw;

using System.ComponentModel;

namespace WfWasm.Shim;

using Draw = WfDraw::System.Drawing;

/// <summary>
/// Palette shared by shim controls. Values match
/// <c>WfWasm.Renderer.DialogOverlayPainter</c>; keep in sync.
/// </summary>
internal static class Palette
{
    internal const uint ButtonFill = 0xE1E1E1FF;
    internal const uint DefaultButtonFill = 0x0078D7FF;
    internal const uint ButtonText = 0x000000FF;
    internal const uint DefaultButtonText = 0xFFFFFFFF;
    internal const uint LabelText = 0x000000FF;
    internal const string UiFontCss = "13px sans-serif";
}

/// <summary>
/// Minimal interactive control node (cf. System.Windows.Forms.Control).
/// Bounds (X, Y, Width, Height) and Text mirror the designer model
/// (cf. Form1.Designer.cs: Location, Size, Text). X/Y are relative to the
/// parent; a root is painted and hit-tested from (0, 0).
/// </summary>
public partial class Control : System.ComponentModel.Component, IWin32Window
{
    private string _name = "";
    private string _text = "";
    private readonly int _creatingThreadId;

    public Control(string name = "")
    {
        _creatingThreadId = Environment.CurrentManagedThreadId;
        Application.EnsureContextInstalled();
        Name = name;
        Children = CreateControlsInstance();
    }

    protected virtual ControlCollection CreateControlsInstance() => new(this);

    /// <summary>
    /// Parameterless ctor for rewritten apps: compiled `new Button()` emits
    /// `newobj .ctor()`, which an optional-parameter ctor does not satisfy.
    /// </summary>
    public Control()
        : this("")
    {
    }

    /// <summary>
    /// Child collection (cf. System.Windows.Forms.Control.ControlCollection):
    /// a List with parent wiring, so `Controls.Add` behaves like WinForms.
    /// </summary>
    public class ControlCollection : ArrangedElementCollection
    {
        private readonly Control _owner;

        public ControlCollection(Control owner)
        {
            _owner = owner;
        }

        /// <summary>Typed indexer (cf. ControlCollection.this[int]).</summary>
        public new virtual Control this[int index]
        {
            get => (Control)base[index]!;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                Release((Control)base[index]!);
                base[index] = value;
                Adopt(value);
            }
        }

        /// <summary>Typed enumerator so internal foreach stays typed.</summary>
        public new virtual System.Collections.Generic.IEnumerator<Control> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return this[i];
        }

        private void Adopt(Control child)
        {
            ArgumentNullException.ThrowIfNull(child);
            child.Parent?.RemoveChild(child);
            child.Parent = _owner;
        }

        private void Release(Control child)
        {
            if (child.Parent == _owner)
                child.Parent = null;
        }

        public virtual new void Add(Control child)
        {
            Adopt(child);
            base.Add(child);
        }

        /// <summary>Adds controls (cf. ControlCollection.AddRange(Control[])).</summary>
        public virtual void AddRange(Control[] controls)
        {
            ArgumentNullException.ThrowIfNull(controls);
            foreach (Control child in controls)
                Add(child);
        }

        public virtual new void Insert(int index, Control child)
        {
            Adopt(child);
            base.Insert(index, child);
        }

        /// <summary>Removes a control (cf. ControlCollection.Remove).</summary>
        public virtual new void Remove(Control child)
        {
            int index = IndexOf(child);
            if (index < 0)
                return;
            RemoveAt(index);
        }

        public virtual new void RemoveAt(int index)
        {
            Release(this[index]);
            base.RemoveAt(index);
        }

        public virtual new void Clear()
        {
            foreach (Control child in this)
                Release(child);
            base.Clear();
        }

        public virtual void SetChildIndex(Control child, int newIndex)
        {
            ArgumentNullException.ThrowIfNull(child);
            int old = base.IndexOf(child);
            if (old < 0)
                throw new ArgumentException("Control is not a child control.", nameof(child));
            base.RemoveAt(old);
            base.Insert(Math.Clamp(newIndex, 0, Count), child);
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _name = value;
        }
    }

    private int _x;
    private int _y;
    private int _width;
    private int _height;

    public int X
    {
        get => _x;
        set
        {
            if (_x == value)
                return;
            _x = value;
            OnLocationChanged(EventArgs.Empty);
            OnMove(EventArgs.Empty);
        }
    }

    public int Y
    {
        get => _y;
        set
        {
            if (_y == value)
                return;
            _y = value;
            OnLocationChanged(EventArgs.Empty);
            OnMove(EventArgs.Empty);
        }
    }

    public int Width
    {
        get => _width;
        set
        {
            if (_width == value)
                return;
            _width = value;
            RaiseSizeChanged();
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            if (_height == value)
                return;
            _height = value;
            RaiseSizeChanged();
        }
    }

    private void RaiseSizeChanged()
    {
        OnClientSizeChanged(EventArgs.Empty);
        OnSizeChanged(EventArgs.Empty);
        OnResize(EventArgs.Empty);
    }

    // Virtual like System.Windows.Forms.Control.Text: rewritten apps
    // routinely call the base slot (callvirt Control::get_Text) even on
    // derived instances, so hiding with new would split the value.
    public virtual string Text
    {
        get => _text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_text == value)
                return;
            _text = value;
            OnTextChanged(EventArgs.Empty);
        }
    }

    private bool _enabled = true;
    private bool _visible = true;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            OnEnabledChanged(EventArgs.Empty);
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
                return;
            _visible = value;
            OnVisibleChanged(EventArgs.Empty);
        }
    }

    public ControlCollection Children { get; }

    /// <summary>WinForms alias for Children (cf. Control.Controls).</summary>
    public ControlCollection Controls => Children;

    /// <summary>WinForms geometry aliases (cf. Control.Left/Top).</summary>
    public int Left
    {
        get => X;
        set => X = value;
    }

    public int Top
    {
        get => Y;
        set => Y = value;
    }

    /// <summary>Client size (cf. Control.ClientSize); borderless here.</summary>
    public WfDraw::System.Drawing.Size ClientSize
    {
        get => new(Width, Height);
        set
        {
            Width = value.Width;
            Height = value.Height;
        }
    }

    private WfDraw::System.Drawing.Color _backColor = DefaultBackColorValue;
    private WfDraw::System.Drawing.Color _foreColor = DefaultForeColorValue;
    private WfDraw::System.Drawing.Font _font = DefaultFont;

    /// <summary>Fill color (cf. Control.BackColor).</summary>
    public WfDraw::System.Drawing.Color BackColor
    {
        get => _backColor;
        set
        {
            if (_backColor.Equals(value))
                return;
            _backColor = value;
            OnBackColorChanged(EventArgs.Empty);
        }
    }

    /// <summary>Text color (cf. Control.ForeColor).</summary>
    public WfDraw::System.Drawing.Color ForeColor
    {
        get => _foreColor;
        set
        {
            if (_foreColor.Equals(value))
                return;
            _foreColor = value;
            OnForeColorChanged(EventArgs.Empty);
        }
    }

    /// <summary>Typeface (cf. Control.Font).</summary>
    public WfDraw::System.Drawing.Font Font
    {
        get => _font;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_font.Equals(value))
                return;
            _font = value;
            OnFontChanged(EventArgs.Empty);
        }
    }

    public static WfDraw::System.Drawing.Font DefaultFont { get; } =
        new("Microsoft Sans Serif", 8.25f);

    /// <summary>Stock fill color (cf. Control.DefaultBackColor).</summary>
    public static WfDraw::System.Drawing.Color DefaultBackColor => DefaultBackColorValue;

    /// <summary>Stock text color (cf. Control.DefaultForeColor).</summary>
    public static WfDraw::System.Drawing.Color DefaultForeColor => DefaultForeColorValue;

    private static WfDraw::System.Drawing.Color DefaultBackColorValue =>
        WfDraw::System.Drawing.Color.FromArgb(0xF0, 0xF0, 0xF0);

    private static WfDraw::System.Drawing.Color DefaultForeColorValue =>
        WfDraw::System.Drawing.Color.FromArgb(0, 0, 0);

    public void ResetBackColor() => BackColor = DefaultBackColor;
    public void ResetForeColor() => ForeColor = DefaultForeColor;

    /// <summary>Outer size (cf. Control.Size).</summary>
    public WfDraw::System.Drawing.Size Size
    {
        get => new(Width, Height);
        set
        {
            Width = value.Width;
            Height = value.Height;
        }
    }

    /// <summary>Outer position (cf. Control.Location).</summary>
    public WfDraw::System.Drawing.Point Location
    {
        get => new(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
        }
    }

    /// <summary>Outer bounds (cf. Control.Bounds).</summary>
    public WfDraw::System.Drawing.Rectangle Bounds
    {
        get => new(X, Y, Width, Height);
        set => SetBounds(value.X, value.Y, value.Width, value.Height, BoundsSpecified.All);
    }

    /// <summary>Client rectangle (cf. Control.ClientRectangle).</summary>
    public WfDraw::System.Drawing.Rectangle ClientRectangle => new(0, 0, Width, Height);

    /// <summary>Display rectangle (cf. Control.DisplayRectangle).</summary>
    public virtual WfDraw::System.Drawing.Rectangle DisplayRectangle => ClientRectangle;

    public int Bottom => Y + Height;
    public int Right => X + Width;

    public WfDraw::System.Drawing.Size MinimumSize { get; set; } = WfDraw::System.Drawing.Size.Empty;
    public WfDraw::System.Drawing.Size MaximumSize { get; set; } = WfDraw::System.Drawing.Size.Empty;
    public bool AutoSize { get; set; }
    public DockStyle Dock { get; set; } = DockStyle.None;
    public AnchorStyles Anchor { get; set; } = AnchorStyles.Top | AnchorStyles.Left;
    public Padding Margin { get; set; } = new(3);
    public Padding Padding { get; set; } = new(0);
    public int TabIndex { get; set; }
    public bool TabStop { get; set; } = true;
    public Cursor? Cursor { get; set; }
    /// <summary>Layout direction (cf. Control.RightToLeft). Mirrored layout
    /// is not applied yet; the value is stored for API compatibility.</summary>
    public RightToLeft RightToLeft { get; set; } = RightToLeft.Inherit;
    public bool AllowDrop { get; set; }

    protected bool DoubleBuffered { get; set; }
    protected bool ResizeRedraw { get; set; }
    protected ControlStyles StyleFlags { get; private set; }

    public bool Capture { get; set; }
    public virtual bool Focused { get; set; }
    public bool ContainsFocus { get; internal set; }
    public bool IsDisposed { get; private set; }
    public virtual bool IsHandleCreated => _handleCreated;
    private bool _handleCreated;

    /// <summary>Native handle (cf. Control.Handle): always zero, there is no
    /// OS window; the canvas backend owns drawing.</summary>
    public IntPtr Handle => IntPtr.Zero;

    public object? Tag { get; set; }

    public static Keys ModifierKeys { get; internal set; }
    public static MouseButtons MouseButtons { get; internal set; }
    public static WfDraw::System.Drawing.Point MousePosition { get; internal set; }
    public static bool CheckForIllegalCrossThreadCalls { get; set; } = true;

    /// <summary>True when the caller is off the creating thread (cf.
    /// Control.InvokeRequired).</summary>
    public virtual bool InvokeRequired =>
        Environment.CurrentManagedThreadId != _creatingThreadId;

    public WfDraw::System.Drawing.Size PreferredSize => GetPreferredSize(WfDraw::System.Drawing.Size.Empty);

    public virtual WfDraw::System.Drawing.Size GetPreferredSize(WfDraw::System.Drawing.Size proposedSize) =>
        proposedSize.IsEmpty ? new(Width, Height) : proposedSize;

    /// <summary>Owning parent, set by AddChild (cf. Control.Parent).</summary>
    public Control? Parent { get; private set; }

    /// <summary>Re-parents without touching child collections (cf. panel/row joins).</summary>
    internal void SetParent(Control? parent) => Parent = parent;

    /// <summary>Window-creation parameters (cf. Control.CreateParams).</summary>
    protected virtual CreateParams CreateParams => new();

    public event EventHandler? Click;
    public event EventHandler? DoubleClick;
    public event MouseEventHandler? MouseDown;
    public event MouseEventHandler? MouseUp;
    public event MouseEventHandler? MouseMove;
    public event EventHandler? MouseEnter;
    public event EventHandler? MouseLeave;
    public event MouseEventHandler? MouseWheel;
    public event KeyEventHandler? KeyDown;
    public event KeyEventHandler? KeyUp;
    public event KeyPressEventHandler? KeyPress;
    public event PreviewKeyDownEventHandler? PreviewKeyDown;
    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;
    public event EventHandler? Enter;
    public event EventHandler? Leave;
    public event EventHandler? Resize;
    public event EventHandler? SizeChanged;
    public event EventHandler? ClientSizeChanged;
    public event EventHandler? LocationChanged;
    public event EventHandler? TextChanged;
    public event EventHandler? VisibleChanged;
    public event EventHandler? EnabledChanged;
    public event EventHandler? BackColorChanged;
    public event EventHandler? ForeColorChanged;
    public event EventHandler? FontChanged;
    public event LayoutEventHandler? Layout;
    public event PaintEventHandler? Paint;
    public event CancelEventHandler? Validating;
    public event EventHandler? HandleCreated;
    public event EventHandler? HandleDestroyed;
    public event EventHandler? Move;
    public event DragEventHandler? DragDrop;
    public event DragEventHandler? DragEnter;
    public event EventHandler? DragLeave;
    public event DragEventHandler? DragOver;

    public void AddChild(Control child)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Parent?.RemoveChild(child);
        Children.Add(child);
        child.Parent = this;
    }

    public bool RemoveChild(Control child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!Children.Contains(child))
            return false;
        Children.Remove(child);
        child.Parent = null;
        return true;
    }

    public void Show() => Visible = true;
    public void Hide() => Visible = false;

    /// <summary>Take focus (cf. Control.Focus); headless, the host owns real
    /// focus routing and calls this when it moves focus.</summary>
    public virtual bool Focus()
    {
        if (!Enabled)
            return false;
        Focused = true;
        OnEnter(EventArgs.Empty);
        OnGotFocus(EventArgs.Empty);
        return true;
    }

    /// <summary>Takes focus (cf. Control.Select).</summary>
    public void Select() => Focus();

    /// <summary>Directional focus (cf. Control.Select(bool, bool)).</summary>
    protected virtual bool Select(bool directed, bool forward) => Focus();

    private int _layoutSuspended;

    public void SuspendLayout() => _layoutSuspended++;
    public void ResumeLayout() => ResumeLayout(true);

    public void ResumeLayout(bool performLayout)
    {
        if (_layoutSuspended > 0)
            _layoutSuspended--;
        if (performLayout && _layoutSuspended == 0)
            PerformLayout();
    }

    public void PerformLayout() => OnLayout(new LayoutEventArgs(this, null));
    public void PerformLayout(Control affectedControl, string? property) =>
        OnLayout(new LayoutEventArgs(affectedControl, property));

    public void SetBounds(int x, int y, int width, int height) =>
        SetBounds(x, y, width, height, BoundsSpecified.All);

    public void SetBounds(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if ((specified & BoundsSpecified.X) != 0)
            X = x;
        if ((specified & BoundsSpecified.Y) != 0)
            Y = y;
        if ((specified & BoundsSpecified.Width) != 0)
            Width = width;
        if ((specified & BoundsSpecified.Height) != 0)
            Height = height;
        SetBoundsCore(x, y, width, height, specified);
    }

    protected virtual void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
    }

    public bool GetStyle(ControlStyles flag) => (StyleFlags & flag) == flag;

    public void SetStyle(ControlStyles flag, bool value)
    {
        StyleFlags = value ? StyleFlags | flag : StyleFlags & ~flag;
        if (value && (flag & (ControlStyles.ResizeRedraw | ControlStyles.Opaque)) != 0)
            ResizeRedraw = true;
    }

    /// <summary>Mark for repaint (cf. Control.Invalidate). The canvas host
    /// repaints continuously, so invalidation needs no dirty tracking.</summary>
    public virtual void Invalidate() => OnInvalidated();

    /// <summary>Mark for repaint, optionally including children (cf. Invalidate(bool)).</summary>
    public virtual void Invalidate(bool invalidateChildren) => OnInvalidated();

    public virtual void Invalidate(WfDraw::System.Drawing.Rectangle rc) => OnInvalidated();
    public virtual void Invalidate(WfDraw::System.Drawing.Rectangle rc, bool invalidateChildren) => OnInvalidated();

    protected virtual void OnInvalidated()
    {
    }

    /// <summary>Repaint now (cf. Control.Refresh).</summary>
    public virtual void Refresh() => Invalidate();
    /// <summary>Process pending paints (cf. Control.Update): no-op, the host
    /// pumps painting; kept so rewritten call sites resolve.</summary>
    public virtual void Update()
    {
    }

    /// <summary>Parent chain to screen-agnostic coordinates (cf. PointToClient).</summary>
    public WfDraw::System.Drawing.Point PointToClient(WfDraw::System.Drawing.Point p)
    {
        int x = p.X;
        int y = p.Y;
        for (Control? node = this; node is not null; node = node.Parent)
        {
            x -= node.X;
            y -= node.Y;
        }

        return new(x, y);
    }

    public WfDraw::System.Drawing.Point PointToScreen(WfDraw::System.Drawing.Point p)
    {
        int x = p.X;
        int y = p.Y;
        for (Control? node = this; node is not null; node = node.Parent)
        {
            x += node.X;
            y += node.Y;
        }

        return new(x, y);
    }

    public WfDraw::System.Drawing.Rectangle RectangleToClient(WfDraw::System.Drawing.Rectangle r)
    {
        WfDraw::System.Drawing.Point p = PointToClient(new(r.X, r.Y));
        return new(p.X, p.Y, r.Width, r.Height);
    }

    public WfDraw::System.Drawing.Rectangle RectangleToScreen(WfDraw::System.Drawing.Rectangle r)
    {
        WfDraw::System.Drawing.Point p = PointToScreen(new(r.X, r.Y));
        return new(p.X, p.Y, r.Width, r.Height);
    }

    /// <summary>Nearest owning Form (cf. Control.FindForm).</summary>
    public Form? FindForm()
    {
        for (Control? node = this; node is not null; node = node.Parent)
        {
            if (node is Form form)
                return form;
        }

        return null;
    }

    protected virtual void CreateHandle() => _handleCreated = true;
    public void CreateControl() => CreateHandle();

    protected override void Dispose(bool disposing)
    {
        MarkDisposing();
        IsDisposed = true;
        _handleCreated = false;
        base.Dispose(disposing);
    }

    private sealed class CompletedInvoke : IAsyncResult
    {
        public CompletedInvoke(object? result)
        {
            AsyncState = result;
        }

        public object? AsyncState { get; }
        public WaitHandle AsyncWaitHandle => _done;
        private readonly ManualResetEvent _done = new(true);
        public bool CompletedSynchronously => true;
        public bool IsCompleted => true;
    }

    /// <summary>Run on the creating thread (cf. Control.Invoke): inline when
    /// already there (or with no pump to marshal through), otherwise queued
    /// for the Run pump with the caller blocked.</summary>
    public object? Invoke(Delegate method) => Invoke(method, null);

    public object? Invoke(Delegate method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!InvokeRequired || !Application.Pumping || Application.IsPumpThread)
            return method.DynamicInvoke(args ?? []);
        var completion = new PendingInvoke();
        object?[] captured = args ?? [];
        Application.PostToUiThread(() => method.DynamicInvoke(captured), completion);
        return completion.End();
    }

    /// <summary>Run an action on the creating thread (cf. Control.Invoke(Action)).</summary>
    public void Invoke(Action method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!InvokeRequired || !Application.Pumping || Application.IsPumpThread)
        {
            method();
            return;
        }
        var completion = new PendingInvoke();
        Application.PostToUiThread(() =>
        {
            method();
            return null;
        }, completion);
        completion.End();
    }

    public IAsyncResult BeginInvoke(Delegate method) => BeginInvoke(method, null);

    public IAsyncResult BeginInvoke(Delegate method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!InvokeRequired || !Application.Pumping || Application.IsPumpThread)
            return new CompletedInvoke(Invoke(method, args));
        var completion = new PendingInvoke();
        object?[] captured = args ?? [];
        Application.PostToUiThread(() => method.DynamicInvoke(captured), completion);
        return completion;
    }

    /// <summary>Queue an action on the creating thread (cf.
    /// Control.BeginInvoke(Action)).</summary>
    public IAsyncResult BeginInvoke(Action method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!InvokeRequired || !Application.Pumping || Application.IsPumpThread)
        {
            method();
            return new CompletedInvoke(null);
        }
        var completion = new PendingInvoke();
        Application.PostToUiThread(() =>
        {
            method();
            return null;
        }, completion);
        return completion;
    }

    public object? EndInvoke(IAsyncResult asyncResult)
    {
        ArgumentNullException.ThrowIfNull(asyncResult);
        if (asyncResult is PendingInvoke pending)
            return pending.End();
        return asyncResult.AsyncState;
    }

    /// <summary>Start a drag (cf. Control.DoDragDrop): headless, records the
    /// allowed effect; the host drives real drags when it pumps input.</summary>
    public DragDropEffects DoDragDrop(object data, DragDropEffects allowedEffects) =>
        DoDragDrop(new DataObject(data), allowedEffects);

    public DragDropEffects DoDragDrop(IDataObject data, DragDropEffects allowedEffects) =>
        DragDropEffects.None;

    protected virtual void WndProc(ref Message message)
    {
    }

    protected virtual void OnClick(EventArgs e) => Click?.Invoke(this, e);
    protected virtual void OnDoubleClick(EventArgs e) => DoubleClick?.Invoke(this, e);
    protected virtual void OnMouseDown(MouseEventArgs e) => MouseDown?.Invoke(this, e);
    protected virtual void OnMouseDoubleClick(MouseEventArgs e) => DoubleClick?.Invoke(this, EventArgs.Empty);
    protected virtual void OnMouseUp(MouseEventArgs e) => MouseUp?.Invoke(this, e);
    protected virtual void OnMouseMove(MouseEventArgs e) => MouseMove?.Invoke(this, e);
    protected virtual void OnMouseEnter(EventArgs e) => MouseEnter?.Invoke(this, e);
    protected virtual void OnMouseLeave(EventArgs e) => MouseLeave?.Invoke(this, e);
    protected virtual void OnMouseHover(EventArgs e)
    {
    }

    protected virtual void OnMouseWheel(MouseEventArgs e) => MouseWheel?.Invoke(this, e);
    protected virtual void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(this, e);
    protected virtual void OnKeyUp(KeyEventArgs e) => KeyUp?.Invoke(this, e);
    protected virtual void OnKeyPress(KeyPressEventArgs e) => KeyPress?.Invoke(this, e);
    protected virtual void OnPreviewKeyDown(PreviewKeyDownEventArgs e) => PreviewKeyDown?.Invoke(this, e);
    protected virtual void OnGotFocus(EventArgs e) => GotFocus?.Invoke(this, e);
    protected virtual void OnLostFocus(EventArgs e) => LostFocus?.Invoke(this, e);
    protected virtual void OnEnter(EventArgs e) => Enter?.Invoke(this, e);
    protected virtual void OnLeave(EventArgs e) => Leave?.Invoke(this, e);
    protected virtual void OnResize(EventArgs e) => Resize?.Invoke(this, e);
    protected virtual void OnClientSizeChanged(EventArgs e) => ClientSizeChanged?.Invoke(this, e);
    protected virtual void OnSizeChanged(EventArgs e) => SizeChanged?.Invoke(this, e);
    protected virtual void OnTextChanged(EventArgs e) => TextChanged?.Invoke(this, e);
    protected virtual void OnVisibleChanged(EventArgs e) => VisibleChanged?.Invoke(this, e);
    protected virtual void OnEnabledChanged(EventArgs e) => EnabledChanged?.Invoke(this, e);
    protected virtual void OnBackColorChanged(EventArgs e) => BackColorChanged?.Invoke(this, e);
    protected virtual void OnForeColorChanged(EventArgs e) => ForeColorChanged?.Invoke(this, e);
    protected virtual void OnFontChanged(EventArgs e) => FontChanged?.Invoke(this, e);
    protected virtual void OnLocationChanged(EventArgs e) => LocationChanged?.Invoke(this, e);
    protected virtual void OnMove(EventArgs e) => Move?.Invoke(this, e);

    protected virtual void OnLayout(LayoutEventArgs e) => Layout?.Invoke(this, e);

    protected virtual void OnPaint(PaintEventArgs e) => Paint?.Invoke(this, e);
    protected virtual void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected virtual void OnValidating(CancelEventArgs e) => Validating?.Invoke(this, e);
    protected virtual void OnHandleCreated(EventArgs e) => HandleCreated?.Invoke(this, e);
    protected virtual void OnHandleDestroyed(EventArgs e) => HandleDestroyed?.Invoke(this, e);
    protected virtual void OnDragDrop(DragEventArgs e) => DragDrop?.Invoke(this, e);
    protected virtual void OnDragEnter(DragEventArgs e) => DragEnter?.Invoke(this, e);
    protected virtual void OnDragLeave(EventArgs e) => DragLeave?.Invoke(this, e);
    protected virtual void OnDragOver(DragEventArgs e) => DragOver?.Invoke(this, e);

    /// <summary>Compat raiser (cf. Control.OnParentVisibleChanged): kept so
    /// rewritten overrides that call it resolve.</summary>
    protected virtual void OnParentVisibleChanged(EventArgs e)
    {
    }

    protected virtual void OnSystemColorsChanged(EventArgs e)
    {
    }

    /// <summary>
    /// Render self then children, accumulating offsets down the tree. Named
    /// Render (not Paint) because Control.Paint is the paint EVENT (cf.
    /// System.Windows.Forms.Control.Paint), raised via OnPaint.
    /// </summary>
    public virtual void Render(ICanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        PaintAt(canvas, 0, 0);
    }

    internal void PaintAt(ICanvas canvas, int dx, int dy)
    {
        if (!Visible)
            return;
        int ax = dx + X;
        int ay = dy + Y;
        PaintSelf(canvas, ax, ay);
        PaintChildren(canvas, ax, ay);
    }

    protected virtual void PaintChildren(ICanvas canvas, int ax, int ay)
    {
        foreach (Control child in Children)
            PaintChild(canvas, child, ax, ay);
    }

    protected void PaintChild(ICanvas canvas, Control child, int ax, int ay) =>
        child.PaintAt(canvas, ax, ay);

    /// <summary>Draw this node at absolute canvas position (ax, ay). Default: container, draws nothing.</summary>
    protected virtual void PaintSelf(ICanvas canvas, int ax, int ay)
    {
    }

    /// <summary>
    /// Deepest visible child containing (x, y), else self; null when (x, y)
    /// is outside these bounds or this node is invisible. Coordinates are in
    /// the parent's space. Disabled or invisible children are skipped.
    /// </summary>
    public Control? HitTest(int x, int y)
    {
        if (!Visible)
            return null;
        if (x < X || y < Y || x >= X + Width || y >= Y + Height)
            return null;
        int lx = x - X;
        int ly = y - Y;
        (int cx, int cy) = ToClientPoint(lx, ly);
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            Control child = Children[i];
            if (!child.Visible || !child.Enabled)
                continue;
            Control? hit = child.HitTest(cx, cy);
            if (hit is not null)
                return hit;
        }

        return this;
    }

    /// <summary>Maps control-local point to child space. Default: identity.</summary>
    protected virtual (int cx, int cy) ToClientPoint(int lx, int ly) => (lx, ly);

    /// <summary>Inverse of <see cref="ToClientPoint"/> (for overlay bounds).</summary>
    protected virtual (int lx, int ly) ToLocalPoint(int cx, int cy) => (cx, cy);

    /// <summary>This-local point to parent-local (cf. PointToScreen, one level).</summary>
    public (int Px, int Py) PointToParent(int x, int y)
    {
        int px = x + X;
        int py = y + Y;
        return Parent is null ? (px, py) : Parent.ToLocalPoint(px, py);
    }

    /// <summary>
    /// Origin of this control in <paramref name="ancestor"/>'s local space.
    /// Null when <paramref name="ancestor"/> is not an ancestor.
    /// </summary>
    public (int Ax, int Ay)? AbsolutePosition(Control ancestor)
    {
        int ax = 0;
        int ay = 0;
        for (Control? node = this; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, ancestor))
                return (ax, ay);
            (ax, ay) = node.PointToParent(ax, ay);
        }

        return null;
    }

    /// <summary>Hover in local space; (-1,-1) means leave. Default: ignore.</summary>
    public virtual void HandleHoverLocal(int localX, int localY)
    {
    }

    /// <summary>
    /// Deepest visible child containing (x, y) plus the point in that
    /// control's local space (cf. hit-testing with client coordinates).
    /// Null when (x, y) is outside these bounds. Coordinates are in the
    /// parent's space.
    /// </summary>
    public virtual (Control Control, int LocalX, int LocalY)? HitTestDeep(int x, int y)
    {
        if (!Visible)
            return null;
        if (x < X || y < Y || x >= X + Width || y >= Y + Height)
            return null;
        int lx = x - X;
        int ly = y - Y;
        (int cx, int cy) = ToClientPoint(lx, ly);
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            Control child = Children[i];
            if (!child.Visible || !child.Enabled)
                continue;
            var deep = child.HitTestDeep(cx, cy);
            if (deep.HasValue)
                return deep;
        }

        return (this, lx, ly);
    }

    /// <summary>
    /// Position-sensitive click in local space. Default ignores the position
    /// (cf. controls that treat the whole bounds as the button).
    /// </summary>
    public virtual void HandleClickLocal(int localX, int localY) => PerformClick();

    /// <summary>Raise <see cref="Click"/> only when enabled and visible (cf. Button.OnClick).</summary>
    public virtual void PerformClick()
    {
        if (!Enabled || !Visible)
            return;
        OnClick(EventArgs.Empty);
    }
}

/// <summary>
/// Push-button node (cf. System.Windows.Forms.Button). Implements the
/// FlatStyle rules: Standard is a raised face that sinks and shifts content
/// (+1,+1) when pressed; Flat/Paint uses border + hover/pressed washes;
/// Popup is borderless until hot; System (or themed) uses the visual-styles
/// faces with hot/pressed/default states. Default buttons get the extra
/// frame (black classic, blue themed); focused buttons get the dotted rect.
/// </summary>
public class Button : ButtonBase, IButtonControl
{
    public Button(string name = "")
        : base(name)
    {
    }

    public Button()
        : this("")
    {
    }

    public bool IsDefault { get; set; }
    public DialogResult DialogResult { get; set; } = DialogResult.None;

    public void NotifyDefault(bool value)
    {
        IsDefault = value;
    }
    public Draw.ContentAlignment TextAlign { get; set; } = Draw.ContentAlignment.MiddleCenter;
    public bool UseMnemonic { get; set; } = true;
    public override bool Focused { get; set; }

    /// <summary>Hover/pressed states, driven by the host (cf. mouse capture).</summary>
    public bool IsHot { get; set; }
    public bool IsPressed { get; set; }

    public uint FlatBorderColor { get; set; } = SystemColors.ControlDark;
    public uint FlatMouseOverBackColor { get; set; } = 0xE8E8E8FF;
    public uint FlatMouseDownBackColor { get; set; } = SystemColors.ControlDark;
    public uint FlatCheckedBackColor { get; set; } = 0xD8D8D8FF;

    public string FontCss { get; set; } = Palette.UiFontCss;

    public override void HandleHoverLocal(int localX, int localY) =>
        IsHot = localX >= 0 && Enabled;

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        // FlatStyle.Standard always paints classic, even when themed.
        bool themed = RenderingConfig.UseVisualStyles && FlatStyle != FlatStyle.Standard;
        bool pressed = IsPressed && Enabled;
        // Keyboard focus draws the dotted rect, not the hot wash.
        bool hot = IsHot && Enabled && !pressed;
        bool contentShift = pressed && !themed;

        if (themed && FlatStyle == FlatStyle.System)
            PaintThemed(canvas, ax, ay, pressed, hot);
        else if (FlatStyle == FlatStyle.Flat)
            PaintFlat(canvas, ax, ay, pressed, hot);
        else if (FlatStyle == FlatStyle.Popup)
            PaintPopup(canvas, ax, ay, pressed, hot);
        else
            PaintStandard(canvas, ax, ay, pressed);

        int dx = contentShift ? 1 : 0;
        int dy = contentShift ? 1 : 0;
        uint ink = !Enabled ? SystemColors.GrayText
            : themed && IsDefault && Enabled ? SystemColors.ControlText
            : SystemColors.ControlText;
        TextRules.DrawAlignedText(canvas, Text, UseMnemonic, TextAlign,
            ax + 4 + dx, ay + 2 + dy, Width - 8, Height - 4,
            FontCss, ink, !Enabled);

        if (Focused && Enabled)
            ControlPaintGlyphs.DrawFocusRectangle(canvas, ax + 4, ay + 4, Width - 8, Height - 8);
    }

    private void PaintStandard(ICanvas canvas, int ax, int ay, bool pressed)
    {
        canvas.FillRectangle(ax, ay, Width, Height, SystemColors.Control);
        if (IsDefault && Enabled)
        {
            // Extra 1px black frame outside the raised edge.
            ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
                SystemColors.Black, ControlPaintEdges.LineBorderStyle.Solid);
            ControlPaintEdges.DrawBorder(canvas, ax + 1, ay + 1, Width - 2, Height - 2,
                SystemColors.Control,
                pressed ? ControlPaintEdges.LineBorderStyle.Inset : ControlPaintEdges.LineBorderStyle.Outset);
        }
        else
        {
            ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
                SystemColors.Control,
                pressed ? ControlPaintEdges.LineBorderStyle.Inset : ControlPaintEdges.LineBorderStyle.Outset);
        }
    }

    private void PaintFlat(ICanvas canvas, int ax, int ay, bool pressed, bool hot)
    {
        uint face = SystemColors.Control;
        if (pressed)
            face = FlatMouseDownBackColor;
        else if (hot)
            face = FlatMouseOverBackColor;
        canvas.FillRectangle(ax, ay, Width, Height, face);
        ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
            FlatBorderColor, ControlPaintEdges.LineBorderStyle.Solid);
    }

    private void PaintPopup(ICanvas canvas, int ax, int ay, bool pressed, bool hot)
    {
        canvas.FillRectangle(ax, ay, Width, Height, SystemColors.Control);
        if (pressed)
            ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
                SystemColors.Control, ControlPaintEdges.LineBorderStyle.Inset);
        else if (hot)
            ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
                SystemColors.Control, ControlPaintEdges.LineBorderStyle.Outset);
    }

    private void PaintThemed(ICanvas canvas, int ax, int ay, bool pressed, bool hot)
    {
        uint fill = !Enabled ? SystemColors.ThemedButtonDisabledFill
            : pressed ? SystemColors.ThemedButtonPressedFill
            : hot ? SystemColors.ThemedButtonHotFill
            : SystemColors.ThemedButtonFill;
        uint border = !Enabled ? SystemColors.ThemedButtonDisabledBorder
            : pressed ? SystemColors.ThemedButtonPressedBorder
            : hot || IsDefault ? SystemColors.ThemedButtonHotBorder
            : SystemColors.ThemedButtonBorder;
        canvas.FillRectangle(ax, ay, Width, Height, fill);
        ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
            border, ControlPaintEdges.LineBorderStyle.Solid);
    }
}

/// <summary>
/// Text node (cf. System.Windows.Forms.Label, cf. label1 in Form1.Designer.cs).
/// Implements TextAlign (nine-way, backend-measured), mnemonics, and the
/// BorderStyle frame (None/FixedSingle/Fixed3D).
/// </summary>
public class Label : Control
{
    private string _fontCss = Palette.UiFontCss;

    public Label(string name = "")
        : base(name)
    {
    }

    public Label()
        : this("")
    {
    }

    public string FontCss
    {
        get => _fontCss;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _fontCss = value;
        }
    }

    public Draw.ContentAlignment TextAlign { get; set; } = Draw.ContentAlignment.TopLeft;
    public bool UseMnemonic { get; set; } = true;
    public BorderStyle BorderStyle { get; set; } = BorderStyle.None;

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        switch (BorderStyle)
        {
            case BorderStyle.FixedSingle:
                ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
                    SystemColors.ControlDarkDark, ControlPaintEdges.LineBorderStyle.Solid);
                break;
            case BorderStyle.Fixed3D:
                ControlPaintEdges.DrawEdge(canvas, ax, ay, Width, Height,
                    ControlPaintEdges.EdgeStyle.Sunken);
                break;
        }

        int pad = BorderStyle == BorderStyle.None ? 0 : 2;
        TextRules.DrawAlignedText(canvas, Text, UseMnemonic, TextAlign,
            ax + pad, ay + pad, Width - (2 * pad), Height - (2 * pad),
            FontCss, Palette.LabelText, !Enabled);
    }
}

/// <summary>
/// Check box (cf. System.Windows.Forms.CheckBox): 13px glyph at CheckAlign,
/// text with mnemonic, ThreeState cycling on click, Appearance.Button mode.
/// </summary>
public class CheckBox : ButtonBase
{
    public const int GlyphSize = 13;
    public const int TextGap = 4;

    public CheckBox()
        : this("")
    {
    }

    public CheckBox(string name = "")
        : base(name)
    {
    }

    public CheckState CheckState { get; set; } = CheckState.Unchecked;
    public bool ThreeState { get; set; }
    public bool AutoCheck { get; set; } = true;
    public Appearance Appearance { get; set; } = Appearance.Normal;
    public Draw.ContentAlignment CheckAlign { get; set; } = Draw.ContentAlignment.MiddleLeft;
    public Draw.ContentAlignment TextAlign { get; set; } = Draw.ContentAlignment.MiddleLeft;
    public bool UseMnemonic { get; set; } = true;
    public override bool Focused { get; set; }
    public bool IsHot { get; set; }
    public bool IsPressed { get; set; }
    public string FontCss { get; set; } = Palette.UiFontCss;

    public override void HandleHoverLocal(int localX, int localY) =>
        IsHot = localX >= 0 && Enabled;

    public bool Checked
    {
        get => CheckState != CheckState.Unchecked;
        set => CheckState = value ? CheckState.Checked : CheckState.Unchecked;
    }

    public override void PerformClick()
    {
        if (!Enabled || !Visible)
            return;
        if (AutoCheck)
            CycleCheckState();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        base.PerformClick();
    }

    protected virtual void CycleCheckState()
    {
        CheckState = CheckState switch
        {
            CheckState.Unchecked => CheckState.Checked,
            CheckState.Checked => ThreeState ? CheckState.Indeterminate : CheckState.Unchecked,
            _ => CheckState.Unchecked,
        };
    }

    public event EventHandler? CheckedChanged;

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        if (Appearance == Appearance.Button)
        {
            PaintAsButton(canvas, ax, ay);
            return;
        }

        bool themed = RenderingConfig.UseVisualStyles;
        (float gx, float gy) = TextRules.Anchor(CheckAlign, ax, ay, Width, Height);
        int glyphX = CheckAlign is Draw.ContentAlignment.TopLeft or Draw.ContentAlignment.MiddleLeft or Draw.ContentAlignment.BottomLeft
            ? (int)gx : (int)gx - GlyphSize;
        int glyphY = CheckAlign is Draw.ContentAlignment.TopLeft or Draw.ContentAlignment.TopCenter or Draw.ContentAlignment.TopRight
            ? (int)gy : CheckAlign is Draw.ContentAlignment.BottomLeft or Draw.ContentAlignment.BottomCenter or Draw.ContentAlignment.BottomRight
                ? (int)gy - GlyphSize : (int)gy - (GlyphSize / 2);

        PaintGlyphBox(canvas, glyphX, glyphY, themed);

        // Text sits beside the glyph on the glyph's outer side; default
        // left-anchored layouts (the common case) are exact.
        float tx = gx + (glyphX == (int)gx ? GlyphSize + TextGap : 0);
        float tw = glyphX == (int)gx ? Width - (GlyphSize + TextGap) : Width;
        TextRules.DrawAlignedText(canvas, Text, UseMnemonic, TextAlign,
            tx, ay, tw, Height, FontCss, SystemColors.ControlText, !Enabled);

        if (Focused && Enabled)
            ControlPaintGlyphs.DrawFocusRectangle(canvas, ax + 1, ay + 1, Width - 2, Height - 2);
    }

    protected virtual void PaintGlyphBox(ICanvas canvas, int x, int y, bool themed)
    {
        if (themed)
            PaintThemedGlyph(canvas, x, y);
        else
            ControlPaintGlyphs.DrawCheckBox(canvas, x, y, GlyphSize,
                CheckState, flat: false, disabled: !Enabled);
    }

    private void PaintThemedGlyph(ICanvas canvas, int x, int y)
    {
        bool hot = IsHot && Enabled;
        canvas.FillRectangle(x, y, GlyphSize, GlyphSize, SystemColors.Window);
        uint border = !Enabled ? SystemColors.ThemedGlyphDisabledBorder
            : hot ? SystemColors.ThemedGlyphHotBorder
            : SystemColors.ThemedGlyphBorder;
        ControlPaintEdges.DrawBorder(canvas, x, y, GlyphSize, GlyphSize,
            border, ControlPaintEdges.LineBorderStyle.Solid);
        if (CheckState == CheckState.Checked)
            ControlPaintGlyphs.DrawCheckBox(canvas, x, y, GlyphSize,
                CheckState.Checked, flat: true, disabled: !Enabled);
        else if (CheckState == CheckState.Indeterminate)
            canvas.FillRectangle(x + 3, y + 3, GlyphSize - 6, GlyphSize - 6,
                !Enabled ? SystemColors.ControlDark : SystemColors.ControlText);
    }

    private void PaintAsButton(ICanvas canvas, int ax, int ay)
    {
        // Appearance.Button: sunken when checked (cf. ButtonBaseAdapter).
        // The proxy paints at this control's absolute position.
        var proxy = new Button(Name)
        {
            X = ax, Y = ay, Width = Width, Height = Height,
            Text = Text, Enabled = Enabled,
            FlatStyle = FlatStyle.Standard,
            IsPressed = Checked,
            IsHot = IsHot,
            Focused = Focused,
        };
        proxy.Render(canvas);
    }
}

/// <summary>
/// Radio button (cf. System.Windows.Forms.RadioButton): circular glyph,
/// clicking checks (never unchecks), no ThreeState.
/// </summary>
public class RadioButton : CheckBox
{
    public RadioButton()
        : this("")
    {
    }

    public RadioButton(string name = "")
        : base(name)
    {
    }

    protected override void CycleCheckState()
    {
        CheckState = CheckState.Checked;
        // Mutual exclusion within the container (cf. radio button groups).
        if (Parent is not null)
        {
            foreach (Control sibling in Parent.Children)
            {
                if (!ReferenceEquals(sibling, this) && sibling is RadioButton other)
                    other.CheckState = CheckState.Unchecked;
            }
        }
    }

    protected override void PaintGlyphBox(ICanvas canvas, int x, int y, bool themed)
    {
        if (themed)
        {
            bool hot = IsHot && Enabled;
            uint border = !Enabled ? SystemColors.ThemedGlyphDisabledBorder
                : hot ? SystemColors.ThemedGlyphHotBorder
                : SystemColors.ThemedGlyphBorder;
            // Ring: outer disc, inner window disc.
            canvas.FillRoundedRectangle(x, y, GlyphSize, GlyphSize, GlyphSize / 2f, border);
            canvas.FillRoundedRectangle(x + 1, y + 1, GlyphSize - 2, GlyphSize - 2,
                (GlyphSize - 2) / 2f, !Enabled ? SystemColors.Control : SystemColors.Window);
            if (Checked)
            {
                int d = 5;
                int o = (GlyphSize - d) / 2;
                canvas.FillRoundedRectangle(x + o, y + o, d, d, d / 2f,
                    !Enabled ? SystemColors.ControlDark : SystemColors.ControlText);
            }
        }
        else
        {
            ControlPaintGlyphs.DrawRadioButton(canvas, x, y, GlyphSize,
                Checked, disabled: !Enabled);
        }
    }
}
