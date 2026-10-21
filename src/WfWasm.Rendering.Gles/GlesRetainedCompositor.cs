namespace WfWasm.Rendering.Gles;

internal sealed class GlesRetainedCompositor : IRetainedCompositor
{
    private readonly GlesRenderDevice _device;

    internal GlesRetainedCompositor(GlesRenderDevice device) => _device = device;

    // Offscreen twin of canvas presentation for verification: recompose the
    // retained scene into a caller-owned stage surface (same stale guards
    // and placement math). No repaint or upload is implied; the caller reads
    // the stage back through explicit CPU access. The framebuffer is
    // created and deleted within the call (bounded, no retention).
    internal ValueTask<SubmissionReceipt> PresentToSurfaceAsync(SceneCommit scene, SurfaceHandle stage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _device.ThrowIfUsable();
        ArgumentNullException.ThrowIfNull(scene);
        ResourceGuards.SameDevice(_device.Identity, scene.Device);
        _device.Context.MakeCurrent();
        // One ordered submission: drain queued drawing first, then compose.
        _device.ExecuteQueueDrained();
        _device.CollectReleases();
        foreach (var layer in scene.Layers)
        {
            var current = _device.Store.Describe(layer.Surface.Handle);
            if (current.ContentVersion != layer.Surface.ContentVersion)
                throw new InvalidOperationException($"Retained layer '{layer.Id}' holds stale content v{layer.Surface.ContentVersion} (current v{current.ContentVersion}). Refresh from Surfaces.Describe and re-place the layer.");
        }
        var target = _device.Store.GetTarget(stage);
        var desc = target.Descriptor;
        int fbo = _device.Context.CreateFramebuffer(target.Texture);
        try
        {
            _device.Context.BindFramebuffer(fbo);
            _device.Context.SetViewport(desc.PixelWidth, desc.PixelHeight);
            _device.Context.SetScissor(null);
            _device.Context.SetSourceOver(true);
            // Same neutral-base rule as canvas presentation: scenes must
            // fully cover the stage for desktop parity.
            _device.Context.Clear(0, 0, 0, 0);
            _device.NoteDraw();
            foreach (var layer in scene.Layers)
            {
                var src = _device.Store.GetTarget(layer.Surface.Handle);
                var quad = GlesCommandTranslator.TransformQuad(
                    new RenderRect(0, 0, src.Descriptor.PixelWidth, src.Descriptor.PixelHeight), layer.Transform);
                _device.Context.SetScissor(GlesCommandTranslator.ToScissor(layer.Clip, desc));
                _device.Context.DrawTextureQuad(quad.P0, quad.P1, quad.P2, quad.P3, src.Texture,
                    new RenderRect(0, 0, src.Descriptor.PixelWidth, src.Descriptor.PixelHeight),
                    ImageSampling.Linear, layer.Opacity);
                _device.Store.Touch(layer.Surface.Handle);
            }
            _device.Context.Flush();
        }
        finally { _device.Context.DeleteFramebuffer(fbo); }
        return ValueTask.FromResult(_device.NextReceipt());
    }

    public ValueTask<SubmissionReceipt> PresentAsync(SceneCommit scene, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _device.ThrowIfUsable();
        ArgumentNullException.ThrowIfNull(scene);
        ResourceGuards.SameDevice(_device.Identity, scene.Device);
        _device.Context.MakeCurrent();
        // One ordered submission: drain queued drawing first, then compose.
        _device.ExecuteQueueDrained();
        _device.CollectReleases();
        foreach (var layer in scene.Layers)
        {
            var current = _device.Store.Describe(layer.Surface.Handle);
            if (current.ContentVersion != layer.Surface.ContentVersion)
                throw new InvalidOperationException($"Retained layer '{layer.Id}' holds stale content v{layer.Surface.ContentVersion} (current v{current.ContentVersion}). Refresh from Surfaces.Describe and re-place the layer.");
        }
        var presentation = _device.Presentation;
        _device.Context.BindFramebuffer(0);
        _device.Context.SetViewport(presentation.PixelWidth, presentation.PixelHeight);
        _device.Context.SetScissor(null);
        _device.Context.SetSourceOver(true);
        // Placement-only recompose must not leave stale pixels: erase to the
        // straight-alpha neutral base first. On the opaque presentation canvas
        // this displays black wherever no layer covers, so scenes must fully
        // cover the canvas (opaque background layer) for desktop parity.
        _device.Context.Clear(0, 0, 0, 0);
        _device.NoteDraw();
        foreach (var layer in scene.Layers)
        {
            var target = _device.Store.GetTarget(layer.Surface.Handle);
            var quad = GlesCommandTranslator.TransformQuad(
                new RenderRect(0, 0, target.Descriptor.PixelWidth, target.Descriptor.PixelHeight), layer.Transform);
            _device.Context.SetScissor(GlesCommandTranslator.ToScissor(layer.Clip, presentation));
            _device.Context.DrawTextureQuad(quad.P0, quad.P1, quad.P2, quad.P3, target.Texture,
                new RenderRect(0, 0, target.Descriptor.PixelWidth, target.Descriptor.PixelHeight),
                ImageSampling.Linear, layer.Opacity);
            _device.Store.Touch(layer.Surface.Handle);
        }
        _device.Context.Flush();
        return ValueTask.FromResult(_device.NextReceipt());
    }
}
