// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Windows.Win32.Foundation
{
    internal readonly unsafe struct PSTR : IEquatable<PSTR>
    {
        public readonly byte* Value;
        public PSTR(byte* value) => Value = value;
        public static implicit operator byte*(PSTR value) => value.Value;
        public static explicit operator PSTR(byte* value) => new(value);
        public static explicit operator PSTR(nint value) => new((byte*)value);
        public static explicit operator nint(PSTR value) => (nint)value.Value;
        public bool Equals(PSTR other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is PSTR other && Equals(other);
        public override int GetHashCode() => unchecked((int)(long)Value);
        public static bool operator ==(PSTR left, PSTR right) => left.Value == right.Value;
        public static bool operator !=(PSTR left, PSTR right) => left.Value != right.Value;
        public override string? ToString() => Value == null ? null : Marshal.PtrToStringAnsi((nint)Value);
    }

    internal readonly unsafe struct PCSTR : IEquatable<PCSTR>
    {
        public readonly byte* Value;
        public PCSTR(byte* value) => Value = value;
        public static implicit operator byte*(PCSTR value) => value.Value;
        public static explicit operator PCSTR(byte* value) => new(value);
        public static explicit operator PCSTR(nint value) => new((byte*)value);
        public static explicit operator nint(PCSTR value) => (nint)value.Value;
        public bool Equals(PCSTR other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is PCSTR other && Equals(other);
        public override int GetHashCode() => unchecked((int)(long)Value);
        public static bool operator ==(PCSTR left, PCSTR right) => left.Value == right.Value;
        public static bool operator !=(PCSTR left, PCSTR right) => left.Value != right.Value;
        public override string? ToString() => Value == null ? null : Marshal.PtrToStringAnsi((nint)Value);
    }
}

namespace Windows.Win32.Graphics.Gdi
{
    internal enum GET_DEVICE_CAPS_INDEX : int
    {
        DRIVERVERSION = 0,
        TECHNOLOGY = 2,
        HORZSIZE = 4,
        VERTSIZE = 6,
        HORZRES = 8,
        VERTRES = 10,
        BITSPIXEL = 12,
        PLANES = 14,
        NUMBRUSHES = 16,
        NUMPENS = 18,
        NUMMARKERS = 20,
        NUMFONTS = 22,
        NUMCOLORS = 24,
        PDEVICESIZE = 26,
        CURVECAPS = 28,
        LINECAPS = 30,
        POLYGONCAPS = 32,
        TEXTCAPS = 34,
        CLIPCAPS = 36,
        RASTERCAPS = 38,
        ASPECTX = 40,
        ASPECTY = 42,
        ASPECTXY = 44,
        LOGPIXELSX = 88,
        LOGPIXELSY = 90,
        SIZEPALETTE = 104,
        NUMRESERVED = 106,
        COLORRES = 108,
        PHYSICALWIDTH = 110,
        PHYSICALHEIGHT = 111,
        PHYSICALOFFSETX = 112,
        PHYSICALOFFSETY = 113,
        SCALINGFACTORX = 114,
        SCALINGFACTORY = 115,
        VREFRESH = 116,
        DESKTOPVERTRES = 117,
        DESKTOPHORZRES = 118,
        BLITALIGNMENT = 119,
        SHADEBLENDCAPS = 120,
        COLORMGMTCAPS = 121,
    }

    internal enum GET_STOCK_OBJECT_FLAGS : int
    {
        WHITE_BRUSH = 0,
        LTGRAY_BRUSH = 1,
        GRAY_BRUSH = 2,
        DKGRAY_BRUSH = 3,
        BLACK_BRUSH = 4,
        NULL_BRUSH = 5,
        HOLLOW_BRUSH = 5,
        WHITE_PEN = 6,
        BLACK_PEN = 7,
        NULL_PEN = 8,
        OEM_FIXED_FONT = 10,
        ANSI_FIXED_FONT = 11,
        ANSI_VAR_FONT = 12,
        SYSTEM_FONT = 13,
        DEVICE_DEFAULT_FONT = 14,
        DEFAULT_PALETTE = 15,
        SYSTEM_FIXED_FONT = 16,
        DEFAULT_GUI_FONT = 17,
        DC_BRUSH = 18,
        DC_PEN = 19,
    }

    internal enum OBJ_TYPE : int
    {
        OBJ_PEN = 1,
        OBJ_BRUSH = 2,
        OBJ_DC = 3,
        OBJ_METADC = 4,
        OBJ_PAL = 5,
        OBJ_FONT = 6,
        OBJ_BITMAP = 7,
        OBJ_REGION = 8,
        OBJ_METAFILE = 9,
        OBJ_MEMDC = 10,
        OBJ_EXTPEN = 11,
        OBJ_ENHMETADC = 12,
        OBJ_ENHMETAFILE = 13,
        OBJ_COLORSPACE = 14,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal partial struct RGNDATAHEADER
    {
        public uint dwSize;
        public uint iType;
        public uint nCount;
        public uint nRgnSize;
        public RECT rcBound;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct RGNDATA
    {
        public RGNDATAHEADER rdh;
        public fixed byte Buffer[1];
    }

    internal enum DIB_USAGE : uint
    {
        DIB_RGB_COLORS = 0,
        DIB_PAL_COLORS = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RGBQUAD
    {
        public byte rgbBlue;
        public byte rgbGreen;
        public byte rgbRed;
        public byte rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public RGBQUAD bmiColors;
    }

    internal enum RGN_COMBINE_MODE : int
    {
        RGN_AND = 1,
        RGN_OR = 2,
        RGN_XOR = 3,
        RGN_DIFF = 4,
        RGN_COPY = 5,
    }

    internal enum GDI_IMAGE_TYPE : uint
    {
        IMAGE_BITMAP = 0,
        IMAGE_ICON = 1,
        IMAGE_CURSOR = 2,
        IMAGE_ENHMETAFILE = 3,
    }

    [Flags]
    internal enum MONITOR_FROM_FLAGS : uint
    {
        MONITOR_DEFAULTTONULL = 0x00000000,
        MONITOR_DEFAULTTOPRIMARY = 0x00000001,
        MONITOR_DEFAULTTONEAREST = 0x00000002,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}

namespace Windows.Win32.System.Ole
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct PICTDESC
    {
        public uint cbSizeofstruct;
        public uint picType;
        public void* dummy;
    }
}

namespace Windows.Win32.System.Com
{
    [Flags]
    internal enum CLSCTX : uint
    {
        CLSCTX_INPROC_SERVER = 0x1,
        CLSCTX_INPROC_HANDLER = 0x2,
        CLSCTX_LOCAL_SERVER = 0x4,
        CLSCTX_INPROC_SERVER16 = 0x8,
        CLSCTX_REMOTE_SERVER = 0x10,
        CLSCTX_INPROC_HANDLER16 = 0x20,
        CLSCTX_ALL = CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER,
    }
}

namespace Windows.Win32.System.Com.StructuredStorage
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public void* dummy;
    }
}
