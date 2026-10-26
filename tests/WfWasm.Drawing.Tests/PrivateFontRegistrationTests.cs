extern alias WfDraw;

using System;
using System.Runtime.InteropServices;
using WfPrivateFonts = WfDraw::System.Drawing.Text.PrivateFontCollection;
using Xunit;

namespace WfWasm.Drawing.Tests;

// Private-font byte registry (browser FontFace bridge for vendor icon
// fonts): AddMemoryFont must record (parsed family name -> TTF bytes) so
// BrowserText can register the exact bytes when text first uses the family.
// Uses a hand-built minimal name table — no vendor binaries in the repo.
public sealed class PrivateFontRegistrationTests
{
    private static byte[] BuildNameTableTtf(string family)
    {
        byte[] nameBytes = System.Text.Encoding.BigEndianUnicode.GetBytes(family);
        // offset table (12) + 1 table record (16) + name header (6) + 1 record (12) + string
        byte[] buf = new byte[12 + 16 + 6 + 12 + nameBytes.Length];
        void U16(int o, int v) { buf[o] = (byte)(v >> 8); buf[o + 1] = (byte)v; }
        void U32(int o, int v) { buf[o] = (byte)(v >> 24); buf[o + 1] = (byte)(v >> 16); buf[o + 2] = (byte)(v >> 8); buf[o + 3] = (byte)v; }
        // sfnt version 1.0, 1 table
        U32(0, 0x00010000); U16(4, 1);
        // 'name' record at offset 28
        buf[12] = (byte)'n'; buf[13] = (byte)'a'; buf[14] = (byte)'m'; buf[15] = (byte)'e';
        U32(12 + 8, 28); U32(12 + 12, 6 + 12 + nameBytes.Length);
        // name table: format 0, 1 record, storage offset 18
        U16(28, 0); U16(30, 1); U16(32, 18);
        // record: platform 3 (Windows), encoding 1 (UCS-2), lang 0x0409, nameID 1 (family)
        U16(34, 3); U16(36, 1); U16(38, 0x0409); U16(40, 1);
        U16(42, nameBytes.Length); U16(44, 0);
        Buffer.BlockCopy(nameBytes, 0, buf, 46, nameBytes.Length);
        return buf;
    }

    private static void AddBytes(WfPrivateFonts pfc, byte[] bytes)
    {
        IntPtr mem = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, mem, bytes.Length);
            pfc.AddMemoryFont(mem, bytes.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }

    [Fact]
    public void AddMemoryFont_Records_Family_And_Bytes()
    {
        const string family = "TestPrivateIconsWF";
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, BuildNameTableTtf(family));
        Assert.Contains(pfc.Families, f => f.Name == family);
        Assert.True(WfDraw::System.Drawing.Text.PrivateFontCollection.TryGetPrivateFontBytes(family, out byte[]? got));
        Assert.NotNull(got);
        Assert.Equal(BuildNameTableTtf(family), got);
    }

    [Fact]
    public void AddMemoryFont_Garbage_Records_Nothing()
    {
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, new byte[64]);
        Assert.Empty(pfc.Families);
        Assert.False(WfDraw::System.Drawing.Text.PrivateFontCollection.TryGetPrivateFontBytes("NoSuchFamilyXYZ", out _));
    }

    [Fact]
    public void AddMemoryFont_MacRoman_Name_Parses()
    {
        // platform 1 / encoding 0 / lang 0 Mac Roman family record
        byte[] nameBytes = System.Text.Encoding.Latin1.GetBytes("TestMacIcons");
        byte[] buf = new byte[12 + 16 + 6 + 12 + nameBytes.Length];
        void U16(int o, int v) { buf[o] = (byte)(v >> 8); buf[o + 1] = (byte)v; }
        void U32(int o, int v) { buf[o] = (byte)(v >> 24); buf[o + 1] = (byte)(v >> 16); buf[o + 2] = (byte)(v >> 8); buf[o + 3] = (byte)v; }
        U32(0, 0x00010000); U16(4, 1);
        buf[12] = (byte)'n'; buf[13] = (byte)'a'; buf[14] = (byte)'m'; buf[15] = (byte)'e';
        U32(20, 28); U32(24, 6 + 12 + nameBytes.Length);
        U16(28, 0); U16(30, 1); U16(32, 18);
        U16(34, 1); U16(36, 0); U16(38, 0); U16(40, 1);
        U16(42, nameBytes.Length); U16(44, 0);
        Buffer.BlockCopy(nameBytes, 0, buf, 46, nameBytes.Length);
        using var pfc = new WfPrivateFonts();
        AddBytes(pfc, buf);
        Assert.Contains(pfc.Families, f => f.Name == "TestMacIcons");
    }
}
