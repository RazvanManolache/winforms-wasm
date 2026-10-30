using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// CpuPaint.IsNullThumbnailBitmap: the drain's null-bitmap guard reads the
// ready-event-args through reflection (PDN assemblies load dynamically, so
// no static PDN types). Fail-closed: any anomaly (null args, missing
// Value/Item2, reflection failure) reports null so the drain skips the
// item instead of delivering a dead bitmap and killing the session.
public sealed class IsNullThumbnailBitmapTests
{
    private sealed class FakeArgs
    {
        public (object?, object?) Value { get; }
        public FakeArgs((object?, object?) value) { Value = value; }
    }

    [Fact]
    public void NullArgs_ReportsNull()
    {
        Assert.True(CpuPaint.IsNullThumbnailBitmap(null!));
    }

    [Fact]
    public void NullBitmap_ReportsNull()
    {
        Assert.True(CpuPaint.IsNullThumbnailBitmap(new FakeArgs((new object(), null))));
    }

    [Fact]
    public void LiveBitmap_ReportsNotNull()
    {
        Assert.False(CpuPaint.IsNullThumbnailBitmap(new FakeArgs((new object(), new object()))));
    }

    [Fact]
    public void WrongShape_ReportsNull_FailClosed()
    {
        Assert.True(CpuPaint.IsNullThumbnailBitmap(new object()));
    }

    private sealed class FakeDisposable : IDisposable
    {
        public bool Disposed;
        public void Dispose() { Disposed = true; }
    }

    private sealed class FakePooledArgs
    {
        public (object?, object?) Value { get; }
        public bool Returned;
        public FakePooledArgs((object?, object?) value) { Value = value; }
        public void Dispose() { Returned = true; }
    }

    [Fact]
    public void Dispose_Disposes_Bitmap_And_Returns_Args()
    {
        var bmp = new FakeDisposable();
        var args = new FakePooledArgs((new object(), bmp));
        CpuPaint.DisposeThumbnailItem(args);
        Assert.True(bmp.Disposed);
        Assert.True(args.Returned);
    }

    [Fact]
    public void Dispose_NullBitmap_Still_Returns_Args()
    {
        var args = new FakePooledArgs((new object(), null));
        CpuPaint.DisposeThumbnailItem(args);
        Assert.True(args.Returned);
    }

    [Fact]
    public void Dispose_WrongShape_NoThrow()
    {
        CpuPaint.DisposeThumbnailItem(new object());
        CpuPaint.DisposeThumbnailItem(null!);
    }
}
