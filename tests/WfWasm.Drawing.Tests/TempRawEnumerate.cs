extern alias WfDraw;

using System.Reflection;
using System.Runtime.ExceptionServices;
using Xunit;

// Raw name-table enumeration for v2 (DeserializingResourceReader-stamped)
// blobs: SafeApplyResources must list keys WITHOUT constructing any
// reader (one first-chance NSE per enumeration today via the BCL
// GetResourceSet fallback). Uses a hand-stamped modern blob; string +
// user-type + imagelist entries exercise every name shape.
public sealed class TempRawEnumerate
{
    private const string ModernReaderStamp = "System.Resources.Extensions.DeserializingResourceReader, System.Resources.Extensions, Version=4.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51";

    [Fact]
    public void Temp_RawEnumerate_Lists_All_Names_Without_FirstChance()
    {
        var asm = BuildStamped("Fake.Enum",
            ("w.Item1", null, null, "one"),
            ("w.Item2", null, new byte[] { 1, 2, 3 }, null),
            ("w.Stream", "System.Windows.Forms.ImageListStreamer, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", new byte[] { 0, 1, 0 }, null));
        var rm = new System.Resources.ResourceManager("Fake.Enum", asm);
        System.Collections.Generic.List<string> keys = new System.Collections.Generic.List<string>();
        int nse = 0;
        void Handler(object? s, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is System.NotSupportedException)
                System.Threading.Interlocked.Increment(ref nse);
        }
        System.AppDomain.CurrentDomain.FirstChanceException += Handler;
        try
        {
            foreach (string k in WfDraw::WfWasm.Drawing.ResourceHelper.EnumerateKeys(rm))
                keys.Add(k);
        }
        finally
        {
            System.AppDomain.CurrentDomain.FirstChanceException -= Handler;
        }
        System.IO.File.WriteAllLines(@"E:\tmp\rawenum.txt", keys);
        Assert.Contains("w.Item1", keys);
        Assert.Contains("w.Item2", keys);
        Assert.Contains("w.Stream", keys);
        Assert.Equal(0, nse);
    }

    private static Assembly BuildStamped(string baseName, params (string Name, string? UserType, byte[]? Data, string? Text)[] entries)
    {
        // Minimal modern writer: header, type table, hashes, positions,
        // names (7-bit byte len + UTF-16 + Int32 extra), sequential data.
        var types = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var e in entries)
            if (e.UserType is not null && !types.ContainsKey(e.UserType))
                types[e.UserType] = types.Count;
        var order = System.Linq.Enumerable.OrderBy(entries,
            e => ResourceHelperTests_Hash(e.Name)).ToArray();
        using var ms = new System.IO.MemoryStream();
        using (var w = new System.IO.BinaryWriter(ms))
        {
            w.Write(unchecked((int)0xBEEFCACE));
            w.Write(1);
            byte[] readerBytes;
            byte[] setBytes;
            // skip placeholder: patched after reader/set names are known.
            long skipPos = ms.Position;
            w.Write(0);
            using (var tmp = new System.IO.MemoryStream())
            {
                using (var tw = new System.IO.BinaryWriter(tmp))
                    tw.Write(ModernReaderStamp);
                readerBytes = tmp.ToArray();
            }
            using (var tmp = new System.IO.MemoryStream())
            {
                using (var tw = new System.IO.BinaryWriter(tmp))
                    tw.Write("System.Resources.Extensions.RuntimeResourceSet");
                setBytes = tmp.ToArray();
            }
            w.BaseStream.Write(readerBytes, 0, readerBytes.Length);
            w.BaseStream.Write(setBytes, 0, setBytes.Length);
            long afterNames = ms.Position;
            ms.Position = skipPos;
            w.Write(readerBytes.Length + setBytes.Length);
            ms.Position = afterNames;
            w.Write(2); // major, no minor
            w.Write(entries.Length);
            w.Write(types.Count);
            foreach (var kv in types)
                w.Write(kv.Key);
            while (ms.Position % 8 != 0)
                w.Write((byte)0);
            foreach (var e in order)
                w.Write(ResourceHelperTests_Hash(e.Name));
            long posPos = ms.Position;
            for (int i = 0; i < order.Length; i++)
                w.Write(0);
            long dsPos = ms.Position;
            w.Write(0);
            long namesStart = ms.Position;
            int[] dataOffsets = new int[order.Length];
            using (var enc = new System.IO.MemoryStream())
            {
                using (var dw = new System.IO.BinaryWriter(enc))
                {
                    for (int i = 0; i < order.Length; i++)
                    {
                        var e = order[i];
                        dataOffsets[i] = (int)enc.Position;
                        if (e.Text is not null)
                        {
                            dw.Write((byte)1);
                            dw.Write(e.Text);
                        }
                        else if (e.UserType is not null)
                        {
                            dw.Write((byte)(64 + types[e.UserType]));
                            dw.Write((byte)1); // fmt TypeConverterByteArray
                            dw.Write((byte)e.Data!.Length);
                            dw.BaseStream.Write(e.Data, 0, e.Data.Length);
                        }
                        else
                        {
                            dw.Write((byte)32);
                            dw.Write(e.Data!.Length);
                            dw.BaseStream.Write(e.Data, 0, e.Data.Length);
                        }
                    }
                }
                byte[] block = enc.ToArray();
                int[] npos = new int[order.Length];
                using (var nm = new System.IO.MemoryStream())
                {
                    using (var nw = new System.IO.BinaryWriter(nm))
                    {
                        for (int i = 0; i < order.Length; i++)
                        {
                            npos[i] = (int)(nm.Position);
                            byte[] utf16 = System.Text.Encoding.Unicode.GetBytes(order[i].Name);
                            uint v = (uint)utf16.Length;
                            while (v >= 0x80)
                            {
                                nw.Write((byte)(v | 0x80));
                                v >>= 7;
                            }
                            nw.Write((byte)v);
                            nw.BaseStream.Write(utf16, 0, utf16.Length);
                            nw.Write(dataOffsets[i]);
                        }
                    }
                    byte[] nameBlock = nm.ToArray();
                    // backfill positions (name offsets) and data offset
                    long endPos = ms.Position;
                    ms.Position = posPos;
                    foreach (int p in npos)
                        w.Write((int)(p));
                    ms.Position = dsPos;
                    w.Write((int)(namesStart + nameBlock.Length));
                    ms.Position = namesStart;
                    w.BaseStream.Write(nameBlock, 0, nameBlock.Length);
                    w.BaseStream.Write(block, 0, block.Length);
                    ms.Position = endPos + 0; // == namesStart+nameBlock+block
                }
            }
        }
        byte[] resources = ms.ToArray();
        var asmName = new Mono.Cecil.AssemblyNameDefinition("FakeEnum", new Version(1, 0, 0, 0));
        using var asm = Mono.Cecil.AssemblyDefinition.CreateAssembly(asmName, "FakeEnum", Mono.Cecil.ModuleKind.Dll);
        asm.MainModule.Resources.Add(new Mono.Cecil.EmbeddedResource(
            baseName + ".resources", Mono.Cecil.ManifestResourceAttributes.Public, new System.IO.MemoryStream(resources)));
        using var outMs = new System.IO.MemoryStream();
        asm.Write(outMs);
        return Assembly.Load(outMs.ToArray());
    }

    private static int ResourceHelperTests_Hash(string key)
    {
        unchecked
        {
            uint h = 5381;
            foreach (char c in key)
                h = ((h << 5) + h) ^ c;
            return (int)h;
        }
    }
}
