using System.Numerics;
using WfWasm.AppIntegration;
using WfWasm.Rendering;

namespace WfWasm.AppIntegration.Tests;

// Independent protocol/ownership tests only: no hardware pixels or GPU performance claims.
public sealed class ControlLayerBridgeTests
{
    private static SurfaceDescriptor Target => new(80, 60, 1,
        new(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb));

    [Fact]
    public void Disconnected_adapter_fails_before_allocation_or_paint()
    {
        var device = new RecordingDevice();
        Assert.Throws<NotSupportedException>(() => new ControlLayerBridge(device, new(device.Identity),
            new DisconnectedControlPaintAdapter("control"), Target));
        Assert.Equal(0, device.Allocations);
    }

    [Fact]
    public void Unsupported_requirements_fail_before_the_application_callback()
    {
        var device = new RecordingDevice { Features = DrawingFeatures.RetainedComposition };
        var adapter = new Adapter { RequiredFeatures = DrawingFeatures.Gradients };
        Assert.Throws<NotSupportedException>(() => new ControlLayerBridge(device, new(device.Identity), adapter, Target));
        Assert.Equal(0, adapter.Calls);
        Assert.Equal(0, device.Allocations);
    }

    [Fact]
    public async Task Movement_reuses_content_and_unchanged_frames_do_no_submission()
    {
        var device = new RecordingDevice(); var scene = new RetainedScene(device.Identity); var adapter = new Adapter();
        using var layer = new ControlLayerBridge(device, scene, adapter, Target);
        var presenter = new RetainedFramePresenter(device, scene);
        Assert.True(layer.PaintIfDirty());
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        var before = Assert.Single(scene.Capture().Layers).Surface;
        Assert.False(layer.PaintIfDirty());
        Assert.Null(await presenter.PresentIfChangedAsync());
        layer.SetPlacement(Matrix3x2.CreateTranslation(21, 13), new(0, 0, 200, 100), 1, 2);
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        var after = Assert.Single(scene.Capture().Layers);
        Assert.Equal(before, after.Surface);
        Assert.Equal(21, after.Transform.M31);
        Assert.Equal(1, adapter.Calls);
        Assert.Equal(1, device.Batches);
        Assert.Equal(2, device.Presentations);
    }

    [Fact]
    public void Content_change_invokes_once_and_refreshes_retained_version()
    {
        var device = new RecordingDevice(); var scene = new RetainedScene(device.Identity); var adapter = new Adapter();
        using var layer = new ControlLayerBridge(device, scene, adapter, Target);
        layer.PaintIfDirty(); var initial = Assert.Single(scene.Capture().Layers).Surface;
        layer.InvalidateContent(); layer.PaintIfDirty();
        var changed = Assert.Single(scene.Capture().Layers).Surface;
        Assert.Equal(initial.Handle, changed.Handle);
        Assert.Equal(initial.ContentVersion + 1, changed.ContentVersion);
        Assert.Equal(2, adapter.Calls);
        Assert.IsType<ClearSurface>(device.LastBatch!.Commands[0]);
    }

    [Fact]
    public void Failed_callback_is_not_replayed_and_stale_layer_is_removed()
    {
        var device = new RecordingDevice(); var scene = new RetainedScene(device.Identity); var adapter = new Adapter();
        using var layer = new ControlLayerBridge(device, scene, adapter, Target);
        layer.PaintIfDirty();
        adapter.Fail = true; layer.InvalidateContent();
        Assert.Throws<NotSupportedException>(() => layer.PaintIfDirty());
        Assert.Equal(ControlLayerState.Faulted, layer.State);
        Assert.Empty(scene.Capture().Layers);
        Assert.Throws<InvalidOperationException>(() => layer.PaintIfDirty());
        Assert.Equal(2, adapter.Calls); Assert.Equal(1, device.Batches);
    }

    [Fact]
    public void Invalid_surface_and_foreign_scene_are_rejected_before_paint()
    {
        var device = new RecordingDevice(); var adapter = new Adapter();
        Assert.Throws<InvalidOperationException>(() => new ControlLayerBridge(device,
            new(new DeviceIdentity(Guid.NewGuid(), 1)), adapter, Target));
        using var layer = new ControlLayerBridge(device, new(device.Identity), adapter, Target);
        device.Surfaces.Release(layer.Surface);
        Assert.Throws<InvalidOperationException>(() => layer.PaintIfDirty());
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public void Disposal_releases_only_this_layer_once()
    {
        var device = new RecordingDevice(); var scene = new RetainedScene(device.Identity);
        var layer = new ControlLayerBridge(device, scene, new Adapter(), Target); layer.PaintIfDirty();
        layer.Dispose(); layer.Dispose();
        Assert.Empty(scene.Capture().Layers); Assert.Equal(1, device.Releases);
        Assert.Equal(RenderDeviceState.Ready, device.State);
        Assert.Throws<ObjectDisposedException>(() => layer.PaintIfDirty());
    }

    [Fact]
    public void Delayed_old_disposal_does_not_remove_replacement_control_content()
    {
        var device = new RecordingDevice(); var scene = new RetainedScene(device.Identity);
        var old = new ControlLayerBridge(device, scene, new Adapter(), Target); old.PaintIfDirty();
        scene.Remove("real-control-adapter-seam");
        using var replacement = new ControlLayerBridge(device, scene, new Adapter(), Target); replacement.PaintIfDirty();
        old.Dispose();
        Assert.Equal(replacement.Surface, Assert.Single(scene.Capture().Layers).Surface.Handle);
        Assert.True(device.Surfaces.IsValid(replacement.Surface));
    }

    private sealed class Adapter : IControlPaintAdapter
    {
        public string ControlId => "real-control-adapter-seam";
        public bool IsConnected => true;
        public DrawingFeatures RequiredFeatures { get; init; }
        public int Calls; public bool Fail;
        public void PaintOnce(DrawingCommandRecorder recorder, SurfaceDescriptor target)
        {
            Calls++;
            Assert.Equal(80, target.PixelWidth); Assert.Equal(60, target.PixelHeight);
            if (Fail) throw new NotSupportedException("Independent unsupported paint witness");
            recorder.Record(new ClearSurface(new(0xff0000ff)));
        }
    }

    private sealed class RecordingDevice : IRenderDevice, ISurfaceStore, IRetainedCompositor
    {
        private readonly Dictionary<SurfaceHandle, SurfaceSnapshot> _surfaces = [];
        public DeviceIdentity Identity { get; } = new(Guid.NewGuid(), 1);
        public BackendKind Kind => BackendKind.CanvasKitWebGl2;
        public DrawingFeatures Features { get; init; } = DrawingFeatures.RetainedComposition;
        public RenderDeviceState State { get; private set; } = RenderDeviceState.Ready;
        public ISurfaceStore Surfaces => this;
        public IRetainedCompositor Compositor => this;
        public IImageResources Images => throw new NotSupportedException();
        public ICpuSurfaceAccess CpuAccess => throw new NotSupportedException();
        public ITextService Text => throw new NotSupportedException();
        public event Action<DeviceIdentity>? GenerationChanged { add { } remove { } }
        public event Action<SurfaceHandle>? Invalidated;
        public int Allocations, Batches, Presentations, Releases;
        public DrawingBatch? LastBatch;
        public long ResidentBytes => _surfaces.Values.Sum(s => s.Descriptor.MinimumPixelBytes);
        public SurfaceSnapshot Create(SurfaceDescriptor descriptor)
        {
            var s = new SurfaceSnapshot(new(Identity, ++Allocations, 1), descriptor, 0, PixelAuthority.Device);
            _surfaces.Add(s.Handle, s); return s;
        }
        public SurfaceSnapshot Describe(SurfaceHandle handle) => _surfaces[handle];
        public bool IsValid(SurfaceHandle handle) => State == RenderDeviceState.Ready && _surfaces.ContainsKey(handle);
        public SurfaceSnapshot Resize(SurfaceHandle handle, SurfaceDescriptor descriptor) => throw new NotSupportedException();
        public void Release(SurfaceHandle handle) { if (_surfaces.Remove(handle)) { Releases++; Invalidated?.Invoke(handle); } }
        public IReadOnlyList<SurfaceHandle> TrimToBudget(ResourceBudget budget) => [];
        public void Enqueue(DrawingBatch batch)
        {
            var s = Describe(batch.Target); Assert.Equal(s.ContentVersion, batch.ExpectedContentVersion);
            _surfaces[batch.Target] = s with { ContentVersion = s.ContentVersion + 1 }; Batches++; LastBatch = batch;
        }
        public ValueTask<SubmissionReceipt> SubmitAsync(CancellationToken cancellationToken = default) => new(new SubmissionReceipt(Batches));
        public ValueTask<SubmissionReceipt> PresentAsync(SceneCommit scene, CancellationToken cancellationToken = default)
        {
            foreach (var layer in scene.Layers) Assert.Equal(Describe(layer.Surface.Handle).ContentVersion, layer.Surface.ContentVersion);
            return new(new SubmissionReceipt(++Presentations));
        }
        public ValueTask DrainDisposalsAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { State = RenderDeviceState.Disposed; return ValueTask.CompletedTask; }
    }
}
