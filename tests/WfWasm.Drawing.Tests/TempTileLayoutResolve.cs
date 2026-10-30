extern alias WfDraw;

using System.Reflection;
using Xunit;

// REGRESSION (tile-layout Form1 ctor NRE): imageStreamerN.Images entries
// are BinaryFormatter-serialized List<System.Drawing.Image> under an
// UnknownType slot (Syncfusion component model, NOT WinForms MSFt).
// SafeGetObject recovers the healthy empty list; the 15 live entries
// are identical 367-byte placeholders (no real bitmap fits).
public sealed class TempTileLayoutResolve
{
    [Fact]
    public void Temp_TileLayout_ImageStreamers_ResolveToEmptyLists()
    {
        var asm = Assembly.LoadFrom(@"E:\tmp\wfwasm-inputs\syncfusion-winforms-demos\layout\Tile layout\TileLayout\CS\obj\Debug\net9.0-windows\TileLayoutDemo_2008.dll");
        var rm = new System.Resources.ResourceManager("TileLayoutDemo_2008.Form1", asm);
        var lines = new System.Collections.Generic.List<string>();
        foreach (string key in new[] { "imageStreamer1.Images", "imageStreamer2.Images", "imageStreamer9.Images" })
        {
            object? value = WfDraw::WfWasm.Drawing.ResourceHelper.SafeGetObject(rm, key);
            var list = value as System.Collections.Generic.List<WfDraw::System.Drawing.Image>;
            lines.Add($"{key} type={(value is null ? "null" : value.GetType().FullName)} count={(list is null ? -1 : list.Count)}");
            Assert.NotNull(value);
            Assert.NotNull(list);
            Assert.Empty(list);
        }
        System.IO.File.WriteAllLines(@"E:\tmp\tilelayout-resolve.txt", lines);
    }
}
