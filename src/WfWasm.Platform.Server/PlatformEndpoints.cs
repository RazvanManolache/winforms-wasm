using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace WfWasm.Platform.Server;

public static class PlatformEndpoints
{
    public static IServiceCollection AddWfPlatform(this IServiceCollection services, PlatformOptions options)
    {
        if (options.MaxFileBytes is <= 0 or > 256 * 1024 * 1024 || options.SessionLifetime <= TimeSpan.Zero) throw new ArgumentException("Invalid platform limits.");
        Directory.CreateDirectory(options.DataDirectory);
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.DataDirectory, "identity-keys"))).SetApplicationName("WfWasm.Platform");
        services.AddSingleton(options); services.AddSingleton<VirtualFiles>(); services.AddSingleton<VirtualRegistry>(); services.AddSingleton<PlatformSessions>();
        return services;
    }

    public static IEndpointRouteBuilder MapWfPlatform(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/platform");
        group.MapPost("/session", async (HttpContext c, PlatformSessions sessions, PlatformOptions options) =>
            await Guard(c, async () =>
            {
                RequireBrowserRequest(c);
                var req = await ReadJson<SessionRequest>(c, 2048);
                string app = Names.Id(req.App);
                if (!options.IsKnownApp(app)) return Results.NotFound(new { error = "Unknown application." });
                return Results.Ok(sessions.Create(c, app));
            }));
        group.MapDelete("/session", (HttpContext c, PlatformSessions sessions) => Scoped(c, sessions, _ =>
        { sessions.Revoke(c.Request.Headers["X-Wf-Session"].ToString()); return Task.FromResult(Results.NoContent()); }));
        group.MapGet("/mounts", (HttpContext c, PlatformSessions s, VirtualFiles files) => Scoped(c, s, scope => Task.FromResult(Results.Ok(files.GetMounts(scope)))));
        group.MapGet("/files/list", (HttpContext c, PlatformSessions s, VirtualFiles files) => Scoped(c, s, scope =>
        {
            int offset = int.TryParse(c.Request.Query["offset"], out var n) ? n : 0;
            return Task.FromResult(Results.Ok(files.List(scope, c.Request.Query["mount"].ToString(), c.Request.Query["path"].ToString(), offset)));
        }));
        group.MapGet("/files/content", (HttpContext c, PlatformSessions s, VirtualFiles files) => Scoped(c, s, scope =>
        {
            c.Response.Headers.XContentTypeOptions = "nosniff";
            c.Response.Headers.ContentDisposition = "attachment";
            return Task.FromResult(Results.Bytes(files.Read(scope, c.Request.Query["mount"].ToString(), c.Request.Query["path"].ToString()), "application/octet-stream"));
        }));
        group.MapPut("/files/content", (HttpContext c, PlatformSessions s, VirtualFiles files, PlatformOptions options) => Scoped(c, s, async scope =>
        {
            byte[] bytes = await ReadBody(c, options.MaxFileBytes);
            files.Write(scope, c.Request.Query["mount"].ToString(), c.Request.Query["path"].ToString(), bytes);
            return Results.NoContent();
        }));
        group.MapPost("/files/directory", (HttpContext c, PlatformSessions s, VirtualFiles files) => Scoped(c, s, scope =>
        {
            files.CreateDirectory(scope, c.Request.Query["mount"].ToString(), c.Request.Query["path"].ToString());
            return Task.FromResult(Results.NoContent());
        }));
        group.MapDelete("/files/content", (HttpContext c, PlatformSessions s, VirtualFiles files) => Scoped(c, s, scope =>
        {
            files.Delete(scope, c.Request.Query["mount"].ToString(), c.Request.Query["path"].ToString()); return Task.FromResult(Results.NoContent());
        }));
        group.MapGet("/registry", (HttpContext c, PlatformSessions s, VirtualRegistry registry) => Scoped(c, s, scope => Task.FromResult(Results.Ok(registry.Snapshot(scope)))));
        group.MapPut("/registry/key", (HttpContext c, PlatformSessions s, VirtualRegistry registry) => Scoped(c, s, scope =>
        {
            registry.CreateKey(scope, c.Request.Query["key"].ToString()); return Task.FromResult(Results.NoContent());
        }));
        group.MapPut("/registry/value", (HttpContext c, PlatformSessions s, VirtualRegistry registry, PlatformOptions options) => Scoped(c, s, async scope =>
        {
            var entry = await ReadJson<RegistryEntry>(c, options.MaxRegistryValueBytes + 4096);
            registry.Set(scope, c.Request.Query["key"].ToString(), entry); return Results.NoContent();
        }));
        group.MapDelete("/registry/value", (HttpContext c, PlatformSessions s, VirtualRegistry registry) => Scoped(c, s, scope =>
            Task.FromResult(registry.DeleteValue(scope, c.Request.Query["key"].ToString(), c.Request.Query["name"].ToString()) ? Results.NoContent() : Results.NotFound())));
        group.MapDelete("/registry/key", (HttpContext c, PlatformSessions s, VirtualRegistry registry) => Scoped(c, s, scope =>
            Task.FromResult(registry.DeleteKey(scope, c.Request.Query["key"].ToString(), c.Request.Query["recursive"] == "true") ? Results.NoContent() : Results.NotFound())));
        return endpoints;
    }

    private static Task<IResult> Scoped(HttpContext c, PlatformSessions sessions, Func<PlatformScope, Task<IResult>> action) => Guard(c, () =>
    { RequireBrowserRequest(c); return action(sessions.Resolve(c)); });

    private static async Task<IResult> Guard(HttpContext c, Func<Task<IResult>> action)
    {
        c.Response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Results.Json(new { error = "Access denied or session expired." }, statusCode: 403); }
        catch (FileNotFoundException) { return Results.NotFound(new { error = "File not found." }); }
        catch (DirectoryNotFoundException) { return Results.NotFound(new { error = "Folder not found." }); }
        catch (Exception e) when (e is ArgumentException or JsonException or InvalidOperationException or FormatException)
        { return Results.BadRequest(new { error = "Invalid platform request." }); }
        catch (IOException) { return Results.Json(new { error = "Storage operation failed or quota exceeded." }, statusCode: 409); }
    }

    private static void RequireBrowserRequest(HttpContext c)
    {
        if (c.Request.Headers["X-Wf-Platform"] != "1") throw new UnauthorizedAccessException();
        string origin = c.Request.Headers.Origin.ToString();
        string expected = c.Request.Scheme + "://" + c.Request.Host;
        var options = c.RequestServices.GetRequiredService<PlatformOptions>();
        string host = c.Request.Host.Host.Trim('[', ']');
        bool loopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            System.Net.IPAddress.TryParse(host, out var ip) && System.Net.IPAddress.IsLoopback(ip);
        if (options.TrustedOrigins.Count > 0 ? !options.TrustedOrigins.Contains(expected) : !loopback) throw new UnauthorizedAccessException();
        if (origin.Length > 0 ? !string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase) : c.Request.Headers["Sec-Fetch-Site"] != "same-origin")
            throw new UnauthorizedAccessException();
    }
    private static async Task<byte[]> ReadBody(HttpContext c, long limit)
    {
        if (c.Request.ContentLength > limit) throw new ArgumentException("Body too large.");
        using var output = new MemoryStream(); var buffer = new byte[8192];
        int n;
        while ((n = await c.Request.Body.ReadAsync(buffer, c.RequestAborted)) != 0)
        {
            if (output.Length + n > limit) throw new ArgumentException("Body too large.");
            output.Write(buffer, 0, n);
        }
        return output.ToArray();
    }
    private static async Task<T> ReadJson<T>(HttpContext c, long limit) => JsonSerializer.Deserialize<T>(await ReadBody(c, limit), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new ArgumentException("JSON body required.");
    private sealed record SessionRequest(string App);
}

public sealed class PlatformSessions(PlatformOptions options, IDataProtectionProvider protection)
{
    private const string CookieName = "wf-platform-user";
    private readonly IDataProtector _protector = protection.CreateProtector("anonymous-user-v1");
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private sealed record Session(PlatformScope Scope, DateTimeOffset Expires);
    private string User(HttpContext context, bool create)
    {
        string? authenticated = options.AuthenticatedUser(context);
        if (!string.IsNullOrWhiteSpace(authenticated)) return "auth:" + authenticated;
        if (options.RequireAuthenticatedUsers) throw new UnauthorizedAccessException();
        if (context.Request.Cookies.TryGetValue(CookieName, out string? value))
        {
            try { return "anon:" + _protector.Unprotect(value); } catch (CryptographicException) { }
        }
        if (!create) throw new UnauthorizedAccessException();
        string id = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(CookieName, _protector.Protect(id), new CookieOptions
        { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/api/platform", MaxAge = TimeSpan.FromDays(365), IsEssential = true });
        return "anon:" + id;
    }
    public object Create(HttpContext c, string app)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _sessions) if (entry.Value.Expires <= now) _sessions.TryRemove(entry.Key, out _);
        if (_sessions.Count >= 10_000) throw new IOException("Session capacity reached.");
        var scope = new PlatformScope(User(c, true), app);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = new(scope, now + options.SessionLifetime);
        return new { token, app, user = PlatformScope.Hash(scope.UserId), expires = now + options.SessionLifetime };
    }
    public PlatformScope Resolve(HttpContext c)
    {
        string token = c.Request.Headers["X-Wf-Session"].ToString();
        if (!_sessions.TryGetValue(token, out var session) || session.Expires <= DateTimeOffset.UtcNow || session.Scope.UserId != User(c, false))
            throw new UnauthorizedAccessException();
        return session.Scope;
    }
    public void Revoke(string token) => _sessions.TryRemove(token, out _);
}
