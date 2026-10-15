using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// Native-vs-WASM geometry parity for the zoom composite: instead of
// re-encoding the formula, every blit must satisfy the desktop layout
// contract (round-trip mapping, scale identity, containment, centering)
// across a sweep of zooms, viewports, documents and viewport offsets.
public sealed class DocumentZoomParityTests
{
    [Fact]
    public void ZoomedBlit_Satisfies_Desktop_Contract_For_All_Combos()
    {
        double[] zooms = [0.25, 0.5, 1.0, 1.5, 2.0, 4.0];
        (int W, int H)[] views = [(800, 600), (1500, 950), (438, 315)];
        (int W, int H)[] docs = [(800, 600), (100, 100), (5000, 4000)];
        (double X, double Y)[] offsets = [(0, 0), (1000, 1000), (-50, -50)];

        foreach (double zoom in zooms)
        {
            foreach (var view in views)
            {
                foreach (var doc in docs)
                {
                    foreach (var off in offsets)
                    {
                        var blit = DocumentZoom.ComputeZoomedBlit(
                            0, 0, view.W, view.H, doc.W, doc.H, zoom, off.X, off.Y);

                        // Scale identity: dest is the source scaled by zoom.
                        Assert.Equal((float)(blit.SrcW * zoom), blit.DestW, precision: 3);
                        Assert.Equal((float)(blit.SrcH * zoom), blit.DestH, precision: 3);

                        // Round-trip: dest corners map back to source corners.
                        var (ox, oy) = DocumentZoom.MapToDoc(blit.DestX, blit.DestY, blit);
                        Assert.Equal(blit.SrcX, ox, precision: 6);
                        Assert.Equal(blit.SrcY, oy, precision: 6);
                        var (ex, ey) = DocumentZoom.MapToDoc(
                            blit.DestX + blit.DestW, blit.DestY + blit.DestH, blit);
                        Assert.Equal(blit.SrcX + blit.SrcW, ex, precision: 3);
                        Assert.Equal(blit.SrcY + blit.SrcH, ey, precision: 3);

                        // Containment: the source rect stays inside the doc.
                        Assert.InRange(blit.SrcX, 0, Math.Max(0, doc.W - 1));
                        Assert.InRange(blit.SrcY, 0, Math.Max(0, doc.H - 1));
                        Assert.True(blit.SrcX + blit.SrcW <= doc.W);
                        Assert.True(blit.SrcY + blit.SrcH <= doc.H);

                        // Centering: dest sits centered in the view.
                        Assert.Equal((view.W - (int)blit.DestW) / 2, blit.DestX);
                        Assert.Equal((view.H - (int)blit.DestH) / 2, blit.DestY);
                    }
                }
            }
        }
    }
}
