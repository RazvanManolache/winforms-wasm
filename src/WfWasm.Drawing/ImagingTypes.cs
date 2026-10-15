// Extended imaging types: exact .NET member shapes, web-compatible.
// Color adjustments are stored per ColorAdjustType; the CPU blitter applies
// the color matrix, the remaining adjustments wait for the imaging milestone.
namespace System.Drawing.Imaging
{
    /// <summary>5x5 color transform (cf. System.Drawing.Imaging.ColorMatrix).</summary>
    public sealed class ColorMatrix
    {
        private readonly float[,] _m = new float[5, 5];

        public ColorMatrix()
        {
            _m[0, 0] = _m[1, 1] = _m[2, 2] = _m[3, 3] = _m[4, 4] = 1f;
        }

        public ColorMatrix(float[][] newColorMatrix)
        {
            ArgumentNullException.ThrowIfNull(newColorMatrix);
            if (newColorMatrix.Length != 5 || Array.Exists(newColorMatrix, row => row is null || row.Length != 5))
                throw new ArgumentException("Color matrix must be 5x5.", nameof(newColorMatrix));
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                    _m[r, c] = newColorMatrix[r][c];
        }

        public ColorMatrix(ReadOnlySpan<float> newColorMatrix)
        {
            if (newColorMatrix.Length != 25)
                throw new ArgumentException("Color matrix span must hold 25 elements.", nameof(newColorMatrix));
            for (int i = 0; i < 25; i++)
                _m[i / 5, i % 5] = newColorMatrix[i];
        }

        public float this[int row, int column]
        {
            get => _m[row, column];
            set => _m[row, column] = value;
        }

        public float Matrix00 { get => _m[0, 0]; set => _m[0, 0] = value; }
        public float Matrix01 { get => _m[0, 1]; set => _m[0, 1] = value; }
        public float Matrix02 { get => _m[0, 2]; set => _m[0, 2] = value; }
        public float Matrix03 { get => _m[0, 3]; set => _m[0, 3] = value; }
        public float Matrix04 { get => _m[0, 4]; set => _m[0, 4] = value; }
        public float Matrix10 { get => _m[1, 0]; set => _m[1, 0] = value; }
        public float Matrix11 { get => _m[1, 1]; set => _m[1, 1] = value; }
        public float Matrix12 { get => _m[1, 2]; set => _m[1, 2] = value; }
        public float Matrix13 { get => _m[1, 3]; set => _m[1, 3] = value; }
        public float Matrix14 { get => _m[1, 4]; set => _m[1, 4] = value; }
        public float Matrix20 { get => _m[2, 0]; set => _m[2, 0] = value; }
        public float Matrix21 { get => _m[2, 1]; set => _m[2, 1] = value; }
        public float Matrix22 { get => _m[2, 2]; set => _m[2, 2] = value; }
        public float Matrix23 { get => _m[2, 3]; set => _m[2, 3] = value; }
        public float Matrix24 { get => _m[2, 4]; set => _m[2, 4] = value; }
        public float Matrix30 { get => _m[3, 0]; set => _m[3, 0] = value; }
        public float Matrix31 { get => _m[3, 1]; set => _m[3, 1] = value; }
        public float Matrix32 { get => _m[3, 2]; set => _m[3, 2] = value; }
        public float Matrix33 { get => _m[3, 3]; set => _m[3, 3] = value; }
        public float Matrix34 { get => _m[3, 4]; set => _m[3, 4] = value; }
        public float Matrix40 { get => _m[4, 0]; set => _m[4, 0] = value; }
        public float Matrix41 { get => _m[4, 1]; set => _m[4, 1] = value; }
        public float Matrix42 { get => _m[4, 2]; set => _m[4, 2] = value; }
        public float Matrix43 { get => _m[4, 3]; set => _m[4, 3] = value; }
        public float Matrix44 { get => _m[4, 4]; set => _m[4, 4] = value; }

        internal Color Apply(Color color)
        {
            float r = color.R / 255f, g = color.G / 255f, b = color.B / 255f, a = color.A / 255f;
            float nr = (r * _m[0, 0]) + (g * _m[1, 0]) + (b * _m[2, 0]) + (a * _m[3, 0]) + _m[4, 0];
            float ng = (r * _m[0, 1]) + (g * _m[1, 1]) + (b * _m[2, 1]) + (a * _m[3, 1]) + _m[4, 1];
            float nb = (r * _m[0, 2]) + (g * _m[1, 2]) + (b * _m[2, 2]) + (a * _m[3, 2]) + _m[4, 2];
            float na = (r * _m[0, 3]) + (g * _m[1, 3]) + (b * _m[2, 3]) + (a * _m[3, 3]) + _m[4, 3];
            return Color.FromArgb(Clamp(na * 255f), Clamp(nr * 255f), Clamp(ng * 255f), Clamp(nb * 255f));
        }

        private static int Clamp(float v) => v < 0f ? 0 : v > 255f ? 255 : (int)(v + 0.5f);
    }

    /// <summary>Single color remap entry (cf. System.Drawing.Imaging.ColorMap).</summary>
    public sealed class ColorMap
    {
        public Color OldColor { get; set; }
        public Color NewColor { get; set; }
    }

    /// <summary>Record callback (cf. System.Drawing.Imaging.PlayRecordCallback).</summary>
    public delegate void PlayRecordCallback(EmfPlusRecordType recordType, int flags, int dataSize, IntPtr recordData);

    /// <summary>Draw-time color adjustments (cf. System.Drawing.Imaging.ImageAttributes).</summary>
    public sealed class ImageAttributes : ICloneable, IDisposable
    {
        public ImageAttributes()
        {
        }

        public object Clone()
        {
            var copy = new ImageAttributes();
            foreach (var (type, slot) in _slots)
                copy._slots[type] = slot.Clone();
            return copy;
        }

        public void SetColorMatrix(ColorMatrix newColorMatrix) =>
            SetColorMatrix(newColorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Default);

        public void SetColorMatrix(ColorMatrix newColorMatrix, ColorMatrixFlag mode) =>
            SetColorMatrix(newColorMatrix, mode, ColorAdjustType.Default);

        public void SetColorMatrix(ColorMatrix newColorMatrix, ColorMatrixFlag mode, ColorAdjustType type)
        {
            ArgumentNullException.ThrowIfNull(newColorMatrix);
            GetSlot(type).ColorMatrix = newColorMatrix;
            GetSlot(type).MatrixFlags = mode;
        }

        public void ClearColorMatrix() => ClearColorMatrix(ColorAdjustType.Default);

        public void ClearColorMatrix(ColorAdjustType type) => GetSlot(type).ColorMatrix = null;

        public void SetColorMatrices(ColorMatrix newColorMatrix, ColorMatrix newGrayMatrix) =>
            SetColorMatrices(newColorMatrix, newGrayMatrix, ColorMatrixFlag.Default, ColorAdjustType.Default);

        public void SetColorMatrices(ColorMatrix newColorMatrix, ColorMatrix newGrayMatrix, ColorMatrixFlag mode) =>
            SetColorMatrices(newColorMatrix, newGrayMatrix, mode, ColorAdjustType.Default);

        public void SetColorMatrices(ColorMatrix newColorMatrix, ColorMatrix newGrayMatrix, ColorMatrixFlag mode, ColorAdjustType type)
        {
            ArgumentNullException.ThrowIfNull(newColorMatrix);
            ArgumentNullException.ThrowIfNull(newGrayMatrix);
            Slot s = GetSlot(type);
            s.ColorMatrix = newColorMatrix;
            s.GrayMatrix = newGrayMatrix;
            s.MatrixFlags = mode;
        }

        public void SetThreshold(float threshold) => SetThreshold(threshold, ColorAdjustType.Default);

        public void SetThreshold(float threshold, ColorAdjustType type)
        {
            if (threshold is < 0f or > 1f)
                throw new ArgumentException("Threshold must be between 0 and 1.", nameof(threshold));
            GetSlot(type).Threshold = threshold;
        }

        public void ClearThreshold() => ClearThreshold(ColorAdjustType.Default);

        public void ClearThreshold(ColorAdjustType type) => GetSlot(type).Threshold = null;

        public void SetGamma(float gamma) => SetGamma(gamma, ColorAdjustType.Default);

        public void SetGamma(float gamma, ColorAdjustType type)
        {
            if (gamma <= 0f)
                throw new ArgumentException("Gamma must be positive.", nameof(gamma));
            GetSlot(type).Gamma = gamma;
        }

        public void ClearGamma() => ClearGamma(ColorAdjustType.Default);

        public void ClearGamma(ColorAdjustType type) => GetSlot(type).Gamma = null;

        public void SetNoOp() => SetNoOp(ColorAdjustType.Default);

        public void SetNoOp(ColorAdjustType type) => GetSlot(type).NoOp = true;

        public void ClearNoOp() => ClearNoOp(ColorAdjustType.Default);

        public void ClearNoOp(ColorAdjustType type) => GetSlot(type).NoOp = false;

        public void SetColorKey(Color colorLow, Color colorHigh) =>
            SetColorKey(colorLow, colorHigh, ColorAdjustType.Default);

        public void SetColorKey(Color colorLow, Color colorHigh, ColorAdjustType type)
        {
            Slot s = GetSlot(type);
            s.ColorKeyLow = colorLow;
            s.ColorKeyHigh = colorHigh;
            s.HasColorKey = true;
        }

        public void ClearColorKey() => ClearColorKey(ColorAdjustType.Default);

        public void ClearColorKey(ColorAdjustType type) => GetSlot(type).HasColorKey = false;

        public void SetOutputChannel(ColorChannelFlag flags) =>
            SetOutputChannel(flags, ColorAdjustType.Default);

        public void SetOutputChannel(ColorChannelFlag flags, ColorAdjustType type) =>
            GetSlot(type).OutputChannel = flags;

        public void ClearOutputChannel() => ClearOutputChannel(ColorAdjustType.Default);

        public void ClearOutputChannel(ColorAdjustType type) => GetSlot(type).OutputChannel = null;

        public void SetOutputChannelColorProfile(string colorProfileFilename) =>
            SetOutputChannelColorProfile(colorProfileFilename, ColorAdjustType.Default);

        public void SetOutputChannelColorProfile(string colorProfileFilename, ColorAdjustType type)
        {
            ArgumentNullException.ThrowIfNull(colorProfileFilename);
            GetSlot(type).IccProfile = colorProfileFilename;
        }

        public void ClearOutputChannelColorProfile() =>
            ClearOutputChannelColorProfile(ColorAdjustType.Default);

        public void ClearOutputChannelColorProfile(ColorAdjustType type) =>
            GetSlot(type).IccProfile = null;

        public void SetRemapTable(ColorMap[] map) => SetRemapTable(map, ColorAdjustType.Default);

        public void SetRemapTable(ColorMap[] map, ColorAdjustType type)
        {
            ArgumentNullException.ThrowIfNull(map);
            GetSlot(type).RemapTable = (ColorMap[])map.Clone();
        }

        public void SetRemapTable(ReadOnlySpan<ColorMap> map) =>
            SetRemapTable(map.ToArray(), ColorAdjustType.Default);

        public void SetRemapTable(ReadOnlySpan<(Color Old, Color New)> map)
        {
            var table = new ColorMap[map.Length];
            for (int i = 0; i < map.Length; i++)
                table[i] = new ColorMap { OldColor = map[i].Old, NewColor = map[i].New };
            SetRemapTable(table, ColorAdjustType.Default);
        }

        public void SetRemapTable(ColorAdjustType type, ReadOnlySpan<ColorMap> map) =>
            SetRemapTable(map.ToArray(), type);

        public void SetRemapTable(ColorAdjustType type, ReadOnlySpan<(Color Old, Color New)> map)
        {
            var table = new ColorMap[map.Length];
            for (int i = 0; i < map.Length; i++)
                table[i] = new ColorMap { OldColor = map[i].Old, NewColor = map[i].New };
            SetRemapTable(table, type);
        }

        public void ClearRemapTable() => ClearRemapTable(ColorAdjustType.Default);

        public void ClearRemapTable(ColorAdjustType type) => GetSlot(type).RemapTable = null;

        public void SetBrushRemapTable(ColorMap[] map)
        {
            ArgumentNullException.ThrowIfNull(map);
            GetSlot(ColorAdjustType.Brush).BrushRemapTable = (ColorMap[])map.Clone();
        }

        public void SetBrushRemapTable(ReadOnlySpan<ColorMap> map) =>
            SetBrushRemapTable(map.ToArray());

        public void SetBrushRemapTable(ReadOnlySpan<(Color Old, Color New)> map)
        {
            var table = new ColorMap[map.Length];
            for (int i = 0; i < map.Length; i++)
                table[i] = new ColorMap { OldColor = map[i].Old, NewColor = map[i].New };
            SetBrushRemapTable(table);
        }

        public void ClearBrushRemapTable() => GetSlot(ColorAdjustType.Brush).BrushRemapTable = null;

        public void SetWrapMode(Drawing2D.WrapMode mode) =>
            GetSlot(ColorAdjustType.Default).WrapMode = mode;

        public void SetWrapMode(Drawing2D.WrapMode mode, Color color)
        {
            Slot s = GetSlot(ColorAdjustType.Default);
            s.WrapMode = mode;
            s.WrapColor = color;
        }

        public void SetWrapMode(Drawing2D.WrapMode mode, Color color, bool clampX)
        {
            Slot s = GetSlot(ColorAdjustType.Default);
            s.WrapMode = mode;
            s.WrapColor = color;
            s.WrapClamp = clampX;
        }

        public void GetAdjustedPalette(ColorPalette palette, ColorAdjustType type)
        {
            ArgumentNullException.ThrowIfNull(palette);
            if (_slots.TryGetValue(type, out Slot? s) && s.ColorMatrix is not null)
            {
                for (int i = 0; i < palette.Entries.Length; i++)
                    palette.Entries[i] = s.ColorMatrix.Apply(palette.Entries[i]);
            }
        }

        internal bool TryGetMatrix(ColorAdjustType type, out ColorMatrix? matrix)
        {
            matrix = _slots.TryGetValue(type, out Slot? s) ? s.ColorMatrix : null;
            return matrix is not null;
        }
        internal void ApplyDefaultTo(Bitmap bitmap)
        {
            if (!_slots.TryGetValue(ColorAdjustType.Default, out Slot? s) || s is null || s.NoOp)
                return;
            ColorMap[]? remap = s.RemapTable;
            ColorMatrix? matrix = s.ColorMatrix ?? s.GrayMatrix;
            float? gamma = s.Gamma;
            float? threshold = s.Threshold;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color c = bitmap.GetPixel(x, y);
                    if (remap is not null)
                    {
                        foreach (ColorMap m in remap)
                        {
                            if (c.ToArgb() == m.OldColor.ToArgb())
                            {
                                c = m.NewColor;
                                break;
                            }
                        }
                    }
                    if (matrix is not null)
                        c = matrix.Apply(c);
                    if (gamma.HasValue)
                    {
                        float g = Math.Max(0.01f, gamma.Value);
                        c = Color.FromArgb(c.A,
                            (int)(Math.Pow(c.R / 255f, g) * 255f),
                            (int)(Math.Pow(c.G / 255f, g) * 255f),
                            (int)(Math.Pow(c.B / 255f, g) * 255f));
                    }
                    if (threshold.HasValue)
                    {
                        float t = Math.Clamp(threshold.Value, 0f, 1f) * 255f;
                        c = Color.FromArgb(c.A,
                            c.R < t ? 0 : 255,
                            c.G < t ? 0 : 255,
                            c.B < t ? 0 : 255);
                    }
                    if (s.HasColorKey
                        && c.R >= s.ColorKeyLow.R && c.R <= s.ColorKeyHigh.R
                        && c.G >= s.ColorKeyLow.G && c.G <= s.ColorKeyHigh.G
                        && c.B >= s.ColorKeyLow.B && c.B <= s.ColorKeyHigh.B)
                    {
                        c = Color.FromArgb(0, c.R, c.G, c.B);
                    }
                    if (s.OutputChannel.HasValue)
                        c = ApplyOutputChannel(c, s.OutputChannel.Value);
                    bitmap.SetPixel(x, y, c);
                }
            }
        }

        internal static Color ApplyOutputChannel(Color c, ColorChannelFlag channel) => channel switch
        {
            ColorChannelFlag.ColorChannelC => Color.FromArgb(c.A, c.R, c.R, c.R),
            ColorChannelFlag.ColorChannelM => Color.FromArgb(c.A, c.G, c.G, c.G),
            ColorChannelFlag.ColorChannelY => Color.FromArgb(c.A, c.B, c.B, c.B),
            ColorChannelFlag.ColorChannelK => Color.FromArgb(c.A, 255 - Math.Max(c.R, Math.Max(c.G, c.B)), 255 - Math.Max(c.R, Math.Max(c.G, c.B)), 255 - Math.Max(c.R, Math.Max(c.G, c.B))),
            _ => c,
        };

        ~ImageAttributes()
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

        private Slot GetSlot(ColorAdjustType type)
        {
            if (!_slots.TryGetValue(type, out Slot? s))
                _slots[type] = s = new Slot();
            return s;
        }

        private readonly Dictionary<ColorAdjustType, Slot> _slots = new();

        private sealed class Slot
        {
            public ColorMatrix? ColorMatrix;
            public ColorMatrix? GrayMatrix;
            public ColorMatrixFlag MatrixFlags;
            public float? Threshold;
            public float? Gamma;
            public bool NoOp;
            public Color ColorKeyLow;
            public Color ColorKeyHigh;
            public bool HasColorKey;
            public ColorChannelFlag? OutputChannel;
            public string? IccProfile;
            public ColorMap[]? RemapTable;
            public ColorMap[]? BrushRemapTable;
            public Drawing2D.WrapMode WrapMode;
            public Color WrapColor;
            public bool WrapClamp;

            public Slot Clone() => (Slot)MemberwiseClone();
        }
    }

    /// <summary>Device-dependent bitmap copy (cf. System.Drawing.Imaging.CachedBitmap).</summary>
    public sealed class CachedBitmap : IDisposable
    {
        public CachedBitmap(Bitmap bitmap, Graphics graphics)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(graphics);
            Bitmap = (Bitmap)bitmap.Clone();
        }

        public IntPtr Handle => IntPtr.Zero;

        internal Bitmap Bitmap { get; }

        ~CachedBitmap()
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

    /// <summary>Vector metafile header (cf. System.Drawing.Imaging.MetaHeader).</summary>
    public sealed class MetaHeader
    {
        public MetaHeader()
        {
        }

        public short Type { get; set; }
        public short HeaderSize { get; set; }
        public short Version { get; set; }
        public int Size { get; set; }
        public short NoObjects { get; set; }
        public int MaxRecord { get; set; }
        public short NoParameters { get; set; }
    }

    /// <summary>Placeable WMF header (cf. System.Drawing.Imaging.WmfPlaceableFileHeader).</summary>
    public sealed class WmfPlaceableFileHeader
    {
        public WmfPlaceableFileHeader()
        {
        }

        public int Key { get; set; }
        public short Hmf { get; set; }
        public short BboxLeft { get; set; }
        public short BboxTop { get; set; }
        public short BboxRight { get; set; }
        public short BboxBottom { get; set; }
        public short Inch { get; set; }
        public int Reserved { get; set; }
        public short Checksum { get; set; }
    }

    /// <summary>Metafile summary (cf. System.Drawing.Imaging.MetafileHeader).</summary>
    public sealed class MetafileHeader
    {
        internal MetafileHeader()
        {
        }

        public bool IsWmf() => false;
        public bool IsWmfPlaceable() => false;
        public bool IsEmf() => false;
        public bool IsEmfOrEmfPlus() => false;
        public bool IsEmfPlus() => false;
        public bool IsEmfPlusDual() => false;
        public bool IsEmfPlusOnly() => false;
        public bool IsDisplay() => false;

        public MetafileType Type { get; }
        public int MetafileSize { get; }
        public int Version { get; }
        public float DpiX { get; }
        public float DpiY { get; }
        public Rectangle Bounds { get; }
        public MetaHeader? WmfHeader { get; }
        public int EmfPlusHeaderSize { get; }
        public int LogicalDpiX { get; }
        public int LogicalDpiY { get; }
    }

    /// <summary>Vector metafile (cf. System.Drawing.Imaging.Metafile).
    /// No metafile engine exists on web: every constructor throws.</summary>
    public sealed class Metafile : Image
    {
        private Metafile()
        {
            throw new NotSupportedException("Metafiles are not supported on web.");
        }

        public override int Width => 0;
        public override int Height => 0;
        public override object Clone() =>
            throw new NotSupportedException("Metafiles are not supported on web.");

        public Metafile(IntPtr hmetafile, WmfPlaceableFileHeader wmfHeader, bool deleteWmf)
            : this()
        {
        }

        public Metafile(IntPtr henhmetafile, bool deleteEmf)
            : this()
        {
        }

        public Metafile(string filename)
            : this()
        {
        }

        public Metafile(IntPtr referenceHdc, Rectangle frameRect)
            : this()
        {
        }

        public Metafile(IntPtr referenceHdc, EmfType emfType)
            : this()
        {
        }

        public Metafile(IntPtr referenceHdc, RectangleF frameRect)
            : this()
        {
        }

        public Metafile(IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit)
            : this()
        {
        }

        public Metafile(IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type)
            : this()
        {
        }

        public Metafile(IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type, string description)
            : this()
        {
        }

        public Metafile(IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit)
            : this()
        {
        }

        public Metafile(IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, EmfType type)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, RectangleF frameRect)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, string desc)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type, string description)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, Rectangle frameRect)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, string description)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc, EmfType type)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc, RectangleF frameRect)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc, Rectangle frameRect)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type)
            : this()
        {
        }

        public Metafile(IntPtr hmetafile, WmfPlaceableFileHeader wmfHeader)
            : this()
        {
        }

        public Metafile(Stream stream)
            : this()
        {
        }

        public Metafile(IntPtr referenceHdc, EmfType emfType, string description)
            : this()
        {
        }

        public Metafile(IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type, string desc)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, EmfType type, string description)
            : this()
        {
        }

        public Metafile(string fileName, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type, string description)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc, EmfType type, string description)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type, string description)
            : this()
        {
        }

        public Metafile(Stream stream, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type, string description)
            : this()
        {
        }

        public void PlayRecord(EmfPlusRecordType recordType, int flags, int dataSize, byte[] data) =>
            throw new NotSupportedException("Metafiles are not supported on web.");

        public IntPtr GetHenhmetafile() => IntPtr.Zero;

        public MetafileHeader GetMetafileHeader() => new();

        public static MetafileHeader GetMetafileHeader(IntPtr henhmetafile) =>
            throw new NotSupportedException("Metafiles are not supported on web.");

        public static MetafileHeader GetMetafileHeader(IntPtr henhmetafile, WmfPlaceableFileHeader wmfHeader) =>
            throw new NotSupportedException("Metafiles are not supported on web.");

        public static MetafileHeader GetMetafileHeader(string fileName) =>
            throw new NotSupportedException("Metafiles are not supported on web.");

        public static MetafileHeader GetMetafileHeader(Stream stream) =>
            throw new NotSupportedException("Metafiles are not supported on web.");
    }
}

