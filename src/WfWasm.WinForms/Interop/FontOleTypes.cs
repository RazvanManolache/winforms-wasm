// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;

namespace Windows.Win32.System.Ole
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct FONTDESC
    {
        public uint cbSizeofstruct;
        public PWSTR lpstrName;
        public CY cySize;
        public short sWeight;
        public short sCharset;
        public BOOL fItalic;
        public BOOL fUnderline;
        public BOOL fStrikethrough;
    }

    [Guid("BEF6E002-A874-101A-8BBA-00AA00300CAB")]
    internal unsafe partial struct IFont : IComIID, IVTable<IFont, IFont.Vtbl>
    {
        private static readonly Guid s_guid = new("BEF6E002-A874-101A-8BBA-00AA00300CAB");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public uint Release() => 0;
        public HFONT hFont => default;
        public BSTR Name { get => default; set { } }
        public CY Size { get => default; set { } }
        public short Weight { get => default; set { } }
        public BOOL Bold { get => default; set { } }
        public BOOL Italic { get => default; set { } }
        public BOOL Underline { get => default; set { } }
        public BOOL Strikethrough { get => default; set { } }
        public short Charset { get => default; set { } }

        public interface Interface
        {
            HFONT hFont => default;
        }
    }

    [Guid("BEF6E003-A874-101A-8BBA-00AA00300CAB")]
    internal unsafe partial struct IFontDisp : IComIID, IVTable<IFontDisp, IFontDisp.Vtbl>
    {
        private static readonly Guid s_guid = new("BEF6E003-A874-101A-8BBA-00AA00300CAB");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
        }
    }
}

namespace Windows.Win32
{
    internal static partial class PInvoke
    {
        public static unsafe HRESULT OleCreateFontIndirect(in System.Ole.FONTDESC pFontDesc, in Guid riid, out void* ppvObj)
        {
            ppvObj = null;
            return HRESULT.E_NOTIMPL;
        }

        public static unsafe HRESULT OleCreateFontIndirect(System.Ole.FONTDESC* pFontDesc, in Guid riid, void** ppvObj) => HRESULT.S_OK;
        public static unsafe HRESULT OleCreateFontIndirect(System.Ole.FONTDESC* pFontDesc, Guid* riid, void** ppvObj) => HRESULT.S_OK;
    }
}
