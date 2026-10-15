// Fork-owned: CsWin32 generates these markers; the fork provides managed ones.
// Mirrors dotnet/winforms ComHelpers/ComScope contracts (managed backend:
// there are no real COM objects, so queries fail and pointers stay null).

namespace Windows.Win32;

/// <summary>COM interface identity (cf. generated IComIID).</summary>
internal interface IComIID
{
    static abstract ref readonly Guid Guid { get; }
}

/// <summary>Vtable population hook (cf. CsWin32's generated IVTable).</summary>
internal unsafe interface IVTable
{
    static virtual void* VTable => null;
}

/// <summary>Vtable population hook (cf. CsWin32's generated IVTable). </summary>
internal unsafe interface IVTable<TSelf, TVtbl> : IVTable
{
    static abstract void PopulateVTable(TVtbl* vtable);
}

/// <summary>Real IUnknown lives in Windows.Win32.System.Com (hand-written).</summary>
