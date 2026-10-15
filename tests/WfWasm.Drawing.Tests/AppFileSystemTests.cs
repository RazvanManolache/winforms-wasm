extern alias WfDraw;

using System;
using System.IO;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfImage = WfDraw::System.Drawing.Image;
using WfVfs = WfDraw::WfWasm.Drawing.AppFileSystem;
using Xunit;

public sealed class AppFileSystemTests
{
    [Fact]
    public void Resolve_Exact_And_Normalized_Paths()
    {
        try
        {
            WfVfs.Register("Photos/1.jpg", new byte[] { 1, 2, 3 });
            Assert.True(WfVfs.TryGetBytes("Photos/1.jpg", out var a));
            Assert.Equal(new byte[] { 1, 2, 3 }, a);
            Assert.False(WfVfs.TryGetBytes("/background.jpg", out _));
            WfVfs.Register("background.jpg", new byte[] { 4 });
            Assert.True(WfVfs.TryGetBytes("/background.jpg", out var b));
            Assert.Equal(new byte[] { 4 }, b);
            Assert.True(WfVfs.TryGetBytes(@"Photos\1.jpg", out _));
            Assert.True(WfVfs.TryGetBytes(@"C:\demo\Photos\1.jpg", out _));
            Assert.False(WfVfs.TryGetBytes("Photos/9.jpg", out _));
            Assert.False(WfVfs.TryGetBytes(null, out _));
            Assert.False(WfVfs.TryGetBytes("", out _));
        }
        finally
        {
            WfVfs.Clear();
        }
    }

    [Fact]
    public void Clear_Drops_Registrations()
    {
        try
        {
            WfVfs.Register("x.bin", new byte[] { 9 });
            Assert.True(WfVfs.TryGetBytes("x.bin", out _));
            WfVfs.Clear();
            Assert.False(WfVfs.TryGetBytes("x.bin", out _));
        }
        finally
        {
            WfVfs.Clear();
        }
    }

    [Fact]
    public void GetFiles_Glob_Matches_PhotoAlbum_Call()
    {
        // Form1_Load: Directory.GetFiles(@"Photos\", "*.jpg") (rewritten
        // to AppFileSystem). Returns caller-spelled paths a follow-up
        // FromFile resolves through the same registry.
        try
        {
            byte[] px = new byte[] { 255, 0, 0, 255 };
            WfVfs.RegisterDecoded("Photos/1.jpg", 1, 1, px);
            WfVfs.RegisterDecoded("Photos/2.jpg", 1, 1, px);
            WfVfs.Register("Photos/notes.txt", new byte[] { 7 });
            WfVfs.Register("Photos/Sub/3.jpg", new byte[] { 8 });
            string[] jpgs = WfVfs.GetFiles(@"Photos\", "*.jpg");
            Assert.Equal(2, jpgs.Length);
            Assert.All(jpgs, p => Assert.StartsWith(@"Photos\", p));
            Assert.Contains(@"Photos\1.jpg", jpgs);
            string[] all = WfVfs.GetFiles(@"Photos\");
            Assert.Equal(3, all.Length);
            string[] deep = WfVfs.GetFiles(@"Photos\", "*.jpg", SearchOption.AllDirectories);
            Assert.Equal(3, deep.Length);
            Assert.Empty(WfVfs.GetFiles(@"Photos\", "*.png"));
            Assert.Empty(WfVfs.GetFiles(@"Missing\"));
        }
        finally
        {
            WfVfs.Clear();
        }
    }

    [Fact]
    public void Decoded_Pixels_Win_And_Map_Rgba_To_Argb()
    {
        // JPEG has no managed decoder: the loader decodes in the browser
        // and registers pixels. Decoded entries beat raw bytes; RGBA
        // maps to ARGB (opaque red -> A=255 R=255 G=0 B=0).
        try
        {
            WfVfs.Register("bg.jpg", TinyRedPng());
            WfVfs.RegisterDecoded("bg.jpg", 1, 1, new byte[] { 255, 0, 0, 255 });
            using var bmp = new WfBitmap("bg.jpg");
            Assert.Equal(1, bmp.Width);
            var c = bmp.GetPixel(0, 0);
            Assert.Equal(255, c.A);
            Assert.Equal(255, c.R);
            Assert.Equal(0, c.G);
            Assert.Equal(0, c.B);
        }
        finally
        {
            WfVfs.Clear();
        }
    }

    [Fact]
    public void Raw_Non_Png_Bytes_Throw_Actionable()
    {
        // Registered-but-undecoded JPEG is a bootstrap bug, not a missing
        // file: the error must say how to fix it.
        try
        {
            WfVfs.Register("raw.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0x00 });
            var ex = Assert.Throws<InvalidOperationException>(() => WfImage.FromFile("raw.jpg"));
            Assert.Contains("RegisterDecoded", ex.Message);
        }
        finally
        {
            WfVfs.Clear();
        }
    }

    [Fact]
    public void Clear_Drops_Decoded_Too()
    {
        try
        {
            WfVfs.RegisterDecoded("d.jpg", 1, 1, new byte[] { 0, 0, 0, 255 });
            Assert.True(WfVfs.TryGetDecoded("d.jpg", out _));
            WfVfs.Clear();
            Assert.False(WfVfs.TryGetDecoded("d.jpg", out _));
            Assert.Empty(WfVfs.GetFiles(""));
        }
        finally
        {
            WfVfs.Clear();
        }
    }

    [Fact]
    public void Bitmap_Resolves_Vfs_Bytes_Before_Disk()
    {
        // The browser has no disk: registered PNG bytes decode through
        // the managed codec. Regression: FileNotFoundException
        // '/background.jpg' on telerik-photo onload (JPEG instead
        // arrives via RegisterDecoded after a browser canvas decode).
        try
        {
            WfVfs.Register("/photos/red.png", TinyRedPng());
            using var bmp = new WfBitmap("/photos/red.png");
            Assert.Equal(1, bmp.Width);
            Assert.Equal(1, bmp.Height);
            using var img = WfImage.FromFile("/photos/red.png");
            Assert.Equal(1, img.Width);
            Assert.Equal(1, img.Height);
        }
        finally
        {
            WfVfs.Clear();
        }
    }

    // Minimal 1x1 RGB red PNG: signature + IHDR + IDAT (raw deflate
    // stored block: filter 0 + FF0000) + IEND, valid zlib/adler32/CRC32.
    private static byte[] TinyRedPng()
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        using var ihdr = new MemoryStream();
        WriteBe32(ihdr, 1);
        WriteBe32(ihdr, 1);
        ihdr.Write(new byte[] { 8, 2, 0, 0, 0 }, 0, 5);
        WriteChunk(png, "IHDR", ihdr.ToArray());
        byte[] raw = new byte[] { 0, 0xFF, 0, 0 };
        using var idat = new MemoryStream();
        idat.Write(new byte[] { 0x78, 0x9C }, 0, 2);
        idat.Write(new byte[] { 0x01 }, 0, 1);
        idat.Write(new byte[] { (byte)raw.Length, 0 }, 0, 2);
        idat.Write(new byte[] { (byte)~raw.Length, 0xFF }, 0, 2);
        idat.Write(raw, 0, raw.Length);
        uint s1 = 1, s2 = 0;
        foreach (byte b in raw) { s1 = (s1 + b) % 65521; s2 = (s2 + s1) % 65521; }
        WriteBe32(idat, (s2 << 16) | s1);
        WriteChunk(png, "IDAT", idat.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteBe32(Stream s, uint v) =>
        s.Write(new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }, 0, 4);

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        WriteBe32(png, (uint)data.Length);
        byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
        png.Write(name, 0, 4);
        png.Write(data, 0, data.Length);
        WriteBe32(png, Crc32(name, data));
    }

    private static uint Crc32(byte[] name, byte[] data)
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        uint crc = 0xFFFFFFFFu;
        foreach (byte b in name) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    [Fact]
    public void Exists_Prefers_Vfs_Then_Disk()
    {
        // Served content answers true even under StartupPath-rooted
        // absolutes; anything else falls through to the real disk.
        // Regression: ArgumentNullException Path.Combine(null) <-
        // BookShelfApp.LocalBackupFileName (backup never reached VFS).
        string probe = Path.Combine(Path.GetTempPath(), $"vfsdisk_{Guid.NewGuid()}.bin");
        try
        {
            WfVfs.Register("BackUp.xml", new byte[] { 1 });
            Assert.True(WfVfs.Exists(@"C:\demo\BackUp.xml"));
            Assert.True(WfVfs.Exists("/BackUp.xml"));
            Assert.False(WfVfs.Exists(probe));
            File.WriteAllBytes(probe, new byte[] { 2 });
            Assert.True(WfVfs.Exists(probe));
        }
        finally
        {
            WfVfs.Clear();
            try { File.Delete(probe); } catch { }
        }
    }

    [Fact]
    public void OpenFileStream_Serves_Real_Stream_For_Vfs()
    {
        // VFS hits spool to MEMFS so callers get a real FileStream
        // (XmlSerializer/Seek work unmodified); write modes drop the
        // entry so later reads see the disk, not stale bytes.
        try
        {
            WfVfs.Register("data.bin", new byte[] { 10, 20, 30 });
            using (var s = WfVfs.OpenFileStream(@"C:\demo\data.bin", FileMode.Open))
            {
                Assert.IsType<FileStream>(s);
                Assert.Equal(3, s.Length);
                Assert.Equal(10, s.ReadByte());
                s.Seek(0, SeekOrigin.Begin);
                Assert.Equal(10, s.ReadByte());
            }

            string wpath = Path.Combine(Path.GetTempPath(), $"vfswrite_{Guid.NewGuid()}.bin");
            WfVfs.Register(Path.GetFileName(wpath), new byte[] { 1 });
            using (WfVfs.OpenFileStream(wpath, FileMode.Create)) { }
            Assert.False(WfVfs.TryGetBytes(Path.GetFileName(wpath), out _));
            try { File.Delete(wpath); } catch { }
        }
        finally
        {
            WfVfs.Clear();
        }
    }

    [Fact]
    public void CopyFile_Materializes_Vfs_Source()
    {
        string dest = Path.Combine(Path.GetTempPath(), $"vfscopy_{Guid.NewGuid()}.bin");
        try
        {
            WfVfs.Register("src.bin", new byte[] { 7, 8 });
            WfVfs.CopyFile(@"C:\demo\src.bin", dest, overwrite: false);
            Assert.Equal(new byte[] { 7, 8 }, File.ReadAllBytes(dest));
            Assert.Throws<IOException>(() => WfVfs.CopyFile("src.bin", dest, overwrite: false));
            WfVfs.CopyFile("src.bin", dest, overwrite: true);
        }
        finally
        {
            WfVfs.Clear();
            try { File.Delete(dest); } catch { }
        }
    }

    [Fact]
    public void GetCreationTime_Reports_Now_For_Vfs()
    {
        try
        {
            WfVfs.Register("t.bin", new byte[] { 1 });
            DateTime before = DateTime.Now.AddSeconds(-5);
            Assert.True(WfVfs.GetCreationTime("t.bin") >= before);
            Assert.Equal(new DateTime(1601, 1, 1),
                WfVfs.GetCreationTime(Path.Combine(Path.GetTempPath(), $"vfsno_{Guid.NewGuid()}.bin")).Date);
        }
        finally
        {
            WfVfs.Clear();
        }
    }

    [Fact]
    public void MirrorPath_Accepts_Safe_Relative_Names()
    {
        // Unrewritten framework readers (System.Data, XmlReader) open
        // content on the real FS: safe manifest names mirror to MEMFS
        // root. Regression: FileNotFoundException '/XMLSchema.xsd' <-
        // BugTracker.RadRibbonForm1.FillDataSet.
        Assert.True(WfVfs.TryMirrorPath("XMLSchema.xsd", out string? a));
        Assert.Equal("/XMLSchema.xsd", a);
        Assert.True(WfVfs.TryMirrorPath("Photos/1.jpg", out string? b));
        Assert.Equal("/Photos/1.jpg", b);
    }

    [Fact]
    public void MirrorPath_Rejects_Escapes_And_Junk()
    {
        Assert.False(WfVfs.TryMirrorPath("../../source.xml", out _));
        Assert.False(WfVfs.TryMirrorPath("a/../../b", out _));
        Assert.False(WfVfs.TryMirrorPath("", out _));
        Assert.False(WfVfs.TryMirrorPath("C:/x/y.bin", out _));
        Assert.False(WfVfs.TryMirrorPath("a//b", out _));
        Assert.False(WfVfs.TryMirrorPath(".", out _));
        Assert.False(WfVfs.TryMirrorPath("ok/./no", out _));
        Assert.False(WfVfs.TryMirrorPath(new string('x', 261), out _));
        Assert.False(WfVfs.TryMirrorPath("a/" + new string('y', 129), out _));
    }

    [Fact]
    public void Register_Headless_Never_Touches_Disk()
    {
        // The MEMFS mirror is browser-only: headless Register calls
        // (tests, tools) must not write to disk anywhere.
        if (OperatingSystem.IsBrowser()) return;
        string unique = "vfsnomirror_" + Guid.NewGuid().ToString("N") + ".bin";
        string[] probes =
        {
            Path.Combine(Directory.GetCurrentDirectory(), unique),
            Path.Combine(Path.GetPathRoot(Directory.GetCurrentDirectory())!, unique),
            Path.Combine(Path.GetTempPath(), unique),
        };
        try
        {
            WfVfs.Register(unique, new byte[] { 1, 2, 3 });
            WfVfs.Register("sub/" + unique, new byte[] { 4 });
            foreach (string p in probes)
                Assert.False(File.Exists(p), p);
        }
        finally
        {
            WfVfs.Clear();
            foreach (string p in probes)
                try { File.Delete(p); } catch { }
        }
    }
}
