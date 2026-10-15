namespace WfWasm.Shim;

/// <summary>Window-handle contract (cf. System.Windows.Forms.IWin32Window).</summary>
public interface IWin32Window
{
    IntPtr Handle { get; }
}

/// <summary>Dialog-button contract (cf. System.Windows.Forms.IButtonControl).</summary>
public interface IButtonControl
{
    DialogResult DialogResult { get; set; }
    void NotifyDefault(bool value);
    void PerformClick();
}

/// <summary>Message filter (cf. System.Windows.Forms.IMessageFilter).</summary>
public interface IMessageFilter
{
    bool PreFilterMessage(ref Message m);
}

/// <summary>Clipboard formats (cf. System.Windows.Forms.DataFormats).</summary>
public static class DataFormats
{
    public const string Text = "Text";
    public const string UnicodeText = "UnicodeText";
    public const string Rtf = "Rich Text Format";
    public const string Html = "HTML Format";
    public const string Bitmap = "Bitmap";
    public const string FileDrop = "FileDrop";
    public const string WaveAudio = "WaveAudio";
    public const string Serializable = "WindowsForms10PersistentObject";
    public const string StringFormat = "System.String";
    public const string CommaSeparatedValue = "Csv";
    public const string Dib = "DeviceIndependentBitmap";
    public const string Dif = "DataInterchangeFormat";
    public const string Tiff = "TaggedImageFileFormat";
    public const string Palette = "Palette";
    public const string Locale = "Locale";
    public const string EnhancedMetafile = "EnhancedMetafile";
    public const string MetafilePict = "MetaFilePict";
    public const string SymbolicLink = "SymbolicLink";
    public const string OemText = "OEMText";
    public const string PenData = "PenData";
    public const string Riff = "RiffAudio";

    /// <summary>Named format (cf. DataFormats.Format).</summary>
    public sealed class Format
    {
        public Format(string name, int id)
        {
            Name = name;
            Id = id;
        }

        public string Name { get; }
        public int Id { get; }
    }

    public static Format GetFormat(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return new Format(format, StringComparer.Ordinal.GetHashCode(format));
    }

    public static Format GetFormat(int id) => new(string.Empty, id);
}
