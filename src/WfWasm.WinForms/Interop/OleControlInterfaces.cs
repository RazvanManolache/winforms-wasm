// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.WindowsAndMessaging;
using ComIDataObject = Windows.Win32.System.Com.IDataObject;

namespace Windows.Win32.System.Ole
{
    [Guid("00000118-0000-0000-C000-000000000046")]
    internal unsafe partial struct IOleClientSite : IComIID, IVTable<IOleClientSite, IOleClientSite.Vtbl>
    {
        private static readonly Guid s_guid = new("00000118-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT SaveObject() => HRESULT.E_NOTIMPL;
        public HRESULT GetMoniker(uint dwAssign, uint dwWhichMoniker, IMoniker** ppmk) => HRESULT.E_NOTIMPL;
        public HRESULT GetContainer(IOleContainer** ppContainer) => HRESULT.E_NOTIMPL;
        public HRESULT ShowObject() => HRESULT.S_OK;
        public HRESULT OnShowWindow(BOOL fShow) => HRESULT.S_OK;
        public HRESULT RequestNewObjectLayout() => HRESULT.E_NOTIMPL;
        public uint AddRef() => 1;
        public uint Release() => 0;

        public interface Interface
        {
            HRESULT SaveObject();
            HRESULT GetMoniker(uint dwAssign, uint dwWhichMoniker, IMoniker** ppmk);
            HRESULT GetContainer(IOleContainer** ppContainer);
            HRESULT ShowObject();
            HRESULT OnShowWindow(BOOL fShow);
            HRESULT RequestNewObjectLayout();
        }
    }

    [Guid("00000112-0000-0000-C000-000000000046")]
    internal unsafe partial struct IOleObject : IComIID, IVTable<IOleObject, IOleObject.Vtbl>
    {
        private static readonly Guid s_guid = new("00000112-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT SetClientSite(IOleClientSite* pClientSite) => HRESULT.S_OK;
        public HRESULT GetClientSite(IOleClientSite** ppClientSite) => HRESULT.E_NOTIMPL;
        public HRESULT SetHostNames(PCWSTR szContainerApp, PCWSTR szContainerObj) => HRESULT.S_OK;
        public HRESULT Close(uint dwSaveOption) => HRESULT.S_OK;
        public HRESULT SetMoniker(uint dwWhichMoniker, IMoniker* pmk) => HRESULT.E_NOTIMPL;
        public HRESULT GetMoniker(uint dwAssign, uint dwWhichMoniker, IMoniker** ppmk) => HRESULT.E_NOTIMPL;
        public HRESULT InitFromData(ComIDataObject* pDataObject, BOOL fCreation, uint dwReserved) => HRESULT.E_NOTIMPL;
        public HRESULT GetClipboardData(uint dwReserved, ComIDataObject** ppDataObject) => HRESULT.E_NOTIMPL;
        public HRESULT DoVerb(int iVerb, MSG* lpmsg, IOleClientSite* pActiveSite, int lindex, HWND hwndParent, RECT* lprcPosRect) => HRESULT.S_OK;
        public HRESULT EnumVerbs(IEnumOLEVERB** ppEnumOleVerb) => HRESULT.E_NOTIMPL;
        public HRESULT Update() => HRESULT.S_OK;
        public HRESULT IsUpToDate() => HRESULT.S_OK;
        public HRESULT GetUserClassID(Guid* pClsid) => HRESULT.E_NOTIMPL;
        public HRESULT GetUserType(uint dwFormOfType, PWSTR* pszUserType) => HRESULT.E_NOTIMPL;
        public HRESULT SetExtent(DVASPECT dwDrawAspect, SIZE* psizel) => HRESULT.S_OK;
        public HRESULT GetExtent(DVASPECT dwDrawAspect, SIZE* psizel) => HRESULT.E_NOTIMPL;
        public HRESULT Advise(IAdviseSink* pAdvSink, uint* pdwConnection) => HRESULT.E_NOTIMPL;
        public HRESULT Unadvise(uint dwConnection) => HRESULT.S_OK;
        public HRESULT EnumAdvise(IEnumSTATDATA** ppenumAdvise) => HRESULT.E_NOTIMPL;
        public HRESULT GetMiscStatus(DVASPECT dwAspect, OLEMISC* pdwStatus) => HRESULT.E_NOTIMPL;
        public HRESULT GetMiscStatus(DVASPECT dwAspect, out OLEMISC pdwStatus) { pdwStatus = default; return HRESULT.E_NOTIMPL; }
        public HRESULT SetColorScheme(LOGPALETTE* pLogpal) => HRESULT.S_OK;

        public interface Interface
        {
            HRESULT SetClientSite(IOleClientSite* pClientSite);
            HRESULT GetClientSite(IOleClientSite** ppClientSite);
            HRESULT SetHostNames(PCWSTR szContainerApp, PCWSTR szContainerObj);
            HRESULT Close(uint dwSaveOption);
            HRESULT SetMoniker(uint dwWhichMoniker, IMoniker* pmk);
            HRESULT GetMoniker(uint dwAssign, uint dwWhichMoniker, IMoniker** ppmk);
            HRESULT InitFromData(ComIDataObject* pDataObject, BOOL fCreation, uint dwReserved);
            HRESULT GetClipboardData(uint dwReserved, ComIDataObject** ppDataObject);
            HRESULT DoVerb(int iVerb, MSG* lpmsg, IOleClientSite* pActiveSite, int lindex, HWND hwndParent, RECT* lprcPosRect);
            HRESULT EnumVerbs(IEnumOLEVERB** ppEnumOleVerb);
            HRESULT Update();
            HRESULT IsUpToDate();
            HRESULT GetUserClassID(Guid* pClsid);
            HRESULT GetUserType(uint dwFormOfType, PWSTR* pszUserType);
            HRESULT SetExtent(DVASPECT dwDrawAspect, SIZE* psizel);
            HRESULT GetExtent(DVASPECT dwDrawAspect, SIZE* psizel);
            HRESULT Advise(IAdviseSink* pAdvSink, uint* pdwConnection);
            HRESULT Unadvise(uint dwConnection);
            HRESULT EnumAdvise(IEnumSTATDATA** ppenumAdvise);
            HRESULT GetMiscStatus(DVASPECT dwAspect, OLEMISC* pdwStatus);
            HRESULT SetColorScheme(LOGPALETTE* pLogpal);

            HRESULT GetMiscStatus(DVASPECT dwAspect, out OLEMISC pdwStatus)
            {
                fixed (OLEMISC* p = &pdwStatus)
                {
                    return GetMiscStatus(dwAspect, p);
                }
            }
        }
    }

    [Guid("B196B28D-BAB4-101A-B69C-00AA00341D07")]
    internal unsafe partial struct IOleControl : IComIID, IVTable<IOleControl, IOleControl.Vtbl>
    {
        private static readonly Guid s_guid = new("B196B28D-BAB4-101A-B69C-00AA00341D07");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetControlInfo(CONTROLINFO* pCI) => HRESULT.E_NOTIMPL;
        public HRESULT OnMnemonic(MSG* pMsg) => HRESULT.S_OK;
        public HRESULT OnAmbientPropertyChange(int dispID) => HRESULT.S_OK;
        public HRESULT FreezeEvents(BOOL bFreeze) => HRESULT.S_OK;

        public interface Interface
        {
            HRESULT GetControlInfo(CONTROLINFO* pCI);
            HRESULT OnMnemonic(MSG* pMsg);
            HRESULT OnAmbientPropertyChange(int dispID);
            HRESULT FreezeEvents(BOOL bFreeze);
        }
    }

    [Guid("B196B289-BAB4-101A-B69C-00AA00341D07")]
    internal unsafe partial struct IOleControlSite : IComIID, IVTable<IOleControlSite, IOleControlSite.Vtbl>
    {
        private static readonly Guid s_guid = new("B196B289-BAB4-101A-B69C-00AA00341D07");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT OnControlInfoChanged() => HRESULT.S_OK;
        public HRESULT LockInPlaceActive(BOOL fLock) => HRESULT.E_NOTIMPL;
        public HRESULT GetExtendedControl(IDispatch** ppDisp) => HRESULT.E_NOTIMPL;
        public HRESULT TransformCoords(POINTL* pPtlHimetric, PointF* pPtfContainer, uint dwFlags) => HRESULT.S_OK;
        public HRESULT TranslateAccelerator(MSG* pMsg, KEYMODIFIERS grfModifiers) => HRESULT.S_OK;
        public HRESULT OnFocus(BOOL fGotFocus) => HRESULT.S_OK;
        public HRESULT ShowPropertyFrame() => HRESULT.E_NOTIMPL;

        public interface Interface
        {
            HRESULT OnControlInfoChanged();
            HRESULT LockInPlaceActive(BOOL fLock);
            HRESULT GetExtendedControl(IDispatch** ppDisp);
            HRESULT TransformCoords(POINTL* pPtlHimetric, PointF* pPtfContainer, uint dwFlags);
            HRESULT TranslateAccelerator(MSG* pMsg, KEYMODIFIERS grfModifiers);
            HRESULT OnFocus(BOOL fGotFocus);
            HRESULT ShowPropertyFrame();
        }
    }

    [Guid("0000011B-0000-0000-C000-000000000046")]
    internal unsafe partial struct IOleContainer : IComIID, IVTable<IOleContainer, IOleContainer.Vtbl>
    {
        private static readonly Guid s_guid = new("0000011B-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT ParseDisplayName(IBindCtx* pbc, PWSTR pszDisplayName, uint* pchEaten, IMoniker** ppmkOut) => HRESULT.E_NOTIMPL;
        public HRESULT EnumObjects(uint grfFlags, IEnumUnknown** ppenum) => HRESULT.E_NOTIMPL;
        public HRESULT LockContainer(BOOL fLock) => HRESULT.E_NOTIMPL;

        public interface Interface : IParseDisplayName.Interface
        {
            new HRESULT ParseDisplayName(IBindCtx* pbc, PWSTR pszDisplayName, uint* pchEaten, IMoniker** ppmkOut);
            HRESULT EnumObjects(uint grfFlags, IEnumUnknown** ppenum);
            HRESULT LockContainer(BOOL fLock);
        }
    }

    [Guid("0000011A-0000-0000-C000-000000000046")]
    internal unsafe partial struct IParseDisplayName : IComIID, IVTable<IParseDisplayName, IParseDisplayName.Vtbl>
    {
        private static readonly Guid s_guid = new("0000011A-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT ParseDisplayName(IBindCtx* pbc, PWSTR pszDisplayName, uint* pchEaten, IMoniker** ppmkOut) => HRESULT.E_NOTIMPL;

        public interface Interface
        {
            HRESULT ParseDisplayName(IBindCtx* pbc, PWSTR pszDisplayName, uint* pchEaten, IMoniker** ppmkOut);
        }
    }
}
