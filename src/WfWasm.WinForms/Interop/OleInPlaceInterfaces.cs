// Fork-owned: In-place OLE interfaces for Windows.Win32.System.Ole.

using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32.System.Ole;

internal unsafe partial struct IOleWindow : IComIID, IVTable<IOleWindow, IOleWindow.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00000114-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetWindow(HWND* phwnd) => HRESULT.E_NOTIMPL;
    public HRESULT ContextSensitiveHelp(BOOL fEnterMode) => HRESULT.E_NOTIMPL;

    public interface Interface
    {
        HRESULT GetWindow(HWND* phwnd);
        HRESULT ContextSensitiveHelp(BOOL fEnterMode);
    }
}

internal unsafe partial struct IOleInPlaceSite : IComIID, IVTable<IOleInPlaceSite, IOleInPlaceSite.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00000119-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetWindow(HWND* phwnd) => HRESULT.E_NOTIMPL;
    public HRESULT ContextSensitiveHelp(BOOL fEnterMode) => HRESULT.E_NOTIMPL;
    public HRESULT CanInPlaceActivate() => HRESULT.S_OK;
    public HRESULT OnInPlaceActivate() => HRESULT.S_OK;
    public HRESULT OnUIActivate() => HRESULT.S_OK;
    public HRESULT GetWindowContext(IOleInPlaceFrame** ppFrame, IOleInPlaceUIWindow** ppDoc, RECT* lprcPosRect, RECT* lprcClipRect, OLEINPLACEFRAMEINFO* lpFrameInfo) => HRESULT.S_OK;
    public HRESULT Scroll(SIZE scrollExtant) => HRESULT.S_OK;
    public HRESULT OnUIDeactivate(BOOL fUndoable) => HRESULT.S_OK;
    public HRESULT OnInPlaceDeactivate() => HRESULT.S_OK;
    public HRESULT DiscardUndoState() => HRESULT.S_OK;
    public HRESULT DeactivateAndUndo() => HRESULT.S_OK;
    public HRESULT OnPosRectChange(RECT* lprcPosRect) => HRESULT.S_OK;

    public interface Interface
    {
        HRESULT GetWindow(HWND* phwnd);
        HRESULT ContextSensitiveHelp(BOOL fEnterMode);
        HRESULT CanInPlaceActivate();
        HRESULT OnInPlaceActivate();
        HRESULT OnUIActivate();
        HRESULT GetWindowContext(IOleInPlaceFrame** ppFrame, IOleInPlaceUIWindow** ppDoc, RECT* lprcPosRect, RECT* lprcClipRect, OLEINPLACEFRAMEINFO* lpFrameInfo);
        HRESULT Scroll(SIZE scrollExtant);
        HRESULT OnUIDeactivate(BOOL fUndoable);
        HRESULT OnInPlaceDeactivate();
        HRESULT DiscardUndoState();
        HRESULT DeactivateAndUndo();
        HRESULT OnPosRectChange(RECT* lprcPosRect);
    }
}

internal unsafe partial struct IOleInPlaceFrame : IComIID, IVTable<IOleInPlaceFrame, IOleInPlaceFrame.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00000116-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetWindow(HWND* phwnd) => HRESULT.E_NOTIMPL;
    public HRESULT ContextSensitiveHelp(BOOL fEnterMode) => HRESULT.E_NOTIMPL;
    public HRESULT GetBorder(RECT* lprectBorder) => HRESULT.E_NOTIMPL;
    public HRESULT RequestBorderSpace(RECT* pborderwidths) => HRESULT.S_OK;
    public HRESULT SetBorderSpace(RECT* pborderwidths) => HRESULT.S_OK;
    public HRESULT SetActiveObject(IOleInPlaceActiveObject* pActiveObject, PCWSTR pszObjName) => HRESULT.S_OK;
    public HRESULT InsertMenus(HMENU hmenuShared, OLEMENUGROUPWIDTHS* lpMenuWidths) => HRESULT.S_OK;
    public HRESULT SetMenu(HMENU hmenuShared, nint holemenu, HWND hwndActiveObject) => HRESULT.S_OK;
    public HRESULT RemoveMenus(HMENU hmenuShared) => HRESULT.S_OK;
    public HRESULT SetStatusText(PCWSTR pszStatusText) => HRESULT.S_OK;
    public HRESULT EnableModeless(BOOL fEnable) => HRESULT.S_OK;
    public HRESULT TranslateAccelerator(MSG* lpmsg, ushort wID) => HRESULT.S_OK;

    public interface Interface
    {
        HRESULT GetWindow(HWND* phwnd);
        HRESULT ContextSensitiveHelp(BOOL fEnterMode);
        HRESULT GetBorder(RECT* lprectBorder);
        HRESULT RequestBorderSpace(RECT* pborderwidths);
        HRESULT SetBorderSpace(RECT* pborderwidths);
        HRESULT SetActiveObject(IOleInPlaceActiveObject* pActiveObject, PCWSTR pszObjName);
        HRESULT InsertMenus(HMENU hmenuShared, OLEMENUGROUPWIDTHS* lpMenuWidths);
        HRESULT SetMenu(HMENU hmenuShared, nint holemenu, HWND hwndActiveObject);
        HRESULT RemoveMenus(HMENU hmenuShared);
        HRESULT SetStatusText(PCWSTR pszStatusText);
        HRESULT EnableModeless(BOOL fEnable);
        HRESULT TranslateAccelerator(MSG* lpmsg, ushort wID);
    }
}

internal unsafe partial struct IOleInPlaceUIWindow : IComIID, IVTable<IOleInPlaceUIWindow, IOleInPlaceUIWindow.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00000115-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetWindow(HWND* phwnd) => HRESULT.E_NOTIMPL;
    public HRESULT ContextSensitiveHelp(BOOL fEnterMode) => HRESULT.E_NOTIMPL;
    public HRESULT GetBorder(RECT* lprectBorder) => HRESULT.E_NOTIMPL;
    public HRESULT RequestBorderSpace(RECT* pborderwidths) => HRESULT.S_OK;
    public HRESULT SetBorderSpace(RECT* pborderwidths) => HRESULT.S_OK;
    public HRESULT SetActiveObject(IOleInPlaceActiveObject* pActiveObject, PCWSTR pszObjName) => HRESULT.S_OK;

    public interface Interface
    {
        HRESULT GetWindow(HWND* phwnd);
        HRESULT ContextSensitiveHelp(BOOL fEnterMode);
        HRESULT GetBorder(RECT* lprectBorder);
        HRESULT RequestBorderSpace(RECT* pborderwidths);
        HRESULT SetBorderSpace(RECT* pborderwidths);
        HRESULT SetActiveObject(IOleInPlaceActiveObject* pActiveObject, PCWSTR pszObjName);
    }
}

internal unsafe partial struct IOleInPlaceObject : IComIID, IVTable<IOleInPlaceObject, IOleInPlaceObject.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00000113-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetWindow(HWND* phwnd) => HRESULT.E_NOTIMPL;
    public HRESULT ContextSensitiveHelp(BOOL fEnterMode) => HRESULT.E_NOTIMPL;
    public HRESULT InPlaceDeactivate() => HRESULT.S_OK;
    public HRESULT UIDeactivate() => HRESULT.S_OK;
    public HRESULT SetObjectRects(RECT* lprcPosRect, RECT* lprcClipRect) => HRESULT.S_OK;
    public HRESULT ReactivateAndUndo() => HRESULT.S_OK;

    public interface Interface
    {
        HRESULT GetWindow(HWND* phwnd);
        HRESULT ContextSensitiveHelp(BOOL fEnterMode);
        HRESULT InPlaceDeactivate();
        HRESULT UIDeactivate();
        HRESULT SetObjectRects(RECT* lprcPosRect, RECT* lprcClipRect);
        HRESULT ReactivateAndUndo();
    }
}

internal unsafe partial struct IOleInPlaceActiveObject : IComIID, IVTable<IOleInPlaceActiveObject, IOleInPlaceActiveObject.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00000117-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetWindow(HWND* phwnd) => HRESULT.E_NOTIMPL;
    public HRESULT ContextSensitiveHelp(BOOL fEnterMode) => HRESULT.E_NOTIMPL;
    public HRESULT TranslateAccelerator(MSG* lpmsg) => HRESULT.S_OK;
    public HRESULT OnFrameWindowActivate(BOOL fActivate) => HRESULT.S_OK;
    public HRESULT OnDocWindowActivate(BOOL fActivate) => HRESULT.S_OK;
    public HRESULT ResizeBorder(RECT* prcBorder, IOleInPlaceUIWindow* pUIWindow, BOOL fFrameWindow) => HRESULT.S_OK;
    public HRESULT EnableModeless(BOOL fEnable) => HRESULT.S_OK;

    public interface Interface
    {
        HRESULT GetWindow(HWND* phwnd);
        HRESULT ContextSensitiveHelp(BOOL fEnterMode);
        HRESULT TranslateAccelerator(MSG* lpmsg);
        HRESULT OnFrameWindowActivate(BOOL fActivate);
        HRESULT OnDocWindowActivate(BOOL fActivate);
        HRESULT ResizeBorder(RECT* prcBorder, IOleInPlaceUIWindow* pUIWindow, BOOL fFrameWindow);
        HRESULT EnableModeless(BOOL fEnable);
    }
}
