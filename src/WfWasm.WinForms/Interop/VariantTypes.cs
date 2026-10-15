// Fork-owned: VARENUM, VARIANT_BOOL, and VARIANT inner structs.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace Windows.Win32.System.Variant
{
[Flags]
internal enum VARENUM : ushort
{
    VT_EMPTY = 0,
    VT_NULL = 1,
    VT_I2 = 2,
    VT_I4 = 3,
    VT_R4 = 4,
    VT_R8 = 5,
    VT_CY = 6,
    VT_DATE = 7,
    VT_BSTR = 8,
    VT_DISPATCH = 9,
    VT_ERROR = 10,
    VT_BOOL = 11,
    VT_VARIANT = 12,
    VT_UNKNOWN = 13,
    VT_DECIMAL = 14,
    VT_I1 = 16,
    VT_UI1 = 17,
    VT_UI2 = 18,
    VT_UI4 = 19,
    VT_I8 = 20,
    VT_UI8 = 21,
    VT_INT = 22,
    VT_UINT = 23,
    VT_VOID = 24,
    VT_HRESULT = 25,
    VT_PTR = 26,
    VT_SAFEARRAY = 27,
    VT_CARRAY = 28,
    VT_USERDEFINED = 29,
    VT_LPSTR = 30,
    VT_LPWSTR = 31,
    VT_RECORD = 36,
    VT_INT_PTR = 37,
    VT_UINT_PTR = 38,
    VT_FILETIME = 64,
    VT_BLOB = 65,
    VT_STREAM = 66,
    VT_STORAGE = 67,
    VT_STREAMED_OBJECT = 68,
    VT_STORED_OBJECT = 69,
    VT_BLOB_OBJECT = 70,
    VT_CF = 71,
    VT_CLSID = 72,
    VT_VERSIONED_STREAM = 73,
    VT_BSTR_BLOB = 0x0fff,
    VT_VECTOR = 0x1000,
    VT_ARRAY = 0x2000,
    VT_BYREF = 0x4000,
    VT_RESERVED = 0x8000,
    VT_ILLEGAL = 0xffff,
    VT_ILLEGALMASKED = 0x0fff,
    VT_TYPEMASK = 0x0fff
}
}

namespace Windows.Win32.Foundation
{
    internal readonly struct VARIANT_BOOL : IEquatable<VARIANT_BOOL>
    {
        public readonly short Value;
        public VARIANT_BOOL(short value) => Value = value;
        public static readonly VARIANT_BOOL VARIANT_TRUE = new(-1);
        public static readonly VARIANT_BOOL VARIANT_FALSE = new(0);
        public static implicit operator bool(VARIANT_BOOL value) => value.Value != 0;
        public static implicit operator VARIANT_BOOL(bool value) => value ? VARIANT_TRUE : VARIANT_FALSE;
        public static explicit operator VARIANT_BOOL(short value) => new(value);
        public static explicit operator short(VARIANT_BOOL value) => value.Value;
        public bool Equals(VARIANT_BOOL other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is VARIANT_BOOL other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(VARIANT_BOOL left, VARIANT_BOOL right) => left.Value == right.Value;
        public static bool operator !=(VARIANT_BOOL left, VARIANT_BOOL right) => left.Value != right.Value;
    }
}

namespace Windows.Win32.System.Variant
{

internal unsafe partial struct VARIANT
{
    public _Anonymous_e__Union Anonymous;

    [StructLayout(LayoutKind.Explicit)]
    internal partial struct _Anonymous_e__Union
    {
        [FieldOffset(0)]
        public DECIMAL decVal;

        [FieldOffset(0)]
        public _Anonymous_e__Struct Anonymous;

        internal partial struct _Anonymous_e__Struct
        {
            public VARENUM vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public _Anonymous_e__Union Anonymous;

            [StructLayout(LayoutKind.Explicit)]
            internal partial struct _Anonymous_e__Union
            {
                [FieldOffset(0)] public sbyte cVal;
                [FieldOffset(0)] public byte bVal;
                [FieldOffset(0)] public short iVal;
                [FieldOffset(0)] public ushort uiVal;
                [FieldOffset(0)] public int lVal;
                [FieldOffset(0)] public int intVal;
                [FieldOffset(0)] public uint ulVal;
                [FieldOffset(0)] public uint uintVal;
                [FieldOffset(0)] public long llVal;
                [FieldOffset(0)] public ulong ullVal;
                [FieldOffset(0)] public float fltVal;
                [FieldOffset(0)] public double dblVal;
                [FieldOffset(0)] public VARIANT_BOOL boolVal;
                [FieldOffset(0)] public int scode;
                [FieldOffset(0)] public CY cyVal;
                [FieldOffset(0)] public double date;
                [FieldOffset(0)] public BSTR bstrVal;
                [FieldOffset(0)] public IUnknown* punkVal;
                [FieldOffset(0)] public IDispatch* pdispVal;
                [FieldOffset(0)] public SAFEARRAY* parray;
                [FieldOffset(0)] public void* byref;

                public unsafe partial struct _Anonymous_e__Struct
                {
                    public void* pvRecord;
                    public void* pRecInfo;
                }
            }
        }
    }
}
}
