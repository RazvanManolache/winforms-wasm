extern alias WfDraw;

namespace WfWasm.Shim;

using Draw = WfDraw::System.Drawing;

/// <summary>
/// Group box (cf. System.Windows.Forms.GroupBox): etched (classic) or single
/// (themed) edge with the caption interrupting the top line.
/// </summary>
public class GroupBox : Control
{
    public GroupBox()
        : this("")
    {
    }

    public GroupBox(string name = "")
        : base(name)
    {
    }

    public string FontCss { get; set; } = Palette.UiFontCss;
    public bool UseMnemonic { get; set; } = true;

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        float lineH = TextRules.EstimateLineHeight(FontCss);
        int top = (int)(lineH / 2);
        // Caption gap estimate (text metrics arrive in a later stage).
        float gapW = 12 + (Text.Length * TextRules.EstimateLineHeight(FontCss) * 0.45f);

        if (themed)
        {
            canvas.FillRectangle(ax, ay + top, Width, Height - top, SystemColors.Control);
            ControlPaintEdges.DrawBorder(canvas, ax, ay + top, Width, Height - top,
                SystemColors.ThemedGroupBorder, ControlPaintEdges.LineBorderStyle.Solid);
            canvas.FillRectangle(ax + 8, ay, (int)gapW, (int)lineH, SystemColors.Control);
        }
        else
        {
            canvas.FillRectangle(ax, ay, Width, Height, SystemColors.Control);
            // Etched edge with a caption gap on the top line.
            ControlPaintEdges.DrawEdge(canvas, ax, ay + top, 8, Height - top,
                ControlPaintEdges.EdgeStyle.Etched, ControlPaintEdges.EdgeSides.All);
            var sides = ControlPaintEdges.EdgeSides.Left | ControlPaintEdges.EdgeSides.Right | ControlPaintEdges.EdgeSides.Bottom;
            ControlPaintEdges.DrawEdge(canvas, ax + 8 + (int)gapW, ay + top, Width - 8 - (int)gapW, Height - top,
                ControlPaintEdges.EdgeStyle.Etched, sides);
            canvas.FillRectangle(ax, ay + top, 8, 1, SystemColors.Control);
            ControlPaintEdges.DrawEdge(canvas, ax, ay + top, 8, 1,
                ControlPaintEdges.EdgeStyle.Etched, ControlPaintEdges.EdgeSides.Top);
        }

        if (!string.IsNullOrEmpty(Text))
            TextRules.DrawAlignedText(canvas, Text, UseMnemonic, Draw.ContentAlignment.TopLeft,
                ax + 8, ay, gapW, lineH, FontCss,
                Enabled ? SystemColors.ControlText : SystemColors.GrayText, !Enabled);
    }

    protected override (int cx, int cy) ToClientPoint(int lx, int ly)
    {
        float lineH = TextRules.EstimateLineHeight(FontCss);
        return (lx - 8, ly - (int)(lineH / 2) - 8);
    }

    protected override (int lx, int ly) ToLocalPoint(int cx, int cy)
    {
        float lineH = TextRules.EstimateLineHeight(FontCss);
        return (cx + 8, cy + (int)(lineH / 2) + 8);
    }

    protected override void PaintChildren(ICanvas canvas, int ax, int ay)
    {
        float lineH = TextRules.EstimateLineHeight(FontCss);
        int oy = ay + (int)(lineH / 2) + 8;
        foreach (Control child in Children)
            PaintChild(canvas, child, ax + 8, oy);
    }
}

/// <summary>Tab page (cf. System.Windows.Forms.TabPage): header text + content children.</summary>
public class TabPage : Control
{
    public TabPage()
        : this("")
    {
    }

    public TabPage(string name = "")
        : base(name)
    {
    }

    public string FontCss { get; set; } = Palette.UiFontCss;
    public bool UseMnemonic { get; set; } = true;

    /// <summary>Tab tooltip (cf. ToolTipText).</summary>
    public string ToolTipText { get; set; } = string.Empty;

    /// <summary>Themed background switch (cf. UseVisualStyleBackColor). Stored only.</summary>
    public bool UseVisualStyleBackColor { get; set; }
}

/// <summary>
/// Tab control, top tabs only (cf. System.Windows.Forms.TabControl):
/// selected tab merges into the page; clicking a tab selects it.
/// </summary>
public class TabControl : Control
{
    public const int TabHeight = 22;

    public TabPageCollection TabPages { get; }
    public string FontCss { get; set; } = Palette.UiFontCss;

    /// <summary>Tab row edge (cf. Alignment).</summary>
    public TabAlignment Alignment { get; set; } = TabAlignment.Top;

    /// <summary>Hot-tracking switch (cf. HotTrack). Stored only.</summary>
    public bool HotTrack { get; set; }

    /// <summary>Multi-row switch (cf. Multiline). Stored only; one row paints.</summary>
    public bool Multiline { get; set; }

    /// <summary>Tooltip switch (cf. ShowToolTips). Stored only.</summary>
    public bool ShowToolTips { get; set; }

    /// <summary>Page count (cf. TabCount).</summary>
    public int TabCount => TabPages.Count;

    public TabControl(string name = "")
        : this()
    {
        Name = name;
    }

    public TabControl()
    {
        TabPages = new TabPageCollection(this);
    }

    private int _selectedIndex = -1;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = TabPages.Count == 0 ? -1 : Math.Clamp(value, 0, TabPages.Count - 1);
            if (clamped == _selectedIndex)
                return;
            _selectedIndex = clamped;
            OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    public TabPage? SelectedTab =>
        SelectedIndex >= 0 && SelectedIndex < TabPages.Count ? TabPages[SelectedIndex] : null;

    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raises <c>SelectedIndexChanged</c> (cf. OnSelectedIndexChanged).</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) =>
        SelectedIndexChanged?.Invoke(this, e);

    /// <summary>Tab header bounds (cf. GetTabRect).</summary>
    public WfDraw::System.Drawing.Rectangle GetTabRect(int index)
    {
        var bounds = TabBounds();
        if (index < 0 || index >= bounds.Count)
            return WfDraw::System.Drawing.Rectangle.Empty;
        return new WfDraw::System.Drawing.Rectangle(bounds[index].X, 0, bounds[index].Width, TabHeight);
    }

    public void AddTabPage(TabPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        TabPages.Add(page);
        if (_selectedIndex == -1)
            _selectedIndex = 0;
        LayoutPages();
    }

    internal void OnPagesChanged() => LayoutPages();

    /// <summary>Page list (cf. TabControl.TabPageCollection).</summary>
    public class TabPageCollection : System.Collections.ObjectModel.Collection<TabPage>
    {
        private readonly TabControl _owner;

        /// <summary>Creates a page list (cf. TabPageCollection(TabControl)).</summary>
        public TabPageCollection(TabControl owner)
        {
            _owner = owner;
        }

        /// <summary>Adds a page by header text (cf. Add(string)).</summary>
        public void Add(string text) => Add(new TabPage { Text = text });

        /// <summary>Adds a page by key and text (cf. Add(string, string)).</summary>
        public void Add(string key, string text) => Add(new TabPage { Name = key, Text = text });

        /// <summary>Adds a page by key, text and image (cf. Add(string, string, int)).</summary>
        public void Add(string key, string text, int imageIndex) => Add(new TabPage { Name = key, Text = text });

        /// <summary>Adds a page by key, text and image key (cf. Add(string, string, string)).</summary>
        public void Add(string key, string text, string imageKey) => Add(new TabPage { Name = key, Text = text });

        /// <summary>Adds pages (cf. AddRange).</summary>
        public void AddRange(TabPage[] pages)
        {
            ArgumentNullException.ThrowIfNull(pages);
            foreach (TabPage page in pages)
                Add(page);
        }

        /// <summary>Whether a key is listed (cf. ContainsKey).</summary>
        public virtual bool ContainsKey(string key) =>
            this.Any(p => p.Name == key);

        /// <summary>Index of a key (cf. IndexOfKey).</summary>
        public virtual int IndexOfKey(string key)
        {
            for (int i = 0; i < Count; i++)
                if (this[i].Name == key)
                    return i;
            return -1;
        }

        /// <summary>Inserts a page by text (cf. Insert(int, string)).</summary>
        public void Insert(int index, string text) => Insert(index, new TabPage { Text = text });

        /// <summary>Inserts a page by key and text (cf. Insert(int, string, string)).</summary>
        public void Insert(int index, string key, string text) =>
            Insert(index, new TabPage { Name = key, Text = text });

        /// <summary>Inserts a page by key, text and image (cf. Insert(int, string, string, int)).</summary>
        public void Insert(int index, string key, string text, int imageIndex) =>
            Insert(index, new TabPage { Name = key, Text = text });

        /// <summary>Page by key (cf. this[string]).</summary>
        public virtual TabPage? this[string key] =>
            this.FirstOrDefault(p => p.Name == key);

        /// <inheritdoc/>
        protected override void InsertItem(int index, TabPage item)
        {
            ArgumentNullException.ThrowIfNull(item);
            base.InsertItem(index, item);
            _owner.AddChild(item);
            _owner.OnPagesChanged();
        }

        /// <inheritdoc/>
        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);
            _owner.OnPagesChanged();
        }

        /// <inheritdoc/>
        protected override void SetItem(int index, TabPage item)
        {
            ArgumentNullException.ThrowIfNull(item);
            base.SetItem(index, item);
            _owner.AddChild(item);
            _owner.OnPagesChanged();
        }

        /// <inheritdoc/>
        protected override void ClearItems()
        {
            base.ClearItems();
            _owner.OnPagesChanged();
        }
    }

    private void LayoutPages()
    {
        foreach (TabPage page in TabPages)
        {
            page.X = 4;
            page.Y = TabHeight + 4;
            page.Width = Math.Max(0, Width - 8);
            page.Height = Math.Max(0, Height - TabHeight - 8);
            page.Visible = page == SelectedTab;
        }
    }

    /// <summary>Tab header bounds in local space (widths estimated until metrics).</summary>
    public List<(int X, int Width)> TabBounds()
    {
        var bounds = new List<(int, int)>();
        int x = 4;
        foreach (TabPage page in TabPages)
        {
            int w = 18 + (int)(page.Text.Length * TextRules.EstimateLineHeight(FontCss) * 0.5f);
            bounds.Add((x, w));
            x += w;
        }

        return bounds;
    }

    public override void HandleClickLocal(int localX, int localY)
    {
        if (!Enabled)
            return;
        if (localY >= 0 && localY < TabHeight + 2)
        {
            var bounds = TabBounds();
            for (int i = 0; i < bounds.Count; i++)
            {
                if (localX >= bounds[i].X && localX < bounds[i].X + bounds[i].Width)
                {
                    int prev = SelectedIndex;
                    SelectedIndex = i;
                    LayoutPages();
                    if (SelectedIndex != prev)
                        base.HandleClickLocal(localX, localY);
                    return;
                }
            }
        }

        base.HandleClickLocal(localX, localY);
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        LayoutPages();
        var bounds = TabBounds();

        // Page area.
        int pageY = TabHeight;
        if (themed)
        {
            canvas.FillRectangle(ax, ay + pageY, Width, Height - pageY, SystemColors.Window);
            ControlPaintEdges.DrawBorder(canvas, ax, ay + pageY, Width, Height - pageY,
                SystemColors.ThemedTabBorder, ControlPaintEdges.LineBorderStyle.Solid);
        }
        else
        {
            canvas.FillRectangle(ax, ay + pageY, Width, Height - pageY, SystemColors.Control);
            ControlPaintEdges.DrawEdge(canvas, ax, ay + pageY, Width, Height - pageY,
                ControlPaintEdges.EdgeStyle.Raised);
        }

        // Tabs, right-to-left so the selected tab paints last (on top).
        for (int i = bounds.Count - 1; i >= 0; i--)
        {
            bool selected = i == SelectedIndex;
            int tx = bounds[i].X;
            int tw = bounds[i].Width;
            int ty = selected ? 0 : 2;
            int th = selected ? TabHeight + 2 : TabHeight;
            if (themed)
            {
                canvas.FillRectangle(ax + tx, ay + ty, tw, th,
                    selected ? SystemColors.Window : SystemColors.Control);
                var top = ControlPaintEdges.EdgeSides.Left | ControlPaintEdges.EdgeSides.Top | ControlPaintEdges.EdgeSides.Right;
                ControlPaintEdges.DrawEdge(canvas, ax + tx, ay + ty, tw, th,
                    ControlPaintEdges.EdgeStyle.Flat, top);
                if (!selected)
                    canvas.FillRectangle(ax + tx, ay + ty + th - 1, tw, 1,
                        SystemColors.ThemedTabBorder);
            }
            else
            {
                canvas.FillRectangle(ax + tx, ay + ty, tw, th, SystemColors.Control);
                var top = ControlPaintEdges.EdgeSides.Left | ControlPaintEdges.EdgeSides.Top | ControlPaintEdges.EdgeSides.Right;
                ControlPaintEdges.DrawEdge(canvas, ax + tx, ay + ty, tw, th,
                    ControlPaintEdges.EdgeStyle.Raised, top);
            }

            TextRules.DrawAlignedText(canvas, TabPages[i].Text, TabPages[i].UseMnemonic,
                Draw.ContentAlignment.MiddleCenter, ax + tx, ay + ty, tw, TabHeight,
                FontCss, Enabled ? SystemColors.ControlText : SystemColors.GrayText, !Enabled);
        }
    }
}

/// <summary>Completion strings (cf. AutoCompleteStringCollection).</summary>
public class AutoCompleteStringCollection : List<string>
{
    /// <summary>Adds a string (cf. Add(string)).</summary>
    public new int Add(string value)
    {
        base.Add(value);
        return Count - 1;
    }

    public void AddRange(string[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        base.AddRange(items);
    }
}

/// <summary>Shared item mechanics for list controls (cf. ObjectCollection
/// backing stores): insertion-sort when the owner sorts, string display.</summary>
public class ObjectList : System.Collections.IList
{
    private readonly List<object?> _items = new();
    private readonly Func<bool> _sorted;

    public ObjectList(Func<bool>? sorted = null)
    {
        _sorted = sorted ?? (() => false);
    }

    public int Add(object? value)
    {
        if (value is null)
            return -1;
        if (_sorted())
        {
            int at = _items.FindIndex(item => string.Compare(
                item?.ToString(), value.ToString(), StringComparison.Ordinal) > 0);
            if (at < 0)
                at = _items.Count;
            _items.Insert(at, value);
            return at;
        }

        _items.Add(value);
        return _items.Count - 1;
    }

    public void AddRange(object[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (object? item in items)
            Add(item);
    }

    public void CopyTo(object?[] array, int index) => _items.CopyTo(array, index);

    public virtual void Clear() => _items.Clear();
    public virtual bool Contains(object? value) => _items.Contains(value);
    public virtual int IndexOf(object? value) => _items.IndexOf(value);
    public virtual void Insert(int index, object? value) => _items.Insert(index, value);
    public virtual void Remove(object? value) => _items.Remove(value);
    public virtual void RemoveAt(int index) => _items.RemoveAt(index);
    public int Count => _items.Count;
    public object? this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public void Sort() => _items.Sort((a, b) => string.Compare(
        a?.ToString(), b?.ToString(), StringComparison.Ordinal));

    public virtual System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();

    int System.Collections.IList.Add(object? value) => Add(value);
    bool System.Collections.IList.IsReadOnly => false;
    bool System.Collections.IList.IsFixedSize => false;
    void System.Collections.ICollection.CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_items).CopyTo(array, index);
    bool System.Collections.ICollection.IsSynchronized => false;
    object System.Collections.ICollection.SyncRoot => this;
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>List selection contract (cf. System.Windows.Forms.ListControl).</summary>
public class ListControl : Control
{
    public ListControl(string name = "")
        : base(name)
    {
    }

    public ListControl()
        : this("")
    {
    }

    private int _selectedIndex = -1;

    public virtual int SelectedIndex
    {
        get => _selectedIndex;
        set => SetSelectedIndex(value);
    }

    protected void SetSelectedIndex(int value, int count)
    {
        int clamped = count == 0 ? -1 : Math.Clamp(value, -1, count - 1);
        if (clamped == _selectedIndex)
            return;
        _selectedIndex = clamped;
        OnSelectedIndexChanged(EventArgs.Empty);
        OnSelectedValueChanged(EventArgs.Empty);
    }

    private void SetSelectedIndex(int value) => SetSelectedIndex(value, SelectedCount);

    protected virtual int SelectedCount => 0;

    public virtual object? SelectedItem
    {
        get => null;
        set
        {
        }
    }

    public virtual object? SelectedValue => SelectedItem;

    public string? DisplayMember { get; set; }
    public string? ValueMember { get; set; }
    public bool FormattingEnabled { get; set; }

    public virtual string GetItemText(object? item)
    {
        if (item is null)
            return "";
        if (DisplayMember is not null)
        {
            var prop = item.GetType().GetProperty(DisplayMember);
            object? value = prop?.GetValue(item);
            if (value is not null)
                return value.ToString() ?? "";
        }

        return item.ToString() ?? "";
    }

    public event EventHandler? SelectedIndexChanged;
    public event EventHandler? SelectedValueChanged;

    protected virtual void OnSelectedIndexChanged(EventArgs e) => SelectedIndexChanged?.Invoke(this, e);
    protected virtual void OnSelectedValueChanged(EventArgs e) => SelectedValueChanged?.Invoke(this, e);
}

/// <summary>Plain panel (cf. System.Windows.Forms.Panel): bare container.</summary>
public class Panel : Control
{
    public Panel(string name = "")
        : base(name)
    {
    }

    public Panel()
        : this("")
    {
    }

    public BorderStyle BorderStyle { get; set; } = BorderStyle.None;
}

/// <summary>Grid cell position (cf. TableLayoutPanelCellPosition).</summary>
public struct TableLayoutPanelCellPosition : IEquatable<TableLayoutPanelCellPosition>
{
    public TableLayoutPanelCellPosition(int column, int row)
    {
        Column = column;
        Row = row;
    }

    public int Column { get; set; }
    public int Row { get; set; }

    public bool Equals(TableLayoutPanelCellPosition other) =>
        Column == other.Column && Row == other.Row;

    public override bool Equals(object? obj) =>
        obj is TableLayoutPanelCellPosition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Column, Row);
    public override string ToString() => $"{Column},{Row}";

    public static bool operator ==(TableLayoutPanelCellPosition left, TableLayoutPanelCellPosition right) =>
        left.Equals(right);

    public static bool operator !=(TableLayoutPanelCellPosition left, TableLayoutPanelCellPosition right) =>
        !left.Equals(right);
}

/// <summary>Grid sizing rule (cf. TableLayoutStyle).</summary>
public abstract class TableLayoutStyle
{
    public SizeType SizeType { get; set; } = SizeType.AutoSize;
}

/// <summary>Column sizing (cf. ColumnStyle).</summary>
public class ColumnStyle : TableLayoutStyle
{
    public ColumnStyle()
    {
    }

    public ColumnStyle(SizeType sizeType)
    {
        SizeType = sizeType;
    }

    public ColumnStyle(SizeType sizeType, float width)
    {
        SizeType = sizeType;
        Width = width;
    }

    public float Width { get; set; }
}

/// <summary>Row sizing (cf. RowStyle).</summary>
public class RowStyle : TableLayoutStyle
{
    public RowStyle()
    {
    }

    public RowStyle(SizeType sizeType)
    {
        SizeType = sizeType;
    }

    public RowStyle(SizeType sizeType, float height)
    {
        SizeType = sizeType;
        Height = height;
    }

    public float Height { get; set; }
}

/// <summary>Style row (cf. TableLayoutStyleCollection).</summary>
public class TableLayoutStyleCollection : System.Collections.IList
{
    private readonly List<TableLayoutStyle> _styles = new();

    public int Add(TableLayoutStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _styles.Add(style);
        return _styles.Count - 1;
    }

    public void AddRange(TableLayoutStyle[] styles)
    {
        ArgumentNullException.ThrowIfNull(styles);
        foreach (TableLayoutStyle style in styles)
            Add(style);
    }

    public virtual void Clear() => _styles.Clear();
    public void Remove(TableLayoutStyle style) => _styles.Remove(style);
    public virtual void RemoveAt(int index) => _styles.RemoveAt(index);
    public int Count => _styles.Count;

    public TableLayoutStyle this[int index]
    {
        get => _styles[index];
        set => _styles[index] = value;
    }

    int System.Collections.IList.Add(object? value) => Add((TableLayoutStyle)value!);
    bool System.Collections.IList.Contains(object? value) => value is TableLayoutStyle s && _styles.Contains(s);
    int System.Collections.IList.IndexOf(object? value) => value is TableLayoutStyle s ? _styles.IndexOf(s) : -1;
    void System.Collections.IList.Insert(int index, object? value) => _styles.Insert(index, (TableLayoutStyle)value!);
    void System.Collections.IList.Remove(object? value)
    {
        if (value is TableLayoutStyle s)
            _styles.Remove(s);
    }

    object? System.Collections.IList.this[int index]
    {
        get => _styles[index];
        set => _styles[index] = (TableLayoutStyle)value!;
    }

    bool System.Collections.IList.IsReadOnly => false;
    bool System.Collections.IList.IsFixedSize => false;
    void System.Collections.ICollection.CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_styles).CopyTo(array, index);
    bool System.Collections.ICollection.IsSynchronized => false;
    object System.Collections.ICollection.SyncRoot => this;
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        _styles.GetEnumerator();
}

/// <summary>Column styles (cf. TableLayoutColumnStyleCollection).</summary>
public class TableLayoutColumnStyleCollection : TableLayoutStyleCollection
{
    public int Add(ColumnStyle style) => base.Add(style);

    /// <summary>Column style by index (cf. this[int]).</summary>
    public new ColumnStyle this[int index]
    {
        get => (ColumnStyle)base[index];
        set => base[index] = value;
    }
}

/// <summary>Row styles (cf. TableLayoutRowStyleCollection).</summary>
public class TableLayoutRowStyleCollection : TableLayoutStyleCollection
{
    public int Add(RowStyle style) => base.Add(style);

    /// <summary>Row style by index (cf. this[int]).</summary>
    public new RowStyle this[int index]
    {
        get => (RowStyle)base[index];
        set => base[index] = value;
    }
}

/// <summary>Grid settings (cf. TableLayoutSettings).</summary>
public class TableLayoutSettings
{
    internal TableLayoutSettings()
    {
    }

    public int ColumnCount { get; set; } = 2;
    public int RowCount { get; set; } = 2;
    public TableLayoutPanelGrowStyle GrowStyle { get; set; } = TableLayoutPanelGrowStyle.AddRows;
    public TableLayoutColumnStyleCollection ColumnStyles { get; } = new();
    public TableLayoutRowStyleCollection RowStyles { get; } = new();
}

/// <summary>Grid panel (cf. TableLayoutPanel). Cell placement is tracked;
/// automatic cell layout arrives with the layout milestone.</summary>
public class TableLayoutPanel : Panel
{
    private readonly Dictionary<Control, TableLayoutPanelCellPosition> _positions = new();
    private readonly Dictionary<Control, int> _columnSpans = new();
    private readonly Dictionary<Control, int> _rowSpans = new();

    public TableLayoutPanel()
    {
        LayoutSettings = new TableLayoutSettings();
    }

    protected override ControlCollection CreateControlsInstance() =>
        new TableLayoutControlCollection(this);

    public new TableLayoutControlCollection Controls =>
        (TableLayoutControlCollection)Children;

    public TableLayoutSettings LayoutSettings { get; }

    public int ColumnCount
    {
        get => LayoutSettings.ColumnCount;
        set => LayoutSettings.ColumnCount = value;
    }

    public int RowCount
    {
        get => LayoutSettings.RowCount;
        set => LayoutSettings.RowCount = value;
    }

    public TableLayoutPanelGrowStyle GrowStyle
    {
        get => LayoutSettings.GrowStyle;
        set => LayoutSettings.GrowStyle = value;
    }

    public TableLayoutColumnStyleCollection ColumnStyles => LayoutSettings.ColumnStyles;
    public TableLayoutRowStyleCollection RowStyles => LayoutSettings.RowStyles;
    public TableLayoutPanelCellBorderStyle CellBorderStyle { get; set; } = TableLayoutPanelCellBorderStyle.None;

    public void SetColumn(Control control, int column)
    {
        var cell = Cell(control);
        cell.Column = column;
        _positions[control] = cell;
    }

    public int GetColumn(Control control) => Cell(control).Column;

    public void SetRow(Control control, int row)
    {
        var cell = Cell(control);
        cell.Row = row;
        _positions[control] = cell;
    }

    public int GetRow(Control control) => Cell(control).Row;

    public void SetColumnSpan(Control control, int value)
    {
        ArgumentNullException.ThrowIfNull(control);
        _columnSpans[control] = Math.Max(1, value);
    }

    public int GetColumnSpan(Control control) =>
        _columnSpans.TryGetValue(control, out int span) ? span : 1;

    public void SetRowSpan(Control control, int value)
    {
        ArgumentNullException.ThrowIfNull(control);
        _rowSpans[control] = Math.Max(1, value);
    }

    public int GetRowSpan(Control control) =>
        _rowSpans.TryGetValue(control, out int span) ? span : 1;

    public TableLayoutPanelCellPosition GetCellPosition(Control control) =>
        FindCell(control);

    public TableLayoutPanelCellPosition GetPositionFromControl(Control control) =>
        FindCell(control);

    public Control? GetControlFromPosition(int column, int row)
    {
        foreach (var entry in _positions)
        {
            if (entry.Value.Column == column && entry.Value.Row == row)
                return entry.Key;
        }

        return null;
    }

    public int[] GetColumnWidths() => new int[Math.Max(0, ColumnCount)];
    public int[] GetRowHeights() => new int[Math.Max(0, RowCount)];

    public void SetCellPosition(Control control, TableLayoutPanelCellPosition position)
    {
        ArgumentNullException.ThrowIfNull(control);
        _positions[control] = position;
    }

    private TableLayoutPanelCellPosition FindCell(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _positions.TryGetValue(control, out TableLayoutPanelCellPosition cell)
            ? cell
            : new TableLayoutPanelCellPosition(-1, -1);
    }

    private TableLayoutPanelCellPosition Cell(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (!_positions.TryGetValue(control, out TableLayoutPanelCellPosition cell))
            _positions[control] = cell = new TableLayoutPanelCellPosition(0, 0);
        return cell;
    }
}

/// <summary>Cell-aware child list (cf. TableLayoutControlCollection).</summary>
public class TableLayoutControlCollection : Control.ControlCollection
{
    private readonly TableLayoutPanel _panel;

    public TableLayoutControlCollection(TableLayoutPanel owner)
        : base(owner)
    {
        _panel = owner;
    }

    public TableLayoutPanel Container => _panel;

    public void Add(Control control, int column, int row)
    {
        base.Add(control);
        _panel.SetCellPosition(control, new TableLayoutPanelCellPosition(column, row));
    }
}

/// <summary>Scrollable area (cf. System.Windows.Forms.ScrollableControl).</summary>
public class ScrollableControl : Control
{
    public ScrollableControl(string name = "")
        : base(name)
    {
    }

    public ScrollableControl()
        : this("")
    {
    }

    public bool AutoScroll { get; set; }
    public WfDraw::System.Drawing.Point AutoScrollPosition { get; set; }
    public HScrollProperties HorizontalScroll { get; } = new();
    public VScrollProperties VerticalScroll { get; } = new();
    public override WfDraw::System.Drawing.Rectangle DisplayRectangle =>
        new(AutoScrollPosition.X, AutoScrollPosition.Y, Width, Height);

    public event ScrollEventHandler? Scroll;

    protected virtual void OnScroll(ScrollEventArgs e) => Scroll?.Invoke(this, e);

    public void ScrollControlIntoView(Control activeControl)
    {
        ArgumentNullException.ThrowIfNull(activeControl);
    }

    public void SetAutoScrollMargin(int x, int y)
    {
    }
}

/// <summary>Container with scaling (cf. System.Windows.Forms.ContainerControl).</summary>
public class ContainerControl : ScrollableControl
{
    public ContainerControl(string name = "")
        : base(name)
    {
    }

    public ContainerControl()
        : this("")
    {
    }

    public WfDraw::System.Drawing.SizeF AutoScaleDimensions { get; set; } = new(6f, 13f);
    public AutoScaleMode AutoScaleMode { get; set; } = AutoScaleMode.Inherit;

    public Form? ParentForm => FindForm();

    private Control? _activeControl;

    /// <summary>Active child (cf. ActiveControl).</summary>
    public Control? ActiveControl
    {
        get => _activeControl;
        set
        {
            _activeControl = value;
            if (value is not null)
                ActivateControl(value);
        }
    }

    /// <summary>Dialog-key processing (cf. ProcessDialogKey). No-op: false.</summary>
    protected virtual bool ProcessDialogKey(Keys keyData) => false;

    protected virtual bool ProcessCmdKey(ref Message message, Keys keyData) => false;

    public bool ActivateControl(Control? active)
    {
        return active is not null && ActivateControlCore(active);
    }

    private bool ActivateControlCore(Control active) => active.Focus();
}

/// <summary>Scrollbar switch (cf. ScrollProperties).</summary>
public abstract class ScrollProperties
{
    protected ScrollProperties()
    {
    }

    protected ScrollProperties(ScrollableControl parent)
    {
        ParentControl = parent;
    }

    protected ScrollableControl? ParentControl { get; }

    public virtual bool Enabled { get; set; } = true;
    public virtual bool Visible { get; set; }
    public virtual int Value { get; set; }
    public virtual int Minimum { get; set; }
    public virtual int Maximum { get; set; } = 100;
    public virtual int SmallChange { get; set; } = 1;
    public virtual int LargeChange { get; set; } = 10;
}

/// <summary>Scrollbar facade (cf. HScrollProperties).</summary>
public class HScrollProperties : ScrollProperties
{
}

/// <summary>Scrollbar facade (cf. VScrollProperties).</summary>
public class VScrollProperties : ScrollProperties
{
}

/// <summary>User-composed control (cf. System.Windows.Forms.UserControl).</summary>
public class UserControl : ContainerControl
{
    public UserControl(string name = "")
        : base(name)
    {
    }

    public UserControl()
        : this("")
    {
    }

    public BorderStyle BorderStyle { get; set; } = BorderStyle.None;

    protected virtual void OnLoad(EventArgs e)
    {
    }
}

/// <summary>
/// List box (cf. System.Windows.Forms.ListBox): single selection, sunken
/// frame, highlight + focus rect, vertical scroll for overflow.
/// </summary>
public class ListBox : ListControl
{
    public const int DefaultItemHeight = 15;

    public ListBox()
        : this("")
    {
    }

    public ListBox(string name = "")
        : base(name)
    {
        Items = new ObjectCollection();
        SelectedIndices = new SelectedIndexCollection(this);
    }

    /// <summary>Clears the selection (cf. ClearSelected).</summary>
    public void ClearSelected() => SelectedIndex = -1;

    public ObjectCollection Items { get; }
    public int ItemHeight { get; set; } = DefaultItemHeight;
    public string FontCss { get; set; } = Palette.UiFontCss;
    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;
    public int TopIndex { get; set; }
    public SelectionMode SelectionMode { get; set; } = SelectionMode.One;
    public DrawMode DrawMode { get; set; } = DrawMode.Normal;
    public bool IntegralHeight { get; set; } = true;

    private bool _sorted;

    public bool Sorted
    {
        get => _sorted;
        set
        {
            _sorted = value;
            if (value)
                Items.Sort();
        }
    }

    protected override int SelectedCount => Items.Count;

    public override int SelectedIndex
    {
        get => base.SelectedIndex;
        set
        {
            SetSelectedIndex(value, Items.Count);
            SelectedIndices.Refresh();
        }
    }

    public override object? SelectedItem
    {
        get => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.IndexOf(value);
    }

    public override bool Focused { get; set; }

    /// <summary>Selected indices (cf. SelectedIndices).</summary>
    public SelectedIndexCollection SelectedIndices { get; }

    /// <summary>Item bounds (cf. GetItemRectangle).</summary>
    public WfDraw::System.Drawing.Rectangle GetItemRectangle(int index)
    {
        if (index < 0 || index >= Items.Count)
            return WfDraw::System.Drawing.Rectangle.Empty;
        return new WfDraw::System.Drawing.Rectangle(2, 2 + index * ItemHeight, Math.Max(0, Width - 4), ItemHeight);
    }

    /// <summary>Index at a point, or -1 (cf. IndexFromPoint).</summary>
    public int IndexFromPoint(WfDraw::System.Drawing.Point p) => IndexFromPoint(p.X, p.Y);

    /// <summary>Index at coordinates, or -1 (cf. IndexFromPoint).</summary>
    public int IndexFromPoint(int x, int y)
    {
        int index = (y - 2) / Math.Max(1, ItemHeight);
        return index >= 0 && index < Items.Count ? index : -1;
    }

    /// <summary>Owner-draw notifier (cf. DrawItem).</summary>
    public event DrawItemEventHandler? DrawItem;

    /// <summary>Raises <c>DrawItem</c> (cf. OnDrawItem).</summary>
    protected virtual void OnDrawItem(DrawItemEventArgs e) =>
        DrawItem?.Invoke(this, e);

    /// <summary>Selected-index list (cf. ListBox.SelectedIndexCollection).</summary>
    public class SelectedIndexCollection : System.Collections.ObjectModel.Collection<int>
    {
        private readonly ListBox _owner;

        /// <summary>Creates an index list (cf. SelectedIndexCollection(ListBox)).</summary>
        public SelectedIndexCollection(ListBox owner)
        {
            _owner = owner;
            Refresh();
        }

        internal void Refresh()
        {
            base.ClearItems();
            if (_owner.SelectedIndex >= 0)
                base.InsertItem(0, _owner.SelectedIndex);
        }

        /// <inheritdoc/>
        protected override void InsertItem(int index, int item) =>
            _owner.SelectedIndex = item;

        /// <inheritdoc/>
        protected override void RemoveItem(int index) =>
            _owner.ClearSelected();

        /// <inheritdoc/>
        protected override void SetItem(int index, int item) =>
            _owner.SelectedIndex = item;

        /// <inheritdoc/>
        protected override void ClearItems() => _owner.ClearSelected();
    }

    /// <summary>Item store (cf. ListBox.ObjectCollection).</summary>
    public class ObjectCollection : ObjectList
    {
        public ObjectCollection()
            : base(null)
        {
        }
    }

    private ScrollBar? _vBar;

    private int Inset => BorderStyle == BorderStyle.None ? 0 : 2;

    public override void HandleClickLocal(int localX, int localY)
    {
        if (!Enabled)
            return;
        int inset = Inset;
        int index = TopIndex + ((localY - inset) / ItemHeight);
        if (index >= TopIndex && index < Items.Count)
        {
            SelectedIndex = index;
            base.HandleClickLocal(localX, localY);
        }
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        int inset = Inset;
        switch (BorderStyle)
        {
            case BorderStyle.FixedSingle:
                ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
                    themed ? SystemColors.ThemedEditBorder : SystemColors.ControlDarkDark,
                    ControlPaintEdges.LineBorderStyle.Solid);
                break;
            case BorderStyle.Fixed3D:
                if (themed)
                    ControlPaintGlyphs.DrawVisualStyleBorder(canvas, ax, ay, Width, Height);
                else
                    ControlPaintEdges.DrawEdge(canvas, ax, ay, Width, Height,
                        ControlPaintEdges.EdgeStyle.Sunken);
                break;
        }

        canvas.FillRectangle(ax + inset, ay + inset,
            Width - (2 * inset), Height - (2 * inset), SystemColors.Window);

        int visible = Math.Max(1, (Height - (2 * inset)) / ItemHeight);
        bool scroll = Items.Count > visible;
        int listW = Width - (2 * inset) - (scroll ? 17 : 0);
        for (int i = 0; i < visible && TopIndex + i < Items.Count; i++)
        {
            int iy = ay + inset + (i * ItemHeight);
            bool selected = TopIndex + i == SelectedIndex;
            string label = GetItemText(Items[TopIndex + i]);
            if (selected && Enabled)
            {
                uint fill = Focused ? SystemColors.Highlight : SystemColors.ControlLight;
                uint ink = Focused ? SystemColors.HighlightText : SystemColors.ControlText;
                canvas.FillRectangle(ax + inset, iy, listW, ItemHeight, fill);
                canvas.DrawTextAligned(label, ax + inset + 3, iy + 3,
                    FontCss, ink, TextHAlign.Left, TextVAlign.Top);
                if (Focused)
                    ControlPaintGlyphs.DrawFocusRectangle(
                        canvas, ax + inset, iy, listW, ItemHeight);
            }
            else
            {
                canvas.DrawTextAligned(label, ax + inset + 3, iy + 3,
                    FontCss, Enabled ? SystemColors.WindowText : SystemColors.GrayText,
                    TextHAlign.Left, TextVAlign.Top);
            }
        }

        if (scroll)
            SyncBar(ax, ay, inset, visible);
        else if (_vBar is not null)
            _vBar.Visible = false;
    }

    private void SyncBar(int ax, int ay, int inset, int visible)
    {
        _vBar ??= new ScrollBar("listv") { Vertical = true };
        if (_vBar.Parent != this)
            AddChild(_vBar);
        _vBar.Visible = true;
        _vBar.X = Width - inset - 17;
        _vBar.Y = inset;
        _vBar.Width = 17;
        _vBar.Height = Height - (2 * inset);
        _vBar.Minimum = 0;
        _vBar.Maximum = Items.Count - 1;
        _vBar.LargeChange = visible;
        _vBar.Value = Math.Clamp(TopIndex, 0, _vBar.EffectiveMaximum);
        _vBar.Enabled = Enabled;
        _vBar.ValueChanged -= OnBarScroll;
        _vBar.ValueChanged += OnBarScroll;
        _ = ax;
        _ = ay;
    }

    private void OnBarScroll(object? sender, EventArgs e)
    {
        if (_vBar is not null)
            TopIndex = _vBar.Value;
    }
}

/// <summary>
/// Combo box (cf. System.Windows.Forms.ComboBox): DropDown (editable field +
/// button), DropDownList (static field + button), Simple (field + open list).
/// The drop-down list paints as an overlay and closes on selection.
/// </summary>
public class ComboBox : ListControl
{
    public const int ButtonWidth = 17;
    public const int FieldHeight = 23;
    public const int MaxDropDownItems = 8;

    public int ItemHeight { get; set; } = 15;

    public ComboBox()
        : this("")
    {
    }

    public ComboBox(string name = "")
        : base(name)
    {
        Items = new ObjectCollection(() => _sorted);
    }

    public ObjectCollection Items { get; }
    public AutoCompleteMode AutoCompleteMode { get; set; } = AutoCompleteMode.None;
    public AutoCompleteSource AutoCompleteSource { get; set; } = AutoCompleteSource.None;
    public AutoCompleteStringCollection AutoCompleteCustomSource { get; } = new();
    public ComboBoxStyle DropDownStyle { get; set; } = ComboBoxStyle.DropDown;
    public DrawMode DrawMode { get; set; } = DrawMode.Normal;
    public FlatStyle FlatStyle { get; set; } = FlatStyle.System;
    public int DropDownHeight { get; set; } = 106;
    public int DropDownWidth { get; set; }
    public string FontCss { get; set; } = Palette.UiFontCss;
    public override bool Focused { get; set; }
    public bool IsHot { get; set; }

    private bool _sorted;
    private bool _droppedDown;
    private int _updateCount;

    public bool Sorted
    {
        get => _sorted;
        set
        {
            _sorted = value;
            if (value)
                Items.Sort();
        }
    }

    protected override int SelectedCount => Items.Count;

    public override int SelectedIndex
    {
        get => base.SelectedIndex;
        set => SetSelectedIndex(value, Items.Count);
    }

    public override object? SelectedItem
    {
        get => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.IndexOf(value);
    }

    public string FieldText =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? GetItemText(Items[SelectedIndex]) : Text;

    public void BeginUpdate() => _updateCount++;
    public void EndUpdate()
    {
        if (_updateCount > 0)
            _updateCount--;
    }

    /// <summary>Item store (cf. ComboBox.ObjectCollection).</summary>
    public class ObjectCollection : ObjectList
    {
        public ObjectCollection(Func<bool> sorted)
            : base(sorted)
        {
        }
    }

    public bool DroppedDown
    {
        get => _droppedDown;
        set
        {
            if (value == _droppedDown)
                return;
            _droppedDown = value;
            DropDown?.Invoke(this, EventArgs.Empty);
            if (!value)
                DropDownClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChangeCommitted;
    public event EventHandler? DropDown;
    public event EventHandler? DropDownClosed;
    public event MeasureItemEventHandler? MeasureItem;
    public event DrawItemEventHandler? DrawItem;

    protected virtual void OnSelectionChangeCommitted(EventArgs e) =>
        SelectionChangeCommitted?.Invoke(this, e);

    protected virtual void OnMeasureItem(MeasureItemEventArgs e) => MeasureItem?.Invoke(this, e);
    protected virtual void OnDrawItem(DrawItemEventArgs e) => DrawItem?.Invoke(this, e);

    /// <summary>Raises <c>DropDown</c> (cf. OnDropDown).</summary>
    protected virtual void OnDropDown(EventArgs e) =>
        DropDown?.Invoke(this, e);

    /// <summary>Raises <c>DropDownClosed</c> (cf. OnDropDownClosed).</summary>
    protected virtual void OnDropDownClosed(EventArgs e) =>
        DropDownClosed?.Invoke(this, e);

    public override (Control Control, int LocalX, int LocalY)? HitTestDeep(int x, int y)
    {
        var hit = base.HitTestDeep(x, y);
        if (hit.HasValue || !Visible || !Enabled || !DroppedDown
            || DropDownStyle == ComboBoxStyle.Simple)
            return hit;
        // The open list overflows the field bounds (cf. drop-down window).
        (int bx, int by, int bw, int bh) = DropDownBounds();
        int lx = x - X;
        int ly = y - Y;
        if (lx >= bx && lx < bx + bw && ly >= by && ly < by + bh)
            return (this, lx, ly);
        return null;
    }

    /// <summary>Drop-down list bounds in local space (below the field).</summary>
    public (int X, int Y, int Width, int Height) DropDownBounds()
    {
        int rows = Math.Min(MaxDropDownItems, Math.Max(1, Items.Count));
        return (0, FieldHeight, Width, (rows * ItemHeight) + 2);
    }

    protected override void PaintChildren(ICanvas canvas, int ax, int ay)
    {
        base.PaintChildren(canvas, ax, ay);
        // Overlay paints after children so the list floats above content.
        // (It stays within this control's z-order; later siblings may cover
        // it, unlike a native top-level drop-down window.)
        if (DroppedDown && DropDownStyle != ComboBoxStyle.Simple)
            PaintDropDown(canvas, ax, ay);
    }

    public override void HandleClickLocal(int localX, int localY)
    {
        if (!Enabled)
            return;
        if (DroppedDown && DropDownStyle != ComboBoxStyle.Simple)
        {
            (int bx, int by, int bw, int bh) = DropDownBounds();
            if (localX >= bx && localX < bx + bw && localY >= by && localY < by + bh)
            {
                int index = (localY - by - 1) / ItemHeight;
                if (index >= 0 && index < Items.Count)
                {
                    SelectedIndex = index;
                    DroppedDown = false;
                    base.HandleClickLocal(localX, localY);
                }

                return;
            }

            if (localX >= Width - ButtonWidth && localY < FieldHeight)
            {
                DroppedDown = false;
                return;
            }

            DroppedDown = false;
        }

        if (localX >= Width - ButtonWidth && localY < FieldHeight)
        {
            if (DropDownStyle != ComboBoxStyle.Simple)
                DroppedDown = true;
            return;
        }

        if (DropDownStyle == ComboBoxStyle.DropDownList)
        {
            DroppedDown = true;
            return;
        }

        base.HandleClickLocal(localX, localY);
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        if (themed)
            ControlPaintGlyphs.DrawVisualStyleBorder(canvas, ax, ay, Width, FieldHeight);
        else
            ControlPaintEdges.DrawEdge(canvas, ax, ay, Width, FieldHeight,
                ControlPaintEdges.EdgeStyle.Sunken);
        canvas.FillRectangle(ax + 1, ay + 1, Width - 2, FieldHeight - 2, SystemColors.Window);

        if (DropDownStyle == ComboBoxStyle.DropDown)
        {
            string text = FieldText;
            if (!string.IsNullOrEmpty(text))
                canvas.DrawTextAligned(text, ax + 4, ay + 1 + ((FieldHeight - 2) / 2f),
                    FontCss, Enabled ? SystemColors.WindowText : SystemColors.GrayText,
                    TextHAlign.Left, TextVAlign.Middle);
        }
        else if (DropDownStyle == ComboBoxStyle.DropDownList)
        {
            // Static field: highlight only while the list is open.
            if (DroppedDown)
                canvas.FillRectangle(ax + 1, ay + 1, Width - ButtonWidth - 1, FieldHeight - 2,
                    SystemColors.Highlight);
            string text = FieldText;
            if (!string.IsNullOrEmpty(text))
                canvas.DrawTextAligned(text, ax + 4, ay + 1 + ((FieldHeight - 2) / 2f),
                    FontCss, DroppedDown ? SystemColors.HighlightText : SystemColors.WindowText,
                    TextHAlign.Left, TextVAlign.Middle);
            if (Focused && Enabled && !DroppedDown)
                ControlPaintGlyphs.DrawFocusRectangle(
                    canvas, ax + 2, ay + 2, Width - ButtonWidth - 2, FieldHeight - 4);
        }

        ControlPaintGlyphs.DrawComboButton(canvas, ax + Width - ButtonWidth, ay + 1,
            ButtonWidth, FieldHeight - 2, pressed: DroppedDown, disabled: !Enabled, themed);

        if (DropDownStyle == ComboBoxStyle.Simple)
        {
            int listY = FieldHeight + 2;
            int listH = Math.Max(0, Height - listY);
            PaintList(canvas, ax + 1, ay + listY, Width - 2, listH);
        }
    }

    private void PaintDropDown(ICanvas canvas, int ax, int ay)
    {
        (int bx, int by, int bw, int bh) = DropDownBounds();
        PaintList(canvas, ax + bx, ay + by, bw, bh);
    }

    private void PaintList(ICanvas canvas, int x, int y, int w, int h)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        canvas.FillRectangle(x, y, w, h, SystemColors.Window);
        if (themed)
            ControlPaintGlyphs.DrawVisualStyleBorder(canvas, x, y, w, h);
        else
            ControlPaintEdges.DrawBorder(canvas, x, y, w, h,
                SystemColors.Black, ControlPaintEdges.LineBorderStyle.Solid);

        int rows = Math.Max(0, (h - 2) / ItemHeight);
        for (int i = 0; i < rows && i < Items.Count; i++)
        {
            int iy = y + 1 + (i * ItemHeight);
            string label = GetItemText(Items[i]);
            if (i == SelectedIndex)
            {
                canvas.FillRectangle(x + 1, iy, w - 2, ItemHeight, SystemColors.Highlight);
                canvas.DrawTextAligned(label, x + 4, iy + 2, FontCss,
                    SystemColors.HighlightText, TextHAlign.Left, TextVAlign.Top);
            }
            else
            {
                canvas.DrawTextAligned(label, x + 4, iy + 2, FontCss,
                    SystemColors.WindowText, TextHAlign.Left, TextVAlign.Top);
            }
        }
    }
}

/// <summary>Progress bar (cf. System.Windows.Forms.ProgressBar): sunken
/// frame, block/continuous fill, tick-count marquee.</summary>
public class ProgressBar : Control
{
    public ProgressBar(string name = "")
        : base(name)
    {
    }

    public ProgressBar()
        : this("")
    {
    }

    private int _minimum;
    private int _maximum = 100;
    private int _value;

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            Value = Math.Clamp(_value, _minimum, _maximum);
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            Value = Math.Clamp(_value, _minimum, _maximum);
        }
    }

    public int Value
    {
        get => _value;
        set => _value = Math.Clamp(value, _minimum, _maximum);
    }

    public ProgressBarStyle Style { get; set; } = ProgressBarStyle.Blocks;
    public int Step { get; set; } = 10;
    public int MarqueeAnimationSpeed { get; set; } = 100;

    public void PerformStep() => Increment(Step);

    public void Increment(int value) => Value = _value + value;

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        canvas.FillRectangle(ax, ay, Width, Height, SystemColors.Control);
        ControlPaintEdges.DrawEdge(canvas, ax, ay, Width, Height,
            ControlPaintEdges.EdgeStyle.Sunken);
        int ix = ax + 2;
        int iy = ay + 2;
        int iw = Math.Max(0, Width - 4);
        int ih = Math.Max(0, Height - 4);
        if (iw <= 0 || ih <= 0)
            return;
        if (Style == ProgressBarStyle.Marquee)
        {
            int block = Math.Max(8, iw / 6);
            int span = iw + block;
            int at = (int)((Environment.TickCount / (long)Math.Max(1, MarqueeAnimationSpeed)) % span);
            canvas.FillRectangle(ix + Math.Min(at, iw - 1), iy, Math.Min(block, iw - at), ih,
                SystemColors.Highlight);
            return;
        }

        double fraction = _maximum <= _minimum
            ? 0
            : (double)(_value - _minimum) / (_maximum - _minimum);
        int fill = (int)(iw * Math.Clamp(fraction, 0, 1));
        if (Style == ProgressBarStyle.Blocks)
        {
            const int Block = 10;
            const int Gap = 2;
            for (int x = 0; x + Block <= fill; x += Block + Gap)
                canvas.FillRectangle(ix + x, iy, Block, ih, SystemColors.Highlight);
        }
        else
        {
            canvas.FillRectangle(ix, iy, fill, ih, SystemColors.Highlight);
        }
    }
}

/// <summary>List column (cf. ColumnHeader).</summary>
public class ColumnHeader
{
    public ColumnHeader()
    {
    }

    public ColumnHeader(string text)
    {
        Text = text;
    }

    public ColumnHeader(string text, int width)
    {
        Text = text;
        Width = width;
    }

    public ColumnHeader(int imageIndex)
    {
        ImageIndex = imageIndex;
    }

    public string Text { get; set; } = "";
    public int Width { get; set; } = 60;
    public HorizontalAlignment TextAlign { get; set; } = HorizontalAlignment.Left;
    public int DisplayIndex { get; set; }
    public int ImageIndex { get; set; } = -1;
    public string ImageKey { get; set; } = "";
    public string Name { get; set; } = "";
    public object? Tag { get; set; }
    public ListView? ListView { get; internal set; }
}

/// <summary>List row (cf. ListViewItem).</summary>
public class ListViewItem
{
    public ListViewItem()
    {
        SubItems.Add("");
    }

    public ListViewItem(string text)
    {
        SubItems.Add(text);
    }

    public ListViewItem(string text, int imageIndex)
    {
        SubItems.Add(text);
        ImageIndex = imageIndex;
    }

    public ListViewItem(string[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (string item in items)
            SubItems.Add(item);
    }

    public ListViewItem(string[] items, int imageIndex)
        : this(items)
    {
        ImageIndex = imageIndex;
    }

    public string Text
    {
        get => SubItems.Count > 0 ? SubItems[0].Text : "";
        set
        {
            if (SubItems.Count == 0)
                SubItems.Add(value);
            else
                SubItems[0].Text = value;
        }
    }

    public ListViewSubItemCollection SubItems { get; } = new();
    public int ImageIndex { get; set; } = -1;
    public string ImageKey { get; set; } = "";
    public string Name { get; set; } = "";
    public object? Tag { get; set; }
    public bool Selected { get; set; }
    public bool Focused { get; set; }
    public bool Checked { get; set; }
    public bool UseItemStyleForSubItems { get; set; } = true;
    public WfDraw::System.Drawing.Color BackColor { get; set; } = WfDraw::System.Drawing.Color.Empty;
    public WfDraw::System.Drawing.Color ForeColor { get; set; } = WfDraw::System.Drawing.Color.Empty;
    public WfDraw::System.Drawing.Font? Font { get; set; }
    public ListView? ListView { get; internal set; }

    public virtual void Remove() => ListView?.Items.Remove(this);

    /// <summary>Cell collection (cf. ListViewItem.ListViewSubItemCollection).</summary>
    public class ListViewSubItemCollection : System.Collections.IList
    {
        private readonly List<ListViewSubItem> _items = new();

        public ListViewSubItem Add(string text)
        {
            var sub = new ListViewSubItem(text);
            _items.Add(sub);
            return sub;
        }

        public ListViewSubItem Add(ListViewSubItem subItem)
        {
            ArgumentNullException.ThrowIfNull(subItem);
            _items.Add(subItem);
            return subItem;
        }

        public ListViewSubItem Add(string text, WfDraw::System.Drawing.Color foreColor, WfDraw::System.Drawing.Color backColor, WfDraw::System.Drawing.Font? font)
        {
            var sub = new ListViewSubItem(text)
            {
                ForeColor = foreColor, BackColor = backColor, Font = font,
            };
            _items.Add(sub);
            return sub;
        }

        public void AddRange(string[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (string item in items)
                Add(item);
        }

        public void AddRange(ListViewSubItem[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (ListViewSubItem item in items)
                Add(item);
        }

        public void AddRange(string[] items, WfDraw::System.Drawing.Color foreColor, WfDraw::System.Drawing.Color backColor, WfDraw::System.Drawing.Font? font)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (string item in items)
                Add(item, foreColor, backColor, font);
        }

        public virtual void Clear() => _items.Clear();
        public bool Contains(ListViewSubItem subItem) => _items.Contains(subItem);
        public virtual bool ContainsKey(string key) => IndexOfKey(key) >= 0;
        public int IndexOf(ListViewSubItem subItem) => _items.IndexOf(subItem);
        public virtual int IndexOfKey(string key) => _items.FindIndex(s => s.Name == key);
        public void Insert(int index, ListViewSubItem subItem) => _items.Insert(index, subItem);
        public void Remove(ListViewSubItem subItem) => _items.Remove(subItem);
        public virtual void RemoveAt(int index) => _items.RemoveAt(index);
        public virtual void RemoveByKey(string key)
        {
            int at = IndexOfKey(key);
            if (at >= 0)
                RemoveAt(at);
        }

        public int Count => _items.Count;

        public virtual System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();

        public ListViewSubItem this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        int System.Collections.IList.Add(object? value)
        {
            if (value is string text)
                Add(text);
            else
                Add((ListViewSubItem)value!);
            return _items.Count - 1;
        }

        bool System.Collections.IList.Contains(object? value) => value is ListViewSubItem s && Contains(s);
        int System.Collections.IList.IndexOf(object? value) => value is ListViewSubItem s ? IndexOf(s) : -1;
        void System.Collections.IList.Insert(int index, object? value) => Insert(index, (ListViewSubItem)value!);
        void System.Collections.IList.Remove(object? value)
        {
            if (value is ListViewSubItem s)
                Remove(s);
        }

        object? System.Collections.IList.this[int index]
        {
            get => this[index];
            set => this[index] = (ListViewSubItem)value!;
        }

        bool System.Collections.IList.IsReadOnly => false;
        bool System.Collections.IList.IsFixedSize => false;
        void System.Collections.ICollection.CopyTo(Array array, int index) =>
            ((System.Collections.ICollection)_items).CopyTo(array, index);
        bool System.Collections.ICollection.IsSynchronized => false;
        object System.Collections.ICollection.SyncRoot => this;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Row cell (cf. ListViewItem.ListViewSubItem).</summary>
    public class ListViewSubItem
    {
        public ListViewSubItem()
        {
        }

        public ListViewSubItem(string text)
        {
            Text = text;
        }

        public string Text { get; set; } = "";
        public string Name { get; set; } = "";
        public object? Tag { get; set; }
        public WfDraw::System.Drawing.Color BackColor { get; set; } = WfDraw::System.Drawing.Color.Empty;
        public WfDraw::System.Drawing.Color ForeColor { get; set; } = WfDraw::System.Drawing.Color.Empty;
        public WfDraw::System.Drawing.Font? Font { get; set; }
    }
}

/// <summary>Row list (cf. System.Windows.Forms.ListView): Details report
/// with header + rows; other views paint the same rows without headers.</summary>
public class ListView : Control
{
    public const int HeaderHeight = 20;
    public const int RowHeight = 17;

    public ListView(string name = "")
        : base(name)
    {
        Items.Owner = this;
    }

    public ListView()
        : this("")
    {
    }

    public View View { get; set; } = View.Details;
    public ListViewItemCollection Items { get; } = new();
    public ColumnHeaderCollection Columns { get; } = new();
    public bool CheckBoxes { get; set; }
    public bool FullRowSelect { get; set; } = true;
    public bool GridLines { get; set; }
    public bool MultiSelect { get; set; } = true;
    public bool Scrollable { get; set; } = true;
    public bool AllowColumnReorder { get; set; }
    public bool HideSelection { get; set; } = true;
    public bool LabelEdit { get; set; }
    public string FontCss { get; set; } = Palette.UiFontCss;

    private int _updateCount;

    public void BeginUpdate() => _updateCount++;
    public void EndUpdate()
    {
        if (_updateCount > 0)
            _updateCount--;
    }

    public void AutoResizeColumn(int columnIndex) =>
        AutoResizeColumn(columnIndex, ColumnHeaderAutoResizeStyle.ColumnContent);

    public void AutoResizeColumn(int columnIndex, ColumnHeaderAutoResizeStyle headerAutoResize)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
            return;
        int need = 20;
        if (headerAutoResize != ColumnHeaderAutoResizeStyle.HeaderSize)
        {
            foreach (ListViewItem item in Items)
            {
                if (columnIndex < item.SubItems.Count)
                    need = Math.Max(need, 12 + (item.SubItems[columnIndex].Text.Length * 7));
            }
        }

        if (headerAutoResize != ColumnHeaderAutoResizeStyle.ColumnContent)
            need = Math.Max(need, 12 + (Columns[columnIndex].Text.Length * 7));
        Columns[columnIndex].Width = need;
    }

    public void AutoResizeColumns(ColumnHeaderAutoResizeStyle headerAutoResize)
    {
        for (int i = 0; i < Columns.Count; i++)
            AutoResizeColumn(i, headerAutoResize);
    }

    public event EventHandler? SelectedIndexChanged;

    protected virtual void OnSelectedIndexChanged(EventArgs e) => SelectedIndexChanged?.Invoke(this, e);

    public override void HandleClickLocal(int localX, int localY)
    {
        if (!Enabled)
            return;
        int row = RowAt(localY);
        if (row < 0)
        {
            base.HandleClickLocal(localX, localY);
            return;
        }

        bool selected = Items[row].Selected;
        if (!MultiSelect)
        {
            foreach (ListViewItem item in Items)
                item.Selected = false;
        }

        Items[row].Selected = !selected;
        Items[row].Focused = true;
        OnSelectedIndexChanged(EventArgs.Empty);
        base.HandleClickLocal(localX, localY);
    }

    private int RowAt(int localY)
    {
        int top = View == View.Details ? HeaderHeight : 0;
        int row = (localY - top) / RowHeight;
        return row >= 0 && row < Items.Count ? row : -1;
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        canvas.FillRectangle(ax, ay, Width, Height, SystemColors.Window);
        ControlPaintEdges.DrawEdge(canvas, ax, ay, Width, Height,
            ControlPaintEdges.EdgeStyle.Sunken);
        int y = ay;
        if (View == View.Details)
        {
            PaintHeader(canvas, ax, y);
            y += HeaderHeight;
        }

        int x = ax;
        int rows = Math.Max(0, (ay + Height - y) / RowHeight);
        for (int r = 0; r < rows && r < Items.Count; r++)
            PaintRow(canvas, Items[r], x, y + (r * RowHeight), r);
    }

    private void PaintHeader(ICanvas canvas, int ax, int ay)
    {
        int x = ax;
        foreach (ColumnHeader column in Columns)
        {
            canvas.FillRectangle(x, ay, column.Width, HeaderHeight, SystemColors.Control);
            canvas.DrawTextAligned(column.Text, x + 4, ay + 2, FontCss,
                SystemColors.ControlText, TextHAlign.Left, TextVAlign.Top);
            x += column.Width;
        }
    }

    private void PaintRow(ICanvas canvas, ListViewItem item, int ax, int ay, int row)
    {
        bool selected = item.Selected && Enabled;
        if (selected)
        {
            uint fill = Focused ? SystemColors.Highlight : SystemColors.ControlLight;
            canvas.FillRectangle(ax, ay, Width, RowHeight, fill);
        }

        uint ink = !Enabled ? SystemColors.GrayText
            : selected && Focused ? SystemColors.HighlightText
            : SystemColors.WindowText;
        int x = ax + 2;
        if (CheckBoxes)
        {
            canvas.FillRectangle(x, ay + 3, 11, 11, SystemColors.Window);
            ControlPaintEdges.DrawBorder(canvas, x, ay + 3, 11, 11,
                SystemColors.ControlDarkDark, ControlPaintEdges.LineBorderStyle.Solid);
            if (item.Checked)
                canvas.FillRectangle(x + 2, ay + 5, 7, 7, SystemColors.ControlText);
            x += 15;
        }

        if (View == View.Details && Columns.Count > 0)
        {
            foreach (ColumnHeader column in Columns)
            {
                int col = Columns.IndexOf(column);
                string text = col < item.SubItems.Count ? item.SubItems[col].Text : "";
                canvas.DrawTextAligned(text, x + 2, ay + 1, FontCss, ink,
                    TextHAlign.Left, TextVAlign.Top);
                x += column.Width;
                if (x - ax >= Width)
                    break;
            }
        }
        else
        {
            canvas.DrawTextAligned(item.Text, x + 2, ay + 1, FontCss, ink,
                TextHAlign.Left, TextVAlign.Top);
        }

        if (GridLines)
            ControlPaintEdges.DrawBorder(canvas, ax, ay + RowHeight - 1, Width, 1,
                SystemColors.ControlLight, ControlPaintEdges.LineBorderStyle.Solid);
    }

    /// <summary>Row collection (cf. ListView.ListViewItemCollection).</summary>
    public class ListViewItemCollection : System.Collections.IList
    {
        private readonly List<ListViewItem> _items = new();
        internal ListView? Owner { get; set; }

        public virtual ListViewItem Add(string text)
        {
            var item = new ListViewItem(text);
            Adopt(item);
            return item;
        }

        public virtual ListViewItem Add(string key, string text, int imageIndex)
        {
            var item = new ListViewItem(text, imageIndex) { Name = key };
            Adopt(item);
            return item;
        }

        public virtual ListViewItem Add(ListViewItem item)
        {
            Adopt(item);
            return item;
        }

        public virtual ListViewItem Add(string key, string text, string imageKey)
        {
            var item = new ListViewItem(text) { Name = key, ImageKey = imageKey };
            Adopt(item);
            return item;
        }

        public virtual ListViewItem Add(string key, string text)
        {
            var item = new ListViewItem(text) { Name = key };
            Adopt(item);
            return item;
        }

        private void Adopt(ListViewItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _items.Add(item);
            item.ListView = Owner;
        }

        public virtual void AddRange(ListViewItem[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (ListViewItem item in items)
                Add(item);
        }

        public virtual void AddRange(string[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (string item in items)
                Add(item);
        }

        public virtual void Clear()
        {
            foreach (ListViewItem item in _items)
                item.ListView = null;
            _items.Clear();
        }

        public bool Contains(ListViewItem item) => _items.Contains(item);
        public int IndexOf(ListViewItem item) => _items.IndexOf(item);
        public void Insert(int index, ListViewItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _items.Insert(index, item);
            item.ListView = Owner;
        }

        public void Remove(ListViewItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (_items.Remove(item))
                item.ListView = null;
        }

        public void RemoveAt(int index)
        {
            ListViewItem item = _items[index];
            _items.RemoveAt(index);
            item.ListView = null;
        }

        public int Count => _items.Count;

        public virtual System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();

        public ListViewItem this[int index]
        {
            get => _items[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _items[index].ListView = null;
                _items[index] = value;
                value.ListView = Owner;
            }
        }

        int System.Collections.IList.Add(object? value)
        {
            if (value is string text)
                Add(text);
            else
                Add((ListViewItem)value!);
            return _items.Count - 1;
        }

        bool System.Collections.IList.Contains(object? value) => value is ListViewItem i && Contains(i);
        int System.Collections.IList.IndexOf(object? value) => value is ListViewItem i ? IndexOf(i) : -1;
        void System.Collections.IList.Insert(int index, object? value) => Insert(index, (ListViewItem)value!);
        void System.Collections.IList.Remove(object? value)
        {
            if (value is ListViewItem i)
                Remove(i);
        }

        object? System.Collections.IList.this[int index]
        {
            get => this[index];
            set => this[index] = (ListViewItem)value!;
        }

        bool System.Collections.IList.IsReadOnly => false;
        bool System.Collections.IList.IsFixedSize => false;
        void System.Collections.ICollection.CopyTo(Array array, int index) =>
            ((System.Collections.ICollection)_items).CopyTo(array, index);
        bool System.Collections.ICollection.IsSynchronized => false;
        object System.Collections.ICollection.SyncRoot => this;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Column collection (cf. ListView.ColumnHeaderCollection).</summary>
    public class ColumnHeaderCollection : System.Collections.IList
    {
        private readonly List<ColumnHeader> _columns = new();

        public virtual ColumnHeader Add(string text)
        {
            var header = new ColumnHeader(text);
            Adopt(header);
            return header;
        }

        public virtual ColumnHeader Add(string text, int width)
        {
            var header = new ColumnHeader(text, width);
            Adopt(header);
            return header;
        }

        public virtual ColumnHeader Add(string text, int width, HorizontalAlignment textAlign)
        {
            var header = new ColumnHeader(text, width) { TextAlign = textAlign };
            Adopt(header);
            return header;
        }

        public virtual ColumnHeader Add(string key, string text)
        {
            var header = new ColumnHeader(text) { Name = key };
            Adopt(header);
            return header;
        }

        public virtual ColumnHeader Add(string key, string text, int width)
        {
            var header = new ColumnHeader(text, width) { Name = key };
            Adopt(header);
            return header;
        }

        public virtual ColumnHeader Add(string key, string text, int width, HorizontalAlignment textAlign, string imageKey)
        {
            var header = new ColumnHeader(text, width)
            {
                Name = key, TextAlign = textAlign, ImageKey = imageKey,
            };
            Adopt(header);
            return header;
        }

        public virtual ColumnHeader Add(string key, string text, int width, HorizontalAlignment textAlign, int imageIndex)
        {
            var header = new ColumnHeader(text, width)
            {
                Name = key, TextAlign = textAlign, ImageIndex = imageIndex,
            };
            Adopt(header);
            return header;
        }

        public virtual int Add(ColumnHeader header)
        {
            Adopt(header);
            return _columns.Count - 1;
        }

        private void Adopt(ColumnHeader header)
        {
            ArgumentNullException.ThrowIfNull(header);
            _columns.Add(header);
        }

        public virtual void AddRange(ColumnHeader[] headers)
        {
            ArgumentNullException.ThrowIfNull(headers);
            foreach (ColumnHeader header in headers)
                Add(header);
        }

        public virtual void AddRange(string[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (string item in items)
                Add(item);
        }

        public virtual void Clear() => _columns.Clear();
        public bool Contains(ColumnHeader header) => _columns.Contains(header);
        public virtual bool ContainsKey(string key) => IndexOfKey(key) >= 0;
        public int IndexOf(ColumnHeader header) => _columns.IndexOf(header);
        public virtual int IndexOfKey(string key) => _columns.FindIndex(h => h.Name == key);
        public void Insert(int index, ColumnHeader header) => _columns.Insert(index, header);
        public void Insert(int index, string text) => Insert(index, new ColumnHeader(text));
        public void Insert(int index, string text, int width) =>
            Insert(index, new ColumnHeader(text, width));
        public void Insert(int index, string text, int width, HorizontalAlignment textAlign) =>
            Insert(index, new ColumnHeader(text, width) { TextAlign = textAlign });
        public void Insert(int index, string key, string text) =>
            Insert(index, new ColumnHeader(text) { Name = key });
        public void Insert(int index, string key, string text, int width) =>
            Insert(index, new ColumnHeader(text, width) { Name = key });
        public void Insert(int index, string key, string text, int width, HorizontalAlignment textAlign, string imageKey) =>
            Insert(index, new ColumnHeader(text, width)
            {
                Name = key, TextAlign = textAlign, ImageKey = imageKey,
            });
        public void Insert(int index, string key, string text, int width, HorizontalAlignment textAlign, int imageIndex) =>
            Insert(index, new ColumnHeader(text, width)
            {
                Name = key, TextAlign = textAlign, ImageIndex = imageIndex,
            });
        public virtual void Remove(ColumnHeader header) => _columns.Remove(header);
        public virtual void RemoveAt(int index) => _columns.RemoveAt(index);
        public virtual void RemoveByKey(string key)
        {
            int at = IndexOfKey(key);
            if (at >= 0)
                RemoveAt(at);
        }

        public int Count => _columns.Count;

        public virtual System.Collections.IEnumerator GetEnumerator() => _columns.GetEnumerator();

        public ColumnHeader this[int index]
        {
            get => _columns[index];
            set => _columns[index] = value;
        }

        int System.Collections.IList.Add(object? value)
        {
            if (value is string text)
                Add(text);
            else
                Add((ColumnHeader)value!);
            return _columns.Count - 1;
        }

        bool System.Collections.IList.Contains(object? value) => value is ColumnHeader h && Contains(h);
        int System.Collections.IList.IndexOf(object? value) => value is ColumnHeader h ? IndexOf(h) : -1;
        void System.Collections.IList.Insert(int index, object? value) => Insert(index, (ColumnHeader)value!);
        void System.Collections.IList.Remove(object? value)
        {
            if (value is ColumnHeader h)
                Remove(h);
        }

        object? System.Collections.IList.this[int index]
        {
            get => this[index];
            set => this[index] = (ColumnHeader)value!;
        }

        bool System.Collections.IList.IsReadOnly => false;
        bool System.Collections.IList.IsFixedSize => false;
        void System.Collections.ICollection.CopyTo(Array array, int index) =>
            ((System.Collections.ICollection)_columns).CopyTo(array, index);
        bool System.Collections.ICollection.IsSynchronized => false;
        object System.Collections.ICollection.SyncRoot => this;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>Arranged child elements (cf. ArrangedElementCollection).</summary>
public class ArrangedElementCollection : System.Collections.IList
{
    private readonly System.Collections.Generic.List<object?> _items = new();

    public ArrangedElementCollection()
    {
    }

    public ArrangedElementCollection(System.Collections.IList list)
    {
        foreach (var item in list)
            _items.Add(item);
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => _items;
    public object? this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public int Add(object? value)
    {
        _items.Add(value);
        return _items.Count - 1;
    }

    public void Clear() => _items.Clear();
    public bool Contains(object? value) => _items.Contains(value);
    public int IndexOf(object? value) => _items.IndexOf(value);
    public void Insert(int index, object? value) => _items.Insert(index, value);
    public void Remove(object? value) => _items.Remove(value);
    public void RemoveAt(int index) => _items.RemoveAt(index);
    public void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
    public System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
}

/// <summary>Slider (cf. TrackBar).</summary>
public class TrackBar : Control
{
    private int _value;
    private bool _dragging;

    public TrackBar()
    {
        Width = 104; Height = 45;
    }

    public int Minimum { get; set; }
    public int Maximum { get; set; } = 10;
    public int TickFrequency { get; set; } = 1;
    public TickStyle TickStyle { get; set; } = TickStyle.BottomRight;
    public Orientation Orientation { get; set; } = Orientation.Horizontal;
    public int SmallChange { get; set; } = 1;
    public int LargeChange { get; set; } = 5;

    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Max(Minimum, Math.Min(Maximum, value));
            if (clamped == _value)
                return;
            _value = clamped;
            OnValueChanged(EventArgs.Empty);
            OnScroll(EventArgs.Empty);
            Invalidate();
        }
    }

    public event EventHandler? ValueChanged;
    public event EventHandler? Scroll;

    protected virtual void OnValueChanged(EventArgs e) => ValueChanged?.Invoke(this, e);
    protected virtual void OnScroll(EventArgs e) => Scroll?.Invoke(this, e);

    public void SetRange(int minValue, int maxValue)
    {
        Minimum = minValue;
        Maximum = maxValue;
        Value = Math.Max(minValue, Math.Min(maxValue, _value));
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        canvas.FillRectangle(ax, ay, Width, Height, BackColor);
        int channelPad = 12;
        if (Orientation == Orientation.Horizontal)
        {
            int cy = ay + Height / 2;
            canvas.FillRectangle(ax + channelPad, cy - 2, Width - channelPad * 2, 4,
                SystemColors.ControlDark);
            for (int t = Minimum; t <= Maximum; t += Math.Max(1, TickFrequency))
            {
                int tx = ax + channelPad + (int)((long)(Width - channelPad * 2) * (t - Minimum) /
                    Math.Max(1, Maximum - Minimum));
                canvas.FillRectangle(tx, cy + 4, 1, TickStyle == TickStyle.None ? 0 : 4,
                    SystemColors.ControlDarkDark);
            }

            int thumbX = ax + channelPad + (int)((long)(Width - channelPad * 2) * (_value - Minimum) /
                Math.Max(1, Maximum - Minimum));
            PaintThumb(canvas, thumbX - 5, cy - 8, 11, 17);
        }
        else
        {
            int cx = ax + Width / 2;
            canvas.FillRectangle(cx - 2, ay + channelPad, 4, Height - channelPad * 2,
                SystemColors.ControlDark);
            int thumbY = ay + channelPad + (int)((long)(Height - channelPad * 2) * (_value - Minimum) /
                Math.Max(1, Maximum - Minimum));
            PaintThumb(canvas, cx - 8, thumbY - 5, 17, 11);
        }
    }

    private void PaintThumb(ICanvas canvas, int x, int y, int w, int h)
    {
        canvas.FillRectangle(x, y, w, h, SystemColors.ControlLightLight);
        ControlPaintEdges.DrawBorder(canvas, x, y, w, h, SystemColors.ControlDarkDark,
            ControlPaintEdges.LineBorderStyle.Solid);
    }

    /// <summary>Pointer press: jumps to the position (host calls DragTo while held).</summary>
    public void PressTo(int x, int y)
    {
        _dragging = true;
        DragTo(x, y);
    }

    public void DragTo(int x, int y)
    {
        if (!_dragging)
            return;
        if (Orientation == Orientation.Horizontal)
        {
            int channelPad = 12;
            Value = Minimum + (int)((long)(x - channelPad) * (Maximum - Minimum) /
                Math.Max(1, Width - channelPad * 2));
        }
        else
        {
            int channelPad = 12;
            Value = Minimum + (int)((long)(y - channelPad) * (Maximum - Minimum) /
                Math.Max(1, Height - channelPad * 2));
        }
    }

    public void EndDrag() => _dragging = false;
}

/// <summary>Spin-box base (cf. UpDownBase). The inner edit box paints inline;
/// a hosted TextBox child arrives with text-services integration.</summary>
public abstract class UpDownBase : ContainerControl
{
    private string _text = "";
    private bool _initializing;

    public UpDownBase()
    {
    }

    public override string Text
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

    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;
    public string FontCss { get; set; } = Palette.UiFontCss;
    public HorizontalAlignment TextAlign { get; set; } = HorizontalAlignment.Left;
    public bool ReadOnly { get; set; }
    public bool InterceptArrowKeys { get; set; } = true;
    public LeftRightAlignment UpDownAlign { get; set; } = LeftRightAlignment.Right;

    protected bool UserEdit { get; set; }

    public int PreferredHeight => (int)TextRules.EstimateLineHeight(FontCss) + 6;

    /// <summary>Selects text (cf. UpDownBase.Select(int, int)).</summary>
    public void Select(int start, int length)
    {
    }

    public virtual void UpButton()
    {
    }

    public virtual void DownButton()
    {
    }

    public void BeginInit() => _initializing = true;

    public void EndInit()
    {
        _initializing = false;
        UpdateEditText();
    }

    protected bool Initializing => _initializing;

    protected virtual void OnChanged(object? source, EventArgs e)
    {
    }

    protected virtual void UpdateEditText()
    {
    }

    protected void SetTextInternal(string text)
    {
        _text = text;
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        PaintFrame(canvas, ax, ay);
        PaintSpinBox(canvas, ax, ay);
    }

    private void PaintFrame(ICanvas canvas, int ax, int ay)
    {
        const uint backFill = 0xFFFFFFFF;
        int spinWidth = 16;
        int editWidth = Math.Max(0, Width - spinWidth);
        int editX = UpDownAlign == LeftRightAlignment.Right ? ax : ax + spinWidth;
        switch (BorderStyle)
        {
            case BorderStyle.None:
                canvas.FillRectangle(ax, ay, Width, Height, backFill);
                break;
            case BorderStyle.FixedSingle:
                ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
                    SystemColors.Black, ControlPaintEdges.LineBorderStyle.Solid);
                canvas.FillRectangle(ax + 1, ay + 1, Width - 2, Height - 2, backFill);
                break;
            default:
                ControlPaintEdges.DrawEdge(canvas, ax, ay, Width, Height,
                    ControlPaintEdges.EdgeStyle.Sunken);
                canvas.FillRectangle(ax + 2, ay + 2, Width - 4, Height - 4, backFill);
                break;
        }

        canvas.DrawTextAligned(_text, editX + 3, ay + Height / 2, FontCss,
            Enabled ? 0x000000FF : SystemColors.GrayText,
            TextHAlign.Left, TextVAlign.Middle);
        _ = editWidth;
    }

    private void PaintSpinBox(ICanvas canvas, int ax, int ay)
    {
        int spinWidth = 16;
        int spinX = UpDownAlign == LeftRightAlignment.Right ? ax + Width - spinWidth : ax;
        int half = Height / 2;
        canvas.FillRectangle(spinX, ay, spinWidth, Height, SystemColors.Control);
        ControlPaintEdges.DrawBorder(canvas, spinX, ay, spinWidth, Height,
            SystemColors.ControlDarkDark, ControlPaintEdges.LineBorderStyle.Solid);
        canvas.FillRectangle(spinX, ay + half, spinWidth, 1, SystemColors.ControlDarkDark);
        canvas.DrawTextAligned("^", spinX + spinWidth / 2, ay + half / 2, FontCss,
            SystemColors.ControlText, TextHAlign.Center, TextVAlign.Middle);
        canvas.DrawTextAligned("v", spinX + spinWidth / 2, ay + half + half / 2, FontCss,
            SystemColors.ControlText, TextHAlign.Center, TextVAlign.Middle);
    }
}

/// <summary>Numeric spin box (cf. NumericUpDown).</summary>
public class NumericUpDown : UpDownBase
{
    private decimal _value;
    private decimal _minimum;
    private decimal _maximum = 100m;
    private decimal _increment = 1m;
    private int _decimalPlaces;

    public NumericUpDown()
    {
        UpdateEditText();
    }

    public decimal Value
    {
        get => _value;
        set
        {
            decimal clamped = Math.Max(_minimum, Math.Min(_maximum, value));
            if (clamped == _value)
                return;
            _value = clamped;
            UpdateEditText();
            OnValueChanged(EventArgs.Empty);
        }
    }

    public decimal Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_minimum > _maximum)
                _maximum = _minimum;
            Value = Math.Max(_minimum, Math.Min(_maximum, _value));
        }
    }

    public decimal Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            if (_maximum < _minimum)
                _minimum = _maximum;
            Value = Math.Max(_minimum, Math.Min(_maximum, _value));
        }
    }

    public decimal Increment
    {
        get => _increment;
        set => _increment = value;
    }

    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set
        {
            _decimalPlaces = value;
            UpdateEditText();
        }
    }

    public bool Hexadecimal { get; set; }
    public bool ThousandsSeparator { get; set; }

    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    public event EventHandler? ValueChanged;

    protected virtual void OnValueChanged(EventArgs e) => ValueChanged?.Invoke(this, e);

    public override void UpButton() => Value += _increment;
    public override void DownButton() => Value -= _increment;

    public virtual void ParseEditText()
    {
        if (decimal.TryParse(Text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.CurrentCulture, out decimal parsed))
            Value = parsed;
        else
            UpdateEditText();
    }

    protected override void UpdateEditText()
    {
        if (Initializing)
            return;
        SetTextInternal(FormatValue(_value));
    }

    public virtual void ValidateEditText() => ParseEditText();

    private string FormatValue(decimal value)
    {
        if (Hexadecimal)
            return ((long)value).ToString("X", System.Globalization.CultureInfo.InvariantCulture);
        string format = ThousandsSeparator ? $"N{_decimalPlaces}" : $"F{_decimalPlaces}";
        return value.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
    }
}
