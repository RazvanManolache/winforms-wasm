using WfWasm.CanvasHost;

public sealed class DocumentZoomTests
{
    [Fact]
    public void Zoom1_Matches_Legacy_Centered_Blit()
    {
        // Legacy layout this replaces (WinFormsAppHost document branch).
        int absX = 222, absY = 74, w = 1200, h = 800, docW = 800, docH = 600;
        int pageW = Math.Min(docW, w - 80);
        int pageH = Math.Min(docH, h - 60);
        int pageX = absX + ((w - pageW) / 2);
        int pageY = absY + ((h - pageH) / 2);

        var blit = DocumentZoom.ComputeZoomedBlit(absX, absY, w, h, docW, docH, 1.0, 0, 0);

        Assert.Equal((0, 0, pageW, pageH), (blit.SrcX, blit.SrcY, blit.SrcW, blit.SrcH));
        Assert.Equal((pageX, pageY), (blit.DestX, blit.DestY));
        Assert.Equal(pageW, blit.DestW);
        Assert.Equal(pageH, blit.DestH);
    }

    [Fact]
    public void ZoomedOut_Small_Doc_Stays_Full_Source_Scaled_Dest()
    {
        var blit = DocumentZoom.ComputeZoomedBlit(0, 0, 1000, 800, 400, 300, 2.0, 0, 0);

        Assert.Equal((0, 0, 400, 300), (blit.SrcX, blit.SrcY, blit.SrcW, blit.SrcH));
        Assert.Equal(800f, blit.DestW);
        Assert.Equal(600f, blit.DestH);
        Assert.Equal((100, 100), (blit.DestX, blit.DestY));
    }

    [Fact]
    public void ZoomedIn_Large_Doc_Crops_To_Viewport()
    {
        var blit = DocumentZoom.ComputeZoomedBlit(0, 0, 1000, 800, 800, 600, 2.0, 100, 50);

        Assert.Equal(460, blit.SrcW); // (1000-80)/2
        Assert.Equal(370, blit.SrcH); // (800-60)/2
        Assert.Equal((100, 50), (blit.SrcX, blit.SrcY));
        Assert.Equal(920f, blit.DestW);
    }

    [Fact]
    public void Offset_Clamps_Into_Document()
    {
        var lo = DocumentZoom.ComputeZoomedBlit(0, 0, 1000, 800, 800, 600, 2.0, -40, -40);
        Assert.Equal((0, 0), (lo.SrcX, lo.SrcY));
        var hi = DocumentZoom.ComputeZoomedBlit(0, 0, 1000, 800, 800, 600, 2.0, 9999, 9999);
        Assert.Equal((800 - hi.SrcW, 600 - hi.SrcH), (hi.SrcX, hi.SrcY));
    }

    [Fact]
    public void SmallWindow_Zoom1_Crops_One_To_One_Like_Desktop()
    {
        // Live-observed 800x600 session: panel 438x209, doc 800x600.
        // Desktop shows scrollbars + the 1:1 corner, never a shrink.
        var blit = DocumentZoom.ComputeZoomedBlit(10, 121, 438, 209, 800, 600, 1.0, 0, 0);

        Assert.Equal((0, 0, 358, 149), (blit.SrcX, blit.SrcY, blit.SrcW, blit.SrcH));
        Assert.Equal((50, 151), (blit.DestX, blit.DestY));
        Assert.Equal(358f, blit.DestW);
    }

    [Fact]
    public void NonPositive_Zoom_Falls_Back_To_One()
    {
        var blit = DocumentZoom.ComputeZoomedBlit(0, 0, 1000, 800, 800, 600, 0, 0, 0);
        Assert.Equal(1.0, blit.Zoom);
        Assert.Equal((0, 0, 800, 600), (blit.SrcX, blit.SrcY, blit.SrcW, blit.SrcH));
    }

    [Fact]
    public void MapToDoc_Round_Trips_Through_Blit()
    {
        var blit = DocumentZoom.ComputeZoomedBlit(100, 50, 1000, 800, 800, 600, 1.5, 60, 40);
        // Dest origin maps to source origin.
        var (x0, y0) = DocumentZoom.MapToDoc(blit.DestX, blit.DestY, blit);
        Assert.Equal(60.0, x0, precision: 9);
        Assert.Equal(40.0, y0, precision: 9);
        // One zoomed pixel over maps one doc pixel over.
        var (x1, _) = DocumentZoom.MapToDoc(blit.DestX + 3f, blit.DestY, blit);
        Assert.Equal(62.0, x1, precision: 9);
    }

    [Fact]
    public void CropRgba_Copies_Sub_Rect()
    {
        // 4x3 image, pixel value = index.
        byte[] rgba = new byte[4 * 3 * 4];
        for (int i = 0; i < 12; i++)
            rgba[i * 4] = (byte)i;
        byte[] crop = DocumentZoom.CropRgba(rgba, 4, 3, 1, 1, 2, 2);
        // Rows 1..2, cols 1..2 -> indices 5,6,9,10.
        Assert.Equal(new byte[] { 5, 0, 0, 0, 6, 0, 0, 0, 9, 0, 0, 0, 10, 0, 0, 0 }, crop);
    }
}
