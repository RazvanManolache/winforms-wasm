extern alias WfDraw;

namespace WfWasm.Shim;

using Draw = WfDraw::System.Drawing;

/// <summary>Strip item (cf. System.Windows.Forms.ToolStripItem): text,
/// appearance, owner wiring, click. Image members wait for the Drawing
/// Image backend; everything else resolves and behaves.</summary>
public class ToolStripItem : System.ComponentModel.Component
{
    public ToolStripItem()
    {
    }

    public ToolStripItem(string text)
    {
        Text = text;
    }

    private string _text = "";
    private bool _enabled = true;
    private bool _visible = true;
    private bool _available = true;

    public virtual string Text
    {
        get => _text;
        set
        {
            if (_text == value)
                return;
            _text = value;
            OnTextChanged(EventArgs.Empty);
        }
    }

    public string Name { get; set; } = "";
    public object? Tag { get; set; }

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

    public bool Available
    {
        get => _available;
        set
        {
            if (_available == value)
                return;
            _available = value;
            OnAvailableChanged(EventArgs.Empty);
        }
    }

    public bool AutoSize { get; set; } = true;
    public bool AutoToolTip { get; set; }
    public string ToolTipText { get; set; } = "";
    public ToolStripItemDisplayStyle DisplayStyle { get; set; } = ToolStripItemDisplayStyle.ImageAndText;
    public ToolStripItemAlignment Alignment { get; set; } = ToolStripItemAlignment.Left;
    public ToolStripTextDirection TextDirection { get; set; } = ToolStripTextDirection.Horizontal;
    public ToolStripItemOverflow Overflow { get; set; } = ToolStripItemOverflow.AsNeeded;
    public RightToLeft RightToLeft { get; set; } = RightToLeft.Inherit;
    public bool RightToLeftAutoMirrorImage { get; set; }
    public bool DoubleClickEnabled { get; set; }
    public Draw.ContentAlignment ImageAlign { get; set; } = Draw.ContentAlignment.MiddleCenter;
    public ToolStripItemImageScaling ImageScaling { get; set; } = ToolStripItemImageScaling.SizeToFit;
    public Draw.ContentAlignment TextAlign { get; set; } = Draw.ContentAlignment.MiddleCenter;
    public TextImageRelation TextImageRelation { get; set; } = TextImageRelation.ImageBeforeText;
    public AnchorStyles Anchor { get; set; } = AnchorStyles.Top | AnchorStyles.Left;
    public Padding Margin { get; set; } = new(0, 1, 0, 2);
    public Padding Padding { get; set; } = new(0);
    public WfDraw::System.Drawing.Image? Image { get; set; }
    public int ImageIndex { get; set; } = -1;
    public string ImageKey { get; set; } = "";

    public WfDraw::System.Drawing.Font Font { get; set; } = Control.DefaultFont;
    public WfDraw::System.Drawing.Color BackColor { get; set; } = WfDraw::System.Drawing.Color.Empty;
    public WfDraw::System.Drawing.Color ForeColor { get; set; } = WfDraw::System.Drawing.Color.FromArgb(0, 0, 0);

    protected virtual Padding DefaultMargin => new(0, 1, 0, 2);
    protected virtual Padding DefaultPadding => new(0);
    protected virtual WfDraw::System.Drawing.Size DefaultSize => new(23, 23);

    public int Width
    {
        get => Size.Width;
        set => Size = new(value, Size.Height);
    }

    public int Height
    {
        get => Size.Height;
        set => Size = new(Size.Width, value);
    }

    public WfDraw::System.Drawing.Size Size { get; set; } = new(23, 23);
    public WfDraw::System.Drawing.Rectangle Bounds { get; set; }

    public WfDraw::System.Drawing.Rectangle ContentRectangle
    {
        get
        {
            WfDraw::System.Drawing.Rectangle bounds = Bounds;
            return new(
                bounds.X + Padding.Left,
                bounds.Y + Padding.Top,
                Math.Max(0, bounds.Width - Padding.Horizontal),
                Math.Max(0, bounds.Height - Padding.Vertical));
        }
    }

    public bool Selected { get; internal set; }
    public bool Pressed { get; internal set; }
    public bool IsOnDropDown { get; internal set; }
    public ToolStrip? Owner { get; internal set; }
    public ToolStrip? Parent { get; internal set; }
    public ToolStripItemPlacement Placement { get; internal set; } = ToolStripItemPlacement.Main;
    public bool IsDisposed { get; private set; }

    public virtual WfDraw::System.Drawing.Size GetPreferredSize(WfDraw::System.Drawing.Size constrainingSize)
    {
        // Approximation until text measurement: font-height rows, ~0.6em
        // per character, never smaller than the default item size.
        WfDraw::System.Drawing.Size basis = DefaultSize;
        if (!string.IsNullOrEmpty(Text))
        {
            int w = (int)(Text.Length * Font.Size * 0.6f) + Padding.Horizontal + 8;
            int h = Font.Height + Padding.Vertical + 6;
            basis = new(Math.Max(basis.Width, w), Math.Max(basis.Height, h));
        }

        if (!constrainingSize.IsEmpty)
            basis = new(Math.Min(basis.Width, constrainingSize.Width), Math.Min(basis.Height, constrainingSize.Height));
        return basis;
    }

    public void Invalidate()
    {
    }

    public virtual void PerformClick()
    {
        if (Enabled && Available && Visible)
            OnClick(EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }

    public event EventHandler? Click;
    public event EventHandler? DoubleClick;
    public event EventHandler? AvailableChanged;
    public event EventHandler? TextChanged;
    public event MouseEventHandler? MouseDown;
    public event MouseEventHandler? MouseUp;
    public event EventHandler? MouseEnter;
    public event EventHandler? MouseHover;
    public event EventHandler? MouseLeave;
    public event PaintEventHandler? Paint;

    /// <summary>Raises <c>MouseEnter</c> (cf. OnMouseEnter).</summary>
    protected virtual void OnMouseEnter(EventArgs e) =>
        MouseEnter?.Invoke(this, e);

    protected virtual void OnClick(EventArgs e) => Click?.Invoke(this, e);
    protected virtual void OnDoubleClick(EventArgs e) => DoubleClick?.Invoke(this, e);
    protected virtual void OnAvailableChanged(EventArgs e) => AvailableChanged?.Invoke(this, e);
    protected virtual void OnTextChanged(EventArgs e) => TextChanged?.Invoke(this, e);
    protected virtual void OnEnabledChanged(EventArgs e)
    {
    }

    protected virtual void OnVisibleChanged(EventArgs e)
    {
    }

    protected virtual void OnMouseDown(MouseEventArgs e) => MouseDown?.Invoke(this, e);
    protected virtual void OnMouseUp(MouseEventArgs e) => MouseUp?.Invoke(this, e);
    protected virtual void OnMouseHover(EventArgs e) => MouseHover?.Invoke(this, e);
    protected virtual void OnMouseLeave(EventArgs e) => MouseLeave?.Invoke(this, e);
    protected virtual void OnOwnerChanged(EventArgs e)
    {
    }

    protected virtual void OnParentChanged(ToolStrip? oldParent, ToolStrip? newParent)
    {
    }

    internal void NotifyParentChanged(ToolStrip? oldParent, ToolStrip? newParent) =>
        OnParentChanged(oldParent, newParent);

    protected virtual void OnPaint(PaintEventArgs e) => Paint?.Invoke(this, e);

    internal void NotifyPaint(PaintEventArgs e) => OnPaint(e);
}

/// <summary>Item collection (cf. ToolStripItemCollection).</summary>
public class ToolStripItemCollection : System.Collections.IList
{
    private readonly List<ToolStripItem> _items = new();
    private ToolStrip? _owner;

    public ToolStripItemCollection()
    {
    }

    internal ToolStripItemCollection(ToolStrip owner)
    {
        _owner = owner;
    }

    public int Add(ToolStripItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        Adopt(item);
        return _items.Count - 1;
    }

    public ToolStripItem Add(string text)
    {
        var button = new ToolStripButton(text);
        Add(button);
        return button;
    }

    public ToolStripItem Add(WfDraw::System.Drawing.Image image)
    {
        var button = new ToolStripButton(image);
        Add(button);
        return button;
    }

    public ToolStripItem Add(string text, WfDraw::System.Drawing.Image image)
    {
        var button = new ToolStripButton(text, image);
        Add(button);
        return button;
    }

    public ToolStripItem Add(string text, WfDraw::System.Drawing.Image image, EventHandler onClick)
    {
        var button = new ToolStripButton(text, image, onClick);
        Add(button);
        return button;
    }

    public void CopyTo(ToolStripItem[] array, int index) => _items.CopyTo(array, index);

    public void AddRange(ToolStripItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (ToolStripItem item in items)
            Add(item);
    }

    public void AddRange(ToolStripItemCollection items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (ToolStripItem item in items)
            Add(item);
    }

    public void Clear()
    {
        foreach (ToolStripItem item in _items)
            Release(item);
        _items.Clear();
    }

    public bool Contains(ToolStripItem item) => _items.Contains(item);
    public bool ContainsKey(string key) => IndexOfKey(key) >= 0;

    public ToolStripItem[] Find(string key, bool searchAllChildren)
    {
        var found = new List<ToolStripItem>();
        FindInto(key, searchAllChildren, found);
        return found.ToArray();
    }

    private void FindInto(string key, bool deep, List<ToolStripItem> found)
    {
        foreach (ToolStripItem item in _items)
        {
            if (item.Name == key)
                found.Add(item);
            if (deep && item is ToolStripDropDownItem drop)
                drop.DropDownItems.FindInto(key, true, found);
        }
    }

    public int IndexOf(ToolStripItem item) => _items.IndexOf(item);
    public int IndexOfKey(string key) => _items.FindIndex(item => item.Name == key);
    public void Insert(int index, ToolStripItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        Adopt(item);
    }

    public void Remove(ToolStripItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_items.Remove(item))
            Release(item);
    }

    public void RemoveAt(int index)
    {
        ToolStripItem item = _items[index];
        _items.RemoveAt(index);
        Release(item);
    }

    public void RemoveByKey(string key)
    {
        int at = IndexOfKey(key);
        if (at >= 0)
            RemoveAt(at);
    }

    public int Count => _items.Count;

    public ToolStripItem this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Release(_items[index]);
            _items[index] = value;
            Adopt(value);
        }
    }

    public ToolStripItem? this[string key] =>
        IndexOfKey(key) is int at && at >= 0 ? _items[at] : null;

    private void Adopt(ToolStripItem item)
    {
        if (_owner is null)
            return;
        ToolStrip? old = item.Parent;
        item.Parent = _owner;
        item.Owner = _owner;
        item.NotifyParentChanged(old, _owner);
    }

    private static void Release(ToolStripItem item)
    {
        item.Parent = null;
    }

    public bool Contains(object? value) => value is ToolStripItem item && Contains(item);
    public int IndexOf(object? value) => value is ToolStripItem item ? IndexOf(item) : -1;
    public void Insert(int index, object? value) => Insert(index, (ToolStripItem)value!);
    public void Remove(object? value)
    {
        if (value is ToolStripItem item)
            Remove(item);
    }

    object? System.Collections.IList.this[int index]
    {
        get => this[index];
        set => this[index] = (ToolStripItem)value!;
    }

    int System.Collections.IList.Add(object? value) => Add((ToolStripItem)value!);
    bool System.Collections.IList.IsReadOnly => false;
    bool System.Collections.IList.IsFixedSize => false;
    void System.Collections.ICollection.CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_items).CopyTo(array, index);
    bool System.Collections.ICollection.IsSynchronized => false;
    object System.Collections.ICollection.SyncRoot => this;
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        _items.GetEnumerator();
}

/// <summary>Drop-down item (cf. ToolStripDropDownItem).</summary>
public class ToolStripDropDownItem : ToolStripItem
{
    public ToolStripDropDownItem()
    {
    }

    public ToolStripDropDownItem(string text)
        : base(text)
    {
    }

    private ToolStripDropDown? _dropDown;

    public ToolStripItemCollection DropDownItems { get; } = new();

    public ToolStripDropDown DropDown => _dropDown ??= CreateDefaultDropDown();
    public ToolStripDropDownDirection DropDownDirection { get; set; } = ToolStripDropDownDirection.Default;

    public WfDraw::System.Drawing.Point DropDownLocation
    {
        get
        {
            WfDraw::System.Drawing.Rectangle bounds = Bounds;
            return DropDownDirection is ToolStripDropDownDirection.AboveLeft or ToolStripDropDownDirection.AboveRight
                ? new(bounds.X, bounds.Y - DropDown.Height)
                : new(bounds.X, bounds.Bottom);
        }
    }

    public bool HasDropDownItems => DropDownItems.Count > 0;

    protected virtual ToolStripDropDown CreateDefaultDropDown() => new(DropDownItems);

    public void ShowDropDown()
    {
        OnDropDownShow(EventArgs.Empty);
        OnDropDownOpened(EventArgs.Empty);
    }

    public void HideDropDown() => OnDropDownClosed(EventArgs.Empty);

    public event EventHandler? DropDownOpening;
    public event EventHandler? DropDownOpened;
    public event EventHandler? DropDownClosed;
    public event ToolStripItemClickedEventHandler? DropDownItemClicked;

    protected virtual void OnDropDownShow(EventArgs e)
    {
    }

    protected virtual void OnDropDownOpening(EventArgs e) => DropDownOpening?.Invoke(this, e);
    protected virtual void OnDropDownOpened(EventArgs e) => DropDownOpened?.Invoke(this, e);
    protected virtual void OnDropDownClosed(EventArgs e) => DropDownClosed?.Invoke(this, e);

    protected virtual void OnDropDownItemClicked(ToolStripItemClickedEventArgs e) =>
        DropDownItemClicked?.Invoke(this, e);
}

/// <summary>Menu item (cf. ToolStripMenuItem): text, check, submenu, click.</summary>
public class ToolStripMenuItem : ToolStripDropDownItem
{
    public ToolStripMenuItem()
    {
    }

    public ToolStripMenuItem(string text = "")
        : base(text)
    {
    }

    public bool Checked { get; set; }
    public bool CheckOnClick { get; set; }
    public CheckState CheckState
    {
        get => Checked ? CheckState.Checked : CheckState.Unchecked;
        set => Checked = value != CheckState.Unchecked;
    }

    public Keys ShortcutKeys { get; set; } = Keys.None;
    public string ShortcutKeyDisplayString { get; set; } = "";
    public string ShortcutText { get; set; } = "";

    public bool IsSeparator => Text == "-";
    public bool HasDropDown => DropDownItems.Count > 0;

    public override void PerformClick()
    {
        if (!Enabled || IsSeparator)
            return;
        if (CheckOnClick)
            Checked = !Checked;
        base.PerformClick();
    }

    protected override void OnClick(EventArgs e) => base.OnClick(e);
    protected override void OnMouseLeave(EventArgs e) => base.OnMouseLeave(e);
    protected override void OnPaint(PaintEventArgs e) => base.OnPaint(e);
}

/// <summary>
/// Menu strip (cf. MenuStrip): top-level items in a bar; clicking opens the
/// drop-down (white + gutter + hot tracking); submenus open to the right.
/// Hover switches while open; Escape or re-click closes.
/// </summary>
public class MenuStrip : ToolStrip
{
    public const int BarHeight = 24;
    public const int ItemHeight = 22;
    public const int GutterWidth = 28;

    public MenuStrip()
        : this("")
    {
    }

    public MenuStrip(string name = "")
        : base(name)
    {
    }

    public string FontCss { get; set; } = Palette.UiFontCss;

    private int _openTop = -1;
    private readonly List<int> _openChain = new();
    private int _hotTop = -1;
    private int _hotDrop = -1;

    public bool IsOpen => _openTop >= 0;

    public override (Control Control, int LocalX, int LocalY)? HitTestDeep(int x, int y)
    {
        var hit = base.HitTestDeep(x, y);
        if (hit.HasValue || !Visible || !Enabled || !IsOpen)
            return hit;
        // The open drop-down overflows the strip bounds (cf. top-level menu window).
        (int X, int Y, int Width, int Height)? bounds = DropDownBounds();
        int lx = x - X;
        int ly = y - Y;
        if (bounds.HasValue && lx >= bounds.Value.X && lx < bounds.Value.X + bounds.Value.Width
            && ly >= bounds.Value.Y && ly < bounds.Value.Y + bounds.Value.Height)
            return (this, lx, ly);
        return null;
    }

    public void CloseAll()
    {
        _openTop = -1;
        _openChain.Clear();
        _hotTop = -1;
        _hotDrop = -1;
    }

    /// <summary>Top-level item bounds (widths estimated until metrics).</summary>
    public List<(int X, int Width)> TopBounds()
    {
        var bounds = new List<(int, int)>();
        int x = 4;
        foreach (ToolStripMenuItem item in Items)
        {
            int w = 20 + (int)(Measure(item.Text) * 0.5f * 13);
            bounds.Add((x, w));
            x += w;
        }

        return bounds;
    }

    /// <summary>Open drop-down bounds in strip-local space.</summary>
    public (int X, int Y, int Width, int Height)? DropDownBounds()
    {
        if (_openTop < 0 || _openTop >= Items.Count)
            return null;
        var top = TopBounds();
        int w = DropDownWidth((ToolStripMenuItem)Items[_openTop]);
        return (top[_openTop].X, BarHeight, w, (((ToolStripMenuItem)Items[_openTop]).DropDownItems.Count * ItemHeight) + 4);
    }

    private int DropDownWidth(ToolStripMenuItem parent)
    {
        int w = 120;
        foreach (ToolStripMenuItem item in parent.DropDownItems)
        {
            int need = GutterWidth + 12 + (int)(Measure(item.Text) * 0.5f * 13)
                + (string.IsNullOrEmpty(item.ShortcutText) ? 0 : 60)
                + (item.HasDropDown ? 20 : 0);
            w = Math.Max(w, need);
        }

        return w;
    }

    private static float Measure(string text) => text.Length;

    public override void HandleHoverLocal(int localX, int localY)
    {
        if (!Enabled)
            return;
        _hotTop = -1;
        _hotDrop = -1;
        var top = TopBounds();
        for (int i = 0; i < top.Count; i++)
        {
            if (localX >= top[i].X && localX < top[i].X + top[i].Width && localY < BarHeight)
            {
                _hotTop = i;
                if (IsOpen && i != _openTop)
                {
                    _openTop = i;
                    _openChain.Clear();
                }

                return;
            }
        }

        if (!IsOpen)
            return;
        (int X, int Y, int Width, int Height)? bounds = DropDownBounds();
        if (!bounds.HasValue)
            return;
        int relY = localY - bounds.Value.Y;
        if (localX >= bounds.Value.X && localX < bounds.Value.X + bounds.Value.Width
            && relY >= 0 && relY < bounds.Value.Height)
        {
            int index = (relY - 2) / ItemHeight;
            if (index >= 0 && index < ((ToolStripMenuItem)Items[_openTop]).DropDownItems.Count)
            {
                _hotDrop = index;
                ToolStripMenuItem item = (ToolStripMenuItem)((ToolStripMenuItem)Items[_openTop]).DropDownItems[index];
                if (item.HasDropDown && item.Enabled)
                {
                    _openChain.Clear();
                    _openChain.Add(index);
                }
                else if (!item.HasDropDown)
                {
                    _openChain.Clear();
                }
            }
        }
    }

    public override void HandleClickLocal(int localX, int localY)
    {
        if (!Enabled)
            return;
        var top = TopBounds();
        for (int i = 0; i < top.Count; i++)
        {
            if (localX >= top[i].X && localX < top[i].X + top[i].Width && localY < BarHeight)
            {
                if (_openTop == i)
                    CloseAll();
                else
                {
                    _openTop = i;
                    _openChain.Clear();
                }

                return;
            }
        }

        if (!IsOpen)
        {
            base.HandleClickLocal(localX, localY);
            return;
        }

        (int X, int Y, int Width, int Height)? bounds = DropDownBounds();
        if (bounds.HasValue && localX >= bounds.Value.X && localX < bounds.Value.X + bounds.Value.Width
            && localY >= bounds.Value.Y && localY < bounds.Value.Y + bounds.Value.Height)
        {
            int index = (localY - bounds.Value.Y - 2) / ItemHeight;
            if (index < 0 || index >= ((ToolStripMenuItem)Items[_openTop]).DropDownItems.Count)
                return;
            ToolStripMenuItem item = (ToolStripMenuItem)((ToolStripMenuItem)Items[_openTop]).DropDownItems[index];
            if (!item.Enabled || item.IsSeparator)
                return;
            // Submenu leaf level only (one nested level); deeper chains collapse.
            if (_openChain.Count > 0)
            {
                ToolStripMenuItem parent = (ToolStripMenuItem)((ToolStripMenuItem)Items[_openTop]).DropDownItems[_openChain[0]];
                int sub = SubItemIndex(parent, localX - bounds.Value.X, localY - bounds.Value.Y);
                if (sub >= 0)
                {
                    ToolStripMenuItem leaf = (ToolStripMenuItem)parent.DropDownItems[sub];
                    if (leaf.Enabled && !leaf.IsSeparator)
                    {
                        CloseAll();
                        leaf.PerformClick();
                        OnItemClicked(new ToolStripItemClickedEventArgs(leaf));
                    }

                    return;
                }
            }

            if (item.HasDropDown)
            {
                _openChain.Clear();
                _openChain.Add(index);
                return;
            }

            CloseAll();
            item.PerformClick();
            OnItemClicked(new ToolStripItemClickedEventArgs(item));
            return;
        }

        // Submenu hit area (right of the open parent item).
        if (_openChain.Count > 0)
        {
            ToolStripMenuItem parent = (ToolStripMenuItem)((ToolStripMenuItem)Items[_openTop]).DropDownItems[_openChain[0]];
            int sub = SubItemIndex(parent, localX - bounds!.Value.X, localY - bounds.Value.Y);
            if (sub >= 0)
            {
                ToolStripMenuItem leaf = (ToolStripMenuItem)parent.DropDownItems[sub];
                if (leaf.Enabled && !leaf.IsSeparator)
                {
                    CloseAll();
                    leaf.PerformClick();
                    OnItemClicked(new ToolStripItemClickedEventArgs(leaf));
                }

                return;
            }
        }

        CloseAll();
    }

    private int SubItemIndex(ToolStripMenuItem parent, int relX, int relY)
    {
        (int X, int Y, int Width, int Height)? bounds = DropDownBounds();
        if (!bounds.HasValue)
            return -1;
        int parentTop = 2 + (_openChain[0] * ItemHeight);
        int sx = bounds.Value.Width - 2;
        int sy = parentTop;
        int sw = DropDownWidth(parent);
        int sh = (parent.DropDownItems.Count * ItemHeight) + 4;
        if (relX >= sx - bounds.Value.X && relX < sx - bounds.Value.X + sw
            && relY >= sy && relY < sy + sh)
        {
            int sub = (relY - sy - 2) / ItemHeight;
            if (sub >= 0 && sub < parent.DropDownItems.Count)
                return sub;
        }

        return -1;
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        if (themed)
            canvas.FillRectangle(ax, ay, Width, BarHeight, SystemColors.Menu);
        else
            canvas.FillRectangle(ax, ay, Width, BarHeight, SystemColors.Control);

        var top = TopBounds();
        for (int i = 0; i < top.Count && i < Items.Count; i++)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)Items[i];
            bool hot = (i == _hotTop || (IsOpen && i == _openTop)) && item.Enabled;
            if (hot)
            {
                if (themed)
                {
                    canvas.FillRectangle(ax + top[i].X, ay + 2, top[i].Width, BarHeight - 4,
                        SystemColors.ThemedMenuHotFill);
                    ControlPaintEdges.DrawBorder(canvas, ax + top[i].X, ay + 2, top[i].Width, BarHeight - 4,
                        SystemColors.ThemedMenuHotBorder, ControlPaintEdges.LineBorderStyle.Solid);
                }
                else
                {
                    canvas.FillRectangle(ax + top[i].X, ay + 2, top[i].Width, BarHeight - 4,
                        SystemColors.Highlight);
                }
            }

            uint ink = !item.Enabled ? SystemColors.GrayText
                : hot && !themed ? SystemColors.HighlightText
                : SystemColors.MenuText;
            TextRules.DrawAlignedText(canvas, item.Text, true, Draw.ContentAlignment.MiddleCenter,
                ax + top[i].X, ay, top[i].Width, BarHeight, FontCss, ink, !item.Enabled);
        }

        if (IsOpen)
            PaintDropDown(canvas, ax, ay);
    }

    private void PaintDropDown(ICanvas canvas, int ax, int ay)
    {
        (int X, int Y, int Width, int Height)? bounds = DropDownBounds();
        if (!bounds.HasValue)
            return;
        bool themed = RenderingConfig.UseVisualStyles;
        int x = ax + bounds.Value.X;
        int y = ay + bounds.Value.Y;
        canvas.FillRectangle(x, y, bounds.Value.Width, bounds.Value.Height, SystemColors.Window);
        // Icon gutter.
        canvas.FillRectangle(x + 1, y + 1, GutterWidth - 2, bounds.Value.Height - 2,
            themed ? SystemColors.ThemedMenuGutter : SystemColors.Control);
        ControlPaintEdges.DrawBorder(canvas, x, y, bounds.Value.Width, bounds.Value.Height,
            SystemColors.ControlDarkDark, ControlPaintEdges.LineBorderStyle.Solid);

        var items = ((ToolStripMenuItem)Items[_openTop]).DropDownItems;
        for (int i = 0; i < items.Count; i++)
        {
            int iy = y + 2 + (i * ItemHeight);
            ToolStripMenuItem item = (ToolStripMenuItem)items[i];
            if (item.IsSeparator)
            {
                canvas.FillRectangle(x + GutterWidth, iy + ((ItemHeight - 1) / 2),
                    bounds.Value.Width - GutterWidth - 6, 1, SystemColors.ThemedSeparator);
                continue;
            }

            bool hot = (i == _hotDrop || (_openChain.Count > 0 && _openChain[0] == i)) && item.Enabled;
            if (hot)
            {
                if (themed)
                {
                    canvas.FillRectangle(x + 2, iy, bounds.Value.Width - 4, ItemHeight,
                        SystemColors.ThemedMenuHotFill);
                    ControlPaintEdges.DrawBorder(canvas, x + 2, iy, bounds.Value.Width - 4, ItemHeight,
                        SystemColors.ThemedMenuHotBorder, ControlPaintEdges.LineBorderStyle.Solid);
                }
                else
                {
                    canvas.FillRectangle(x + 2, iy, bounds.Value.Width - 4, ItemHeight,
                        SystemColors.Highlight);
                }
            }

            if (item.Checked)
                ControlPaintGlyphs.DrawMenuGlyph(canvas, x + 6, iy + 4, 16, ItemHeight - 6,
                    MenuGlyph.Checkmark, SystemColors.MenuText);

            uint ink = !item.Enabled ? SystemColors.GrayText
                : hot && !themed ? SystemColors.HighlightText
                : SystemColors.MenuText;
            TextRules.DrawAlignedText(canvas, item.Text, true, Draw.ContentAlignment.MiddleLeft,
                x + GutterWidth, iy, bounds.Value.Width - GutterWidth - 8, ItemHeight,
                FontCss, ink, !item.Enabled);

            if (!string.IsNullOrEmpty(item.ShortcutText))
                TextRules.DrawAlignedText(canvas, item.ShortcutText, false, Draw.ContentAlignment.MiddleRight,
                    x + GutterWidth, iy, bounds.Value.Width - GutterWidth - 24, ItemHeight,
                    FontCss, ink, !item.Enabled);

            if (item.HasDropDown)
                ControlPaintGlyphs.DrawMenuGlyph(canvas,
                    x + bounds.Value.Width - 16, iy + 4, 12, ItemHeight - 6,
                    MenuGlyph.Arrow, ink);
        }

        // One nested submenu level, right of its parent item.
        if (_openChain.Count > 0)
        {
            ToolStripMenuItem parent = (ToolStripMenuItem)items[_openChain[0]];
            PaintSubMenu(canvas, x + bounds.Value.Width - 2,
                y + 2 + (_openChain[0] * ItemHeight), parent);
        }
    }

    private void PaintSubMenu(ICanvas canvas, int x, int y, ToolStripMenuItem parent)
    {
        int w = DropDownWidth(parent);
        int h = (parent.DropDownItems.Count * ItemHeight) + 4;
        canvas.FillRectangle(x, y, w, h, SystemColors.Window);
        ControlPaintEdges.DrawBorder(canvas, x, y, w, h,
            SystemColors.ControlDarkDark, ControlPaintEdges.LineBorderStyle.Solid);
        for (int i = 0; i < parent.DropDownItems.Count; i++)
        {
            int iy = y + 2 + (i * ItemHeight);
            ToolStripMenuItem item = (ToolStripMenuItem)parent.DropDownItems[i];
            if (item.IsSeparator)
            {
                canvas.FillRectangle(x + 4, iy + ((ItemHeight - 1) / 2), w - 8, 1,
                    SystemColors.ThemedSeparator);
                continue;
            }

            TextRules.DrawAlignedText(canvas, item.Text, true, Draw.ContentAlignment.MiddleLeft,
                x + 8, iy, w - 16, ItemHeight, FontCss,
                item.Enabled ? SystemColors.MenuText : SystemColors.GrayText, !item.Enabled);
        }
    }
}

/// <summary>Status panel (cf. ToolStripStatusLabel : ToolStripLabel): text with fixed width.</summary>
public class ToolStripStatusLabel : ToolStripLabel
{
    public ToolStripStatusLabel()
        : this("")
    {
    }

    public ToolStripStatusLabel(string text = "", int width = 120)
        : base(text)
    {
        Width = width;
    }

    public BorderStyle BorderStyle { get; set; } = BorderStyle.None;
    public bool Spring { get; set; }
}

/// <summary>
/// Status strip (cf. StatusStrip): panels row with an optional sizing grip.
/// </summary>
public class StatusStrip : ToolStrip
{
    public const int StripHeight = 22;

    public StatusStrip()
        : this("")
    {
    }

    public StatusStrip(string name = "")
        : base(name)
    {
    }

    public List<ToolStripStatusLabel> Panels { get; } = new();
    public string FontCss { get; set; } = Palette.UiFontCss;
    public bool SizingGrip { get; set; } = true;

    protected internal virtual Padding DefaultPadding => new(1, 0, 14, 0);

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        canvas.FillRectangle(ax, ay, Width, StripHeight,
            themed ? SystemColors.Menu : SystemColors.Control);
        canvas.FillRectangle(ax, ay, Width, 1,
            themed ? SystemColors.ThemedSeparator : SystemColors.ControlDark);

        int x = 2;
        int gripW = SizingGrip ? 16 : 0;
        foreach (ToolStripStatusLabel panel in Panels)
        {
            int w = panel.Spring ? Width - x - gripW - 2 : Math.Min(panel.Width, Width - x - gripW - 2);
            if (w <= 0)
                break;
            if (panel.BorderStyle == BorderStyle.FixedSingle)
                ControlPaintEdges.DrawBorder(canvas, ax + x, ay + 2, w, StripHeight - 4,
                    SystemColors.ControlDarkDark, ControlPaintEdges.LineBorderStyle.Solid);
            else if (panel.BorderStyle == BorderStyle.Fixed3D)
                ControlPaintEdges.DrawEdge(canvas, ax + x, ay + 2, w, StripHeight - 4,
                    ControlPaintEdges.EdgeStyle.Sunken);
            if (!string.IsNullOrEmpty(panel.Text))
                canvas.DrawTextAligned(panel.Text, ax + x + 4, ay + (StripHeight / 2f),
                    FontCss, SystemColors.ControlText, TextHAlign.Left, TextVAlign.Middle);
            x += w + 2;
        }

        if (SizingGrip)
            ControlPaintGlyphs.DrawSizeGrip(
                canvas, ax + Width - 16, ay + StripHeight - 16, 16, 16);
    }
}

/// <summary>Item-click data (cf. ToolStripItemClickedEventArgs).</summary>
public class ToolStripItemClickedEventArgs : EventArgs
{
    public ToolStripItemClickedEventArgs(ToolStripItem? clickedItem)
    {
        ClickedItem = clickedItem;
    }

    public ToolStripItem? ClickedItem { get; }
}

public delegate void ToolStripItemClickedEventHandler(object? sender, ToolStripItemClickedEventArgs e);

/// <summary>Layout settings (cf. Layout.LayoutSettings).</summary>
public abstract class LayoutSettings
{
    public abstract ToolStripLayoutStyle LayoutStyle { get; }
}

/// <summary>Flow settings (cf. FlowLayoutSettings).</summary>
public class FlowLayoutSettings : LayoutSettings
{
    public FlowLayoutSettings(ToolStrip? owner)
    {
        FlowDirection = FlowDirection.LeftToRight;
    }

    public FlowDirection FlowDirection { get; set; }
    public bool WrapContents { get; set; } = true;
    public override ToolStripLayoutStyle LayoutStyle => ToolStripLayoutStyle.Flow;

    private readonly HashSet<object> _breaks = new();

    public bool GetFlowBreak(object control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _breaks.Contains(control);
    }

    public void SetFlowBreak(object control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (value)
            _breaks.Add(control);
        else
            _breaks.Remove(control);
    }
}

/// <summary>Drop-down window (cf. ToolStripDropDown).</summary>
public class ToolStripDropDown : ToolStrip
{
    public ToolStripDropDown()
    {
    }

    internal ToolStripDropDown(ToolStripItemCollection shared)
        : base(shared)
    {
    }

    public bool TopLevel { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public bool AutoClose { get; set; } = true;

    public event System.ComponentModel.CancelEventHandler? Opening;
    public event EventHandler? Opened;
    public event System.ComponentModel.CancelEventHandler? Closing;
    public event ToolStripDropDownClosedEventHandler? Closed;

    protected virtual void OnOpening(System.ComponentModel.CancelEventArgs e) =>
        Opening?.Invoke(this, e);
    protected virtual void OnOpened(EventArgs e) =>
        Opened?.Invoke(this, e);
    protected virtual void OnClosing(ToolStripDropDownClosingEventArgs e) =>
        Closing?.Invoke(this, e);
    protected virtual void OnClosed(ToolStripDropDownClosedEventArgs e) =>
        Closed?.Invoke(this, e);

    public new void Show()
    {
        var opening = new System.ComponentModel.CancelEventArgs();
        OnOpening(opening);
        if (!opening.Cancel)
            OnOpened(EventArgs.Empty);
    }

    public void Close() => Close(ToolStripDropDownCloseReason.CloseCalled);

    public void Close(ToolStripDropDownCloseReason reason)
    {
        var closing = new ToolStripDropDownClosingEventArgs(reason);
        OnClosing(closing);
        if (!closing.Cancel)
            OnClosed(new ToolStripDropDownClosedEventArgs(reason));
    }

    public void Show(WfDraw::System.Drawing.Point position) =>
        Show(position, ToolStripDropDownDirection.Default);

    public void Show(WfDraw::System.Drawing.Point position, ToolStripDropDownDirection direction)
    {
        var opening = new System.ComponentModel.CancelEventArgs();
        OnOpening(opening);
        if (!opening.Cancel)
            OnOpened(EventArgs.Empty);
    }

    public void Show(int x, int y) =>
        Show(new WfDraw::System.Drawing.Point(x, y));

    public void Show(Control owner, WfDraw::System.Drawing.Point position) =>
        Show(owner, position, ToolStripDropDownDirection.Default);

    public void Show(Control owner, WfDraw::System.Drawing.Point position, ToolStripDropDownDirection direction)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Show(position, direction);
    }

    public void Show(Control owner, int x, int y) =>
        Show(owner, new WfDraw::System.Drawing.Point(x, y));

    /// <summary>Layout settings factory (cf. ToolStripDropDown.CreateLayoutSettings).</summary>
    protected virtual LayoutSettings CreateLayoutSettings(ToolStripLayoutStyle layoutStyle) =>
        new FlowLayoutSettings(this);
}

/// <summary>Tool bar (cf. System.Windows.Forms.ToolStrip): item row with a
/// simple left-flow layout. Rendering goes through <see cref="Renderer"/> so
/// custom renderers (including rewritten app subclasses) plug in; the base
/// renderer paints the strip chrome while item faces await the canvas
/// theming milestone.</summary>
public class ToolStrip : ScrollableControl
{
    public ToolStrip(string name = "")
        : base(name)
    {
        Items = new ToolStripItemCollection(this);
        LayoutSettings = new FlowLayoutSettings(this);
        Renderer = new ToolStripProfessionalRenderer();
    }

    public ToolStrip()
        : this("")
    {
    }

    internal ToolStrip(ToolStripItemCollection shared)
        : base("")
    {
        Items = shared;
        LayoutSettings = new FlowLayoutSettings(this);
        Renderer = new ToolStripProfessionalRenderer();
    }

    public ToolStripItemCollection Items { get; }
    public ToolStripRenderer Renderer { get; set; }

    /// <summary>Backing tip window (cf. ToolStrip.ToolTip).</summary>
    internal ToolTip ToolTip { get; } = new ToolTip();

    /// <summary>Click-through switch (cf. AllowClickThrough). Stored only.</summary>
    public bool AllowClickThrough { get; set; }

    /// <summary>Image size (cf. ImageScalingSize).</summary>
    public WfDraw::System.Drawing.Size ImageScalingSize { get; set; } = new(16, 16);
    public ToolStripLayoutStyle LayoutStyle { get; set; } = ToolStripLayoutStyle.HorizontalStackWithOverflow;
    public ToolStripGripStyle GripStyle { get; set; } = ToolStripGripStyle.Visible;
    public bool ShowItemToolTips { get; set; }
    public ToolStripDropDownDirection DefaultDropDownDirection { get; set; } = ToolStripDropDownDirection.BelowRight;
    public LayoutSettings LayoutSettings { get; }
    public int GripMargin { get; set; } = 2;

    public event ToolStripItemClickedEventHandler? ItemClicked;

    protected virtual void OnItemClicked(ToolStripItemClickedEventArgs e) => ItemClicked?.Invoke(this, e);

    protected virtual void OnItemAdded(ToolStripItemEventArgs e)
    {
    }

    protected virtual void OnItemRemoved(ToolStripItemEventArgs e)
    {
    }

    public ToolStripItem? GetNextItem(ToolStripItem? start, ArrowDirection direction)
    {
        var candidates = new List<ToolStripItem>();
        foreach (ToolStripItem item in Items)
        {
            if (item.Available && item.Visible && item.Enabled)
                candidates.Add(item);
        }

        if (candidates.Count == 0)
            return null;
        if (start is null)
            return candidates[0];
        int at = candidates.IndexOf(start);
        if (at < 0)
            return candidates[0];
        int step = direction is ArrowDirection.Left or ArrowDirection.Up ? -1 : 1;
        return candidates[(at + step + candidates.Count) % candidates.Count];
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        Renderer.RenderStripBackground(this, canvas, ax, ay);
        int x = GripStyle == ToolStripGripStyle.Visible ? GripMargin + 8 : GripMargin;
        foreach (ToolStripItem item in Items)
        {
            if (!item.Available || !item.Visible)
                continue;
            WfDraw::System.Drawing.Size size = item.AutoSize
                ? item.GetPreferredSize(WfDraw::System.Drawing.Size.Empty)
                : item.Size;
            item.Bounds = new(ax - X + x, 0, size.Width, Height);
            Renderer.RenderItem(this, item, canvas, ax + x, ay);
            x += size.Width + item.Margin.Horizontal;
        }
    }
}

/// <summary>Item event data (cf. ToolStripItemEventArgs).</summary>
public class ToolStripItemEventArgs : EventArgs
{
    public ToolStripItemEventArgs(ToolStripItem? item)
    {
        Item = item;
    }

    public ToolStripItem? Item { get; }
}

/// <summary>Strip button (cf. ToolStripButton).</summary>
public class ToolStripButton : ToolStripItem
{
    public ToolStripButton()
    {
    }

    public ToolStripButton(string text)
        : base(text)
    {
    }

    public ToolStripButton(WfDraw::System.Drawing.Image image)
        : base("")
    {
        Image = image;
    }

    public ToolStripButton(string text, WfDraw::System.Drawing.Image image)
        : base(text)
    {
        Image = image;
    }

    public ToolStripButton(string text, WfDraw::System.Drawing.Image image, EventHandler onClick)
        : this(text, image)
    {
        Click += onClick;
    }

    public ToolStripButton(string text, WfDraw::System.Drawing.Image image, EventHandler onClick, string name)
        : this(text, image, onClick)
    {
        Name = name;
    }

    public bool Checked { get; set; }
    public bool CheckOnClick { get; set; }
    public CheckState CheckState
    {
        get => Checked ? CheckState.Checked : CheckState.Unchecked;
        set => Checked = value != CheckState.Unchecked;
    }

    public override void PerformClick()
    {
        if (!Enabled)
            return;
        if (CheckOnClick)
            Checked = !Checked;
        base.PerformClick();
    }
}

/// <summary>Drop-down button (cf. ToolStripDropDownButton).</summary>
public class ToolStripDropDownButton : ToolStripDropDownItem
{
    public ToolStripDropDownButton()
    {
    }

    public ToolStripDropDownButton(string text)
        : base(text)
    {
    }

    public bool ShowDropDownArrow { get; set; } = true;
}

/// <summary>Split button (cf. ToolStripSplitButton).</summary>
public class ToolStripSplitButton : ToolStripDropDownItem
{
    public ToolStripSplitButton()
    {
    }

    public ToolStripSplitButton(string text)
        : base(text)
    {
    }

    public ToolStripItem? DefaultItem { get; set; }
    public int DropDownButtonWidth { get; set; } = 16;

    public WfDraw::System.Drawing.Rectangle DropDownButtonBounds
    {
        get
        {
            WfDraw::System.Drawing.Rectangle bounds = Bounds;
            return new(bounds.Right - DropDownButtonWidth, bounds.Y, DropDownButtonWidth, bounds.Height);
        }
    }

    public event EventHandler? ButtonClick;

    protected virtual void OnButtonClick(EventArgs e) => ButtonClick?.Invoke(this, e);

    public void PerformButtonClick()
    {
        if (Enabled)
            OnButtonClick(EventArgs.Empty);
    }
}

/// <summary>Hosted control item (cf. ToolStripControlHost).</summary>
public class ToolStripControlHost : ToolStripItem
{
    public ToolStripControlHost(Control control)
    {
        Control = control;
    }

    public ToolStripControlHost(Control control, string name)
        : base(name)
    {
        Control = control;
    }

    public Control Control { get; }
    public Draw.ContentAlignment ControlAlign { get; set; } = Draw.ContentAlignment.MiddleCenter;

    public override WfDraw::System.Drawing.Size GetPreferredSize(WfDraw::System.Drawing.Size constrainingSize) =>
        new(Control.Width, Control.Height);

    public event EventHandler? Enter;
    public event EventHandler? Leave;
    public event System.ComponentModel.CancelEventHandler? Validating;

    protected virtual void OnSubscribeControlEvents(Control control)
    {
    }

    protected virtual void OnUnsubscribeControlEvents(Control control)
    {
    }

    protected virtual void OnEnter(EventArgs e) => Enter?.Invoke(this, e);
    protected virtual void OnLeave(EventArgs e) => Leave?.Invoke(this, e);

    protected virtual void OnValidating(System.ComponentModel.CancelEventArgs e) =>
        Validating?.Invoke(this, e);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Control.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Hosted combo box (cf. ToolStripComboBox).</summary>
public class ToolStripComboBox : ToolStripControlHost
{
    public ToolStripComboBox()
        : base(new ComboBox())
    {
        ComboBox.DropDownStyle = ComboBoxStyle.DropDown;
    }

    public ComboBox ComboBox => (ComboBox)Control;

    public ComboBox.ObjectCollection Items => ComboBox.Items;
    public ComboBoxStyle DropDownStyle
    {
        get => ComboBox.DropDownStyle;
        set => ComboBox.DropDownStyle = value;
    }

    public int DropDownHeight
    {
        get => ComboBox.DropDownHeight;
        set => ComboBox.DropDownHeight = value;
    }

    public int DropDownWidth
    {
        get => ComboBox.DropDownWidth;
        set => ComboBox.DropDownWidth = value;
    }

    public int MaxDropDownItems { get; set; } = ComboBox.MaxDropDownItems;

    public int SelectedIndex
    {
        get => ComboBox.SelectedIndex;
        set => ComboBox.SelectedIndex = value;
    }

    public object? SelectedItem
    {
        get => ComboBox.SelectedItem;
        set => ComboBox.SelectedItem = value;
    }

    public event EventHandler? SelectedIndexChanged
    {
        add => ComboBox.SelectedIndexChanged += value;
        remove => ComboBox.SelectedIndexChanged -= value;
    }

    /// <summary>Drop-down close notifier (cf. DropDownClosed).</summary>
    public event EventHandler? DropDownClosed
    {
        add => ComboBox.DropDownClosed += value;
        remove => ComboBox.DropDownClosed -= value;
    }

    public void BeginUpdate() => ComboBox.BeginUpdate();
    public void EndUpdate() => ComboBox.EndUpdate();
}

/// <summary>Hosted progress bar (cf. ToolStripProgressBar).</summary>
public class ToolStripProgressBar : ToolStripControlHost
{
    public ToolStripProgressBar()
        : base(new ProgressBar())
    {
    }

    public ProgressBar ProgressBar => (ProgressBar)Control;

    public int Minimum
    {
        get => ProgressBar.Minimum;
        set => ProgressBar.Minimum = value;
    }

    public int Maximum
    {
        get => ProgressBar.Maximum;
        set => ProgressBar.Maximum = value;
    }

    public int Value
    {
        get => ProgressBar.Value;
        set => ProgressBar.Value = value;
    }

    public ProgressBarStyle Style
    {
        get => ProgressBar.Style;
        set => ProgressBar.Style = value;
    }

    public int Step
    {
        get => ProgressBar.Step;
        set => ProgressBar.Step = value;
    }
}

/// <summary>Strip renderer (cf. ToolStripRenderer): virtual render hooks.
/// Base hooks paint nothing; custom renderers (including rewritten app
/// subclasses) override them. Strip chrome is drawn by the caller helpers
/// below; item faces await canvas text/font metrics.</summary>
public abstract class ToolStripRenderer
{
    protected virtual void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
    }

    protected virtual void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderGrip(ToolStripGripRenderEventArgs e)
    {
    }

    protected virtual void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
    }

    protected virtual void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
    }

    protected virtual void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
    }

    protected virtual void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
    }

    protected virtual void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
    }

    protected virtual void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
    {
    }

    protected virtual void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e)
    {
    }

    protected virtual void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
    }

    protected virtual void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
    }

    /// <summary>Public draw entry points (cf. ToolStripRenderer.Draw*):
    /// each forwards to its OnRender hook.</summary>
    public virtual void DrawArrow(ToolStripArrowRenderEventArgs e) => OnRenderArrow(e);
    public virtual void DrawButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderButtonBackground(e);
    public virtual void DrawDropDownButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderDropDownButtonBackground(e);
    public virtual void DrawGrip(ToolStripGripRenderEventArgs e) => OnRenderGrip(e);
    public virtual void DrawImageMargin(ToolStripRenderEventArgs e) => OnRenderImageMargin(e);
    public virtual void DrawItemBackground(ToolStripItemRenderEventArgs e) => OnRenderItemBackground(e);
    public virtual void DrawItemCheck(ToolStripItemImageRenderEventArgs e) => OnRenderItemCheck(e);
    public virtual void DrawItemImage(ToolStripItemImageRenderEventArgs e) => OnRenderItemImage(e);
    public virtual void DrawItemText(ToolStripItemTextRenderEventArgs e) => OnRenderItemText(e);
    public virtual void DrawLabelBackground(ToolStripItemRenderEventArgs e) => OnRenderLabelBackground(e);
    public virtual void DrawMenuItemBackground(ToolStripItemRenderEventArgs e) => OnRenderMenuItemBackground(e);
    public virtual void DrawOverflowButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderOverflowButtonBackground(e);
    public virtual void DrawSeparator(ToolStripSeparatorRenderEventArgs e) => OnRenderSeparator(e);
    public virtual void DrawSplitButton(ToolStripItemRenderEventArgs e) => OnRenderSplitButtonBackground(e);
    public virtual void DrawStatusStripSizingGrip(ToolStripRenderEventArgs e) => OnRenderStatusStripSizingGrip(e);
    public virtual void DrawToolStripBackground(ToolStripRenderEventArgs e) => OnRenderToolStripBackground(e);
    public virtual void DrawToolStripBorder(ToolStripRenderEventArgs e) => OnRenderToolStripBorder(e);
    public virtual void DrawToolStripPanelBackground(ToolStripPanelRenderEventArgs e) => OnRenderToolStripPanelBackground(e);

    protected virtual void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
    {
    }

    public static WfDraw::System.Drawing.Image CreateDisabledImage(WfDraw::System.Drawing.Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var gray = new WfDraw::System.Drawing.Bitmap(image.Width, image.Height);
        using (WfDraw::System.Drawing.Graphics g = WfDraw::System.Drawing.Graphics.FromImage(gray))
            g.DrawImage(image, 0, 0);
        for (int y = 0; y < gray.Height; y++)
        {
            for (int x = 0; x < gray.Width; x++)
            {
                WfDraw::System.Drawing.Color c = gray.GetPixel(x, y);
                int luminosity = ((c.R * 77) + (c.G * 150) + (c.B * 29)) >> 8;
                gray.SetPixel(x, y, WfDraw::System.Drawing.Color.FromArgb(c.A, luminosity, luminosity, luminosity));
            }
        }

        return gray;
    }

    /// <summary>Strip chrome dispatch used by ToolStrip paint.</summary>
    internal void RenderStripBackground(ToolStrip strip, ICanvas canvas, int ax, int ay)
    {
        var args = new ToolStripRenderEventArgs(strip);
        OnRenderToolStripBackground(args);
        canvas.FillRectangle(ax, ay, strip.Width, strip.Height, SystemColors.Control);
        OnRenderToolStripBorder(args);
        ControlPaintEdges.DrawBorder(canvas, ax, ay, strip.Width, strip.Height,
            SystemColors.ControlDarkDark, ControlPaintEdges.LineBorderStyle.Solid);
    }

    /// <summary>Item dispatch used by ToolStrip paint.</summary>
    internal void RenderItem(ToolStrip strip, ToolStripItem item, ICanvas canvas, int ax, int ay)
    {
        var args = new ToolStripItemRenderEventArgs(item);
        switch (item)
        {
            case ToolStripSplitButton:
                OnRenderSplitButtonBackground(args);
                break;
            case ToolStripDropDownButton:
                OnRenderDropDownButtonBackground(args);
                break;
            case ToolStripMenuItem:
                OnRenderMenuItemBackground(args);
                break;
            default:
                OnRenderButtonBackground(args);
                break;
        }

        item.NotifyPaint(new PaintEventArgs(item.Bounds));
    }
}

/// <summary>Professional renderer (cf. ToolStripProfessionalRenderer).</summary>
public class ToolStripProfessionalRenderer : ToolStripRenderer
{
    public ToolStripProfessionalRenderer()
        : this(new ProfessionalColorTable())
    {
    }

    /// <summary>Creates a renderer over a color table (cf. ToolStripProfessionalRenderer(ProfessionalColorTable)).</summary>
    public ToolStripProfessionalRenderer(ProfessionalColorTable professionalColorTable)
    {
        ColorTable = professionalColorTable;
    }

    /// <summary>Color table (cf. ColorTable).</summary>
    public ProfessionalColorTable ColorTable { get; }

    public bool RoundedEdges { get; set; } = true;
}

/// <summary>Professional color table (cf. ProfessionalColorTable).</summary>
public class ProfessionalColorTable
{
    /// <summary>Creates a color table (cf. ProfessionalColorTable()).</summary>
    public ProfessionalColorTable()
    {
    }

    /// <summary>System-color switch (cf. UseSystemColors).</summary>
    public bool UseSystemColors { get; set; } = true;
}

/// <summary>Render data: strip (cf. ToolStripRenderEventArgs).</summary>
public class ToolStripRenderEventArgs : EventArgs
{
    public ToolStripRenderEventArgs(ToolStrip? toolStrip)
        : this(toolStrip, WfDraw::System.Drawing.Rectangle.Empty, WfDraw::System.Drawing.Color.Empty)
    {
    }

    public ToolStripRenderEventArgs(WfDraw::System.Drawing.Graphics? graphics, ToolStrip? toolStrip)
        : this(toolStrip)
    {
        Graphics = graphics;
    }

    public ToolStripRenderEventArgs(WfDraw::System.Drawing.Graphics? graphics, ToolStrip? toolStrip, WfDraw::System.Drawing.Rectangle affectedBounds, WfDraw::System.Drawing.Color backColor)
        : this(toolStrip, affectedBounds, backColor)
    {
        Graphics = graphics;
    }

    public ToolStripRenderEventArgs(ToolStrip? toolStrip, WfDraw::System.Drawing.Rectangle affectedBounds, WfDraw::System.Drawing.Color backColor)
    {
        ToolStrip = toolStrip;
        AffectedBounds = affectedBounds;
        BackColor = backColor;
    }

    public WfDraw::System.Drawing.Graphics? Graphics { get; set; }
    public ToolStrip? ToolStrip { get; }
    public WfDraw::System.Drawing.Rectangle AffectedBounds { get; }
    public WfDraw::System.Drawing.Rectangle ConnectedArea { get; set; }
    public WfDraw::System.Drawing.Color BackColor { get; set; }
}

/// <summary>Render data: grip (cf. ToolStripGripRenderEventArgs).</summary>
public class ToolStripGripRenderEventArgs : ToolStripRenderEventArgs
{
    public ToolStripGripRenderEventArgs(ToolStrip? toolStrip, WfDraw::System.Drawing.Rectangle gripBounds)
        : base(toolStrip, gripBounds, WfDraw::System.Drawing.Color.Empty)
    {
        GripBounds = gripBounds;
    }

    public WfDraw::System.Drawing.Rectangle GripBounds { get; }
    public ToolStripGripStyle GripStyle { get; set; } = ToolStripGripStyle.Visible;
    public int GripDisplayWidth { get; set; } = 8;
}

/// <summary>Render data: item (cf. ToolStripItemRenderEventArgs).</summary>
public class ToolStripItemRenderEventArgs : EventArgs
{
    public ToolStripItemRenderEventArgs(ToolStripItem? item)
    {
        Item = item;
    }

    public ToolStripItemRenderEventArgs(WfDraw::System.Drawing.Graphics? graphics, ToolStripItem? item)
        : this(item)
    {
        Graphics = graphics;
    }

    public WfDraw::System.Drawing.Graphics? Graphics { get; set; }
    public ToolStripItem? Item { get; }
    public ToolStrip? ToolStrip => Item?.Parent;
}

/// <summary>Render data: check/image (cf. ToolStripItemImageRenderEventArgs).</summary>
public class ToolStripItemImageRenderEventArgs : ToolStripItemRenderEventArgs
{
    public ToolStripItemImageRenderEventArgs(ToolStripItem? item, WfDraw::System.Drawing.Rectangle imageRectangle)
        : base(item)
    {
        ImageRectangle = imageRectangle;
        Image = item?.Image;
    }

    public ToolStripItemImageRenderEventArgs(WfDraw::System.Drawing.Graphics? graphics, ToolStripItem? item, WfDraw::System.Drawing.Rectangle imageRectangle)
        : base(graphics, item)
    {
        ImageRectangle = imageRectangle;
        Image = item?.Image;
    }

    public WfDraw::System.Drawing.Rectangle ImageRectangle { get; }
    public WfDraw::System.Drawing.Image? Image { get; set; }
}

/// <summary>Render data: text (cf. ToolStripItemTextRenderEventArgs).</summary>
public class ToolStripItemTextRenderEventArgs : ToolStripItemRenderEventArgs
{
    public ToolStripItemTextRenderEventArgs(
        ToolStripItem? item,
        string text,
        WfDraw::System.Drawing.Font? textFont,
        WfDraw::System.Drawing.Rectangle textRectangle,
        WfDraw::System.Drawing.Color textColor,
        TextFormatFlags textFormat,
        ToolStripTextDirection textDirection)
        : base(item)
    {
        Text = text;
        TextFont = textFont;
        TextRectangle = textRectangle;
        TextColor = textColor;
        TextFormat = textFormat;
        TextDirection = textDirection;
    }

    public string Text { get; set; }
    public WfDraw::System.Drawing.Font? TextFont { get; set; }
    public WfDraw::System.Drawing.Rectangle TextRectangle { get; set; }
    public WfDraw::System.Drawing.Color TextColor { get; set; }
    public TextFormatFlags TextFormat { get; set; }
    public ToolStripTextDirection TextDirection { get; set; }
}

/// <summary>Render data: arrow (cf. ToolStripArrowRenderEventArgs).</summary>
public class ToolStripArrowRenderEventArgs : EventArgs
{
    public ToolStripArrowRenderEventArgs(
        WfDraw::System.Drawing.Graphics? graphics,
        ToolStripItem? item,
        WfDraw::System.Drawing.Rectangle arrowRectangle,
        ArrowDirection direction)
        : this(item, arrowRectangle, WfDraw::System.Drawing.Color.Empty, direction)
    {
        Graphics = graphics;
    }

    public ToolStripArrowRenderEventArgs(
        WfDraw::System.Drawing.Graphics? graphics,
        ToolStripItem? item,
        WfDraw::System.Drawing.Rectangle arrowRectangle,
        WfDraw::System.Drawing.Color arrowColor,
        ArrowDirection direction)
        : this(item, arrowRectangle, arrowColor, direction)
    {
        Graphics = graphics;
    }

    public ToolStripArrowRenderEventArgs(
        ToolStripItem? item,
        WfDraw::System.Drawing.Rectangle arrowRectangle,
        ArrowDirection direction)
        : this(item, arrowRectangle, WfDraw::System.Drawing.Color.Empty, direction)
    {
    }

    public ToolStripArrowRenderEventArgs(
        ToolStripItem? item,
        WfDraw::System.Drawing.Rectangle arrowRectangle,
        WfDraw::System.Drawing.Color arrowColor,
        ArrowDirection direction)
    {
        Item = item;
        ArrowRectangle = arrowRectangle;
        ArrowColor = arrowColor;
        Direction = direction;
    }

    public WfDraw::System.Drawing.Graphics? Graphics { get; set; }
    public ToolStripItem? Item { get; }
    public WfDraw::System.Drawing.Rectangle ArrowRectangle { get; }
    public WfDraw::System.Drawing.Color ArrowColor { get; set; }
    public ArrowDirection Direction { get; }
}

/// <summary>Render data: dock panel (cf. ToolStripPanelRenderEventArgs).</summary>
public class ToolStripPanelRenderEventArgs : ToolStripRenderEventArgs
{
    public ToolStripPanelRenderEventArgs(ToolStripPanel? toolStripPanel)
        : base((ToolStrip?)null)
    {
        ToolStripPanel = toolStripPanel;
    }

    public ToolStripPanelRenderEventArgs(WfDraw::System.Drawing.Graphics? graphics, ToolStripPanel? toolStripPanel)
        : base(graphics, null)
    {
        ToolStripPanel = toolStripPanel;
    }

    public ToolStripPanel? ToolStripPanel { get; }
    public bool Handled { get; set; }
}

/// <summary>Render data: separator (cf. ToolStripSeparatorRenderEventArgs).</summary>
public class ToolStripSeparatorRenderEventArgs : ToolStripItemRenderEventArgs
{
    public ToolStripSeparatorRenderEventArgs(ToolStripItem? item, bool vertical)
        : base(item)
    {
        Vertical = vertical;
    }

    public bool Vertical { get; }
}

/// <summary>Strip separator (cf. ToolStripSeparator).</summary>
public class ToolStripSeparator : ToolStripItem
{
    public ToolStripSeparator()
    {
        Text = "-";
    }

    public override WfDraw::System.Drawing.Size GetPreferredSize(WfDraw::System.Drawing.Size constrainingSize) =>
        new(6, 6);
}

/// <summary>Strip text (cf. ToolStripLabel).</summary>
public class ToolStripLabel : ToolStripItem
{
    public ToolStripLabel()
    {
    }

    public ToolStripLabel(string text)
        : base(text)
    {
    }

    public bool IsLink { get; set; }
}

/// <summary>Strip text box (cf. ToolStripTextBox).</summary>
public class ToolStripTextBox : ToolStripControlHost
{
    public ToolStripTextBox()
        : base(new TextBox())
    {
    }

    public TextBox TextBox => (TextBox)Control;
    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;

    public HorizontalAlignment TextBoxTextAlign
    {
        get => TextBox.TextAlign;
        set => TextBox.TextAlign = value;
    }

    public override string Text
    {
        get => TextBox.Text;
        set => TextBox.Text = value;
    }

    public override WfDraw::System.Drawing.Size GetPreferredSize(WfDraw::System.Drawing.Size constrainingSize) =>
        new(TextBox.Width, TextBox.Height);
}

/// <summary>Drop-down menu (cf. ToolStripDropDownMenu).</summary>
public class ToolStripDropDownMenu : ToolStripDropDown
{
    public ToolStripDropDownMenu()
    {
        LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
    }

    public bool ShowCheckMargin { get; set; }
    public bool ShowImageMargin { get; set; } = true;
}

/// <summary>Context menu (cf. ContextMenuStrip).</summary>
public class ContextMenuStrip : ToolStripDropDownMenu
{
    public ContextMenuStrip()
    {
    }

    public ContextMenuStrip(System.ComponentModel.IContainer? container)
    {
        container?.Add(this);
    }

    public Control? SourceControl { get; set; }
}

/// <summary>Professional theme colors (cf. ProfessionalColors): theme
/// approximations backing custom renderers until OS theme reads exist.</summary>
public static class ProfessionalColors
{
    public static WfDraw::System.Drawing.Color MenuStripGradientBegin =>
        WfDraw::System.Drawing.Color.FromArgb(246, 246, 246);

    public static WfDraw::System.Drawing.Color MenuStripGradientEnd =>
        WfDraw::System.Drawing.Color.FromArgb(214, 221, 228);

    public static WfDraw::System.Drawing.Color StatusStripGradientBegin =>
        WfDraw::System.Drawing.Color.FromArgb(235, 238, 242);

    public static WfDraw::System.Drawing.Color StatusStripGradientEnd =>
        WfDraw::System.Drawing.Color.FromArgb(214, 221, 228);
}

/// <summary>Theme part handles (cf. VisualStyles.VisualStyleElement).
/// Class/part/state numbers match the Win32 theme schema; actual themed
/// bitmaps arrive with the renderer theming milestone.</summary>
public class VisualStyleElement
{
    private VisualStyleElement(string className, int part, int state)
    {
        ClassName = className;
        Part = part;
        State = state;
    }

    public string ClassName { get; }
    public int Part { get; }
    public int State { get; }

    public static VisualStyleElement CreateElement(string className, int part, int state) =>
        new(className, part, state);

    /// <summary>Window parts (cf. VisualStyleElement.Window).</summary>
    public static class Window
    {
        /// <summary>Caption parts (cf. VisualStyleElement.Window.Caption).</summary>
        public static class Caption
        {
            private const string Class = "WINDOW";
            private const int Part = 1;

            public static VisualStyleElement Active => new(Class, Part, 1);
            public static VisualStyleElement Inactive => new(Class, Part, 2);
            public static VisualStyleElement Disabled => new(Class, Part, 3);
        }

        /// <summary>Frame parts (cf. VisualStyleElement.Window.Frame).</summary>
        public static class Frame
        {
            private const string Class = "WINDOW";

            public static VisualStyleElement Bottom => new(Class, 9, 1);
            public static VisualStyleElement Left => new(Class, 7, 1);
            public static VisualStyleElement Right => new(Class, 8, 1);
        }
    }

    /// <summary>Button parts (cf. VisualStyleElement.Button).</summary>
    public static class Button
    {
        /// <summary>Push-button states (cf. VisualStyleElement.Button.PushButton).</summary>
        public static class PushButton
        {
            private const string Class = "BUTTON";
            private const int Part = 1;

            public static VisualStyleElement Normal => new(Class, Part, 1);
            public static VisualStyleElement Hot => new(Class, Part, 2);
            public static VisualStyleElement Pressed => new(Class, Part, 3);
            public static VisualStyleElement Disabled => new(Class, Part, 4);
            public static VisualStyleElement Default => new(Class, Part, 5);
        }

        /// <summary>Check-box states (cf. VisualStyleElement.Button.CheckBox).</summary>
        public static class CheckBox
        {
            private const string Class = "BUTTON";
            private const int Part = 3;

            public static VisualStyleElement UncheckedNormal => new(Class, Part, 1);
            public static VisualStyleElement UncheckedHot => new(Class, Part, 2);
            public static VisualStyleElement UncheckedPressed => new(Class, Part, 3);
            public static VisualStyleElement UncheckedDisabled => new(Class, Part, 4);
            public static VisualStyleElement CheckedNormal => new(Class, Part, 5);
            public static VisualStyleElement CheckedHot => new(Class, Part, 6);
            public static VisualStyleElement CheckedPressed => new(Class, Part, 7);
            public static VisualStyleElement CheckedDisabled => new(Class, Part, 8);
        }

        /// <summary>Radio-button states (cf. VisualStyleElement.Button.RadioButton).</summary>
        public static class RadioButton
        {
            private const string Class = "BUTTON";
            private const int Part = 2;

            public static VisualStyleElement UncheckedNormal => new(Class, Part, 1);
            public static VisualStyleElement UncheckedHot => new(Class, Part, 2);
            public static VisualStyleElement UncheckedPressed => new(Class, Part, 3);
            public static VisualStyleElement UncheckedDisabled => new(Class, Part, 4);
            public static VisualStyleElement CheckedNormal => new(Class, Part, 5);
            public static VisualStyleElement CheckedHot => new(Class, Part, 6);
            public static VisualStyleElement CheckedPressed => new(Class, Part, 7);
            public static VisualStyleElement CheckedDisabled => new(Class, Part, 8);
        }
    }

    /// <summary>Spin parts (cf. VisualStyleElement.Spin).</summary>
    public static class Spin
    {
        private const string Class = "SPIN";

        /// <summary>Up button (cf. VisualStyleElement.Spin.Up).</summary>
        public static class Up
        {
            public static VisualStyleElement Normal => new(Class, 1, 1);
            public static VisualStyleElement Hot => new(Class, 1, 2);
            public static VisualStyleElement Pressed => new(Class, 1, 3);
            public static VisualStyleElement Disabled => new(Class, 1, 4);
        }

        /// <summary>Down button (cf. VisualStyleElement.Spin.Down).</summary>
        public static class Down
        {
            public static VisualStyleElement Normal => new(Class, 2, 1);
            public static VisualStyleElement Hot => new(Class, 2, 2);
            public static VisualStyleElement Pressed => new(Class, 2, 3);
            public static VisualStyleElement Disabled => new(Class, 2, 4);
        }
    }

    /// <summary>Text-box parts (cf. VisualStyleElement.TextBox).</summary>
    public static class TextBox
    {
        /// <summary>Edit states (cf. VisualStyleElement.TextBox.TextEdit).</summary>
        public static class TextEdit
        {
            private const string Class = "EDIT";
            private const int Part = 1;

            public static VisualStyleElement Normal => new(Class, Part, 1);
            public static VisualStyleElement Hot => new(Class, Part, 2);
            public static VisualStyleElement Selected => new(Class, Part, 3);
            public static VisualStyleElement Disabled => new(Class, Part, 4);
            public static VisualStyleElement Focused => new(Class, Part, 5);
            public static VisualStyleElement ReadOnly => new(Class, Part, 6);
            public static VisualStyleElement Assist => new(Class, Part, 7);
        }
    }

    /// <summary>Track-bar parts (cf. VisualStyleElement.TrackBar).</summary>
    public static class TrackBar
    {
        private const string Class = "TRACKBAR";

        /// <summary>Bottom thumb (cf. VisualStyleElement.TrackBar.ThumbBottom).</summary>
        public static class ThumbBottom
        {
            public static VisualStyleElement Normal => new(Class, 3, 1);
            public static VisualStyleElement Hot => new(Class, 3, 2);
            public static VisualStyleElement Pressed => new(Class, 3, 3);
            public static VisualStyleElement Focused => new(Class, 3, 4);
            public static VisualStyleElement Disabled => new(Class, 3, 5);
        }

        /// <summary>Track (cf. VisualStyleElement.TrackBar.Track).</summary>
        public static class Track
        {
            public static VisualStyleElement Normal => new(Class, 1, 1);
        }
    }

    /// <summary>Task-band parts (cf. VisualStyleElement.TaskBand).</summary>
    public static class TaskBand
    {
        /// <summary>Content-pane parts (cf. VisualStyleElement.TaskBand.ContentPane).</summary>
        public static class ContentPane
        {
            private const string Class = "TASKBAND";

            public static VisualStyleElement Fill => new(Class, 8, 1);
        }
    }
}

/// <summary>Theme deployment facts (cf. VisualStyles.VisualStyleInformation).
/// The web backend has no OS theme: styles are unsupported and disabled.</summary>
public static class VisualStyleInformation
{
    /// <summary>Whether the OS supports themes (cf. IsSupportedByOS). False.</summary>
    public static bool IsSupportedByOS => false;

    /// <summary>Whether the user enabled themes (cf. IsEnabledByUser). False.</summary>
    public static bool IsEnabledByUser => false;
}

/// <summary>Strip row (cf. ToolStripPanelRow). Row bounds are assigned by
/// layout; until then Bounds stays empty and PointToRow finds no row.</summary>
public class ToolStripPanelRow : System.ComponentModel.Component
{
    public ToolStripPanelRow()
    {
    }

    public ToolStripPanelRow(ToolStripPanel parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
    }

    public WfDraw::System.Drawing.Rectangle Bounds { get; set; }
    public List<ToolStrip> Controls { get; } = new();
}

/// <summary>Strip dock panel (cf. ToolStripPanel). Joined strips parent to
/// the panel and collect into rows; row bounds arrive with layout.</summary>
public class ToolStripPanel : ContainerControl
{
    private readonly List<ToolStripPanelRow> _rows = new();

    public ToolStripPanel()
    {
    }

    public Orientation Orientation { get; set; } = Orientation.Horizontal;
    public bool Locked { get; set; }
    public ToolStripRenderer? Renderer { get; set; }
    public ToolStripRenderMode RenderMode { get; set; } = ToolStripRenderMode.ManagerRenderMode;
    public Padding RowMargin { get; set; } = new(3, 0, 0, 0);

    public ToolStripPanelRow[] Rows => _rows.ToArray();

    public void Join(ToolStrip toolStrip) => Join(toolStrip, 0);

    public void Join(ToolStrip toolStrip, int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(toolStrip);
        while (_rows.Count <= rowIndex)
            _rows.Add(new ToolStripPanelRow(this));
        _rows[rowIndex].Controls.Add(toolStrip);
        toolStrip.SetParent(this);
    }

    public void Join(ToolStrip toolStrip, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(toolStrip);
        toolStrip.Location = new WfDraw::System.Drawing.Point(x, y);
        Join(toolStrip);
    }

    public void Join(ToolStrip toolStrip, WfDraw::System.Drawing.Point location) =>
        Join(toolStrip, location.X, location.Y);

    public ToolStripPanelRow? PointToRow(WfDraw::System.Drawing.Point clientLocation)
    {
        foreach (ToolStripPanelRow row in _rows)
        {
            if (!row.Bounds.IsEmpty && row.Bounds.Contains(clientLocation))
                return row;
        }

        return null;
    }
}
