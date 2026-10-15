namespace WfWasm.Shim;

/// <summary>
/// Win32-bound seams a stock app needs reimplemented for the browser.
/// Placeholder for stage 2; seam details live in SOURCES.md.
/// </summary>
public enum CompatibilitySeam
{
    MessageLoop,
    WindowHandle,
    ModalDialog,
    GdiPlusPaint,
    FileSystemDialogs,
    RegistryAndConfig,
    ClipboardDragDrop,
    Printing,
    ThreadingModel,
}
