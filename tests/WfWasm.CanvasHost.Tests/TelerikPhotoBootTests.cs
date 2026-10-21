using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;

public sealed class TelerikPhotoBootTests
{
    [Fact]
    public void PhotoAlbum_Boots_And_Paints_Headless()
    {
        // Same regenerable-data coupling as TictactoePaintTests: the
        // batch-rewritten app must exist in E:\tmp.
        string dir = @"E:\tmp\rewritten-telerik-photo";
        string app = Path.Combine(dir, "PhotoAlbum.exe");
        Assert.True(File.Exists(app), $"regenerate via E:/tmp/rewrite-telerik-photo.ps1: {app}");
        var photo = Assembly.LoadFrom(app);
        Assert.Equal("PhotoAlbum", photo.GetName().Name);
        // Browser parity: one copy of each assembly wins. Prefer the
        // already-loaded framework copies, then the batch siblings.
        ResolveEventHandler? resolver = (_, args) =>
        {
            string name = args.Name.Split(',')[0].Trim();
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == name);
            if (loaded is not null) return loaded;
            string dll = Path.Combine(dir, name + ".dll");
            if (File.Exists(dll)) return Assembly.LoadFrom(dll);
            string exe = Path.Combine(dir, name + ".exe");
            if (File.Exists(exe)) return Assembly.LoadFrom(exe);
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            using var host = new WinFormsAppHost();
            host.SelectScene(AppScene.TelerikPhoto);
            FormsForm? form = host.GetOrCreateForm(AppScene.TelerikPhoto);
            Assert.NotNull(form);
            Assert.Contains("Photo album", form.Text);
            FormsControl? viewer = FindByTypeName(form, "RadPhotoAlbumViewer");
            Assert.NotNull(viewer);
            var recorder = new RecordingRenderer();
            host.Paint(recorder);
            Assert.NotEmpty(recorder.Commands);
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    private static FormsControl? FindByTypeName(FormsControl root, string name)
    {
        if (root.GetType().Name.Contains(name, StringComparison.Ordinal)) return root;
        foreach (FormsControl c in root.Controls)
        {
            FormsControl? hit = FindByTypeName(c, name);
            if (hit is not null) return hit;
        }
        return null;
    }
}
