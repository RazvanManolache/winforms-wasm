using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WfWasm.Platform.Server;

namespace WfWasm.Platform.Tests;

public sealed class EndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private string _origin = "";
    private readonly string _root = Path.Combine(Path.Combine(Path.GetTempPath(), "wf-platform-http-tests"), Guid.NewGuid().ToString("N"));
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddWfPlatform(new() { DataDirectory = _root, IsKnownApp = app => app is "demo" or "other", MaxFileBytes = 32 });
        _app = builder.Build(); _app.MapWfPlatform(); await _app.StartAsync();
        _origin = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }
    private HttpClient Client(string? origin = null)
    {
        var c = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new(_origin) };
        c.DefaultRequestHeaders.Add("Origin", origin ?? _origin); c.DefaultRequestHeaders.Add("X-Wf-Platform", "1"); return c;
    }
    private static async Task<string> Session(HttpClient c, string app = "demo")
    {
        var r = await c.PostAsJsonAsync("/api/platform/session", new { app }); r.EnsureSuccessStatusCode();
        var doc = await r.Content.ReadFromJsonAsync<JsonElement>(); string token = doc.GetProperty("token").GetString()!;
        c.DefaultRequestHeaders.Remove("X-Wf-Session"); c.DefaultRequestHeaders.Add("X-Wf-Session", token); return token;
    }
    [Fact] public async Task Session_Requires_Same_Origin_And_Known_App()
    {
        using var hostile = Client("http://attacker.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await hostile.PostAsJsonAsync("/api/platform/session", new { app = "demo" })).StatusCode);
        using var c = Client(); Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/platform/session", new { app = "missing" })).StatusCode);
        c.DefaultRequestHeaders.Remove("X-Wf-Platform"); Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/platform/session", new { app = "demo" })).StatusCode);
        using var rebound = Client("http://evil.invalid"); rebound.DefaultRequestHeaders.Host = "evil.invalid";
        Assert.Equal(HttpStatusCode.Forbidden, (await rebound.PostAsJsonAsync("/api/platform/session", new { app = "demo" })).StatusCode);
    }
    [Fact] public async Task Token_Is_Bound_To_Protected_User_Cookie_And_App()
    {
        using var alice = Client(); string token = await Session(alice);
        using var bob = Client(); await Session(bob); bob.DefaultRequestHeaders.Remove("X-Wf-Session"); bob.DefaultRequestHeaders.Add("X-Wf-Session", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/platform/registry")).StatusCode);
        var set = await alice.PutAsJsonAsync("/api/platform/registry/value?key=HKCU%5CDemo", new { name = "x", kind = "String", data = "alice" }); Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        await Session(alice, "other");
        var state = await alice.GetFromJsonAsync<RegistrySnapshot>("/api/platform/registry"); Assert.Empty(state!.Values);
    }
    [Fact] public async Task Session_Revocation_And_Reload_Persistence()
    {
        using var c = Client(); await Session(c);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/platform/registry/value?key=HKCU%5CDemo", new { name = "x", kind = "DWord", data = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/platform/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/platform/registry")).StatusCode);
        await Session(c); var state = await c.GetFromJsonAsync<RegistrySnapshot>("/api/platform/registry"); Assert.Equal(7, Assert.Single(state!.Values).Data.GetInt32());
    }
    [Fact] public async Task Files_HTTP_Write_Read_List_And_Reject_Oversize_Traversal()
    {
        using var c = Client(); await Session(c);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync("/api/platform/files/content?mount=workspace&path=docs/a.txt", new ByteArrayContent([1, 2, 3]))).StatusCode);
        var r = await c.GetAsync("/api/platform/files/content?mount=workspace&path=docs/a.txt"); Assert.Equal(new byte[] { 1, 2, 3 }, await r.Content.ReadAsByteArrayAsync()); Assert.Equal("no-store", r.Headers.CacheControl!.ToString());
        var page = await c.GetFromJsonAsync<DirectoryPage>("/api/platform/files/list?mount=workspace&path=docs"); Assert.Equal("a.txt", Assert.Single(page!.Entries).Name);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync("/api/platform/files/content?mount=workspace&path=big", new ByteArrayContent(new byte[33]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/platform/files/content?mount=workspace&path=..%2Fsecret")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/platform/files/content?mount=ungranted&path=x")).StatusCode);
    }
    [Fact] public async Task Invalid_Registry_Body_Is_Rejected_Without_Mutation()
    {
        using var c = Client(); await Session(c);
        foreach (string json in new[] { "{", "null", "{}", "{\"name\":null,\"kind\":\"String\",\"data\":\"x\"}" })
        {
            var r = await c.PutAsync("/api/platform/registry/value?key=HKCU%5CDemo", new StringContent(json, System.Text.Encoding.UTF8, "application/json")); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        Assert.Empty((await c.GetFromJsonAsync<RegistrySnapshot>("/api/platform/registry"))!.Values);
    }
    public async Task DisposeAsync() { await _app.DisposeAsync(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
