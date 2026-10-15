extern alias WfDraw;

namespace WfWasm.Shim;

using Draw = WfDraw::System.Drawing;

/// <summary>Dialog base (cf. CommonDialog). ShowDialog returns OK without a
/// native picker until the host file-picker milestone.</summary>
public abstract class CommonDialog : System.ComponentModel.Component
{
    public virtual void Reset()
    {
    }

    protected abstract bool RunDialog(IntPtr hwndOwner);

    public DialogResult ShowDialog() =>
        RunDialog(IntPtr.Zero) ? DialogResult.OK : DialogResult.Cancel;

    public DialogResult ShowDialog(IWin32Window? owner) =>
        RunDialog(IntPtr.Zero) ? DialogResult.OK : DialogResult.Cancel;

    public override string ToString() => base.ToString() ?? GetType().Name;
}

/// <summary>Folder picker (cf. FolderBrowserDialog). ShowDialog returns OK
/// with the preset SelectedPath until the host picker milestone.</summary>
public class FolderBrowserDialog : CommonDialog
{
    public string Description { get; set; } = string.Empty;
    public string SelectedPath { get; set; } = string.Empty;

    /// <summary>Compat slot: some apps read InitialDirectory off the folder
    /// dialog (real member lives on FileDialog); mirrors SelectedPath.</summary>
    public string InitialDirectory
    {
        get => SelectedPath;
        set => SelectedPath = value;
    }

    public Environment.SpecialFolder RootFolder { get; set; } = Environment.SpecialFolder.Desktop;
    public bool ShowNewFolderButton { get; set; } = true;
    public bool ShowHiddenFiles { get; set; }
    public bool AutoUpgradeEnabled { get; set; } = true;
    public bool UseDescriptionForTitle { get; set; }

    protected override bool RunDialog(IntPtr hwndOwner) => true;

    public override void Reset()
    {
        Description = string.Empty;
        SelectedPath = string.Empty;
        RootFolder = Environment.SpecialFolder.Desktop;
        ShowNewFolderButton = true;
    }
}

/// <summary>
/// A custom file-dialog shortcut (cf. System.Windows.Forms.FileDialogCustomPlace).
/// Carries either a file-system path or a known-folder GUID.
/// </summary>
public class FileDialogCustomPlace
{
    private string? _path;
    private Guid _knownFolderGuid;

    /// <summary>Creates a path-based shortcut (cf. FileDialogCustomPlace(string)).</summary>
    /// <param name="path">File-system path.</param>
    public FileDialogCustomPlace(string path)
    {
        _path = path;
    }

    /// <summary>Creates a known-folder shortcut (cf. FileDialogCustomPlace(Guid)).</summary>
    /// <param name="knownFolderGuid">Known-folder GUID.</param>
    public FileDialogCustomPlace(Guid knownFolderGuid)
    {
        _knownFolderGuid = knownFolderGuid;
    }

    /// <summary>File-system path (cf. Path).</summary>
    public string? Path
    {
        get => _path;
        set => _path = value;
    }

    /// <summary>Known-folder GUID (cf. KnownFolderGuid).</summary>
    public Guid KnownFolderGuid
    {
        get => _knownFolderGuid;
        set => _knownFolderGuid = value;
    }

    /// <inheritdoc/>
    public override string ToString() => _path ?? _knownFolderGuid.ToString();
}

/// <summary>
/// Shortcut list for file dialogs (cf. System.Windows.Forms.FileDialogCustomPlacesCollection).
/// Stored only; the web backend has no native picker to show them in.
/// </summary>
public class FileDialogCustomPlacesCollection : System.Collections.ObjectModel.Collection<string>
{
    /// <summary>Adds a path shortcut (cf. Add(string)).</summary>
    /// <param name="path">File-system path.</param>
    public new void Add(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        base.Add(path);
    }

    /// <summary>Adds a known-folder shortcut (cf. Add(Guid)).</summary>
    /// <param name="knownFolder">Known-folder GUID.</param>
    public void Add(Guid knownFolder) => Add(knownFolder.ToString());
}

/// <summary>
/// File picker base (cf. System.Windows.Forms.FileDialog). ShowDialog returns
/// Cancel: the web backend has no native file picker (host picker milestone).
/// <c>FileName</c> may still be set programmatically and <c>OpenFile</c> opens
/// it when the file exists, so headless flows keep working.
/// </summary>
public abstract class FileDialog : CommonDialog
{
    private string _fileName = string.Empty;
    private readonly FileDialogCustomPlacesCollection _customPlaces = new();

    /// <summary>Raised when the user accepts the dialog (cf. FileOk).</summary>
    public event System.ComponentModel.CancelEventHandler? FileOk;

    /// <summary>Appends the default extension (cf. AddExtension).</summary>
    public bool AddExtension { get; set; } = true;

    /// <summary>Adds the choice to recent documents (cf. AddToRecent).</summary>
    public bool AddToRecent { get; set; } = true;

    /// <summary>Uses the new-style dialog (cf. AutoUpgradeEnabled).</summary>
    public bool AutoUpgradeEnabled { get; set; } = true;

    /// <summary>Requires the file to exist (cf. CheckFileExists).</summary>
    public virtual bool CheckFileExists { get; set; }

    /// <summary>Requires the path to exist (cf. CheckPathExists).</summary>
    public bool CheckPathExists { get; set; } = true;

    /// <summary>Client GUID for persisted state (cf. ClientGuid).</summary>
    public Guid? ClientGuid { get; set; }

    /// <summary>Custom shortcuts (cf. CustomPlaces).</summary>
    public FileDialogCustomPlacesCollection CustomPlaces => _customPlaces;

    /// <summary>Default extension (cf. DefaultExt).</summary>
    public string DefaultExt { get; set; } = string.Empty;

    /// <summary>Follows shortcuts (cf. DereferenceLinks).</summary>
    public bool DereferenceLinks { get; set; } = true;

    /// <summary>Selected file (cf. FileName).</summary>
    public string FileName
    {
        get => _fileName;
        set => _fileName = value ?? string.Empty;
    }

    /// <summary>Selected files (cf. FileNames).</summary>
    public string[] FileNames => _fileName.Length == 0 ? [] : [_fileName];

    /// <summary>Filter expression (cf. Filter).</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Active filter index, 1-based (cf. FilterIndex).</summary>
    public int FilterIndex { get; set; } = 1;

    /// <summary>Starting directory (cf. InitialDirectory).</summary>
    public string InitialDirectory { get; set; } = string.Empty;

    /// <summary>Requires user interaction before OK (cf. OkRequiresInteraction).</summary>
    public bool OkRequiresInteraction { get; set; } = true;

    /// <summary>Restores the working directory (cf. RestoreDirectory).</summary>
    public bool RestoreDirectory { get; set; }

    /// <summary>Shows the help button (cf. ShowHelp).</summary>
    public bool ShowHelp { get; set; }

    /// <summary>Shows the pinned-places bar (cf. ShowPinnedPlaces).</summary>
    public bool ShowPinnedPlaces { get; set; } = true;

    /// <summary>Shows hidden files (cf. ShowHiddenFiles).</summary>
    public bool ShowHiddenFiles { get; set; }

    /// <summary>Allows multi-dot extensions (cf. SupportMultiDottedExtensions).</summary>
    public bool SupportMultiDottedExtensions { get; set; }

    /// <summary>Dialog caption (cf. Title).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Validates Win32 file names (cf. ValidateNames).</summary>
    public bool ValidateNames { get; set; } = true;

    /// <summary>
    /// No native picker exists, so the dialog is treated as dismissed
    /// (cf. RunDialog). Override <c>ShowDialog</c> behavior via Cancel.
    /// </summary>
    /// <param name="hwndOwner">Owner (ignored).</param>
    /// <returns>False (dismissed).</returns>
    protected override bool RunDialog(IntPtr hwndOwner) => false;

    /// <summary>Raises <c>FileOk</c> (cf. OnFileOk).</summary>
    /// <param name="e">Event data.</param>
    protected virtual void OnFileOk(System.ComponentModel.CancelEventArgs e) =>
        FileOk?.Invoke(this, e);

    /// <summary>
    /// Opens the selected file for reading (cf. OpenFile). Works headless:
    /// reads <c>FileName</c> from the real file system when it exists.
    /// </summary>
    /// <returns>Read stream over the selected file.</returns>
    public System.IO.Stream OpenFile()
    {
        if (_fileName.Length != 0 && System.IO.File.Exists(_fileName))
            return System.IO.File.OpenRead(_fileName);
        throw new System.IO.FileNotFoundException(_fileName);
    }

    /// <summary>File name without directory (cf. SafeFileName).</summary>
    public string SafeFileName => System.IO.Path.GetFileName(_fileName) ?? string.Empty;

    /// <summary>File names without directories (cf. SafeFileNames).</summary>
    public string[] SafeFileNames => FileNames
        .Select(f => System.IO.Path.GetFileName(f) ?? string.Empty)
        .ToArray();

    /// <summary>Restores defaults (cf. Reset).</summary>
    public override void Reset()
    {
        _fileName = string.Empty;
        AddExtension = true;
        AddToRecent = true;
        AutoUpgradeEnabled = true;
        CheckFileExists = false;
        CheckPathExists = true;
        ClientGuid = null;
        _customPlaces.Clear();
        DefaultExt = string.Empty;
        DereferenceLinks = true;
        Filter = string.Empty;
        FilterIndex = 1;
        InitialDirectory = string.Empty;
        OkRequiresInteraction = true;
        RestoreDirectory = false;
        ShowHelp = false;
        ShowPinnedPlaces = true;
        ShowHiddenFiles = false;
        SupportMultiDottedExtensions = false;
        Title = string.Empty;
        ValidateNames = true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{GetType().Name}: {_fileName}";
}

/// <summary>File-open picker (cf. System.Windows.Forms.OpenFileDialog).</summary>
public sealed class OpenFileDialog : FileDialog
{
    /// <summary>Requires the file to exist (cf. CheckFileExists).</summary>
    public override bool CheckFileExists { get; set; } = true;

    /// <summary>Shows the read-only checkbox (cf. ShowReadOnly).</summary>
    public bool ShowReadOnly { get; set; }

    /// <summary>Read-only checkbox state (cf. ReadOnlyChecked).</summary>
    public bool ReadOnlyChecked { get; set; }

    /// <summary>Opens read-only by default (cf. ShowPreview).</summary>
    public bool ShowPreview { get; set; }

    /// <summary>Allows multiple selection (cf. Multiselect).</summary>
    public bool Multiselect { get; set; }

    /// <summary>Opens the file read-only (cf. SelectReadOnly).</summary>
    public bool SelectReadOnly { get; set; }

    /// <summary>Restores open-dialog defaults (cf. Reset).</summary>
    public override void Reset()
    {
        base.Reset();
        CheckFileExists = true;
        Multiselect = false;
        ReadOnlyChecked = false;
        SelectReadOnly = false;
        ShowPreview = false;
        ShowReadOnly = false;
    }
}

/// <summary>File-save picker (cf. System.Windows.Forms.SaveFileDialog).</summary>
public sealed class SaveFileDialog : FileDialog
{
    /// <summary>Prompts before creating the file (cf. CreatePrompt).</summary>
    public bool CreatePrompt { get; set; }

    /// <summary>Prompts before overwriting (cf. OverwritePrompt).</summary>
    public bool OverwritePrompt { get; set; } = true;

    /// <summary>Checks write access (cf. CheckWriteAccess).</summary>
    public bool CheckWriteAccess { get; set; }

    /// <summary>Expanded-by-default mode (cf. ExpandedMode).</summary>
    public bool ExpandedMode { get; set; }

    /// <summary>Restores save-dialog defaults (cf. Reset).</summary>
    public override void Reset()
    {
        base.Reset();
        CheckWriteAccess = false;
        CreatePrompt = false;
        ExpandedMode = false;
        OverwritePrompt = true;
    }
}

/// <summary>
/// Color picker (cf. System.Windows.Forms.ColorDialog). ShowDialog returns
/// Cancel: no native picker exists yet (host picker milestone). The <c>Color</c>
/// property keeps its preset value.
/// </summary>
public class ColorDialog : CommonDialog
{
    /// <summary>Shows the help button (cf. ShowHelp).</summary>
    public bool ShowHelp { get; set; }

    /// <summary>Selected color (cf. Color).</summary>
    public Draw.Color Color { get; set; } = Draw.Color.Black;

    /// <summary>Allows full custom-color editing (cf. AllowFullOpen).</summary>
    public bool AllowFullOpen { get; set; } = true;

    /// <summary>Shows the full dialog on open (cf. FullOpen).</summary>
    public bool FullOpen { get; set; }

    /// <summary>Prevents custom-color editing (cf. SolidColorOnly).</summary>
    public bool SolidColorOnly { get; set; }

    /// <summary>Custom colors as 0x00BBGGRR integers (cf. CustomColors).</summary>
    public int[] CustomColors { get; set; } = [];

    /// <summary>Shows all basic colors (cf. AnyColor).</summary>
    public bool AnyColor { get; set; }

    /// <summary>No native picker exists, so the dialog is treated as dismissed.</summary>
    /// <param name="hwndOwner">Owner (ignored).</param>
    /// <returns>False (dismissed).</returns>
    protected override bool RunDialog(IntPtr hwndOwner) => false;

    /// <summary>Restores defaults (cf. Reset).</summary>
    public override void Reset()
    {
        Color = Draw.Color.Black;
        CustomColors = [];
        AllowFullOpen = true;
        AnyColor = false;
        FullOpen = false;
        ShowHelp = false;
        SolidColorOnly = false;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{GetType().Name}: {Color}";
}
