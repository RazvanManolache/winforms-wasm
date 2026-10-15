extern alias WfDraw;

namespace WfWasm.Shim;

using Draw = WfDraw::System.Drawing;

/// <summary>Grid column (cf. DataGridViewColumn): header text + width.</summary>
public class DataGridViewColumn
{
    public DataGridViewColumn(string headerText = "", int width = 100)
    {
        HeaderText = headerText;
        Width = width;
    }

    public string HeaderText { get; set; }
    public int Width { get; set; }
    public int SortDirection { get; set; }
}

/// <summary>Grid selection scope (cf. DataGridViewSelectionMode subset).</summary>
public enum DataGridViewSelectionMode
{
    Cell,
    FullRow,
}

/// <summary>
/// Data grid (cf. System.Windows.Forms.DataGridView, read-only subset):
/// column/row headers, single-line borders in GridColor, current cell,
/// full-row or cell selection, click-to-sort with direction glyphs, and
/// scrollbars when content overflows. Editing, virtual mode, and column
/// resizing are out of scope for this stage.
/// </summary>
public class DataGridView : Control
{
    public DataGridView(string name = "")
        : base(name)
    {
    }

    public List<DataGridViewColumn> Columns { get; } = new();
    public List<List<string>> Rows { get; } = new();
    public bool RowHeadersVisible { get; set; } = true;
    public bool ColumnHeadersVisible { get; set; } = true;
    public int RowHeadersWidth { get; set; } = 41;
    public int ColumnHeadersHeight { get; set; } = 23;
    public int RowHeight { get; set; } = 22;
    public uint GridColor { get; set; } = SystemColors.ControlDark;
    public DataGridViewCellBorderStyle CellBorderStyle { get; set; } = DataGridViewCellBorderStyle.Single;
    public DataGridViewSelectionMode SelectionMode { get; set; } = DataGridViewSelectionMode.FullRow;
    public string FontCss { get; set; } = Palette.UiFontCss;
    public string HeaderFontCss { get; set; } = Palette.UiFontCss;
    public override bool Focused { get; set; }

    public int CurrentRow { get; set; } = -1;
    public int CurrentColumn { get; set; } = -1;
    public int SelectedRow { get; set; } = -1;
    public int TopRowIndex { get; set; }
    public int LeftOffset { get; set; }

    public event EventHandler? SelectionChanged;
    public event EventHandler<int>? Sorted;

    private ScrollBar? _vBar;
    private ScrollBar? _hBar;

    private int HeaderHeight => ColumnHeadersVisible ? ColumnHeadersHeight : 0;
    private int HeaderWidth => RowHeadersVisible ? RowHeadersWidth : 0;
    private int TotalColumnsWidth => Columns.Sum(c => c.Width);

    public override void HandleClickLocal(int localX, int localY)
    {
        if (!Enabled)
            return;
        int hh = HeaderHeight;
        int hw = HeaderWidth;
        if (localY < hh && localX >= hw)
        {
            // Column header: sort (cf. SortMode.Automatic toggle).
            int col = ColumnAt(localX - hw + LeftOffset);
            if (col >= 0)
            {
                SortBy(col);
                return;
            }
        }
        else if (localX < hw && localY >= hh)
        {
            int row = TopRowIndex + ((localY - hh) / RowHeight);
            if (row >= TopRowIndex && row < Rows.Count)
            {
                SelectRow(row);
                return;
            }
        }
        else if (localX >= hw && localY >= hh)
        {
            int row = TopRowIndex + ((localY - hh) / RowHeight);
            int col = ColumnAt(localX - hw + LeftOffset);
            if (row >= TopRowIndex && row < Rows.Count && col >= 0)
            {
                CurrentRow = row;
                CurrentColumn = col;
                if (SelectionMode == DataGridViewSelectionMode.FullRow)
                    SelectRow(row);
                else
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        base.HandleClickLocal(localX, localY);
    }

    private int ColumnAt(int contentX)
    {
        int x = 0;
        for (int i = 0; i < Columns.Count; i++)
        {
            if (contentX >= x && contentX < x + Columns[i].Width)
                return i;
            x += Columns[i].Width;
        }

        return -1;
    }

    private void SelectRow(int row)
    {
        SelectedRow = row;
        CurrentRow = row;
        if (CurrentColumn < 0 && Columns.Count > 0)
            CurrentColumn = 0;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Click-to-sort: ascending, then descending, then ascending...</summary>
    public void SortBy(int column)
    {
        if (column < 0 || column >= Columns.Count)
            return;
        foreach (DataGridViewColumn c in Columns)
        {
            if (!ReferenceEquals(c, Columns[column]))
                c.SortDirection = 0;
        }

        DataGridViewColumn target = Columns[column];
        target.SortDirection = target.SortDirection >= 0 ? -1 : 1;
        bool ascending = target.SortDirection > 0;
        Rows.Sort((a, b) =>
        {
            string av = column < a.Count ? a[column] : "";
            string bv = column < b.Count ? b[column] : "";
            int cmp = string.Compare(av, bv, StringComparison.CurrentCulture);
            return ascending ? cmp : -cmp;
        });
        Sorted?.Invoke(this, column);
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        int hh = HeaderHeight;
        int hw = HeaderWidth;

        // Body background.
        canvas.FillRectangle(ax, ay, Width, Height, SystemColors.Window);

        // Corner cell.
        if (hh > 0 && hw > 0)
            PaintHeaderCell(canvas, ax, ay, hw, hh, "", 0, themed);

        // Column headers.
        if (hh > 0)
        {
            int x = hw - LeftOffset;
            for (int i = 0; i < Columns.Count; i++)
            {
                int w = Columns[i].Width;
                if (x + w > hw && x < Width - (HasVBar() ? 17 : 0))
                    PaintHeaderCell(canvas, ax + x, ay, w, hh,
                        Columns[i].HeaderText, Columns[i].SortDirection, themed);
                x += w;
            }
        }

        // Rows.
        int visibleRows = RowHeight > 0 && hh < Height ? (Height - hh) / RowHeight : 0;
        for (int r = 0; r < visibleRows && TopRowIndex + r < Rows.Count; r++)
        {
            int row = TopRowIndex + r;
            int ry = ay + hh + (r * RowHeight);
            if (hw > 0)
                PaintRowHeader(canvas, ax, ry, row, themed);
            PaintRow(canvas, ax + hw, ry, row, themed);
        }

        // Grid frame.
        ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
            themed ? SystemColors.ThemedTabBorder : SystemColors.ControlDarkDark,
            ControlPaintEdges.LineBorderStyle.Solid);

        SyncScrollBars();
    }

    private bool HasVBar() => Rows.Count * RowHeight > Height - HeaderHeight;
    private bool HasHBar() => TotalColumnsWidth > Width - HeaderWidth;

    private void PaintHeaderCell(
        ICanvas canvas, int x, int y, int w, int h,
        string text, int sortDirection, bool themed)
    {
        if (themed)
        {
            canvas.FillGradientVertical(x, y, w, h,
                SystemColors.ThemedGridHeaderTop, SystemColors.ThemedGridHeaderBottom);
            canvas.FillRectangle(x, y + h - 1, w, 1, SystemColors.ThemedTabBorder);
        }
        else
        {
            canvas.FillRectangle(x, y, w, h, SystemColors.Control);
            ControlPaintEdges.DrawEdge(canvas, x, y, w, h,
                ControlPaintEdges.EdgeStyle.Raised);
        }

        if (!string.IsNullOrEmpty(text))
        {
            int glyphReserve = sortDirection == 0 ? 0 : 14;
            TextRules.DrawAlignedText(canvas, text, false, Draw.ContentAlignment.MiddleCenter,
                x + 2, y, w - 4 - glyphReserve, h, HeaderFontCss,
                SystemColors.ControlText, false);
        }

        if (sortDirection != 0)
        {
            // Sort glyph: up/down triangle, right-aligned (cf. header sort arrow).
            int gw = 8;
            int gh = 4;
            int gx = x + w - gw - 5;
            int gy = y + ((h - gh) / 2);
            uint ink = SystemColors.ControlDarkDark;
            if (sortDirection > 0)
            {
                // Ascending: apex at the top.
                for (int row = 0; row < gh; row++)
                {
                    int inset = ((gh - 1 - row) * (gw / 2)) / gh;
                    canvas.FillRectangle(gx + inset, y + ((h - gh) / 2) + row, gw - (2 * inset), 1, ink);
                }
            }
            else
            {
                // Descending: apex at the bottom.
                for (int row = 0; row < gh; row++)
                {
                    int inset = (row * (gw / 2)) / gh;
                    canvas.FillRectangle(gx + inset, gy + row, gw - (2 * inset), 1, ink);
                }
            }

            _ = gy;
        }
    }

    private void PaintRowHeader(ICanvas canvas, int x, int y, int row, bool themed)
    {
        if (themed)
            canvas.FillGradientVertical(x, y, HeaderWidth, RowHeight,
                SystemColors.ThemedGridHeaderTop, SystemColors.ThemedGridHeaderBottom);
        else
        {
            canvas.FillRectangle(x, y, HeaderWidth, RowHeight, SystemColors.Control);
            ControlPaintEdges.DrawEdge(canvas, x, y, HeaderWidth, RowHeight,
                ControlPaintEdges.EdgeStyle.Raised);
        }

        if (row == CurrentRow || row == SelectedRow)
        {
            // Current-row glyph (cf. row header arrow).
            int s = 7;
            int gx = x + ((HeaderWidth - (s / 2)) / 2);
            int gy = y + ((RowHeight - s) / 2);
            // Apex at the right (cf. row header arrow).
            for (int col = 0; col < s / 2; col++)
            {
                int inset = (col * (s / 2)) / (s / 2);
                canvas.FillRectangle(gx + col, gy + inset, 1, s - (2 * inset), SystemColors.Black);
            }
        }
    }

    private void PaintRow(ICanvas canvas, int x, int y, int row, bool themed)
    {
        bool rowSelected = SelectionMode == DataGridViewSelectionMode.FullRow && row == SelectedRow;
        int cx = -LeftOffset;
        for (int c = 0; c < Columns.Count; c++)
        {
            int w = Columns[c].Width;
            bool selected = (rowSelected || (SelectionMode == DataGridViewSelectionMode.Cell
                && row == CurrentRow && c == CurrentColumn)) && Enabled;
            uint back = selected ? SystemColors.Highlight : SystemColors.Window;
            uint ink = selected ? SystemColors.HighlightText : SystemColors.WindowText;
            canvas.FillRectangle(x + cx, y, w, RowHeight, back);

            string text = c < Rows[row].Count ? Rows[row][c] : "";
            if (!string.IsNullOrEmpty(text))
                TextRules.DrawAlignedText(canvas, text, false, Draw.ContentAlignment.MiddleLeft,
                    x + cx + 3, y, w - 6, RowHeight, FontCss, ink, !Enabled);

            switch (CellBorderStyle)
            {
                case DataGridViewCellBorderStyle.Single:
                    canvas.FillRectangle(x + cx + w - 1, y, 1, RowHeight, GridColor);
                    canvas.FillRectangle(x + cx, y + RowHeight - 1, w, 1, GridColor);
                    break;
                case DataGridViewCellBorderStyle.Raised:
                    ControlPaintEdges.DrawEdge(canvas, x + cx, y, w, RowHeight,
                        ControlPaintEdges.EdgeStyle.Raised);
                    break;
                case DataGridViewCellBorderStyle.Sunken:
                    ControlPaintEdges.DrawEdge(canvas, x + cx, y, w, RowHeight,
                        ControlPaintEdges.EdgeStyle.Sunken);
                    break;
            }

            if (SelectionMode == DataGridViewSelectionMode.Cell
                && row == CurrentRow && c == CurrentColumn && Focused && Enabled)
                ControlPaintGlyphs.DrawFocusRectangle(canvas, x + cx + 1, y + 1, w - 2, RowHeight - 2);

            cx += w;
        }

        _ = themed;
    }

    private void SyncScrollBars()
    {
        int vSpace = Height - HeaderHeight;
        int hSpace = Width - HeaderWidth;
        if (HasVBar())
        {
            _vBar ??= CreateBar("gridv", vertical: true);
            _vBar.Visible = true;
            _vBar.X = Width - 17;
            _vBar.Y = HeaderHeight;
            _vBar.Width = 17;
            _vBar.Height = vSpace - (HasHBar() ? 17 : 0);
            _vBar.Minimum = 0;
            int visible = RowHeight > 0 ? _vBar.Height / RowHeight : 0;
            _vBar.Maximum = Math.Max(0, Rows.Count - 1);
            _vBar.LargeChange = Math.Max(1, visible);
            _vBar.Value = Math.Clamp(TopRowIndex, 0, _vBar.EffectiveMaximum);
            _vBar.Enabled = Enabled;
            _vBar.ValueChanged -= OnVScroll;
            _vBar.ValueChanged += OnVScroll;
        }
        else if (_vBar is not null)
        {
            _vBar.Visible = false;
        }

        if (HasHBar())
        {
            _hBar ??= CreateBar("gridh", vertical: false);
            _hBar.Visible = true;
            _hBar.X = HeaderWidth;
            _hBar.Y = Height - 17;
            _hBar.Width = hSpace - (HasVBar() ? 17 : 0);
            _hBar.Height = 17;
            _hBar.Minimum = 0;
            _hBar.Maximum = Math.Max(0, TotalColumnsWidth - 1);
            _hBar.LargeChange = Math.Max(1, _hBar.Width);
            _hBar.Value = Math.Clamp(LeftOffset, 0, _hBar.EffectiveMaximum);
            _hBar.Enabled = Enabled;
            _hBar.ValueChanged -= OnHScroll;
            _hBar.ValueChanged += OnHScroll;
        }
        else if (_hBar is not null)
        {
            _hBar.Visible = false;
        }
    }

    private ScrollBar CreateBar(string name, bool vertical)
    {
        var bar = new ScrollBar(name) { Vertical = vertical };
        AddChild(bar);
        return bar;
    }

    private void OnVScroll(object? sender, EventArgs e)
    {
        if (_vBar is not null)
            TopRowIndex = _vBar.Value;
    }

    private void OnHScroll(object? sender, EventArgs e)
    {
        if (_hBar is not null)
            LeftOffset = _hBar.Value;
    }
}
