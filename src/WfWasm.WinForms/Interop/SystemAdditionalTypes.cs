// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.Power
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}

namespace Windows.Win32.System.Diagnostics.Debug
{
    internal enum FACILITY_CODE
    {
        FACILITY_NULL = 0,
        FACILITY_RPC = 1,
        FACILITY_DISPATCH = 2,
        FACILITY_STORAGE = 3,
        FACILITY_ITF = 4,
        FACILITY_WIN32 = 7,
        FACILITY_WINDOWS = 8,
        FACILITY_SECURITY = 9,
        FACILITY_SSPI = 9,
        FACILITY_CONTROL = 10,
        FACILITY_CERT = 11,
        FACILITY_INTERNET = 12,
        FACILITY_MEDIASERVER = 13,
        FACILITY_MSMQ = 14,
        FACILITY_SETUPAPI = 15,
        FACILITY_SCARD = 16,
        FACILITY_COMPLUS = 17,
        FACILITY_AAF = 18,
        FACILITY_URT = 19,
        FACILITY_ACS = 20,
        FACILITY_DPLAY = 21,
        FACILITY_UMI = 22,
        FACILITY_SXS = 23,
        FACILITY_WINDOWS_CE = 24,
        FACILITY_HTTP = 25,
        FACILITY_USERMODE_COMMONLOG = 26,
        FACILITY_WER = 27,
        FACILITY_DIRECTMUSIC = 30,
        FACILITY_DIRECT3D = 31,
        FACILITY_DIRECTDRAW = 32,
        FACILITY_DIRECTSHOW = 33,
        FACILITY_DOT3 = 34,
        FACILITY_XP = 35,
        FACILITY_DIRECTINPUT = 36,
    }
}

namespace Windows.Win32.System.Memory
{
    [Flags]
    internal enum GLOBAL_ALLOC_FLAGS : uint
    {
        GMEM_FIXED = 0x0000,
        GMEM_MOVEABLE = 0x0002,
        GMEM_NOCOMPACT = 0x0010,
        GMEM_NODISCARD = 0x0020,
        GMEM_ZEROINIT = 0x0040,
        GMEM_MODIFY = 0x0080,
        GMEM_DISCARDABLE = 0x0100,
        GMEM_NOT_BANKED = 0x1000,
        GMEM_SHARE = 0x2000,
        GMEM_DDESHARE = 0x2000,
        GMEM_INVALID_HANDLE = 0x8000,
        GHND = 0x0042,
        GPTR = 0x0040,
    }
}

namespace Windows.Win32.System.ApplicationInstallationAndServicing
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ACTCTXW
    {
        public uint cbSize;
        public uint dwFlags;
        public PCWSTR lpSource;
        public ushort wProcessorArchitecture;
        public ushort wLangId;
        public PCWSTR lpAssemblyDirectory;
        public PCWSTR lpResourceName;
        public PCWSTR lpApplicationName;
        public HMODULE hModule;
    }
}

namespace Windows.Win32.System.LibraryLoader
{
    [Flags]
    internal enum LOAD_LIBRARY_FLAGS : uint
    {
        DONT_RESOLVE_DLL_REFERENCES = 0x00000001,
        LOAD_LIBRARY_AS_DATAFILE = 0x00000002,
        LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008,
        LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x00000010,
        LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020,
        LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040,
        LOAD_LIBRARY_REQUIRE_SIGNED_TARGET = 0x00000080,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100,
        LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200,
        LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400,
        LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800,
        LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,
        LOAD_LIBRARY_SAFE_CURRENT_DIRS = 0x00002000,
        LOAD_LIBRARY_SEARCH_SYSTEM32_NO_FORWARDER = 0x00004000,
    }
}

namespace Windows.Win32.System.StationsAndDesktops
{
    internal readonly unsafe partial struct HWINSTA : IHandle<HWINSTA>, IEquatable<HWINSTA>
    {
        public readonly nint Value;
        public HWINSTA(nint value) => Value = value;
        public HWINSTA Handle => this;
        public static explicit operator HWINSTA(nint value) => new(value);
        public static implicit operator nint(HWINSTA value) => value.Value;
        public static explicit operator HWINSTA(void* value) => new((nint)value);
        public static implicit operator void*(HWINSTA value) => (void*)value.Value;
        public static readonly HWINSTA Null = default;
        public bool IsNull => Value == 0;
        public bool Equals(HWINSTA other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is HWINSTA other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(HWINSTA left, HWINSTA right) => left.Value == right.Value;
        public static bool operator !=(HWINSTA left, HWINSTA right) => !left.Equals(right);
    }
}

namespace Windows.Win32.System.Threading { }
namespace Windows.Win32.System.DataExchange { }
namespace Windows.Win32.System.WinRT { }

namespace Windows.Win32.Media.Audio
{
    [Guid("00000016-0000-0000-C000-000000000046")]
    internal unsafe partial struct IMessageFilter : IComIID, IVTable<IMessageFilter, IMessageFilter.Vtbl>
    {
        private static readonly Guid s_guid = new("00000016-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface { }
    }
}

namespace SourceGenerated
{
    internal static class EnumValidator
    {
        public static void Validate<T>(T value, string? paramName = null) { }
        public static void Validate<T>(T value, string? paramName, Type enumType) { }
    }
}

namespace Windows.Win32
{
    internal static partial class PInvoke
    {
        public static BOOL GetSystemPowerStatus(out System.Power.SYSTEM_POWER_STATUS lpSystemPowerStatus)
        {
            lpSystemPowerStatus = default;
            return (BOOL)true;
        }

        public const uint ACTCTX_FLAG_RESOURCE_NAME_VALID = 0x00000008;
        public const uint ACTCTX_FLAG_HMODULE_VALID = 0x00000080;

        public static unsafe HANDLE CreateActCtx(System.ApplicationInstallationAndServicing.ACTCTXW* pActCtx) => (HANDLE)1;
        public static unsafe BOOL GetCurrentActCtx(HANDLE* phActCtx) { if (phActCtx != null) *phActCtx = (HANDLE)1; return (BOOL)true; }
        public static unsafe BOOL ActivateActCtx(HANDLE hActCtx, nuint* lpCookie) { if (lpCookie != null) *lpCookie = 1; return (BOOL)true; }
        public static BOOL DeactivateActCtx(uint dwFlags, nuint ulCookie) => (BOOL)true;

        public static HINSTANCE LoadLibraryEx(string lpLibFileName, System.LibraryLoader.LOAD_LIBRARY_FLAGS dwFlags) => (HINSTANCE)1;
        public static HINSTANCE GetModuleHandle(string lpModuleName) => (HINSTANCE)1;
        public static unsafe HINSTANCE GetModuleHandle(PCWSTR lpModuleName) => (HINSTANCE)1;
    }

    internal static partial class PInvokeCore
    {
        public static HGLOBAL GlobalAlloc(System.Memory.GLOBAL_ALLOC_FLAGS uFlags, nuint dwBytes) => HGLOBAL.Null;
        public static HGLOBAL GlobalFree(HGLOBAL hMem) => HGLOBAL.Null;
    }
}

namespace Windows.Win32.System.Registry
{
    internal readonly struct HKEY : IEquatable<HKEY>
    {
        public readonly nint Value;
        public HKEY(nint value) => Value = value;
        public static HKEY Null => default;
        public bool IsNull => Value == 0;
        public static implicit operator nint(HKEY value) => value.Value;
        public static explicit operator HKEY(nint value) => new(value);
        public static bool operator ==(HKEY left, HKEY right) => left.Value == right.Value;
        public static bool operator !=(HKEY left, HKEY right) => left.Value != right.Value;
        public override bool Equals(object? obj) => obj is HKEY other && Equals(other);
        public bool Equals(HKEY other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }
}

namespace Windows.Win32.System.WinRT
{
    using Windows.Win32.System.Com;

    internal readonly struct HSTRING : IEquatable<HSTRING>
    {
        public readonly nint Value;
        public HSTRING(nint value) => Value = value;
        public static implicit operator nint(HSTRING value) => value.Value;
        public static explicit operator HSTRING(nint value) => new(value);
        public static bool operator ==(HSTRING left, HSTRING right) => left.Value == right.Value;
        public static bool operator !=(HSTRING left, HSTRING right) => left.Value != right.Value;
        public override bool Equals(object? obj) => obj is HSTRING other && Equals(other);
        public bool Equals(HSTRING other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    [Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90")]
    internal unsafe partial struct IInspectable : IComIID, IVTable<IInspectable, IInspectable.Vtbl>
    {
        private static readonly Guid s_guid = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetIids(uint* iidCount, Guid** iids) => HRESULT.E_NOTIMPL;
        public HRESULT GetRuntimeClassName(HSTRING* className) => HRESULT.E_NOTIMPL;
        public HRESULT GetTrustLevel(void* trustLevel) => HRESULT.E_NOTIMPL;
        public interface Interface { }
    }
}
