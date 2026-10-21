namespace System.Windows.Forms;

internal static class WfWasmInit
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init()
    {
        AppContext.SetSwitch("System.Windows.Forms.ServicePointManagerCheckCrl", false);
        // Route Graphics.GetHdc through the HDC Graphics registry so GDI
        // paint calls over scratch bitmaps (ImageList.GetBitmap, DrawText)
        // resolve their target headless.
        global::System.Drawing.Graphics.HdcHook = static g => global::Windows.Win32.PInvoke.WasmFillBridge.RegisterScratch(g);
        global::System.Drawing.Graphics.HdcReleaseHook = static hdc => global::Windows.Win32.PInvoke.WasmFillBridge.UnregisterScratch(hdc);
        global::System.Drawing.Graphics.HwndOverlaySizeHook = static hwnd => WasmOverlayBridge.OverlaySizeFor(hwnd);
        global::System.Drawing.Graphics.HwndOverlayCommitHook = static (hwnd, bmp) => WasmOverlayBridge.CommitOverlay(hwnd, bmp);
    }
}
