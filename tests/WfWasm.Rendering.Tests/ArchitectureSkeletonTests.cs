using System.Numerics;
using WfWasm.Rendering;

namespace WfWasm.Rendering.Tests;

// These validate contracts/metadata only, never GPU rendering or performance.
public sealed class ArchitectureSkeletonTests
{
    private static readonly PixelLayout Layout = new(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb);
    private static SurfaceDescriptor Description => new(128, 96, 1, Layout);
    private static RenderDeviceOptions Options => new("wfcanvas", Description, new(64 * 1024 * 1024));
    private static SurfaceSnapshot Surface(DeviceIdentity device, long id = 1) =>
        new(new(device, id, 1), Description, 4, PixelAuthority.Device);

    [Fact]
    public async Task Skeleton_cannot_claim_GPU_availability_or_create_a_device()
    {
        var factory = new CanvasKitWebGlFactorySkeleton();
        var probe = await factory.ProbeAsync();
        Assert.Equal(BackendAvailability.NotImplemented, probe.Availability);
        Assert.Equal(DrawingFeatures.None, probe.Features);
        await Assert.ThrowsAsync<NotSupportedException>(() => factory.CreateAsync(Options).AsTask());
    }
    [Fact]
    public async Task Required_GPU_fails_before_constructing_the_software_adapter()
    {
        var software = new FakeFactory(BackendKind.Software);
        await Assert.ThrowsAsync<NotSupportedException>(() => RenderSessionFactory.CreateAsync(
            RenderPolicy.RequireGpu, Options, new CanvasKitWebGlFactorySkeleton(), software).AsTask());
        Assert.Equal(0, software.Creates); Assert.Equal(0, software.Probes);
    }
    [Fact]
    public async Task Prefer_GPU_can_select_an_explicit_software_adapter_before_any_paint()
    {
        var software = new FakeFactory(BackendKind.Software);
        await using var session = await RenderSessionFactory.CreateAsync(RenderPolicy.PreferGpu, Options,
            new CanvasKitWebGlFactorySkeleton(), software);
        Assert.Equal(BackendKind.Software, session.Device.Kind);
        Assert.Contains("pre-paint fallback", session.SelectionReason);
    }
    [Fact]
    public async Task CPU_mode_does_not_probe_the_GPU_or_initialize_its_canvas()
    {
        var gpu = new FakeFactory(BackendKind.CanvasKitWebGl2);
        await using var session = await RenderSessionFactory.CreateAsync(RenderPolicy.CpuCompatibility, Options,
            gpu, new FakeFactory(BackendKind.Software));
        Assert.Equal(0, gpu.Probes); Assert.Equal(0, gpu.Creates);
    }
    [Fact]
    public async Task Startup_failure_does_not_silently_try_software()
    {
        var gpu = new FakeFactory(BackendKind.CanvasKitWebGl2) { FailCreation = true };
        var software = new FakeFactory(BackendKind.Software);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderSessionFactory.CreateAsync(RenderPolicy.PreferGpu,
            Options, gpu, software).AsTask());
        Assert.Equal(0, software.Creates);
    }
    [Fact]
    public async Task A_factory_cannot_substitute_software_after_a_GPU_probe()
    {
        var gpu = new FakeFactory(BackendKind.CanvasKitWebGl2) { ActualKind = BackendKind.Software };
        await Assert.ThrowsAsync<NotSupportedException>(() => RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu,
            Options, gpu, null).AsTask());
        Assert.True(gpu.Created!.Disposed);
    }
    [Fact]
    public async Task Partial_GPU_features_do_not_qualify_as_whole_UI_drawing()
    {
        var gpu = new FakeFactory(BackendKind.CanvasKitWebGl2) { Features = DrawingFeatures.RetainedComposition };
        await Assert.ThrowsAsync<NotSupportedException>(() => RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu,
            Options, gpu, null).AsTask());
        Assert.Equal(0, gpu.Creates);
    }
    [Fact]
    public async Task Cancellation_precedes_probes_and_device_allocation()
    {
        var gpu = new FakeFactory(BackendKind.CanvasKitWebGl2);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu,
            Options, gpu, null, new CancellationToken(true)).AsTask());
        Assert.Equal(0, gpu.Probes);
    }
    [Fact]
    public void Placement_changes_retain_surface_identity_and_content_version()
    {
        var identity = new DeviceIdentity(Guid.NewGuid(), 1);
        var scene = new RetainedScene(identity);
        scene.SetLayer(new("control", Surface(identity), Matrix3x2.Identity, null, 1, 0));
        var before = scene.Capture();
        scene.SetPlacement("control", Matrix3x2.CreateTranslation(30,10), null, .5f, 1);
        var after = scene.Capture();
        Assert.Same(before.Layers[0].Surface, after.Layers[0].Surface);
        Assert.Equal(4, after.Layers[0].Surface.ContentVersion);
        Assert.Equal(Matrix3x2.Identity, before.Layers[0].Transform);
        Assert.Equal(30, after.Layers[0].Transform.M31);
        Assert.True(after.Revision > before.Revision);
    }
    [Fact]
    public void Equal_Z_order_keeps_insertion_order_after_updating_a_layer()
    {
        var identity = new DeviceIdentity(Guid.NewGuid(), 1);
        var scene = new RetainedScene(identity);
        scene.SetLayer(new("z-first", Surface(identity,1), Matrix3x2.Identity, null, 1, 0));
        scene.SetLayer(new("a-second", Surface(identity,2), Matrix3x2.Identity, null, 1, 0));
        scene.SetPlacement("z-first", Matrix3x2.CreateTranslation(1,0), null, 1, 0);
        Assert.Equal(new[]{"z-first","a-second"}, scene.Capture().Layers.Select(l=>l.Id));
    }
    [Fact]
    public void Redundant_layer_update_does_not_produce_a_new_commit_revision()
    {
        var identity = new DeviceIdentity(Guid.NewGuid(), 1);
        var scene = new RetainedScene(identity);
        var layer = new LayerVisual("control", Surface(identity), Matrix3x2.Identity, null, 1, 0);
        scene.SetLayer(layer); long before = scene.Capture().Revision;
        scene.SetLayer(layer); Assert.Equal(before, scene.Capture().Revision);
    }
    [Fact]
    public void Cross_session_and_lost_device_handles_cannot_enter_the_scene()
    {
        var identity = new DeviceIdentity(Guid.NewGuid(), 2);
        var scene = new RetainedScene(identity);
        foreach(var foreign in new[]{new DeviceIdentity(Guid.NewGuid(),2),identity with {Generation=1}})
            Assert.Throws<InvalidOperationException>(()=>scene.SetLayer(new("control",Surface(foreign),Matrix3x2.Identity,null,1,0)));
    }
    [Fact]
    public void Recovery_invalidates_retained_metadata_and_requires_a_new_generation()
    {
        var identity = new DeviceIdentity(Guid.NewGuid(), 1);
        var scene = new RetainedScene(identity);
        scene.SetLayer(new("control",Surface(identity),Matrix3x2.Identity,null,1,0));
        Assert.Equal(new[]{"control"},scene.ResetDevice(identity with {Generation=2}));
        Assert.Empty(scene.Capture().Layers);
        Assert.Throws<InvalidOperationException>(()=>scene.ResetDevice(identity));
    }
    [Fact]
    public async Task Session_recovery_notifies_host_and_disposal_is_idempotent()
    {
        var factory = new FakeFactory(BackendKind.CanvasKitWebGl2);
        var session = await RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu,Options,factory,null);
        int invalidations=0;session.RepaintRequired+=()=>invalidations++;
        factory.Created!.Recover(); Assert.Equal(1,invalidations);
        await session.DisposeAsync();await session.DisposeAsync();
        Assert.Equal(1,factory.Created.Disposals);
        factory.Created.Recover();Assert.Equal(1,invalidations);
    }
    [Fact]
    public void Gradient_and_path_commands_own_snapshots_of_mutable_inputs()
    {
        var stops = new[]{new GradientStop(0,new(0xff0000ff)),new GradientStop(1,new(0x0000ffff))};
        var gradient = new LinearGradientSnapshot(Vector2.Zero,Vector2.One,stops,GradientSpread.Clamp,Matrix3x2.Identity);
        var segments = new[]{new PathSegment(PathVerb.Move,new(1,2))};
        var path = new PathSnapshot(segments,PathFillRule.Alternate);
        stops[0]=new(0,new(0x00ff00ff));segments[0]=new(PathVerb.Line,new(9,9));
        Assert.Equal(0xff0000ffu,gradient.Stops[0].Color.Rgba);
        Assert.Equal(PathVerb.Move,path.Segments[0].Verb);
    }
    [Fact]
    public void Batch_preserves_order_and_rejects_unbalanced_state()
    {
        var handle = Surface(new(Guid.NewGuid(),1)).Handle;
        DrawingCommand[] commands=[new SaveState(),new ClearSurface(new(0xffffffff)),new RestoreState()];
        var batch = new DrawingBatch(handle,0,commands);commands[1]=new ResetClip();
        Assert.IsType<ClearSurface>(batch.Commands[1]);
        Assert.Throws<ArgumentException>(()=>new DrawingBatch(handle,0,[new RestoreState()]));
        Assert.Throws<ArgumentException>(()=>new DrawingBatch(handle,0,[new SaveState()]));
    }
    [Fact]
    public void CPU_import_owns_bytes_and_validates_stride_before_deferred_use()
    {
        var pixels = new byte[]{255,0,0,255};
        var image = new CpuImageSnapshot("source",1,new(1,1,1,Layout),4,pixels);
        pixels[0]=0;var copied=new byte[4];image.CopyTo(copied);Assert.Equal(255,copied[0]);
        Assert.Throws<ArgumentException>(()=>new CpuImageSnapshot("source",1,Description,1,pixels));
    }
    [Fact]
    public void Dimension_accounting_cannot_wrap_the_memory_budget()
    {
        Assert.Throws<OverflowException>(()=>new SurfaceDescriptor(int.MaxValue,int.MaxValue,1,Layout));
        Assert.Equal(128L*96*4,Description.MinimumPixelBytes);
    }
    [Fact]
    public void Image_font_and_texture_brushes_cannot_cross_device_boundaries()
    {
        var target=Surface(new(Guid.NewGuid(),1)).Handle;
        var foreign=new DeviceIdentity(Guid.NewGuid(),1);
        var image=new ImageHandle(foreign,1,1);
        var path=new PathSnapshot([],PathFillRule.Alternate);
        DrawingCommand[] commands=[new DrawSurfaceImage(image,new(0,0,1,1),new(0,0,1,1),ImageSampling.Nearest),
            new FillPath(path,new ImageBrushSnapshot(image,Matrix3x2.Identity,GradientSpread.Repeat,ImageSampling.Linear)),
            new DrawGlyphRun(new(new(foreign,1),12,[]),Vector2.Zero,new SolidBrushSnapshot(new(0xffffffff)))];
        foreach(var command in commands) Assert.Throws<InvalidOperationException>(()=>new DrawingBatch(target,0,[command]));
    }
    [Fact]
    public void Host_wiring_records_once_and_closes_the_callback_lifetime()
    {
        var device=new FakeDevice(BackendKind.CanvasKitWebGl2);
        var target=device.Surfaces.Create(Description);
        DrawingCommandRecorder? captured=null;int paints=0;
        bool queued=new DisplayPaintCoordinator(device).Paint(target.Handle,recorder=>{
            paints++;captured=recorder;recorder.Record(new ClearSurface(new(0xffffffff)));
        });
        Assert.True(queued);Assert.Equal(1,paints);
        var batch=Assert.Single(device.Queued);Assert.Equal(target.ContentVersion,batch.ExpectedContentVersion);
        Assert.IsType<ClearSurface>(Assert.Single(batch.Commands));
        Assert.Throws<InvalidOperationException>(()=>captured!.Record(new ResetClip()));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Callback_or_submission_failure_never_reinvokes_app_paint(bool failSubmission)
    {
        var device=new FakeDevice(BackendKind.CanvasKitWebGl2){FailSubmission=failSubmission};
        var target=device.Surfaces.Create(Description);
        DrawingCommandRecorder? captured=null;int paints=0;
        Assert.Throws<InvalidOperationException>(()=>new DisplayPaintCoordinator(device).Paint(target.Handle,recorder=>{
            paints++;captured=recorder;recorder.Record(new ClearSurface(new(0xffffffff)));
            if(!failSubmission) throw new InvalidOperationException("Unit paint failure");
        }));
        Assert.Equal(1,paints);Assert.Empty(device.Queued);
        Assert.Throws<InvalidOperationException>(()=>captured!.Record(new ResetClip()));
    }
    [Fact]
    public void Stale_surface_is_rejected_before_entering_the_app_callback()
    {
        var device=new FakeDevice(BackendKind.CanvasKitWebGl2);
        var target=device.Surfaces.Create(Description);device.Surfaces.Release(target.Handle);
        int paints=0;
        Assert.Throws<InvalidOperationException>(()=>new DisplayPaintCoordinator(device).Paint(target.Handle,_=>paints++));
        Assert.Equal(0,paints);
    }
    [Fact]
    public void Incomplete_drawing_state_never_reaches_the_backend()
    {
        var device=new FakeDevice(BackendKind.CanvasKitWebGl2);var target=device.Surfaces.Create(Description);
        Assert.Throws<ArgumentException>(()=>new DisplayPaintCoordinator(device).Paint(target.Handle,r=>r.Record(new RestoreState())));
        Assert.Empty(device.Queued);
    }
    [Fact]
    public void Mid_paint_CPU_access_fails_explicitly_until_incremental_submission_exists()
    {
        var device=new FakeDevice(BackendKind.CanvasKitWebGl2);var target=device.Surfaces.Create(Description);
        int paints=0;
        var error=Assert.Throws<NotSupportedException>(()=>new DisplayPaintCoordinator(device).Paint(target.Handle,recorder=>{
            paints++;recorder.Record(new ClearSurface(new(0xffffffff)));
            recorder.PrepareCpuAccess(CpuAccessReason.GetPixel);
        }));
        Assert.Contains("incremental submission",error.Message);
        Assert.Equal(1,paints);Assert.Empty(device.Queued);
    }
    [Fact]
    public async Task Idle_scene_does_not_submit_again_but_placement_does()
    {
        var device=new FakeDevice(BackendKind.CanvasKitWebGl2);var target=device.Surfaces.Create(Description);
        var scene=new RetainedScene(device.Identity);
        scene.SetLayer(new("control",target,Matrix3x2.Identity,null,1,0));
        var presenter=new RetainedFramePresenter(device,scene);
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        for(int i=0;i<5;i++) Assert.Null(await presenter.PresentIfChangedAsync());
        Assert.Equal(1,device.TestCompositor.Calls);
        scene.SetPlacement("control",Matrix3x2.CreateTranslation(30,10),null,1,0);
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        Assert.Equal(2,device.TestCompositor.Calls);
    }
    [Fact]
    public async Task Released_retained_surface_never_reaches_composition()
    {
        var device=new FakeDevice(BackendKind.CanvasKitWebGl2);var target=device.Surfaces.Create(Description);
        var scene=new RetainedScene(device.Identity);scene.SetLayer(new("control",target,Matrix3x2.Identity,null,1,0));
        device.Surfaces.Release(target.Handle);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new RetainedFramePresenter(device,scene).PresentIfChangedAsync().AsTask());
        Assert.Equal(0,device.TestCompositor.Calls);
    }
    [Fact]
    public async Task Failed_submission_can_retry_the_same_revision_without_repainting()
    {
        var device=new FakeDevice(BackendKind.CanvasKitWebGl2);var scene=new RetainedScene(device.Identity);
        var presenter=new RetainedFramePresenter(device,scene);device.TestCompositor.Fail=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>presenter.PresentIfChangedAsync().AsTask());
        device.TestCompositor.Fail=false;Assert.NotNull(await presenter.PresentIfChangedAsync());
        Assert.Equal(2,device.TestCompositor.Calls);
    }
    [Fact]
    public async Task Eviction_invalidates_retained_users_so_idle_cannot_hide_missing_content()
    {
        var factory=new FakeFactory(BackendKind.CanvasKitWebGl2);
        await using var session=await RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu,Options,factory,null);
        var device=factory.Created!;var target=device.Surfaces.Create(Description);
        session.Scene.SetLayer(new("control",target,Matrix3x2.Identity,null,1,0));
        var presenter=new RetainedFramePresenter(device,session.Scene);
        await presenter.PresentIfChangedAsync();int invalidations=0;session.RepaintRequired+=()=>invalidations++;
        device.Surfaces.Release(target.Handle);
        Assert.Empty(session.Scene.Capture().Layers);Assert.Equal(1,invalidations);
        Assert.NotNull(await presenter.PresentIfChangedAsync());
    }
    [Theory]
    [InlineData(0,1,1)] [InlineData(1,0,1)] [InlineData(1,1,0)] [InlineData(1,1,float.NaN)]
    public void Invalid_surface_dimensions_or_DPI_are_rejected(int width,int height,float dpi) =>
        Assert.Throws<ArgumentOutOfRangeException>(()=>new SurfaceDescriptor(width,height,dpi,Layout));

    private sealed class FakeFactory(BackendKind kind) : IRenderDeviceFactory
    {
        public int Probes,Creates;
        public bool FailCreation;
        public BackendKind? ActualKind;
        public DrawingFeatures Features=DrawingFeatures.Display;
        public FakeDevice? Created;
        public ValueTask<BackendProbe> ProbeAsync(CancellationToken token=default)
        { Probes++;return ValueTask.FromResult(new BackendProbe(kind,BackendAvailability.Available,Features,"Unit-test double, not hardware")); }
        public ValueTask<IRenderDevice> CreateAsync(RenderDeviceOptions options,CancellationToken token=default)
        {
            Creates++;
            if(FailCreation) throw new InvalidOperationException("Unit-test startup failure");
            Created=new FakeDevice(ActualKind??kind);return ValueTask.FromResult<IRenderDevice>(Created);
        }
    }
    private sealed class FakeDevice(BackendKind kind) : IRenderDevice
    {
        public DeviceIdentity Identity {get;private set;}=new(Guid.NewGuid(),1);
        public BackendKind Kind=>kind;
        public DrawingFeatures Features=>DrawingFeatures.Display;
        public bool Disposed;
        public int Disposals;
        public RenderDeviceState State=>Disposed?RenderDeviceState.Disposed:RenderDeviceState.Ready;
        private FakeSurfaces? _surfaces;
        public ISurfaceStore Surfaces=>_surfaces??=new(Identity);
        public IImageResources Images=>throw new NotSupportedException();
        public ICpuSurfaceAccess CpuAccess=>throw new NotSupportedException();
        public ITextService Text=>throw new NotSupportedException();
        public FakeCompositor TestCompositor {get;}=new();
        public IRetainedCompositor Compositor=>TestCompositor;
        public event Action<DeviceIdentity>? GenerationChanged;
        public void Recover(){Identity=Identity with{Generation=Identity.Generation+1};GenerationChanged?.Invoke(Identity);}
        public readonly List<DrawingBatch> Queued=[];
        public bool FailSubmission;
        public void Enqueue(DrawingBatch batch){if(FailSubmission) throw new InvalidOperationException("Unit submission failure");Queued.Add(batch);}
        public ValueTask<SubmissionReceipt> SubmitAsync(CancellationToken token=default)=>throw new NotSupportedException();
        public ValueTask DrainDisposalsAsync(CancellationToken token=default)=>throw new NotSupportedException();
        public ValueTask DisposeAsync(){Disposed=true;Disposals++;return ValueTask.CompletedTask;}
    }
    private sealed class FakeSurfaces(DeviceIdentity identity) : ISurfaceStore
    {
        public event Action<SurfaceHandle>? Invalidated;
        private SurfaceSnapshot? _current;
        public SurfaceSnapshot Create(SurfaceDescriptor descriptor)=>_current=new(new(identity,1,1),descriptor,4,PixelAuthority.Device);
        public bool IsValid(SurfaceHandle handle)=>_current?.Handle==handle;
        public SurfaceSnapshot Describe(SurfaceHandle handle)=>IsValid(handle)?_current!:throw new InvalidOperationException("Unit stale surface");
        public SurfaceSnapshot Resize(SurfaceHandle handle,SurfaceDescriptor descriptor)
        {var old=Describe(handle);_current=old with{Handle=handle with{Generation=handle.Generation+1},Descriptor=descriptor};Invalidated?.Invoke(handle);return _current;}
        public void Release(SurfaceHandle handle){Describe(handle);_current=null;Invalidated?.Invoke(handle);}
        public long ResidentBytes=>0; // Test metadata only: no GPU allocation.
        public IReadOnlyList<SurfaceHandle> TrimToBudget(ResourceBudget budget)=>[];
    }
    private sealed class FakeCompositor : IRetainedCompositor
    {
        public int Calls;public bool Fail;
        public ValueTask<SubmissionReceipt> PresentAsync(SceneCommit scene,CancellationToken token=default)
        {Calls++;if(Fail) throw new InvalidOperationException("Unit compositor failure");return ValueTask.FromResult(new SubmissionReceipt(Calls));}
    }
}
