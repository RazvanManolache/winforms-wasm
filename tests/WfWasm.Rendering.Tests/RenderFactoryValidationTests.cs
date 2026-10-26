using System.Numerics;
using WfWasm.Rendering;

namespace WfWasm.Rendering.Tests;

// Factory argument-validation and contract net: bad inputs must fail
// fast with the documented exception before any probe or device work.
public sealed class RenderFactoryValidationTests
{
    private static readonly PixelLayout Layout = new(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb);
    private static SurfaceDescriptor Description => new(128, 96, 1, Layout);
    private static RenderDeviceOptions Options => new("wfcanvas", Description, new(64 * 1024 * 1024));

    [Fact]
    public async Task Null_Options_Throws_Before_Probes()
    {
        var gpu = new ValFactory(BackendKind.CanvasKitWebGl2);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu, null!, gpu, null).AsTask());
        Assert.Equal(0, gpu.Probes);
    }

    [Fact]
    public async Task Blank_TargetId_Throws()
    {
        var gpu = new ValFactory(BackendKind.CanvasKitWebGl2);
        var bad = new RenderDeviceOptions("  ", Description, new(1024));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu, bad, gpu, null).AsTask());
        Assert.Equal(0, gpu.Probes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositive_Budget_Throws(long bytes)
    {
        var gpu = new ValFactory(BackendKind.CanvasKitWebGl2);
        var bad = new RenderDeviceOptions("wfcanvas", Description, new(bytes));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu, bad, gpu, null).AsTask());
        Assert.Equal(0, gpu.Probes);
    }

    [Fact]
    public async Task Undefined_Policy_Throws()
    {
        var gpu = new ValFactory(BackendKind.CanvasKitWebGl2);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            RenderSessionFactory.CreateAsync((RenderPolicy)999, Options, gpu, null).AsTask());
        Assert.Equal(0, gpu.Probes);
    }

    [Fact]
    public async Task RequireGpu_Without_Factory_Throws_With_Reason()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu, Options, null, null).AsTask());
        Assert.Contains("no GPU factory registered", ex.Message);
    }

    [Fact]
    public async Task CpuCompatibility_Without_Software_Throws()
    {
        var gpu = new ValFactory(BackendKind.CanvasKitWebGl2);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            RenderSessionFactory.CreateAsync(RenderPolicy.CpuCompatibility, Options, gpu, null).AsTask());
        Assert.Equal(0, gpu.Probes);
    }

    [Fact]
    public async Task Unavailable_Software_Adapter_Throws()
    {
        var gpu = new ValFactory(BackendKind.CanvasKitWebGl2);
        var soft = new ValFactory(BackendKind.Software, BackendAvailability.Unsupported);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            RenderSessionFactory.CreateAsync(RenderPolicy.CpuCompatibility, Options, gpu, soft).AsTask());
        Assert.Equal(0, gpu.Probes);
        Assert.Equal(1, soft.Probes);
    }

    [Fact]
    public async Task Cancelled_Token_Throws_Before_Work()
    {
        var gpu = new ValFactory(BackendKind.CanvasKitWebGl2);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu, Options, gpu, null,
                new CancellationToken(true)).AsTask());
        Assert.Equal(0, gpu.Probes);
    }

    [Fact]
    public void Display_Flag_Is_Full_UI_Set()
    {
        Assert.Equal(
            DrawingFeatures.Paths | DrawingFeatures.ComplexClips | DrawingFeatures.Gradients |
            DrawingFeatures.Images | DrawingFeatures.Text | DrawingFeatures.SourceCopy |
            DrawingFeatures.CpuPixelAccess | DrawingFeatures.RetainedComposition,
            DrawingFeatures.Display);
    }

    [Fact]
    public void SubmissionReceipt_Is_Value_Equality()
    {
        Assert.Equal(new SubmissionReceipt(7), new SubmissionReceipt(7));
        Assert.NotEqual(new SubmissionReceipt(7), new SubmissionReceipt(8));
    }

    [Fact]
    public void RenderingCounters_Carry_All_Six_Signals()
    {
        var c = new RenderingCounters(1, 2, 3, 4, 5, 6);
        Assert.Equal((1L, 2L, 3L, 4L, 5L, 6L),
            (c.ControlPaints, c.CpuRasterPixels, c.UploadedBytes, c.ReadbackBytes, c.Fallbacks, c.ResidentBytes));
    }

    private sealed class ValFactory(BackendKind kind, BackendAvailability avail = BackendAvailability.Available)
        : IRenderDeviceFactory
    {
        public int Probes;
        public ValueTask<BackendProbe> ProbeAsync(CancellationToken token = default)
        {
            Probes++;
            return ValueTask.FromResult(new BackendProbe(kind, avail, DrawingFeatures.Display, "unit"));
        }
        public ValueTask<IRenderDevice> CreateAsync(RenderDeviceOptions options, CancellationToken token = default) =>
            ValueTask.FromException<IRenderDevice>(new NotSupportedException("unit has no device"));
    }
}
