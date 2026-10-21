// Design-time attributes, animator, and category names: exact .NET member
// shapes; resource-backed images return null until the codec milestone.
using System.Collections;
using System.Runtime.CompilerServices;

namespace System.Drawing
{
    /// <summary>Toolbox image selector (cf. System.Drawing.ToolboxBitmapAttribute).</summary>
    public class ToolboxBitmapAttribute : Attribute
    {
        public static readonly ToolboxBitmapAttribute Default = new((string?)null);

        private readonly Type? _type;
        private readonly string? _name;

        public ToolboxBitmapAttribute(string? imageFile)
        {
            _name = imageFile;
        }

        public ToolboxBitmapAttribute(Type t)
        {
            _type = t;
        }

        public ToolboxBitmapAttribute(Type t, string? name)
        {
            _type = t;
            _name = name;
        }

        public override bool Equals(object? obj) =>
            obj is ToolboxBitmapAttribute other
            && Equals(_type, other._type)
            && string.Equals(_name, other._name, StringComparison.Ordinal);

        public override int GetHashCode() =>
            HashCode.Combine(_type, _name);

        public Image? GetImage(object component) => GetImage(component?.GetType(), false);

        public Image? GetImage(object component, bool large) => GetImage(component?.GetType(), large);

        public Image? GetImage(Type? type) => GetImage(type, false);

        public Image? GetImage(Type? type, bool large)
        {
            _ = large;
            return GetImage(type, _name, false);
        }

        public Image? GetImage(Type? type, string? imgName, bool large)
        {
            _ = type;
            _ = imgName;
            _ = large;
            return null;
        }

        public static Image? GetImageFromResource(Type? type, string? imgName, bool large)
        {
            _ = type;
            _ = imgName;
            _ = large;
            return null;
        }
    }

    /// <summary>Bitmap suffix marker (cf. System.Drawing.BitmapSuffixInSameAssemblyAttribute).</summary>
    public class BitmapSuffixInSameAssemblyAttribute : Attribute
    {
        public BitmapSuffixInSameAssemblyAttribute()
        {
        }
    }

    /// <summary>Bitmap suffix marker (cf. System.Drawing.BitmapSuffixInSatelliteAssemblyAttribute).</summary>
    public class BitmapSuffixInSatelliteAssemblyAttribute : Attribute
    {
        public BitmapSuffixInSatelliteAssemblyAttribute()
        {
        }
    }

    /// <summary>Multi-frame animation driver (cf. System.Drawing.ImageAnimator).</summary>
    public static class ImageAnimator
    {
        private static readonly ConditionalWeakTable<Image, AnimatorState> _states = new();

        public static bool CanAnimate(Image? image) =>
            image is not null && image.GetFrameCount(Imaging.FrameDimension.Time) > 1;

        // Desktop parity (verified live on .NET Framework: Animate /
        // StopAnimate with null image and/or null handler are silent
        // no-ops, never throws): Telerik's ImagePrimitive unbinds with
        // a null image during construction.
        // Regression: ArgumentNullException 'image' | StopAnimate <-
        // BugTracker.RadRibbonForm1..ctor.
        public static void Animate(Image? image, EventHandler? onFrameChanged)
        {
            if (image is null || onFrameChanged is null)
                return;
            StateFor(image).Handlers.Add(onFrameChanged);
        }

        public static void StopAnimate(Image? image, EventHandler? onFrameChanged)
        {
            if (image is null || onFrameChanged is null)
                return;
            if (_states.TryGetValue(image, out AnimatorState? state))
                state.Handlers.Remove(onFrameChanged);
        }

        public static void UpdateFrames(Image? image)
        {
            if (image is null)
                return;
            if (_states.TryGetValue(image, out AnimatorState? state))
                Advance(image, state);
        }

        public static void UpdateFrames()
        {
            foreach (var entry in _states)
                Advance(entry.Key, entry.Value);
        }

        private static AnimatorState StateFor(Image image)
        {
            if (!_states.TryGetValue(image, out AnimatorState? state))
                _states.Add(image, state = new AnimatorState());
            return state;
        }

        private static void Advance(Image image, AnimatorState state)
        {
            int frames = image.GetFrameCount(Imaging.FrameDimension.Time);
            if (frames <= 1)
                return;
            state.Frame = (state.Frame + 1) % frames;
            image.SelectActiveFrame(Imaging.FrameDimension.Time, state.Frame);
            foreach (EventHandler handler in state.Handlers.ToArray())
                handler(image, EventArgs.Empty);
        }

        private sealed class AnimatorState
        {
            public readonly List<EventHandler> Handlers = new();
            public int Frame;
        }
    }
}

namespace System.Drawing.Design
{
    /// <summary>Category names (cf. System.Drawing.Design.CategoryNameCollection).</summary>
    public sealed class CategoryNameCollection : System.Collections.ReadOnlyCollectionBase
    {
        public CategoryNameCollection(CategoryNameCollection value)
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (string name in value)
                InnerList.Add(name);
        }

        public CategoryNameCollection(string[] value)
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (string name in value)
                InnerList.Add(name);
        }

        public string? this[int index] => InnerList[index] as string;

        public bool Contains(string? value) => InnerList.Contains(value);

        public void CopyTo(string[] array, int index) => InnerList.CopyTo(array, index);

        public int IndexOf(string? value) => InnerList.IndexOf(value);
    }

    /// <summary>Editor invocation style (cf. System.Drawing.Design.UITypeEditorEditStyle).</summary>
    public enum UITypeEditorEditStyle
    {
        None = 1,
        Modal = 2,
        DropDown = 3,
    }

    /// <summary>Paint-value context (cf. System.Drawing.Design.PaintValueEventArgs).</summary>
    public class PaintValueEventArgs : EventArgs
    {
        public PaintValueEventArgs(ComponentModel.ITypeDescriptorContext? context, object? value, Graphics? graphics, Rectangle bounds)
        {
            Context = context;
            Value = value;
            Graphics = graphics;
            Bounds = bounds;
        }

        public Rectangle Bounds { get; }

        public ComponentModel.ITypeDescriptorContext? Context { get; }

        public Graphics? Graphics { get; }

        public object? Value { get; }
    }

    /// <summary>
    /// Property-grid value editor base (cf. System.Drawing.Design.UITypeEditor).
    /// Headless: no modal/drop-down UI exists in the browser, so the base
    /// answers the desktop defaults (None / passthrough / no paint). The
    /// full virtual shape is load-bearing — Telerik derives editors from
    /// this type, and an override with no base virtual is itself a
    /// TypeLoad. Regression: UITypeEditor TypeLoad &lt;- RadColorBoxElement.
    /// </summary>
    public abstract class UITypeEditor
    {
        protected UITypeEditor()
        {
        }

        public virtual object? EditValue(IServiceProvider? provider, object? value) => value;

        public virtual object? EditValue(ComponentModel.ITypeDescriptorContext? context, IServiceProvider? provider, object? value) => value;

        public virtual UITypeEditorEditStyle GetEditStyle() => UITypeEditorEditStyle.None;

        public virtual UITypeEditorEditStyle GetEditStyle(ComponentModel.ITypeDescriptorContext? context) => GetEditStyle();

        public virtual bool GetPaintValueSupported() => false;

        public virtual bool GetPaintValueSupported(ComponentModel.ITypeDescriptorContext? context) => GetPaintValueSupported();

        public virtual void PaintValue(PaintValueEventArgs? e)
        {
        }

        public virtual void PaintValue(object? value, Graphics? canvas, Rectangle rectangle)
        {
        }
    }

    /// <summary>Toolbox-item factory callback (cf. System.Drawing.Design.ToolboxItemCreatorCallback).</summary>
    public delegate ToolboxItem? ToolboxItemCreatorCallback(object? serializedObject, string? format);

    /// <summary>Toolbox service contract (cf. System.Drawing.Design.IToolboxService).</summary>
    public interface IToolboxService
    {
        void AddCreator(ToolboxItemCreatorCallback? creator, string? format);

        void AddCreator(ToolboxItemCreatorCallback? creator, string? format, ComponentModel.Design.IDesignerHost? host);

        void AddLinkedToolboxItem(ToolboxItem? toolboxItem, ComponentModel.Design.IDesignerHost? host);

        void AddLinkedToolboxItem(ToolboxItem? toolboxItem, string? category, ComponentModel.Design.IDesignerHost? host);

        void AddToolboxItem(ToolboxItem? toolboxItem);

        void AddToolboxItem(ToolboxItem? toolboxItem, ComponentModel.Design.IDesignerHost? host);

        void AddToolboxItem(ToolboxItem? toolboxItem, string? category);

        void AddToolboxItem(ToolboxItem? toolboxItem, string? category, ComponentModel.Design.IDesignerHost? host);

        ToolboxItem? GetSelectedToolboxItem();

        ToolboxItem? GetSelectedToolboxItem(ComponentModel.Design.IDesignerHost? host);

        bool IsSupported(object? serializedObject, ComponentModel.Design.IDesignerHost? host);

        bool IsSupported(object? serializedObject, ICollection? filterAttributes);

        bool IsToolboxItem(object? serializedObject);

        bool IsToolboxItem(object? serializedObject, ComponentModel.Design.IDesignerHost? host);

        void Refresh();

        void RemoveCreator(string? format);

        void RemoveCreator(string? format, ComponentModel.Design.IDesignerHost? host);

        void RemoveToolboxItem(ToolboxItem? toolboxItem);

        void RemoveToolboxItem(ToolboxItem? toolboxItem, string? category);

        string? SelectedCategory { get; set; }

        bool SetCursor();

        void SetSelectedToolboxItem(ToolboxItem? toolboxItem);

        void SetSelectedToolboxItem(ToolboxItem? toolboxItem, ComponentModel.Design.IDesignerHost? host);
    }

    /// <summary>
    /// Toolbox item (cf. System.Drawing.Design.ToolboxItem). Headless:
    /// creation answers empty (no designer host in the browser); Bitmap
    /// stays null per the codec-milestone rule at the top of this file.
    /// </summary>
    public class ToolboxItem
    {
        public ToolboxItem()
        {
        }

        public ToolboxItem(Type? toolType)
        {
            Initialize(toolType);
        }

        public virtual Bitmap? Bitmap { get; set; }

        public virtual string DisplayName { get; set; } = "";

        public virtual string TypeName { get; set; } = "";

        public virtual Version Version => new(0, 0);

        public virtual ComponentModel.IComponent[] CreateComponents() => [];

        public virtual ComponentModel.IComponent[] CreateComponents(ComponentModel.Design.IDesignerHost? host) => [];

        public virtual void Initialize(Type? toolType)
        {
            if (toolType is not null)
            {
                TypeName = toolType.AssemblyQualifiedName ?? "";
                DisplayName = toolType.Name;
            }
        }
    }
}
