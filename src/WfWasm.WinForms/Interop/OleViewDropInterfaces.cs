// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.System.Variant;
using ComIDataObject = Windows.Win32.System.Com.IDataObject;

namespace Windows.Win32.System.Ole
{
    [Guid("0000010D-0000-0000-C000-000000000046")]
    internal unsafe partial struct IViewObject : IComIID, IVTable<IViewObject, IViewObject.Vtbl>
    {
        private static readonly Guid s_guid = new("0000010D-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT Draw(DVASPECT dwDrawAspect, int lindex, void* pvAspect, DVTARGETDEVICE* ptd, HDC hdcTargetDev, HDC hdcDraw, RECTL* lprcBounds, RECTL* lprcWBounds, nint pfnContinue, nuint dwContinue);
            HRESULT GetColorSet(DVASPECT dwDrawAspect, int lindex, void* pvAspect, DVTARGETDEVICE* ptd, HDC hicTargetDev, LOGPALETTE** ppColorSet);
            HRESULT Freeze(DVASPECT dwDrawAspect, int lindex, void* pvAspect, uint* pdwFreeze);
            HRESULT Unfreeze(uint dwFreeze);
            HRESULT SetAdvise(DVASPECT aspects, uint advf, IAdviseSink* pAdvSink);
            HRESULT GetAdvise(uint* pAspects, uint* pAdvf, IAdviseSink** ppAdvSink);
        }
    }

    [Guid("00000127-0000-0000-C000-000000000046")]
    internal unsafe partial struct IViewObject2 : IComIID, IVTable<IViewObject2, IViewObject2.Vtbl>
    {
        private static readonly Guid s_guid = new("00000127-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface : IViewObject.Interface
        {
            new HRESULT Draw(DVASPECT dwDrawAspect, int lindex, void* pvAspect, DVTARGETDEVICE* ptd, HDC hdcTargetDev, HDC hdcDraw, RECTL* lprcBounds, RECTL* lprcWBounds, nint pfnContinue, nuint dwContinue);
            new HRESULT GetColorSet(DVASPECT dwDrawAspect, int lindex, void* pvAspect, DVTARGETDEVICE* ptd, HDC hicTargetDev, LOGPALETTE** ppColorSet);
            new HRESULT Freeze(DVASPECT dwDrawAspect, int lindex, void* pvAspect, uint* pdwFreeze);
            new HRESULT Unfreeze(uint dwFreeze);
            new HRESULT SetAdvise(DVASPECT aspects, uint advf, IAdviseSink* pAdvSink);
            new HRESULT GetAdvise(uint* pAspects, uint* pAdvf, IAdviseSink** ppAdvSink);
            HRESULT GetExtent(DVASPECT dwDrawAspect, int lindex, DVTARGETDEVICE* ptd, SIZE* lpsizel);
        }
    }

    [Guid("CF515160-FC44-11CE-ABC4-02608C9E7553")]
    internal unsafe partial struct IQuickActivate : IComIID, IVTable<IQuickActivate, IQuickActivate.Vtbl>
    {
        private static readonly Guid s_guid = new("CF515160-FC44-11CE-ABC4-02608C9E7553");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT QuickActivate(QACONTAINER* pQaContainer, QACONTROL* pQaControl);
            HRESULT SetContentExtent(SIZE* pSizel);
            HRESULT GetContentExtent(SIZE* pSizel);
        }
    }

    [Guid("00000122-0000-0000-C000-000000000046")]
    internal unsafe partial struct IDropTarget : IComIID, IVTable<IDropTarget, IDropTarget.Vtbl>
    {
        private static readonly Guid s_guid = new("00000122-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT DragEnter(ComIDataObject* pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect);
            HRESULT DragOver(MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect);
            HRESULT DragLeave();
            HRESULT Drop(ComIDataObject* pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect);
        }
    }

    [Guid("00000121-0000-0000-C000-000000000046")]
    internal unsafe partial struct IDropSource : IComIID, IVTable<IDropSource, IDropSource.Vtbl>
    {
        private static readonly Guid s_guid = new("00000121-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT QueryContinueDrag(BOOL fEscapePressed, MODIFIERKEYS_FLAGS grfKeyState);
            HRESULT GiveFeedback(DROPEFFECT dwEffect);
        }
    }

    [Guid("00000124-0000-0000-C000-000000000046")]
    internal unsafe partial struct IDropSourceNotify : IComIID, IVTable<IDropSourceNotify, IDropSourceNotify.Vtbl>
    {
        private static readonly Guid s_guid = new("00000124-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT DragEnterTarget(HWND hwndTarget);
            HRESULT DragLeaveTarget();
        }
    }

    [Guid("742B0E01-14E6-101B-914E-00AA00300CAB")]
    internal unsafe partial struct ISimpleFrameSite : IComIID, IVTable<ISimpleFrameSite, ISimpleFrameSite.Vtbl>
    {
        private static readonly Guid s_guid = new("742B0E01-14E6-101B-914E-00AA00300CAB");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT PreMessageFilter(HWND hWnd, uint msg, WPARAM wp, LPARAM lp, LRESULT* plResult, uint* pdwCookie);
            HRESULT PostMessageFilter(HWND hWnd, uint msg, WPARAM wp, LPARAM lp, LRESULT* plResult, uint dwCookie);
        }
    }

    [Guid("3386FD70-9611-11CF-8F75-444553540000")]
    internal unsafe partial struct IVBGetControl : IComIID, IVTable<IVBGetControl, IVBGetControl.Vtbl>
    {
        private static readonly Guid s_guid = new("3386FD70-9611-11CF-8F75-444553540000");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT EnumControls(uint dwOleContF, ENUM_CONTROLS_WHICH_FLAGS dwWhich, IEnumUnknown** ppenum);
        }
    }

    [Guid("8A7010A0-45A6-11CE-B69C-00AA00341D07")]
    internal unsafe partial struct IGetOleObject : IComIID, IVTable<IGetOleObject, IGetOleObject.Vtbl>
    {
        private static readonly Guid s_guid = new("8A7010A0-45A6-11CE-B69C-00AA00341D07");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT GetOleObject(Guid* riid, void** ppvObj);
        }
    }

    [Guid("3386FD71-9611-11CF-8F75-444553540000")]
    internal unsafe partial struct IGetVBAObject : IComIID, IVTable<IGetVBAObject, IGetVBAObject.Vtbl>
    {
        private static readonly Guid s_guid = new("3386FD71-9611-11CF-8F75-444553540000");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT GetObject(Guid* riid, void** ppvObj, uint dwReserved);
        }
    }

    [Guid("9849FD60-3768-101B-8D72-AE6164FDE604")]
    internal unsafe partial struct IVBFormat : IComIID, IVTable<IVBFormat, IVBFormat.Vtbl>
    {
        private static readonly Guid s_guid = new("9849FD60-3768-101B-8D72-AE6164FDE604");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT Format(VARIANT* vData, BSTR bstrFormat, void* lpBuffer, ushort cb, int lcid, short sFirstDayOfWeek, ushort sFirstWeekOfYear, ushort* rcb);
        }
    }
}
