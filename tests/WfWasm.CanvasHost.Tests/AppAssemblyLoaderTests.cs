using System.Reflection;
using WfWasm.CanvasHost;

public sealed class AppAssemblyLoaderTests
{
    private static Assembly? Resolve(string assemblyName)
    {
        var m = typeof(AppAssemblyLoader).GetMethod("ResolveRequestedAssembly",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        return (Assembly?)m.Invoke(null, [null, new ResolveEventArgs(assemblyName)]);
    }

    [Fact]
    public void Resolve_FrameworkScopes_Unify_With_Loaded_Fork()
    {
        // Verbatim assemblies keep framework scopes (System.Windows.Forms
        // from Legacy/WPF); the hook must bind those to the already-loaded
        // fork/drawing instead of fetching a duplicate multi-MB rewrite.
        Assembly forms = typeof(System.Windows.Forms.Form).Assembly;
        Assert.Same(forms, Resolve("System.Windows.Forms, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"));
        Assert.Same(forms, Resolve("System.Windows.Forms.Primitives, Version=9.0.0.0"));
        Assembly drawing = typeof(System.Drawing.Bitmap).Assembly;
        Assert.Same(drawing, Resolve("System.Drawing.Common, Version=9.0.0.0"));
        Assert.Same(drawing, Resolve("System.Drawing, Version=9.0.0.0"));
    }

    [Fact]
    public void Resolve_SameName_Ignores_Version()
    {
        // One copy per name even across major versions: a netfx v4 ref
        // (Telerik UIA -> WindowsBase v4) binds the loaded v9 instead of
        // fetching a twin that would split type identity (Rect != Rect
        // -> "get_BoundingRectangle ... does not have an implementation").
        Assembly json = typeof(System.Text.Json.JsonSerializer).Assembly;
        Assert.Same(json, Resolve("System.Text.Json, Version=6.0.0.0"));
        Assembly linq = typeof(System.Linq.Enumerable).Assembly;
        Assert.Same(linq, Resolve("System.Linq, Version=1.0.0.0"));
    }

    [Fact]
    public void Resolve_ThrowingLoad_Returns_Null_InsteadOfPropagating()
    {
        // An exception out of an AssemblyResolve handler kills the Mono
        // runtime (exit 1): PDN's WPF AssemblyHelper.OnLoaded throws NIE
        // on ReflectionOnly during Load. The hook must convert a throwing
        // load into a catchable miss (null) instead of propagating.
        var bytesField = typeof(AppAssemblyLoader).GetField("Bytes",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(bytesField);
        var dict = (System.Collections.IDictionary)bytesField.GetValue(null)!;
        dict["Throwing.Load.Probe"] = new byte[] { 0x00, 0x01, 0x02 };
        try
        {
            Assert.Null(Resolve("Throwing.Load.Probe, Version=1.0.0.0"));
        }
        finally
        {
            dict.Remove("Throwing.Load.Probe");
        }
    }

    [Fact]
    public void Resolve_UnknownName_Returns_Null_Headless()
    {
        // No active app and no browser fetch headless: unknown names resolve null.
        Assert.Null(Resolve("Definitely.Not.Present.Assembly, Version=1.0.0.0"));
    }
}
