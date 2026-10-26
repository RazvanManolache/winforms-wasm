extern alias IsoStub;

using System.IO.IsolatedStorage;
using IsoFile = IsoStub::System.IO.IsolatedStorage.IsolatedStorageFile;
using IsoFileStream = IsoStub::System.IO.IsolatedStorage.IsolatedStorageFileStream;

// FileMode honesty net for the headless store (control-bars layout
// persistence, spellchecker dictionaries, settings flows): every mode
// must behave like first-run desktop over the stored bytes. Unique
// evidence types per test isolate the static session maps.
[Collection("IsoStore")]
public sealed class IsolatedStorageFileFlowTests
{
    private sealed class FlowA { }
    private sealed class FlowB { }
    private sealed class FlowC { }
    private sealed class FlowD { }
    private sealed class FlowE { }
    private sealed class FlowF { }
    private sealed class FlowG { }
    private sealed class FlowH { }
    private sealed class FlowI { }
    private sealed class FlowJ { }
    private sealed class FlowK { }
    private sealed class FlowL { }
    private sealed class FlowM { }
    private sealed class FlowN { }
    private sealed class FlowO { }
    private sealed class FlowP { }

    private static IsoFile Store<T>() =>
        IsoFile.GetStore(IsolatedStorageScope.User, typeof(T), typeof(T));

    private static byte[] ReadAll(IsoFileStream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Create_ThenOpen_ReadsBytes()
    {
        var store = Store<FlowA>();
        using (var w = new IsoFileStream("roundtrip.bin", FileMode.Create, store))
            w.Write([1, 2, 3, 4], 0, 4);
        using var r = new IsoFileStream("roundtrip.bin", FileMode.Open, store);
        Assert.Equal([1, 2, 3, 4], ReadAll(r));
    }

    [Fact]
    public void Create_Existing_Overwrites()
    {
        var store = Store<FlowB>();
        using (var w = new IsoFileStream("over.bin", FileMode.Create, store))
            w.Write([9, 9, 9], 0, 3);
        using (var w = new IsoFileStream("over.bin", FileMode.Create, store))
            w.Write([1], 0, 1);
        using var r = new IsoFileStream("over.bin", FileMode.Open, store);
        Assert.Equal([1], ReadAll(r));
    }

    [Fact]
    public void CreateNew_Missing_Creates()
    {
        var store = Store<FlowC>();
        using (var w = new IsoFileStream("fresh.bin", FileMode.CreateNew, store))
            w.Write([5], 0, 1);
        Assert.True(store.FileExists("fresh.bin"));
    }

    [Fact]
    public void CreateNew_Existing_ThrowsIOException()
    {
        var store = Store<FlowD>();
        using (var w = new IsoFileStream("dup.bin", FileMode.Create, store))
            w.Write([5], 0, 1);
        Assert.Throws<IOException>(() => new IsoFileStream("dup.bin", FileMode.CreateNew, store));
    }

    [Fact]
    public void Open_Missing_ThrowsFileNotFound()
    {
        var store = Store<FlowE>();
        Assert.Throws<FileNotFoundException>(() => new IsoFileStream("nope.bin", FileMode.Open, store));
    }

    [Fact]
    public void OpenOrCreate_Missing_CreatesEmpty()
    {
        var store = Store<FlowF>();
        using (var s = new IsoFileStream("maybe.bin", FileMode.OpenOrCreate, store))
            Assert.Equal(0, s.Length);
        Assert.True(store.FileExists("maybe.bin"));
    }

    [Fact]
    public void OpenOrCreate_Existing_PreservesBytes()
    {
        var store = Store<FlowG>();
        using (var w = new IsoFileStream("keep.bin", FileMode.Create, store))
            w.Write([7, 8], 0, 2);
        using var s = new IsoFileStream("keep.bin", FileMode.OpenOrCreate, store);
        Assert.Equal([7, 8], ReadAll(s));
    }

    [Fact]
    public void Truncate_ClearsExisting()
    {
        var store = Store<FlowH>();
        using (var w = new IsoFileStream("cut.bin", FileMode.Create, store))
            w.Write([1, 2, 3], 0, 3);
        using (var t = new IsoFileStream("cut.bin", FileMode.Truncate, store))
            Assert.Equal(0, t.Length);
        using var r = new IsoFileStream("cut.bin", FileMode.Open, store);
        Assert.Equal(0, r.Length);
    }

    [Fact]
    public void Truncate_Missing_ThrowsFileNotFound()
    {
        var store = Store<FlowI>();
        Assert.Throws<FileNotFoundException>(() => new IsoFileStream("ghost.bin", FileMode.Truncate, store));
    }

    [Fact]
    public void Append_SeeksToEnd()
    {
        var store = Store<FlowJ>();
        using (var w = new IsoFileStream("log.bin", FileMode.Create, store))
            w.Write([1, 2], 0, 2);
        using (var a = new IsoFileStream("log.bin", FileMode.Append, store))
        {
            Assert.Equal(2, a.Position);
            a.Write([3], 0, 1);
        }
        using var r = new IsoFileStream("log.bin", FileMode.Open, store);
        Assert.Equal([1, 2, 3], ReadAll(r));
    }

    [Fact]
    public void Append_Missing_Creates()
    {
        var store = Store<FlowK>();
        using (var a = new IsoFileStream("newlog.bin", FileMode.Append, store))
            a.Write([4], 0, 1);
        using var r = new IsoFileStream("newlog.bin", FileMode.Open, store);
        Assert.Equal([4], ReadAll(r));
    }

    [Fact]
    public void ReadOnly_Write_ThrowsNotSupported()
    {
        var store = Store<FlowL>();
        using (var w = new IsoFileStream("ro.bin", FileMode.Create, store))
            w.Write([1], 0, 1);
        using var r = new IsoFileStream("ro.bin", FileMode.Open, FileAccess.Read, store);
        Assert.False(r.CanWrite);
        Assert.Throws<NotSupportedException>(() => r.Write([2], 0, 1));
    }

    [Fact]
    public void Dispose_Persists_WithoutFlush()
    {
        var store = Store<FlowM>();
        using (var w = new IsoFileStream("auto.bin", FileMode.Create, store))
            w.Write([6, 7], 0, 2);
        using var r = new IsoFileStream("auto.bin", FileMode.Open, store);
        Assert.Equal([6, 7], ReadAll(r));
    }

    [Fact]
    public void Backslash_And_Case_Normalize()
    {
        var store = Store<FlowN>();
        using (var w = new IsoFileStream(@"sub\FILE.bin", FileMode.Create, store))
            w.Write([1], 0, 1);
        Assert.True(store.FileExists("sub/file.BIN"));
        Assert.True(store.DirectoryExists("sub"));
    }

    [Fact]
    public void Null_Paths_Throw()
    {
        var store = Store<FlowO>();
        Assert.Throws<ArgumentNullException>(() => store.FileExists(null!));
        Assert.Throws<ArgumentNullException>(() => new IsoFileStream(null!, FileMode.Open, store));
        Assert.Throws<ArgumentNullException>(() => new IsoFileStream("x", FileMode.Open, null!));
    }

    [Fact]
    public void Disposed_Store_Throws()
    {
        var store = Store<FlowP>();
        store.Dispose();
        Assert.Throws<ObjectDisposedException>(() => store.FileExists("x"));
        Assert.Throws<ObjectDisposedException>(() => store.CreateDirectory("d"));
    }
}
