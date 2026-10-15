// Printer settings, controllers, and document: exact .NET member shapes.
// No spooler on web: Print() renders pages to CPU bitmaps and discards them
// (preview controllers retain the page images instead).
using System.Collections;
using System.ComponentModel;

namespace System.Drawing.Printing
{
    /// <summary>Printer configuration (cf. System.Drawing.Printing.PrinterSettings).</summary>
    public class PrinterSettings : ICloneable
    {
        private short _copies = 1;
        private bool _collate;
        private Duplex _duplex;
        private int _fromPage;
        private int _maxPage;
        private int _minPage;
        private string _printFileName = string.Empty;
        private PrintRange _printRange = PrintRange.AllPages;
        private bool _printToFile;
        private string _printerName = string.Empty;
        private bool _supportsColor = true;
        private int _toPage;

        public PrinterSettings()
        {
            PaperSizes = new PaperSizeCollection([
                new PaperSize("Letter", 850, 1100) { RawKind = (int)PaperKind.Letter },
                new PaperSize("Legal", 850, 1400) { RawKind = (int)PaperKind.Legal },
                new PaperSize("A4", 827, 1169) { RawKind = (int)PaperKind.A4 },
            ]);
            PaperSources = new PaperSourceCollection([
                new PaperSource { SourceName = "Upper", RawKind = (int)PaperSourceKind.Upper },
                new PaperSource { SourceName = "Lower", RawKind = (int)PaperSourceKind.Lower },
            ]);
            PrinterResolutions = new PrinterResolutionCollection([
                new PrinterResolution { Kind = PrinterResolutionKind.Draft, X = 150, Y = 150 },
                new PrinterResolution { Kind = PrinterResolutionKind.Low, X = 300, Y = 300 },
                new PrinterResolution { Kind = PrinterResolutionKind.Medium, X = 600, Y = 600 },
                new PrinterResolution { Kind = PrinterResolutionKind.High, X = 1200, Y = 1200 },
            ]);

        }

        public bool CanDuplex => false;
        public short Copies { get => _copies; set => _copies = value; }
        public bool Collate { get => _collate; set => _collate = value; }
        public PageSettings DefaultPageSettings => new(this);
        public Duplex Duplex { get => _duplex; set => _duplex = value; }
        public int FromPage { get => _fromPage; set => _fromPage = value; }
        public static StringCollection InstalledPrinters { get; } = new([]);
        public bool IsDefaultPrinter => false;
        public bool IsPlotter => false;
        public bool IsValid => false;
        public int LandscapeAngle => 0;
        public int MaximumCopies => 9999;
        public int MaximumPage { get => _maxPage; set => _maxPage = value; }
        public int MinimumPage { get => _minPage; set => _minPage = value; }
        public string PrintFileName { get => _printFileName; set => _printFileName = value ?? string.Empty; }
        public PaperSizeCollection PaperSizes { get; }
        public PaperSourceCollection PaperSources { get; }
        public PrintRange PrintRange { get => _printRange; set => _printRange = value; }
        public bool PrintToFile { get => _printToFile; set => _printToFile = value; }
        public string PrinterName { get => _printerName; set => _printerName = value ?? string.Empty; }
        public PrinterResolutionCollection PrinterResolutions { get; }
        public bool SupportsColor => _supportsColor;
        public int ToPage { get => _toPage; set => _toPage = value; }

        public object Clone()
        {
            var clone = new PrinterSettings
            {
                _copies = _copies,
                _collate = _collate,
                _duplex = _duplex,
                _fromPage = _fromPage,
                _maxPage = _maxPage,
                _minPage = _minPage,
                _printFileName = _printFileName,
                _printRange = _printRange,
                _printToFile = _printToFile,
                _printerName = _printerName,
                _toPage = _toPage,
            };
            return clone;
        }

        public Graphics CreateMeasurementGraphics() =>
            Graphics.FromImage(new Bitmap(850, 1100));

        public Graphics CreateMeasurementGraphics(bool honorOriginAtMargins)
        {
            _ = honorOriginAtMargins;
            return CreateMeasurementGraphics();
        }

        public Graphics CreateMeasurementGraphics(PageSettings pageSettings)
        {
            ArgumentNullException.ThrowIfNull(pageSettings);
            return CreateMeasurementGraphics();
        }

        public Graphics CreateMeasurementGraphics(PageSettings pageSettings, bool honorOriginAtMargins)
        {
            ArgumentNullException.ThrowIfNull(pageSettings);
            _ = honorOriginAtMargins;
            return CreateMeasurementGraphics();
        }

        public IntPtr GetHdevmode() => IntPtr.Zero;
        public IntPtr GetHdevmode(PageSettings pageSettings) => IntPtr.Zero;
        public IntPtr GetHdevnames() => IntPtr.Zero;

        public bool IsDirectPrintingSupported(Imaging.ImageFormat imageFormat) => false;
        public bool IsDirectPrintingSupported(Image image) => false;

        public void SetHdevmode(IntPtr hdevmode)
        {
        }

        public void SetHdevnames(IntPtr hdevnames)
        {
        }

        public override string ToString() =>
            $"[PrinterSettings {PrinterName}]";

        /// <summary>Paper sizes (cf. PrinterSettings.PaperSizeCollection).</summary>
        public class PaperSizeCollection : ICollection, IEnumerable
        {
            private readonly List<PaperSize> _items;

            public PaperSizeCollection(PaperSize[] array)
            {
                ArgumentNullException.ThrowIfNull(array);
                _items = new List<PaperSize>(array);
            }

            public int Count => _items.Count;
            public PaperSize this[int index] => _items[index];

            public int Add(PaperSize paperSize)
            {
                ArgumentNullException.ThrowIfNull(paperSize);
                _items.Add(paperSize);
                return _items.Count - 1;
            }

            public void CopyTo(PaperSize[] paperSizes, int index) => _items.CopyTo(paperSizes, index);

            public IEnumerator GetEnumerator() => _items.GetEnumerator();

            bool ICollection.IsSynchronized => false;
            object ICollection.SyncRoot => this;
            void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        }

        /// <summary>Paper trays (cf. PrinterSettings.PaperSourceCollection).</summary>
        public class PaperSourceCollection : ICollection, IEnumerable
        {
            private readonly List<PaperSource> _items;

            public PaperSourceCollection(PaperSource[] array)
            {
                ArgumentNullException.ThrowIfNull(array);
                _items = new List<PaperSource>(array);
            }

            public int Count => _items.Count;
            public PaperSource this[int index] => _items[index];

            public int Add(PaperSource paperSource)
            {
                ArgumentNullException.ThrowIfNull(paperSource);
                _items.Add(paperSource);
                return _items.Count - 1;
            }

            public void CopyTo(PaperSource[] paperSources, int index) => _items.CopyTo(paperSources, index);

            public IEnumerator GetEnumerator() => _items.GetEnumerator();

            bool ICollection.IsSynchronized => false;
            object ICollection.SyncRoot => this;
            void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        }

        /// <summary>Resolutions (cf. PrinterSettings.PrinterResolutionCollection).</summary>
        public class PrinterResolutionCollection : ICollection, IEnumerable
        {
            private readonly List<PrinterResolution> _items;

            public PrinterResolutionCollection(PrinterResolution[] array)
            {
                ArgumentNullException.ThrowIfNull(array);
                _items = new List<PrinterResolution>(array);
            }

            public int Count => _items.Count;
            public PrinterResolution this[int index] => _items[index];

            public int Add(PrinterResolution printerResolution)
            {
                ArgumentNullException.ThrowIfNull(printerResolution);
                _items.Add(printerResolution);
                return _items.Count - 1;
            }

            public void CopyTo(PrinterResolution[] printerResolutions, int index) => _items.CopyTo(printerResolutions, index);

            public IEnumerator GetEnumerator() => _items.GetEnumerator();

            bool ICollection.IsSynchronized => false;
            object ICollection.SyncRoot => this;
            void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        }

        /// <summary>Printer names (cf. PrinterSettings.StringCollection).</summary>
        public class StringCollection : ICollection, IEnumerable, IList
        {
            private readonly List<string> _items;

            public StringCollection(string[] array)
            {
                ArgumentNullException.ThrowIfNull(array);
                _items = new List<string>(array);
            }

            public int Count => _items.Count;
            public string this[int index] => _items[index];

            public int Add(string value)
            {
                ArgumentNullException.ThrowIfNull(value);
                _items.Add(value);
                return _items.Count - 1;
            }

            public void CopyTo(string[] array, int index) => _items.CopyTo(array, index);

            public IEnumerator GetEnumerator() => _items.GetEnumerator();

            bool ICollection.IsSynchronized => false;
            object ICollection.SyncRoot => this;
            void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

            bool IList.IsFixedSize => false;
            bool IList.IsReadOnly => false;
            object? IList.this[int index]
            {
                get => _items[index];
                set => _items[index] = (string)value!;
            }
            int IList.Add(object? value)
            {
                ArgumentNullException.ThrowIfNull(value);
                return Add((string)value);
            }
            void IList.Clear() => _items.Clear();
            bool IList.Contains(object? value) => value is string s && _items.Contains(s);
            int IList.IndexOf(object? value) => value is string s ? _items.IndexOf(s) : -1;
            void IList.Insert(int index, object? value) => _items.Insert(index, (string)value!);
            void IList.Remove(object? value)
            {
                if (value is string s)
                    _items.Remove(s);
            }
            void IList.RemoveAt(int index) => _items.RemoveAt(index);
        }
    }

    /// <summary>Print driver (cf. System.Drawing.Printing.PrintController).</summary>
    public abstract class PrintController
    {
        public virtual bool IsPreview => false;

        public virtual void OnStartPrint(PrintDocument document, PrintEventArgs e)
        {
        }

        public virtual Graphics? OnStartPage(PrintDocument document, PrintPageEventArgs e) => null;

        public virtual void OnEndPage(PrintDocument document, PrintPageEventArgs e)
        {
        }

        public virtual void OnEndPrint(PrintDocument document, PrintEventArgs e)
        {
        }
    }

    /// <summary>Default driver (cf. System.Drawing.Printing.StandardPrintController).</summary>
    public class StandardPrintController : PrintController, IDisposable
    {
        public StandardPrintController()
        {
        }

        public override void OnStartPrint(PrintDocument document, PrintEventArgs e) =>
            base.OnStartPrint(document, e);

        public override Graphics? OnStartPage(PrintDocument document, PrintPageEventArgs e) =>
            e.Graphics;

        public override void OnEndPage(PrintDocument document, PrintPageEventArgs e) =>
            base.OnEndPage(document, e);

        public override void OnEndPrint(PrintDocument document, PrintEventArgs e) =>
            base.OnEndPrint(document, e);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }

    /// <summary>Preview driver (cf. System.Drawing.Printing.PreviewPrintController).</summary>
    public class PreviewPrintController : PrintController
    {
        private readonly List<(Bitmap Page, Size Size)> _pages = new();
        private Bitmap? _current;

        public PreviewPrintController()
        {
            UseAntiAlias = true;
        }

        public override bool IsPreview => true;

        public bool UseAntiAlias { get; set; }

        public PreviewPageInfo[] GetPreviewPageInfo()
        {
            var infos = new PreviewPageInfo[_pages.Count];
            for (int i = 0; i < _pages.Count; i++)
                infos[i] = new PreviewPageInfo((Image)_pages[i].Page.Clone(), _pages[i].Size);
            return infos;
        }

        public override void OnStartPrint(PrintDocument document, PrintEventArgs e)
        {
            foreach ((Bitmap page, _) in _pages)
                page.Dispose();
            _pages.Clear();
        }

        public override Graphics? OnStartPage(PrintDocument document, PrintPageEventArgs e)
        {
            Rectangle bounds = e.PageBounds;
            _current = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
            return Graphics.FromImage(_current);
        }

        public override void OnEndPage(PrintDocument document, PrintPageEventArgs e)
        {
            if (_current is not null)
            {
                _pages.Add((_current, new Size(_current.Width, _current.Height)));
                _current = null;
            }
        }

        public override void OnEndPrint(PrintDocument document, PrintEventArgs e)
        {
        }
    }

    /// <summary>Print job (cf. System.Drawing.Printing.PrintDocument).</summary>
    public class PrintDocument : Component
    {
        private PageSettings _defaultPageSettings = new();
        private string _documentName = "document";
        private PrintController _controller = new StandardPrintController();
        private PrinterSettings _printerSettings = new();

        public PrintDocument()
        {
        }

        public PageSettings DefaultPageSettings
        {
            get => _defaultPageSettings;
            set => _defaultPageSettings = value ?? new PageSettings();
        }

        public string DocumentName
        {
            get => _documentName;
            set => _documentName = value ?? "document";
        }

        public bool OriginAtMargins { get; set; }

        public PrintController PrintController
        {
            get => _controller;
            set => _controller = value ?? new StandardPrintController();
        }

        public PrinterSettings PrinterSettings
        {
            get => _printerSettings;
            set => _printerSettings = value ?? new PrinterSettings();
        }

        public event PrintEventHandler? BeginPrint;
        public event PrintEventHandler? EndPrint;
        public event PrintPageEventHandler? PrintPage;
        public event QueryPageSettingsEventHandler? QueryPageSettings;

        protected virtual void OnBeginPrint(PrintEventArgs e) => BeginPrint?.Invoke(this, e);

        protected virtual void OnEndPrint(PrintEventArgs e) => EndPrint?.Invoke(this, e);

        protected virtual void OnPrintPage(PrintPageEventArgs e) => PrintPage?.Invoke(this, e);

        protected virtual void OnQueryPageSettings(QueryPageSettingsEventArgs e) => QueryPageSettings?.Invoke(this, e);

        public void Print()
        {
            var beginArgs = new PrintEventArgs(PrintAction.PrintToPrinter);
            _controller.OnStartPrint(this, beginArgs);
            OnBeginPrint(beginArgs);
            if (beginArgs.Cancel)
            {
                var cancelEnd = new PrintEventArgs(PrintAction.PrintToPrinter);
                _controller.OnEndPrint(this, cancelEnd);
                OnEndPrint(cancelEnd);
                return;
            }

            var settings = (PageSettings)_defaultPageSettings.Clone();
            var query = new QueryPageSettingsEventArgs(settings);
            OnQueryPageSettings(query);
            settings = query.PageSettings ?? settings;

            bool more = true;
            while (more)
            {
                Rectangle bounds = settings.Bounds;
                Rectangle margins = new(
                    bounds.X + settings.Margins.Left,
                    bounds.Y + settings.Margins.Top,
                    Math.Max(0, bounds.Width - settings.Margins.Left - settings.Margins.Right),
                    Math.Max(0, bounds.Height - settings.Margins.Top - settings.Margins.Bottom));
                var pageImage = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
                using var graphics = Graphics.FromImage(pageImage);
                var pageArgs = new PrintPageEventArgs(graphics, margins, bounds, settings);
                Graphics? pageGraphics = _controller.OnStartPage(this, pageArgs);
                OnPrintPage(pageArgs);
                _controller.OnEndPage(this, pageArgs);
                pageImage.Dispose();
                if (pageGraphics is not null && !ReferenceEquals(pageGraphics, graphics))
                    pageGraphics.Dispose();
                more = pageArgs.HasMorePages && !pageArgs.Cancel;
            }

            var endArgs = new PrintEventArgs(PrintAction.PrintToPrinter);
            _controller.OnEndPrint(this, endArgs);
            OnEndPrint(endArgs);
        }

        public override string ToString() => $"[PrintDocument {DocumentName}]";
    }


    /// <summary>Margins conversion (cf. System.Drawing.Printing.MarginsConverter).</summary>
    public class MarginsConverter : ComponentModel.ExpandableObjectConverter
    {
        public MarginsConverter()
        {
        }

        public override bool CanConvertFrom(ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ComponentModel.ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
        {
            if (value is string text)
            {
                string[] parts = text.Split(',');
                if (parts.Length == 4)
                {
                    return new Margins(
                        int.Parse(parts[0].Trim(), culture),
                        int.Parse(parts[1].Trim(), culture),
                        int.Parse(parts[2].Trim(), culture),
                        int.Parse(parts[3].Trim(), culture));
                }
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is Margins margins)
                return string.Format(culture, "{0}, {1}, {2}, {3}", margins.Left, margins.Right, margins.Top, margins.Bottom);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object? CreateInstance(ComponentModel.ITypeDescriptorContext? context, Collections.IDictionary propertyValues)
        {
            ArgumentNullException.ThrowIfNull(propertyValues);
            return new Margins(
                (int)(propertyValues["Left"] ?? 0),
                (int)(propertyValues["Right"] ?? 0),
                (int)(propertyValues["Top"] ?? 0),
                (int)(propertyValues["Bottom"] ?? 0));
        }

        public override bool GetCreateInstanceSupported(ComponentModel.ITypeDescriptorContext? context) => true;
    }
}
