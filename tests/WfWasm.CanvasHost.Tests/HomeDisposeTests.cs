using System.Reflection;
using WfWasm.CanvasHost.Pages;

public sealed class HomeDisposeTests
{
    private static string RepoFile(params string[] parts)
    {
        string? dir = Path.GetDirectoryName(SourceFile());
        return Path.GetFullPath(Path.Combine(new[] { "../../" }.Concat(parts).ToArray()), dir!);
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void Home_Is_AsyncDisposable()
    {
        // DisposeAsync releases the canvas ResizeObserver BEFORE the
        // DotNetObjectReference dies (dispose-race: "no tracked object").
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(Home)));
        Assert.NotNull(typeof(Home).GetMethod("DisposeAsync",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
    }

    [Fact]
    public void OnCanvasResized_Ignores_Invokes_After_Dispose()
    {
        // In-flight resizes that resolve after dispose must be ignored,
        // not applied to a dead host.
        var home = new Home();
        object host = typeof(Home).GetField("_winFormsHost",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(home)!;
        PropertyInfo vw = host.GetType().GetProperty("ViewWidth")!;
        home.OnCanvasResized(800, 600);
        Assert.Equal(800, vw.GetValue(host));
        typeof(Home).GetField("_disposed",
            BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(home, true);
        Task done = home.OnCanvasResized(1024, 768);
        Assert.True(done.IsCompletedSuccessfully);
        Assert.Equal(800, vw.GetValue(host));
    }

    [Fact]
    public void CanvasBridge_Releases_Observer_On_ReleaseCanvas()
    {
        // Static contract for the razor's DisposeAsync: releaseCanvas must
        // disconnect the ResizeObserver and remove the fallback listener.
        string file = RepoFile("src", "WfWasm.CanvasHost", "wwwroot", "canvas-bridge.js");
        Assert.True(File.Exists(file), file);
        string js = File.ReadAllText(file);
        Assert.Contains("releaseCanvas", js);
        Assert.Contains(".disconnect()", js);
        Assert.Contains("removeEventListener(\"resize\"", js);
        Assert.Contains("wfwasm._fit", js);
    }
}
