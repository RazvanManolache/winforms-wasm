extern alias WfDraw;

namespace WfWasm.Shim;


/// <summary>
/// Image store (cf. System.Windows.Forms.ImageList). Images are kept as
/// managed bitmaps and <c>Draw</c> paints the stored image; there is no native
/// image list behind the handle token.
/// </summary>
public sealed class ImageList : System.ComponentModel.Component
{
    private readonly ImageCollection _images;

    /// <summary>Creates an image list (cf. ImageList()).</summary>
    public ImageList()
    {
        _images = new ImageCollection(this);
    }

    /// <summary>Creates an image list in a container (cf. ImageList(IContainer)).</summary>
    public ImageList(System.ComponentModel.IContainer? container)
        : this()
    {
        container?.Add(this);
    }

    /// <summary>Color depth (cf. ColorDepth). Stored only.</summary>
    public ColorDepth ColorDepth { get; set; } = ColorDepth.Depth32Bit;

    /// <summary>Native handle token (cf. Handle). Zero: no native list.</summary>
    public IntPtr Handle => IntPtr.Zero;

    /// <summary>Whether the handle exists (cf. HandleCreated). Always false.</summary>
    public bool HandleCreated => false;

    /// <summary>Stored images (cf. Images).</summary>
    public ImageCollection Images => _images;

    /// <summary>Image size (cf. ImageSize).</summary>
    public WfDraw::System.Drawing.Size ImageSize { get; set; } = new(16, 16);

    /// <summary>Caller data (cf. Tag).</summary>
    public object? Tag { get; set; }

    /// <summary>Transparent color (cf. TransparentColor). Stored only.</summary>
    public WfDraw::System.Drawing.Color TransparentColor { get; set; } = WfDraw::System.Drawing.Color.Transparent;

    /// <summary>Raised when the handle recreates (cf. RecreateHandle). Never raised.</summary>
    public event EventHandler? RecreateHandle;

    private void OnRecreateHandle(EventArgs e) => RecreateHandle?.Invoke(this, e);

    /// <summary>Draws an image at a point (cf. Draw(Graphics, Point, int)).</summary>
    public void Draw(WfDraw::System.Drawing.Graphics graphics, WfDraw::System.Drawing.Point point, int index)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (index >= 0 && index < _images.Count && _images[index] is WfDraw::System.Drawing.Image image)
            graphics.DrawImage(image, point);
    }

    /// <summary>Draws an image at coordinates (cf. Draw(Graphics, int, int, int)).</summary>
    public void Draw(WfDraw::System.Drawing.Graphics graphics, int x, int y, int index) =>
        Draw(graphics, new WfDraw::System.Drawing.Point(x, y), index);

    /// <summary>Draws an image sized (cf. Draw(Graphics, int, int, int, int, int)).</summary>
    public void Draw(WfDraw::System.Drawing.Graphics graphics, int x, int y, int width, int height, int index)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (index >= 0 && index < _images.Count && _images[index] is WfDraw::System.Drawing.Image image)
            graphics.DrawImage(image, new WfDraw::System.Drawing.Rectangle(x, y, width, height));
    }

    /// <inheritdoc/>
    public override string ToString() => $"ImageList({Images.Count} images)";

    /// <summary>Image store (cf. ImageList.ImageCollection).</summary>
    public sealed class ImageCollection
    {
        private readonly List<(string Key, WfDraw::System.Drawing.Image? Image)> _items = new();

        internal ImageCollection(ImageList owner)
        {
        }

        /// <summary>Image count.</summary>
        public int Count => _items.Count;

        /// <summary>Whether the list is read-only. False.</summary>
        public bool IsReadOnly => false;

        /// <summary>Image at an index.</summary>
        public WfDraw::System.Drawing.Image? this[int index] => _items[index].Image;

        /// <summary>Adds an image (cf. Add(Image)).</summary>
        public int Add(WfDraw::System.Drawing.Image? image)
        {
            _items.Add((string.Empty, image));
            return _items.Count - 1;
        }

        /// <summary>Adds a keyed image (cf. Add(string, Image)).</summary>
        public void Add(string key, WfDraw::System.Drawing.Image? image) => _items.Add((key, image));

        /// <summary>Removes all images (cf. Clear).</summary>
        public void Clear() => _items.Clear();

        /// <summary>Whether a key exists (cf. ContainsKey).</summary>
        public bool ContainsKey(string key) => IndexOfKey(key) >= 0;

        /// <summary>Index of a key (cf. IndexOfKey).</summary>
        public int IndexOfKey(string key)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Key == key)
                    return i;
            return -1;
        }

        /// <summary>Image keys (cf. Keys).</summary>
        public System.Collections.Specialized.StringCollection Keys
        {
            get
            {
                var keys = new System.Collections.Specialized.StringCollection();
                foreach ((string key, _) in _items)
                    keys.Add(key);
                return keys;
            }
        }
    }
}
