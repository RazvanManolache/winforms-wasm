using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace WfWasm.WinForms.Tests;

// Focused coverage for the fork-owned managed interop (no real COM on the
// canvas backend: queries fail with E_NOINTERFACE, pointers stay null).
public sealed unsafe class ComInfrastructureTests
{
    internal readonly struct FakeObject : IComIID
    {
        private static readonly Guid s_iid = new("11111111-2222-3333-4444-555555555555");

        public static ref readonly Guid Guid => ref s_iid;
    }

    [Fact]
    public void TryQuery_Reports_NoInterface_With_Null_Scope()
    {
        ComScope<FakeObject> scope = default;
        ComScope<FakeObject> queried = scope.TryQuery<FakeObject>(out HRESULT hr);
        Assert.Equal(unchecked((HRESULT)0x80004002), hr);
        Assert.True(queried.IsNull);
    }

    [Fact]
    public void ComPointers_Are_Null_Without_Backend()
    {
        Assert.Throws<InvalidCastException>(() => ComHelpers.GetComPointer<FakeObject>(new object()));
        Assert.True(ComHelpers.TryGetComPointer<FakeObject>(new object(), out HRESULT hr) is null);
        Assert.Equal(unchecked((HRESULT)0x80004002), hr);
        Assert.Throws<ArgumentNullException>(() => ComHelpers.GetObjectForIUnknown(null));
    }

    [Fact]
    public void Scopes_Dispose_Cleanly()
    {
        Assert.Throws<InvalidCastException>(() => ComHelpers.GetComScope<FakeObject>(new object()));
        using ComScope<FakeObject> scope = ComHelpers.TryGetComScope<FakeObject>(new object());
        Assert.True(scope.IsNull);
    }
}
