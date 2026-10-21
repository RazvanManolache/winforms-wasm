// Fork-owned headless replacement for upstream WebBrowserBase (Compile-
// Removed in WfWasm.WinForms.csproj). Upstream drives an IE ActiveX state
// machine (Passive -> Loaded -> InPlaceActive) from OnParentChanged/
// OnVisibleChanged/OnGotFocus and throws InvalidCastException with no COM
// in the browser, which killed form construction for every WebBrowser
// host (Telerik RssReader first). There is no AX host on WASM, so the
// base is an inert Control: parenting/visibility/focus do nothing extra.

namespace System.Windows.Forms;

public class WebBrowserBase : Control
{
    public WebBrowserBase()
    {
    }

    // Upstream compatibility: derived WebBrowser passes the AX CLSID.
    protected WebBrowserBase(string clsid)
    {
        _ = clsid;
    }
}
