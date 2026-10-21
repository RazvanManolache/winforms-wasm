extern alias Fork;
extern alias WasmDraw;

using System.Linq;
using Fork::Windows.Win32;
using Fork::Windows.Win32.Foundation;
using Fork::Windows.Win32.Graphics.Gdi;
using Fork::Windows.Win32.UI.Controls;
using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfColor = WasmDraw::System.Drawing.Color;
using WfSize = WasmDraw::System.Drawing.Size;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using ForkList = Fork::System.Windows.Forms.ImageList;
using ForkStore = Fork::System.Windows.Forms.WasmImageListStore;

namespace WfWasm.WinForms.Tests;

// Runtime image insertion (Syncfusion Diagram failure): Images.Add used to
// die in CreateHandle with InvalidOperationException (ImageList_Add always
// -1). The shim now resolves the source Bitmap from the GetHbitmap fake
// handle and stores actual pixels; unknown handles keep the honest -1.
public sealed class ImageListManagedInsertionTests
{
    [Fact]
    public void Runtime_Add_Before_Handle_Survives_CreateHandle_With_Pixels()
    {
        using var red = Solid(4, 4, WfColor.FromArgb(255, 200, 30, 30));
        using var semi = Solid(4, 4, WfColor.FromArgb(128, 10, 20, 30));
        using var list = new ForkList();
        list.ImageSize = new WfSize(4, 4);
        list.Images.Add(red);
        list.Images.Add(semi);

        nint handle = list.Handle;
        Assert.Equal(2, list.Images.Count);
        Assert.Equal(2, ForkStore.GetCount(handle));

        Assert.True(ForkStore.TryGetImage(handle, 0, out WfBitmap? got0));
        Assert.Equal(WfColor.FromArgb(255, 200, 30, 30).ToArgb(), got0!.GetPixel(3, 3).ToArgb());
        Assert.True(ForkStore.TryGetImage(handle, 1, out WfBitmap? got1));
        Assert.Equal(WfColor.FromArgb(128, 10, 20, 30).ToArgb(), got1!.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Runtime_Add_After_Handle_Appends_With_Pixels()
    {
        using var red = Solid(4, 4, WfColor.Red);
        using var list = new ForkList();
        list.ImageSize = new WfSize(4, 4);
        list.Images.Add(red);
        nint handle = list.Handle;

        using var blue = Solid(4, 4, WfColor.Blue);
        list.Images.Add(blue);
        Assert.Equal(2, list.Images.Count);
        Assert.True(ForkStore.TryGetImage(handle, 1, out WfBitmap? got));
        Assert.Equal(WfColor.Blue.ToArgb(), got!.GetPixel(1, 2).ToArgb());
    }

    [Fact]
    public void Replace_And_Remove_Update_Slots()
    {
        using var red = Solid(4, 4, WfColor.Red);
        using var green = Solid(4, 4, WfColor.Green);
        using var blue = Solid(4, 4, WfColor.Blue);
        using var list = new ForkList();
        list.ImageSize = new WfSize(4, 4);
        list.Images.Add(red);
        list.Images.Add(blue);
        nint handle = list.Handle;

        list.Images[0] = green;
        Assert.True(ForkStore.TryGetImage(handle, 0, out WfBitmap? got));
        Assert.Equal(WfColor.Green.ToArgb(), got!.GetPixel(0, 0).ToArgb());

        list.Images.RemoveAt(0);
        Assert.Single(list.Images);
        using var enumerated = Assert.Single(list.Images.Cast<WfBitmap>());
        Assert.Equal(WfColor.Blue.ToArgb(), enumerated.GetPixel(0, 0).ToArgb());
        Assert.True(ForkStore.TryGetImage(handle, 0, out WfBitmap? shifted));
        Assert.Equal(WfColor.Blue.ToArgb(), shifted!.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Enumerate_MultiImage_List_Returns_Each_Frame_Pixels()
    {
        // GetBitmap cuts frame i out of the GetImageInfo strip. A local
        // (unrooted) strip dies with GC before FromHbitmap resolves it, and
        // a mis-oriented strip returns a sibling frame's pixels; distinct
        // colors per slot catch both.
        using var red = Solid(4, 4, WfColor.Red);
        using var green = Solid(4, 4, WfColor.Green);
        using var blue = Solid(4, 4, WfColor.Blue);
        using var list = new ForkList();
        list.ImageSize = new WfSize(4, 4);
        list.Images.Add(red);
        list.Images.Add(green);
        list.Images.Add(blue);

        int[] expected = [WfColor.Red.ToArgb(), WfColor.Green.ToArgb(), WfColor.Blue.ToArgb()];
        int seen = 0;
        foreach (WfBitmap frame in list.Images.Cast<WfBitmap>())
        {
            using (frame)
                Assert.Equal(expected[seen], frame.GetPixel(1, 2).ToArgb());
            seen++;
        }
        Assert.Equal(3, seen);
    }

    [Fact]
    public void Unknown_Handles_Keep_Honest_Failures()
    {
        using var red = Solid(4, 4, WfColor.Red);
        using var list = new ForkList();
        list.ImageSize = new WfSize(4, 4);
        list.Images.Add(red);
        nint handle = list.Handle;

        // Zero/foreign HBITMAPs resolve to nothing: Add keeps -1.
        Assert.Equal(-1, PInvoke.ImageList_Add((HIMAGELIST)handle, (HBITMAP)0, (HBITMAP)0));
        Assert.Equal(-1, PInvoke.ImageList_Add((HIMAGELIST)0x7FFF, (HBITMAP)0, (HBITMAP)0));
        Assert.Equal(1, ForkStore.GetCount(handle));

        // Out-of-range replace/remove fail instead of corrupting.
        Assert.False(PInvoke.ImageList_Replace((HIMAGELIST)handle, 5, (HBITMAP)0, (HBITMAP)0));
        Assert.False(PInvoke.ImageList_Remove((HIMAGELIST)0x7FFF, 0));
    }

    [Fact]
    public void DrawEx_Paints_Runtime_Added_Slot()
    {
        using var red = Solid(4, 4, WfColor.FromArgb(255, 200, 30, 30));
        using var list = new ForkList();
        list.ImageSize = new WfSize(4, 4);
        list.Images.Add(red);
        nint handle = list.Handle;

        using var canvas = new WfBitmap(8, 4);
        using var g = WfGraphics.FromImage(canvas);
        using var _ = PInvoke.WasmFillBridge.TrackTarget(default, g);

        Assert.True(PInvoke.ImageList_DrawEx((HIMAGELIST)handle, 0, default, 2, 0, 4, 4, default, default, default));
        Assert.Equal(WfColor.FromArgb(255, 200, 30, 30).ToArgb(), canvas.GetPixel(2, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 200, 30, 30).ToArgb(), canvas.GetPixel(5, 3).ToArgb());
    }

    private static WfBitmap Solid(int w, int h, WfColor color)
    {
        var bmp = new WfBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bmp.SetPixel(x, y, color);
        return bmp;
    }
}
