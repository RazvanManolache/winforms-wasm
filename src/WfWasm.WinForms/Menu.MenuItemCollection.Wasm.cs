// Legacy-menu overlay: List-backed MenuItemCollection (upstream stub
// throws from every member). See Menu.Wasm.cs for the rationale.
using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

public abstract partial class Menu
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    [ListBindable(false)]
    public class MenuItemCollection : IList
    {
        private readonly List<MenuItem> _items = new();
        private readonly Menu _owner;

        public MenuItemCollection(Menu owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public virtual MenuItem this[int index] => _items[index];

        object? IList.this[int index]
        {
            get => this[index];
            set
            {
                if (value is not MenuItem item)
                    throw new ArgumentException("Value must be a MenuItem.", nameof(value));
                _items[index].ParentInternal = null;
                _items[index] = item;
                item.ParentInternal = _owner;
            }
        }

        public virtual MenuItem? this[string key] => Find(key, searchAllChildren: false).FirstOrDefault();

        public int Count => _items.Count;

        object ICollection.SyncRoot => this;

        bool ICollection.IsSynchronized => false;

        bool IList.IsFixedSize => false;

        public bool IsReadOnly => false;

        public virtual MenuItem Add(string caption) => AddAndReturn(new MenuItem(caption));

        public virtual MenuItem Add(string caption, EventHandler onClick) =>
            AddAndReturn(new MenuItem(caption, onClick));

        public virtual MenuItem Add(string caption, MenuItem[] items) =>
            AddAndReturn(new MenuItem(caption, items));

        public virtual int Add(MenuItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _items.Add(item);
            item.ParentInternal = _owner;
            return _items.Count - 1;
        }

        public virtual int Add(int index, MenuItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            index = Math.Clamp(index, 0, _items.Count);
            _items.Insert(index, item);
            item.ParentInternal = _owner;
            return index;
        }

        public virtual void AddRange(MenuItem[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (MenuItem item in items)
                Add(item);
        }

        int IList.Add(object? value)
        {
            if (value is not MenuItem item)
                throw new ArgumentException("Value must be a MenuItem.", nameof(value));
            return Add(item);
        }

        public bool Contains(MenuItem value) => value is not null && _items.Contains(value);

        bool IList.Contains(object? value) => value is MenuItem item && Contains(item);

        public virtual bool ContainsKey(string key) => IndexOfKey(key) >= 0;

        public virtual void Clear()
        {
            foreach (MenuItem item in _items)
                item.ParentInternal = null;
            _items.Clear();
        }

        public void CopyTo(Array dest, int index) => ((ICollection)_items).CopyTo(dest, index);

        public MenuItem[] Find(string key, bool searchAllChildren)
        {
            List<MenuItem> matches = new();
            FindInto(_items, key, searchAllChildren, matches);
            return matches.ToArray();
        }

        private static void FindInto(List<MenuItem> items, string key, bool recursive, List<MenuItem> matches)
        {
            foreach (MenuItem item in items)
            {
                if (string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase))
                    matches.Add(item);
                if (recursive)
                    FindInto(item.ItemsInternal, key, recursive: true, matches);
            }
        }

        public int IndexOf(MenuItem value) => value is null ? -1 : _items.IndexOf(value);

        public virtual int IndexOfKey(string key)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].Name, key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        int IList.IndexOf(object? value) => value is MenuItem item ? IndexOf(item) : -1;

        void IList.Insert(int index, object? value)
        {
            if (value is not MenuItem item)
                throw new ArgumentException("Value must be a MenuItem.", nameof(value));
            Add(index, item);
        }

        public IEnumerator GetEnumerator() => _items.GetEnumerator();

        public virtual void RemoveAt(int index)
        {
            _items[index].ParentInternal = null;
            _items.RemoveAt(index);
        }

        public virtual void RemoveByKey(string key)
        {
            int index = IndexOfKey(key);
            if (index >= 0)
                RemoveAt(index);
        }

        public virtual void Remove(MenuItem item)
        {
            if (item is not null && _items.Remove(item))
                item.ParentInternal = null;
        }

        void IList.Remove(object? value)
        {
            if (value is MenuItem item)
                Remove(item);
        }

        private MenuItem AddAndReturn(MenuItem item)
        {
            Add(item);
            return item;
        }
    }
}
