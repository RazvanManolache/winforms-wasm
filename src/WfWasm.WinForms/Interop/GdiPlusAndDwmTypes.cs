// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Windows.Win32.Graphics.Gdi
{
    [Flags]
    internal enum DRAW_EDGE_FLAGS : uint
    {
        BF_LEFT = 0x0001,
        BF_TOP = 0x0002,
        BF_RIGHT = 0x0004,
        BF_BOTTOM = 0x0008,
        BF_TOPLEFT = BF_TOP | BF_LEFT,
        BF_TOPRIGHT = BF_TOP | BF_RIGHT,
        BF_BOTTOMLEFT = BF_BOTTOM | BF_LEFT,
        BF_BOTTOMRIGHT = BF_BOTTOM | BF_RIGHT,
        BF_RECT = BF_LEFT | BF_TOP | BF_RIGHT | BF_BOTTOM,
        BF_DIAGONAL = 0x0010,
        BF_MIDDLE = 0x0800,
        BF_SOFT = 0x1000,
        BF_ADJUST = 0x2000,
        BF_FLAT = 0x4000,
        BF_MONO = 0x8000,
    }

    [Flags]
    internal enum DRAWEDGE_FLAGS : uint
    {
        BDR_RAISEDOUTER = 0x0001,
        BDR_SUNKENOUTER = 0x0002,
        BDR_RAISEDINNER = 0x0004,
        BDR_SUNKENINNER = 0x0008,
        EDGE_RAISED = BDR_RAISEDOUTER | BDR_RAISEDINNER,
        EDGE_SUNKEN = BDR_SUNKENOUTER | BDR_SUNKENINNER,
        EDGE_ETCHED = BDR_SUNKENOUTER | BDR_RAISEDINNER,
        EDGE_BUMP = BDR_RAISEDOUTER | BDR_SUNKENINNER,
    }

    [Flags]
    internal enum TMPF_FLAGS : byte
    {
        TMPF_FIXED_PITCH = 0x01,
        TMPF_VECTOR = 0x02,
        TMPF_DEVICE = 0x08,
        TMPF_TRUETYPE = 0x04,
    }

    internal enum BACKGROUND_MODE : uint
    {
        TRANSPARENT = 1,
        OPAQUE = 2,
        BKMODE_LAST = 2,
    }

    internal enum R2_MODE : uint
    {
        R2_BLACK = 1,
        R2_NOTMERGEPEN = 2,
        R2_MASKNOTPEN = 3,
        R2_NOTCOPYPEN = 4,
        R2_MASKPENNOT = 5,
        R2_NOT = 6,
        R2_XORPEN = 7,
        R2_NOTMASKPEN = 8,
        R2_MASKPEN = 9,
        R2_NOTXORPEN = 10,
        R2_NOP = 11,
        R2_MERGENOTPEN = 12,
        R2_COPYPEN = 13,
        R2_MERGEPENNOT = 14,
        R2_MERGEPEN = 15,
        R2_WHITE = 16,
        R2_LAST = 16,
    }

    [Flags]
    internal enum TEXT_ALIGN_OPTIONS : uint
    {
        TA_NOUPDATECP = 0,
        TA_UPDATECP = 1,
        TA_LEFT = 0,
        TA_RIGHT = 2,
        TA_CENTER = 6,
        TA_TOP = 0,
        TA_BOTTOM = 8,
        TA_BASELINE = 24,
        TA_RTLREADING = 256,
        TA_MASK = TA_BASELINE + TA_CENTER + TA_UPDATECP + TA_RTLREADING,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TEXTMETRICW
    {
        public int tmHeight;
        public int tmAscent;
        public int tmDescent;
        public int tmInternalLeading;
        public int tmExternalLeading;
        public int tmAveCharWidth;
        public int tmMaxCharWidth;
        public int tmWeight;
        public int tmOverhang;
        public int tmDigitizedAspectX;
        public int tmDigitizedAspectY;
        public char tmFirstChar;
        public char tmLastChar;
        public char tmDefaultChar;
        public char tmBreakChar;
        public byte tmItalic;
        public byte tmUnderlined;
        public byte tmStruckOut;
        public TMPF_FLAGS tmPitchAndFamily;
        public byte tmCharSet;
    }
}

namespace Windows.Win32.Graphics.Dwm
{
    internal enum DWM_WINDOW_CORNER_PREFERENCE
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3,
    }
}

namespace Windows.Win32.Graphics.GdiPlus
{
    internal enum Status
    {
        Ok = 0,
        GenericError = 1,
        InvalidParameter = 2,
        OutOfMemory = 3,
        ObjectBusy = 4,
        InsufficientBuffer = 5,
        NotImplemented = 6,
        Win32Error = 7,
        WrongState = 8,
        Aborted = 9,
        FileNotFound = 10,
        ValueOverflow = 11,
        AccessDenied = 12,
        UnknownImageFormat = 13,
        PropertyNotFound = 14,
        PropertyNotSupported = 15,
        FontFamilyNotFound = 16,
        FontStyleNotFound = 17,
        NotTrueTypeFont = 18,
        UnsupportedGdiplusVersion = 19,
        GdiplusNotInitialized = 20,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public void* DebugEventCallback;
        public BOOL SuppressBackgroundThread;
        public BOOL SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal partial struct GdiplusStartupInputEx
    {
        public GdiplusStartupInput Base;
        public int StartupParameters;
    }
}

