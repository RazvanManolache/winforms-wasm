// Headless label-edit overlay: replaces the runtimes/winforms
// (System/Windows/Forms/Accessibility/LabelEditNativeWindow.cs,
// Compile-Removed in the csproj). Upstream InstallWinEventHooks
// marshals the WINEVENTPROC delegate with
// GetFunctionPointerForDelegate, which aborts the browser runtime
// with a wasm out-of-bounds trap (same class as CommonDialog..ctor).
// No assistive tech listens headless, so hook installation is a
// no-op; the accessibility-object plumbing is unchanged.
// Regression: func96 OOB <- InstallWinEventHooks <- label edit
// (latent; same trap class as the dialog ctor).
using Windows.Win32.UI.Accessibility;

namespace System.Windows.Forms;

internal class LabelEditNativeWindow : NativeWindow
{
    private const uint TextSelectionChanged = 0x8014;

    private readonly WeakReference<Control> _owningControl;
    private WINEVENTPROC? _winEventProcCallback;
    private HWINEVENTHOOK _valueChangeHook;
    private HWINEVENTHOOK _textSelectionChangedHook;
    private bool _winEventHooksInstalled;
    private bool _isReleasing;

    private delegate void WINEVENTPROC(
        HWINEVENTHOOK hWinEventHook,
        uint @event,
        HWND hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime);

    public LabelEditNativeWindow(Control owningControl)
    {
        ArgumentNullException.ThrowIfNull(owningControl);

        _owningControl = new(owningControl);
    }

    public virtual AccessibleObject? AccessibilityObject { get; set; }

    private void InstallWinEventHooks()
    {
        // No-op headless: installing the hooks needs a native function
        // pointer for the callback, and generating the thunk aborts the
        // runtime. No assistive tech runs in the browser to consume them.
        _winEventHooksInstalled = false;
    }

    private bool IsAccessibilityObjectCreated => AccessibilityObject is not null;

    public bool IsHandleCreated => Handle != HWND.Null;

    protected override void OnHandleChange()
    {
        base.OnHandleChange();

        // Prevent reentrant hook installation during ReleaseHandle.
        // Without this guard, UnhookWinEvent can trigger OnHandleChange (via NativeWindow.Callback),
        // causing hooks to be reinstalled on a window that is being destroyed.
        if (_isReleasing)
        {
            return;
        }

        if (!IsHandleCreated)
        {
            // The native window handle was destroyed (e.g., Windows sent WM_NCDESTROY and NativeWindow
            // cleared the handle without going through our ReleaseHandle override). Unhook the WinEvent
            // hooks immediately so the WINEVENT_INCONTEXT hook cannot fire after _winEventProcCallback is
            // garbage collected. Without this, the LabelEditNativeWindow is removed from the NativeWindow
            // handle table and becomes eligible for GC while the hooks remain registered. The next
            // CallWindowProc on any window on this thread (e.g. GridViewTextBox.DefWndProc) would then
            // invoke the GC'd delegate and trigger a FailFast.
            UnhookWinEventHooks();
            return;
        }

        // Install winevent hooks *only* when the parent Control has an existing accessible object.
        // If we don't install hooks at the label edit startup then assistive tech (e.g. Narrator) won't announce the text pattern for it.
        // By invoking UiaClientsAreListening we will have hooks installed even if the assistive tech isn't currently run.
        if (_owningControl.TryGetTarget(out Control? target) && !target.IsAccessibilityObjectCreated)
        {
            return;
        }

        InstallWinEventHooks();
    }

    public override unsafe void ReleaseHandle()
    {
        _isReleasing = true;
        try
        {
            UnhookWinEventHooks();

            if (IsHandleCreated)
            {
                // When a window that previously returned providers has been destroyed,
                // you should notify UI Automation by calling the UiaReturnRawElementProvider
                // as follows: UiaReturnRawElementProvider(hwnd, 0, 0, NULL). This call tells
                // UI Automation that it can safely remove all map entries that refer to the specified window.
                PInvoke.UiaReturnRawElementProvider(HWND, wParam: 0, lParam: 0, (IRawElementProviderSimple*)null);
            }

            PInvoke.UiaDisconnectProvider(AccessibilityObject);
            AccessibilityObject = null;
            base.ReleaseHandle();
        }
        finally
        {
            _isReleasing = false;
        }
    }

    private void UnhookWinEventHooks()
    {
        if (_winEventHooksInstalled)
        {
            PInvoke.UnhookWinEvent(_valueChangeHook);
            PInvoke.UnhookWinEvent(_textSelectionChangedHook);
            _winEventHooksInstalled = false;

            // Allow the delegate to be garbage collected now that no hook holds a reference
            // to its underlying unmanaged function pointer.
            _winEventProcCallback = null;
        }
    }

    private void WinEventProcCallback(HWINEVENTHOOK hWinEventHook, uint eventId, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        // Late-arriving callbacks should be ignored if we are releasing or hooks are already uninstalled.
        if (_isReleasing || !_winEventHooksInstalled)
        {
            return;
        }

        if (hwnd != Handle || idObject != (int)OBJECT_IDENTIFIER.OBJID_CLIENT || !IsAccessibilityObjectCreated)
        {
            return;
        }

        switch (eventId)
        {
            case (uint)AccessibleEvents.ValueChange:
                AccessibilityObject?.RaiseAutomationEvent(UIA_EVENT_ID.UIA_Text_TextChangedEventId);
                break;
            case TextSelectionChanged:
                AccessibilityObject?.RaiseAutomationEvent(UIA_EVENT_ID.UIA_Text_TextSelectionChangedEventId);
                break;
        }
    }

    private void WmGetObject(ref Message m)
    {
        AccessibleObject? EnsureWinEventHooksInstalledAndGetAccessibilityObject()
        {
            AccessibleObject? accessibilityObject = AccessibilityObject;

            // Accessibility object was likely requested by an assistive tech (which sent WM_GETOBJECT message).
            // We may need to install winevent hooks to produce the automation events related to the text pattern.
            // However, if we are in the process of releasing the handle, skip hook installation to avoid
            // creating new hooks on a window that is being destroyed.
            if (!_winEventHooksInstalled && !_isReleasing)
            {
                InstallWinEventHooks();
            }

            return accessibilityObject;
        }

        if (m.LParamInternal == PInvoke.UiaRootObjectId)
        {
            // If the requested object identifier is UiaRootObjectId,
            // we should return an UI Automation provider using the UiaReturnRawElementProvider function.
            m.ResultInternal = PInvoke.UiaReturnRawElementProvider(
                this,
                m.WParamInternal,
                m.LParamInternal,
                EnsureWinEventHooksInstalledAndGetAccessibilityObject());

            return;
        }

        if ((int)m.LParamInternal != (int)OBJECT_IDENTIFIER.OBJID_CLIENT)
        {
            DefWndProc(ref m);

            return;
        }

        try
        {
            m.ResultInternal = EnsureWinEventHooksInstalledAndGetAccessibilityObject()!.GetLRESULT(m.WParamInternal);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(SR.RichControlLresult, ex);
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.MsgInternal)
        {
            case PInvokeCore.WM_GETOBJECT:
                WmGetObject(ref m);
                return;
            default:
                base.WndProc(ref m);
                return;
        }
    }
}
