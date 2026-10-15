namespace WfWasm.Shim;

/// <summary>
/// Single-threaded UI queue replacing the Win32 message loop.
/// The constructing thread owns the loop (STA-like): only it may <see cref="Pump"/>.
/// </summary>
public sealed class BrowserMessageLoop
{
    private readonly int _ownerThreadId;
    private readonly Queue<WorkItem> _queue = new();
    private readonly object _gate = new();

    public BrowserMessageLoop() => _ownerThreadId = Environment.CurrentManagedThreadId;

    /// <summary>True when the caller is not on the loop thread (cf. Control.InvokeRequired).</summary>
    public bool InvokeRequired => Environment.CurrentManagedThreadId != _ownerThreadId;

    public int PendingCount
    {
        get { lock (_gate) return _queue.Count; }
    }

    /// <summary>Queue an action (cf. Control.BeginInvoke without a result).</summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Enqueue(new WorkItem(action, null));
    }

    /// <summary>Queue an action and complete the task after it runs.</summary>
    public Task PostAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var done = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new WorkItem(action, done));
        return done.Task;
    }

    /// <summary>Queue a function and complete the task with its result.</summary>
    public Task<T> PostAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new WorkItem(() =>
        {
            try
            {
                done.TrySetResult(func());
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }, null));
        return done.Task;
    }

    /// <summary>
    /// Run on the loop thread, like Control.Invoke. Inline when already on the
    /// loop thread (never deadlocks); otherwise blocks until pumped.
    /// </summary>
    public T Send<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (!InvokeRequired)
            return func();
        return PostAsync(func).GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="Send{T}(Func{T})" />
    public void Send(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Send<object?>(() => { action(); return null; });
    }

    /// <summary>Run up to <paramref name="maxActions"/> queued actions. Returns the count run.</summary>
    public int Pump(int maxActions = int.MaxValue)
    {
        if (InvokeRequired)
            throw new InvalidOperationException("Pump must run on the loop thread.");
        if (maxActions < 0)
            throw new ArgumentOutOfRangeException(nameof(maxActions));

        int ran = 0;
        while (ran < maxActions)
        {
            WorkItem next;
            lock (_gate)
            {
                if (_queue.Count == 0)
                    break;
                next = _queue.Dequeue();
            }

            next.Execute();
            ran++;
        }

        return ran;
    }

    private void Enqueue(WorkItem item)
    {
        lock (_gate)
            _queue.Enqueue(item);
    }

    private sealed record WorkItem(Action Action, TaskCompletionSource<object?>? Completion)
    {
        public void Execute()
        {
            try
            {
                Action();
                Completion?.TrySetResult(null);
            }
            catch (Exception ex)
            {
                if (Completion is null)
                    throw;
                Completion.TrySetException(ex);
            }
        }
    }
}
