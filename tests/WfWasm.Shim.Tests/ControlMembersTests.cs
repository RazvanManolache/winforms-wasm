using System.Reflection;
using WfWasm.Shim;

public sealed class ControlMembersTests
{
    private static void Raise(object target, string method, params object?[] args)
    {
        MethodInfo? m = null;
        for (Type? t = target.GetType(); t is not null; t = t.BaseType)
        {
            m = t.GetMethod(method, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic);
            if (m is not null)
                break;
        }

        Assert.NotNull(m);
        m.Invoke(target, args);
    }

    [Fact]
    public void Width_Set_Fires_Resize_Chain_Once()
    {
        var control = new Control("c");
        int resize = 0;
        int size = 0;
        int client = 0;
        control.Resize += (_, _) => resize++;
        control.SizeChanged += (_, _) => size++;
        control.ClientSizeChanged += (_, _) => client++;
        control.Width = 50;
        Assert.Equal((1, 1, 1), (resize, size, client));
        control.Width = 50;
        Assert.Equal((1, 1, 1), (resize, size, client));
    }

    [Fact]
    public void Location_Set_Fires_Move_Chain()
    {
        var control = new Control("c");
        int move = 0;
        int located = 0;
        control.Move += (_, _) => move++;
        control.LocationChanged += (_, _) => located++;
        control.X = 7;
        control.Y = 9;
        Assert.Equal(new DrawingPoint(7, 9), control.Location);
        Assert.Equal(2, move);
        Assert.Equal(2, located);
    }

    [Fact]
    public void Text_Set_Fires_TextChanged()
    {
        var control = new Control("c");
        int fired = 0;
        control.TextChanged += (_, _) => fired++;
        control.Text = "hi";
        control.Text = "hi";
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Enabled_Toggle_Fires_EnabledChanged()
    {
        var control = new Control("c");
        int fired = 0;
        control.EnabledChanged += (_, _) => fired++;
        control.Enabled = false;
        control.Enabled = false;
        Assert.Equal(1, fired);
    }

    [Fact]
    public void BackColor_ForeColor_Font_Roundtrip_With_Change_Events()
    {
        var control = new Control("c");
        int back = 0;
        int fore = 0;
        int font = 0;
        control.BackColorChanged += (_, _) => back++;
        control.ForeColorChanged += (_, _) => fore++;
        control.FontChanged += (_, _) => font++;
        control.BackColor = Color.FromArgb(10, 20, 30);
        control.ForeColor = Color.FromArgb(40, 50, 60);
        control.Font = new DrawingFont("Arial", 14f);
        Assert.Equal(Color.FromArgb(10, 20, 30).ToArgb(), control.BackColor.ToArgb());
        Assert.Equal(Color.FromArgb(40, 50, 60).ToArgb(), control.ForeColor.ToArgb());
        Assert.Equal("Arial", control.Font.Name);
        Assert.Equal((1, 1, 1), (back, fore, font));
        control.ResetBackColor();
        control.ResetForeColor();
        Assert.Equal((2, 2, 1), (back, fore, font));
    }

    [Fact]
    public void KeyDown_Reaches_Handler_Through_OnKeyDown()
    {
        var control = new Control("c");
        Keys? seen = null;
        control.KeyDown += (_, e) => seen = e.KeyCode;
        Raise(control, "OnKeyDown", new KeyEventArgs(Keys.Control | Keys.S));
        Assert.Equal(Keys.S, seen);
    }

    [Fact]
    public void MouseDown_Reaches_Handler_With_Button_And_Position()
    {
        var control = new Control("c");
        MouseButtons button = MouseButtons.None;
        int x = -1;
        control.MouseDown += (_, e) =>
        {
            button = e.Button;
            x = e.X;
        };
        Raise(control, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 3, 4, 0));
        Assert.Equal(MouseButtons.Left, button);
        Assert.Equal(3, x);
    }

    [Fact]
    public void Show_Hide_Toggle_Visible_With_Event()
    {
        var control = new Control("c");
        int fired = 0;
        control.VisibleChanged += (_, _) => fired++;
        control.Hide();
        Assert.False(control.Visible);
        control.Show();
        Assert.True(control.Visible);
        Assert.Equal(2, fired);
    }

    [Fact]
    public void Focus_Sets_State_And_Raises_Enter_GotFocus()
    {
        var control = new Control("c");
        int enter = 0;
        int got = 0;
        control.Enter += (_, _) => enter++;
        control.GotFocus += (_, _) => got++;
        Assert.True(control.Focus());
        Assert.True(control.Focused);
        Assert.Equal((1, 1), (enter, got));
    }

    [Fact]
    public void Invoke_Runs_Inline_And_BeginInvoke_Completes()
    {
        var control = new Control("c");
        Assert.Equal(42, control.Invoke(new Func<int>(() => 42)));
        IAsyncResult async = control.BeginInvoke(new Func<int>(() => 7));
        Assert.True(async.IsCompleted);
        Assert.Equal(7, control.EndInvoke(async));
    }

    [Fact]
    public void Point_Mapping_Roundtrips_Through_Parents()
    {
        var root = new Control("root") { X = 10, Y = 20 };
        var child = new Control("child") { X = 5, Y = 5 };
        root.AddChild(child);
        Assert.Equal(new DrawingPoint(0, 0), child.PointToClient(new DrawingPoint(15, 25)));
        Assert.Equal(new DrawingPoint(15, 25), child.PointToScreen(new DrawingPoint(0, 0)));
    }

    [Fact]
    public void FindForm_Walks_To_Owning_Form()
    {
        var form = new Form("f");
        var child = new Control("c");
        form.AddChild(child);
        Assert.Same(form, child.FindForm());
        Assert.Same(form, form.FindForm());
        Assert.Null(new Control("orphan").FindForm());
    }

    [Fact]
    public void SuspendLayout_Coalesces_Layout_Event()
    {
        var control = new Control("c");
        int fired = 0;
        control.Layout += (_, _) => fired++;
        control.SuspendLayout();
        control.SuspendLayout();
        control.ResumeLayout(false);
        Assert.Equal(0, fired);
        control.ResumeLayout(true);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SetBounds_Sets_All_Components()
    {
        var control = new Control("c");
        control.SetBounds(1, 2, 30, 40, BoundsSpecified.All);
        Assert.Equal(new DrawingRectangle(1, 2, 30, 40), control.Bounds);
        control.SetBounds(5, 6, 70, 80, BoundsSpecified.Location);
        Assert.Equal(new DrawingRectangle(5, 6, 30, 40), control.Bounds);
    }

    [Fact]
    public void Dispose_Marks_Disposed()
    {
        var control = new Control("c");
        Assert.False(control.IsDisposed);
        control.Dispose();
        Assert.True(control.IsDisposed);
    }

    [Fact]
    public void ToolStrip_Collection_Wires_Owner_And_Keys()
    {
        var strip = new ToolStrip("t");
        var first = new ToolStripButton("a") { Name = "alpha" };
        var second = new ToolStripButton("b") { Name = "beta" };
        strip.Items.Add(first);
        strip.Items.Add(second);
        Assert.Same(strip, first.Parent);
        Assert.Same(strip, first.Owner);
        Assert.Equal(1, strip.Items.IndexOfKey("beta"));
        Assert.Same(second, strip.Items["beta"]);
        Assert.Same(first, strip.Items.Find("alpha", false)[0]);
        strip.Items.Remove(first);
        Assert.Null(first.Parent);
        Assert.Single(strip.Items);
    }

    [Fact]
    public void ToolStripMenuItem_Click_Toggles_Check_And_Blocks_Separator()
    {
        var item = new ToolStripMenuItem("x") { CheckOnClick = true };
        int fired = 0;
        item.Click += (_, _) => fired++;
        item.PerformClick();
        Assert.True(item.Checked);
        Assert.Equal(1, fired);
        var sep = new ToolStripMenuItem("-");
        sep.PerformClick();
        Assert.False(sep.Checked);
    }

    [Fact]
    public void ComboBox_Items_Are_Objects_With_Sorted_Insert()
    {
        var combo = new ComboBox("c");
        combo.Items.Add("banana");
        combo.Items.Add("apple");
        combo.Sorted = true;
        Assert.Equal("apple", combo.GetItemText(combo.Items[0]));
        combo.SelectedIndex = 1;
        Assert.Equal("banana", combo.GetItemText(combo.SelectedItem));
        combo.SelectedItem = "apple";
        Assert.Equal(0, combo.SelectedIndex);
    }

    [Fact]
    public void ListBox_SelectedItem_Roundtrips_As_Object()
    {
        var list = new ListBox("l");
        list.Items.AddRange(["a", "b"]);
        list.SelectedIndex = 1;
        Assert.Equal("b", list.SelectedItem);
        list.SelectedItem = "a";
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void TextBoxBase_SelectAll_Covers_Text()
    {
        var box = new TextBox("t") { Text = "hello" };
        box.SelectAll();
        Assert.Equal((0, 5), (box.SelectionStart, box.SelectionLength));
        Assert.Equal("hello", box.SelectedText);
        box.Select(1, 3);
        Assert.Equal("ell", box.SelectedText);
        box.AppendText("!");
        Assert.Equal("hello!", box.Text);
    }

    [Fact]
    public void Form_ShowDialog_Returns_Result_And_Close_Disposes()
    {
        var form = new Form("f") { DialogResult = DialogResult.OK };
        Assert.Equal(DialogResult.OK, form.ShowDialog());
        Assert.True(form.Modal);
        bool closing = false;
        form.FormClosing += (_, _) => closing = true;
        form.Close();
        Assert.True(closing);
        Assert.True(form.IsDisposed);
    }

    [Fact]
    public void FormClosing_Cancel_Prevents_Close()
    {
        var form = new Form("f");
        form.FormClosing += (_, e) => e.Cancel = true;
        form.Close();
        Assert.False(form.IsDisposed);
        Assert.True(form.Visible);
    }

    [Fact]
    public void Form_OwnedForms_Track_Owner()
    {
        var owner = new Form("o");
        var owned = new Form("d");
        owner.AddOwnedForm(owned);
        Assert.Same(owner, owned.Owner);
        Assert.Contains(owned, owner.OwnedForms);
        owner.RemoveOwnedForm(owned);
        Assert.Null(owned.Owner);
    }

    private volatile bool _wasShown;

    [Fact]
    public void Application_Run_Registers_Shows_And_Pumps_Until_Close()
    {
        var form = new Form("main");
        form.Visible = false;
        _wasShown = false;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            SpinWait.SpinUntil(() => form.Visible, TimeSpan.FromSeconds(5));
            _wasShown = form.Visible;
            form.Close();
        });
        Application.Run(form);
        Assert.True(_wasShown);
        Assert.Contains(form, Application.OpenForms);
        Assert.Same(form, Form.ActiveForm);
    }

    private volatile int _pumpWorkThread;
    private volatile bool _pumpRan;

    [Fact]
    public void Run_Pumps_Posted_Work_On_Run_Thread()
    {
        var form = new Form("pump");
        var ctx = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ctx);
        try
        {
            int runThread = Environment.CurrentManagedThreadId;
            _pumpWorkThread = -1;
            _pumpRan = false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SpinWait.SpinUntil(() => form.Visible, TimeSpan.FromSeconds(5));
                ctx.Post(_ =>
                {
                    _pumpWorkThread = Environment.CurrentManagedThreadId;
                    _pumpRan = true;
                    form.Close();
                }, null);
            });
            Application.Run(form);
            Assert.True(_pumpRan);
            Assert.Equal(runThread, _pumpWorkThread);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    private volatile int _marshalWorkThread;

    [Fact]
    public void BeginInvoke_OffThread_Marshals_To_Creating_Thread()
    {
        var control = new Control("m");
        var form = new Form("host");
        int createThread = Environment.CurrentManagedThreadId;
        _marshalWorkThread = -1;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            SpinWait.SpinUntil(() => form.Visible, TimeSpan.FromSeconds(5));
            IAsyncResult async = control.BeginInvoke(new Action(() =>
            {
                _marshalWorkThread = Environment.CurrentManagedThreadId;
                form.Close();
            }));
            control.EndInvoke(async);
        });
        Application.Run(form);
        Assert.Equal(createThread, _marshalWorkThread);
    }

    [Fact]
    public void Run_Returns_When_MainForm_Closes()
    {
        var form = new Form("closer");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            SpinWait.SpinUntil(() => form.Visible, TimeSpan.FromSeconds(5));
            form.Close();
        });
        Application.Run(form);
        Assert.True(form.IsDisposed);
    }

    [Fact]
    public void Timer_Tick_Fires_While_Enabled()
    {
        using var ticked = new System.Threading.ManualResetEventSlim(false);
        var timer = new WfWasm.Shim.Timer { Interval = 50 };
        timer.Tick += (_, _) => ticked.Set();
        timer.Start();
        Assert.True(timer.Enabled);
        Assert.True(ticked.Wait(5000));
        timer.Stop();
        Assert.False(timer.Enabled);
        timer.Dispose();
    }

    [Fact]
    public void ToolTip_Stores_Tips_Per_Control()
    {
        var tip = new ToolTip();
        var control = new Control("c");
        tip.SetToolTip(control, "hello");
        Assert.Equal("hello", tip.GetToolTip(control));
        Assert.Equal("", tip.GetToolTip(new Control("other")));
        tip.RemoveAll();
        Assert.Equal("", tip.GetToolTip(control));
        tip.Dispose();
    }

    [Fact]
    public void ProgressBar_Value_Clamps_And_Steps()
    {
        var bar = new ProgressBar("p") { Minimum = 10, Maximum = 110 };
        bar.Value = 500;
        Assert.Equal(110, bar.Value);
        bar.Value = -500;
        Assert.Equal(10, bar.Value);
        bar.Value = 20;
        bar.PerformStep();
        Assert.Equal(30, bar.Value);
        bar.Increment(-5);
        Assert.Equal(25, bar.Value);
    }

    [Fact]
    public void Keys_Decompose_Into_Code_And_Modifiers()
    {
        var args = new KeyEventArgs(Keys.Control | Keys.Alt | Keys.Delete);
        Assert.Equal(Keys.Delete, args.KeyCode);
        Assert.True(args.Control);
        Assert.True(args.Alt);
        Assert.False(args.Shift);
        Assert.False(args.Handled);
    }

    [Fact]
    public void Bare_Shift_Key_Reports_Shift_And_ShiftKey_Code()
    {
        var args = new KeyEventArgs(Keys.ShiftKey | Keys.Shift);
        Assert.Equal(Keys.ShiftKey, args.KeyCode);
        Assert.True(args.Shift);
        Assert.False(args.Control);
    }

    [Fact]
    public void Cursor_Equality_Is_Handle_Based()
    {
        Assert.Equal(Cursors.Default, Cursors.Arrow);
        Assert.NotEqual(Cursors.Arrow, Cursors.Hand);
        Assert.Equal(new DrawingPoint(0, 0), Cursor.Position);
    }

    [Fact]
    public void DataObject_Roundtrips_Formats()
    {
        IDataObject data = new DataObject("text", "hi");
        Assert.Equal("hi", data.GetData("text"));
        Assert.True(data.GetDataPresent("text"));
        Assert.False(data.GetDataPresent("nope"));
        Assert.Contains("text", data.GetFormats());
    }

    [Fact]
    public void ScrollBars_Have_Orientation()
    {
        Assert.False(new HScrollBar().Vertical);
        Assert.True(new VScrollBar().Vertical);
    }

    [Fact]
    public void TrackBar_Value_Clamps_And_Fires()
    {
        var track = new TrackBar();
        int value = 0;
        int scroll = 0;
        track.ValueChanged += (_, _) => value++;
        track.Scroll += (_, _) => scroll++;
        track.Value = 50;
        Assert.Equal(10, track.Value);
        track.Value = -5;
        Assert.Equal(0, track.Value);
        Assert.Equal((2, 2), (value, scroll));
        track.Value = 0;
        Assert.Equal((2, 2), (value, scroll));
    }

    [Fact]
    public void TrackBar_SetRange_Clamps_Value()
    {
        var track = new TrackBar();
        track.SetRange(20, 30);
        Assert.Equal(20, track.Value);
        Assert.Equal(20, track.Minimum);
        Assert.Equal(30, track.Maximum);
    }

    [Fact]
    public void TrackBar_Press_Drags_Value()
    {
        var track = new TrackBar { Width = 112 };
        track.PressTo(12, 0);
        Assert.Equal(0, track.Value);
        track.DragTo(112 - 12, 0);
        Assert.Equal(track.Maximum, track.Value);
        track.EndDrag();
        track.DragTo(12, 0);
        Assert.Equal(track.Maximum, track.Value);
    }

    [Fact]
    public void ContextMenuStrip_Show_Opens_With_Base_Shape()
    {
        var menu = new ContextMenuStrip();
        var button = new Button();
        int opened = 0;
        menu.Opened += (_, _) => opened++;
        menu.Show(button, new DrawingPoint(3, 4));
        Assert.Equal(1, opened);
        Assert.Null(menu.SourceControl);
        menu.SourceControl = button;
        Assert.Same(button, menu.SourceControl);
    }

    [Fact]
    public void ToolStripDropDown_Close_Raises_Reason()
    {
        var menu = new ContextMenuStrip();
        ToolStripDropDownCloseReason? seen = null;
        menu.Closed += (_, e) => seen = e.CloseReason;
        menu.Close(ToolStripDropDownCloseReason.Keyboard);
        Assert.Equal(ToolStripDropDownCloseReason.Keyboard, seen);
    }

    [Fact]
    public void ToolStripDropDown_Opening_Cancel_Blocks_Opened()
    {
        var menu = new ContextMenuStrip();
        int opened = 0;
        menu.Opened += (_, _) => opened++;
        menu.Opening += (_, e) => e.Cancel = true;
        menu.Show();
        Assert.Equal(0, opened);
    }

    [Fact]
    public void FolderBrowserDialog_Show_Returns_OK_With_Path()
    {
        var dialog = new FolderBrowserDialog { SelectedPath = @"C:\Work" };
        Assert.Equal(@"C:\Work", dialog.InitialDirectory);
        Assert.Equal(DialogResult.OK, dialog.ShowDialog());
        dialog.Reset();
        Assert.Equal(string.Empty, dialog.SelectedPath);
    }

    [Fact]
    public void LinkLabel_Click_Marks_Visited_And_Raises()
    {
        var label = new LinkLabel { Text = "go home" };
        label.Links.Add(3, 4);
        LinkLabelLink? seen = null;
        label.LinkClicked += (_, e) => seen = e.Link;
        label.SimulateLinkClick(4);
        Assert.NotNull(seen);
        Assert.True(label.Links[0].Visited);
        Assert.True(label.LinkVisited);
    }

    [Fact]
    public void RichTextBox_Defaults_And_Find()
    {
        var rich = new RichTextBox();
        Assert.True(rich.Multiline);
        rich.Text = "hello world";
        Assert.Equal(6, rich.Find("world"));
        int clicks = 0;
        rich.LinkClicked += (_, e) =>
        {
            clicks++;
            Assert.Equal("http://x", e.LinkText);
        };
        rich.SimulateLinkClick("http://x");
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Strip_Family_Resolves_With_Defaults()
    {
        var item = new ToolStripLabel("hi");
        Assert.Equal(ToolStripItemAlignment.Left, item.Alignment);
        Assert.Equal(ToolStripItemDisplayStyle.ImageAndText, item.DisplayStyle);
        Assert.True(item.Available);
        Assert.Equal(ToolStripItemOverflow.AsNeeded, item.Overflow);
        Assert.Equal(new DrawingSize(6, 6),
            new ToolStripSeparator().GetPreferredSize(new DrawingSize()));
        var menu = new ToolStripDropDownMenu();
        Assert.Equal(ToolStripLayoutStyle.VerticalStackWithOverflow, menu.LayoutStyle);
        Assert.True(menu.ShowImageMargin);
        Assert.False(ProfessionalColors.MenuStripGradientBegin.IsEmpty);
    }

    [Fact]
    public void Controls_And_Items_Are_Components()
    {
        var control = new Control("c");
        var item = new ToolStripButton("b");
        Assert.IsAssignableFrom<System.ComponentModel.IComponent>(control);
        Assert.IsAssignableFrom<System.ComponentModel.IComponent>(item);
        using var container = new System.ComponentModel.Container();
        container.Add(control);
        container.Add(item);
        Assert.Equal(2, container.Components.Count);
    }

    [Fact]
    public void RightToLeft_Defaults_To_Inherit()
    {
        Assert.Equal(RightToLeft.Inherit, new Control("c").RightToLeft);
        Assert.Equal(RightToLeft.Inherit, new ToolStripButton("b").RightToLeft);
    }

    [Fact]
    public void ArrangedElementCollection_Roundtrips()
    {
        var items = new ArrangedElementCollection();
        var control = new Control("c");
        items.Add(control);
        Assert.Single(items);
        Assert.Same(control, items[0]);
        Assert.True(items.Contains(control));
        Assert.Equal(0, items.IndexOf(control));
    }

    [Fact]
    public void ListView_Collections_Are_Nested()
    {
        Assert.NotNull(typeof(ListView).GetNestedType("ListViewItemCollection"));
        Assert.NotNull(typeof(ListView).GetNestedType("ColumnHeaderCollection"));
        Assert.NotNull(typeof(ListViewItem).GetNestedType("ListViewSubItemCollection"));
        Assert.NotNull(typeof(ListViewItem).GetNestedType("ListViewSubItem"));
        var view = new ListView();
        view.Columns.Add("Name", 120);
        view.Items.Add("row");
        Assert.Equal(120, view.Columns[0].Width);
        Assert.Equal("row", view.Items[0].Text);
    }

    [Fact]
    public void TableLayoutControlCollection_Is_Top_Level()
    {
        Assert.Null(typeof(TableLayoutPanel).GetNestedType("TableLayoutControlCollection"));
        var grid = new TableLayoutPanel();
        var label = new Label();
        grid.Controls.Add(label, 1, 2);
        Assert.IsType<TableLayoutControlCollection>(grid.Controls);
        Assert.Same(grid, grid.Controls.Container);
        Assert.Equal(new TableLayoutPanelCellPosition(1, 2), grid.GetCellPosition(label));
    }

    [Fact]
    public void New_Enums_Match_Framework_Values()
    {
        Assert.Equal(0, (int)MessageBoxButtons.OK);
        Assert.Equal(6, (int)MessageBoxButtons.CancelTryContinue);
        Assert.Equal(0, (int)MessageBoxIcon.None);
        Assert.Equal(16, (int)MessageBoxIcon.Hand);
        Assert.Equal(16, (int)MessageBoxIcon.Error);
        Assert.Equal(64, (int)MessageBoxIcon.Information);
        Assert.Equal(0, (int)ColumnHeaderAutoResizeStyle.None);
        Assert.Equal(1, (int)ColumnHeaderAutoResizeStyle.HeaderSize);
        Assert.Equal(2, (int)ColumnHeaderAutoResizeStyle.ColumnContent);
        Assert.Equal(1, (int)CheckBoxState.UncheckedNormal);
        Assert.Equal(12, (int)CheckBoxState.MixedDisabled);
        Assert.Equal(1, (int)PushButtonState.Normal);
        Assert.Equal(5, (int)PushButtonState.Default);
        Assert.Equal(0, (int)LeftRightAlignment.Left);
        Assert.Equal(1, (int)LeftRightAlignment.Right);
        Assert.Equal(0, (int)ToolStripRenderMode.Custom);
        Assert.Equal(3, (int)ToolStripRenderMode.ManagerRenderMode);
    }

    [Fact]
    public void NumericUpDown_Value_Clamps_And_Spins()
    {
        var spin = new NumericUpDown();
        Assert.Equal(0m, spin.Value);
        Assert.True(spin.InterceptArrowKeys);
        Assert.Equal(LeftRightAlignment.Right, spin.UpDownAlign);
        int changed = 0;
        spin.ValueChanged += (_, _) => changed++;
        spin.UpButton();
        Assert.Equal(1m, spin.Value);
        spin.Value = 250m;
        Assert.Equal(100m, spin.Value);
        Assert.Equal(2, changed);
        spin.DownButton();
        Assert.Equal(99m, spin.Value);
        spin.DecimalPlaces = 2;
        spin.Value = 3.5m;
        Assert.Equal(
            3.5m.ToString("F2", System.Globalization.CultureInfo.CurrentCulture),
            spin.Text);
        spin.Text = 7.25m.ToString(System.Globalization.CultureInfo.CurrentCulture);
        spin.ParseEditText();
        Assert.Equal(7.25m, spin.Value);
    }

    [Fact]
    public void DragEventArgs_Carries_Data_With_Real_Shape()
    {
        IDataObject data = new DataObject("text", "hi");
        var args = new DragEventArgs(data, 0, 3, 4, DragDropEffects.Copy, DragDropEffects.Copy);
        Assert.Same(data, args.Data);
        Assert.Equal(DragDropEffects.Copy, args.AllowedEffect);
        Assert.Equal((3, 4), (args.X, args.Y));
    }

    [Fact]
    public void ListView_AutoResizeColumn_By_Style()
    {
        var view = new ListView();
        view.Columns.Add("Name", 10);
        view.Items.Add("a much longer row text");
        view.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.ColumnContent);
        Assert.True(view.Columns[0].Width > 10);
        view.Columns[0].Width = 10;
        view.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
        Assert.True(view.Columns[0].Width >= 10);
    }

    [Fact]
    public void ControlCollection_SetChildIndex_Reorders()
    {
        var panel = new Panel();
        var a = new Label("a");
        var b = new Label("b");
        panel.Controls.Add(a);
        panel.Controls.Add(b);
        panel.Controls.SetChildIndex(a, 1);
        Assert.Same(b, panel.Controls[0]);
        Assert.Same(a, panel.Controls[1]);
        Assert.Throws<ArgumentException>(() => panel.Controls.SetChildIndex(new Label("x"), 0));
    }

    [Fact]
    public void RadioButtonState_Matches_Framework()
    {
        Assert.Equal(1, (int)RadioButtonState.UncheckedNormal);
        Assert.Equal(8, (int)RadioButtonState.CheckedDisabled);
        var combo = new ComboBox();
        Assert.Equal(15, combo.ItemHeight);
        combo.ItemHeight = 20;
        Assert.Equal(20, combo.ItemHeight);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.Automatic, true);
    }

    [Fact]
    public void ToolStripPanel_Join_Collects_Rows()
    {
        var panel = new ToolStripPanel();
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.Equal(ToolStripRenderMode.ManagerRenderMode, panel.RenderMode);
        var strip = new ToolStrip();
        panel.Join(strip);
        Assert.Single(panel.Rows);
        Assert.Contains(strip, panel.Rows[0].Controls);
        Assert.Same(panel, strip.Parent);
        var strip2 = new ToolStrip();
        panel.Join(strip2, 2);
        Assert.Equal(3, panel.Rows.Length);
        Assert.Null(panel.PointToRow(new DrawingPoint(5, 5)));
    }

    [Fact]
    public void TableLayoutPanel_Tracks_Cells()
    {
        var grid = new TableLayoutPanel();
        Assert.Equal(2, grid.ColumnCount);
        Assert.Equal(2, grid.RowCount);
        Assert.Equal(TableLayoutPanelGrowStyle.AddRows, grid.GrowStyle);
        var label = new Label();
        grid.Controls.Add(label);
        grid.SetColumn(label, 1);
        grid.SetRow(label, 2);
        Assert.Equal(1, grid.GetColumn(label));
        Assert.Equal(2, grid.GetRow(label));
        grid.SetColumnSpan(label, 2);
        Assert.Equal(2, grid.GetColumnSpan(label));
        Assert.Equal(1, grid.GetRowSpan(label));
        Assert.Equal(new TableLayoutPanelCellPosition(1, 2), grid.GetCellPosition(label));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        Assert.Equal(50, ((ColumnStyle)grid.ColumnStyles[0]).Width);
        Assert.Equal(SizeType.Absolute, ((RowStyle)grid.RowStyles[0]).SizeType);
        Assert.Equal(new TableLayoutPanelCellPosition(-1, -1),
            grid.GetCellPosition(new Label()));
        Assert.Same(label, grid.GetControlFromPosition(1, 2));
    }

    [Fact]
    public void ScrollProperties_Rewire_Keeps_Values()
    {
        ScrollProperties h = new HScrollProperties();
        ScrollProperties v = new VScrollProperties();
        Assert.True(h.Enabled);
        Assert.False(h.Visible);
        v.Visible = true;
        Assert.True(v.Visible);
        Assert.Equal(100, ((HScrollProperties)h).Maximum);
    }

    [Fact]
    public void DataFormats_And_Filter_Resolve()
    {
        Assert.Equal("FileDrop", DataFormats.FileDrop);
        var format = DataFormats.GetFormat(DataFormats.FileDrop);
        Assert.Equal(DataFormats.FileDrop, format.Name);
        IMessageFilter? filter = null;
        Assert.Null(filter);
    }

    [Fact]
    public void VisualStyleElement_Parts_Resolve()
    {
        Assert.Equal("WINDOW", VisualStyleElement.Window.Caption.Active.ClassName);
        Assert.Equal(1, VisualStyleElement.Window.Caption.Active.Part);
        Assert.Equal("BUTTON", VisualStyleElement.Button.PushButton.Normal.ClassName);
        var made = VisualStyleElement.CreateElement("X", 2, 3);
        Assert.Equal((2, 3), (made.Part, made.State));
    }

    [Fact]
    public void Enum_Values_Match_Framework()
    {
        Assert.Equal(2, (int)ToolStripItemOverflow.AsNeeded);
        Assert.Equal(0, (int)ToolStripItemOverflow.Never);
        Assert.Equal(3, (int)ToolStripDropDownCloseReason.Keyboard);
        Assert.Equal(4, (int)ToolStripDropDownCloseReason.CloseCalled);
        Assert.Equal(0, (int)RichTextBoxScrollBars.None);
        Assert.Equal(1, (int)TableLayoutPanelGrowStyle.AddRows);
        Assert.Equal(6, (int)TableLayoutPanelCellBorderStyle.OutsetPartial);
        Assert.Equal(4, (int)PictureBoxSizeMode.Zoom);
        Assert.Equal("Csv", DataFormats.CommaSeparatedValue);
        Assert.Equal("DataInterchangeFormat", DataFormats.Dif);
        Assert.Equal("RiffAudio", DataFormats.Riff);
        Assert.Equal("MetaFilePict", DataFormats.MetafilePict);
        Assert.Equal("OEMText", DataFormats.OemText);
    }

    [Fact]
    public void ListViewItemCollection_Add_Returns_Item()
    {
        var view = new ListView();
        var plain = view.Items.Add("row");
        Assert.Equal("row", plain.Text);
        var keyed = view.Items.Add("k2", "second", 7);
        Assert.Equal("k2", keyed.Name);
        Assert.Equal(7, keyed.ImageIndex);
        var imageKeyed = view.Items.Add("k3", "third", "img");
        Assert.Equal("img", imageKeyed.ImageKey);
        Assert.Equal(3, view.Items.Count);
    }

    [Fact]
    public void ColumnHeaderCollection_Add_Returns_Header_With_Keys()
    {
        var view = new ListView();
        var header = view.Columns.Add("key", "Name", 120, HorizontalAlignment.Center, 3);
        Assert.Equal("Name", header.Text);
        Assert.Equal("key", header.Name);
        Assert.Equal(3, header.ImageIndex);
        Assert.True(view.Columns.ContainsKey("key"));
        Assert.Equal(0, view.Columns.IndexOfKey("key"));
        view.Columns.RemoveByKey("key");
        Assert.Empty(view.Columns);
    }

    [Fact]
    public void SubItemCollection_Color_Add_And_Keys()
    {
        var item = new ListViewItem("a");
        var sub = item.SubItems.Add("b", Color.Red, Color.Blue, null);
        Assert.Equal("b", sub.Text);
        Assert.True(item.SubItems.ContainsKey(string.Empty));
        sub.Name = "second";
        Assert.Equal(1, item.SubItems.IndexOfKey("second"));
        item.SubItems.RemoveByKey("second");
        Assert.Single(item.SubItems);
    }

    [Fact]
    public void TableLayout_ControlCollection_Tracks_Cells()
    {
        var grid = new TableLayoutPanel();
        var label = new Label();
        grid.Controls.Add(label, 2, 1);
        Assert.IsType<TableLayoutControlCollection>(grid.Controls);
        Assert.Same(grid, grid.Controls.Container);
        Assert.Equal(new TableLayoutPanelCellPosition(2, 1), grid.GetCellPosition(label));
    }

    [Fact]
    public void DropDown_Shows_All_Shapes_And_Builds_Layouts()
    {
        var menu = new ContextMenuStrip();
        int opened = 0;
        menu.Opened += (_, _) => opened++;
        menu.Show(new DrawingPoint(1, 2));
        menu.Show(3, 4);
        menu.Show(new DrawingPoint(1, 2), ToolStripDropDownDirection.BelowRight);
        menu.Show(new Button(), new DrawingPoint(1, 2));
        menu.Show(new Button(), 5, 6);
        menu.Show(new Button(), new DrawingPoint(1, 2), ToolStripDropDownDirection.AboveLeft);
        Assert.Equal(6, opened);
        var probe = new LayoutProbeDropDown();
        Assert.IsType<FlowLayoutSettings>(probe.Probe(ToolStripLayoutStyle.Flow));
    }

    [Fact]
    public void Renderer_DrawArrow_Dispatches()
    {
        var renderer = new ArrowProbeRenderer();
        renderer.DrawArrow(new ToolStripArrowRenderEventArgs(
            null, new DrawingRectangle(0, 0, 8, 8), ArrowDirection.Down));
        Assert.Equal(1, renderer.Arrows);
    }

    [Fact]
    public void ListBox_DrawMode_And_IntegralHeight_Default()
    {
        var box = new ListBox();
        Assert.Equal(DrawMode.Normal, box.DrawMode);
        Assert.True(box.IntegralHeight);
    }

    [Fact]
    public void FlowBreak_Roundtrips_Object()
    {
        var settings = new FlowLayoutSettings(null);
        object target = new Button();
        Assert.True(settings.WrapContents);
        settings.SetFlowBreak(target, true);
        Assert.True(settings.GetFlowBreak(target));
        settings.SetFlowBreak(target, false);
        Assert.False(settings.GetFlowBreak(target));
    }

    private sealed class LayoutProbeDropDown : ToolStripDropDown
    {
        public LayoutSettings Probe(ToolStripLayoutStyle style) => CreateLayoutSettings(style);
    }

    private sealed class ArrowProbeRenderer : ToolStripProfessionalRenderer
    {
        public int Arrows;

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) => Arrows++;
    }
}
