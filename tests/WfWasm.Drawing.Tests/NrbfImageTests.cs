extern alias WfDraw;

using System.IO;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfIcon = WfDraw::System.Drawing.Icon;
using WfNrbf = WfDraw::WfWasm.Drawing.NrbfImage;
using Xunit;

public sealed class NrbfImageTests
{
    [Fact]
    public void Extract_Bitmap_Payload_Returns_Image_Bytes()
    {
        byte[] bmp = NrbfFixtures.Bmp1x1(0x33, 0x22, 0x11);
        Assert.True(WfNrbf.TryExtractImageBytes(NrbfFixtures.BitmapPayload(bmp), out byte[] image));
        Assert.Equal(bmp, image);
        using var ms = new MemoryStream(image, false);
        using var decoded = new WfBitmap(ms);
        Assert.Equal(1, decoded.Width);
        Assert.Equal(1, decoded.Height);
        var px = decoded.GetPixel(0, 0);
        Assert.Equal(0x33, px.R);
        Assert.Equal(0x22, px.G);
        Assert.Equal(0x11, px.B);
    }

    [Fact]
    public void Extract_Icon_Payload_With_Size_Member_Returns_Ico_Bytes()
    {
        byte[] ico = NrbfFixtures.Ico1x1();
        Assert.True(WfNrbf.TryExtractImageBytes(NrbfFixtures.IconPayload(ico), out byte[] image));
        Assert.Equal(ico, image);
        using var ms = new MemoryStream(image, false);
        using var decoded = new WfIcon(ms);
        Assert.Equal(1, decoded.Width);
        Assert.Equal(1, decoded.Height);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0, 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 1, 0, 0, 0, 0, 0, 0, 0, 9, 1, 0, 0, 0 })]
    public void Extract_Malformed_Returns_False(byte[]? nrbf)
    {
        Assert.False(WfNrbf.TryExtractImageBytes(nrbf, out _));
    }

    [Fact]
    public void Extract_Null_Returns_False()
    {
        Assert.False(WfNrbf.TryExtractImageBytes(null, out _));
    }

    [Fact]
    public void Extract_Real_BookShelf_Bitmap_Entry_Yields_Png()
    {
        // fav_full (16x13 PNG) from the real BookShelf resources: the
        // ground-truth shape the walker must accept. Skips when the
        // demo is absent (same guard as the rewrite fixture tests).
        const string exe = @"E:\AI\Code\WindowsForms\telerik\Telerik UI for WinForms demos\ExamplesProjects\BookShelf\bin\BookShelf.exe";
        if (!File.Exists(exe)) return;
        byte[]? raw = ReadResourcePayload(exe, "BookShelf.Properties.Resources.resources", "fav_full", out string? typeName);
        Assert.NotNull(raw);
        Assert.Contains("Bitmap", typeName, StringComparison.Ordinal);
        Assert.True(WfNrbf.TryExtractImageBytes(raw, out byte[] image));
        Assert.Equal(0x89, image[0]);
        Assert.Equal(0x50, image[1]);
        using var ms = new MemoryStream(image, false);
        using var decoded = new WfBitmap(ms);
        Assert.Equal(16, decoded.Width);
        Assert.Equal(13, decoded.Height);
    }

    [Fact]
    public void Extract_Real_Telerik_Icon_Entry_Yields_Ico()
    {
        // WinFormsIcon (32x32) from the real Telerik resources: the
        // two-member Icon shape with the embedded Size class.
        const string dll = @"E:\AI\Code\WindowsForms\telerik\Telerik UI for WinForms demos\Bin\Release\Telerik.WinControls.dll";
        if (!File.Exists(dll)) return;
        byte[]? raw = ReadResourcePayload(dll, "Telerik.WinControls.Properties.Resources.resources", "WinFormsIcon", out string? typeName);
        Assert.NotNull(raw);
        Assert.Contains("Icon", typeName, StringComparison.Ordinal);
        Assert.True(WfNrbf.TryExtractImageBytes(raw, out byte[] image));
        Assert.Equal(0, image[0]);
        Assert.Equal(0, image[1]);
        Assert.Equal(1, image[2]);
        Assert.Equal(0, image[3]);
        using var ms = new MemoryStream(image, false);
        using var decoded = new WfIcon(ms);
        Assert.Equal(256, decoded.Width);
        Assert.Equal(256, decoded.Height);
    }

    // Real Syncfusion.Diagram.Base Bold payload (netfx writer
    // envelope: 04 + 7-bit length + raw BMP, 16x16 8bpp). 62 Bitmap
    // entries in that stream share this exact shape (BMP and PNG).
    private const string BoldBlobBase64 =
"BLYKQk02BQAAAAAAADYEAAAoAAAAEAAAABAAAAABAAgAAAAAAAABAAAzCwAAMwsAAAABAAAAAQAAAAAAAO/n5wD37+8A9/f3AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A////AP///wD///8A/wD/AP////////////////////////////////////////////////////////////////////////////////////////8AAAAAAAAAAP///////////wAAAP//AAAA//////////8AAAD//wAAAP//////////AAAA//8AAAD//////////wAAAAAAAAD///////////8AAAD//wAAAP//////////AAAA//8AAAD//////////wAAAP//AAAA/////////wAAAAAAAAAA//////////////////////////////////////////////////////////////////////8=";

    [Fact]
    public void Prefixed_Netfx04_Marker_Lifts_Bmp()
    {
        byte[] payload = System.Convert.FromBase64String(BoldBlobBase64);
        Assert.True(WfNrbf.TryExtractPrefixedImageBytes(payload, out byte[] image));
        Assert.Equal(1334, image.Length);
        Assert.Equal((byte)'B', image[0]);
        Assert.Equal((byte)'M', image[1]);
        using var ms = new MemoryStream(image, false);
        using var decoded = new WfBitmap(ms);
        Assert.Equal(16, decoded.Width);
        Assert.Equal(16, decoded.Height);
    }

    [Fact]
    public void Prefixed_Core02_Marker_Lifts_Png()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];
        byte[] payload = [0x02, (byte)png.Length, .. png];
        Assert.True(WfNrbf.TryExtractPrefixedImageBytes(payload, out byte[] image));
        Assert.Equal(png, image);
    }

    [Theory]
    [InlineData(new byte[] { 0x04, 0x01, 0x00 })]
    [InlineData(new byte[] { 0x04, 0x02, (byte)'X', (byte)'Y' })]
    [InlineData(new byte[] { 0x04, 0x05, (byte)'B', (byte)'M' })]
    public void Prefixed_Malformed_Returns_False(byte[] payload)
    {
        Assert.False(WfNrbf.TryExtractPrefixedImageBytes(payload, out _));
    }

    private static byte[]? ReadResourcePayload(string asmPath, string stream, string name, out string? typeName)
    {
        typeName = null;
        using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(asmPath);
        foreach (var res in asm.MainModule.Resources)
        {
            if (res is not Mono.Cecil.EmbeddedResource er || er.Name != stream)
                continue;
            using var ms = new MemoryStream(er.GetResourceData(), false);
            using var reader = new System.Resources.ResourceReader(ms);
            reader.GetResourceData(name, out string? tn, out byte[]? data);
            typeName = tn;
            return data;
        }
        return null;
    }
}
