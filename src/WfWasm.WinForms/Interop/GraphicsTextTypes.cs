// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Windows.Win32.Graphics.Gdi
{
[Flags]
internal enum DRAW_TEXT_FORMAT : uint
{
    DT_TOP = 0x00000000,
    DT_LEFT = 0x00000000,
    DT_CENTER = 0x00000001,
    DT_RIGHT = 0x00000002,
    DT_VCENTER = 0x00000004,
    DT_BOTTOM = 0x00000008,
    DT_WORDBREAK = 0x00000010,
    DT_SINGLELINE = 0x00000020,
    DT_EXPANDTABS = 0x00000040,
    DT_TABSTOP = 0x00000080,
    DT_NOCLIP = 0x00000100,
    DT_EXTERNALLEADING = 0x00000200,
    DT_CALCRECT = 0x00000400,
    DT_NOPREFIX = 0x00000800,
    DT_INTERNAL = 0x00001000,
    DT_EDITCONTROL = 0x00002000,
    DT_PATH_ELLIPSIS = 0x00004000,
    DT_END_ELLIPSIS = 0x00008000,
    DT_MODIFYSTRING = 0x00010000,
    DT_RTLREADING = 0x00020000,
    DT_WORD_ELLIPSIS = 0x00040000,
    DT_NOFULLWIDTHCHARBREAK = 0x00080000,
    DT_HIDEPREFIX = 0x00100000,
    DT_PREFIXONLY = 0x00200000,
}

[StructLayout(LayoutKind.Sequential)]
internal struct DRAWTEXTPARAMS
{
    public uint cbSize;
    public int iTabLength;
    public int iLeftMargin;
    public int iRightMargin;
    public uint uiLengthDrawn;
}

internal enum ROP_CODE : uint
{
    SRCCOPY = 0x00CC0020,
    SRCPAINT = 0x00EE0086,
    SRCAND = 0x008800C6,
    SRCINVERT = 0x00660046,
    SRCERASE = 0x00440328,
    NOTSRCCOPY = 0x00330008,
    NOTSRCERASE = 0x001100A6,
    MERGECOPY = 0x00C000CA,
    MERGEPAINT = 0x00BB0226,
    PATCOPY = 0x00F00021,
    PATPAINT = 0x00FB0A09,
    PATINVERT = 0x005A0049,
    DSTINVERT = 0x00550009,
    BLACKNESS = 0x00000042,
    WHITENESS = 0x00FF0062,
    CAPTUREBLT = 0x40000000,
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe partial struct LOGFONTW
{
    public int lfHeight;
    public int lfWidth;
    public int lfEscapement;
    public int lfOrientation;
    public int lfWeight;
    public byte lfItalic;
    public byte lfUnderline;
    public byte lfStrikeOut;
    public FONT_CHARSET lfCharSet;
    public FONT_OUTPUT_PRECISION lfOutPrecision;
    public byte lfClipPrecision;
    public FONT_QUALITY lfQuality;
    public byte lfPitchAndFamily;
    public _lfFaceName_e__FixedBuffer lfFaceName;

    [global::System.Runtime.CompilerServices.InlineArray(32)]
    public struct _lfFaceName_e__FixedBuffer
    {
        public char e0;

        public Span<char> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref e0, 32);
    }
}
}

namespace Windows.Win32.Graphics.Printing
{
    internal readonly struct PRINTER_HANDLE : IEquatable<PRINTER_HANDLE>
    {
        public readonly nint Value;
        public PRINTER_HANDLE(nint value) => Value = value;
        public static PRINTER_HANDLE Null => default;
        public bool IsNull => Value == 0;
        public static implicit operator nint(PRINTER_HANDLE value) => value.Value;
        public static explicit operator PRINTER_HANDLE(nint value) => new(value);
        public static bool operator ==(PRINTER_HANDLE left, PRINTER_HANDLE right) => left.Value == right.Value;
        public static bool operator !=(PRINTER_HANDLE left, PRINTER_HANDLE right) => left.Value != right.Value;
        public override bool Equals(object? obj) => obj is PRINTER_HANDLE other && Equals(other);
        public bool Equals(PRINTER_HANDLE other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }
}
