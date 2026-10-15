// Legacy-menu overlay: replaces the runtimes/winforms binary-compat stub
// (Controls/Unsupported/ContextMenu/Menu.cs, Compile-Removed in the
// csproj), whose constructor unconditionally throws
// PlatformNotSupportedException. Framework-era apps and libraries (Telerik
// RadTitleBar builds a legacy system menu in its constructor) need a
// working in-memory model: construction, MenuItems, merge helpers. There
// are no native HMENUs in the browser: Handle is IntPtr.Zero, Show raises
// Popup without displaying (host popup bridge is future work).
// Signatures and attributes mirror the upstream stub exactly so the rest
// of the fork (Control.ContextMenu, Form.Menu) keeps compiling.
// Regression: PNSE Menu..ctor <- RadTitleBar..ctor <- BookShelf.MainForm
// create (every RadForm-based Telerik demo failed to construct).
using System.ComponentModel;

namespace System.Windows.Forms;

[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ListBindable(false)]
[ToolboxItemFilter("System.Windows.Forms")]
public abstract partial class Menu : Component
{
    public const int FindHandle = 0;
    public const int FindShortcut = 1;

    private readonly MenuItemCollection _items;
    private string _name = string.Empty;
    private object? _tag;

    protected Menu(MenuItem[]? items)
    {
        _items = new MenuItemCollection(this);
        if (items is not null)
            _items.AddRange(items);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IntPtr Handle => IntPtr.Zero;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual bool IsParent => MenuItems.Count != 0;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MenuItem? MdiListItem
    {
        get
        {
            foreach (MenuItem item in MenuItems)
            {
                if (item.MdiList)
                    return item;
            }

            return null;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    [MergableProperty(false)]
    public MenuItemCollection MenuItems => _items;

    [Localizable(false)]
    [Bindable(true)]
    [DefaultValue(null)]
    [TypeConverter(typeof(StringConverter))]
    public object? Tag
    {
        get => _tag;
        set => _tag = value;
    }

    protected internal void CloneMenu(Menu menuSrc)
    {
        MenuItems.Clear();
        foreach (MenuItem item in menuSrc.MenuItems)
            MenuItems.Add(item.CloneMenu());
    }

    protected virtual IntPtr CreateMenuHandle() => IntPtr.Zero;

    public MenuItem? FindMenuItem(int type, IntPtr value)
    {
        if (type == FindShortcut)
            return FindShortcutItem(MenuItems, (Shortcut)(int)value);
        return null;
    }

    private static MenuItem? FindShortcutItem(MenuItemCollection items, Shortcut shortcut)
    {
        foreach (MenuItem item in items)
        {
            if (item.Shortcut == shortcut)
                return item;
            MenuItem? nested = FindShortcutItem(item.MenuItems, shortcut);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    protected int FindMergePosition(int mergeOrder)
    {
        int position = 0;
        foreach (MenuItem item in MenuItems)
        {
            if (item.MergeOrder > mergeOrder)
                break;
            position++;
        }

        return position;
    }

    public ContextMenu? GetContextMenu()
    {
        Menu menu = this;
        while (menu is MenuItem item && item.Parent is not null)
            menu = item.Parent;
        return menu as ContextMenu;
    }

    public MainMenu? GetMainMenu()
    {
        Menu menu = this;
        while (menu is MenuItem item && item.Parent is not null)
            menu = item.Parent;
        return menu as MainMenu;
    }

    public virtual void MergeMenu(Menu menuSrc)
    {
        foreach (MenuItem src in menuSrc.MenuItems)
        {
            switch (src.MergeType)
            {
                case MenuMerge.Add:
                    MenuItems.Add(FindMergePosition(src.MergeOrder), src.CloneMenu());
                    break;
                case MenuMerge.Replace:
                case MenuMerge.MergeItems:
                case MenuMerge.Remove:
                    MergeMatchedItem(src);
                    break;
            }
        }
    }

    private void MergeMatchedItem(MenuItem src)
    {
        foreach (MenuItem item in MenuItems)
        {
            if (item.MergeOrder != src.MergeOrder)
                continue;
            if (src.MergeType == MenuMerge.Remove)
            {
                MenuItems.Remove(item);
                return;
            }

            if (src.MergeType == MenuMerge.Replace)
            {
                int index = MenuItems.IndexOf(item);
                MenuItems.RemoveAt(index);
                MenuItems.Add(index, src.CloneMenu());
                return;
            }

            item.MergeMenu(src);
            return;
        }

        if (src.MergeType != MenuMerge.Remove)
            MenuItems.Add(src.CloneMenu());
    }

    protected internal virtual bool ProcessCmdKey(ref Message msg, Keys keyData) => false;
}
