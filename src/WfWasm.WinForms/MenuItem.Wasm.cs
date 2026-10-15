// Legacy-menu overlay: working in-memory MenuItem (upstream stub throws
// from every member). See Menu.Wasm.cs for the rationale.
using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ToolboxItem(false)]
[DesignTimeVisible(false)]
[DefaultEvent(nameof(Click))]
[DefaultProperty(nameof(Text))]
public class MenuItem : Menu
{
    private static int s_nextMenuId;
    private readonly int _menuId = Interlocked.Increment(ref s_nextMenuId);

    private bool _checked;
    private bool _radioCheck;
    private bool _break;
    private bool _barBreak;
    private bool _defaultItem;
    private bool _ownerDraw;
    private bool _enabled = true;
    private bool _visible = true;
    private bool _showShortcut = true;
    private bool _mdiList;
    private MenuMerge _mergeType = MenuMerge.Add;
    private int _mergeOrder;
    private Shortcut _shortcut = Shortcut.None;
    private string _text = string.Empty;

    internal Menu? ParentInternal { get; set; }

    internal List<MenuItem> ItemsInternal
    {
        get
        {
            List<MenuItem> items = new();
            foreach (MenuItem item in MenuItems)
                items.Add(item);
            return items;
        }
    }

    public MenuItem()
        : this(MenuMerge.Add, 0, Shortcut.None, null, null, null, null, null)
    {
    }

    public MenuItem(string? text)
        : this(MenuMerge.Add, 0, Shortcut.None, text, null, null, null, null)
    {
    }

    public MenuItem(string? text, EventHandler? onClick)
        : this(MenuMerge.Add, 0, Shortcut.None, text, onClick, null, null, null)
    {
    }

    public MenuItem(string? text, EventHandler? onClick, Shortcut shortcut)
        : this(MenuMerge.Add, 0, shortcut, text, onClick, null, null, null)
    {
    }

    public MenuItem(string? text, MenuItem[]? items)
        : this(MenuMerge.Add, 0, Shortcut.None, text, null, null, null, items)
    {
    }

    public MenuItem(
        MenuMerge mergeType,
        int mergeOrder,
        Shortcut shortcut,
        string? text,
        EventHandler? onClick,
        EventHandler? onPopup,
        EventHandler? onSelect,
        MenuItem[]? items)
        : base(items)
    {
        MergeType = mergeType;
        MergeOrder = mergeOrder;
        Shortcut = shortcut;
        Text = text ?? string.Empty;
        if (onClick is not null)
            Click += onClick;
        if (onPopup is not null)
            Popup += onPopup;
        if (onSelect is not null)
            Select += onSelect;
    }

    [Browsable(false)]
    [DefaultValue(false)]
    public bool BarBreak
    {
        get => _barBreak;
        set => _barBreak = value;
    }

    [Browsable(false)]
    [DefaultValue(false)]
    public bool Break
    {
        get => _break;
        set => _break = value;
    }

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set => _checked = value;
    }

    [DefaultValue(false)]
    public bool DefaultItem
    {
        get => _defaultItem;
        set => _defaultItem = value;
    }

    [DefaultValue(false)]
    public bool OwnerDraw
    {
        get => _ownerDraw;
        set => _ownerDraw = value;
    }

    [Localizable(true)]
    [DefaultValue(true)]
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    [Browsable(false)]
    public int Index
    {
        get => ParentInternal is null ? -1 : ParentInternal.MenuItems.IndexOf(this);
        set
        {
            if (ParentInternal is null)
                return;
            MenuItemCollection siblings = ParentInternal.MenuItems;
            int from = siblings.IndexOf(this);
            if (from < 0)
                return;
            int to = Math.Clamp(value, 0, siblings.Count - 1);
            if (from == to)
                return;
            siblings.RemoveAt(from);
            siblings.Add(to, this);
        }
    }

    [Browsable(false)]
    public override bool IsParent => MenuItems.Count != 0;

    [DefaultValue(false)]
    public bool MdiList
    {
        get => _mdiList;
        set => _mdiList = value;
    }

    protected int MenuID => _menuId;

    [DefaultValue(MenuMerge.Add)]
    public MenuMerge MergeType
    {
        get => _mergeType;
        set => _mergeType = value;
    }

    [DefaultValue(0)]
    public int MergeOrder
    {
        get => _mergeOrder;
        set => _mergeOrder = value;
    }

    [Browsable(false)]
    public char Mnemonic
    {
        get
        {
            string text = Text;
            for (int i = 0; i + 1 < text.Length; i++)
            {
                if (text[i] != '&')
                    continue;
                if (text[i + 1] == '&')
                {
                    i++;
                    continue;
                }

                return char.ToUpper(text[i + 1], CultureInfo.InvariantCulture);
            }

            return '\0';
        }
    }

    [Browsable(false)]
    public Menu? Parent => ParentInternal;

    [DefaultValue(false)]
    public bool RadioCheck
    {
        get => _radioCheck;
        set => _radioCheck = value;
    }

    [Localizable(true)]
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    [Localizable(true)]
    [DefaultValue(Shortcut.None)]
    public Shortcut Shortcut
    {
        get => _shortcut;
        set => _shortcut = value;
    }

    [DefaultValue(true)]
    [Localizable(true)]
    public bool ShowShortcut
    {
        get => _showShortcut;
        set => _showShortcut = value;
    }

    [Localizable(true)]
    [DefaultValue(true)]
    public bool Visible
    {
        get => _visible;
        set => _visible = value;
    }

    public event EventHandler? Click;

    public event DrawItemEventHandler? DrawItem;

    public event MeasureItemEventHandler? MeasureItem;

    public event EventHandler? Popup;

    public event EventHandler? Select;

    public virtual MenuItem CloneMenu()
    {
        MenuItem clone = new();
        clone.CloneMenu(this);
        return clone;
    }

    protected void CloneMenu(MenuItem itemSrc)
    {
        BarBreak = itemSrc.BarBreak;
        Break = itemSrc.Break;
        Checked = itemSrc.Checked;
        DefaultItem = itemSrc.DefaultItem;
        Enabled = itemSrc.Enabled;
        MdiList = itemSrc.MdiList;
        MergeType = itemSrc.MergeType;
        MergeOrder = itemSrc.MergeOrder;
        Name = itemSrc.Name;
        OwnerDraw = itemSrc.OwnerDraw;
        RadioCheck = itemSrc.RadioCheck;
        Shortcut = itemSrc.Shortcut;
        ShowShortcut = itemSrc.ShowShortcut;
        Text = itemSrc.Text;
        Visible = itemSrc.Visible;
        Tag = itemSrc.Tag;
        MenuItems.Clear();
        foreach (MenuItem sub in itemSrc.MenuItems)
            MenuItems.Add(sub.CloneMenu());
    }

    public virtual MenuItem MergeMenu() => CloneMenu();

    public void MergeMenu(MenuItem itemSrc)
    {
        foreach (MenuItem sub in itemSrc.MenuItems)
            MenuItems.Add(sub.CloneMenu());
    }

    protected virtual void OnClick(EventArgs e) => Click?.Invoke(this, e);

    protected virtual void OnDrawItem(DrawItemEventArgs e) => DrawItem?.Invoke(this, e);

    protected virtual void OnInitMenuPopup(EventArgs e) => OnPopup(e);

    protected virtual void OnMeasureItem(MeasureItemEventArgs e) => MeasureItem?.Invoke(this, e);

    protected virtual void OnPopup(EventArgs e) => Popup?.Invoke(this, e);

    protected virtual void OnSelect(EventArgs e) => Select?.Invoke(this, e);

    public void PerformClick() => OnClick(EventArgs.Empty);

    public virtual void PerformSelect() => OnSelect(EventArgs.Empty);
}
