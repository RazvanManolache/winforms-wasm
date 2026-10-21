using Microsoft.Extensions.FileProviders;
using WfWasm.Platform.Server;

var builder = WebApplication.CreateBuilder(args);
string data = builder.Configuration["Platform:DataDirectory"] ?? throw new InvalidOperationException("Set Platform:DataDirectory to a private writable directory outside the web root.");
string samples = Path.Combine(data, "samples"); Directory.CreateDirectory(samples);
string example = Path.Combine(samples, "Welcome.txt");
if (!File.Exists(example)) File.WriteAllText(example, "This is an explicitly granted application file.\nSave your own documents in My workspace.\n");
builder.Services.AddWfPlatform(new()
{
    DataDirectory = data, IsKnownApp = app => app == "demo",
    Mounts = _ => new[] { new FileMount("samples", "Application files", samples), new FileMount("welcome", "One shared file", example, IsFile: true) }
        .Concat(builder.Configuration["Platform:MountRoot"] is { Length: > 0 } root ? new[] { new FileMount("server", "Shared server folder", root) } : [])
});
var app = builder.Build(); app.MapWfPlatform();
string browser = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../WfWasm.Platform.Browser"));
if (Directory.Exists(browser)) app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(browser), RequestPath = "/platform" });
app.UseDefaultFiles(); app.UseStaticFiles(); app.Run();
