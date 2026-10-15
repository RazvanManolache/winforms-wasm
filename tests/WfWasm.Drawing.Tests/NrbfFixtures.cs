using System.IO;

// Hand-rolled BinaryFormatter (MS-NRBF) payloads mimicking the netfx
// writer's layout for resource Bitmap/Icon values (BinaryFormatter
// itself is removed on .NET 9+, so fixtures are built by hand).
internal static class NrbfFixtures
{
    internal const string DrawingLib = "System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";

    // One class, single byte[] "Data" member, MemberReference indirection.
    internal static byte[] BitmapPayload(byte[] imageBytes)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)0);
            w.Write(1);
            w.Write(-1);
            w.Write(1);
            w.Write(0);
            w.Write((byte)12);
            w.Write(2);
            w.Write(DrawingLib);
            w.Write((byte)5);
            w.Write(1);
            w.Write("System.Drawing.Bitmap");
            w.Write(1);
            w.Write("Data");
            w.Write((byte)7);
            w.Write((byte)2);
            w.Write(2);
            w.Write((byte)9);
            w.Write(3);
            w.Write((byte)15);
            w.Write(3);
            w.Write(imageBytes.Length);
            w.Write((byte)2);
            w.Write(imageBytes);
            w.Write((byte)11);
        }
        return ms.ToArray();
    }

    // Two members: byte[] "IconData" plus an embedded
    // System.Drawing.Size class with two inline Int32s.
    internal static byte[] IconPayload(byte[] icoBytes)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)0);
            w.Write(1);
            w.Write(-1);
            w.Write(1);
            w.Write(0);
            w.Write((byte)12);
            w.Write(2);
            w.Write(DrawingLib);
            w.Write((byte)5);
            w.Write(1);
            w.Write("System.Drawing.Icon");
            w.Write(2);
            w.Write("IconData");
            w.Write("IconSize");
            w.Write((byte)7);
            w.Write((byte)4);
            w.Write((byte)2);
            w.Write("System.Drawing.Size");
            w.Write(2);
            w.Write(2);
            w.Write((byte)9);
            w.Write(3);
            w.Write((byte)5);
            w.Write(4);
            w.Write("System.Drawing.Size");
            w.Write(2);
            w.Write("X");
            w.Write("Y");
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((byte)8);
            w.Write((byte)8);
            w.Write(2);
            w.Write(32);
            w.Write(32);
            w.Write((byte)15);
            w.Write(3);
            w.Write(icoBytes.Length);
            w.Write((byte)2);
            w.Write(icoBytes);
            w.Write((byte)11);
        }
        return ms.ToArray();
    }

    internal static byte[] Bmp1x1(byte r, byte g, byte b)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)'B');
            w.Write((byte)'M');
            w.Write(58);
            w.Write(0);
            w.Write(54);
            w.Write(40);
            w.Write(1);
            w.Write(1);
            w.Write((short)1);
            w.Write((short)24);
            w.Write(0);
            w.Write(4);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(b);
            w.Write(g);
            w.Write(r);
            w.Write((byte)0);
        }
        return ms.ToArray();
    }

    internal static byte[] Ico1x1()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)1);
            w.Write((byte)1);
            w.Write((byte)1);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(48);
            w.Write(22);
            w.Write(40);
            w.Write(1);
            w.Write(2);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(0);
            w.Write(8);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(unchecked((int)0xFF112233));
            w.Write(0);
        }
        return ms.ToArray();
    }
}
