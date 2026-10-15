// Printing model: exact .NET member shapes; no spooler on web, so Print
// renders pages to CPU bitmaps through the controller/event pipeline.
using System.Collections;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace System.Drawing.Printing
{
    /// <summary>Print margins (cf. System.Drawing.Printing.Margins).</summary>
    public class Margins : ICloneable
    {
        public Margins()
        {
        }

        public Margins(int left, int right, int top, int bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }

        public int Left { get; set; }
        public int Right { get; set; }
        public int Top { get; set; }
        public int Bottom { get; set; }

        public object Clone() => new Margins(Left, Right, Top, Bottom);

        public override bool Equals(object? obj) =>
            obj is Margins other
            && Left == other.Left && Right == other.Right
            && Top == other.Top && Bottom == other.Bottom;

        public override int GetHashCode() => HashCode.Combine(Left, Right, Top, Bottom);

        public static bool operator ==(Margins? left, Margins? right) =>
            left is null ? right is null : left.Equals(right);

        public static bool operator !=(Margins? left, Margins? right) => !(left == right);

        public override string ToString() =>
            $"[Margins Left={Left} Right={Right} Top={Top} Bottom={Bottom}]";
    }

    /// <summary>Paper size (cf. System.Drawing.Printing.PaperSize).</summary>
    public class PaperSize
    {
        private string _name = string.Empty;
        private int _width;
        private int _height;
        private int _rawKind;

        public PaperSize()
        {
        }

        public PaperSize(string paperName, int width, int height)
        {
            PaperName = paperName;
            Width = width;
            Height = height;
        }

        public int Height
        {
            get => _height;
            set
            {
                _height = value;
                _rawKind = (int)PaperKind.Custom;
            }
        }

        public PaperKind Kind =>
            Enum.IsDefined(typeof(PaperKind), _rawKind) ? (PaperKind)_rawKind : PaperKind.Custom;

        public string PaperName
        {
            get => _name;
            set
            {
                _name = value ?? string.Empty;
                _rawKind = (int)PaperKind.Custom;
            }
        }

        public int RawKind
        {
            get => _rawKind;
            set => _rawKind = value;
        }

        public int Width
        {
            get => _width;
            set
            {
                _width = value;
                _rawKind = (int)PaperKind.Custom;
            }
        }

        public override string ToString() => $"[PaperSize {PaperName} Width={Width} Height={Height}]";
    }

    /// <summary>Paper tray (cf. System.Drawing.Printing.PaperSource).</summary>
    public class PaperSource
    {
        private string _name = string.Empty;
        private int _rawKind;

        public PaperSource()
        {
        }

        public PaperSourceKind Kind =>
            Enum.IsDefined(typeof(PaperSourceKind), _rawKind) ? (PaperSourceKind)_rawKind : PaperSourceKind.Custom;

        public int RawKind
        {
            get => _rawKind;
            set => _rawKind = value;
        }

        public string SourceName
        {
            get => _name;
            set
            {
                _name = value ?? string.Empty;
                _rawKind = (int)PaperSourceKind.Custom;
            }
        }

        public override string ToString() => $"[PaperSource {SourceName}]";
    }

    /// <summary>Printer resolution (cf. System.Drawing.Printing.PrinterResolution).</summary>
    public class PrinterResolution
    {
        public PrinterResolution()
        {
        }

        public PrinterResolutionKind Kind { get; set; }
        public int X { get; set; }
        public int Y { get; set; }

        public override string ToString() => $"[PrinterResolution Kind={Kind} X={X} Y={Y}]";
    }

    /// <summary>Page configuration (cf. System.Drawing.Printing.PageSettings).</summary>
    public class PageSettings : ICloneable
    {
        private bool _color = true;
        private bool _landscape;
        private Margins _margins = new(100, 100, 100, 100);
        private PaperSize _paperSize = new("Letter", 850, 1100);
        private PaperSource _paperSource = new();
        private PrinterResolution _resolution = new() { Kind = PrinterResolutionKind.Medium, X = 600, Y = 600 };
        private PrinterSettings? _printerSettings;

        public PageSettings()
        {
        }

        public PageSettings(PrinterSettings printerSettings)
        {
            PrinterSettings = printerSettings;
        }

        public Rectangle Bounds
        {
            get
            {
                int w = _paperSize.Width;
                int h = _paperSize.Height;
                if (_landscape)
                    (w, h) = (h, w);
                return new Rectangle(0, 0, w, h);
            }
        }

        public bool Color
        {
            get => _color;
            set => _color = value;
        }

        public float HardMarginX => 0f;
        public float HardMarginY => 0f;

        public bool Landscape
        {
            get => _landscape;
            set => _landscape = value;
        }

        public Margins Margins
        {
            get => _margins;
            set => _margins = value ?? new Margins();
        }

        public PaperSize PaperSize
        {
            get => _paperSize;
            set => _paperSize = value ?? new PaperSize();
        }

        public PaperSource PaperSource
        {
            get => _paperSource;
            set => _paperSource = value ?? new PaperSource();
        }

        public RectangleF PrintableArea
        {
            get
            {
                Rectangle bounds = Bounds;
                return new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }

        public PrinterResolution PrinterResolution
        {
            get => _resolution;
            set => _resolution = value ?? new PrinterResolution();
        }

        public PrinterSettings PrinterSettings
        {
            get => _printerSettings ??= new PrinterSettings();
            set => _printerSettings = value;
        }

        public object Clone()
        {
            var clone = new PageSettings
            {
                _color = _color,
                _landscape = _landscape,
                _margins = (Margins)_margins.Clone(),
                _paperSize = new PaperSize(_paperSize.PaperName, _paperSize.Width, _paperSize.Height),
                _resolution = new PrinterResolution { Kind = _resolution.Kind, X = _resolution.X, Y = _resolution.Y },
            };
            clone._paperSize.RawKind = _paperSize.RawKind;
            return clone;
        }

        public void CopyToHdevmode(IntPtr hdevmode)
        {
        }

        public void SetHdevmode(IntPtr hdevmode)
        {
        }

        public override string ToString() => "[PageSettings]";
    }

    /// <summary>Unit conversion (cf. System.Drawing.Printing.PrinterUnitConvert).</summary>
    public static class PrinterUnitConvert
    {
        public static double Convert(double value, PrinterUnit fromUnit, PrinterUnit toUnit) =>
            FromInches(ToInches(value, fromUnit), toUnit);

        public static int Convert(int value, PrinterUnit fromUnit, PrinterUnit toUnit) =>
            (int)Math.Round(Convert((double)value, fromUnit, toUnit));

        public static Point Convert(Point value, PrinterUnit fromUnit, PrinterUnit toUnit) =>
            new(Convert(value.X, fromUnit, toUnit), Convert(value.Y, fromUnit, toUnit));

        public static Size Convert(Size value, PrinterUnit fromUnit, PrinterUnit toUnit) =>
            new(Convert(value.Width, fromUnit, toUnit), Convert(value.Height, fromUnit, toUnit));

        public static Rectangle Convert(Rectangle value, PrinterUnit fromUnit, PrinterUnit toUnit) =>
            new(Convert(value.Location, fromUnit, toUnit), Convert(value.Size, fromUnit, toUnit));

        public static Margins Convert(Margins value, PrinterUnit fromUnit, PrinterUnit toUnit)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new Margins(
                Convert(value.Left, fromUnit, toUnit),
                Convert(value.Right, fromUnit, toUnit),
                Convert(value.Top, fromUnit, toUnit),
                Convert(value.Bottom, fromUnit, toUnit));
        }

        internal static double ToInches(double value, PrinterUnit unit) => unit switch
        {
            PrinterUnit.Display => value / 100.0,
            PrinterUnit.ThousandthsOfAnInch => value / 1000.0,
            PrinterUnit.HundredthsOfAMillimeter => value / 2540.0,
            PrinterUnit.TenthsOfAMillimeter => value / 254.0,
            _ => value / 100.0,
        };

        internal static double FromInches(double inches, PrinterUnit unit) => unit switch
        {
            PrinterUnit.Display => inches * 100.0,
            PrinterUnit.ThousandthsOfAnInch => inches * 1000.0,
            PrinterUnit.HundredthsOfAMillimeter => inches * 2540.0,
            PrinterUnit.TenthsOfAMillimeter => inches * 254.0,
            _ => inches * 100.0,
        };
    }

    /// <summary>Preview page (cf. System.Drawing.Printing.PreviewPageInfo).</summary>
    public sealed class PreviewPageInfo
    {
        public PreviewPageInfo(Image image, Size physicalSize)
        {
            Image = image;
            PhysicalSize = physicalSize;
        }

        public Image Image { get; }
        public Size PhysicalSize { get; }
    }

    /// <summary>Bad printer (cf. System.Drawing.Printing.InvalidPrinterException).</summary>
    public class InvalidPrinterException : SystemException
    {
        public InvalidPrinterException(PrinterSettings settings)
            : base($"No printer is installed.")
        {
            _ = settings;
        }

#pragma warning disable SYSLIB0051 // formatter-based serialization matches the framework shape.
        protected InvalidPrinterException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        public InvalidPrinterException(PrinterSettings settings, string message)
            : base(message)
        {
            _ = settings;
        }

        [Obsolete("Formatter-based serialization is obsolete.")]
        public override void GetObjectData(SerializationInfo info, StreamingContext context) =>
            base.GetObjectData(info, context);
    }

#pragma warning restore SYSLIB0051

    /// <summary>Print lifecycle (cf. System.Drawing.Printing.PrintEventHandler).</summary>
    public delegate void PrintEventHandler(object sender, PrintEventArgs e);

    /// <summary>Print page (cf. System.Drawing.Printing.PrintPageEventHandler).</summary>
    public delegate void PrintPageEventHandler(object sender, PrintPageEventArgs e);

    /// <summary>Page query (cf. System.Drawing.Printing.QueryPageSettingsEventHandler).</summary>
    public delegate void QueryPageSettingsEventHandler(object sender, QueryPageSettingsEventArgs e);

    /// <summary>Print lifecycle args (cf. System.Drawing.Printing.PrintEventArgs).</summary>
    public class PrintEventArgs : CancelEventArgs
    {
        public PrintEventArgs()
        {
        }

        internal PrintEventArgs(PrintAction action)
        {
            PrintAction = action;
        }

        public PrintAction PrintAction { get; }
    }

    /// <summary>Page args (cf. System.Drawing.Printing.PrintPageEventArgs).</summary>
    public class PrintPageEventArgs : EventArgs
    {
        public PrintPageEventArgs(Graphics graphics, Rectangle marginBounds, Rectangle pageBounds, PageSettings pageSettings)
        {
            Graphics = graphics;
            MarginBounds = marginBounds;
            PageBounds = pageBounds;
            PageSettings = pageSettings;
        }

        public bool Cancel { get; set; }
        public Graphics Graphics { get; set; }
        public bool HasMorePages { get; set; }
        public Rectangle MarginBounds { get; }
        public Rectangle PageBounds { get; }
        public PageSettings? PageSettings { get; }
    }

    /// <summary>Page query args (cf. System.Drawing.Printing.QueryPageSettingsEventArgs).</summary>
    public class QueryPageSettingsEventArgs : PrintEventArgs
    {
        public QueryPageSettingsEventArgs(PageSettings pageSettings)
        {
            PageSettings = pageSettings;
        }

        public PageSettings? PageSettings { get; set; }
    }
}
