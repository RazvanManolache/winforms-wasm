extern alias WfDraw;

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace WfWasm.Shim;

/// <summary>App lifetime (cf. System.Windows.Forms.Application): open-form
/// registry, exit/idle plumbing, path info, and the UI-thread pump.
/// Run blocks and pumps posted work on the calling thread until the main
/// form closes or Exit is called, mirroring WinForms; hosts that own their
/// own loop (browser canvas pump) never call Run and keep inline behavior.</summary>
public static class Application
{
    private static volatile bool _running;
    private static volatile bool _exitRequested;
    private static volatile int _pumpThreadId = -1;
    private static readonly ConcurrentQueue<UiWorkItem> _uiQueue = new();
    private static readonly AutoResetEvent _uiWake = new(false);

    public static FormCollection OpenForms { get; } = new();

    public static bool MessageLoop => _running && !_exitRequested;

    /// <summary>True while a Run pump owns the calling thread's loop.</summary>
    internal static bool Pumping => _pumpThreadId >= 0;

    /// <summary>True when the caller runs on the pump thread.</summary>
    internal static bool IsPumpThread =>
        Environment.CurrentManagedThreadId == _pumpThreadId;

    public static string ExecutablePath =>
        Environment.ProcessPath ?? AppContext.BaseDirectory;

    public static string StartupPath => AppContext.BaseDirectory;

    public static void Run()
    {
        ClaimPumpThread();
        RunLoop(null);
    }

    public static void Run(Form mainForm)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        ClaimPumpThread();
        if (!OpenForms.Contains(mainForm))
            OpenForms.Add(mainForm);
        Form.ActiveForm = mainForm;
        mainForm.Visible = true;
        RunLoop(mainForm);
    }

    /// <summary>Claims the pump thread before any posted work can observe it.
    /// Must precede Visible=true: a worker that sees a shown form must also
    /// see Pumping, otherwise its Post would run inline on the wrong thread.</summary>
    private static void ClaimPumpThread()
    {
        _running = true;
        _pumpThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>Blocks pumping queued UI work until Exit or main-form close.</summary>
    private static void RunLoop(Form? mainForm)
    {
        _exitRequested = false;
        EnsureContextInstalled();
        try
        {
            while (!_exitRequested && !(mainForm?.IsDisposed ?? false))
            {
                PumpOnce();
                RaiseIdle();
                if (!_exitRequested && !(mainForm?.IsDisposed ?? false))
                    _uiWake.WaitOne(16);
            }
        }
        finally
        {
            _pumpThreadId = -1;
            _running = false;
        }
    }

    /// <summary>Runs queued UI callbacks once without blocking.</summary>
    internal static void PumpOnce()
    {
        while (_uiQueue.TryDequeue(out UiWorkItem? work))
        {
            try
            {
                object? result = work.Run();
                work.Completion?.SetResult(result);
            }
            catch (Exception ex)
            {
                if (work.Completion is not null)
                    work.Completion.SetException(ex);
                else
                    RaiseThreadException(ex);
            }
        }
    }

    /// <summary>Enqueues a result thunk for the pump thread.</summary>
    internal static void PostToUiThread(Func<object?> run, PendingInvoke? completion = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        _uiQueue.Enqueue(new UiWorkItem(run, completion));
        _uiWake.Set();
    }

    /// <summary>Enqueues a callback for the pump thread.</summary>
    internal static void PostToUiThread(SendOrPostCallback callback, object? state, PendingInvoke? completion = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        PostToUiThread(() =>
        {
            callback(state);
            return null;
        }, completion);
    }

    /// <summary>Installs the WinForms sync context when the thread still has
    /// the default, mirroring handle-creation install (cf.
    /// WindowsFormsSynchronizationContext). Custom contexts are preserved.</summary>
    internal static void EnsureContextInstalled()
    {
        var current = SynchronizationContext.Current;
        if (current is null || current.GetType() == typeof(SynchronizationContext))
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    }

    public static void Exit()
    {
        _exitRequested = true;
        _uiWake.Set();
        ApplicationExit?.Invoke(null, EventArgs.Empty);
    }

    public static void ExitThread()
    {
        Exit();
    }

    public static void DoEvents()
    {
        PumpOnce();
    }

    public static void EnableVisualStyles()
    {
        RenderingConfig.UseVisualStyles = true;
    }

    public static void SetCompatibleTextRenderingDefault(bool defaultValue)
    {
    }

    /// <summary>Whether visual styles render (cf. RenderWithVisualStyles).</summary>
    public static bool RenderWithVisualStyles => RenderingConfig.UseVisualStyles;

    /// <summary>Raised when a modal loop starts (cf. EnterThreadModal). Never raised: no blocking dialogs.</summary>
    public static event EventHandler? EnterThreadModal;

    /// <summary>Raised when a modal loop ends (cf. LeaveThreadModal). Never raised: no blocking dialogs.</summary>
    public static event EventHandler? LeaveThreadModal;

    private static void OnEnterThreadModal(EventArgs e) => EnterThreadModal?.Invoke(null, e);

    private static void OnLeaveThreadModal(EventArgs e) => LeaveThreadModal?.Invoke(null, e);

    public static void SetUnhandledExceptionMode(UnhandledExceptionMode mode)
    {
    }

    public static void SetUnhandledExceptionMode(UnhandledExceptionMode mode, bool throwIfNoDebugger)
    {
        _ = throwIfNoDebugger;
    }

    public static event EventHandler? ApplicationExit;
    public static event EventHandler? Idle;
    public static event System.Threading.ThreadExceptionEventHandler? ThreadException;

    internal static void RaiseIdle() => Idle?.Invoke(null, EventArgs.Empty);

    internal static void RaiseThreadException(Exception exception) =>
        ThreadException?.Invoke(null, new System.Threading.ThreadExceptionEventArgs(exception));

    internal static void RegisterForm(Form form)
    {
        if (!OpenForms.Contains(form))
            OpenForms.Add(form);
    }

    internal static void UnregisterForm(Form form) => OpenForms.Remove(form);
}

/// <summary>Queued UI callback with its optional waiter.</summary>
internal sealed record UiWorkItem(Func<object?> Run, PendingInvoke? Completion);

/// <summary>Marshal result (cf. IAsyncResult from BeginInvoke).</summary>
internal sealed class PendingInvoke : IAsyncResult
{
    private readonly ManualResetEventSlim _done = new(false);
    private object? _result;
    private ExceptionDispatchInfo? _error;

    public object? AsyncState => _result;
    public WaitHandle AsyncWaitHandle => _done.WaitHandle;
    public bool CompletedSynchronously => false;
    public bool IsCompleted => _done.IsSet;

    internal void SetResult(object? result)
    {
        _result = result;
        _done.Set();
    }

    internal void SetException(Exception exception) =>
        SetError(ExceptionDispatchInfo.Capture(exception));

    internal void SetError(ExceptionDispatchInfo error)
    {
        _error = error;
        _done.Set();
    }

    internal object? End()
    {
        _done.Wait();
        _error?.Throw();
        return _result;
    }
}

/// <summary>UI-thread marshaler (cf.
/// System.Windows.Forms.WindowsFormsSynchronizationContext): Post queues for
/// the Run pump, Send blocks until the pump runs the callback. Without a
/// pump (browser host owns the loop) both run inline.</summary>
public sealed class WindowsFormsSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (!Application.Pumping)
        {
            d(state);
            return;
        }
        Application.PostToUiThread(d, state);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (SynchronizationContext.Current == this || !Application.Pumping)
        {
            d(state);
            return;
        }
        var completion = new PendingInvoke();
        Application.PostToUiThread(d, state, completion);
        completion.End();
    }

    public override SynchronizationContext CreateCopy() => new WindowsFormsSynchronizationContext();
}

/// <summary>Unhandled-exception policy (cf. UnhandledExceptionMode).</summary>
public enum UnhandledExceptionMode
{
    Automatic = 0,
    ThrowException = 1,
    CatchException = 2,
}

/// <summary>Open-form registry (cf. FormCollection).</summary>
public class FormCollection : System.Collections.ObjectModel.ReadOnlyCollection<Form>
{
    public FormCollection()
        : base(new List<Form>())
    {
    }

    internal void Add(Form form) => ((List<Form>)Items).Add(form);
    internal void Remove(Form form) => ((List<Form>)Items).Remove(form);
}

/// <summary>Display info (cf. System.Windows.Forms.Screen).</summary>
public class Screen
{
    public static Screen PrimaryScreen { get; } = new(true);
    public static Screen[] AllScreens { get; } = [PrimaryScreen];

    private Screen(bool primary)
    {
        Primary = primary;
    }

    public bool Primary { get; }
    public string DeviceName => "";
    public bool MultiMonitor => false;
    public int BitsPerPixel => 32;

    public WfDraw::System.Drawing.Rectangle Bounds =>
        new(0, 0, CanvasApp.ViewWidth, CanvasApp.ViewHeight);

    public WfDraw::System.Drawing.Rectangle WorkingArea => Bounds;

    public static Screen FromControl(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return PrimaryScreen;
    }

    public static Screen FromHandle(IntPtr handle) => PrimaryScreen;

    public static Screen FromPoint(WfDraw::System.Drawing.Point point) => PrimaryScreen;

    public static Screen FromRectangle(WfDraw::System.Drawing.Rectangle rect) => PrimaryScreen;

    public static WfDraw::System.Drawing.Rectangle GetBounds(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return PrimaryScreen.Bounds;
    }

    public static WfDraw::System.Drawing.Rectangle GetBounds(WfDraw::System.Drawing.Point point) =>
        PrimaryScreen.Bounds;

    public static WfDraw::System.Drawing.Rectangle GetBounds(WfDraw::System.Drawing.Rectangle rect) =>
        PrimaryScreen.Bounds;

    public static WfDraw::System.Drawing.Rectangle GetWorkingArea(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return PrimaryScreen.WorkingArea;
    }

    public static WfDraw::System.Drawing.Rectangle GetWorkingArea(WfDraw::System.Drawing.Point point) =>
        PrimaryScreen.WorkingArea;

    public static WfDraw::System.Drawing.Rectangle GetWorkingArea(WfDraw::System.Drawing.Rectangle rect) =>
        PrimaryScreen.WorkingArea;
}

/// <summary>System metrics (cf. SystemInformation).</summary>
public static class SystemInformation
{
    public static WfDraw::System.Drawing.Size DragSize => new(4, 4);
    public static bool HighContrast => false;
    public static int MouseWheelScrollLines => 3;
    public static int MouseWheelScrollDelta => 120;

    /// <summary>3-D border size (cf. Border3DSize).</summary>
    public static WfDraw::System.Drawing.Size Border3DSize => new(2, 2);

    /// <summary>Double-click spread (cf. DoubleClickSize).</summary>
    public static WfDraw::System.Drawing.Size DoubleClickSize => new(4, 4);

    /// <summary>Double-click window in ms (cf. DoubleClickTime).</summary>
    public static int DoubleClickTime => 500;

    /// <summary>Font smoothing style (cf. FontSmoothingType). 0: none available.</summary>
    public static int FontSmoothingType => 0;

    /// <summary>Whether font smoothing is on (cf. IsFontSmoothingEnabled). False.</summary>
    public static bool IsFontSmoothingEnabled => false;

    /// <summary>Whether hot tracking is on (cf. IsHotTrackingEnabled). False.</summary>
    public static bool IsHotTrackingEnabled => false;

    /// <summary>Keyboard repeat delay (cf. KeyboardDelay). 1: matches the OS default index.</summary>
    public static int KeyboardDelay => 1;

    /// <summary>Keyboard repeat speed (cf. KeyboardSpeed). 31: matches the OS default index.</summary>
    public static int KeyboardSpeed => 31;
    public static int HorizontalScrollBarHeight => 17;
    public static int VerticalScrollBarWidth => 17;
    public static int CaptionHeight => Form.TitleBarHeight;
    public static WfDraw::System.Drawing.Size BorderSize => new(1, 1);
    public static bool MenuAccessKeysUnderlined => false;
    public static bool UserInteractive => true;
}
