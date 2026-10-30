using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// CpuPaint.DictTryGetValue/DictTryAddValue: the non-generic IDictionary
// boundary plus a linear Equals scan that only runs on hash-miss. Pin the
// contract the LayersStrip shadow cache needs: present keys read back,
// absent keys read false, Add-if-absent never throws on duplicates, and a
// repeated add reports already-present without growing the table.
public sealed class DictHelperTests
{
    private struct SizeLike
    {
        public int W;
        public int H;
        public SizeLike(int w, int h) { W = w; H = h; }
    }

    [Fact]
    public void TryGet_Present_Key_Reads_Value()
    {
        var d = new System.Collections.Hashtable { [new SizeLike(50, 50)] = "shadow" };
        Assert.True(CpuPaint.DictTryGetValue(d, new SizeLike(50, 50), out object v));
        Assert.Equal("shadow", v);
    }

    [Fact]
    public void TryGet_Absent_Key_Reads_False()
    {
        var d = new System.Collections.Hashtable { [new SizeLike(50, 50)] = "shadow" };
        Assert.False(CpuPaint.DictTryGetValue(d, new SizeLike(60, 60), out object v));
        Assert.Null(v);
    }

    [Fact]
    public void TryAdd_Absent_Key_Inserts_True()
    {
        var d = new System.Collections.Hashtable();
        Assert.True(CpuPaint.DictTryAddValue(d, new SizeLike(50, 50), "shadow"));
        Assert.Equal("shadow", d[new SizeLike(50, 50)]);
    }

    [Fact]
    public void TryAdd_Duplicate_Key_Returns_False_Without_Throw()
    {
        var d = new System.Collections.Hashtable { [new SizeLike(50, 50)] = "shadow" };
        Assert.False(CpuPaint.DictTryAddValue(d, new SizeLike(50, 50), "other"));
        Assert.Equal(1, d.Count);
        Assert.Equal("shadow", d[new SizeLike(50, 50)]);
    }

    [Fact]
    public void TryAdd_StructKey_GenericDictionary_RoundTrips()
    {
        var d = new System.Collections.Generic.Dictionary<SizeLike, string>();
        try
        {
            Assert.True(CpuPaint.DictTryAddValue(d, new SizeLike(50, 50), "shadow"));
            Assert.True(CpuPaint.DictTryGetValue(d, new SizeLike(50, 50), out object v));
            Assert.Equal("shadow", v);
            Assert.False(CpuPaint.DictTryAddValue(d, new SizeLike(50, 50), "other"));
            // rev258: the interpreter-untrustworthy PDN table is never
            // mutated at shadow sites; the side table carries the entry.
            Assert.Equal(0, d.Count);
        }
        finally { CpuPaint.DictShadowClear(d); }
    }

    [Fact]
    public void TryAdd_GenericDictionary_SideTable_Isolated_Per_DictRef()
    {
        var d1 = new System.Collections.Generic.Dictionary<SizeLike, string>();
        var d2 = new System.Collections.Generic.Dictionary<SizeLike, string>();
        try
        {
            var key = new SizeLike(50, 50);
            Assert.True(CpuPaint.DictTryAddValue(d1, key, "one"));
            Assert.True(CpuPaint.DictTryAddValue(d2, key, "two"));
            Assert.True(CpuPaint.DictTryGetValue(d1, key, out object v1));
            Assert.True(CpuPaint.DictTryGetValue(d2, key, out object v2));
            Assert.Equal("one", v1);
            Assert.Equal("two", v2);
        }
        finally { CpuPaint.DictShadowClear(d1); CpuPaint.DictShadowClear(d2); }
    }

    [Fact]
    public void ShadowClear_Evicts_Only_Target_Dict_And_ReAdd_Works()
    {
        var d1 = new System.Collections.Generic.Dictionary<SizeLike, string>();
        var d2 = new System.Collections.Generic.Dictionary<SizeLike, string>();
        try
        {
            var key = new SizeLike(50, 50);
            Assert.True(CpuPaint.DictTryAddValue(d1, key, "one"));
            Assert.True(CpuPaint.DictTryAddValue(d2, key, "two"));
            CpuPaint.DictShadowClear(d1);
            Assert.False(CpuPaint.DictTryGetValue(d1, key, out object v1));
            Assert.Null(v1);
            Assert.True(CpuPaint.DictTryGetValue(d2, key, out object v2));
            Assert.Equal("two", v2);
            Assert.True(CpuPaint.DictTryAddValue(d1, key, "one-again"));
            Assert.True(CpuPaint.DictTryGetValue(d1, key, out object v3));
            Assert.Equal("one-again", v3);
        }
        finally { CpuPaint.DictShadowClear(d1); CpuPaint.DictShadowClear(d2); }
    }

    [Fact]
    public void ShadowClear_Null_Dict_Is_NoOp()
    {
        CpuPaint.DictShadowClear(null);
    }

    [Fact]
    public void TryAdd_SideTable_Fifo_Cap_Bounds_Growth()
    {
        var d = new System.Collections.Generic.Dictionary<SizeLike, string>();
        try
        {
            const int n = 600;
            for (int i = 0; i < n; i++)
                Assert.True(CpuPaint.DictTryAddValue(d, new SizeLike(i, i), "v" + i));
            // Oldest evicted past the cap, newest retained.
            Assert.False(CpuPaint.DictTryGetValue(d, new SizeLike(0, 0), out object _));
            Assert.True(CpuPaint.DictTryGetValue(d, new SizeLike(n - 1, n - 1), out object last));
            Assert.Equal("v" + (n - 1), last);
        }
        finally { CpuPaint.DictShadowClear(d); }
    }

    [Fact]
    public void TryAdd_Null_Key_Throws()
    {
        var d = new System.Collections.Hashtable();
        Assert.Throws<System.ArgumentNullException>(() => CpuPaint.DictTryAddValue(d, null, "x"));
    }
}
