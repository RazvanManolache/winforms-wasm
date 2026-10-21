extern alias WfDraw;

using System.IO;
using System.Resources;
using WfResourceHelper = WfDraw::WfWasm.Drawing.ResourceHelper;
using Xunit;

// Reader selection for .resources streams (Syncfusion/SfCalendar silent
// OOB): vendor streams pair a v1 version field with the
// DeserializingResourceReader name, which throws NSE out of the legacy
// ResourceReader. OpenResourceReader must honor the header-named reader
// when it resolves and keep the legacy reader otherwise.
public sealed class ResourceReaderSelectionTests
{
    private sealed class ProbeReader : IResourceReader
    {
        public ProbeReader(Stream stream)
        {
        }

        public System.Collections.IDictionaryEnumerator GetEnumerator() =>
            new System.Collections.Hashtable().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }

    private static MemoryStream LegacyStream()
    {
        var ms = new MemoryStream();
        using (var w = new ResourceWriter(ms))
        {
            w.AddResource("k", "v");
            w.Generate();
            ms = new MemoryStream(ms.ToArray());
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream NamedStream(string readerAssemblyQualifiedName)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(unchecked((int)0xBEEFCACE));
            w.Write(1);
            w.Write(readerAssemblyQualifiedName);
        }

        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void OpenResourceReader_Legacy_Header_Returns_ResourceReader()
    {
        using var ms = LegacyStream();
        using var reader = WfResourceHelper.OpenResourceReader(ms);
        Assert.IsType<ResourceReader>(reader);
    }

    [Fact]
    public void OpenResourceReader_Named_Header_Instantiates_Named_Reader()
    {
        using var ms = NamedStream(typeof(ProbeReader).AssemblyQualifiedName!);
        using var reader = WfResourceHelper.OpenResourceReader(ms);
        Assert.IsType<ProbeReader>(reader);
    }

    [Fact]
    public void OpenResourceReader_Garbage_Answers_Null()
    {
        using var ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert.Null(WfResourceHelper.OpenResourceReader(ms));
    }

    [Fact]
    public void OpenResourceReader_Unresolvable_Name_Answers_Null()
    {
        using var ms = NamedStream("Nope.Missing, Nope");
        Assert.Null(WfResourceHelper.OpenResourceReader(ms));
    }

    private sealed class PrefixedSetManager : ResourceManager
    {
        private readonly ResourceSet _set;

        public PrefixedSetManager(ResourceSet set)
        {
            _set = set;
        }

        public override ResourceSet? GetResourceSet(
            System.Globalization.CultureInfo culture,
            bool createIfNotExists,
            bool tryParents) => _set;
    }

    [Fact]
    public void EnumerateKeys_Falls_Back_To_Bcl_ResourceSet_When_Raw_Streams_Yield_Nothing()
    {
        // Vendor pattern (managed calendar daycontainer): the embedded
        // blob names a reader no directly constructible path takes, so
        // raw-stream enumeration yields zero keys and every downstream
        // apply degrades to defaults (container stuck at its 200x100
        // default, day clicks hit=null). The BCL ResourceManager
        // resolves such blobs tolerantly (134 keys off the vendor
        // calendar blob), so enumeration falls back to its set.
        byte[] blob;
        using (var tmp = new MemoryStream())
        {
            using (var w = new ResourceWriter(tmp))
            {
                w.AddResource("daycontainer.Size", "1104, 711");
                w.AddResource("other", "x");
                w.Generate();
            }
            blob = tmp.ToArray();
        }
        using var ms = new MemoryStream(blob, writable: false);
        using var set = new ResourceSet(ms);
        var rm = new PrefixedSetManager(set);
        var keys = WfResourceHelper.EnumerateKeys(rm);
        Assert.Contains("daycontainer.Size", keys);
    }
}
