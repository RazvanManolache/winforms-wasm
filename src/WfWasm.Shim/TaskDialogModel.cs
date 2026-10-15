namespace WfWasm.Shim;

/// <summary>Renderer-neutral progress state (cf. TaskDialogProgressBarState).</summary>
public enum DialogProgressState
{
    None,
    Normal,
    Marquee,
    Paused,
    Error,
}

/// <summary>
/// Renderer-neutral button (cf. TaskDialogButton): identity plus the flags the
/// baseline samples mutate at runtime (Enabled, Visible, AllowClose).
/// </summary>
public sealed class DialogButtonModel
{
    public DialogButtonModel(string id, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Text = text;
    }

    public string Id { get; }
    public string Text { get; }
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool AllowClose { get; set; } = true;
    public bool IsDefault { get; set; }
}

/// <summary>Outcome of clicking a button: whether the dialog closes and which button fired.</summary>
public sealed record DialogClickOutcome(bool DialogClosed, string? ClickedId);

/// <summary>
/// Renderer-neutral dialog page (cf. TaskDialogPage). Property changes raise
/// <see cref="PageChanged"/> so the renderer refreshes live Text/Progress/
/// Expander updates like the auto-closing and multi-page baseline samples.
/// </summary>
public sealed class DialogPageModel
{
    private string? _text;
    private DialogProgressState _progressState = DialogProgressState.None;
    private int _progressValue;
    private string? _expanderText;
    private bool _verificationChecked;

    public string? Heading { get; set; }
    public bool AllowCancel { get; set; }

    public string? Text
    {
        get => _text;
        set { _text = value; PageChanged?.Invoke(this, EventArgs.Empty); }
    }

    public DialogProgressState ProgressState
    {
        get => _progressState;
        set { _progressState = value; PageChanged?.Invoke(this, EventArgs.Empty); }
    }

    public int ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; PageChanged?.Invoke(this, EventArgs.Empty); }
    }

    public string? VerificationText { get; set; }

    public bool VerificationChecked
    {
        get => _verificationChecked;
        set
        {
            if (_verificationChecked == value)
                return;
            _verificationChecked = value;
            VerificationChanged?.Invoke(this, EventArgs.Empty);
            PageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? ExpanderText
    {
        get => _expanderText;
        set { _expanderText = value; PageChanged?.Invoke(this, EventArgs.Empty); }
    }

    public bool ExpanderExpanded { get; set; }

    public List<DialogButtonModel> Buttons { get; } = new();

    public event EventHandler? PageChanged;
    public event EventHandler? VerificationChanged;

    public string? DefaultButtonId => Buttons
        .FirstOrDefault(b => b.IsDefault && b.Enabled && b.Visible)?.Id;

    /// <summary>
    /// Click a button by id. Disabled/hidden/missing buttons are no-ops.
    /// A button with AllowClose=false fires but keeps the dialog open
    /// (cf. the invisible-Cancel and navigation patterns in the baseline).
    /// </summary>
    public DialogClickOutcome ClickButton(string id)
    {
        DialogButtonModel? button = Buttons.FirstOrDefault(b => b.Id == id);
        if (button is null || !button.Enabled || !button.Visible)
            return new DialogClickOutcome(false, null);
        return new DialogClickOutcome(button.AllowClose, button.Id);
    }
}
