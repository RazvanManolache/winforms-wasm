namespace WfWasm.Shim;

/// <summary>Which app scene the single canvas shows: real mirrored apps only.</summary>
public enum AppScene
{
    Smoke,
    Calendar,
    Paint,
    Calculator,
    Todo,
    TicTacToe,
    TelerikPhoto,
    TelerikBookshelf,
    TelerikBugtracker,
    TelerikDemoAppsHub,
    TelerikDiagram,
    TelerikErp,
    TelerikExportWithDpl,
    TelerikFileExplorer,
    TelerikHotelApp,
    TelerikMedicalApp,
    TelerikRichTextEditor,
    TelerikRssReader,
    TelerikSpellCheck,
    TelerikToast,
    SyncfusionButtons,
    SyncfusionComboBox,
    SyncfusionDataGrid,
    SyncfusionChart,
    SyncfusionDiagram,
    SyncfusionCalendar,
    SyncfusionTreeView,
    SyncfusionNumeric,
    SyncfusionSkinManager,
    SyncfusionWord,
    SyncfusionControlBars,
    SyncfusionDocking,
    SyncfusionWizard,
    SyncfusionGauges,
    SyncfusionPivotGrid,
    SyncfusionGridGrouping,
    SyncfusionGridCells,
    SyncfusionMaps,
    SyncfusionTreeMap,
    SyncfusionBarcode,
    SyncfusionBulletGraph,
    SyncfusionSmithChart,
    SyncfusionPivotChart,
    SyncfusionHtml,
    SyncfusionNavigationDrawer,
    SyncfusionMultiColumnTree,
    SyncfusionRangeSlider,
    SyncfusionRadialSlider,
    SyncfusionRating,
    SyncfusionMaskedEdit,
    SyncfusionToolTip,
    SyncfusionScrollFrame,
    SyncfusionTabbedMdi,
    SyncfusionHubTile,
    SyncfusionSpellChecker,
    SyncfusionToggle,
    SyncfusionSplitContainer,
    SyncfusionTileLayout,
    SyncfusionStatusBar,
    SyncfusionFontCombo,
    SyncfusionAutoComplete,
    SyncfusionClock,
    BenchmarkOpenPdn,
    BenchmarkPkhex,
    BenchmarkKeePass,
    BenchmarkGitExtensions,
    BenchmarkBizHawk,
    BenchmarkDockPanel,
    BenchmarkKrypton,
    BenchmarkMinesweeper,
    BenchmarkTreemap,
    BenchmarkChip8,
    BenchmarkSubtitleEdit,
    BenchmarkShareX,
    BenchmarkNaps2,
    BenchmarkMRemoteNg,
    BenchmarkReaLTaiizor,
    BenchmarkCyotek,
    BenchmarkAdvancedDataGridView,
    BenchmarkFastColoredTextBox,
    BenchmarkExternalCalculator,
    BenchmarkVgaPainter,
    BenchmarkHexBox,
    BenchmarkReflector,
    BenchmarkLinqPad5,
    BenchmarkFiddlerClassic,
    BenchmarkDevExpress,
    BenchmarkComponentOne,
    BenchmarkInfragistics,
}

/// <summary>
/// Owns the interactive app state behind one canvas: the active scene,
/// click dispatch via hit-testing (with textbox focus tracking) and key
/// routing (focused box first, scene-switch digits otherwise).
/// </summary>
public sealed class CanvasApp
{
    /// <summary>Current view size; tracks the canvas backing store so scenes fill available space.</summary>
    public static int ViewWidth { get; set; } = 1000;
    public static int ViewHeight { get; set; } = 750;

    private const uint BackFill = 0xF0F0F0FF;

    private readonly Form _smokeForm;
    private readonly Label _smokeStatus;
    private readonly Control _paintRoot;

    private Control? _hovered;
    private readonly List<Button> _pressed = new();
    private Form? _dragForm;
    private int _dragOffX;
    private int _dragOffY;
    private PaintCanvas? _painting;

    /// <summary>Raised when the paint scene asks to download its canvas.</summary>
    public event EventHandler? SaveRequested;

    private MessageBoxOverlay? _modal;

    /// <summary>Whether a modal message box currently captures all input.</summary>
    public bool HasModal => _modal is not null;

    /// <summary>Active modal overlay root (for embedders and tests).</summary>
    public Control? ModalRoot => _modal?.Window;

    /// <summary>Show a modal message box (cf. MessageBox.Show(text, caption)).</summary>
    public void ShowMessage(string text, string caption)
    {
        CloseOverlays();
        var overlay = new MessageBoxOverlay(text, caption);
        overlay.OkButton.Click += (_, _) => _modal = null;
        _modal = overlay;
    }

    private void DismissModal() => _modal = null;

    public CanvasApp()
    {
        _smokeForm = new Form("form") { X = 100, Y = 150, Width = 800, Height = 450, Text = "Form1" };
        _smokeForm.AddChild(new Label("label1")
        {
            X = 180, Y = 169, Width = 300, Height = 50,
            Text = "Hello .NET", FontCss = "36px sans-serif",
        });
        _smokeStatus = new Label("status") { X = 20, Y = 380, Width = 400, Height = 30, Text = "Ready." };
        var ok = new Button("ok") { X = 690, Y = 380, Width = 90, Height = 30, Text = "OK", IsDefault = true };
        ok.Click += (_, _) => _smokeStatus.Text = "OK clicked!";
        _smokeForm.AddChild(ok);
        _smokeForm.AddChild(_smokeStatus);

        Calendar = new CalendarScreen(120, 45);
        _paintRoot = GalleryScenes.BuildPaint(() => SaveRequested?.Invoke(this, EventArgs.Empty));
        Calculator = new CalcScreen(120, 45, ShowMessage);
        Todo = new TodoScreen(60, 30, ShowMessage);
        TicTacToe = new TicTacToeScreen(60, 30, ShowMessage);
    }

    public AppScene Current { get; private set; } = AppScene.Smoke;

    /// <summary>Switch scenes from the host menu (cf. digit keys in KeyChar).</summary>
    public void ShowScene(AppScene scene) => Current = scene;

    /// <summary>Active scene root (for embedders and tests).</summary>
    public Control CurrentSceneRoot => CurrentRoot;

    public CalendarScreen Calendar { get; }

    public CalcScreen Calculator { get; }

    public TodoScreen Todo { get; }

    public TicTacToeScreen TicTacToe { get; }

    public Form SmokeForm => _smokeForm;

    public void Paint(ICanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.FillRectangle(0, 0, ViewWidth, ViewHeight, BackFill);
        CurrentRoot.Render(canvas);
        if (_modal is not null)
        {
            canvas.FillRectangle(0, 0, ViewWidth, ViewHeight, 0x00000080);
            _modal.Window.Render(canvas);
        }
    }

    /// <summary>Dispatch a canvas click: hit-test, focus textboxes, route with local coords.</summary>
    public void ClickAt(int x, int y)
    {
        if (_modal is not null)
        {
            var hit = _modal.Window.HitTestDeep(x, y);
            if (hit.HasValue)
                hit.Value.Control.HandleClickLocal(hit.Value.LocalX, hit.Value.LocalY);
            return;
        }

        var deep = CurrentRoot.HitTestDeep(x, y);
        SetFocus(deep.HasValue ? FindTextBox(CurrentRoot, deep.Value.Control) : null);
        if (!deep.HasValue)
        {
            CloseOverlays();
            return;
        }

        if (ReferenceEquals(deep.Value.Control, Calendar.Grid) && Current == AppScene.Calendar)
        {
            // Day cells are painted, not child controls: map to a date in the
            // grid's parent (window client) space. Blanks are no-ops.
            int ox = Calendar.Window.X + Form.Border;
            int oy = Calendar.Window.Y + Form.TitleBarHeight + Form.Border;
            Calendar.Grid.ClickDayAt(x - ox, y - oy);
            return;
        }

        CloseOverlaysExcept(deep.Value.Control);
        deep.Value.Control.HandleClickLocal(deep.Value.LocalX, deep.Value.LocalY);
    }

    /// <summary>Hover: hot-tracking for buttons, menus, scrollbars.</summary>
    public void HoverAt(int x, int y)
    {
        var deep = HitRoot.HitTestDeep(x, y);
        Control? hit = deep?.Control;
        if (!ReferenceEquals(hit, _hovered))
        {
            _hovered?.HandleHoverLocal(-1, -1);
            _hovered = hit;
        }

        if (deep.HasValue)
            hit?.HandleHoverLocal(deep.Value.LocalX, deep.Value.LocalY);
    }

    /// <summary>Button press visuals + scrollbar thumb capture + form drag start.</summary>
    public void MouseDownAt(int x, int y)
    {
        var deep = HitRoot.HitTestDeep(x, y);
        if (!deep.HasValue)
            return;
        if (deep.Value.Control is Button button && button.Enabled)
        {
            button.IsPressed = true;
            _pressed.Add(button);
        }

        if (deep.Value.Control is Form form && form.WindowState == FormWindowState.Normal
            && form.CaptionDragZone(deep.Value.LocalX, deep.Value.LocalY))
        {
            _dragForm = form;
            _dragOffX = x - form.X;
            _dragOffY = y - form.Y;
        }

        if (deep.Value.Control is PaintCanvas paint && paint.Enabled)
            _painting = paint;
    }

    /// <summary>Mouse moves: form/overlay drags and scrollbar thumbs.</summary>
    public void MouseMoveAt(int x, int y)
    {
        HoverAt(x, y);
        if (_dragForm is not null)
        {
            _dragForm.X = x - _dragOffX;
            _dragForm.Y = y - _dragOffY;
            return;
        }

        ScrollBar? drag = FindDraggingBar(CurrentRoot);
        if (drag is not null && drag.AbsolutePosition(CurrentRoot) is (int bax, int bay))
            drag.DragTo(drag.Vertical ? y - bay : x - bax);

        if (_painting is not null
            && _painting.AbsolutePosition(CurrentRoot) is (int pax, int pay))
            _painting.AddPoint(
                Math.Clamp(x - pax, 0, _painting.Width),
                Math.Clamp(y - pay, 0, _painting.Height));
    }

    /// <summary>Release presses, drags, and captures.</summary>
    public void MouseUpAt(int x, int y)
    {
        foreach (Button button in _pressed)
            button.IsPressed = false;
        _pressed.Clear();
        _dragForm = null;
        _painting?.EndStroke();
        _painting = null;
        EndAllDrags(CurrentRoot);
        _ = x;
        _ = y;
    }

    /// <summary>Escape closes open menus and drop-downs (cf. CancelMode).</summary>
    public void KeyEscape()
    {
        if (_modal is not null)
            DismissModal();
        CloseOverlays();
    }

    /// <summary>Route a typed char: focused box first, digits switch scenes otherwise.</summary>
    public void KeyChar(char c)
    {
        if (_modal is not null)
        {
            // Enter/Escape dismiss like a real OK dialog; nothing else leaks through.
            if (c is '\r' or '\n' or '\u001b')
                DismissModal();
            return;
        }

        TextBox? focused = FindFocused(CurrentRoot);
        if (focused is not null)
        {
            focused.HandleTextInput(c.ToString());
            return;
        }

        Current = c switch
        {
            '1' => AppScene.Smoke,
            '2' => AppScene.Calendar,
            '3' => AppScene.Paint,
            '4' => AppScene.Calculator,
            '5' => AppScene.Todo,
            '6' => AppScene.TicTacToe,
            _ => Current,
        };
    }

    public void KeyBackspace() => FindFocused(CurrentRoot)?.HandleBackspace();

    private Control HitRoot => (Control?)_modal?.Window ?? CurrentRoot;

    private Control CurrentRoot => Current switch
    {
        AppScene.Calendar => Calendar.Window,
        AppScene.Paint => _paintRoot,
        AppScene.Calculator => Calculator.Window,
        AppScene.Todo => Todo.Window,
        AppScene.TicTacToe => TicTacToe.Window,
        _ => _smokeForm,
    };

    private void SetFocus(TextBox? box)
    {
        foreach (TextBox other in AllTextBoxes(CurrentRoot))
            other.Focused = ReferenceEquals(other, box);
    }

    private static TextBox? FindTextBox(Control root, Control? hit)
    {
        for (Control? node = hit; node is not null; node = ParentOf(root, node))
            if (node is TextBox box)
                return box;
        return null;
    }

    private static Control? ParentOf(Control root, Control node)
    {
        if (ReferenceEquals(root, node))
            return null;
        foreach (Control child in root.Children)
        {
            if (ReferenceEquals(child, node))
                return root;
            Control? found = ParentOf(child, node);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static IEnumerable<TextBox> AllTextBoxes(Control root)
    {
        if (root is TextBox box)
            yield return box;
        foreach (Control child in root.Children)
            foreach (TextBox nested in AllTextBoxes(child))
                yield return nested;
    }

    private static TextBox? FindFocused(Control root) =>
        AllTextBoxes(root).FirstOrDefault(b => b.Focused);

    private void CloseOverlays()
    {
        foreach (Control root in AllRoots())
            CloseOverlaysIn(root);
    }

    private void CloseOverlaysExcept(Control keep)
    {
        foreach (Control root in AllRoots())
            CloseOverlaysIn(root, keep);
    }

    private IEnumerable<Control> AllRoots()
    {
        yield return _smokeForm;
        yield return Calendar.Window;
        yield return _paintRoot;
        yield return Calculator.Window;
        yield return Todo.Window;
        yield return TicTacToe.Window;
    }

    private static void CloseOverlaysIn(Control node, Control? keep = null)
    {
        if (!ReferenceEquals(node, keep))
        {
            if (node is ComboBox combo && combo.DroppedDown && !IsAncestorOf(combo, keep))
                combo.DroppedDown = false;
            if (node is MenuStrip menu && menu.IsOpen && !IsAncestorOf(menu, keep))
                menu.CloseAll();
        }

        foreach (Control child in node.Children)
            CloseOverlaysIn(child, keep);
    }

    private static bool IsAncestorOf(Control node, Control? candidate)
    {
        for (Control? n = candidate; n is not null; n = n.Parent)
        {
            if (ReferenceEquals(n, node))
                return true;
        }

        return false;
    }

    private static ScrollBar? FindDraggingBar(Control node)
    {
        if (node is ScrollBar bar && bar.IsDragging)
            return bar;
        foreach (Control child in node.Children)
        {
            ScrollBar? found = FindDraggingBar(child);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static void EndAllDrags(Control node)
    {
        if (node is ScrollBar bar)
            bar.EndDrag();
        foreach (Control child in node.Children)
            EndAllDrags(child);
    }
}
