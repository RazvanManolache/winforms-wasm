using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using WfWasm.CanvasHost;
using WfWasm.Drawing;

[Collection("AppHost")]
public sealed class AppAssemblyLoaderTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public readonly string Id = Guid.NewGuid().ToString("N");
        public int Manifests, Assemblies, Contents;
        public bool FailOnce, CorruptOnce, IncludeUnusedAssembly;
        private readonly byte[] _assembly = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString());
        public string Hash => Convert.ToHexString(SHA256.HashData(_assembly));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response;
            if (path == "/api/appmanifest")
            {
                Manifests++;
                string app = request.RequestUri.Query.Split('=')[1];
                response = new(HttpStatusCode.OK) { Content = JsonContent.Create(new {
                    entry = typeof(SceneState).Assembly.GetName().Name,
                    files = new[] {
                        new { name = typeof(SceneState).Assembly.GetName().Name + ".dll", sha256 = Hash, url = "/assembly" },
                        new { name = "Unused.Library.dll", sha256 = new string('0', 64), url = "/must-not-fetch" }
                    }.Take(IncludeUnusedAssembly ? 2 : 1).ToArray(),
                    content = new[] { new { name = "loader-test.txt", url = "/content/" + app } }
                }) };
            }
            else if (path == "/assembly")
            {
                Assemblies++;
                if (FailOnce) { FailOnce = false; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
                byte[] bytes = CorruptOnce ? new byte[] { 0 } : _assembly;
                CorruptOnce = false;
                response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            else if (path == "/must-not-fetch") throw new InvalidOperationException("An unused library was downloaded.");
            else
            {
                Contents++;
                response = new(HttpStatusCode.OK) { Content = new StringContent(path) };
            }
            return Task.FromResult(response);
        }
        public HttpClient Client() => new(this) { BaseAddress = new Uri("http://localhost/") };
    }

    [Fact]
    public async Task Only_Entry_Is_Fetched_Even_When_A_Manifest_Lists_Unused_Libraries()
    {
        var handler = new Handler { IncludeUnusedAssembly = true };
        using var http = handler.Client();
        await AppAssemblyLoader.EnsureAppAsync(handler.Id, http);
        Assert.Equal(1, handler.Assemblies);
        Assert.Equal(1, handler.Contents);
    }

    [Fact]
    public async Task Failed_Fetch_Can_Be_Retried_In_The_Same_Session()
    {
        var handler = new Handler { FailOnce = true };
        using var http = handler.Client();
        string app = handler.Id;
        await Assert.ThrowsAsync<HttpRequestException>(() => AppAssemblyLoader.EnsureAppAsync(app, http));
        await AppAssemblyLoader.EnsureAppAsync(app, http);
        Assert.Equal(2, handler.Assemblies);
        Assert.Equal(2, handler.Manifests);
    }

    [Fact]
    public async Task Corrupt_Assembly_Is_Rejected_And_Does_Not_Poison_Retry()
    {
        var handler = new Handler { CorruptOnce = true };
        using var http = handler.Client();
        await Assert.ThrowsAsync<InvalidDataException>(() => AppAssemblyLoader.EnsureAppAsync(handler.Id, http));
        await AppAssemblyLoader.EnsureAppAsync(handler.Id, http);
        Assert.Equal(2, handler.Assemblies);
    }

    [Fact]
    public async Task Switching_A_B_A_Restores_A_Content_Without_Fetching_Again()
    {
        var handler = new Handler();
        using var http = handler.Client();
        string a = handler.Id + "-a", b = handler.Id + "-b";
        await AppAssemblyLoader.EnsureAppAsync(a, http);
        Assert.True(AppFileSystem.TryGetBytes("loader-test.txt", out var first));
        await AppAssemblyLoader.EnsureAppAsync(b, http);
        Assert.True(AppFileSystem.TryGetBytes("loader-test.txt", out var second));
        Assert.NotEqual(Encoding.UTF8.GetString(first!), Encoding.UTF8.GetString(second!));
        await AppAssemblyLoader.EnsureAppAsync(a, http);
        Assert.True(AppFileSystem.TryGetBytes("loader-test.txt", out var restored));
        Assert.Equal(first, restored);
        Assert.Equal(1, handler.Assemblies); // Identical shared assembly, two app manifests.
        Assert.Equal(2, handler.Manifests);
        Assert.Equal(2, handler.Contents);
        AppFileSystem.Clear();
    }
}
