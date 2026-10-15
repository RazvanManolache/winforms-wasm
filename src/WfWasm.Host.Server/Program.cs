using WfWasm.ErrorLog;
using WfWasm.Host.Server;
using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.ResponseCompression;

// Desktop host for the WASM client: serves the Blazor CanvasHost app,
// receives browser/client error reports (JS window hook + in-WASM
// BrowserErrorReporter), and keeps the crash inventory under logs/.
// Run: dotnet run --project src/WfWasm.Host.Server --urls http://127.0.0.1:5210
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/octet-stream", "application/wasm" });
});
var app = builder.Build();
app.UseResponseCompression();

// A source checkout includes the built-in smoke form, not third-party apps.
// Only show an external app after its entry assembly is supplied locally.
app.MapGet("/api/apps", () => AppTransform.AppFolders.Keys.Where(id =>
    AppTransform.TryGetSpec(id, out var spec) && spec is not null &&
    AppTransform.TryResolveAssembly(id, spec.Entry, out _)).ToArray());

string logDir = Path.Combine(app.Environment.ContentRootPath, "logs");

app.MapPost("/api/client-errors", (List<ErrorReport>? batch) =>
{
    if (batch is null || batch.Count == 0)
        return Results.BadRequest("empty batch");
    if (batch.Count > 50)
        return Results.BadRequest("batch too large");
    int logged = 0;
    foreach (var report in batch)
    {
        if (report is null)
            continue;
        ErrorLog.Log(logDir, report);
        logged++;
    }

    return Results.Ok(new { logged });
});

app.MapGet("/api/client-errors", () => Results.Ok(ErrorLog.ReadIndex(logDir)));

app.MapPost("/api/errors/mark-fixed", (MarkFixedRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Signature))
        return Results.BadRequest("signature required");
    return ErrorLog.MarkFixed(logDir, req.Signature, req.Note ?? "")
        ? Results.Ok(new { signature = req.Signature, status = ErrorLog.StatusFixed })
        : Results.NotFound("unknown signature");
});

app.MapGet("/errors", () =>
    Results.Content(ErrorInventoryPage.Render(ErrorLog.ReadIndex(logDir)), "text/html"));

// On-demand app delivery: the browser fetches the manifest, then the
// assemblies it needs. Transform output is content-hashed and cached,
// so identical inputs serve identical bytes.
app.MapGet("/api/appmanifest", (string app, HttpContext context) =>
{
    if (!AppTransform.TryGetSpec(app, out var spec) || spec is null)
        return Results.NotFound($"unknown app '{app}'");
    try
    {
        context.Response.Headers.CacheControl = "no-cache";
        string dependencies = AppTransform.DependencyHash(app);
        if (!AppTransform.TryResolveAssembly(app, spec.Entry, out string? entryFile) || entryFile is null)
            return Results.NotFound($"entry assembly not found for '{app}'");
        var files = new[] { entryFile }.Select(name =>
        {
            string hash = AppTransform.ContentHash(app, name, dependencies);
            return new { name, sha256 = hash,
                url = $"/api/appasm?app={Uri.EscapeDataString(app)}&name={Uri.EscapeDataString(name)}&v={hash}" };
        }).ToList();
        var content = spec.ContentFiles.Select(file =>
        {
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Value)));
            return new { name = file.Key, sha256 = hash,
                url = $"/api/appcontent?app={Uri.EscapeDataString(app)}&name={Uri.EscapeDataString(file.Key)}&v={hash}" };
        }).ToList();
        return Results.Ok(new { entry = spec.Entry, files, content });
    }
    catch (Exception ex)
    {
        return Results.Problem($"transform failed for '{app}': {ex}");
    }
});

// Called by the CLR's missing-assembly hook, one requested name at a time.
// A missing optional assembly is an ordinary resolution miss, not a server error.
app.MapGet("/api/appresolve", (string app, string name, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-cache";
    if (!AppTransform.TryGetSpec(app, out _)) return Results.NotFound("unknown app");
    if (!AppTransform.TryResolveAssembly(app, name, out string? file) || file is null)
        return Results.NoContent();
    try
    {
        byte[] bytes = AppTransform.TransformOrCached(app, file);
        return CachedAsset(context, bytes, "application/octet-stream");
    }
    catch (Exception ex)
    {
        return Results.Problem($"assembly resolution failed for '{app}/{name}': {ex.GetType().Name}: {ex.Message}");
    }
});

app.MapGet("/api/appasm", (string app, string name, HttpContext context) =>
{
    try
    {
        byte[] bytes = AppTransform.TransformOrCached(app, name);
        return CachedAsset(context, bytes, "application/octet-stream");
    }
    catch (ArgumentException ex)
    {
        return Results.NotFound(ex.Message);
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(ex.Message);
    }
    catch (Exception ex)
    {
        return Results.Problem($"transform failed for '{app}/{name}': {ex.GetType().Name}: {ex.Message}");
    }
});

// Loose app content by manifest name (raw bytes, unconverted).
// The name must come from the spec map: no path traversal.
app.MapGet("/api/appcontent", (string app, string name, HttpContext context) =>
{
    if (!AppTransform.TryGetSpec(app, out var spec) || spec is null)
        return Results.NotFound($"unknown app '{app}'");
    if (!spec.ContentFiles.TryGetValue(name, out string? path) || path is null)
        return Results.NotFound($"unknown content '{name}' for app '{app}'");
    if (!File.Exists(path))
        return Results.NotFound($"missing content file '{name}' for app '{app}'");
    string mime = Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream",
    };
    return CachedAsset(context, File.ReadAllBytes(path), mime);
});

// Loose app content (images and other resources) ships unconverted,
// one static root per on-demand app folder.
foreach (var (slug, folder) in AppTransform.AppFolders)
{
    if (Directory.Exists(folder))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(folder),
            RequestPath = "/appfiles/" + slug,
        });
    }
}

var clientFiles = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        string path = context.Context.Request.Path.Value ?? "";
        // Fingerprinted runtime assets are immutable; shell files must revalidate
        // after every deployment so a new shell cannot use stale scripts/styles.
        bool fingerprinted = path.StartsWith("/_framework/", StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(context.File.Name, @"\.[a-z0-9]{10}\.(wasm|js)$");
        context.Context.Response.Headers.CacheControl = fingerprinted
            ? "public,max-age=31536000,immutable" : "no-cache";
    }
};
app.UseStaticFiles(clientFiles);
app.UseBlazorFrameworkFiles();
app.MapFallbackToFile("index.html", clientFiles);

app.Run();

static IResult CachedAsset(HttpContext context, byte[] bytes, string mime)
{
    string hash = Convert.ToHexString(SHA256.HashData(bytes));
    context.Response.Headers["X-WfWasm-Sha256"] = hash;
    // Only the matching content version may be cached forever.
    if (context.Request.Query.TryGetValue("v", out var version) && version != hash)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Conflict("Asset changed; reload the app manifest.");
    }
    context.Response.Headers.CacheControl = context.Request.Query.ContainsKey("v")
        ? "private,max-age=31536000,immutable" : "no-cache";
    return Results.File(bytes, mime, entityTag: new EntityTagHeaderValue("\"" + hash + "\""));
}

public sealed record MarkFixedRequest(string Signature, string? Note);
