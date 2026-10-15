extern alias WfDraw;

using System.ComponentModel;

namespace WfWasm.Shim;

/// <summary>Interval tick (cf. System.Windows.Forms.Timer): pool-thread
/// backed; the host marshals Tick onto the UI pump when it mounts
/// rewritten apps (browser), headless runs observe it directly.</summary>
public class Timer : Component
{
    public Timer()
    {
    }

    public Timer(IContainer? container)
    {
        container?.Add(this);
    }

    private System.Threading.Timer? _timer;
    private int _interval = 100;
    private bool _enabled;

    public int Interval
    {
        get => _interval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _interval = value;
            if (_enabled)
                Restart();
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            if (value)
                StartCore();
            else
                StopCore();
        }
    }

    public event EventHandler? Tick;

    public void Start() => Enabled = true;
    public void Stop() => Enabled = false;

    private void StartCore()
    {
        StopCore();
        _timer = new System.Threading.Timer(
            _ => OnTick(EventArgs.Empty), null, _interval, _interval);
    }

    private void StopCore()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Restart()
    {
        if (_enabled)
            StartCore();
    }

    protected virtual void OnTick(EventArgs e) => Tick?.Invoke(this, e);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            StopCore();
        base.Dispose(disposing);
    }
}

/// <summary>Hover tip (cf. System.Windows.Forms.ToolTip): tip registry with
/// headless Show/Hide; the host renders tips when it pumps hover.</summary>
public class ToolTip : Component
{
    public ToolTip()
    {
    }

    public ToolTip(IContainer? container)
    {
        container?.Add(this);
    }

    private readonly Dictionary<Control, string> _tips = new();

    public int AutomaticDelay { get; set; } = 500;
    public int AutoPopDelay { get; set; } = 5000;
    public int InitialDelay { get; set; } = 500;
    public int ReshowDelay { get; set; } = 100;
    public bool ShowAlways { get; set; }
    public bool Active { get; set; } = true;
    public bool IsBalloon { get; set; }

    public void SetToolTip(Control control, string caption)
    {
        ArgumentNullException.ThrowIfNull(control);
        _tips[control] = caption;
    }

    public string GetToolTip(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _tips.TryGetValue(control, out string? tip) ? tip : "";
    }

    public void RemoveAll() => _tips.Clear();

    public void Show(string text, IWin32Window window) => Show(text, window, 0);

    public void Show(string text, IWin32Window window, int duration)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(window);
    }

    public void Show(
        string text,
        IWin32Window window,
        WfDraw::System.Drawing.Point point) =>
        Show(text, window, point, 0);

    public void Show(
        string text,
        IWin32Window window,
        WfDraw::System.Drawing.Point point,
        int duration)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(window);
    }

    public void Hide(IWin32Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
    }
}
