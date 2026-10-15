extern alias WfDraw;

using System.IO;
using System.Reflection;
using System.Resources;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfResourceHelper = WfDraw::WfWasm.Drawing.ResourceHelper;
using Xunit;

public sealed class ResourceHelperTests
{
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

    private sealed class FakeWidget
    {
        public WfBitmap? Image { get; set; }
        public string? Text { get; set; }
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
    private const string ImageListStreamerType = "System.Windows.Forms.ImageListStreamer, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

    private static Assembly BuildNrbfAssembly(string baseName, params (string Name, string? UserType, byte[]? Data, string? Text)[] entries)
    {
        byte[] resources = CraftV2(entries);
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
    private static byte[] CraftV2((string Name, string? UserType, byte[]? Data, string? Text)[] entries)
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
        const string readerName = "System.Resources.ResourceReader, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
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
