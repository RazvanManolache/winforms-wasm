using WfWasm.Shim;

namespace WfWasm.Renderer;

/// <summary>Minimal canvas abstraction for stage 3. Backends (Skia/Canvas) come later.</summary>
public interface ICanvasRenderer : ICanvas
{
    void BeginFrame(int width, int height);
    void EndFrame();

    /// <summary>Retained bitmap layers. Backends without layer storage
    /// keep the default (false) and callers fall back to DrawImage.</summary>
    bool SupportsBitmapLayers => false;

    void DefBitmap(string layerId, byte[] rgba, int width, int height) =>
        throw new NotSupportedException("Bitmap layers unsupported.");

    void BlitBitmap(string layerId, float x, float y, float dw, float dh) =>
        throw new NotSupportedException("Bitmap layers unsupported.");

    void PatchBitmap(string layerId, int x, int y, int width, int height, byte[] rgba) =>
        throw new NotSupportedException("Bitmap layers unsupported.");

    void RetainLayers(IReadOnlyList<string> liveIds) { }
}
