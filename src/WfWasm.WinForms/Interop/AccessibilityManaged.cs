// Fork-owned managed projection of Accessibility.dll (runtimes/winforms/src/Accessibility/src/Accessibility.il).
// Provides Accessibility.IAccessible and related interfaces for WinForms AccessibleObject.

using System.Runtime.InteropServices;

namespace Accessibility;

public enum AnnoScope
{
    ANNO_THIS = 0,
    ANNO_CONTAINER = 1
}

[ComImport]
[Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IAccessible
{
    [DispId(-5000)]
    object? accParent { get; }

    [DispId(-5001)]
    int accChildCount { get; }

    [DispId(-5002)]
    object? get_accChild(object childID);

    [DispId(-5003)]
    string? get_accName(object childID);

    [DispId(-5004)]
    string? get_accValue(object childID);

    [DispId(-5005)]
    string? get_accDescription(object childID);

    [DispId(-5006)]
    object? get_accRole(object childID);

    [DispId(-5007)]
    object? get_accState(object childID);

    [DispId(-5008)]
    string? get_accHelp(object childID);

    [DispId(-5009)]
    int get_accHelpTopic(out string? pszHelpFile, object childID);

    [DispId(-5010)]
    string? get_accKeyboardShortcut(object childID);

    [DispId(-5011)]
    object? accFocus { get; }

    [DispId(-5012)]
    object? accSelection { get; }

    [DispId(-5013)]
    string? get_accDefaultAction(object childID);

    [DispId(-5014)]
    void accSelect(int flagsSelect, object childID);

    [DispId(-5015)]
    void accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, object childID);

    [DispId(-5016)]
    object? accNavigate(int navDir, object childID);

    [DispId(-5017)]
    object? accHitTest(int xLeft, int yTop);

    [DispId(-5018)]
    void accDoDefaultAction(object childID);

    [DispId(-5003)]
    void set_accName(object childID, string newName);

    [DispId(-5004)]
    void set_accValue(object childID, string newValue);
}

[ComImport]
[Guid("03022430-ABC4-11D0-BDE2-00AA001A1953")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAccessibleHandler
{
    void AccessibleObjectFromID(int hwnd, int lObjectID, out IAccessible pIAccessible);
}

[ComImport]
[Guid("7852B78D-1CFD-41C1-A615-9C0C85960B5F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAccIdentity
{
    void GetIdentityString(uint dwIDChild, out nint ppIDString, out uint pdwIDStringLen);
}

[ComImport]
[Guid("6E26E776-04F0-495D-80E4-3330352E3169")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAccPropServices
{
    void DecomposeHwndIdentityString(ref byte pIDString, uint dwIDStringLen, out nint phwnd, out uint pidObject, out uint pidChild);
}

[ComImport]
[Guid("76C07795-CB3E-461E-83EB-516333D013EE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAccPropServer
{
    void GetPropValue(ref byte pIDString, uint dwIDStringLen, Guid idProp, out object pvarValue, out int pfHasProp);
}
