// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Windows.Win32.Graphics.Gdi
{
internal enum FONT_QUALITY : byte
{
    DEFAULT_QUALITY = 0,
    DRAFT_QUALITY = 1,
    PROOF_QUALITY = 2,
    NONANTIALIASED_QUALITY = 3,
    ANTIALIASED_QUALITY = 4,
    CLEARTYPE_QUALITY = 5,
    CLEARTYPE_NATURAL_QUALITY = 6,
}

internal enum HDC_MAP_MODE
{
    MM_TEXT = 1,
    MM_LOMETRIC = 2,
    MM_HIMETRIC = 3,
    MM_LOENGLISH = 4,
    MM_HIENGLISH = 5,
    MM_TWIPS = 6,
    MM_ISOTROPIC = 7,
    MM_ANISOTROPIC = 8,
}

internal enum GDI_REGION_TYPE
{
    ERROR = 0,
    RGN_ERROR = 0,
    NULLREGION = 1,
    SIMPLEREGION = 2,
    COMPLEXREGION = 3,
}

[Flags]
internal enum GET_DCX_FLAGS : uint
{
    DCX_WINDOW = 0x00000001,
    DCX_CACHE = 0x00000002,
    DCX_NORESETATTRS = 0x00000004,
    DCX_CLIPCHILDREN = 0x00000008,
    DCX_CLIPSIBLINGS = 0x00000010,
    DCX_PARENTCLIP = 0x00000020,
    DCX_EXCLUDERGN = 0x00000040,
    DCX_INTERSECTRGN = 0x00000080,
    DCX_EXCLUDEUPDATE = 0x00000100,
    DCX_INTERSECTUPDATE = 0x00000200,
    DCX_LOCKWINDOWUPDATE = 0x00000400,
    DCX_VALIDATE = 0x00200000,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PALETTEENTRY
{
    public byte peRed;
    public byte peGreen;
    public byte peBlue;
    public byte peFlags;
}

[Flags]
public enum ETO_OPTIONS : uint
{
    ETO_OPAQUE = 0x0002,
    ETO_CLIPPED = 0x0004,
    ETO_GLYPH_INDEX = 0x0010,
    ETO_RTLREADING = 0x0080,
    ETO_NUMERICSLOCAL = 0x0400,
    ETO_NUMERICSLATIN = 0x0800,
    ETO_IGNORELANGUAGE = 0x1000,
    ETO_PDY = 0x2000,
    ETO_REVERSE_INDEX_MAP = 0x10000,
}

internal readonly unsafe partial struct HMONITOR : IEquatable<HMONITOR>
{
    public readonly nint Value;
    public HMONITOR(nint value) => Value = value;
    public static explicit operator HMONITOR(nint value) => new(value);
    public static implicit operator nint(HMONITOR value) => value.Value;
    public static explicit operator HMONITOR(void* value) => new((nint)value);
    public static implicit operator void*(HMONITOR value) => (void*)value.Value;
    public static readonly HMONITOR Null = default;
    public bool IsNull => Value == 0;
    public bool Equals(HMONITOR other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is HMONITOR other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(HMONITOR left, HMONITOR right) => left.Value == right.Value;
    public static bool operator !=(HMONITOR left, HMONITOR right) => !left.Equals(right);
}

public enum BRUSH_STYLE : uint
{
    BS_SOLID = 0,
    BS_NULL = 1,
    BS_HOLLOW = BS_NULL,
    BS_HATCHED = 2,
    BS_PATTERN = 3,
    BS_INDEXED = 4,
    BS_DIBPATTERN = 5,
    BS_DIBPATTERNPT = 6,
    BS_PATTERN8X8 = 7,
    BS_DIBPATTERN8X8 = 8,
    BS_MONOPATTERN = 9,
}

public enum PEN_STYLE : uint
{
    PS_SOLID = 0,
    PS_DASH = 1,
    PS_DOT = 2,
    PS_DASHDOT = 3,
    PS_DASHDOTDOT = 4,
    PS_NULL = 5,
    PS_INSIDEFRAME = 6,
    PS_USERSTYLE = 7,
    PS_ALTERNATE = 8,
}

[StructLayout(LayoutKind.Sequential)]
internal struct LOGBRUSH
{
    public BRUSH_STYLE lbStyle;
    public COLORREF lbColor;
    public nuint lbHatch;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XFORM
{
    public float eM11;
    public float eM12;
    public float eM21;
    public float eM22;
    public float eDx;
    public float eDy;
}

public enum XFORMCOORDS : uint
{
    XFORMCOORDS_POSITION = 0x1,
    XFORMCOORDS_SIZE = 0x2,
    XFORMCOORDS_HIMETRICTOCONTAINER = 0x4,
    XFORMCOORDS_CONTAINERTOHIMETRIC = 0x8,
    XFORMCOORDS_EVENTCOMPAT = 0x10,
}
}

namespace Windows.Win32.UI.HiDpi
{
    public enum PROCESS_DPI_AWARENESS
    {
        PROCESS_DPI_UNAWARE = 0,
        PROCESS_SYSTEM_DPI_AWARE = 1,
        PROCESS_PER_MONITOR_DPI_AWARE = 2,
    }
}

namespace Windows.Win32.Graphics.Gdi
{
    [Flags]
    public enum DEVMODE_FIELD_FLAGS : uint
    {
        DM_ORIENTATION = 0x00000001,
        DM_PAPERSIZE = 0x00000002,
        DM_PAPERLENGTH = 0x00000004,
        DM_PAPERWIDTH = 0x00000008,
        DM_SCALE = 0x00000010,
        DM_POSITION = 0x00000020,
        DM_NUP = 0x00000040,
        DM_DISPLAYORIENTATION = 0x00000080,
        DM_COPIES = 0x00000100,
        DM_DEFAULTSOURCE = 0x00000200,
        DM_PRINTQUALITY = 0x00000400,
        DM_COLOR = 0x00000800,
        DM_DUPLEX = 0x00001000,
        DM_YRESOLUTION = 0x00002000,
        DM_TTOPTION = 0x00004000,
        DM_COLLATE = 0x00008000,
        DM_FORMNAME = 0x00010000,
        DM_LOGPIXELS = 0x00020000,
        DM_BITSPERPEL = 0x00040000,
        DM_PELSWIDTH = 0x00080000,
        DM_PELSHEIGHT = 0x00100000,
        DM_DISPLAYFLAGS = 0x00200000,
        DM_DISPLAYFREQUENCY = 0x00400000,
        DM_ICMMETHOD = 0x00800000,
        DM_ICMINTENT = 0x01000000,
        DM_MEDIATYPE = 0x02000000,
        DM_DITHERTYPE = 0x04000000,
        DM_PANNINGWIDTH = 0x08000000,
        DM_PANNINGHEIGHT = 0x10000000,
        DM_DISPLAYFIXEDOUTPUT = 0x20000000,
        DM_OUT_BUFFER = 0x00000002,
    }

    public enum ENUM_DISPLAY_SETTINGS_MODE : uint
    {
        ENUM_CURRENT_SETTINGS = unchecked((uint)-1),
        ENUM_REGISTRY_SETTINGS = unchecked((uint)-2),
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    internal unsafe partial struct DEVMODEW
    {
        [FieldOffset(0)]
        public fixed char dmDeviceName[32];
        [FieldOffset(64)]
        public ushort dmSpecVersion;
        [FieldOffset(66)]
        public ushort dmDriverVersion;
        [FieldOffset(68)]
        public ushort dmSize;
        [FieldOffset(70)]
        public ushort dmDriverExtra;
        [FieldOffset(72)]
        public DEVMODE_FIELD_FLAGS dmFields;

        [FieldOffset(76)]
        public _Anonymous1_e__Union Anonymous1;

        [FieldOffset(92)]
        public short dmColor;
        [FieldOffset(94)]
        public short dmDuplex;
        [FieldOffset(96)]
        public short dmYResolution;
        [FieldOffset(98)]
        public short dmTTOption;
        [FieldOffset(100)]
        public short dmCollate;
        [FieldOffset(102)]
        public fixed char dmFormName[32];
        [FieldOffset(166)]
        public ushort dmLogPixels;
        [FieldOffset(168)]
        public uint dmBitsPerPel;
        [FieldOffset(172)]
        public uint dmPelsWidth;
        [FieldOffset(176)]
        public uint dmPelsHeight;

        [FieldOffset(180)]
        public _Anonymous2_e__Union Anonymous2;

        [FieldOffset(184)]
        public uint dmDisplayFrequency;
        [FieldOffset(188)]
        public uint dmICMMethod;
        [FieldOffset(192)]
        public uint dmICMIntent;
        [FieldOffset(196)]
        public uint dmMediaType;
        [FieldOffset(200)]
        public uint dmDitherType;
        [FieldOffset(204)]
        public uint dmReserved1;
        [FieldOffset(208)]
        public uint dmReserved2;
        [FieldOffset(212)]
        public uint dmPanningWidth;
        [FieldOffset(216)]
        public uint dmPanningHeight;

        [StructLayout(LayoutKind.Explicit)]
        public struct _Anonymous1_e__Union
        {
            [FieldOffset(0)]
            public _Anonymous1_e__Struct Anonymous1;
            [FieldOffset(0)]
            public _Anonymous2_e__Struct Anonymous2;

            [StructLayout(LayoutKind.Sequential)]
            public struct _Anonymous1_e__Struct
            {
                public short dmOrientation;
                public short dmPaperSize;
                public short dmPaperLength;
                public short dmPaperWidth;
                public short dmScale;
                public short dmCopies;
                public short dmDefaultSource;
                public short dmPrintQuality;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct _Anonymous2_e__Struct
            {
                public POINTL dmPosition;
                public uint dmDisplayOrientation;
                public uint dmDisplayFixedOutput;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct _Anonymous2_e__Union
        {
            [FieldOffset(0)]
            public uint dmDisplayFlags;
            [FieldOffset(0)]
            public uint dmNup;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public void* bmBits;
    }

    internal enum BI_COMPRESSION : uint
    {
        BI_RGB = 0,
        BI_RLE8 = 1,
        BI_RLE4 = 2,
        BI_BITFIELDS = 3,
        BI_JPEG = 4,
        BI_PNG = 5,
    }

    internal enum FONT_CHARSET : byte
    {
        ANSI_CHARSET = 0,
        DEFAULT_CHARSET = 1,
        SYMBOL_CHARSET = 2,
        SHIFTJIS_CHARSET = 128,
        HANGEUL_CHARSET = 129,
        HANGUL_CHARSET = 129,
        GB2312_CHARSET = 134,
        CHINESEBIG5_CHARSET = 136,
        OEM_CHARSET = 255,
        JOHAB_CHARSET = 130,
        HEBREW_CHARSET = 177,
        ARABIC_CHARSET = 178,
        GREEK_CHARSET = 161,
        TURKISH_CHARSET = 162,
        VIETNAMESE_CHARSET = 163,
        THAI_CHARSET = 222,
        EASTEUROPE_CHARSET = 238,
        RUSSIAN_CHARSET = 204,
        MAC_CHARSET = 77,
        BALTIC_CHARSET = 186,
    }

    internal enum FONT_OUTPUT_PRECISION : byte
    {
        OUT_DEFAULT_PRECIS = 0,
        OUT_STRING_PRECIS = 1,
        OUT_CHARACTER_PRECIS = 2,
        OUT_STROKE_PRECIS = 3,
        OUT_TT_PRECIS = 4,
        OUT_DEVICE_PRECIS = 5,
        OUT_RASTER_PRECIS = 6,
        OUT_TT_ONLY_PRECIS = 7,
        OUT_OUTLINE_PRECIS = 8,
        OUT_SCREEN_OUTLINE_PRECIS = 9,
        OUT_PS_ONLY_PRECIS = 10,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MONITORINFOEXW
    {
        public MONITORINFO monitorInfo;
        public __char_32 szDevice;

        public unsafe struct __char_32
        {
            public fixed char _value[32];
            public override string ToString()
            {
                fixed (char* p = _value)
                    return new string(p);
            }
        }
    }
}

