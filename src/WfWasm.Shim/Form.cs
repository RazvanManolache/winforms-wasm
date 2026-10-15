extern alias WfDraw;

namespace WfWasm.Shim;

/// <summary>
/// Top-level window (cf. System.Windows.Forms.Form): title bar with caption
/// and decorative close box, 1px border, white client area. Children paint
/// in client coordinates (below the title bar); hit-testing translates too.
/// </summary>
public class Form : ContainerControl
{
    public const int TitleBarHeight = 28;
    public const int ToolCaptionHeight = 19;
    public const int Border = 1;
    public const string CaptionFontCss = "13px sans-serif";

    private const uint ClientFill = 0xFFFFFFFF;

    public Form(string name = "")
        : base(name)
    {
    }

    public Form()
        : this("")
    {
    }

    public FormBorderStyle FormBorderStyle { get; set; } = FormBorderStyle.Sizable;
    public bool ControlBox { get; set; } = true;
    public bool MinimizeBox { get; set; } = true;
    public bool MaximizeBox { get; set; } = true;
    public bool HelpButton { get; set; }
    public bool ShowIcon { get; set; } = true;
    public WfDraw::System.Drawing.Icon? Icon { get; set; }
    public bool Active { get; set; } = true;
    public FormWindowState WindowState { get; set; } = FormWindowState.Normal;
    public FormStartPosition StartPosition { get; set; } = FormStartPosition.WindowsDefaultLocation;
    public bool ShowInTaskbar { get; set; } = true;
    public SizeGripStyle SizeGripStyle { get; set; } = SizeGripStyle.Auto;
    public bool KeyPreview { get; set; }
    public DialogResult DialogResult { get; set; } = DialogResult.None;
    public IButtonControl? AcceptButton { get; set; }
    public IButtonControl? CancelButton { get; set; }
    public Form? Owner { get; set; }
    public bool Modal { get; private set; }

    private readonly List<Form> _ownedForms = new();

    /// <summary>Owned forms (cf. Form.OwnedForms).</summary>
    public Form[] OwnedForms => _ownedForms.ToArray();

    public static Form? ActiveForm { get; internal set; }

    /// <summary>Transparency color (cf. Form.TransparencyKey).</summary>
    public WfDraw::System.Drawing.Color TransparencyKey { get; set; } = WfDraw::System.Drawing.Color.Empty;

    protected override CreateParams CreateParams => new()
    {
        Caption = Text,
        ClassName = "WfWasm",
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
    };

    /// <summary>Bounds used when maximized (set by the host scene).</summary>
    public (int Width, int Height) MaximizeExtent { get; set; } = (1000, 750);

    public event EventHandler? Load;
    public event EventHandler? Shown;
    public event EventHandler? Activated;
    public event EventHandler? Deactivate;
    public event EventHandler? ResizeBegin;
    public event EventHandler? ResizeEnd;
    public event FormClosingEventHandler? FormClosing;

    /// <summary>Legacy closing notifier (cf. Form.Closing).</summary>
    public event System.ComponentModel.CancelEventHandler? Closing;

    /// <summary>Raises the legacy <c>Closing</c> event (cf. Form.OnClosing).</summary>
    protected virtual void OnClosing(System.ComponentModel.CancelEventArgs e) =>
        Closing?.Invoke(this, e);

    public event EventHandler? Closed;
    public event EventHandler? HelpRequested;

    protected virtual void OnLoad(EventArgs e) => Load?.Invoke(this, e);
    protected virtual void OnShown(EventArgs e) => Shown?.Invoke(this, e);
    protected virtual void OnResizeBegin(EventArgs e) => ResizeBegin?.Invoke(this, e);
    protected virtual void OnResizeEnd(EventArgs e) => ResizeEnd?.Invoke(this, e);
    protected virtual void OnActivated(EventArgs e) => Activated?.Invoke(this, e);
    protected virtual void OnDeactivate(EventArgs e) => Deactivate?.Invoke(this, e);

    /// <summary>Raises help-button clicks (cf. OnHelpButtonClicked). No-op here.</summary>
    protected virtual void OnHelpButtonClicked(System.ComponentModel.CancelEventArgs e)
    {
    }

    protected virtual void OnFormClosing(FormClosingEventArgs e)
    {
        FormClosing?.Invoke(this, e);
        Closing?.Invoke(this, e);
    }

    protected virtual void OnFormClosed(FormClosingEventArgs e)
    {
    }

    protected virtual void OnClosing(FormClosingEventArgs e) => OnFormClosing(e);
    protected virtual void OnClosed(EventArgs e) => Closed?.Invoke(this, e);

    public void Activate()
    {
        ActiveForm = this;
        Active = true;
        OnActivated(EventArgs.Empty);
    }

    public void Close()
    {
        var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
        OnClosing(args);
        OnFormClosing(args);
        if (args.Cancel)
            return;
        Visible = false;
        Modal = false;
        OnClosed(EventArgs.Empty);
        Dispose();
    }

    /// <summary>Modal show (cf. Form.ShowDialog): headless, marks modal and
    /// returns the current DialogResult; the browser host presents the modal
    /// when it mounts rewritten apps.</summary>
    public DialogResult ShowDialog()
    {
        Modal = true;
        Visible = true;
        return DialogResult;
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return ShowDialog();
    }

    public void AddOwnedForm(Form ownedForm)
    {
        ArgumentNullException.ThrowIfNull(ownedForm);
        if (!_ownedForms.Contains(ownedForm))
            _ownedForms.Add(ownedForm);
        ownedForm.Owner = this;
    }

    public void RemoveOwnedForm(Form ownedForm)
    {
        ArgumentNullException.ThrowIfNull(ownedForm);
        if (_ownedForms.Remove(ownedForm) && ownedForm.Owner == this)
            ownedForm.Owner = null;
    }

    private (int X, int Y, int Width, int Height)? _restoreBounds;

    /// <summary>Caption height: small for tool windows, none for borderless.</summary>
    public int CaptionHeight => FormBorderStyle switch
    {
        FormBorderStyle.None => 0,
        FormBorderStyle.FixedToolWindow or FormBorderStyle.SizableToolWindow => ToolCaptionHeight,
        _ => TitleBarHeight,
    };

    /// <summary>Frame thickness (left, top, right, bottom) per style.</summary>
    public (int Left, int Top, int Right, int Bottom) FrameThickness => FormBorderStyle switch
    {
        FormBorderStyle.None => (0, 0, 0, 0),
        FormBorderStyle.FixedSingle => (1, 1, 1, 1),
        FormBorderStyle.Fixed3D => (2, 2, 2, 2),
        FormBorderStyle.FixedDialog or FormBorderStyle.Sizable => (3, 3, 3, 3),
        FormBorderStyle.FixedToolWindow => (1, 1, 1, 1),
        _ => (2, 2, 2, 2),
    };

    /// <summary>Caption buttons right-to-left with bounds (Close, Max, Min, Help).</summary>
    public List<(CaptionButton Button, int X, int Y, int Size)> CaptionButtons()
    {
        var buttons = new List<(CaptionButton, int, int, int)>();
        if (!ControlBox || CaptionHeight == 0)
            return buttons;
        bool tool = FormBorderStyle is FormBorderStyle.FixedToolWindow or FormBorderStyle.SizableToolWindow;
        int size = CaptionHeight - (tool ? 5 : 7);
        int y = (CaptionHeight - size) / 2;
        int x = Width - FrameThickness.Right - 2 - size;
        buttons.Add((CaptionButton.Close, x, y, size));
        bool max = MaximizeBox && !tool;
        bool min = MinimizeBox && !tool;
        if (max)
        {
            x -= 2 + size;
            buttons.Add((WindowState == FormWindowState.Maximized
                ? CaptionButton.Restore : CaptionButton.Maximize, x, y, size));
        }

        if (min)
        {
            x -= 2 + size;
            buttons.Add((CaptionButton.Minimize, x, y, size));
        }

        if (HelpButton)
        {
            x -= 2 + size;
            buttons.Add((CaptionButton.Help, x, y, size));
        }

        return buttons;
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        var frame = FrameThickness;
        int captionH = CaptionHeight;
        PaintFrame(canvas, ax, ay, frame);
        if (captionH > 0)
            PaintCaption(canvas, ax + frame.Left, ay + frame.Top, Width - frame.Left - frame.Right, captionH);

        int clientH = (WindowState == FormWindowState.Minimized ? 0 : Height)
            - frame.Top - captionH - frame.Bottom;
        if (clientH > 0)
            canvas.FillRectangle(ax + frame.Left, ay + frame.Top + captionH,
                Width - frame.Left - frame.Right, clientH, ClientFill);

        if (FormBorderStyle == FormBorderStyle.Sizable && WindowState == FormWindowState.Normal)
            ControlPaintGlyphs.DrawSizeGrip(canvas,
                ax + Width - frame.Right - 16, ay + Height - frame.Bottom - 16, 16, 16);
    }

    protected override void PaintChildren(ICanvas canvas, int ax, int ay)
    {
        if (WindowState == FormWindowState.Minimized)
            return;
        var frame = FrameThickness;
        int ox = ax + frame.Left;
        int oy = ay + frame.Top + CaptionHeight;
        foreach (Control child in Children)
            PaintChild(canvas, child, ox, oy);
    }

    protected override (int cx, int cy) ToClientPoint(int lx, int ly)
    {
        var frame = FrameThickness;
        return (lx - frame.Left, ly - (frame.Top + CaptionHeight));
    }

    protected override (int lx, int ly) ToLocalPoint(int cx, int cy)
    {
        var frame = FrameThickness;
        return (cx + frame.Left, cy + frame.Top + CaptionHeight);
    }

    public override void HandleClickLocal(int localX, int localY)
    {
        var frame = FrameThickness;
        int captionH = CaptionHeight;
        if (localY >= frame.Top && localY < frame.Top + captionH)
        {
            foreach ((CaptionButton button, int bx, int by, int size) in CaptionButtons())
            {
                int ax = bx + frame.Left;
                int ay = by + frame.Top;
                if (localX >= ax && localX < ax + size && localY >= ay && localY < ay + size)
                {
                    ActivateCaptionButton(button);
                    return;
                }
            }
        }

        base.HandleClickLocal(localX, localY);
    }

    private void ActivateCaptionButton(CaptionButton button)
    {
        switch (button)
        {
            case CaptionButton.Close:
                Visible = false;
                Closed?.Invoke(this, EventArgs.Empty);
                break;
            case CaptionButton.Minimize:
                WindowState = FormWindowState.Minimized;
                break;
            case CaptionButton.Maximize:
                _restoreBounds = (X, Y, Width, Height);
                X = 0;
                Y = 0;
                Width = MaximizeExtent.Width;
                Height = MaximizeExtent.Height;
                WindowState = FormWindowState.Maximized;
                break;
            case CaptionButton.Restore:
                if (_restoreBounds.HasValue)
                {
                    (X, Y, Width, Height) = _restoreBounds.Value;
                    _restoreBounds = null;
                }

                WindowState = FormWindowState.Normal;
                break;
            default:
                HelpRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void PaintFrame(ICanvas canvas, int ax, int ay, (int Left, int Top, int Right, int Bottom) frame)
    {
        int paintH = WindowState == FormWindowState.Minimized
            ? frame.Top + CaptionHeight + frame.Bottom : Height;
        switch (FormBorderStyle)
        {
            case FormBorderStyle.None:
                break;
            case FormBorderStyle.FixedSingle:
            case FormBorderStyle.FixedToolWindow:
                ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, paintH,
                    SystemColors.Black, ControlPaintEdges.LineBorderStyle.Solid);
                break;
            case FormBorderStyle.Fixed3D:
                ControlPaintEdges.DrawEdge(canvas, ax, ay, Width, paintH,
                    ControlPaintEdges.EdgeStyle.Sunken);
                break;
            case FormBorderStyle.FixedDialog:
                canvas.FillRectangle(ax, ay, Width, paintH, SystemColors.Control);
                ControlPaintEdges.DrawEdge(canvas, ax + 1, ay + 1, Width - 2, paintH - 2,
                    ControlPaintEdges.EdgeStyle.Raised);
                break;
            default:
                canvas.FillRectangle(ax, ay, Width, paintH, SystemColors.Control);
                ControlPaintEdges.DrawEdge(canvas, ax + 1, ay + 1, Width - 2, paintH - 2,
                    ControlPaintEdges.EdgeStyle.Raised);
                break;
        }
    }

    private void PaintCaption(
        ICanvas canvas, int x, int y, int width, int height)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        if (themed)
        {
            canvas.FillRectangle(x, y, width, height,
                Active ? SystemColors.ThemedCaptionActive : SystemColors.ThemedCaptionInactive);
        }
        else
        {
            if (Active)
                canvas.FillGradientVertical(x, y, width, height,
                    SystemColors.ActiveCaptionTop, SystemColors.ActiveCaptionBottom);
            else
                canvas.FillGradientVertical(x, y, width, height,
                    SystemColors.InactiveCaptionTop, SystemColors.InactiveCaptionBottom);
        }

        int textX = x + 6;
        if (ShowIcon)
        {
            PaintIcon(canvas, x + 4, y + ((height - 16) / 2));
            textX = x + 24;
        }

        uint ink = themed
            ? (Active ? SystemColors.Black : SystemColors.GrayText)
            : (Active ? SystemColors.White : SystemColors.ControlLight);
        if (!string.IsNullOrEmpty(Text))
            canvas.DrawTextAligned(Text, textX, y + (height / 2f),
                CaptionFontCss, ink, TextHAlign.Left, TextVAlign.Middle);

        foreach ((CaptionButton button, int bx, int by, int size) in CaptionButtons())
            ControlPaintGlyphs.DrawCaptionButton(canvas, x + bx, y + by, size, size,
                button, pressed: false, hot: false, themed, Active);
    }

    /// <summary>Caption area that starts a window drag (excludes buttons).</summary>
    public bool CaptionDragZone(int localX, int localY)
    {
        var frame = FrameThickness;
        if (localY < frame.Top || localY >= frame.Top + CaptionHeight)
            return false;
        foreach ((CaptionButton _, int bx, int by, int size) in CaptionButtons())
        {
            int ax = bx + frame.Left;
            int ay = by + frame.Top;
            if (localX >= ax && localX < ax + size && localY >= ay && localY < ay + size)
                return false;
        }

        return ControlBox;
    }

    private static void PaintIcon(ICanvas canvas, int x, int y)
    {
        canvas.FillRectangle(x, y, 16, 16, SystemColors.Highlight);
        canvas.FillRectangle(x + 2, y + 2, 12, 12, SystemColors.White);
        canvas.FillRectangle(x + 4, y + 4, 8, 2, SystemColors.Highlight);
        canvas.FillRectangle(x + 4, y + 7, 8, 1, SystemColors.ControlDark);
        canvas.FillRectangle(x + 4, y + 9, 5, 1, SystemColors.ControlDark);
    }
}

/// <summary>Editable text contract (cf. System.Windows.Forms.TextBoxBase).</summary>
public class TextBoxBase : Control
{
    public TextBoxBase(string name = "")
        : base(name)
    {
    }

    public TextBoxBase()
        : this("")
    {
    }

    public virtual bool Multiline { get; set; }
    public virtual bool ReadOnly { get; set; }
    public int MaxLength { get; set; } = 32767;
    public bool WordWrap { get; set; } = true;
    public bool AcceptsReturn { get; set; }
    public bool AcceptsTab { get; set; }
    public bool HideSelection { get; set; } = true;
    public bool ShortcutsEnabled { get; set; } = true;
    public char PasswordChar { get; set; }
    public bool UseSystemPasswordChar { get; set; }
    public ScrollBars ScrollBars { get; set; } = ScrollBars.None;
    public int SelectionStart { get; set; }
    public int SelectionLength { get; set; }
    public string SelectedText => Text.Substring(
        Math.Clamp(SelectionStart, 0, Text.Length),
        Math.Clamp(SelectionLength, 0, Text.Length - Math.Clamp(SelectionStart, 0, Text.Length)));

    public virtual void Select(int start, int length)
    {
        SelectionStart = start;
        SelectionLength = length;
    }

    public virtual void SelectAll() => Select(0, Text.Length);
    public virtual void DeselectAll() => Select(0, 0);

    public virtual void AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!ReadOnly)
            Text += text;
    }

    /// <summary>Border style (cf. BorderStyle).</summary>
    public virtual BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;

    /// <summary>Preferred single-line height (cf. PreferredHeight).</summary>
    public int PreferredHeight => Font.Height + 7;
}

/// <summary>
/// Single-line text box (cf. TextBox): white fill, gray border, text inset.
/// Editing is programmatic for now (<see cref="HandleTextInput"/> and
/// <see cref="HandleBackspace"/> act only when <see cref="Focused"/>);
/// caret painting awaits canvas text measurement.
/// </summary>
public class TextBox : TextBoxBase
{
    private const uint BackFill = 0xFFFFFFFF;
    private const uint BorderFill = 0xABADB3FF;
    private const uint TextColor = 0x000000FF;

    private string _text = "";

    public TextBox(string name = "")
        : base(name)
    {
    }

    public TextBox()
        : this("")
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
            CaretIndex = Math.Clamp(CaretIndex, 0, _text.Length);
            OnTextChanged(EventArgs.Empty);
        }
    }

    public override bool Focused { get; set; }

    public int CaretIndex { get; private set; }

    public void MoveCaretTo(int index) => CaretIndex = Math.Clamp(index, 0, _text.Length);

    /// <summary>Insert text at the caret (single-line: newlines dropped). No-op unless focused.</summary>
    public void HandleTextInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Focused)
            return;
        string clean = text.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        _text = _text.Insert(CaretIndex, clean);
        CaretIndex += clean.Length;
        base.Text = _text;
    }

    /// <summary>Delete the char before the caret. No-op unless focused or caret is at 0.</summary>
    public void HandleBackspace()
    {
        if (!Focused || CaretIndex == 0)
            return;
        _text = _text.Remove(CaretIndex - 1, 1);
        CaretIndex--;
        base.Text = _text;
    }

    /// <summary>Delete the char at the caret. No-op unless focused or caret is at the end.</summary>
    public void HandleDelete()
    {
        if (!Focused || CaretIndex >= _text.Length)
            return;
        _text = _text.Remove(CaretIndex, 1);
        base.Text = _text;
    }

    public override BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;
    public string FontCss { get; set; } = "13px sans-serif";
    public HorizontalAlignment TextAlign { get; set; } = HorizontalAlignment.Left;
    public AutoCompleteMode AutoCompleteMode { get; set; } = AutoCompleteMode.None;
    public AutoCompleteSource AutoCompleteSource { get; set; } = AutoCompleteSource.None;
    public AutoCompleteStringCollection AutoCompleteCustomSource { get; set; } = new();

    public uint SelectionBackColor { get; set; } = SystemColors.Highlight;
    public uint SelectionForeColor { get; set; } = SystemColors.HighlightText;

    /// <summary>First visible line (multiline vertical scroll).</summary>
    public int TopLine { get; set; }

    /// <summary>Horizontal scroll offset in pixels (multiline, no wrap).</summary>
    public int LeftOffset { get; set; }

    /// <summary>Average advance estimate until text metrics arrive (selection/caret only).</summary>
    public static float EstimateCharWidth(string fontCss)
    {
        foreach (string part in fontCss.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.EndsWith("px", StringComparison.OrdinalIgnoreCase)
                && float.TryParse(part[..^2], out float px))
                return px * 0.5f;
        }

        return 6.5f;
    }

    public string DisplayText
    {
        get
        {
            if (UseSystemPasswordChar)
                return new string('●', _text.Length);
            if (PasswordChar != '\0')
                return new string(PasswordChar, _text.Length);
            return _text;
        }
    }

    public override void HandleClickLocal(int localX, int localY)
    {
        base.HandleClickLocal(localX, localY);
        if (!Enabled || ReadOnly)
            return;
        // Collapse selection, move caret by estimated column (multiline: first line).
        int inset = ContentInset();
        float charW = EstimateCharWidth(FontCss);
        int col = Math.Clamp((int)((localX - inset - 3) / charW), 0, DisplayText.Length);
        string first = DisplayText.Split('\n')[0];
        SelectionStart = Math.Min(col, first.Length);
        SelectionLength = 0;
        CaretIndex = SelectionStart;
        EnsureCaretVisible();
    }

    public void EnsureCaretVisible()
    {
        if (Multiline)
            return;
        float charW = EstimateCharWidth(FontCss);
        int inset = ContentInset();
        float caretX = inset + 3 + (CaretIndex * charW);
        int viewW = Width - (2 * inset) - (HasVScroll() ? ScrollBarWidth : 0);
        if (caretX - LeftOffset > viewW - 2)
            LeftOffset = (int)(caretX - (viewW - 2));
        if (caretX - LeftOffset < 2)
            LeftOffset = Math.Max(0, (int)caretX - 2);
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        bool themed = RenderingConfig.UseVisualStyles;
        int inset = ContentInset();
        switch (BorderStyle)
        {
            case BorderStyle.None:
                canvas.FillRectangle(ax, ay, Width, Height, BackFill);
                break;
            case BorderStyle.FixedSingle:
                ControlPaintEdges.DrawBorder(canvas, ax, ay, Width, Height,
                    themed ? SystemColors.ThemedEditBorder : SystemColors.Black,
                    ControlPaintEdges.LineBorderStyle.Solid);
                canvas.FillRectangle(ax + 1, ay + 1, Width - 2, Height - 2, BackFill);
                break;
            default:
                if (themed)
                {
                    ControlPaintGlyphs.DrawVisualStyleBorder(canvas, ax, ay, Width, Height);
                    canvas.FillRectangle(ax + 1, ay + 1, Width - 2, Height - 2, BackFill);
                }
                else
                {
                    ControlPaintEdges.DrawEdge(canvas, ax, ay, Width, Height,
                        ControlPaintEdges.EdgeStyle.Sunken);
                    canvas.FillRectangle(ax + 2, ay + 2, Width - 4, Height - 4, BackFill);
                }

                break;
        }

        int vBar = HasVScroll() ? ScrollBarWidth : 0;
        int hBar = HasHScroll() ? ScrollBarWidth : 0;
        int contentW = Width - (2 * inset) - vBar;
        int contentH = Height - (2 * inset) - hBar;
        string display = DisplayText;

        if (!Multiline)
        {
            PaintSingleLine(canvas, ax + inset - LeftOffset, ay, contentW, contentH, display, inset);
        }
        else
        {
            string[] lines = display.Split('\n');
            float lineH = TextRules.EstimateLineHeight(FontCss);
            int visible = Math.Max(1, (int)(contentH / lineH));
            for (int i = 0; i < visible && TopLine + i < lines.Length; i++)
            {
                float ly = ay + inset + (i * lineH);
                PaintLine(canvas, lines[TopLine + i], TopLine + i,
                    ax + inset - LeftOffset, ly, contentW, lineH);
            }
        }

        if (HasVScroll())
            SyncVBar(ax, ay, inset, contentH);
        else if (_vBar is not null)
            _vBar.Visible = false;
        if (HasHScroll())
            SyncHBar(ax, ay, inset, contentW);
        else if (_hBar is not null)
            _hBar.Visible = false;
    }

    private int ContentInset() => BorderStyle switch
    {
        BorderStyle.None => 0,
        BorderStyle.FixedSingle => 1,
        _ => RenderingConfig.UseVisualStyles ? 1 : 2,
    };

    private const int ScrollBarWidth = 17;

    private bool HasVScroll() => Multiline
        && ScrollBars is ScrollBars.Vertical or ScrollBars.Both;

    private bool HasHScroll() => Multiline
        && ScrollBars is ScrollBars.Horizontal or ScrollBars.Both;

    private void PaintSingleLine(
        ICanvas canvas, int tx, int ay, int contentW, int contentH,
        string display, int inset)
    {
        if (display.Length == 0)
        {
            PaintCaret(canvas, tx + 3, ay + inset + 2, contentH - 4);
            return;
        }

        float charW = EstimateCharWidth(FontCss);
        bool showSel = SelectionLength > 0 && (Focused || !HideSelection);
        int selStart = Math.Clamp(SelectionStart, 0, display.Length);
        int selEnd = Math.Clamp(SelectionStart + SelectionLength, 0, display.Length);
        float midY = ay + inset + (contentH / 2f);

        if (showSel && selEnd > selStart)
        {
            float sx = tx + 3 + (selStart * charW);
            canvas.FillRectangle(sx, ay + inset + 1,
                (selEnd - selStart) * charW, contentH - 2, SelectionBackColor);
            canvas.DrawTextAligned(display, tx + 3, midY, FontCss,
                TextColor, TextHAlign.Left, TextVAlign.Middle);
            canvas.DrawTextAligned(display.Substring(selStart, selEnd - selStart),
                sx, midY, FontCss, SelectionForeColor, TextHAlign.Left, TextVAlign.Middle);
        }
        else
        {
            canvas.DrawTextAligned(display, tx + 3, midY, FontCss,
                Enabled ? TextColor : SystemColors.GrayText,
                TextHAlign.Left, TextVAlign.Middle);
        }

        PaintCaret(canvas, tx + 3 + (CaretIndex * charW), ay + inset + 2, contentH - 4);
    }

    private void PaintLine(
        ICanvas canvas, string line, int lineIndex,
        float tx, float ly, int contentW, float lineH)
    {
        if (line.Length == 0)
            return;
        canvas.DrawTextAligned(line, tx + 3, ly, FontCss,
            Enabled ? TextColor : SystemColors.GrayText,
            TextHAlign.Left, TextVAlign.Top);
        _ = contentW;
        _ = lineIndex;
    }

    private void PaintCaret(ICanvas canvas, float x, float y, float height)
    {
        if (Focused && Enabled && !ReadOnly && height > 0)
            canvas.FillRectangle(x, y, 1, height, SystemColors.WindowText);
    }

    private ScrollBar? _vBar;
    private ScrollBar? _hBar;

    private void SyncVBar(int ax, int ay, int inset, int contentH)
    {
        string[] lines = DisplayText.Split('\n');
        float lineH = TextRules.EstimateLineHeight(FontCss);
        int visible = Math.Max(1, (int)(contentH / lineH));
        int maxTop = Math.Max(0, lines.Length - visible);
        _vBar ??= CreateOwnedBar("vscroll", vertical: true);
        _vBar.Visible = true;
        _vBar.X = Width - inset - ScrollBarWidth;
        _vBar.Y = inset;
        _vBar.Width = ScrollBarWidth;
        _vBar.Height = contentH;
        _vBar.Minimum = 0;
        // WinForms Maximum rule: thumb top reaches Maximum - LargeChange + 1.
        _vBar.Maximum = maxTop + visible - 1;
        _vBar.LargeChange = visible;
        _vBar.Value = Math.Clamp(TopLine, 0, _vBar.EffectiveMaximum);
        _vBar.Enabled = Enabled;
        _ = ax;
        _ = ay;
    }

    private void SyncHBar(int ax, int ay, int inset, int contentW)
    {
        _hBar ??= CreateOwnedBar("hscroll", vertical: false);
        _hBar.Visible = true;
        _hBar.X = inset;
        _hBar.Y = Height - inset - ScrollBarWidth;
        _hBar.Width = contentW;
        _hBar.Height = ScrollBarWidth;
        _hBar.Minimum = 0;
        _hBar.Maximum = 400;
        _hBar.LargeChange = 40;
        _hBar.Value = Math.Clamp(LeftOffset, 0, _hBar.EffectiveMaximum);
        _hBar.Enabled = Enabled;
        _ = ax;
        _ = ay;
    }

    private ScrollBar CreateOwnedBar(string name, bool vertical)
    {
        var bar = new ScrollBar(name) { Vertical = vertical };
        bar.ValueChanged += (_, _) =>
        {
            if (vertical)
                TopLine = bar.Value;
            else
                LeftOffset = bar.Value;
        };
        AddChild(bar);
        return bar;
    }
}

/// <summary>Rich text box (cf. RichTextBox). Rich formatting renders as
/// plain text until RTF run painting arrives; Rtf/SelectedRtf hold raw markup.</summary>
public class RichTextBox : TextBox
{
    public RichTextBox()
    {
        Multiline = true;
    }

    public RichTextBox(string name)
        : base(name)
    {
        Multiline = true;
    }

    public string Rtf { get; set; } = string.Empty;
    public string SelectedRtf { get; set; } = string.Empty;
    public bool DetectUrls { get; set; } = true;
    public new RichTextBoxScrollBars ScrollBars { get; set; } = RichTextBoxScrollBars.Both;
    public bool ShowSelectionMargin { get; set; }
    public int BulletIndent { get; set; }
    public bool RightMargin { get; set; }

    public event LinkClickedEventHandler? LinkClicked;

    protected virtual void OnLinkClicked(LinkClickedEventArgs e) =>
        LinkClicked?.Invoke(this, e);

    /// <summary>Click simulation for tests/hosts: raises LinkClicked (cf. link hit-testing).</summary>
    public void SimulateLinkClick(string linkText) =>
        OnLinkClicked(new LinkClickedEventArgs(linkText));

    public int Find(string str) =>
        Text.IndexOf(str, StringComparison.Ordinal);

    public int Find(string str, int start, RichTextBoxFinds options) =>
        Text.IndexOf(str, start, options.HasFlag(RichTextBoxFinds.MatchCase)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase);
}

/// <summary>Clickable text (cf. LinkLabel). Links paint as plain text until
/// run painting arrives; clicks raise LinkClicked.</summary>
public class LinkLabel : Label
{
    public LinkLabel()
    {
        TabStop = true;
    }

    public LinkLabel(string name)
        : base(name)
    {
        TabStop = true;
    }

    public LinkLabelLinkCollection Links { get; } = new();

    /// <summary>Link text range (cf. LinkArea).</summary>
    public LinkArea LinkArea { get; set; }
    /// <summary>Link color (cf. LinkColor).</summary>
    public WfDraw::System.Drawing.Color LinkColor { get; set; } = WfDraw::System.Drawing.Color.Blue;

    /// <summary>Active link color (cf. ActiveLinkColor).</summary>
    public WfDraw::System.Drawing.Color ActiveLinkColor { get; set; } = WfDraw::System.Drawing.Color.Red;

    /// <summary>Visited link color (cf. VisitedLinkColor).</summary>
    public WfDraw::System.Drawing.Color VisitedLinkColor { get; set; } = WfDraw::System.Drawing.Color.Purple;

    /// <summary>Disabled link color (cf. DisabledLinkColor).</summary>
    public WfDraw::System.Drawing.Color DisabledLinkColor { get; set; } = ButtonRenderer.ToColor(SystemColors.GrayText);
    public bool LinkVisited
    {
        get => Links.Count > 0 && Links[0].Visited;
        set
        {
            foreach (LinkLabelLink link in Links)
                link.Visited = value;
        }
    }

    public event LinkLabelLinkClickedEventHandler? LinkClicked;

    protected virtual void OnLinkClicked(LinkLabelLinkClickedEventArgs e) =>
        LinkClicked?.Invoke(this, e);

    /// <summary>Click simulation for tests/hosts: hits the link at the
    /// position, marks it visited, and raises LinkClicked.</summary>
    public void SimulateLinkClick(int position)
    {
        foreach (LinkLabelLink link in Links)
        {
            if (!link.Enabled || position < link.Start || position >= link.Start + link.Length)
                continue;
            link.Visited = true;
            OnLinkClicked(new LinkLabelLinkClickedEventArgs(link));
            return;
        }
    }
}

/// <summary>A clickable span (cf. LinkLabel.Link).</summary>
public class LinkLabelLink
{
    public int Start { get; set; }
    public int Length { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Visited { get; set; }
    public object? LinkData { get; set; }
    public string? Description { get; set; }
    public string? Name { get; set; }
}

/// <summary>Link collection (cf. LinkLabel.LinkCollection).</summary>
public class LinkLabelLinkCollection : System.Collections.IList
{
    private readonly System.Collections.Generic.List<LinkLabelLink> _links = new();

    public int Count => _links.Count;
    public bool IsReadOnly => false;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => _links;
    public LinkLabelLink this[int index]
    {
        get => _links[index];
        set => _links[index] = value;
    }

    object? System.Collections.IList.this[int index]
    {
        get => _links[index];
        set => _links[index] = (LinkLabelLink)value!;
    }

    public LinkLabelLink Add(int start, int length, object? linkData = null)
    {
        var link = new LinkLabelLink { Start = start, Length = length, LinkData = linkData };
        _links.Add(link);
        return link;
    }

    public void Add(LinkLabelLink value) => _links.Add(value);
    public void Clear() => _links.Clear();
    public bool Contains(LinkLabelLink value) => _links.Contains(value);
    public int IndexOf(LinkLabelLink value) => _links.IndexOf(value);
    public void Remove(LinkLabelLink value) => _links.Remove(value);
    public void RemoveAt(int index) => _links.RemoveAt(index);

    int System.Collections.IList.Add(object? value)
    {
        Add((LinkLabelLink)value!);
        return _links.Count - 1;
    }

    bool System.Collections.IList.Contains(object? value) => value is LinkLabelLink link && _links.Contains(link);
    int System.Collections.IList.IndexOf(object? value) => value is LinkLabelLink link ? _links.IndexOf(link) : -1;
    void System.Collections.IList.Insert(int index, object? value) => _links.Insert(index, (LinkLabelLink)value!);
    void System.Collections.IList.Remove(object? value)
    {
        if (value is LinkLabelLink link)
            _links.Remove(link);
    }

    public void CopyTo(Array array, int index) => ((System.Collections.ICollection)_links).CopyTo(array, index);
    public System.Collections.IEnumerator GetEnumerator() => _links.GetEnumerator();
}
