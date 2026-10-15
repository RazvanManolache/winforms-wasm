namespace WfWasm.CanvasHost;

/// <summary>Zoom-aware document composite: which doc-source rect fills which
/// page-dest rect. Paint.NET computes zoom + viewport offset itself (the host
/// used to ignore both and always blit 1:1, so zoom never showed).</summary>
public sealed record ZoomedBlit(
    int SrcX, int SrcY, int SrcW, int SrcH,
    int DestX, int DestY, float DestW, float DestH,
    double Zoom);

/// <summary>Pure document-zoom math (no browser, no reflection): unit-tested
/// in WfWasm.CanvasHost.Tests. At zoom 1 the layout is exactly the legacy
/// centered 1:1 blit.</summary>
public static class DocumentZoom
{
    public const int MarginX = 80;
    public const int MarginY = 60;

    public static ZoomedBlit ComputeZoomedBlit(
        int absX, int absY, int viewW, int viewH,
        int docW, int docH, double zoom, double offX, double offY)
    {
        if (zoom <= 0) zoom = 1;
        if (docW <= 0) docW = 800;
        if (docH <= 0) docH = 600;
        int srcW = Math.Min(docW, (int)((viewW - MarginX) / zoom));
        int srcH = Math.Min(docH, (int)((viewH - MarginY) / zoom));
        if (srcW < 0) srcW = 0;
        if (srcH < 0) srcH = 0;
        int srcX = srcW >= docW ? 0 : (int)Math.Clamp(offX, 0, docW - srcW);
        int srcY = srcH >= docH ? 0 : (int)Math.Clamp(offY, 0, docH - srcH);
        float destW = (float)(srcW * zoom);
        float destH = (float)(srcH * zoom);
        int destX = absX + ((viewW - (int)destW) / 2);
        int destY = absY + ((viewH - (int)destH) / 2);
        return new ZoomedBlit(srcX, srcY, srcW, srcH, destX, destY, destW, destH, zoom);
    }

    /// <summary>Page point back to document pixels (input mapping).</summary>
    public static (double DocX, double DocY) MapToDoc(float px, float py, ZoomedBlit blit)
    {
        double dx = blit.SrcX + (px - blit.DestX) / blit.Zoom;
        double dy = blit.SrcY + (py - blit.DestY) / blit.Zoom;
        return (dx, dy);
    }

    /// <summary>Row-major RGBA sub-rect copy. No bounds check beyond the
    /// caller's contract (x/y/w/h inside imgW/imgH, non-empty).</summary>
    public static byte[] CropRgba(byte[] rgba, int imgW, int imgH, int x, int y, int w, int h)
    {
        var crop = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(rgba, ((y + row) * imgW + x) * 4, crop, row * w * 4, w * 4);
        return crop;
    }
}
