using System.Numerics;
using WfWasm.Rendering;
using WfWasm.Rendering.Gles;

namespace WfWasm.Rendering.Gles.Tests;

// TEST DOUBLE ONLY. This fake logs the driver's protocol (resource lifetime,
// command order, transfer sizes) and simulates Clear fills plus upload/readback
// memory so lease/commit/version behavior is testable. It does NOT rasterize
// Fill/Blit commands and proves nothing about pixels, GPU execution, or
// performance. GPU acceptance still requires a real browser backend and the
// 22 browser contracts. Strict on purpose: unknown/double deletes throw so
// device lifetime bugs surface here.
public sealed class FakeGlesContextFactory : IGlesContextFactory
{
    public bool Supported { get; set; } = true;
    public string Detail { get; set; } = "fake GLES driver (protocol simulation, not pixels)";
    public int SupportQueries { get; private set; }
    public List<FakeGlesContext> Created { get; } = new();

    public bool IsSupported
    {
        get { SupportQueries++; return Supported; }
    }

    public string SupportDetail => Detail;

    public IGlesContext Create(string presentationTargetId, SurfaceDescriptor presentation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationTargetId);
        ArgumentNullException.ThrowIfNull(presentation);
        if (!Supported) throw new NotSupportedException("Fake driver disabled: " + Detail);
        var context = new FakeGlesContext(presentationTargetId, presentation);
        Created.Add(context);
        return context;
    }
}

public sealed class FakeGlesContext : IGlesContext
{
    public sealed record Call(string Kind, string Detail);

    private sealed class Texture
    {
        public int Width;
        public int Height;
        public byte[] Pixels = [];
        public bool Deleted;
    }

    private readonly Dictionary<int, Texture> _textures = new();
    private readonly Dictionary<int, int> _fbos = new();
    private int _nextTexture = 1;
    private int _nextFbo = 1;
    private int _boundFbo;
    private bool _disposed;

    public List<Call> Log { get; } = new();
    public string PresentationTargetId { get; }
    public SurfaceDescriptor Presentation { get; }
    public bool IsLost { get; private set; }
    public bool IsDisposed => _disposed;
    public byte[]? LastUploadBytes { get; private set; }

    public event Action? ContextLost;

    public FakeGlesContext(string presentationTargetId, SurfaceDescriptor presentation)
    {
        PresentationTargetId = presentationTargetId;
        Presentation = presentation;
        _textures[0] = new Texture
        {
            Width = presentation.PixelWidth,
            Height = presentation.PixelHeight,
            Pixels = new byte[checked(presentation.PixelWidth * presentation.PixelHeight * 4)]
        };
        _fbos[0] = 0;
    }

    public int LiveTextureCount => _textures.Values.Count(t => !t.Deleted) - 1; // Excludes presentation.
    public int LiveFboCount => _fbos.Count - 1; // Excludes presentation framebuffer 0.
    public List<Call> CallsOf(string kind) => Log.Where(c => c.Kind == kind).ToList();
    public int LastIndexOf(string kind) => Log.FindLastIndex(c => c.Kind == kind);

    public void SimulateContextLoss()
    {
        if (IsLost || _disposed) return;
        IsLost = true;
        Log.Add(new Call("ContextLost", ""));
        ContextLost?.Invoke();
    }

    private void RequireLive()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FakeGlesContext));
        if (IsLost) throw new InvalidOperationException("Fake context is lost.");
    }

    private Texture RequireTexture(int texture)
    {
        if (texture == 0 || !_textures.TryGetValue(texture, out var found) || found.Deleted)
            throw new InvalidOperationException($"Fake: unknown or deleted texture {texture}.");
        return found;
    }

    // Destination-side check: texture 0 is the presentation canvas
    // backing (framebuffer 0), valid as a draw target but never as a
    // source. Sources keep the strict RequireTexture above.
    private Texture RequireTarget(int texture)
    {
        if (!_textures.TryGetValue(texture, out var found) || found.Deleted)
            throw new InvalidOperationException($"Fake: unknown or deleted target {texture}.");
        return found;
    }

    public void MakeCurrent()
    {
        RequireLive();
        Log.Add(new Call("MakeCurrent", ""));
    }

    public int CreateTexture(int pixelWidth, int pixelHeight)
    {
        RequireLive();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        int id = _nextTexture++;
        _textures[id] = new Texture
        {
            Width = pixelWidth,
            Height = pixelHeight,
            Pixels = new byte[checked(pixelWidth * pixelHeight * 4)]
        };
        Log.Add(new Call("CreateTexture", $"{id} {pixelWidth}x{pixelHeight}"));
        return id;
    }

    public void DeleteTexture(int texture)
    {
        RequireLive();
        var found = RequireTexture(texture);
        found.Deleted = true;
        Log.Add(new Call("DeleteTexture", FormattableString.Invariant($"{texture}")));
    }

    public int CreateFramebuffer(int texture)
    {
        RequireLive();
        RequireTexture(texture);
        int id = _nextFbo++;
        _fbos[id] = texture;
        Log.Add(new Call("CreateFramebuffer", FormattableString.Invariant($"{id} tex={texture}")));
        return id;
    }

    public void DeleteFramebuffer(int framebuffer)
    {
        RequireLive();
        if (framebuffer == 0 || !_fbos.Remove(framebuffer))
            throw new InvalidOperationException($"Fake: unknown framebuffer {framebuffer}.");
        Log.Add(new Call("DeleteFramebuffer", FormattableString.Invariant($"{framebuffer}")));
    }

    public void BindFramebuffer(int framebuffer)
    {
        RequireLive();
        if (!_fbos.ContainsKey(framebuffer))
            throw new InvalidOperationException($"Fake: unknown framebuffer {framebuffer}.");
        _boundFbo = framebuffer;
        Log.Add(new Call("Bind", FormattableString.Invariant($"{framebuffer}")));
    }

    public void SetViewport(int pixelWidth, int pixelHeight)
    {
        RequireLive();
        Log.Add(new Call("Viewport", FormattableString.Invariant($"{pixelWidth}x{pixelHeight}")));
    }

    public void SetScissor(PixelRect? scissor)
    {
        RequireLive();
        Log.Add(new Call("Scissor", scissor?.ToString() ?? "null"));
    }

    public void SetSourceOver(bool sourceOver)
    {
        RequireLive();
        Log.Add(new Call("Blend", sourceOver ? "over" : "copy"));
    }

    public void Clear(float r, float g, float b, float a)
    {
        RequireLive();
        var target = _textures[_fbos[_boundFbo]];
        byte qr = (byte)Math.Clamp((int)MathF.Round(r * 255), 0, 255);
        byte qg = (byte)Math.Clamp((int)MathF.Round(g * 255), 0, 255);
        byte qb = (byte)Math.Clamp((int)MathF.Round(b * 255), 0, 255);
        byte qa = (byte)Math.Clamp((int)MathF.Round(a * 255), 0, 255);
        for (int i = 0; i < target.Pixels.Length; i += 4)
        {
            target.Pixels[i] = qr;
            target.Pixels[i + 1] = qg;
            target.Pixels[i + 2] = qb;
            target.Pixels[i + 3] = qa;
        }
        Log.Add(new Call("Clear", FormattableString.Invariant($"{r:0.###},{g:0.###},{b:0.###},{a:0.###}")));
    }

    public void FillQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, uint straightSrgbRgba)
    {
        RequireLive();
        RequireTarget(_fbos[_boundFbo]);
        Log.Add(new Call("Fill", FormattableString.Invariant($"{Point(p0)} {Point(p1)} {Point(p2)} {Point(p3)} color={straightSrgbRgba:X8}")));
    }

    public void DrawTextureQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int sourceTexture,
        RenderRect sourcePixels, ImageSampling sampling, float opacity)
    {
        RequireLive();
        RequireTexture(sourceTexture);
        RequireTarget(_fbos[_boundFbo]);
        Log.Add(new Call("Blit", FormattableString.Invariant(
            $"{Point(p0)} {Point(p1)} {Point(p2)} {Point(p3)} tex={sourceTexture} src={sourcePixels} {sampling} opacity={opacity:0.###}")));
    }

    public void UploadSubImage(int texture, PixelRect region, ReadOnlySpan<byte> straightRgba, int rowStride)
    {
        RequireLive();
        var target = RequireTexture(texture);
        RequireRegion(target, region);
        if (rowStride < region.Width * 4 || straightRgba.Length < rowStride * region.Height)
            throw new ArgumentException("Fake: upload payload does not cover the region.");
        LastUploadBytes = straightRgba[..(rowStride * region.Height)].ToArray();
        for (int y = 0; y < region.Height; y++)
        {
            var source = straightRgba.Slice(y * rowStride, region.Width * 4);
            source.CopyTo(target.Pixels.AsSpan(((region.Y + y) * target.Width + region.X) * 4));
        }
        Log.Add(new Call("Upload", FormattableString.Invariant($"{texture} {region} stride={rowStride}")));
    }

    public void ReadPixels(int framebuffer, PixelRect region, Span<byte> destination, int rowStride)
    {
        RequireLive();
        if (!_fbos.TryGetValue(framebuffer, out int texture))
            throw new InvalidOperationException($"Fake: unknown framebuffer {framebuffer}.");
        var target = _textures[texture];
        RequireRegion(target, region);
        if (rowStride < region.Width * 4 || destination.Length < rowStride * region.Height)
            throw new ArgumentException("Fake: readback destination does not cover the region.");
        for (int y = 0; y < region.Height; y++)
        {
            var source = target.Pixels.AsSpan(((region.Y + y) * target.Width + region.X) * 4, region.Width * 4);
            source.CopyTo(destination.Slice(y * rowStride));
        }
        Log.Add(new Call("ReadPixels", FormattableString.Invariant($"{framebuffer} {region}")));
    }

    public void Flush()
    {
        RequireLive();
        Log.Add(new Call("Flush", ""));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Log.Add(new Call("Dispose", ""));
    }

    private static void RequireRegion(Texture target, PixelRect region)
    {
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
            region.X > target.Width - region.Width || region.Y > target.Height - region.Height)
            throw new ArgumentOutOfRangeException(nameof(region), "Fake: region is outside the texture.");
    }

    private static string Point(Vector2 point) =>
        FormattableString.Invariant($"({point.X:0.###},{point.Y:0.###})");
}
