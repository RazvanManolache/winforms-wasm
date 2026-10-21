using System.Numerics;

namespace WfWasm.Rendering.Gles;

// Proposed driver seam between the managed GLES device and the real GPU driver.
// Owned by WfWasm.Rendering.Gles; the shared WfWasm.Rendering contracts are unchanged.
//
// Coordinate contract (binding requirements for a real WebGL2 implementation):
// - All positions and rects use device pixels with a top-left origin. The real
//   binding translates to GL coordinates (Y-flip, NDC projection) internally.
// - All pixel transfers use straight sRGB RGBA bytes with row 0 = top row.
// - Region origins are top-left device pixels; the binding maps them against
//   the addressed target's own height (yGL = height - (y + h)).
// - The binding may store premultiplied RGBA internally (fixed-function GL
//   blending cannot produce straight RGB on transparent targets), but only
//   with explicit boundary conversions: uploads/fills/clears premultiply on
//   the way in, readbacks un-premultiply on the way out, and textured draws
//   scale premultiplied texels by opacity. Changing one blend factor alone
//   is not a representation fix.
// - Transforms supplied by the device already map surface-local logical units
//   to device pixels (DPI applied exactly once upstream). The binding must not
//   apply DpiScale again.
// - Identity textured quads must sample texel centers exactly (no filtering
//   shift); Linear sampling is used for transformed composition.
public interface IGlesContextFactory
{
    // Pure capability query. Must not create a context or bind any canvas.
    bool IsSupported { get; }
    string SupportDetail { get; }
    IGlesContext Create(string presentationTargetId, SurfaceDescriptor presentation);
}

public interface IGlesContext : IDisposable
{
    // Raised once when the driver reports context loss. Recovery is explicit
    // re-creation through the device factory, never automatic.
    event Action? ContextLost;
    bool IsLost { get; }

    void MakeCurrent();

    int CreateTexture(int pixelWidth, int pixelHeight);
    void DeleteTexture(int texture);
    int CreateFramebuffer(int texture);
    void DeleteFramebuffer(int framebuffer);

    // Framebuffer 0 is the presentation target with the device's presentation size.
    void BindFramebuffer(int framebuffer);
    void SetViewport(int pixelWidth, int pixelHeight);
    void SetScissor(PixelRect? scissor);
    void SetSourceOver(bool sourceOver);

    void Clear(float r, float g, float b, float a);
    void FillQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, uint straightSrgbRgba);
    void DrawTextureQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int sourceTexture,
        RenderRect sourcePixels, ImageSampling sampling, float opacity);

    void UploadSubImage(int texture, PixelRect region, ReadOnlySpan<byte> straightRgba, int rowStride);
    void ReadPixels(int framebuffer, PixelRect region, Span<byte> destination, int rowStride);
    void Flush();
}
