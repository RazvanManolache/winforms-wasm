// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Variant;

namespace Windows.Win32.System.Com
{
    [Flags]
    internal enum ADVANCED_FEATURE_FLAGS : ushort
    {
        FADF_AUTO = 0x0001,
        FADF_STATIC = 0x0002,
        FADF_EMBEDDED = 0x0004,
        FADF_FIXEDSIZE = 0x0010,
        FADF_RECORD = 0x0020,
        FADF_HAVEIID = 0x0040,
        FADF_HAVEVARTYPE = 0x0080,
        FADF_BSTR = 0x0100,
        FADF_UNKNOWN = 0x0200,
        FADF_DISPATCH = 0x0400,
        FADF_VARIANT = 0x0800,
        FADF_RESERVED = 0xF008,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SAFEARRAYBOUND
    {
        public uint cElements;
        public int lLbound;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe partial struct SAFEARRAY
    {
        public ushort cDims;
        public ADVANCED_FEATURE_FLAGS fFeatures;
        public uint cbElements;
        public uint cLocks;
        public void* pvData;
        public SAFEARRAYBOUND rgsabound;
    }
}

namespace Windows.Win32
{
    using Windows.Win32.System.Com;

    internal static partial class PInvokeCore
    {
        public static unsafe SAFEARRAY* SafeArrayCreate(VARENUM vt, uint cDims, SAFEARRAYBOUND* rgsabound) => null;
        public static unsafe HRESULT SafeArrayGetVartype(SAFEARRAY* psa, VARENUM* pvt) => HRESULT.E_NOTIMPL;
    }
}
