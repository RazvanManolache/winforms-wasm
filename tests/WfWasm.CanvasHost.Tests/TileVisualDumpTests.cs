using System.Drawing;
using WfWasm.CanvasHost;

/// <summary>
/// Tile-tree visual dump: panorama tiles are TPF elements (not Controls),
/// so the scene control dump cannot see them. The dump must surface tile
/// element text + size without throwing on forms with no panorama.
/// </summary>
public sealed class TileVisualDumpTests
{
    private sealed class FakeTileElement
    {
        public string Text { get; set; } = "";
        public Size Size { get; set; }
        public List<object> Children { get; } = new();
        public bool ShouldPaint { get; set; } = true;
        public bool DrawText { get; set; } = true;
        public object? Image { get; set; }
        public FakeTextParams TextParams { get; } = new();
    }

    private sealed class FakeTextParams
    {
        public string paintingRectangle { get; set; } = "{X=10,Y=20,W=99,H=200}";
        public string font { get; set; } = "SegoeUI12";
    }

    private sealed class FakeTileGroup
    {
        public string Text { get; set; } = "";
        public Size Size { get; set; }
        public List<object> Items { get; } = new();
    }

    private sealed class FakePanorama : System.Windows.Forms.Control
    {
        public List<object> Groups { get; } = new();
    }

    [Fact]
    public void DumpTileVisualState_Surfaces_Tile_Text_And_Size()
    {
        using var form = new System.Windows.Forms.Form();
        var pano = new FakePanorama { Name = "panorama" };
        var group = new FakeTileGroup { Text = "Popular", Size = new Size(800, 400) };
        var tile = new FakeTileElement { Text = "The Secret by Rhonda Byrne", Size = new Size(90, 76) };
        group.Items.Add(tile);
        pano.Groups.Add(group);
        form.Controls.Add(pano);

        string output = CaptureConsole(() => WinFormsAppHost.DumpTileVisualState(form));
        Assert.Contains("[TILE-VIS]", output);
        Assert.Contains("FakePanorama", output);
        Assert.Contains("The Secret by Rhonda Byrne", output);
        Assert.Contains("90", output);
    }

    [Fact]
    public void DumpTileVisualState_Surfaces_Paint_Gates()
    {
        using var form = new System.Windows.Forms.Form();
        var pano = new FakePanorama { Name = "panorama" };
        var group = new FakeTileGroup { Text = "Popular", Size = new Size(800, 400) };
        var tile = new FakeTileElement { Text = "Gate probe", Size = new Size(90, 76) };
        group.Items.Add(tile);
        pano.Groups.Add(group);
        form.Controls.Add(pano);

        string output = CaptureConsole(() => WinFormsAppHost.DumpTileVisualState(form));
        Assert.Contains("SP=True", output);
        Assert.Contains("DT=True", output);
        Assert.Contains("Img=null", output);
        Assert.Contains("PR={X=10", output);
        Assert.Contains("TPF=SegoeUI12", output);
        Assert.Contains("FNT=", output);
        Assert.Contains("CBR=", output);
        Assert.Contains("TT=", output);
        Assert.Contains("SPC=", output);
        Assert.Contains("IMPL=", output);
        Assert.Contains("LAYC=", output);
        Assert.Contains("ZC2=", output);
    }

    private sealed class FakePanoramaWithGroups : System.Windows.Forms.Control
    {
        public object PanoramaElement => this;
        public List<object> Groups { get; } = new();
    }

    [Fact]
    public void MaybeEnsureTileRepaint_Invalidates_Once_Groups_Arrive()
    {
        using var form = new System.Windows.Forms.Form();
        var pano = new FakePanoramaWithGroups { Name = "panorama" };
        pano.Groups.Add(new FakeTileGroup { Text = "Popular" });
        form.Controls.Add(pano);

        string output = CaptureConsole(() =>
        {
            Assert.True(WinFormsAppHost.MaybeEnsureTileRepaint(pano));
        });
        Assert.Contains("[TILE-VIS] ensure-paint invalidated", output);
        // Must go through the headless repaint gate (plain Invalidate is
        // a handleless no-op): the panorama must be dirty-whole after.
        Assert.True(System.Windows.Forms.WasmRepaintGate.DrainDirty(out var dirty) || dirty.Count > 0);
        Assert.True(dirty.ContainsKey(pano) && dirty[pano] is null);
        using var empty = new FakePanoramaWithGroups { Name = "panorama" };
        Assert.False(WinFormsAppHost.MaybeEnsureTileRepaint(empty));
    }

    private sealed class FakePaintElement
    {
        public event System.Windows.Forms.PaintEventHandler? ElementPainted;
        public void Raise(System.Drawing.Graphics g, System.Drawing.Rectangle r) =>
            ElementPainted?.Invoke(this, new System.Windows.Forms.PaintEventArgs(g, r));
    }

    private sealed class FakePaintParent
    {
        public List<object> Groups { get; } = new();
    }

    [Fact]
    public void SubscribeTilePaintFire_Counts_Element_Paints()
    {
        var el = new FakePaintElement();
        Assert.True(WinFormsAppHost.SubscribeTilePaintFire(el) > 0);
        // Traversal must skip null kid slots (Children/Groups/Items on a
        // plain holder) and still reach nested elements with the event.
        var parent = new FakePaintParent();
        parent.Groups.Add(el);
        Assert.True(WinFormsAppHost.SubscribeTilePaintFire(parent) > 0);
        using var bmp = new System.Drawing.Bitmap(4, 4);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        el.Raise(g, new System.Drawing.Rectangle(0, 0, 4, 4));
        var fires = WinFormsAppHost.TakePaintFire();
        Assert.Contains(fires.Keys, k => k.StartsWith("FakePaintElement|", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ArmTileRedumpOnResize_Is_Idempotent_Without_Throw()
    {
        using var form = new System.Windows.Forms.Form();
        using var child = new System.Windows.Forms.Control();
        form.Controls.Add(child);
        WinFormsAppHost.ArmTileRedumpOnResize(child);
        WinFormsAppHost.ArmTileRedumpOnResize(child);
    }

    [Fact]
    public void DumpInkCensus_Prints_Three_Lines_Without_Throw()
    {
        string output = CaptureConsole(() => WinFormsAppHost.DumpInkCensus());
        Assert.Contains("TEXT-CENSUS draws=", output);
        Assert.Contains("GDI-CENSUS paints=", output);
        Assert.Contains("DS-CENSUS draws=", output);
    }

    [Fact]
    public void DumpTileVisualState_Noiseless_Without_Panorama()
    {
        using var form = new System.Windows.Forms.Form();
        form.Controls.Add(new System.Windows.Forms.Button { Text = "x" });
        string output = CaptureConsole(() => WinFormsAppHost.DumpTileVisualState(form));
        // No tile-element lines without a panorama; the one-line text
        // census still logs (bounded session diagnostic).
        Assert.DoesNotContain("FakePanorama", output);
        Assert.Contains("TEXT-CENSUS draws=", output);
    }

    [Fact]
    public void FormatPanoScrollState_Reports_Form_Geometry_Without_Panorama_Parts()
    {
        using var form = new System.Windows.Forms.Form { ClientSize = new Size(1192, 716) };
        var pano = new FakePanorama { Name = "radPanorama1" };
        form.Controls.Add(pano);
        form.Controls.Add(new System.Windows.Forms.Panel { Name = "upperPanel", Height = 115 });

        string line = WinFormsAppHost.FormatPanoScrollState(pano);
        Assert.Contains("pano-scroll", line);
        Assert.Contains("radPanorama1", line);
        Assert.Contains("1192", line);
        Assert.Contains("sb=n/a", line);
        Assert.Contains("upperPanel=", line);
    }

    [Fact]
    public void FormatPanoScrollState_Null_Is_Noiseless()
    {
        Assert.Equal("pano-scroll n/a", WinFormsAppHost.FormatPanoScrollState(null));
    }

    [Fact]
    public void FormatLayoutOffset_Without_Element_Is_Noiseless()
    {
        Assert.Equal("layoff=n/a", WinFormsAppHost.FormatLayoutOffset(new FakePanorama()));
        Assert.Equal("layoff=n/a", WinFormsAppHost.FormatLayoutOffset(null));
    }

    [Fact]
    public void MaybeEnsureTileRepaint_Defers_BookShelf_Fit_Until_Groups_Arrive()
    {
        using var form = new BookShelf.FakeMainForm();
        var pano = new FakePanoramaWithGroups { Name = "radPanorama1" };
        pano.Groups.Add(new FakeTileGroup { Text = "Popular" });
        form.Controls.Add(pano);

        string output = CaptureConsole(() =>
        {
            Assert.True(WinFormsAppHost.MaybeEnsureTileRepaint(pano));
        });
        // Creation/viewport fits are skipped for BookShelf (they would
        // SizeChanged with unmeasured content); the fit lands here once
        // groups exist so the app's centering handler measures real sizes.
        Assert.Contains("[TILE-VIS] deferred-fit applied BookShelf", output);
    }

    private static string CaptureConsole(Action action)
    {
        var sb = new System.Text.StringBuilder();
        using var writer = new System.IO.StringWriter(sb);
        System.IO.TextWriter prior = Console.Out;
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(prior);
        }

        return sb.ToString();
    }
}

namespace BookShelf
{
    // Type-name stand-in for the vendor MainForm: the deferred-fit gate
    // keys off the BookShelf namespace in the form's full type name.
    public sealed class FakeMainForm : System.Windows.Forms.Form
    {
    }
}
