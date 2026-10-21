extern alias Fork;
extern alias WasmDraw;

using Fork::Windows.Win32;
using Fork::Windows.Win32.UI.Controls;
using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfColor = WasmDraw::System.Drawing.Color;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using ForkStore = Fork::System.Windows.Forms.WasmImageListStore;

namespace WfWasm.WinForms.Tests;

// Managed imagelist store (Photo G4): headless has no comctl32, so
// ImageList_* natives back onto minted tokens holding decoded bitmaps.
// Draw/DrawEx paint through the HDC Graphics bridge, which is also how
// ImageList.Images[i] resolves (upstream GetBitmap paints via DrawEx).
public sealed class ImageListStoreTests
{
    [Fact]
    public unsafe void DrawEx_Paints_Slot_Through_Registered_Graphics()
    {
        using var red = new WfBitmap(2, 2);
        using var blue = new WfBitmap(2, 2);
        PaintSolid(red, WfColor.Red);
        PaintSolid(blue, WfColor.Blue);
        nint token = ForkStore.Create(2, 2, 8);
        ForkStore.SetImages(token, [red, blue], 2, 2, 8);

        using var canvas = new WfBitmap(8, 2);
        using var g = WfGraphics.FromImage(canvas);
        using var _ = PInvoke.WasmFillBridge.TrackTarget(default, g);

        HIMAGELIST himl = (HIMAGELIST)token;
        Assert.True(PInvoke.ImageList_DrawEx(himl, 1, default, 4, 0, 2, 2, default, default, default));
        Assert.Equal(WfColor.Blue.ToArgb(), canvas.GetPixel(4, 0).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), canvas.GetPixel(5, 1).ToArgb());

        PInvoke.ImageList_Destroy(himl);
        Assert.Equal(0, PInvoke.ImageList_GetImageCount(himl));
    }

    [Fact]
    public void GetImageInfo_Reports_Slot_Geometry()
    {
        using var red = new WfBitmap(16, 16);
        nint token = ForkStore.Create(16, 16, 8);
        ForkStore.SetImages(token, [red], 16, 16, 8);
        HIMAGELIST himl = (HIMAGELIST)token;

        Assert.Equal(1, PInvoke.ImageList_GetImageCount(himl));
        Assert.True(PInvoke.ImageList_GetImageInfo(himl, 0, out IMAGEINFO info));
        Assert.Equal(0, info.rcImage.left);
        Assert.Equal(0, info.rcImage.top);
        Assert.Equal(16, info.rcImage.right);
        Assert.Equal(16, info.rcImage.bottom);
        Assert.NotEqual(0, (nint)info.hbmImage);

        Assert.False(PInvoke.ImageList_GetImageInfo(himl, 5, out _));
        PInvoke.ImageList_Destroy(himl);
    }

    [Fact]
    public void Duplicate_Clones_And_Unknown_Tokens_Fail()
    {
        using var red = new WfBitmap(2, 2);
        nint token = ForkStore.Create(2, 2, 8);
        ForkStore.SetImages(token, [red], 2, 2, 8);

        HIMAGELIST clone = PInvoke.ImageList_Duplicate((HIMAGELIST)token);
        Assert.NotEqual(token, (nint)clone);
        Assert.Equal(1, PInvoke.ImageList_GetImageCount(clone));

        PInvoke.ImageList_Destroy((HIMAGELIST)token);
        Assert.Equal(0, PInvoke.ImageList_GetImageCount((HIMAGELIST)token));
        Assert.Equal(1, PInvoke.ImageList_GetImageCount(clone));

        HIMAGELIST unknown = (HIMAGELIST)0x7FFF;
        Assert.Equal(0, PInvoke.ImageList_GetImageCount(unknown));
        Assert.False(PInvoke.ImageList_DrawEx(unknown, 0, default, 0, 0, 2, 2, default, default, default));

        PInvoke.ImageList_Destroy(clone);
        Assert.Equal(0, PInvoke.ImageList_GetImageCount(clone));
    }

    private static void PaintSolid(WfBitmap bmp, WfColor color)
    {
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                bmp.SetPixel(x, y, color);
    }

    // Canonical bytes live in Drawing's ImageListStreamTests (same blob);
    // duplicated here because test projects cannot share fixtures.
    private const string RealBlobBase64 =
"AAEAAAD/////AQAAAAAAAAAMAgAAAFdTeXN0ZW0uV2luZG93cy5Gb3JtcywgVmVyc2lvbj00LjAuMC4wLCBDdWx0dXJlPW5ldXRyYWwsIFB1YmxpY0tleVRva2VuPWI3N2E1YzU2MTkzNGUwODkFAQAAACZTeXN0ZW0uV2luZG93cy5Gb3Jtcy5JbWFnZUxpc3RTdHJlYW1lcgEAAAAERGF0YQcCAgAAAAkDAAAADwMAAADMBwAAAk1TRnQBSQFMAgEBAwEAATABAAEwAQABEAEAARABAAT/AQkBAAj/AUIBTQE2AQQGAAE2AQQCAAEoAwABQAMAARADAAEBAQABCAYAAQQYAAGAAgABgAMAAoABAAGAAwABgAEAAYABAAKAAgADwAEAAcAB3AHAAQAB8AHKAaYBAAEzBQABMwEAATMBAAEzAQACMwIAAxYBAAMcAQADIgEAAykBAANVAQADTQEAA0IBAAM5AQABgAF8Af8BAAJQAf8BAAGTAQAB1gEAAf8B7AHMAQABxgHWAe8BAAHWAucBAAGQAakBrQIAAf8BMwMAAWYDAAGZAwABzAIAATMDAAIzAgABMwFmAgABMwGZAgABMwHMAgABMwH/AgABZgMAAWYBMwIAAmYCAAFmAZkCAAFmAcwCAAFmAf8CAAGZAwABmQEzAgABmQFmAgACmQIAAZkBzAIAAZkB/wIAAcwDAAHMATMCAAHMAWYCAAHMAZkCAALMAgABzAH/AgAB/wFmAgAB/wGZAgAB/wHMAQABMwH/AgAB/wEAATMBAAEzAQABZgEAATMBAAGZAQABMwEAAcwBAAEzAQAB/wEAAf8BMwIAAzMBAAIzAWYBAAIzAZkBAAIzAcwBAAIzAf8BAAEzAWYCAAEzAWYBMwEAATMCZgEAATMBZgGZAQABMwFmAcwBAAEzAWYB/wEAATMBmQIAATMBmQEzAQABMwGZAWYBAAEzApkBAAEzAZkBzAEAATMBmQH/AQABMwHMAgABMwHMATMBAAEzAcwBZgEAATMBzAGZAQABMwLMAQABMwHMAf8BAAEzAf8BMwEAATMB/wFmAQABMwH/AZkBAAEzAf8BzAEAATMC/wEAAWYDAAFmAQABMwEAAWYBAAFmAQABZgEAAZkBAAFmAQABzAEAAWYBAAH/AQABZgEzAgABZgIzAQABZgEzAWYBAAFmATMBmQEAAWYBMwHMAQABZgEzAf8BAAJmAgACZgEzAQADZgEAAmYBmQEAAmYBzAEAAWYBmQIAAWYBmQEzAQABZgGZAWYBAAFmApkBAAFmAZkBzAEAAWYBmQH/AQABZgHMAgABZgHMATMBAAFmAcwBmQEAAWYCzAEAAWYBzAH/AQABZgH/AgABZgH/ATMBAAFmAf8BmQEAAWYB/wHMAQABzAEAAf8BAAH/AQABzAEAApkCAAGZATMBmQEAAZkBAAGZAQABmQEAAcwBAAGZAwABmQIzAQABmQEAAWYBAAGZATMBzAEAAZkBAAH/AQABmQFmAgABmQFmATMBAAGZATMBZgEAAZkBZgGZAQABmQFmAcwBAAGZATMB/wEAApkBMwEAApkBZgEAA5kBAAKZAcwBAAKZAf8BAAGZAcwCAAGZAcwBMwEAAWYBzAFmAQABmQHMAZkBAAGZAswBAAGZAcwB/wEAAZkB/wIAAZkB/wEzAQABmQHMAWYBAAGZAf8BmQEAAZkB/wHMAQABmQL/AQABzAMAAZkBAAEzAQABzAEAAWYBAAHMAQABmQEAAcwBAAHMAQABmQEzAgABzAIzAQABzAEzAWYBAAHMATMBmQEAAcwBMwHMAQABzAEzAf8BAAHMAWYCAAHMAWYBMwEAAZkCZgEAAcwBZgGZAQABzAFmAcwBAAGZAWYB/wEAAcwBmQIAAcwBmQEzAQABzAGZAWYBAAHMApkBAAHMAZkBzAEAAcwBmQH/AQACzAIAAswBMwEAAswBZgEAAswBmQEAA8wBAALMAf8BAAHMAf8CAAHMAf8BMwEAAZkB/wFmAQABzAH/AZkBAAHMAf8BzAEAAcwC/wEAAcwBAAEzAQAB/wEAAWYBAAH/AQABmQEAAcwBMwIAAf8CMwEAAf8BMwFmAQAB/wEzAZkBAAH/ATMBzAEAAf8BMwH/AQAB/wFmAgAB/wFmATMBAAHMAmYBAAH/AWYBmQEAAf8BZgHMAQABzAFmAf8BAAH/AZkCAAH/AZkBMwEAAf8BmQFmAQAB/wKZAQAB/wGZAcwBAAH/AZkB/wEAAf8BzAIAAf8BzAEzAQAB/wHMAWYBAAH/AcwBmQEAAf8CzAEAAf8BzAH/AQAC/wEzAQABzAH/AWYBAAL/AZkBAAL/AcwBAAJmAf8BAAFmAf8BZgEAAWYC/wEAAf8CZgEAAf8BZgH/AQAC/wFmAQABIQEAAaUBAANfAQADdwEAA4YBAAOWAQADywEAA7IBAAPXAQAD3QEAA+MBAAPqAQAD8QEAA/gBAAHwAfsB/wEAAaQCoAEAA4ADAAH/AgAB/wMAAv8BAAH/AwAB/wEAAf8BAAL/AgAD//8ABQAH9xkAA+8DAAPvFwAH7AkACfcIAAP3AQAD9ygAAfcHAAH3CQAF9ykAAfcHAAH3CgAD9yoAAfcHAAH3CgAD7SoACe0JAALtAewC7SkACewIAALtAewBAAHsAu0oAAnsBwAD7AMAA+z/ABUAAUIBTQE+BwABPgMAASgDAAFAAwABEAMAAQEBAAEBBQABgBcAA/8BAAb/AgAG/wIABv8CAAb/AgAB4AE/Av8B4wGPAgAB4AE/AeABDwHxAR8CAAL/Au8B+AE/AgAC/wLvAfwBfwIAAv8C7wH8AX8CAAL/AeABDwH4AT8CAAL/AeABDwHxAR8CAAL/AeABDwHjAY8CAAb/AgAG/wIABv8CAAb/AgAL";

    [Fact]
    public void Streamer_From_Real_Msft_Hydrates_List_With_Pixels()
    {
        // End-to-end: MSFt bytes -> fork Deserialize (managed store) ->
        // ImageStream assignment -> Images[i] paints via DrawEx through
        // the GetHdc scratch registry.
        byte[] nrbf = Convert.FromBase64String(RealBlobBase64);
        Assert.True(WasmDraw::WfWasm.Drawing.NrbfImage.TryExtractImageListBytes(nrbf, out byte[] msft));
        using var list = new Fork::System.Windows.Forms.ImageList();
        list.ImageStream = new Fork::System.Windows.Forms.ImageListStreamer(msft);
        Assert.Equal(3, list.Images.Count);
        Assert.Equal(new WasmDraw::System.Drawing.Size(16, 16), list.ImageSize);
        using var glyph = (WfBitmap)list.Images[2];
        Assert.Equal(16, glyph.Width);
        Assert.Equal(16, glyph.Height);
        Assert.Equal(0, glyph.GetPixel(0, 0).A);
        Assert.Equal(255, glyph.GetPixel(8, 8).A);
    }

    [Fact]
    public void GetHdc_Mints_Registered_Scratch_Token()
    {
        // The fork module initializer routes Graphics.GetHdc through
        // the bridge registry so GDI paint calls over scratch bitmaps
        // resolve their target. Distinct calls mint distinct tokens.
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        IntPtr hdc1 = g.GetHdc();
        Assert.NotEqual(IntPtr.Zero, hdc1);
        g.ReleaseHdcInternal(hdc1);
        IntPtr hdc2 = g.GetHdc();
        Assert.NotEqual(IntPtr.Zero, hdc2);
        Assert.NotEqual(hdc1, hdc2);
        g.ReleaseHdc(hdc2);
    }
}
