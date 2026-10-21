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

    // No shell drag-drop exists headless: DragAcceptFiles is a no-op leaf.
    // The upstream generic wrapper forwards to the PInvokeCore leaf; the
    // RegisterDragDrop source is not compiled into the fork, so without the
    // leaf the wrapper resolves back to itself and recurses to stack
    // overflow on every RichTextBox handle creation (UpdateOleCallback tail).
    [Fact]
    public void DragAcceptFiles_Generic_Wrapper_Does_Not_Recurse()
    {
        var window = new FakeWindow(default(HWND));
        Exception? fault = Record.Exception(() =>
            PInvokeCore.DragAcceptFiles(window, (BOOL)false));
        Assert.Null(fault);
    }

    private readonly struct FakeWindow(HWND handle) : IHandle<HWND>
    {
        public HWND Handle => handle;
    }
}
