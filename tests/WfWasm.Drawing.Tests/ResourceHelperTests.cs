extern alias WfDraw;

using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.ExceptionServices;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfResourceHelper = WfDraw::WfWasm.Drawing.ResourceHelper;
using Xunit;

public sealed class ResourceHelperTests
{
    [Fact]
    public void AppBaseDirectory_Passes_Real_Value_Through()
    {
        // Root of the WasmAppIdentity.BaseDirectory shim pair: the real
        // runtime answer passes through wherever it exists; only the
        // browser's null/empty falls back to "/app" (untestable here).
        string real = System.AppContext.BaseDirectory;
        Assert.False(string.IsNullOrEmpty(real));
        Assert.Equal(real, WfResourceHelper.AppBaseDirectory());
    }

    // The rewriter swaps ComponentResourceManager.GetObject for
    // SafeGetObject: a BinaryFormatter ImageListStreamer blob (which
    // the browser BCL cannot deserialize) must answer null -- the
    // caller's ImageListStreamer assignment would die with
    // InvalidCast on the bitmap placeholder. The manager below is
    // real (Cecil-built assembly, genuine typed entries), so entries
    // read exactly like the browser's; the blob's type string is
    // copied verbatim from live Telerik exes (PhotoAlbum,
    // DemoAppsHub).
    // Regression: InvalidCastException | Form1.InitializeComponent on
    // the first Telerik demo.
    [Fact]
    public void SafeGetObject_Answers_Null_For_Non_Image_Entry()
    {
        var rm = new ResourceManager("Fake.Form1", BuildResourceAssembly());
        Assert.Null(WfResourceHelper.SafeGetObject(rm, "imageList2.ImageStream"));
    }

    [Fact]
    public void SafeGetObject_Passes_String_Entry_Through()
    {
        var rm = new ResourceManager("Fake.Form1", BuildResourceAssembly());
        Assert.Equal("hello", WfResourceHelper.SafeGetObject(rm, "plain.String"));
    }

    // Raw ByteArray entries (Telerik *_svg icon payloads, verified
    // live in Telerik.WinControls.UI.dll) must pass through
    // verbatim: the caller casts to byte[], and null kills
    // construction further down (desktop BCL returns the bytes).
    // Regression: ArgumentNullException | RadSvgImage.FromByteArray
    // on BugTracker (sparkles_svg etc. nulled as "non-image").
    [Fact]
    public void SafeGetObject_Passes_ByteArray_Entry_Through()
    {
        var rm = new ResourceManager("Fake.Form1", BuildResourceAssembly());
        byte[] bytes = Assert.IsType<byte[]>(WfResourceHelper.SafeGetObject(rm, "icon.Svg"));
        Assert.Equal(new byte[] { 60, 115, 118, 103 }, bytes);
    }

    [Fact]
    public void SafeGetObject_Keeps_Bitmap_For_Unknown_Name()
    {
        var rm = new ResourceManager("Fake.Form1", BuildResourceAssembly());
        Assert.IsType<WfBitmap>(WfResourceHelper.SafeGetObject(rm, "missing"));
    }

    // Legacy .resources store images as BinaryFormatter user-type entries
    // (ResourceWriter cannot emit those on .NET 9+, so the V2 stream is
    // crafted by hand): SafeGetObject must recover the real pixels
    // instead of the black 32x32 placeholder.
    // Regression: black squares for Telerik/BookShelf toolbar icons
    // (PNSE swallowed per lookup, placeholder returned).
    [Fact]
    public void SafeGetObject_Decodes_BinaryFormatter_Bitmap_Entry()
    {
        byte[] bmp = NrbfFixtures.Bmp1x1(0x33, 0x22, 0x11);
        var rm = new ResourceManager("Fake.NrbfBmp", BuildNrbfAssembly("Fake.NrbfBmp",
            ("pic.Logo", BitmapType, NrbfFixtures.BitmapPayload(bmp), null)));
        var obj = WfResourceHelper.SafeGetObject(rm, "pic.Logo");
        var decoded = Assert.IsType<WfBitmap>(obj);
        Assert.Equal(1, decoded.Width);
        Assert.Equal(1, decoded.Height);
        Assert.Equal(0x33, decoded.GetPixel(0, 0).R);
    }

    [Fact]
    public void SafeGetObject_Decodes_BinaryFormatter_Icon_Entry()
    {
        byte[] ico = NrbfFixtures.Ico1x1();
        var rm = new ResourceManager("Fake.NrbfIcon", BuildNrbfAssembly("Fake.NrbfIcon",
            ("app.Icon", IconType, NrbfFixtures.IconPayload(ico), null)));
        var obj = WfResourceHelper.SafeGetObject(rm, "app.Icon");
        var decoded = Assert.IsType<WfDraw::System.Drawing.Icon>(obj);
        Assert.Equal(1, decoded.Width);
        Assert.Equal(1, decoded.Height);
    }

    // Designer Point/Size values ride BinaryFormatter user-type entries
    // (ResourceWriter cannot emit those on .NET 9+, so the DRR-stamped
    // V2 stream is crafted by hand, like the bitmap fixtures): the raw
    // walk must recover the exact values with no deserializer and no
    // first-chance throw, or daycontainer-style layout collapses to
    // defaults. Numbers are the live grp2Calendar entries.
    // Regression: blank Group2 Calendar window with spilled day cells
    // ([RESMISS] daycontainer.Location/Size, DeserializingResourceReader
    // NSE xN at boot).
    [Fact]
    public void SafeGetObject_Decodes_Point_In_DeserializingReader_Stream()
    {
        var rm = new ResourceManager("Fake.NrbfPoint", BuildStampedAssembly("Fake.NrbfPoint",
            ModernReaderStamp,
            ("daycontainer.Location", PointType, NrbfFixtures.PointPayload(20, 119), null)));
        int nse = CountFirstChance<NotSupportedException>(() =>
        {
            var decoded = Assert.IsType<Point>(WfResourceHelper.SafeGetObject(rm, "daycontainer.Location"));
            Assert.Equal(new Point(20, 119), decoded);
        });
        Assert.Equal(0, nse);
    }

    [Fact]
    public void SafeGetObject_Decodes_Size_In_DeserializingReader_Stream()
    {
        var rm = new ResourceManager("Fake.NrbfSize", BuildStampedAssembly("Fake.NrbfSize",
            ModernReaderStamp,
            ("daycontainer.Size", SizeType, NrbfFixtures.SizePayload(1104, 711), null)));
        int nse = CountFirstChance<NotSupportedException>(() =>
        {
            var decoded = Assert.IsType<Size>(WfResourceHelper.SafeGetObject(rm, "daycontainer.Size"));
            Assert.Equal(new Size(1104, 711), decoded);
        });
        Assert.Equal(0, nse);
    }

    // The live shape (captured grp2Calendar blobs: 0x03 + "20, 119").
    [Fact]
    public void SafeGetObject_Decodes_ConverterString_Point()
    {
        var rm = new ResourceManager("Fake.NrbfPointText", BuildStampedAssembly("Fake.NrbfPointText",
            ModernReaderStamp,
            ("daycontainer.Location", PointType, NrbfFixtures.PointText(20, 119), null)));
        int nse = CountFirstChance<NotSupportedException>(() =>
        {
            var decoded = Assert.IsType<Point>(WfResourceHelper.SafeGetObject(rm, "daycontainer.Location"));
            Assert.Equal(new Point(20, 119), decoded);
        });
        Assert.Equal(0, nse);
    }

    [Fact]
    public void SafeGetObject_Decodes_ConverterString_Size()
    {
        var rm = new ResourceManager("Fake.NrbfSizeText", BuildStampedAssembly("Fake.NrbfSizeText",
            ModernReaderStamp,
            ("daycontainer.Size", SizeType, NrbfFixtures.SizeText(1104, 711), null)));
        int nse = CountFirstChance<NotSupportedException>(() =>
        {
            var decoded = Assert.IsType<Size>(WfResourceHelper.SafeGetObject(rm, "daycontainer.Size"));
            Assert.Equal(new Size(1104, 711), decoded);
        });
        Assert.Equal(0, nse);
    }

    [Fact]
    public void SafeGetObject_Rejects_WrongClass_Value_Payload()
    {
        // Point bytes filed under a Rectangle entry: the class gate must
        // miss (null like other non-image user types), never fabricate.
        var rm = new ResourceManager("Fake.NrbfWrongClass", BuildStampedAssembly("Fake.NrbfWrongClass",
            ModernReaderStamp,
            ("rect.Box", RectangleType, NrbfFixtures.PointPayload(1, 2), null)));
        Assert.Null(WfResourceHelper.SafeGetObject(rm, "rect.Box"));
    }

    [Fact]
    public void SafeGetObject_Rejects_Truncated_Value_Payload()
    {
        byte[] full = NrbfFixtures.SizePayload(10, 20);
        byte[] cut = new byte[full.Length - 3];
        Array.Copy(full, cut, cut.Length);
        var rm = new ResourceManager("Fake.NrbfTruncated", BuildStampedAssembly("Fake.NrbfTruncated",
            ModernReaderStamp,
            ("daycontainer.Size", SizeType, cut, null)));
        Assert.Null(WfResourceHelper.SafeGetObject(rm, "daycontainer.Size"));
    }

    // Modern vendor streams stamp DeserializingResourceReader (with the
    // vendor reference-assembly version, here 4.0.0.0 like the real
    // Syncfusion payloads): the legacy reader throws NSE on them and the
    // named reader exposes no raw data, so the raw walk must recover the
    // entry with pixel-identical bytes and no first-chance throw.
    // Headless cannot resolve the real reader (ASP.NET-only assembly),
    // which is exactly why the walk exists: no reader is involved.
    [Fact]
    public void SafeGetObject_Decodes_Bitmap_In_DeserializingReader_Stream()
    {
        byte[] bmp = NrbfFixtures.Bmp1x1(0x44, 0x33, 0x22);
        var rm = new ResourceManager("Fake.ModernBmp", BuildStampedAssembly("Fake.ModernBmp",
            ModernReaderStamp,
            ("pic.Logo", BitmapType, NrbfFixtures.BitmapPayload(bmp), null)));
        int nse = CountFirstChance<NotSupportedException>(() =>
        {
            var decoded = Assert.IsType<WfBitmap>(WfResourceHelper.SafeGetObject(rm, "pic.Logo"));
            Assert.Equal(1, decoded.Width);
            Assert.Equal(0x44, decoded.GetPixel(0, 0).R);
        });
        Assert.Equal(0, nse);
    }

    // User-type payload bounds must not depend on the entry position: a
    // record whose data offset is nonzero truncated to (bound - offset
    // - offset) until the walk stopped re-adding the data-section base.
    // Two bitmaps force the second entry off offset zero.
    [Fact]
    public void SafeGetObject_Decodes_NonzeroOffset_Bitmap_In_DeserializingReader_Stream()
    {
        byte[] first = NrbfFixtures.Bmp1x1(0x11, 0x22, 0x33);
        byte[] second = NrbfFixtures.Bmp1x1(0x44, 0x55, 0x66);
        var rm = new ResourceManager("Fake.ModernPair", BuildStampedAssembly("Fake.ModernPair",
            ModernReaderStamp,
            ("pic.First", BitmapType, NrbfFixtures.BitmapPayload(first), null),
            ("pic.Second", BitmapType, NrbfFixtures.BitmapPayload(second), null)));
        var d1 = Assert.IsType<WfBitmap>(WfResourceHelper.SafeGetObject(rm, "pic.First"));
        Assert.Equal(0x11, d1.GetPixel(0, 0).R);
        var d2 = Assert.IsType<WfBitmap>(WfResourceHelper.SafeGetObject(rm, "pic.Second"));
        Assert.Equal(0x44, d2.GetPixel(0, 0).R);
    }

    private const string ModernReaderStamp = "System.Resources.Extensions.DeserializingResourceReader, System.Resources.Extensions, Version=4.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51";

    // .NET Core+ writers store Bitmap/Icon with no BinaryFormatter envelope:
    // 0x02 marker + 7-bit length + raw image-file bytes (observed live in
    // DRR-stamped Syncfusion streams: 02 + len + PNG). The decoder must
    // lift the inner file; the NRBF walker alone misses it.
    private static byte[] PrefixedPayload(byte[] file)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0x02);
        uint v = (uint)file.Length;
        while (v >= 0x80) { ms.WriteByte((byte)(v | 0x80)); v >>= 7; }
        ms.WriteByte((byte)v);
        ms.Write(file, 0, file.Length);
        return ms.ToArray();
    }

    [Fact]
    public void SafeGetObject_Decodes_PrefixedPng_Bitmap_In_DeserializingReader_Stream()
    {
        byte[] bmp = NrbfFixtures.Bmp1x1(0x44, 0x33, 0x22);
        var rm = new ResourceManager("Fake.ModernPrefixed", BuildStampedAssembly("Fake.ModernPrefixed",
            ModernReaderStamp,
            ("pic.Logo", BitmapType, PrefixedPayload(bmp), null)));
        int nse = CountFirstChance<NotSupportedException>(() =>
        {
            var decoded = Assert.IsType<WfBitmap>(WfResourceHelper.SafeGetObject(rm, "pic.Logo"));
            Assert.Equal(1, decoded.Width);
            Assert.Equal(0x44, decoded.GetPixel(0, 0).R);
        });
        Assert.Equal(0, nse);
    }

    [Fact]
    public void SafeGetObject_Decodes_PrefixedIcon_In_DeserializingReader_Stream()
    {
        byte[] ico = NrbfFixtures.Ico1x1();
        var rm = new ResourceManager("Fake.ModernPrefixedIcon", BuildStampedAssembly("Fake.ModernPrefixedIcon",
            ModernReaderStamp,
            ("app.Icon", IconType, PrefixedPayload(ico), null)));
        var decoded = Assert.IsType<WfDraw::System.Drawing.Icon>(WfResourceHelper.SafeGetObject(rm, "app.Icon"));
        Assert.Equal(1, decoded.Width);
    }

    // Two loaded assemblies share one base name; the entry lives only in
    // the second. The old load-order first-stream-hit scan resolved the
    // first assembly and missed every entry into the 32px placeholder.
    // Regression: BugTracker preloader/validation_icon/FilteringIcon
    // placeholders (telerik-bugtracker-compat F3).
    [Fact]
    public void SafeGetObject_Resolves_Entry_In_Owning_Assembly_On_BaseName_Collision()
    {
        _ = BuildNrbfAssembly("Fake.Shared",
            ("other.Entry", BitmapType, NrbfFixtures.BitmapPayload(NrbfFixtures.Bmp1x1(9, 9, 9)), null));
        Assembly owner = BuildNrbfAssembly("Fake.Shared",
            ("pic.Logo", BitmapType, NrbfFixtures.BitmapPayload(NrbfFixtures.Bmp1x1(0x11, 0x22, 0x33)), null));
        var rm = new ResourceManager("Fake.Shared", owner);
        var decoded = Assert.IsType<WfBitmap>(WfResourceHelper.SafeGetObject(rm, "pic.Logo"));
        Assert.Equal(1, decoded.Width);
        Assert.Equal(0x11, decoded.GetPixel(0, 0).R);
    }

    [Fact]
    public void SafeGetObject_Caches_Decoded_Image_By_Manager_And_Name()
    {
        byte[] bmp = NrbfFixtures.Bmp1x1(1, 2, 3);
        var rm = new ResourceManager("Fake.NrbfCache", BuildNrbfAssembly("Fake.NrbfCache",
            ("pic.Cache", BitmapType, NrbfFixtures.BitmapPayload(bmp), null)));
        object? first = WfResourceHelper.SafeGetObject(rm, "pic.Cache");
        object? second = WfResourceHelper.SafeGetObject(rm, "pic.Cache");
        Assert.Same(first, second);
    }

    [Fact]
    public void SafeGetObject_Placeholder_Stays_Fresh_Per_Call()
    {
        var rm = new ResourceManager("Fake.NrbfMiss", BuildNrbfAssembly("Fake.NrbfMiss",
            ("pic.Cache", BitmapType, NrbfFixtures.BitmapPayload(NrbfFixtures.Bmp1x1(1, 2, 3)), null)));
        object? first = WfResourceHelper.SafeGetObject(rm, "no.such.entry");
        object? second = WfResourceHelper.SafeGetObject(rm, "no.such.entry");
        Assert.IsType<WfBitmap>(first);
        Assert.NotSame(first, second);
    }
    [Fact]
    public void SafeApplyResources_Sets_Image_From_Binary_Payload()
    {
        // entry.Value throws PNSE for the image: the per-key fallback
        // must still set the property (and must not abort the string).
        byte[] bmp = NrbfFixtures.Bmp1x1(0xAA, 0xBB, 0xCC);
        var rm = new TestComponentResourceManager("Fake.NrbfApply", BuildNrbfAssembly("Fake.NrbfApply",
            ("comp.Image", BitmapType, NrbfFixtures.BitmapPayload(bmp), null),
            ("comp.Text", null, null, "hi")));
        Assert.Equal("Fake.NrbfApply", rm.BaseName);
        var widget = new FakeWidget();
        WfResourceHelper.SafeApplyResources(rm, widget, "comp");
        Assert.NotNull(widget.Image);
        Assert.Equal(1, widget.Image.Width);
        Assert.Equal(0xAA, widget.Image.GetPixel(0, 0).R);
        Assert.Equal("hi", widget.Text);
    }

    // Throw-free decode (regression for the per-boot PNSE first-chance
    // noise across all Telerik demos, e.g. CreateUntypedDelegate 41x):
    // SafeGetObject must resolve the BinaryFormatter bitmap entry
    // through the NRBF path without a single first-chance
    // PlatformNotSupportedException, with identical pixels.
    [Fact]
    public void SafeGetObject_Decodes_Bitmap_Without_FirstChance_Throw()
    {
        byte[] bmp = NrbfFixtures.Bmp1x1(0x33, 0x22, 0x11);
        var rm = new ResourceManager("Fake.NrbfQuiet", BuildNrbfAssembly("Fake.NrbfQuiet",
            ("pic.Logo", BitmapType, NrbfFixtures.BitmapPayload(bmp), null)));
        int pnse = CountFirstChance<PlatformNotSupportedException>(() =>
        {
            var decoded = Assert.IsType<WfBitmap>(WfResourceHelper.SafeGetObject(rm, "pic.Logo"));
            Assert.Equal(1, decoded.Width);
            Assert.Equal(0x33, decoded.GetPixel(0, 0).R);
        });
        Assert.Equal(0, pnse);
    }

    [Fact]
    public void SafeGetObject_Passes_String_Without_FirstChance_Throw()
    {
        var rm = new ResourceManager("Fake.QuietStr", BuildNrbfAssembly("Fake.QuietStr",
            ("plain.String", null, null, "hello")));
        int pnse = CountFirstChance<PlatformNotSupportedException>(() =>
        {
            Assert.Equal("hello", WfResourceHelper.SafeGetObject(rm, "plain.String"));
        });
        Assert.Equal(0, pnse);
    }

    [Fact]
    public void SafeApplyResources_Sets_Image_Without_FirstChance_Throw()
    {
        byte[] bmp = NrbfFixtures.Bmp1x1(0xAA, 0xBB, 0xCC);
        var rm = new TestComponentResourceManager("Fake.NrbfQuietApply", BuildNrbfAssembly("Fake.NrbfQuietApply",
            ("comp.Image", BitmapType, NrbfFixtures.BitmapPayload(bmp), null),
            ("comp.Text", null, null, "hi")));
        var widget = new FakeWidget();
        int pnse = CountFirstChance<PlatformNotSupportedException>(() =>
        {
            WfResourceHelper.SafeApplyResources(rm, widget, "comp");
        });
        Assert.Equal(0, pnse);
        Assert.NotNull(widget.Image);
        Assert.Equal(1, widget.Image.Width);
        Assert.Equal(0xAA, widget.Image.GetPixel(0, 0).R);
        Assert.Equal("hi", widget.Text);
    }

    private static int CountFirstChance<T>(Action action) where T : Exception
    {
        int hits = 0;
        void Handler(object? _, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is T)
                System.Threading.Interlocked.Increment(ref hits);
        }
        System.AppDomain.CurrentDomain.FirstChanceException += Handler;
        try
        {
            action();
        }
        finally
        {
            System.AppDomain.CurrentDomain.FirstChanceException -= Handler;
        }
        return hits;
    }

    private enum FakeDock
    {
        None,
        Fill
    }

    // Generic TypeConverter coverage (replaces the removed BCL
    // ApplyResources conversions): a string entry must convert to an
    // enum property with no first-chance PNSE.
    [Fact]
    public void SafeApplyResources_Converts_String_To_Enum_Without_FirstChance_Throw()
    {
        var rm = new TestComponentResourceManager("Fake.NrbfEnum", BuildNrbfAssembly("Fake.NrbfEnum",
            ("comp.Dock", null, null, "Fill")));
        var widget = new FakeWidget();
        int pnse = CountFirstChance<PlatformNotSupportedException>(() =>
        {
            WfResourceHelper.SafeApplyResources(rm, widget, "comp");
        });
        Assert.Equal(0, pnse);
        Assert.Equal(FakeDock.Fill, widget.Dock);
    }

    private sealed class FakeWidget
    {
        public WfBitmap? Image { get; set; }
        public string? Text { get; set; }
        public FakeDock Dock { get; set; }
    }

    // ComponentResourceManager only binds (Type); the fixture stream
    // lives in a Cecil-built assembly, so rebind both halves.
    private sealed class TestComponentResourceManager : System.ComponentModel.ComponentResourceManager
    {
        public TestComponentResourceManager(string baseName, Assembly asm)
            : base(typeof(TestComponentResourceManager))
        {
            MainAssembly = asm;
            FieldInfo? f = typeof(ResourceManager).GetField("BaseNameField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f);
            f.SetValue(this, baseName);
        }
    }

    private const string BitmapType = "System.Drawing.Bitmap, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
    private const string IconType = "System.Drawing.Icon, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
    private const string PointType = "System.Drawing.Point, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
    private const string SizeType = "System.Drawing.Size, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
    private const string RectangleType = "System.Drawing.Rectangle, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
    private const string ImageListStreamerType = "System.Windows.Forms.ImageListStreamer, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

    private static Assembly BuildNrbfAssembly(string baseName, params (string Name, string? UserType, byte[]? Data, string? Text)[] entries) =>
        BuildStampedAssembly(baseName, null, entries);

    // Vendor modern streams stamp DeserializingResourceReader (with the
    // vendor reference-assembly version, here 4.0.0.0 like the real
    // Syncfusion payloads) instead of the legacy mscorlib reader.
    private static Assembly BuildStampedAssembly(string baseName, string? readerStamp, params (string Name, string? UserType, byte[]? Data, string? Text)[] entries)
    {
        byte[] resources = CraftV2(entries, readerStamp);
        var asmName = new Mono.Cecil.AssemblyNameDefinition("FakeNrbf", new Version(1, 0, 0, 0));
        using var asm = Mono.Cecil.AssemblyDefinition.CreateAssembly(asmName, "FakeNrbf", Mono.Cecil.ModuleKind.Dll);
        asm.MainModule.Resources.Add(new Mono.Cecil.EmbeddedResource(
            baseName + ".resources", Mono.Cecil.ManifestResourceAttributes.Public, new MemoryStream(resources)));
        using var outMs = new MemoryStream();
        asm.Write(outMs);
        return Assembly.Load(outMs.ToArray());
    }

    // Minimal .resources V2 writer (ResourceWriter cannot emit user-type
    // entries without BinaryFormatter): header, type table, hashes,
    // positions, names, then the data pairs. Hash mirrors
    // FastResourceComparer.HashFunction (5381, h * 33 ^ ch).
    private static byte[] CraftV2((string Name, string? UserType, byte[]? Data, string? Text)[] entries, string? readerStamp = null)
    {
        var typeIndex = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (e.UserType is not null && !typeIndex.ContainsKey(e.UserType))
                typeIndex[e.UserType] = typeIndex.Count;
        }
        // FindPosForResource binary-searches the hash table: names must
        // be stored hash-ascending, each followed by its Int32 data
        // offset (relative to the data section start).
        var order = entries
            .Select((e, i) => (Entry: e, Hash: ResourceHash(e.Name), Slot: i))
            .OrderBy(x => x.Hash)
            .ToArray();
        string readerName = readerStamp ?? "System.Resources.ResourceReader, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
        const string setName = "System.Resources.RuntimeResourceSet";
        byte[] readerBytes;
        byte[] setBytes;
        using (var tmp = new MemoryStream())
        {
            using (var w = new BinaryWriter(tmp))
                w.Write(readerName);
            readerBytes = tmp.ToArray();
        }
        using (var tmp = new MemoryStream())
        {
            using (var w = new BinaryWriter(tmp))
                w.Write(setName);
            setBytes = tmp.ToArray();
        }
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(unchecked((int)0xBEEFCACE));
            w.Write(1);
            w.Write(readerBytes.Length + setBytes.Length);
            // Raw writes: BinaryWriter.Write(byte[]) would add a 7-bit
            // length prefix the reader does not expect.
            w.BaseStream.Write(readerBytes, 0, readerBytes.Length);
            w.BaseStream.Write(setBytes, 0, setBytes.Length);
            w.Write(2);
            w.Write(entries.Length);
            w.Write(typeIndex.Count);
            foreach (var kv in typeIndex)
                w.Write(kv.Key);
            while (ms.Position % 8 != 0)
                w.Write((byte)0);
            long hashPos = ms.Position;
            foreach (var e in entries)
                w.Write(0);
            long posPos = ms.Position;
            foreach (var e in entries)
                w.Write(0);
            long dataOffPos = ms.Position;
            w.Write(0);
            long namesStart = ms.Position;
            int[] positions = new int[entries.Length];
            int[] dataOffsets = new int[entries.Length];
            using (var encoded = new MemoryStream())
            {
                using (var dw = new BinaryWriter(encoded))
                {
                    for (int i = 0; i < order.Length; i++)
                    {
                        var e = order[i].Entry;
                        dataOffsets[i] = (int)encoded.Position;
                        if (e.Text is not null)
                        {
                            dw.Write((byte)1);
                            dw.Write(e.Text);
                        }
                        else if (e.UserType is not null)
                        {
                            // User-type values carry no length prefix (the
                            // deserializer consumes its own graph; only
                            // ByteArray/Stream are Int32-prefixed).
                            dw.Write((byte)(64 + typeIndex[e.UserType]));
                            dw.BaseStream.Write(e.Data!, 0, e.Data!.Length);
                        }
                        else
                        {
                            dw.Write((byte)32);
                            dw.Write(e.Data!.Length);
                            dw.BaseStream.Write(e.Data!, 0, e.Data!.Length);
                        }
                    }
                    dw.Flush();
                    for (int i = 0; i < order.Length; i++)
                    {
                        positions[i] = (int)(ms.Position - namesStart);
                        WriteResourceName(w, order[i].Entry.Name);
                        w.Write(dataOffsets[i]);
                    }
                    long dataStart = ms.Position;
                    byte[] block = encoded.ToArray();
                    w.BaseStream.Write(block, 0, block.Length);
                    long end = ms.Position;
                    ms.Position = hashPos;
                    foreach (var x in order)
                        w.Write(x.Hash);
                    ms.Position = posPos;
                    foreach (int p in positions)
                        w.Write(p);
                    ms.Position = dataOffPos;
                    w.Write((int)dataStart);
                    ms.Position = end;
                }
            }
        }
        return ms.ToArray();
    }

    // Resource names use 7-bit BYTE length + UTF-16 bytes (NOT
    // BinaryWriter.Write(string), which is UTF-8): the reader halves
    // the prefix as the char count and compares two bytes per char.
    private static void WriteResourceName(BinaryWriter w, string name)
    {
        byte[] utf16 = System.Text.Encoding.Unicode.GetBytes(name);
        uint v = (uint)utf16.Length;
        while (v >= 0x80)
        {
            w.Write((byte)(v | 0x80));
            v >>= 7;
        }
        w.Write((byte)v);
        w.BaseStream.Write(utf16, 0, utf16.Length);
    }

    private static int ResourceHash(string key)
    {
        unchecked
        {
            uint h = 5381;
            foreach (char c in key)
                h = ((h << 5) + h) ^ c;
            return (int)h;
        }
    }

    private static Assembly BuildResourceAssembly()
    {
        // CraftV2 (ResourceWriter cannot emit BF user-type entries on
        // .NET 9+): ImageStream is a genuine ImageListStreamer blob,
        // icon.Svg is raw ByteArray like Telerik's *_svg payloads.
        return BuildNrbfAssembly("Fake.Form1",
            ("imageList2.ImageStream", ImageListStreamerType, new byte[] { 0, 1, 0, 0, 0, 255, 255, 255, 255, 1, 0, 0, 0, 0, 0, 0, 0 }, null),
            ("plain.String", null, null, "hello"),
            ("icon.Svg", null, new byte[] { 60, 115, 118, 103 }, null));
    }
}
