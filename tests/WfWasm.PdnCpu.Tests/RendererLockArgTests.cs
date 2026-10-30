using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// Regression: renderer-bitmap Lock arguments for thumbnail reads.
// The production path invoked Lock with a raw "ReadOnly" string and a
// null rect; reflection Invoke never converts those, so every call
// threw and the renderer-first thumbnail path silently fell back to
// stale surface reads. These tests pin bindable argument shapes using
// fakes with the same (rect, options-enum) member shape: the builder
// output must invoke cleanly while the old shape must fail to bind.
public sealed class RendererLockArgTests
{
    private enum FakeLockOptions
    {
        None = 0,
        Read = 1,
        ReadWrite = 2
    }

    private struct FakeRect
    {
        public int X;
        public int Y;
        public object? Size;
        public FakeRect(int x, int y, object? size) { X = x; Y = y; Size = size; }
    }

    private sealed class FakeLockSource
    {
        public string Seen = "";
        public object Lock(FakeRect rect, FakeLockOptions opt)
        {
            Seen = rect.X + "," + rect.Y + ":" + opt;
            return new object();
        }
    }

    private sealed class FakeCopySource
    {
        public string Seen = "";
        public void CopyPixels(IntPtr dst, int stride, int size, FakeRegion region)
        {
            Seen = "struct-fourth";
        }
        public void CopyRect(IntPtr dst, int stride, int size, FakeRect region)
        {
            Seen = "rect-fourth";
        }
        public void CopyPixelsRef(IntPtr dst, int stride, int size, string? tag)
        {
            Seen = "ref-fourth";
        }
    }

    [Fact]
    public void BuildRendererLockArgs_BindsFakeLock()
    {
        var size = new object();
        object?[] args = CpuPaint.BuildRendererLockArgs(size, typeof(FakeRect), typeof(FakeLockOptions));
        Assert.Equal(2, args.Length);
        var rect = Assert.IsType<FakeRect>(args[0]);
        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Same(size, rect.Size);
        Assert.Equal(FakeLockOptions.Read, args[1]);

        var src = new FakeLockSource();
        var lockM = typeof(FakeLockSource).GetMethod("Lock");
        Assert.NotNull(lockM);
        object? token = lockM.Invoke(src, args);
        Assert.NotNull(token);
        Assert.Equal("0,0:Read", src.Seen);
    }

    [Fact]
    public void OldLockArgShape_FailsToBind()
    {
        // The pre-fix call shape: string option + null rect. Neither
        // converts at the reflection boundary, so the invoke throws.
        var src = new FakeLockSource();
        var lockM = typeof(FakeLockSource).GetMethod("Lock");
        Assert.NotNull(lockM);
        Assert.ThrowsAny<Exception>(() => lockM.Invoke(src, new object?[] { "ReadOnly", null }));
    }

    private struct FakeRegion
    {
        public int X;
        public int Y;
    }

    [Fact]
    public void BuildCopyPixelsFourth_ValueTypeDefaults()
    {
        object? fourth = CpuPaint.BuildCopyPixelsFourth(typeof(FakeRegion));
        Assert.NotNull(fourth);
        Assert.IsType<FakeRegion>(fourth);
        Assert.Equal(default(FakeRegion), fourth);
    }

    [Fact]
    public void BuildCopyPixelsFourth_ReferenceTypeStaysNull()
    {
        Assert.Null(CpuPaint.BuildCopyPixelsFourth(typeof(string)));
        Assert.Null(CpuPaint.BuildCopyPixelsFourth(typeof(int?)));
    }

    [Fact]
    public void BuiltFourth_BindsFakeCopyPixels()
    {
        var src = new FakeCopySource();
        var structM = typeof(FakeCopySource).GetMethod("CopyPixels");
        Assert.NotNull(structM);
        object? fourth = CpuPaint.BuildCopyPixelsFourth(structM.GetParameters()[3].ParameterType);
        structM.Invoke(src, new object?[] { IntPtr.Zero, 0, 0, fourth });
        Assert.Equal("struct-fourth", src.Seen);

        var refM = typeof(FakeCopySource).GetMethod("CopyPixelsRef");
        Assert.NotNull(refM);
        object? refFourth = CpuPaint.BuildCopyPixelsFourth(refM.GetParameters()[3].ParameterType);
        Assert.Null(refFourth);
        refM.Invoke(src, new object?[] { IntPtr.Zero, 0, 0, refFourth });
        Assert.Equal("ref-fourth", src.Seen);
    }

    private sealed unsafe class FakeVoidCopySource
    {
        public string Seen = "";
        public void CopyPixels(void* dst, uint stride, uint size, int? region)
        {
            Seen = "void-nullable:" + region.HasValue;
        }
        public void OnCopyPixels(void* dst, uint stride, uint size, FakeRegion region)
        {
            Seen = "void-struct";
        }
    }

    [Fact]
    public unsafe void SelectCopyPixels_PrefersNullableRegion()
    {
        var methods = typeof(FakeVoidCopySource).GetMethods();
        var cands = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
        foreach (var m in methods)
        {
            if ((m.Name == "CopyPixels" || m.Name.EndsWith("CopyPixels"))
                && m.GetParameters().Length == 4)
                cands.Add(m);
        }
        Assert.Equal(2, cands.Count);
        var picked = CpuPaint.SelectCopyPixels(cands);
        Assert.NotNull(picked);
        Assert.Equal("CopyPixels", picked.Name);
    }

    [Fact]
    public unsafe void BuildCopyPixelsArgs_BindsVoidPointerOverload()
    {
        var src = new FakeVoidCopySource();
        var m = typeof(FakeVoidCopySource).GetMethod("CopyPixels");
        Assert.NotNull(m);
        byte[] buf = new byte[64];
        fixed (byte* p = buf)
        {
            object?[] args = CpuPaint.BuildCopyPixelsArgs(m, (IntPtr)p, 16, 64, null, new object());
            Assert.Equal(4, args.Length);
            m.Invoke(src, args);
            Assert.Equal("void-nullable:False", src.Seen);
        }
    }

    [Fact]
    public unsafe void BuildCopyPixelsArgs_StructRegionBecomesFullRect()
    {
        // FakeRect's name contains "Rect" and it takes (0, 0, size),
        // mirroring the production RectInt32 full-surface shape.
        var src = new FakeCopySource();
        var m = typeof(FakeCopySource).GetMethod("CopyRect");
        Assert.NotNull(m);
        var size = new object();
        byte[] buf = new byte[64];
        fixed (byte* p = buf)
        {
            object?[] args = CpuPaint.BuildCopyPixelsArgs(m, (IntPtr)p, 16, 64, null, size);
            var rect = Assert.IsType<FakeRect>(args[3]);
            Assert.Equal(0, rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Same(size, rect.Size);
            m.Invoke(src, args);
            Assert.Equal("rect-fourth", src.Seen);
        }
    }
}
