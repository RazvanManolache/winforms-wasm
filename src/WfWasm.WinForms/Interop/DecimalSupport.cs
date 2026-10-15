// Fork-owned: DECIMAL layout completion for Windows.Win32.Foundation.

using System.Runtime.InteropServices;

namespace Windows.Win32.Foundation;

internal partial struct DECIMAL
{
    public ushort wReserved;
    public _Anonymous1_e__Union Anonymous1;
    public uint Hi32;
    public _Anonymous2_e__Union Anonymous2;

    [StructLayout(LayoutKind.Explicit)]
    public partial struct _Anonymous1_e__Union
    {
        [FieldOffset(0)]
        public _Anonymous_e__Struct Anonymous;

        public partial struct _Anonymous_e__Struct
        {
            public byte scale;
            public byte sign;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public partial struct _Anonymous2_e__Union
    {
        [FieldOffset(0)]
        public _Anonymous_e__Struct Anonymous;

        [FieldOffset(0)]
        public ulong Lo64;

        public partial struct _Anonymous_e__Struct
        {
            public uint Lo32;
            public uint Mid32;
        }
    }
}
