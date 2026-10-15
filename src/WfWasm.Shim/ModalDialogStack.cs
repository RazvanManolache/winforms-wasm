namespace WfWasm.Shim;

/// <summary>
/// Structural dialog stack replacing blocking ShowDialog. Show returns a Task
/// that completes on close (the web has no blocking modal loop); NavigateTo
/// swaps the top page while keeping its Task (cf. TaskDialogPage.Navigate).
/// </summary>
public sealed class ModalDialogStack<TPage, TResult>
{
    private readonly Stack<Frame> _frames = new();

    public event EventHandler? Changed;

    public int Count => _frames.Count;

    public TPage Top => _frames.Count == 0
        ? throw new InvalidOperationException("No dialog is shown.")
        : _frames.Peek().Page;

    public bool IsModalTop => _frames.Count > 0 && _frames.Peek().Modal;

    public Task<TResult> ShowAsync(TPage page, bool modal = true)
    {
        ArgumentNullException.ThrowIfNull(page);
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _frames.Push(new Frame(page, modal, completion));
        Changed?.Invoke(this, EventArgs.Empty);
        return completion.Task;
    }

    public void NavigateTo(TPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (_frames.Count == 0)
            throw new InvalidOperationException("No dialog is shown.");
        _frames.Peek().Page = page;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool TryCloseTop(TResult result)
    {
        if (_frames.Count == 0)
            return false;
        Frame frame = _frames.Pop();
        frame.Completion.TrySetResult(result);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void CloseTop(TResult result)
    {
        if (!TryCloseTop(result))
            throw new InvalidOperationException("No dialog is shown.");
    }

    private sealed class Frame
    {
        public Frame(TPage page, bool modal, TaskCompletionSource<TResult> completion)
        {
            Page = page;
            Modal = modal;
            Completion = completion;
        }

        public TPage Page;
        public readonly bool Modal;
        public readonly TaskCompletionSource<TResult> Completion;
    }
}
