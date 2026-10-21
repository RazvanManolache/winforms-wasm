extern alias Fork;

using System.Globalization;
using System.Resources;
using ForkShim = Fork::System.Windows.Forms.WasmResourceShim;
using ForkGuard = Fork::System.Windows.Forms.WasmCtorGuard;

namespace WfWasm.WinForms.Tests;

// Never-throw string/stream resource access (Syncfusion silent OOB):
// raw BCL GetString/GetStream over v1-header streams that name
// DeserializingResourceReader threw NSE with no managed catch on the
// ctor path, and the WASM unwinder cannot cross the reflection-invoke
// frames. The shim resolves through the throw-free SafeGetObject and
// answers null for anything unresolvable.
public sealed class ResourceShimStringStreamTests
{
    [Fact]
    public void SafeGetString_Null_Manager_Answers_Null()
    {
        Assert.Null(ForkShim.SafeGetString(null, "k"));
        Assert.Null(ForkShim.SafeGetString(null, "k", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SafeGetStream_Null_Manager_Answers_Null()
    {
        Assert.Null(ForkShim.SafeGetStream(null, "k"));
        Assert.Null(ForkShim.SafeGetStream(null, "k", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SafeGetString_Missing_Set_Answers_Null_Without_Throw()
    {
        var rm = new ResourceManager("Missing.Base", typeof(object).Assembly);
        Assert.Null(ForkShim.SafeGetString(rm, "k"));
        Assert.Null(ForkShim.SafeGetString(rm, "k", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SafeGetStream_Missing_Set_Answers_Null_Without_Throw()
    {
        var rm = new ResourceManager("Missing.Base", typeof(object).Assembly);
        Assert.Null(ForkShim.SafeGetStream(rm, "k"));
        Assert.Null(ForkShim.SafeGetStream(rm, "k", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SafeGetResourceSet_Null_Or_NoCreate_Answers_Null()
    {
        Assert.Null(ForkShim.SafeGetResourceSet(null, CultureInfo.InvariantCulture, true, true));
        var rm = new ResourceManager("Missing.Base", typeof(object).Assembly);
        Assert.Null(ForkShim.SafeGetResourceSet(rm, CultureInfo.InvariantCulture, false, true));
    }

    [Fact]
    public void SafeGetResourceSet_Create_Answers_Working_Set()
    {
        var rm = new ResourceManager("Missing.Base", typeof(object).Assembly);
        var rs = ForkShim.SafeGetResourceSet(rm, CultureInfo.InvariantCulture, true, true);
        Assert.NotNull(rs);
        Assert.Null(rs!.GetString("anything-missing"));
        object? o = null;
        var ex = Record.Exception(() => o = rs.GetObject("anything-missing"));
        Assert.Null(ex);
        Assert.True(o is null || o.GetType().Name == "Bitmap");
    }

    [Fact]
    public void CtorGuard_Stash_Take_Keeps_First_And_Clears()
    {
        ForkGuard.TakeStashed();
        var first = new InvalidOperationException("first");
        ForkGuard.Stash(first);
        ForkGuard.Stash(new InvalidOperationException("second"));
        Assert.Same(first, ForkGuard.TakeStashed());
        Assert.Null(ForkGuard.TakeStashed());
        ForkGuard.Stash(null);
        Assert.Null(ForkGuard.TakeStashed());
    }
}
