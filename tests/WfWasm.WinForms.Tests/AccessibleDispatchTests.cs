namespace WfWasm.WinForms.Tests;

// Headless accessibility: oleacc's typelib can't load in the browser, so
// the upstream AccessibleDispatch static TypeInfo initializer threw and
// poisoned every AccessibleObject constructor (menu/dropdown opens died
// creating bar-item accessible objects). The fork ships an inert dispatch
// (AccessibleDispatch.Wasm.cs): construction and the managed surface must
// work with no COM involved.
//
// NOTE: the poison itself only manifests headless (desktop oleacc loads
// fine), so this locks the managed-surface contract here; the true
// regression oracle is gallery case_mdi (File menu opens clean in the
// browser, creating vendor accessible objects with zero first-chance
// TypeInitializationExceptions).
public sealed class AccessibleDispatchTests
{
    [Fact]
    public void AccessibleObject_Constructs_Without_Com()
    {
        var first = new System.Windows.Forms.AccessibleObject();
        var second = new System.Windows.Forms.AccessibleObject();
        Assert.NotSame(first, second);
    }

    [Fact]
    public void AccessibleObject_Managed_Surface_Works_Without_Owner()
    {
        // These managed getters must not need COM; the dispatch adapter
        // they sit beside is inert headless.
        var ao = new System.Windows.Forms.AccessibleObject();
        Assert.Null(ao.Name);
        Assert.Equal(System.Windows.Forms.AccessibleRole.None, ao.Role);
        Assert.Equal("", ao.Value);
    }
}
