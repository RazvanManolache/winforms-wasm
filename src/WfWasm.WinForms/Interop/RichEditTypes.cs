// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.WindowsAndMessaging;
using ComIDataObject = Windows.Win32.System.Com.IDataObject;

namespace Windows.Win32.UI.Controls.RichEdit
{
    [Flags]
    internal enum GETTEXTEX_FLAGS : uint
    {
        GT_DEFAULT = 0,
        GT_USECRLF = 1,
        GT_SELECTION = 2,
        GT_RAWTEXT = 4,
        GT_NOHIDDENTEXT = 8,
    }

    [Flags]
    internal enum GETTEXTLENGTHEX_FLAGS : uint
    {
        GTL_DEFAULT = 0,
        GTL_USECRLF = 1,
        GTL_PRECISE = 2,
        GTL_CLOSE = 4,
        GTL_NUMCHARS = 8,
        GTL_NUMBYTES = 16,
    }

    internal enum PARAFORMAT_NUMBERING : ushort
    {
        PFN_BULLET = 1,
        PFN_ARABIC = 2,
        PFN_LCLETTER = 3,
        PFN_UCLETTER = 4,
        PFN_LCROMAN = 5,
        PFN_UCROMAN = 6,
    }

    [Flags]
    internal enum RICH_EDIT_GET_CONTEXT_MENU_SEL_TYPE : ushort
    {
        SEL_EMPTY = 0,
        SEL_TEXT = 1,
        SEL_OBJECT = 2,
        SEL_MULTICHAR = 4,
        SEL_MULTIOBJECT = 8,
    }

    internal enum RECO_FLAGS
    {
        RECO_PASTE = 0,
        RECO_DROP = 1,
        RECO_COPY = 2,
        RECO_CUT = 3,
        RECO_DRAG = 4,
    }

    [Guid("00020D03-0000-0000-C000-000000000046")]
    internal unsafe partial struct IRichEditOleCallback : IComIID, IVTable<IRichEditOleCallback, IRichEditOleCallback.Vtbl>
    {
        private static readonly Guid s_guid = new("00020D03-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT GetNewStorage(IStorage** lplpstg);
            HRESULT GetInPlaceContext(IOleInPlaceFrame** lplpFrame, IOleInPlaceUIWindow** lplpDoc, OLEINPLACEFRAMEINFO* lpFrameInfo);
            HRESULT ShowContainerUI(BOOL fShow);
            HRESULT QueryInsertObject(Guid* lpclsid, IStorage* lpstg, int cp);
            HRESULT DeleteObject(IOleObject* lpoleobj);
            HRESULT QueryAcceptData(ComIDataObject* lpdataobj, ushort* lpcfFormat, RECO_FLAGS reco, BOOL fReally, HGLOBAL hMetaPict);
            HRESULT ContextSensitiveHelp(BOOL fEnterMode);
            HRESULT GetClipboardData(CHARRANGE* lpchrg, uint reco, ComIDataObject** lplpdataobj);
            HRESULT GetDragDropEffect(BOOL fDrag, MODIFIERKEYS_FLAGS grfKeyState, DROPEFFECT* pdwEffect);
            HRESULT GetContextMenu(RICH_EDIT_GET_CONTEXT_MENU_SEL_TYPE seltype, IOleObject* lpoleobj, CHARRANGE* lpchrg, HMENU* hmenu);
        }
    }

    public enum tomConstants
    {
        tomFalse = 0,
        tomTrue = -1,
        tomUndefined = -9999999,
        tomToggle = -9999998,
        tomAutoColor = -9999997,
        tomDefault = -9999996,
        tomSuspend = -9999995,
        tomResume = -9999994,
        tomStart = 32,
        tomEnd = 0,
    }

    [Guid("00020D00-0000-0000-C000-000000000046")]
    internal unsafe partial struct IRichEditOle : IComIID, IVTable<IRichEditOle, IRichEditOle.Vtbl>
    {
        private static readonly Guid s_guid = new("00020D00-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetClientSite(IOleClientSite** lplpclientSite) => HRESULT.E_NOTIMPL;
        public HRESULT GetObjectCount() => HRESULT.E_NOTIMPL;
        public interface Interface { }
    }

    [Guid("8CC497C0-A1DF-11CE-8098-00AA0047BE5D")]
    internal unsafe partial struct ITextDocument : IComIID, IVTable<ITextDocument, ITextDocument.Vtbl>
    {
        private static readonly Guid s_guid = new("8CC497C0-A1DF-11CE-8098-00AA0047BE5D");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT Range(int cp1, int cp2, ITextRange** ppRange) => HRESULT.E_NOTIMPL;
        public interface Interface { }
    }

    [Guid("8CC497C2-A1DF-11CE-8098-00AA0047BE5D")]
    internal unsafe partial struct ITextRange : IComIID, IVTable<ITextRange, ITextRange.Vtbl>
    {
        private static readonly Guid s_guid = new("8CC497C2-A1DF-11CE-8098-00AA0047BE5D");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT CanEdit(int* pbCanEdit) => HRESULT.E_NOTIMPL;
        public HRESULT ScrollIntoView(int Value) => HRESULT.S_OK;
        public interface Interface { }
    }
}

namespace Windows.Win32.UI.Controls.Dialogs
{
    [Flags]
    public enum FINDREPLACE_FLAGS : uint
    {
        FR_DOWN = 0x00000001,
        FR_WHOLEWORD = 0x00000002,
        FR_MATCHCASE = 0x00000004,
        FR_FINDNEXT = 0x00000008,
        FR_REPLACE = 0x00000010,
        FR_REPLACEALL = 0x00000020,
        FR_DIALOGTERM = 0x00000040,
        FR_SHOWHELP = 0x00000080,
        FR_ENABLEHOOK = 0x00000100,
        FR_ENABLETEMPLATE = 0x00000200,
        FR_NOUPDOWN = 0x00000400,
        FR_NOMATCHCASE = 0x00000800,
        FR_NOWHOLEWORD = 0x00001000,
        FR_ENABLETEMPLATEHANDLE = 0x00002000,
        FR_HIDEUPDOWN = 0x00004000,
        FR_HIDEMATCHCASE = 0x00008000,
        FR_HIDEWHOLEWORD = 0x00010000,
    }
}
