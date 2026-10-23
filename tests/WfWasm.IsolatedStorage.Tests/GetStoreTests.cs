extern alias IsoStub;

using System.IO.IsolatedStorage;
using IsoFile = IsoStub::System.IO.IsolatedStorage.IsolatedStorageFile;
using IsoFileStream = IsoStub::System.IO.IsolatedStorage.IsolatedStorageFileStream;

// Legacy evidence-based entry points (control-bars window creation calls
// GetStore(scope, Type, Type) at boot): same-triple handles share one
// session store, scope and evidence types separate stores, and the full
// FileMode file flow works over the stored bytes.
public sealed class GetStoreTests
{
    private sealed class EvidenceA { }
    private sealed class EvidenceB { }

    [Fact]
    public void Same_Triple_Shares_One_Store()
    {
        var a = IsoFile.GetStore(IsolatedStorageScope.User, typeof(EvidenceA), typeof(EvidenceA));
        var b = IsoFile.GetStore(IsolatedStorageScope.User, typeof(EvidenceA), typeof(EvidenceA));
        Assert.NotNull(a);
        using var w = new IsoFileStream("shared.txt", FileMode.Create, a);
        w.Write([1, 2, 3], 0, 3);
        w.Flush();
        Assert.True(b.FileExists("shared.txt"));
    }

    [Fact]
    public void Scope_Separates_Stores()
    {
        var user = IsoFile.GetStore(IsolatedStorageScope.User, typeof(EvidenceA), typeof(EvidenceA));
        var machine = IsoFile.GetStore(IsolatedStorageScope.Machine, typeof(EvidenceA), typeof(EvidenceA));
        using var w = new IsoFileStream("scoped.txt", FileMode.Create, user);
        w.Write([7], 0, 1);
        w.Flush();
        Assert.False(machine.FileExists("scoped.txt"));
    }

    [Fact]
    public void Evidence_Types_Separate_Stores()
    {
        var a = IsoFile.GetStore(IsolatedStorageScope.User, typeof(EvidenceA), typeof(EvidenceA));
        var b = IsoFile.GetStore(IsolatedStorageScope.User, typeof(EvidenceB), typeof(EvidenceB));
        using var w = new IsoFileStream("evid.txt", FileMode.Create, a);
        w.Write([9], 0, 1);
        w.Flush();
        Assert.False(b.FileExists("evid.txt"));
    }

    [Fact]
    public void Two_Arg_Overload_Works_And_Round_Trips()
    {
        var store = IsoFile.GetStore(IsolatedStorageScope.User, typeof(EvidenceA));
        Assert.NotNull(store);
        using var w = new IsoFileStream("twoarg.txt", FileMode.Create, store);
        w.Write([4, 5], 0, 2);
        w.Flush();
        using var r = new IsoFileStream("twoarg.txt", FileMode.Open, store);
        var buf = new byte[2];
        Assert.Equal(2, r.Read(buf, 0, 2));
        Assert.Equal([4, 5], buf);
    }

    [Fact]
    public void Missing_File_Open_Throws_FileNotFound()
    {
        var store = IsoFile.GetStore(IsolatedStorageScope.User, typeof(EvidenceA), typeof(EvidenceB));
        Assert.Throws<FileNotFoundException>(() => new IsoFileStream("nope-missing.txt", FileMode.Open, store));
    }

    [Fact]
    public void File_Enumeration_Honors_Patterns()
    {
        var store = IsoFile.GetStore(IsolatedStorageScope.User, typeof(Patterns), typeof(Patterns));
        foreach (var name in new[] { "a.txt", "b.txt", "c.dat", "sub/d.txt" })
        {
            using var w = new IsoFileStream(name, FileMode.Create, store);
            w.Write([1], 0, 1);
            w.Flush();
        }
        Assert.Equal(["a.txt", "b.txt", "c.dat"], store.GetFileNames().OrderBy(x => x).ToArray());
        Assert.Equal(["a.txt", "b.txt"], store.GetFileNames("*.txt").OrderBy(x => x).ToArray());
        Assert.Equal(["a.txt"], store.GetFileNames("a.*"));
        Assert.Single(store.GetFileNames("?.dat"));
    }

    [Fact]
    public void Directory_Enumeration_And_Delete_Flows()
    {
        var store = IsoFile.GetStore(IsolatedStorageScope.User, typeof(Dirs), typeof(Dirs));
        store.CreateDirectory("layouts");
        Assert.True(store.DirectoryExists("layouts"));
        Assert.Contains("layouts", store.GetDirectoryNames());
        using (var w = new IsoFileStream("layouts/main.xml", FileMode.Create, store))
        {
            w.Write([2], 0, 1);
            w.Flush();
        }
        Assert.Contains("layouts", store.GetDirectoryNames("lay*"));
        Assert.True(store.DirectoryExists("layouts"));
        store.DeleteFile("layouts/main.xml");
        Assert.False(store.FileExists("layouts/main.xml"));
        store.DeleteDirectory("layouts");
        Assert.False(store.DirectoryExists("layouts"));
        Assert.Empty(store.GetDirectoryNames());
    }

    [Fact]
    public void Access_Read_Gives_Read_Only_View_Without_Write_Back()
    {
        var store = IsoFile.GetStore(IsolatedStorageScope.User, typeof(Ro), typeof(Ro));
        using (var w = new IsoFileStream("ro.bin", FileMode.Create, FileAccess.ReadWrite, FileShare.None, store))
        {
            w.Write([8, 9], 0, 2);
            w.Flush();
        }
        using (var r = new IsoFileStream("ro.bin", FileMode.Open, FileAccess.Read, FileShare.Read, store))
        {
            Assert.False(r.CanWrite);
            var buf = new byte[2];
            Assert.Equal(2, r.Read(buf, 0, 2));
            Assert.Equal([8, 9], buf);
            Assert.Throws<NotSupportedException>(() => r.Write([0], 0, 1));
        }
        using var check = new IsoFileStream("ro.bin", FileMode.Open, store);
        var again = new byte[2];
        Assert.Equal(2, check.Read(again, 0, 2));
        Assert.Equal([8, 9], again);
    }

    [Fact]
    public void Four_Arg_Opener_Works()
    {
        var store = IsoFile.GetStore(IsolatedStorageScope.User, typeof(Ro), typeof(Ro2));
        using var w = new IsoFileStream("four.bin", FileMode.Create, FileAccess.ReadWrite, store);
        w.Write([3], 0, 1);
        w.Flush();
        Assert.True(store.FileExists("four.bin"));
    }

    private sealed class Patterns { }
    private sealed class Dirs { }
    private sealed class Ro { }
    private sealed class Ro2 { }
}
