using WfWasm.Host.Server;

namespace WfWasm.Host.Server.Tests;

public sealed class AppTransformTests
{
    [Fact]
    public void Pristine_Assemblies_Serve_Byte_Identical()
    {
        // Assemblies the rewriter must not touch (Mono rejects
        // Cecil-processed metadata-only reference assemblies with
        // BadImageFormatException at Assembly.Load) serve pristine.
        // Regression: telerik-photo form-create failed loading
        // UIAutomationProvider even though its bytes were fetched.
        const string app = "telerik-photo";
        const string name = "UIAutomationProvider.dll";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        AppTransform.AppSpec spec = maybe!;
        string pristinePath = spec.SourceOverride.TryGetValue(name, out string? alt)
            ? alt
            : Path.Combine(spec.Folder, name);
        Assert.True(File.Exists(pristinePath), $"pristine missing: {pristinePath}");
        // Drop any cached transform of this file (an older revision may
        // have rewritten it): the cache is regenerable, the server and
        // later test runs rebuild it on demand.
        try
        {
            string dir = Path.Combine(AppTransform.CacheRoot, app);
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir, "*" + Path.GetFileNameWithoutExtension(name) + "*"))
                    File.Delete(f);
            }
        }
        catch { }
        byte[] served = AppTransform.TransformOrCached(app, name);
        Assert.Equal(File.ReadAllBytes(pristinePath), served);
    }
}
