extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfImage = WfDraw::System.Drawing.Image;
using WfPoint = WfDraw::System.Drawing.Point;
using WfRectangle = WfDraw::System.Drawing.Rectangle;
using WfSize = WfDraw::System.Drawing.Size;
using WfIcon = WfDraw::System.Drawing.Icon;
using WfIconConverter = WfDraw::System.Drawing.IconConverter;
using WfImageConverter = WfDraw::System.Drawing.ImageConverter;
using WfToolbox = WfDraw::System.Drawing.ToolboxBitmapAttribute;
using WfImageAnimator = WfDraw::System.Drawing.ImageAnimator;
using WfCategoryNames = WfDraw::System.Drawing.Design.CategoryNameCollection;
using WfMargins = WfDraw::System.Drawing.Printing.Margins;
using WfMarginsConverter = WfDraw::System.Drawing.Printing.MarginsConverter;
using WfPaperSize = WfDraw::System.Drawing.Printing.PaperSize;
using WfPaperKind = WfDraw::System.Drawing.Printing.PaperKind;
using WfPaperSourceKind = WfDraw::System.Drawing.Printing.PaperSourceKind;
using WfPaperSource = WfDraw::System.Drawing.Printing.PaperSource;
using WfPrinterResolution = WfDraw::System.Drawing.Printing.PrinterResolution;
using WfPrinterResolutionKind = WfDraw::System.Drawing.Printing.PrinterResolutionKind;
using WfPageSettings = WfDraw::System.Drawing.Printing.PageSettings;
using WfPrinterSettings = WfDraw::System.Drawing.Printing.PrinterSettings;
using WfPrinterUnit = WfDraw::System.Drawing.Printing.PrinterUnit;
using WfPrinterUnitConvert = WfDraw::System.Drawing.Printing.PrinterUnitConvert;
using WfPrintDocument = WfDraw::System.Drawing.Printing.PrintDocument;
using WfPrintAction = WfDraw::System.Drawing.Printing.PrintAction;
using WfPreviewController = WfDraw::System.Drawing.Printing.PreviewPrintController;
using WfStandardController = WfDraw::System.Drawing.Printing.StandardPrintController;
using WfInvalidPrinter = WfDraw::System.Drawing.Printing.InvalidPrinterException;

public sealed class PrintConvertTests
{
    [Fact]
    public void Margins_Equality_Clone_Convert()
    {
        var a = new WfMargins(10, 20, 30, 40);
        var b = (WfMargins)a.Clone();
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        var conv = new WfMarginsConverter();
        var parsed = (WfMargins)conv.ConvertFrom("10, 20, 30, 40")!;
        Assert.Equal(a, parsed);
        Assert.Equal("10, 20, 30, 40", conv.ConvertTo(a, typeof(string)));
        var made = (WfMargins)conv.CreateInstance(null,
            new System.Collections.Hashtable { ["Left"] = 1, ["Right"] = 2, ["Top"] = 3, ["Bottom"] = 4 })!;
        Assert.Equal(new WfMargins(1, 2, 3, 4), made);
    }

    [Fact]
    public void PaperSize_Kind_Mapping_Works()
    {
        var letter = new WfPaperSize("Letter", 850, 1100) { RawKind = (int)WfPaperKind.Letter };
        Assert.Equal(WfPaperKind.Letter, letter.Kind);
        Assert.Contains("Letter", letter.ToString());
        var custom = new WfPaperSize();
        custom.Width = 500;
        Assert.Equal(WfPaperKind.Custom, custom.Kind);
        var tray = new WfPaperSource { SourceName = "Upper", RawKind = (int)WfPaperSourceKind.Upper };
        Assert.Equal(WfPaperSourceKind.Upper, tray.Kind);
        var res = new WfPrinterResolution { Kind = WfPrinterResolutionKind.Medium, X = 600, Y = 600 };
        Assert.Contains("600", res.ToString());
    }

    [Fact]
    public void PrinterUnitConvert_Scales()
    {
        Assert.Equal(1000, WfPrinterUnitConvert.Convert(100, WfPrinterUnit.Display, WfPrinterUnit.ThousandthsOfAnInch));
        Assert.Equal(new WfPoint(1000, 2000),
            WfPrinterUnitConvert.Convert(new WfPoint(100, 200), WfPrinterUnit.Display, WfPrinterUnit.ThousandthsOfAnInch));
        var margins = WfPrinterUnitConvert.Convert(new WfMargins(100, 100, 100, 100),
            WfPrinterUnit.Display, WfPrinterUnit.Display);
        Assert.Equal(new WfMargins(100, 100, 100, 100), margins);
    }

    [Fact]
    public void PrintDocument_Runs_Event_Pipeline()
    {
        using var doc = new WfPrintDocument();
        int began = 0, ended = 0, pages = 0, queried = 0;
        doc.BeginPrint += (s, e) => { began++; Assert.Equal(WfPrintAction.PrintToPrinter, e.PrintAction); };
        doc.EndPrint += (s, e) => ended++;
        doc.QueryPageSettings += (s, e) => { queried++; };
        doc.PrintPage += (s, e) =>
        {
            pages++;
            Assert.NotNull(e.Graphics);
            Assert.False(e.Cancel);
            e.HasMorePages = pages < 2;
        };
        doc.Print();
        Assert.Equal(1, began);
        Assert.Equal(1, ended);
        Assert.Equal(1, queried);
        Assert.Equal(2, pages);
        Assert.Contains("document", doc.ToString());
    }

    [Fact]
    public void PreviewController_Collects_Pages()
    {
        using var doc = new WfPrintDocument();
        var preview = new WfPreviewController();
        Assert.True(preview.IsPreview);
        doc.PrintController = preview;
        doc.PrintPage += (s, e) =>
        {
            e.Graphics.Clear(WfColor.White);
            e.HasMorePages = false;
        };
        doc.Print();
        var infos = preview.GetPreviewPageInfo();
        Assert.Single(infos);
        Assert.Equal(new WfSize(850, 1100), infos[0].PhysicalSize);
        using var standard = new WfStandardController();
        Assert.False(standard.IsPreview);
        standard.Dispose();
        var settings = new WfPrinterSettings();
        Assert.False(settings.IsValid);
        var ex = new WfInvalidPrinter(settings);
        Assert.NotNull(ex.Message);
    }

    [Fact]
    public void ImageAnimator_SingleFrame_Noops()
    {
        using var bitmap = new WfBitmap(4, 4);
        Assert.False(WfImageAnimator.CanAnimate(bitmap));
        int calls = 0;
        void Handler(object? s, EventArgs e) => calls++;
        WfImageAnimator.Animate(bitmap, Handler);
        WfImageAnimator.UpdateFrames(bitmap);
        WfImageAnimator.UpdateFrames();
        Assert.Equal(0, calls);
        WfImageAnimator.StopAnimate(bitmap, Handler);
    }

    [Fact]
    public void ImageAnimator_Null_Noops()
    {
        // Desktop parity (probed live on .NET Framework): null image
        // and/or null handler are silent no-ops, never throws.
        // Regression: ArgumentNullException 'image' | StopAnimate <-
        // BugTracker.RadRibbonForm1..ctor.
        void Handler(object? s, EventArgs e) { }
        WfImageAnimator.Animate(null, null);
        WfImageAnimator.StopAnimate(null, null);
        using var bitmap = new WfBitmap(4, 4);
        WfImageAnimator.Animate(bitmap, null);
        WfImageAnimator.Animate(null, Handler);
        WfImageAnimator.StopAnimate(bitmap, null);
        WfImageAnimator.StopAnimate(null, Handler);
        Assert.False(WfImageAnimator.CanAnimate(null));
        WfImageAnimator.UpdateFrames(null);
    }

    [Fact]
    public void Converters_And_Design_Types_Work()
    {
        var iconConv = new WfIconConverter();
        Assert.True(iconConv.CanConvertFrom(null, typeof(string)));
        Assert.True(iconConv.CanConvertTo(null, typeof(byte[])));
        var imageConv = new WfImageConverter();
        Assert.True(imageConv.CanConvertFrom(null, typeof(byte[])));
        Assert.True(imageConv.GetPropertiesSupported(null));
        using var bitmap = new WfBitmap(2, 2);
        var props = imageConv.GetProperties(null, bitmap, null)!;
        Assert.True(props.Count >= 6);
        var names = new WfCategoryNames(["Appearance", "Behavior"]);
        Assert.True(names.Contains("Behavior"));
        Assert.Equal(1, names.IndexOf("Behavior"));
        Assert.Equal("Appearance", names[0]);
        var copy = new WfCategoryNames(names);
        Assert.Equal(2, copy.Count);
        Assert.Null(WfToolbox.Default.GetImage(typeof(object)));
        var attr = new WfToolbox(typeof(object), "img");
        Assert.Equal(attr, new WfToolbox(typeof(object), "img"));
        Assert.Null(WfToolbox.GetImageFromResource(typeof(object), "x", false));
        var suffix1 = new WfDraw::System.Drawing.BitmapSuffixInSameAssemblyAttribute();
        var suffix2 = new WfDraw::System.Drawing.BitmapSuffixInSatelliteAssemblyAttribute();
        Assert.NotNull(suffix1);
        Assert.NotNull(suffix2);
    }
}
