using System.Reflection;
using Xunit;

// TEMP-DAIG (DELETE AFTER RUN): enumerate the real ControlBars blob with the
// BCL v2 reader (via reflection, no new refs) for v2.8 ground truth.
public sealed class TempEnumerateBlob
{
    [Fact]
    public void Temp_Enumerate_ControlBars_Blob()
    {
        var asm = Assembly.LoadFrom(@"E:\tmp\controlbars-app.dll");
        string? resName = null;
        foreach (string n in asm.GetManifestResourceNames())
            if (n.EndsWith("ControlBars.Form1.resources"))
                resName = n;
        Assert.NotNull(resName);
        using var s = asm.GetManifestResourceStream(resName)!;
        var extAsm = Assembly.LoadFrom(
            @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.21\System.Resources.Extensions.dll");
        Type? rrType = extAsm.GetType(
            "System.Resources.Extensions.DeserializingResourceReader",
            throwOnError: true);
        object reader = Activator.CreateInstance(rrType!, s)!;
        var sb = new System.Text.StringBuilder();
        int nent = 0;
        var en = ((System.Collections.IEnumerable)reader).GetEnumerator();
        try
        {
            while (true)
            {
                bool moved;
                try { moved = en.MoveNext(); }
                catch (Exception ex)
                {
                    sb.AppendLine($"<MoveNext THROW:{ex.GetType().Name}:{ex.Message.Split('\n')[0]}>");
                    break;
                }
                if (!moved) break;
                nent++;
                System.Collections.DictionaryEntry de;
                try { de = (System.Collections.DictionaryEntry)en.Current!; }
                catch (Exception ex)
                {
                    sb.AppendLine($"<Current THROW:{ex.GetType().Name}:{ex.Message.Split('\n')[0]}>");
                    continue;
                }
                string key = (string)de.Key;
                object? v = null;
                string vtype = "<null>";
                int payload = -1;
                try
                {
                    v = de.Value;
                    if (v is not null)
                    {
                        vtype = v.GetType().FullName ?? "?";
                        if (v is byte[] bb) payload = bb.Length;
                    }
                }
                catch (Exception ex) { vtype = "THROW:" + ex.GetType().Name; }
                if (key.Contains("mageList") || nent <= 12 || vtype.StartsWith("THROW") || vtype.Contains("UnknownType"))
                    sb.AppendLine($"{key} :: {vtype} bytes={payload}");
            }
        }
        finally
        {
            sb.AppendLine($"TOTAL={nent}");
            System.IO.File.WriteAllText(@"E:\tmp\blob-enum.txt", sb.ToString());
        }
    }
}
