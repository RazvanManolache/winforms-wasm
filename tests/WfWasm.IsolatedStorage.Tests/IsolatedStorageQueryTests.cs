extern alias IsoStub;

using System.IO.IsolatedStorage;
using IsoFile = IsoStub::System.IO.IsolatedStorage.IsolatedStorageFile;
using IsoFileStream = IsoStub::System.IO.IsolatedStorage.IsolatedStorageFileStream;

// Name-matching, directory and delete semantics over the headless store.
[Collection("IsoStore")]
public sealed class IsolatedStorageQueryTests
{
    private sealed class QueryA { }
    private sealed class QueryB { }
    private sealed class QueryC { }
    private sealed class QueryD { }
    private sealed class QueryE { }
    private sealed class QueryF { }

    private static IsoFile Store<T>() =>
        IsoFile.GetStore(IsolatedStorageScope.User, typeof(T), typeof(T));

    private static void Seed(IsoFile store, params string[] names)
    {
        foreach (string n in names)
            using (var w = new IsoFileStream(n, FileMode.Create, store))
                w.Write([1], 0, 1);
    }

    [Theory]
    [InlineData("*.txt", new[] { "a.txt", "b.txt" })]
    [InlineData("a.*", new[] { "a.txt", "a.bin" })]
    [InlineData("*", new[] { "a.txt", "a.bin", "b.txt" })]
    [InlineData("?.txt", new[] { "a.txt", "b.txt" })]
    [InlineData("zzz.*", new string[0])]
    [InlineData("A.TXT", new[] { "a.txt" })]
    public void GetFileNames_Pattern_Matches(string pattern, string[] expected)
    {
        var store = Store<QueryA>();
        Seed(store, "a.txt", "a.bin", "b.txt", "sub/c.txt");
        Assert.Equal(expected.OrderBy(x => x), store.GetFileNames(pattern).OrderBy(x => x));
    }

    [Fact]
    public void GetFileNames_Excludes_Subdirectories()
    {
        var store = Store<QueryB>();
        Seed(store, "top.txt", "sub/nested.txt");
        Assert.Equal(["top.txt"], store.GetFileNames());
    }

    [Fact]
    public void GetDirectoryNames_Sees_Explicit_And_Implied()
    {
        var store = Store<QueryC>();
        try
        {
            store.CreateDirectory("qc-explicit");
            Seed(store, "qc-implied/file.bin");
            var names = store.GetDirectoryNames().OrderBy(x => x).ToArray();
            Assert.Contains("qc-explicit", names);
            Assert.Contains("qc-implied", names);
        }
        finally
        {
            store.DeleteDirectory("qc-explicit");
            store.DeleteDirectory("qc-implied");
        }
    }

    [Theory]
    [InlineData("qd-exp*", new[] { "qd-explicit" })]
    [InlineData("qd-*", new[] { "qd-explicit", "qd-implied" })]
    [InlineData("qd-nope*", new string[0])]
    public void GetDirectoryNames_Pattern_Matches(string pattern, string[] expected)
    {
        var store = Store<QueryD>();
        try
        {
            store.CreateDirectory("qd-explicit");
            Seed(store, "qd-implied/file.bin");
            Assert.Equal(expected.OrderBy(x => x), store.GetDirectoryNames(pattern).OrderBy(x => x));
        }
        finally
        {
            store.DeleteDirectory("qd-explicit");
            store.DeleteDirectory("qd-implied");
        }
    }

    [Fact]
    public void DeleteFile_Removes_And_Missing_Throws()
    {
        var store = Store<QueryE>();
        Seed(store, "gone.txt");
        store.DeleteFile("gone.txt");
        Assert.False(store.FileExists("gone.txt"));
        Assert.Throws<IsolatedStorageException>(() => store.DeleteFile("gone.txt"));
    }

    [Fact]
    public void DeleteDirectory_Removes_Tree()
    {
        var store = Store<QueryF>();
        Seed(store, "tree/a.txt", "tree/deep/b.txt", "other.txt");
        store.DeleteDirectory("tree");
        Assert.False(store.FileExists("tree/a.txt"));
        Assert.False(store.FileExists("tree/deep/b.txt"));
        Assert.True(store.FileExists("other.txt"));
        Assert.False(store.DirectoryExists("tree"));
    }
}
