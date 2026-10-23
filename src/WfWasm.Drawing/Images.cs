// Image family: exact .NET member shapes, web-compatible CPU raster model.
// Pixels store as 32bpp ARGB; PNG file/stream decode is implemented
// (PngCodec); other formats name their follow-up milestone.
namespace System.Drawing.Imaging
{
    /// <summary>Codec identity (cf. System.Drawing.Imaging.ImageFormat).</summary>
    public sealed class ImageFormat
    {
        public ImageFormat(Guid guid)
        {
            Guid = guid;
        }

        public Guid Guid { get; }

        public static ImageFormat MemoryBmp { get; } = new(new Guid("b96b3caa-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Bmp { get; } = new(new Guid("b96b3cab-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Emf { get; } = new(new Guid("b96b3cac-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Wmf { get; } = new(new Guid("b96b3cad-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Gif { get; } = new(new Guid("b96b3cb0-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Jpeg { get; } = new(new Guid("b96b3cae-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Png { get; } = new(new Guid("b96b3caf-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Tiff { get; } = new(new Guid("b96b3cb1-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Exif { get; } = new(new Guid("b96b3cb2-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Icon { get; } = new(new Guid("b96b3cb5-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Heif { get; } = new(new Guid("b96b3cb6-0728-11d3-9d7b-0000f81ef32e"));
        public static ImageFormat Webp { get; } = new(new Guid("b96b3cb7-0728-11d3-9d7b-0000f81ef32e"));

        public override bool Equals(object? obj) => obj is ImageFormat other && Guid.Equals(other.Guid);
        public override int GetHashCode() => Guid.GetHashCode();
        public override string ToString() => Guid.ToString();
    }

    /// <summary>Frame identity (cf. System.Drawing.Imaging.FrameDimension).</summary>
    public sealed class FrameDimension
    {
        public FrameDimension(Guid guid)
        {
            Guid = guid;
        }

        public Guid Guid { get; }

        public static FrameDimension Time { get; } = new(new Guid("6aedbd6d-3fb5-418a-83a6-7f45229dc872"));
        public static FrameDimension Resolution { get; } = new(new Guid("84236f7b-3bd3-428f-8dab-4ea1439ca315"));
        public static FrameDimension Page { get; } = new(new Guid("7462dc86-6180-4c7e-8e3f-ee7333a7a483"));

        public override bool Equals(object? obj) => obj is FrameDimension other && Guid.Equals(other.Guid);
        public override int GetHashCode() => Guid.GetHashCode();
        public override string ToString() => Guid.ToString();
    }

    /// <summary>Palette (cf. System.Drawing.Imaging.ColorPalette).</summary>
    public sealed class ColorPalette
    {
        public ColorPalette(int entries)
        {
            Entries = new Color[entries];
        }

        public ColorPalette(Color[] entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            Entries = (Color[])entries.Clone();
        }

        public ColorPalette(PaletteType paletteType)
        {
            int[] table = paletteType switch
            {
                PaletteType.FixedBlackAndWhite => PaletteTables.FixedBlackAndWhite,
                PaletteType.FixedHalftone8 => PaletteTables.FixedHalftone8,
                PaletteType.FixedHalftone27 => PaletteTables.FixedHalftone27,
                PaletteType.FixedHalftone64 => PaletteTables.FixedHalftone64,
                PaletteType.FixedHalftone125 => PaletteTables.FixedHalftone125,
                PaletteType.FixedHalftone216 => PaletteTables.FixedHalftone216,
                PaletteType.FixedHalftone252 => PaletteTables.FixedHalftone252,
                PaletteType.FixedHalftone256 => PaletteTables.FixedHalftone256,
                _ => PaletteTables.Custom,
            };
            Flags = paletteType switch
            {
                PaletteType.FixedBlackAndWhite => 512,
                PaletteType.FixedHalftone8 => 768,
                PaletteType.FixedHalftone27 => 1024,
                PaletteType.FixedHalftone64 => 1280,
                PaletteType.FixedHalftone125 => 1536,
                PaletteType.FixedHalftone216 => 1792,
                PaletteType.FixedHalftone252 => 2048,
                PaletteType.FixedHalftone256 => 2304,
                _ => 0,
            };
            Entries = new Color[table.Length];
            for (int i = 0; i < table.Length; i++)
                Entries[i] = Color.FromArgb(table[i]);
        }

        public static ColorPalette CreateOptimalPalette(int size, bool useTransparentColor, Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
            var freq = new Dictionary<int, int>();
            bool hasTransparent = false;
            foreach (int p in bitmap.Pixels)
            {
                if ((p >> 24) == 0)
                    hasTransparent = true;
                int rgb = p & 0xFFFFFF;
                freq.TryGetValue(rgb, out int n);
                freq[rgb] = n + 1;
            }
            var top = freq.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            var result = new ColorPalette(size);
            int slot = 0;
            if (useTransparentColor && hasTransparent && size > 0)
                result.Entries[slot++] = Color.Transparent;
            foreach (int rgb in top)
            {
                if (slot >= size)
                    break;
                result.Entries[slot++] = Color.FromArgb(unchecked((int)0xFF000000) | rgb);
            }
            return result;
        }

        public int Flags { get; }
        public Color[] Entries { get; }
    }

    /// <summary>Metadata item (cf. System.Drawing.Imaging.PropertyItem).</summary>
    public sealed class PropertyItem
    {
        public int Id { get; set; }
        public int Len { get; set; }
        public short Type { get; set; }
        public byte[] Value { get; set; } = [];
    }

    /// <summary>Locked pixels (cf. System.Drawing.Imaging.BitmapData).</summary>
    public sealed class BitmapData
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
        public PixelFormat PixelFormat { get; set; }
        public IntPtr Scan0 { get; set; }
        public int Reserved { get; set; }

        internal byte[]? Owned { get; set; }
        internal Imaging.PixelFormat OwnedFormat { get; set; }
        internal System.Runtime.InteropServices.GCHandle Pin;
        internal bool HasPin;
    }
}

namespace System.Drawing
{
    /// <summary>Device-context contract (cf. System.Drawing.IDeviceContext).</summary>
    public interface IDeviceContext : IDisposable
    {
        IntPtr GetHdc();
        void ReleaseHdc();
    }

    /// <summary>Image base (cf. System.Drawing.Image).</summary>
    [System.ComponentModel.TypeConverter(typeof(ImageConverter))]
    public abstract class Image : ICloneable, IDisposable
    {
        private readonly Dictionary<int, Imaging.PropertyItem> _properties = new();

        public abstract int Width { get; }
        public abstract int Height { get; }

        public virtual Imaging.PixelFormat PixelFormat => Imaging.PixelFormat.Format32bppArgb;
        public virtual Imaging.ImageFormat RawFormat => Imaging.ImageFormat.MemoryBmp;
        public virtual float HorizontalResolution => 96f;
        public virtual float VerticalResolution => 96f;
        public virtual object? Tag { get; set; }
        public virtual int Flags => 0;

        public Size Size => new(Width, Height);
        public SizeF PhysicalDimension => new(Width, Height);
        public virtual Imaging.ColorPalette Palette { get; set; } = new Imaging.ColorPalette(0);
        public Guid[] FrameDimensionsList => [Imaging.FrameDimension.Time.Guid];
        public int[] PropertyIdList => _properties.Keys.ToArray();
        public Imaging.PropertyItem[] PropertyItems => _properties.Values.ToArray();

        public abstract object Clone();

        public static int GetPixelFormatSize(Imaging.PixelFormat pixelFormat) => pixelFormat switch
        {
            Imaging.PixelFormat.Format1bppIndexed => 1,
            Imaging.PixelFormat.Format4bppIndexed => 4,
            Imaging.PixelFormat.Format8bppIndexed => 8,
            Imaging.PixelFormat.Format16bppArgb1555 => 16,
            Imaging.PixelFormat.Format16bppGrayScale => 16,
            Imaging.PixelFormat.Format16bppRgb555 => 16,
            Imaging.PixelFormat.Format16bppRgb565 => 16,
            Imaging.PixelFormat.Format24bppRgb => 24,
            Imaging.PixelFormat.Format32bppArgb => 32,
            Imaging.PixelFormat.Format32bppPArgb => 32,
            Imaging.PixelFormat.Format32bppRgb => 32,
            Imaging.PixelFormat.Format48bppRgb => 48,
            Imaging.PixelFormat.Format64bppArgb => 64,
            Imaging.PixelFormat.Format64bppPArgb => 64,
            _ => 0,
        };

        public static bool IsAlphaPixelFormat(Imaging.PixelFormat format) =>
            (format & Imaging.PixelFormat.Alpha) != 0;

        public static bool IsCanonicalPixelFormat(Imaging.PixelFormat format) =>
            (format & Imaging.PixelFormat.Canonical) != 0;

        public static bool IsExtendedPixelFormat(Imaging.PixelFormat format) =>
            (format & Imaging.PixelFormat.Extended) != 0;

        public virtual int GetFrameCount(Imaging.FrameDimension dimension) => 1;

        public virtual int SelectActiveFrame(Imaging.FrameDimension dimension, int frameIndex) => 0;

        public virtual RectangleF GetBounds(ref GraphicsUnit pageUnit)
        {
            pageUnit = GraphicsUnit.Pixel;
            return new RectangleF(0, 0, Width, Height);
        }

        public virtual Imaging.PropertyItem GetPropertyItem(int propid)
        {
            if (_properties.TryGetValue(propid, out Imaging.PropertyItem? item))
                return item;
            throw new ArgumentException("Property cannot be found.", nameof(propid));
        }

        public virtual void RemovePropertyItem(int propid) => _properties.Remove(propid);

        public virtual void SetPropertyItem(Imaging.PropertyItem propitem)
        {
            ArgumentNullException.ThrowIfNull(propitem);
            _properties[propitem.Id] = propitem;
        }

        public virtual void RotateFlip(RotateFlipType rotateFlipType) => RotateFlipCore(rotateFlipType);

        protected virtual void RotateFlipCore(RotateFlipType rotateFlipType)
        {
        }

        public virtual Image GetThumbnailImage(int thumbWidth, int thumbHeight, GetThumbnailImageAbort? callback, IntPtr callbackData)
        {
            var thumb = new Bitmap(Math.Max(1, thumbWidth), Math.Max(1, thumbHeight));
            using var g = Graphics.FromImage(thumb);
            g.DrawImage(this, new Rectangle(0, 0, thumb.Width, thumb.Height));
            return thumb;
        }

        public delegate bool GetThumbnailImageAbort();

        public static Image FromFile(string filename)
        {
            if (Bitmap.TryLoadVfs(filename, out Bitmap? vfs))
                return vfs;
            using var fs = File.OpenRead(filename);
            return PngCodec.DecodeStream(fs);
        }

        public static Image FromFile(string filename, bool useEmbeddedColorManagement)
        {
            if (Bitmap.TryLoadVfs(filename, out Bitmap? vfs))
                return vfs;
            using var fs = File.OpenRead(filename);
            return PngCodec.DecodeStream(fs);
        }

        public static Image FromStream(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return PngCodec.DecodeStream(stream);
        }

        public static Image FromStream(Stream stream, bool useEmbeddedColorManagement)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return PngCodec.DecodeStream(stream);
        }

        public static Image FromStream(Stream stream, bool useEmbeddedColorManagement, bool validateImageData)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return PngCodec.DecodeStream(stream);
        }

        // Trackable fakes minted by GetHbitmap/BitmapHandleMap resolve to a
        // pixel copy (callers such as ImageList.GetBitmap dispose the result,
        // so aliasing the registered bitmap would corrupt the owner).
        // Foreign handles keep the honest NotSupportedException.
        public static Bitmap FromHbitmap(IntPtr hbitmap)
        {
            if (BitmapHandleMap.TryResolve(hbitmap, out Bitmap? src) && src is not null)
            {
                Bitmap copy = new Bitmap(src.Width, src.Height, src.PixelFormat);
                using (Graphics g = Graphics.FromImage(copy))
                    g.DrawImage(src, 0, 0);
                return copy;
            }
            throw new NotSupportedException("GDI handles are not available on web.");
        }

        public static Bitmap FromHbitmap(IntPtr hbitmap, IntPtr hpalette)
        {
            if (BitmapHandleMap.TryResolve(hbitmap, out Bitmap? src) && src is not null)
            {
                Bitmap copy = new Bitmap(src.Width, src.Height, src.PixelFormat);
                using (Graphics g = Graphics.FromImage(copy))
                    g.DrawImage(src, 0, 0);
                return copy;
            }
            throw new NotSupportedException("GDI handles are not available on web.");
        }

        public virtual void Save(string filename) =>
            throw new NotSupportedException("Image codecs arrive with the codec milestone.");

        public virtual void Save(string filename, Imaging.ImageFormat format) =>
            throw new NotSupportedException("Image codecs arrive with the codec milestone.");

        public virtual void Save(string filename, Imaging.ImageCodecInfo encoder, Imaging.EncoderParameters encoderParams) =>
            throw new NotSupportedException("Image codecs arrive with the codec milestone.");

        public virtual void Save(Stream stream, Imaging.ImageFormat format) =>
            throw new NotSupportedException("Image codecs arrive with the codec milestone.");

        public virtual void Save(Stream stream, Imaging.ImageCodecInfo encoder, Imaging.EncoderParameters encoderParams) =>
            throw new NotSupportedException("Image codecs arrive with the codec milestone.");

        public virtual void SaveAdd(Image image, Imaging.EncoderParameters encoderParams) =>
            throw new NotSupportedException("Image codecs arrive with the codec milestone.");

        public virtual void SaveAdd(Imaging.EncoderParameters encoderParams) =>
            throw new NotSupportedException("Image codecs arrive with the codec milestone.");

        public virtual Imaging.EncoderParameters GetEncoderParameterList(Guid encoder) =>
            throw new NotSupportedException("Image codecs arrive with the codec milestone.");

        ~Image()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Pixel buffer (cf. System.Drawing.Bitmap).</summary>
    public sealed class Bitmap : Image
    {
        private int[] _pixels;
        private int _width;
        private int _height;
        private float _dpiX = 96f;
        private float _dpiY = 96f;
        private readonly Dictionary<Imaging.BitmapData, LockState> _locks = new();

        public Bitmap(int width, int height)
            : this(width, height, Imaging.PixelFormat.Format32bppArgb)
        {
        }

        public Bitmap(int width, int height, Imaging.PixelFormat format)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            _width = width;
            _height = height;
            PixelFormatStorage = format;
            _pixels = new int[width * height];
        }

        public Bitmap(int width, int height, Graphics g)
            : this(width, height)
        {
            if (g is not null)
            {
                _dpiX = g.DpiX;
                _dpiY = g.DpiY;
            }
        }

        public Bitmap(int width, int height, int stride, Imaging.PixelFormat format, IntPtr scan0)
            : this(width, height, format)
        {
            int bpp = BitsFor(format);
            if (bpp != 24 && bpp != 32)
                throw new NotSupportedException("Only 24/32bpp buffers can wrap a scan pointer.");
            int bytesPerPixel = bpp / 8;
            var bytes = new byte[stride * height];
            System.Runtime.InteropServices.Marshal.Copy(scan0, bytes, 0, bytes.Length);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int o = y * stride + x * bytesPerPixel;
                    _pixels[y * width + x] = bytesPerPixel == 4
                        ? bytes[o] | (bytes[o + 1] << 8) | (bytes[o + 2] << 16) | (bytes[o + 3] << 24)
                        : unchecked((int)0xFF000000) | bytes[o] | (bytes[o + 1] << 8) | (bytes[o + 2] << 16);
                }
            }
        }

        public Bitmap(string filename)
            : this()
        {
            if (TryLoadVfs(filename, out Bitmap? vfs))
            {
                CopyPixels(vfs);
                return;
            }
            using var fs = File.OpenRead(filename);
            CopyPixels(PngCodec.DecodeStream(fs));
        }

        public Bitmap(string filename, bool useIcm)
            : this()
        {
            if (TryLoadVfs(filename, out Bitmap? vfs))
            {
                CopyPixels(vfs);
                return;
            }
            using var fs = File.OpenRead(filename);
            CopyPixels(PngCodec.DecodeStream(fs));
        }

        // VFS resolution shared by FromFile and the filename ctors:
        // browser-decoded pixels win (JPEG), raw bytes must sniff as
        // PNG, anything else falls through to the disk path.
        internal static bool TryLoadVfs(string filename, out Bitmap? bitmap)
        {
            bitmap = null;
            if (WfWasm.Drawing.AppFileSystem.TryGetDecoded(filename, out var decoded) && decoded is not null)
            {
                var bmp = new Bitmap(decoded.Width, decoded.Height);
                int[] px = bmp.Pixels;
                byte[] rgba = decoded.Rgba;
                for (int i = 0; i < px.Length; i++)
                {
                    int o = i * 4;
                    px[i] = (rgba[o + 3] << 24) | (rgba[o] << 16) | (rgba[o + 1] << 8) | rgba[o + 2];
                }

                bitmap = bmp;
                return true;
            }

            if (WfWasm.Drawing.AppFileSystem.TryGetBytes(filename, out byte[]? vfs) && vfs is not null)
            {
                if (!IsPng(vfs))
                    throw new InvalidOperationException(
                        $"VFS file '{filename}' is not PNG and has no decoded pixels: register it via RegisterDecoded at bootstrap.");
                bitmap = PngCodec.DecodeStream(new MemoryStream(vfs, false));
                return true;
            }

            return false;
        }

        private static bool IsPng(byte[] bytes) =>
            bytes.Length > 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 &&
            bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10;
        // PNG encode outlets: bitmap pixels serialize through PngCodec, so
        // vendor snapshot paths (diagram BitmapNode.ImageToBase64) work
        // headless. Every other format keeps the milestone throw.
        public override void Save(Stream stream, Imaging.ImageFormat format)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(format);
            if (format.Equals(Imaging.ImageFormat.Png))
            {
                PngCodec.Encode(stream, this);
                return;
            }

            throw new NotSupportedException("Image codecs arrive with the codec milestone.");
        }

        public override void Save(string filename)
        {
            using var ms = new MemoryStream();
            PngCodec.Encode(ms, this);
            System.IO.File.WriteAllBytes(filename, ms.ToArray());
        }

        public override void Save(string filename, Imaging.ImageFormat format)
        {
            ArgumentNullException.ThrowIfNull(format);
            if (format.Equals(Imaging.ImageFormat.Png))
            {
                using var ms = new MemoryStream();
                PngCodec.Encode(ms, this);
                System.IO.File.WriteAllBytes(filename, ms.ToArray());
                return;
            }

            throw new NotSupportedException("Image codecs arrive with the codec milestone.");
        }


        public Bitmap(Stream stream)
            : this()
        {
            ArgumentNullException.ThrowIfNull(stream);
            CopyPixels(PngCodec.DecodeStream(stream));
        }

        public Bitmap(Stream stream, bool useIcm)
            : this()
        {
            ArgumentNullException.ThrowIfNull(stream);
            CopyPixels(PngCodec.DecodeStream(stream));
        }

        public Bitmap(Type type, string resource)
            : this()
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(resource);
            using var rs = type.Assembly.GetManifestResourceStream(type, resource)
                ?? throw new ArgumentException($"Resource '{resource}' was not found.", nameof(resource));
            CopyPixels(PngCodec.DecodeStream(rs));
        }

        private void CopyPixels(Bitmap source)
        {
            _width = source._width;
            _height = source._height;
            // Defensive copy: the source may be a shared decode-cache
            // entry, and aliasing it would let draws into this bitmap
            // corrupt the cache (and poison DrawImage memo entries keyed
            // by the source's paint version).
            _pixels = (int[])source._pixels.Clone();
            _dpiX = source._dpiX;
            _dpiY = source._dpiY;
            // Keep the decoded storage tag (32bppRgb BMPs report Rgb like
            // GDI+; GetPixel masks those reads opaque on the way out).
            PixelFormatStorage = source.PixelFormatStorage;
        }

        public Bitmap(Image? original)
            : this(OriginalSize(original).Width, OriginalSize(original).Height)
        {
            if (original != null)
                BlitFrom(original, 0, 0, _width, _height);
        }

        private static Size OriginalSize(Image? original)
        {
            return original?.Size ?? new Size(32, 32);
        }

        public Bitmap(Image? original, Size newSize)
            : this(original, newSize.Width, newSize.Height)
        {
        }

        public Bitmap(Image? original, int width, int height)
            : this(width, height)
        {
            if (original != null)
                BlitFrom(original, 0, 0, width, height);
        }

        private Bitmap()
        {
            _width = 1;
            _height = 1;
            _pixels = new int[1];
            PixelFormatStorage = Imaging.PixelFormat.Format32bppArgb;
        }

        public override int Width => _width;
        public override int Height => _height;
        public override Imaging.PixelFormat PixelFormat => PixelFormatStorage;
        public override float HorizontalResolution => _dpiX;
        public override float VerticalResolution => _dpiY;

        internal Imaging.PixelFormat PixelFormatStorage { get; set; }
        internal int[] Pixels => _pixels;

        // Pixel-content generation: bumped by every mutation path
        // (all Graphics write sinks, SetPixel, UnlockBits write-back,
        // MakeTransparent, RotateFlip, ApplyEffect, ConvertFormat).
        // Consumer: the DrawImage memo key (a changed source must miss).
        // Construction-time writes don't bump (a new identity has no
        // entries to poison). Threading matches GDI+: instances are not
        // thread-safe, so no fences are needed around the counter itself.
        public int PaintVersion { get; internal set; }

        // Whole-surface-write generation, bumped by Graphics.Clear only
        // (the sole primitive that ignores the clip). Consumer: the
        // host's region-repaint guard, which snapshots it around a
        // clipped paint and falls back to a full upload on mismatch.
        public int ClearVersion { get; internal set; }

        public override object Clone()
        {
            var clone = new Bitmap(_width, _height, PixelFormatStorage);
            Array.Copy(_pixels, clone._pixels, _pixels.Length);
            clone._dpiX = _dpiX;
            clone._dpiY = _dpiY;
            clone.PaintVersion = PaintVersion;
            clone.ClearVersion = ClearVersion;
            return clone;
        }

        public Bitmap Clone(Rectangle rect, Imaging.PixelFormat format) =>
            Clone(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), format);

        public Bitmap Clone(RectangleF rect, Imaging.PixelFormat format)
        {
            var crop = Rectangle.Intersect(
                new Rectangle(0, 0, _width, _height),
                new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
            var clone = new Bitmap(Math.Max(1, crop.Width), Math.Max(1, crop.Height), format);
            for (int y = 0; y < crop.Height; y++)
                for (int x = 0; x < crop.Width; x++)
                    clone._pixels[y * clone._width + x] = _pixels[(crop.Y + y) * _width + crop.X + x];
            return clone;
        }

        public Color GetPixel(int x, int y)
        {
            if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
                throw new ArgumentOutOfRangeException("Coordinates are outside the bitmap.");
            int argb = _pixels[y * _width + x];
            // GDI+ reports 32bppRgb reads as opaque while keeping the X
            // byte in memory (LockBits exposes it raw). Mask on read.
            if (PixelFormatStorage == Imaging.PixelFormat.Format32bppRgb)
                argb |= unchecked((int)0xFF000000);
            return Color.FromArgb(argb);
        }

        public void SetPixel(int x, int y, Color color)
        {
            if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
                throw new ArgumentOutOfRangeException("Coordinates are outside the bitmap.");
            _pixels[y * _width + x] = color.ToArgb();
            PaintVersion++;
        }

        // Retained scroll reuse (item 4): moves surface content by (dx,dy),
        // so a scrolled control repaints only the exposed strip instead of
        // the full surface. Pixel (x,y) moves to (x+dx,y+dy); exposed strips
        // keep their old bytes (the caller repaints them). Array.Copy is
        // memmove-safe for the overlapping same-array ranges. No-op for
        // (0,0) with no version bump; callers must keep |dx|<width and
        // |dy|<height (a fully-exposed surface has nothing to reuse).
        public void ShiftPixels(int dx, int dy)
        {
            if (dx == 0 && dy == 0)
                return;
            int w = _width, h = _height;
            if (w <= 0 || h <= 0)
                return;
            if (dy != 0)
            {
                if (dy > 0)
                {
                    for (int y = h - 1; y >= dy; y--)
                        Array.Copy(_pixels, (y - dy) * w, _pixels, y * w, w);
                }
                else
                {
                    for (int y = 0; y < h + dy; y++)
                        Array.Copy(_pixels, (y - dy) * w, _pixels, y * w, w);
                }
            }
            if (dx != 0)
            {
                if (dx > 0)
                {
                    for (int y = 0; y < h; y++)
                        Array.Copy(_pixels, y * w, _pixels, y * w + dx, w - dx);
                }
                else
                {
                    for (int y = 0; y < h; y++)
                        Array.Copy(_pixels, y * w - dx, _pixels, y * w, w + dx);
                }
            }
            PaintVersion++;
        }

        // Single-pass int[] -> RGBA bytes (canvas upload path): no
        // LockBits pin/roundtrip, no BGRA intermediate, one read +
        // one write per pixel. Used by the host's layer extractor and
        // small-image painter (per-frame hot path).
        public byte[] ToRgbaBytes()
        {
            byte[] rgba = new byte[_width * _height * 4];
            var dst = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(rgba);
            for (int i = 0; i < _pixels.Length; i++)
            {
                uint v = (uint)_pixels[i];
                dst[i] = (int)((v & 0xFF00FF00u) | ((v & 0xFFu) << 16) | ((v >> 16) & 0xFFu));
            }

            return rgba;
        }

        // Rect-slice twin of ToRgbaBytes (region-repaint upload path):
        // same per-pixel swizzle over the intersected rows only, so the
        // result is byte-identical to slicing the full extract. Empty
        // intersections yield an empty array (never null, never throws).
        public byte[] ToRgbaBytes(Rectangle rect)
        {
            Rectangle crop = Rectangle.Intersect(new Rectangle(0, 0, _width, _height), rect);
            if (crop.Width <= 0 || crop.Height <= 0)
                return Array.Empty<byte>();
            byte[] rgba = new byte[crop.Width * crop.Height * 4];
            var dst = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(rgba);
            int o = 0;
            for (int y = crop.Y; y < crop.Y + crop.Height; y++)
            {
                int row = y * _width + crop.X;
                for (int x = 0; x < crop.Width; x++)
                {
                    uint v = (uint)_pixels[row + x];
                    dst[o++] = (int)((v & 0xFF00FF00u) | ((v & 0xFFu) << 16) | ((v >> 16) & 0xFFu));
                }
            }

            return rgba;
        }

        public void SetResolution(float xDpi, float yDpi)
        {
            _dpiX = xDpi;
            _dpiY = yDpi;
        }

        public void MakeTransparent() => MakeTransparent(Color.LightGray);

        public void MakeTransparent(Color transparentColor)
        {
            int argb = transparentColor.ToArgb();
            for (int i = 0; i < _pixels.Length; i++)
            {
                if ((_pixels[i] & 0xFFFFFF) == (argb & 0xFFFFFF))
                    _pixels[i] = 0x00000000;
            }
            PaintVersion++;
        }

        // Mint a trackable fake (see BitmapHandleMap): ImageList insertion
        // resolves the source pixels from it. Pair with DeleteObject, as
        // with real GDI handles; unreleased entries die with the bitmap.
        public IntPtr GetHbitmap() => BitmapHandleMap.Register(this);
        public IntPtr GetHbitmap(Color background) => BitmapHandleMap.Register(this);
        public IntPtr GetHicon() => IntPtr.Zero;

        // Headless there are no GDI handles, but third-party grids, diagram
        // tools, and settings panes call FromHicon during input handling
        // (Telerik RadDiagram aborts the whole MouseDown when this throws, so
        // shapes never select); same policy as Icon.FromHandle: answer a
        // blank 16x16 placeholder instead of throwing.
        public static Bitmap FromHicon(IntPtr hicon) =>
            new Bitmap(16, 16);

        public static Bitmap FromResource(IntPtr hinstance, string bitmapName) =>
            throw new NotSupportedException("GDI handles are not available on web.");

        public Imaging.BitmapData LockBits(Rectangle rect, Imaging.ImageLockMode flags, Imaging.PixelFormat format) =>
            LockBits(rect, flags, format, new Imaging.BitmapData());

        public Imaging.BitmapData LockBits(Rectangle rect, Imaging.ImageLockMode flags, Imaging.PixelFormat format, Imaging.BitmapData? bitmapData)
        {
            var crop = Rectangle.Intersect(rect, new Rectangle(0, 0, _width, _height));
            int bpp = BitsFor(format);
            if (bpp != 24 && bpp != 32)
                throw new NotSupportedException("Only 24/32bpp lock formats are supported.");
            int bytesPerPixel = bpp / 8;
            int stride = ((crop.Width * bpp + 31) / 32) * 4;
            var bytes = new byte[stride * crop.Height];
            for (int y = 0; y < crop.Height; y++)
            {
                for (int x = 0; x < crop.Width; x++)
                {
                    int argb = _pixels[(crop.Y + y) * _width + crop.X + x];
                    int o = y * stride + x * bytesPerPixel;
                    bytes[o] = (byte)argb;
                    bytes[o + 1] = (byte)(argb >> 8);
                    bytes[o + 2] = (byte)(argb >> 16);
                    if (bytesPerPixel == 4)
                        bytes[o + 3] = (byte)(argb >> 24);
                }
            }

            var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            var data = bitmapData ?? new Imaging.BitmapData();
            data.Width = crop.Width;
            data.Height = crop.Height;
            data.Stride = stride;
            data.PixelFormat = format;
            data.Scan0 = pin.AddrOfPinnedObject();
            data.Owned = bytes;
            data.OwnedFormat = format;
            data.Pin = pin;
            data.HasPin = true;
            _locks[data] = new LockState(crop, flags, format);
            return data;
        }

        public void UnlockBits(Imaging.BitmapData bitmapdata)
        {
            ArgumentNullException.ThrowIfNull(bitmapdata);
            if (!_locks.TryGetValue(bitmapdata, out LockState? state) || bitmapdata.Owned is null)
                throw new ArgumentException("BitmapData was not locked by this bitmap.", nameof(bitmapdata));
            try
            {
                if (state.Flags != Imaging.ImageLockMode.ReadOnly && state.Flags != Imaging.ImageLockMode.UserInputBuffer)
                {
                    int bytesPerPixel = BitsFor(state.Format) / 8;
                    byte[] bytes = bitmapdata.Owned;
                    for (int y = 0; y < state.Rect.Height; y++)
                    {
                        for (int x = 0; x < state.Rect.Width; x++)
                        {
                            int o = y * bitmapdata.Stride + x * bytesPerPixel;
                            int argb = bytes[o] | (bytes[o + 1] << 8) | (bytes[o + 2] << 16)
                                | (bytesPerPixel == 4 ? bytes[o + 3] << 24 : unchecked((int)0xFF000000));
                            _pixels[(state.Rect.Y + y) * _width + state.Rect.X + x] = argb;
                        }
                    }
                    PaintVersion++;
                }
            }
            finally
            {
                if (bitmapdata.HasPin)
                    bitmapdata.Pin.Free();
                bitmapdata.HasPin = false;
                bitmapdata.Scan0 = IntPtr.Zero;
                _locks.Remove(bitmapdata);
            }
        }

        protected override void Dispose(bool disposing)
        {
            // Theme loaders (Telerik ImageFromString) LockBits and never
            // Unlock; free any abandoned pins so repeated loads do not pin
            // garbage forever. Entries stay valid: a later UnlockBits
            // still runs and removes its entry.
            if (disposing)
            {
                foreach (var data in _locks.Keys)
                {
                    if (data.HasPin)
                        data.Pin.Free();
                    data.HasPin = false;
                    data.Scan0 = IntPtr.Zero;
                }
            }
        }

        protected override void RotateFlipCore(RotateFlipType rotateFlipType)
        {
            var next = new int[_pixels.Length];
            int w = _width;
            int h = _height;
            int nw = rotateFlipType is RotateFlipType.Rotate90FlipNone or RotateFlipType.Rotate270FlipNone
                or RotateFlipType.Rotate90FlipX or RotateFlipType.Rotate270FlipX ? h : w;
            int nh = nw == w ? h : w;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    (int dx, int dy) = rotateFlipType switch
                    {
                        RotateFlipType.RotateNoneFlipNone => (x, y),
                        RotateFlipType.Rotate90FlipNone => (h - 1 - y, x),
                        RotateFlipType.Rotate180FlipNone => (w - 1 - x, h - 1 - y),
                        RotateFlipType.Rotate270FlipNone => (y, w - 1 - x),
                        RotateFlipType.RotateNoneFlipX => (w - 1 - x, y),
                        RotateFlipType.Rotate90FlipX => (h - 1 - y, w - 1 - x),
                        RotateFlipType.Rotate180FlipX => (x, h - 1 - y),
                        RotateFlipType.Rotate270FlipX => (y, x),
                        _ => (x, y),
                    };
                    next[dy * nw + dx] = _pixels[y * w + x];
                }
            }

            _pixels = next;
            _width = nw;
            _height = nh;
            PaintVersion++;
        }

        public void ApplyEffect(Imaging.Effects.Effect effect, Rectangle rect)
        {
            ArgumentNullException.ThrowIfNull(effect);
            effect.ApplyTo(this, Rectangle.Intersect(rect, new Rectangle(0, 0, _width, _height)));
            PaintVersion++;
        }

        public void ConvertFormat(Imaging.PixelFormat format) =>
            ConvertFormat(format, Imaging.DitherType.None, Imaging.PaletteType.Custom, new Imaging.ColorPalette(0), 0f);

        public void ConvertFormat(Imaging.PixelFormat format, Imaging.DitherType ditherType, Imaging.PaletteType paletteType, Imaging.ColorPalette palette, float alphaThresholdPercent)
        {
            ArgumentNullException.ThrowIfNull(palette);
            int bpp = BitsFor(format);
            if (bpp is 1 or 4 or 8)
            {
                int[] full = paletteType == Imaging.PaletteType.Custom && palette.Entries.Length == 0
                    ? Imaging.ColorPalette.CreateOptimalPalette(bpp == 1 ? 2 : bpp == 4 ? 16 : 256, false, this).Entries.Select(c => c.ToArgb()).ToArray()
                    : ResolveConvertPalette(paletteType, palette, bpp);
                int want = bpp == 1 ? 2 : bpp == 4 ? 16 : 256;
                int[] table = full.Length == want ? full : full.Take(want).ToArray();
                if (table.Length == 0)
                    table = [unchecked((int)0xFF000000)];
                Palette = new Imaging.ColorPalette(table.Select(Color.FromArgb).ToArray());
                QuantizeToTable(table, ditherType, alphaThresholdPercent);
                PixelFormatStorage = format;
                PaintVersion++;
            }
            else
            {
                PixelFormatStorage = format;
            }
        }

        internal static int[] ResolveConvertPalette(Imaging.PaletteType paletteType, Imaging.ColorPalette palette, int bpp)
        {
            int want = bpp == 1 ? 2 : bpp == 4 ? 16 : 256;
            return paletteType switch
            {
                Imaging.PaletteType.Custom when palette.Entries.Length > 0 =>
                    palette.Entries.Select(c => c.ToArgb()).ToArray(),
                Imaging.PaletteType.FixedBlackAndWhite => Imaging.PaletteTables.FixedBlackAndWhite,
                Imaging.PaletteType.FixedHalftone8 => Imaging.PaletteTables.FixedHalftone8,
                Imaging.PaletteType.FixedHalftone27 => Imaging.PaletteTables.FixedHalftone27,
                Imaging.PaletteType.FixedHalftone64 => Imaging.PaletteTables.FixedHalftone64,
                Imaging.PaletteType.FixedHalftone125 => Imaging.PaletteTables.FixedHalftone125,
                Imaging.PaletteType.FixedHalftone216 => Imaging.PaletteTables.FixedHalftone216,
                Imaging.PaletteType.FixedHalftone252 => Imaging.PaletteTables.FixedHalftone252,
                Imaging.PaletteType.FixedHalftone256 => Imaging.PaletteTables.FixedHalftone256,
                _ => Imaging.PaletteTables.FixedHalftone256,
            };
        }


        internal void QuantizeToTable(int[] table, Imaging.DitherType ditherType, float alphaThresholdPercent)
        {
            float threshold = Math.Clamp(alphaThresholdPercent, 0f, 100f) * 255f / 100f;
            if (ditherType == Imaging.DitherType.ErrorDiffusion)
            {
                FloydSteinberg(table, threshold);
                return;
            }
            bool ordered = ditherType is Imaging.DitherType.Ordered4x4 or Imaging.DitherType.Ordered8x8 or Imaging.DitherType.Ordered16x16;
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int p = _pixels[y * _width + x];
                    int a = (p >> 24) & 0xFF;
                    int r = (p >> 16) & 0xFF;
                    int g = (p >> 8) & 0xFF;
                    int b = p & 0xFF;
                    if (ordered)
                    {
                        int bias = (OrderedBayer[y & 3, x & 3] * 255 / 16) - 127;
                        r = Math.Clamp(r + bias, 0, 255);
                        g = Math.Clamp(g + bias, 0, 255);
                        b = Math.Clamp(b + bias, 0, 255);
                    }
                    int q = NearestInTable(table, r, g, b);
                    _pixels[y * _width + x] = a < threshold
                        ? (q & 0x00FFFFFF)
                        : (a << 24) | (q & 0xFFFFFF);
                }
            }
        }

        internal static int NearestInTable(int[] table, int r, int g, int b)
        {
            int best = table[0];
            int bestDist = int.MaxValue;
            foreach (int t in table)
            {
                int dr = ((t >> 16) & 0xFF) - r;
                int dg = ((t >> 8) & 0xFF) - g;
                int db = (t & 0xFF) - b;
                int dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = t;
                }
            }
            return best;
        }

        internal void FloydSteinberg(int[] table, float threshold)
        {
            var buf = new float[_pixels.Length * 3];
            for (int i = 0; i < _pixels.Length; i++)
            {
                buf[i * 3] = (_pixels[i] >> 16) & 0xFF;
                buf[i * 3 + 1] = (_pixels[i] >> 8) & 0xFF;
                buf[i * 3 + 2] = _pixels[i] & 0xFF;
            }
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int i = y * _width + x;
                    int a = (_pixels[i] >> 24) & 0xFF;
                    int r = Math.Clamp((int)buf[i * 3], 0, 255);
                    int g = Math.Clamp((int)buf[i * 3 + 1], 0, 255);
                    int b = Math.Clamp((int)buf[i * 3 + 2], 0, 255);
                    int q = NearestInTable(table, r, g, b);
                    int qr = (q >> 16) & 0xFF;
                    int qg = (q >> 8) & 0xFF;
                    int qb = q & 0xFF;
                    _pixels[i] = a < threshold ? q & 0xFFFFFF : (a << 24) | (q & 0xFFFFFF);
                    Spread(buf, x + 1, y, 7f / 16f, r - qr, g - qg, b - qb);
                    Spread(buf, x - 1, y + 1, 3f / 16f, r - qr, g - qg, b - qb);
                    Spread(buf, x, y + 1, 5f / 16f, r - qr, g - qg, b - qb);
                    Spread(buf, x + 1, y + 1, 1f / 16f, r - qr, g - qg, b - qb);
                }
            }
        }

        internal void Spread(float[] buf, int x, int y, float f, float er, float eg, float eb)
        {
            if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
                return;
            int i = (y * _width + x) * 3;
            buf[i] += er * f;
            buf[i + 1] += eg * f;
            buf[i + 2] += eb * f;
        }

        internal static readonly int[,] OrderedBayer =
        {
            { 0, 8, 2, 10 },
            { 12, 4, 14, 6 },
            { 3, 11, 1, 9 },
            { 15, 7, 13, 5 },
        };
        internal static int BitsFor(Imaging.PixelFormat format) => GetPixelFormatSize(format);

        private void BlitFrom(Image source, int dx, int dy, int width, int height)
        {
            if (source is Bitmap bmp)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int sx = bmp._width * x / Math.Max(1, width);
                        int sy = bmp._height * y / Math.Max(1, height);
                        _pixels[(dy + y) * _width + dx + x] = bmp._pixels[sy * bmp._width + sx];
                    }
                }
            }
        }

        private sealed class LockState
        {
            public LockState(Rectangle rect, Imaging.ImageLockMode flags, Imaging.PixelFormat format)
            {
                Rect = rect;
                Flags = flags;
                Format = format;
            }

            public Rectangle Rect { get; }
            public Imaging.ImageLockMode Flags { get; }
            public Imaging.PixelFormat Format { get; }
        }
    }

    /// <summary>Icon image (cf. System.Drawing.Icon).</summary>
    [System.ComponentModel.TypeConverter(typeof(IconConverter))]
    public sealed class Icon : ICloneable, IDisposable
    {
        private Bitmap? _bitmap;

        public Icon(string fileName)
            : this()
        {
            if (File.Exists(fileName))
            {
                try
                {
                    using var s = File.OpenRead(fileName);
                    var dec = DecodeIcon(s, null);
                    Width = dec.Width;
                    Height = dec.Height;
                    _bitmap = dec.Raster;
                    return;
                }
                catch { }
            }
            _bitmap = new Bitmap(Width, Height);
        }

        public Icon(string fileName, Size size)
            : this(fileName, size.Width, size.Height)
        {
        }

        public Icon(string fileName, int width, int height)
            : this()
        {
            Width = width;
            Height = height;
            if (File.Exists(fileName))
            {
                try
                {
                    using var s = File.OpenRead(fileName);
                    var dec = DecodeIcon(s, new Size(width, height));
                    Width = dec.Width;
                    Height = dec.Height;
                    _bitmap = dec.Raster;
                    return;
                }
                catch { }
            }
            _bitmap = new Bitmap(Width, Height);
        }

        public Icon(Icon original, Size size)
            : this(original, size.Width, size.Height)
        {
        }

        public Icon(Icon original, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(original);
            _bitmap = original.ToBitmap();
            Width = width;
            Height = height;
        }

        public Icon(Type type, string resource)
            : this()
        {
            if (type != null && resource != null)
            {
                try
                {
                    using var stream = type.Assembly.GetManifestResourceStream(type, resource)
                        ?? type.Assembly.GetManifestResourceStream(resource);
                    if (stream != null)
                    {
                        var decoded = DecodeIcon(stream, null);
                        Width = decoded.Width;
                        Height = decoded.Height;
                        _bitmap = decoded.Raster;
                        return;
                    }
                }
                catch { }
            }
            _bitmap = new Bitmap(Width, Height);
        }

        public Icon(Stream stream)
            : this(DecodeIcon(stream, null))
        {
        }

        public Icon(Stream stream, Size size)
            : this(DecodeIcon(stream, size))
        {
        }

        public Icon(Stream stream, int width, int height)
            : this(DecodeIcon(stream, new Size(width, height)))
        {
        }

        public Icon()
        {
            Width = 32;
            Height = 32;
        }

        private Icon((int Width, int Height, Bitmap Raster) decoded)
        {
            Width = decoded.Width;
            Height = decoded.Height;
            _bitmap = decoded.Raster;
        }

        private sealed record IconEntry(int Width, int Height, int BitCount, int Offset, int Size);

        private static byte[] ReadAll(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        // Decodes an ICO stream: directory + PNG and BMP entries (XOR + AND mask).
        private static (int Width, int Height, Bitmap Raster) DecodeIcon(Stream stream, Size? want)
        {
            byte[] bytes = ReadAll(stream);
            static ushort U16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
            static int I32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
            void Need(int o, int n)
            {
                if (o < 0 || n < 0 || o + n > bytes.Length)
                    throw new ArgumentException("Data is not a valid icon.", nameof(stream));
            }

            Need(0, 6);
            if (U16(bytes, 0) != 0 || U16(bytes, 2) != 1)
                throw new ArgumentException("Data is not a valid icon.", nameof(stream));
            int count = U16(bytes, 4);
            if (count <= 0)
                throw new ArgumentException("Data is not a valid icon.", nameof(stream));
            Need(6, count * 16);

            var entries = new List<IconEntry>(count);
            for (int i = 0; i < count; i++)
            {
                int o = 6 + i * 16;
                int w = bytes[o] == 0 ? 256 : bytes[o];
                int h = bytes[o + 1] == 0 ? 256 : bytes[o + 1];
                int size = I32(bytes, o + 8);
                int off = I32(bytes, o + 12);
                Need(off, size);
                if (size <= 0 || w <= 0 || h <= 0)
                    throw new ArgumentException("Data is not a valid icon.", nameof(stream));
                entries.Add(new IconEntry(w, h, bytes[o + 6], off, size));
            }

            IconEntry pick = entries[0];
            if (want.HasValue)
            {
                IconEntry largest = entries[0];
                IconEntry? exact = null;
                foreach (IconEntry e in entries)
                {
                    if (e.Width == want.Value.Width && e.Height == want.Value.Height)
                    {
                        exact = e;
                        break;
                    }
                    if (e.Width * e.Height > largest.Width * largest.Height)
                        largest = e;
                }
                // Exact match wins; otherwise scale the largest entry down (or up).
                pick = exact ?? largest;
            }

            int at = pick.Offset;
            // The directory size field is advisory: real-world icons
            // understate it (WinForms down.ico declares 104 bytes for a
            // 16x16x32 entry whose geometry needs 1128) and Windows reads
            // by geometry. Clamp to the buffer instead; every read stays
            // bounds-checked against bytes.Length.
            int entrySize = Math.Max(pick.Size, bytes.Length - at);
            bool png = entrySize >= 8 &&
                bytes[at] == 0x89 && bytes[at + 1] == 0x50 && bytes[at + 2] == 0x4E && bytes[at + 3] == 0x47 &&
                bytes[at + 4] == 0x0D && bytes[at + 5] == 0x0A && bytes[at + 6] == 0x1A && bytes[at + 7] == 0x0A;
            Bitmap native = png
                ? PngCodec.DecodeStream(new MemoryStream(bytes, at, entrySize, false))
                : RasterizeBmpEntry(bytes, at, entrySize);
            int tw = want?.Width ?? native.Width;
            int th = want?.Height ?? native.Height;
            Bitmap raster = (tw == native.Width && th == native.Height) ? native : ScaleNearest(native, tw, th);
            return (tw, th, raster);
        }

        // Rasterizes an ICO-embedded BMP (BITMAPINFOHEADER + XOR pixels + AND mask).
        private static Bitmap RasterizeBmpEntry(byte[] bytes, int at, int size)
        {
            static int I32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
            void Need(int o, int n)
            {
                if (o < 0 || n < 0 || at + o + n > bytes.Length || o + n > size)
                    throw new ArgumentException("Data is not a valid icon.", "stream");
            }

            Need(0, 40);
            if (I32(bytes, at) != 40)
                throw new ArgumentException("Data is not a valid icon.", "stream");
            int w = I32(bytes, at + 4);
            int h2 = I32(bytes, at + 8);
            int bpp = I32(bytes, at + 14) & 0xFFFF;
            int compression = I32(bytes, at + 16);
            int colors = I32(bytes, at + 32);
            if (w <= 0 || h2 == 0 || compression != 0 ||
                (bpp != 1 && bpp != 4 && bpp != 8 && bpp != 24 && bpp != 32))
                throw new ArgumentException("Data is not a valid icon.", "stream");
            int h = Math.Abs(h2) / 2;
            bool topDown = h2 < 0;
            if (h <= 0)
                throw new ArgumentException("Data is not a valid icon.", "stream");

            int palCount = bpp <= 8 ? (colors != 0 ? colors : 1 << bpp) : 0;
            Need(40, palCount * 4);
            int[] palette = new int[palCount];
            for (int i = 0; i < palCount; i++)
            {
                int p = at + 40 + i * 4;
                palette[i] = unchecked((int)0xFF000000) | (bytes[p + 2] << 16) | (bytes[p + 1] << 8) | bytes[p];
            }

            int xorStride = ((w * bpp + 31) / 32) * 4;
            int xorOff = 40 + palCount * 4;
            int andStride = ((w + 31) / 32) * 4;
            int andOff = xorOff + xorStride * h;
            Need(xorOff, xorStride * h);
            // The AND mask is advisory: 32bpp icons with an alpha channel
            // routinely omit it (WinForms up.ico ships header+XOR only) and
            // Windows treats the missing mask as all-opaque. Only read it
            // when the bytes are actually there.
            bool hasAndMask = andOff >= 0 && andStride >= 0 && h >= 0
                && (long)andOff + (long)andStride * h <= size
                && (long)at + andOff + (long)andStride * h <= bytes.Length;

            var bitmap = new Bitmap(w, h);
            int[] dest = bitmap.Pixels;
            for (int y = 0; y < h; y++)
            {
                int srcY = topDown ? y : h - 1 - y;
                for (int x = 0; x < w; x++)
                {
                    int r, g, b, aSrc = 255;
                    if (bpp <= 8)
                    {
                        int index = bpp switch
                        {
                            1 => (bytes[at + xorOff + srcY * xorStride + x / 8] >> (7 - x % 8)) & 1,
                            4 => x % 2 == 0 ? bytes[at + xorOff + srcY * xorStride + x / 2] >> 4
                                             : bytes[at + xorOff + srcY * xorStride + x / 2] & 15,
                            _ => bytes[at + xorOff + srcY * xorStride + x],
                        };
                        if ((uint)index >= (uint)palCount)
                            throw new ArgumentException("Data is not a valid icon.", "stream");
                        int c = palette[index];
                        r = (c >> 16) & 255;
                        g = (c >> 8) & 255;
                        b = c & 255;
                    }
                    else if (bpp == 24)
                    {
                        int p = at + xorOff + srcY * xorStride + x * 3;
                        b = bytes[p];
                        g = bytes[p + 1];
                        r = bytes[p + 2];
                    }
                    else
                    {
                        int p = at + xorOff + srcY * xorStride + x * 4;
                        b = bytes[p];
                        g = bytes[p + 1];
                        r = bytes[p + 2];
                        aSrc = bytes[p + 3];
                    }
                    int alpha;
                    if (hasAndMask)
                    {
                        int andByte = bytes[at + andOff + y * andStride + x / 8];
                        alpha = ((andByte >> (7 - x % 8)) & 1) == 1 ? 0 : (bpp == 32 ? aSrc : 255);
                    }
                    else
                    {
                        alpha = bpp == 32 ? aSrc : 255;
                    }
                    dest[y * w + x] = (alpha << 24) | (r << 16) | (g << 8) | b;
                }
            }
            return bitmap;
        }

        private static Bitmap ScaleNearest(Bitmap src, int width, int height)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            var scaled = new Bitmap(width, height);
            int[] from = src.Pixels;
            int[] to = scaled.Pixels;
            for (int y = 0; y < height; y++)
            {
                int sy = Math.Min(src.Height - 1, y * src.Height / height);
                for (int x = 0; x < width; x++)
                    to[y * width + x] = from[sy * src.Width + Math.Min(src.Width - 1, x * src.Width / width)];
            }
            return scaled;
        }

        internal Icon(Bitmap bitmap)
        {
            _bitmap = bitmap;
            Width = bitmap.Width;
            Height = bitmap.Height;
        }

        public int Width { get; }
        public int Height { get; }
        public Size Size => new(Width, Height);
        public IntPtr Handle => IntPtr.Zero;

        public object Clone() => new Icon(ToBitmap());

        public override string ToString() => $"<Icon {Width}x{Height}>";

        public Bitmap ToBitmap()
        {
            if (_bitmap is not null)
                return (Bitmap)_bitmap.Clone();
            return new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
        }

        public static Icon? ExtractAssociatedIcon(string filePath) => null;

        public static Icon ExtractIcon(string file, int number, bool smallIcon) =>
            throw new NotSupportedException("GDI handles are not available on web.");

        public static Icon ExtractIcon(string file, int number, int version) =>
            throw new NotSupportedException("GDI handles are not available on web.");

        // Headless there are no GDI handles, but dialog/menu/history
        // chrome needs a non-null icon to keep building (effect config
        // dialogs die in ToIcon otherwise): answer a blank placeholder.
        public static Icon FromHandle(IntPtr handle) =>
            new(new Bitmap(16, 16));

        public void Save(Stream outputStream) =>
            throw new NotSupportedException("Image codecs arrive with the codec milestone.");

        ~Icon()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
