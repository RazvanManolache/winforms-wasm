// Fork-owned: FDEX_PROP_FLAGS for Windows.Win32.System.Ole.

namespace Windows.Win32.System.Ole;

[Flags]
internal enum FDEX_PROP_FLAGS : uint
{
    fdexPropCanGet = 0x00000001,
    fdexPropCannotGet = 0x00000002,
    fdexPropCanPut = 0x00000004,
    fdexPropCannotPut = 0x00000008,
    fdexPropCanPutRef = 0x00000010,
    fdexPropCannotPutRef = 0x00000020,
    fdexPropNoSideEffects = 0x00000040,
    fdexPropDynamicType = 0x00000080,
    fdexPropCanCall = 0x00000100,
    fdexPropCannotCall = 0x00000200,
    fdexPropCanConstruct = 0x00000400,
    fdexPropCannotConstruct = 0x00000800,
    fdexPropCanSourceEvents = 0x00001000,
    fdexPropCannotSourceEvents = 0x00002000
}
