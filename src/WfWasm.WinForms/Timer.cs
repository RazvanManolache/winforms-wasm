using System.ComponentModel;

namespace System.Windows.Forms;

// Headless System.Windows.Forms.Timer (fork overlay: the upstream file is
// excluded in WfWasm.WinForms.csproj). The desktop implementation pumps
// WM_TIMER through a native window, which cannot exist in the browser —
// without this, every headless timer silently never ticks (Telerik
// animations freeze at their start values, clocks and polling UI stall).
// Same public contract (Tag included: Telerik's animation settings stash
// their element in it); ticks come from a System.Threading.Timer firing
// one-shot callbacks that reschedule, so Tick handlers never overlap the
// way message coalescing prevents on desktop. Callbacks arrive on pool
// threads: control access from Tick follows the same Invoke marshaling
// rules as desktop. A throwing Tick is reported to the console and the
// timer keeps running instead of taking down the runtime, matching the
// desktop "dialog and continue" outcome as closely as headless allows.
[DefaultProperty(nameof(Interval))]
[DefaultEvent(nameof(Tick))]
[ToolboxItemFilter("System.Windows.Forms")]
public class Timer : Component
{
    private int _interval = 100;
    private bool _enabled;
    private System.Threading.Timer? _timer;
    private readonly Lock _lock = new();

    public Timer()
    {
    }

    public Timer(IContainer container)
        : this()
    {
        ArgumentNullException.ThrowIfNull(container);

        container.Add(this);
    }

    [DefaultValue(null)]
    public object? Tag { get; set; }

    private protected EventHandler? _onTimer;

    public event EventHandler Tick
    {
        add => _onTimer += value;
        remove => _onTimer -= value;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                StopTimerLocked();
                _enabled = false;
            }
        }

        base.Dispose(disposing);
    }

    [DefaultValue(false)]
    public virtual bool Enabled
    {
        get => _enabled;
        set
        {
            lock (_lock)
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;

                if (DesignMode)
                {
                    return;
                }

                if (value)
                {
                    StartTimerLocked();
                }
                else
                {
                    StopTimerLocked();
                }
            }
        }
    }

    [DefaultValue(100)]
    public int Interval
    {
        get => _interval;
        set
        {
            lock (_lock)
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"Timer interval must be at least 1ms, got {value}.");
                }

                if (_interval == value)
                {
                    return;
                }

                _interval = value;

                if (_enabled && !DesignMode)
                {
                    // Change the cadence without dropping the enabled state.
                    StartTimerLocked();
                }
            }
        }
    }

    protected virtual void OnTick(EventArgs e) => _onTimer?.Invoke(this, e);

    public void Start() => Enabled = true;

    public void Stop() => Enabled = false;

    public override string ToString() => $"{base.ToString()}, Interval: {Interval}";

    private void StartTimerLocked()
    {
        StopTimerLocked();
        _timer = new System.Threading.Timer(
            static state => ((Timer)state!).FireOnce(),
            this,
            _interval,
            System.Threading.Timeout.Infinite);
    }

    private void StopTimerLocked()
    {
        System.Threading.Timer? timer = _timer;
        _timer = null;
        timer?.Dispose();
    }

    private void FireOnce()
    {
        try
        {
            OnTick(EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Desktop shows the thread-exception dialog and keeps pumping;
            // headless the console is the dialog. Never let one bad Tick
            // kill the runtime or wedge the cadence.
            Console.Error.WriteLine($"[WfWasm.Timer] Tick failed: {ex.GetType().Name}: {ex.Message}");
        }

        lock (_lock)
        {
            if (_enabled && _timer is not null)
            {
                try
                {
                    _timer.Change(_interval, System.Threading.Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // Lost a stop/dispose race: stay stopped.
                    _timer = null;
                }
            }
        }
    }
}
