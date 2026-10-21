using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.JSInterop;
using WfWasm.Drawing;

namespace WfWasm.CanvasHost;

public static partial class AppAssemblyLoader
{
    private sealed record ManifestFile(string name, string sha256, string url);
    private sealed record ManifestContent(string name, string url, string? sha256 = null);
    private sealed record ManifestDefaultFont(string name, float size);
    private sealed record AppManifest(string entry, List<ManifestFile> files, List<ManifestContent>? content, ManifestDefaultFont? defaultFont = null, List<ManifestPrefetch>? prefetch = null);
    private sealed record ManifestPrefetch(string name, string url, long size, string sha256);
    private sealed record ContentItem(string Name, byte[]? Bytes, AppFileSystem.DecodedImage? Image, long SourceSize)
    {
        public long Size => Bytes?.LongLength ?? Image?.Rgba.LongLength ?? 0;
    }
    private sealed record ContentSet(List<ContentItem> Items, long Size);
    private static readonly Dictionary<string, byte[]> Bytes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Task<(Assembly Assembly, AppManifest Manifest)>> Apps = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> Fetches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ContentSet> Content = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> ContentLru = new();
    private const long ContentBudget = 128 * 1024 * 1024;
    private static readonly object Gate = new();
    private static bool _hooked;
    private static string? _activeApp;
    private static readonly Dictionary<string, HashSet<string>> UsedByApp = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Resolving = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Missing = new(StringComparer.OrdinalIgnoreCase);

    [JSImport("globalThis.wfwasmFetchAssembly")]
    private static partial byte[] FetchRequestedAssembly(string app, string name);

    private static string Key(string name)
    {
        string leaf = Path.GetFileName(name.Replace('\\', '/'));
        return leaf.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? leaf[..^4] : leaf;
    }

    private static void HookResolving()
    {
        lock (Gate)
        {
            if (_hooked) return;
            _hooked = true;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveRequestedAssembly;
        }
    }

    // Rewrite-target scopes unify with the bundled fork: rewritten
    // assemblies reference the fork by name, but Verbatim assemblies
    // (System.Windows.Forms.Legacy, WPF) keep framework scopes. Binding
    // those to the already-loaded fork/drawing avoids a duplicate load
    // of the same types (split-brain) and a pointless multi-MB fetch
    // (a rewritten System.Windows.Forms is 7MB of desktop surface the
    // browser must never bind).
    private static string? WellKnownForkAssembly(string name) => name.ToLowerInvariant() switch
    {
        "system.windows.forms" => "WfWasm.WinForms",
        "system.windows.forms.primitives" => "WfWasm.WinForms",
        "system.drawing" => "WfWasm.Drawing",
        "system.drawing.common" => "WfWasm.Drawing",
        "system.drawing.primitives" => "WfWasm.Drawing",
        _ => null,
    };

    private static Assembly? ResolveRequestedAssembly(object? sender, ResolveEventArgs args)
    {
        string? name = new AssemblyName(args.Name).Name;
        /* resolve probe removed (misses still log "treating as missing") */
        if (name is null) return null;
        string? app;
        byte[]? bytes;
        lock (Gate)
        {
            app = _activeApp;
            if (!Resolving.Add(name)) return null;
            Bytes.TryGetValue(name, out bytes);
        }
        try
        {
            if (bytes is null)
            {
                // One copy per name, version-agnostic: netfx v4 refs
                // (Telerik UIA -> WindowsBase v4) must bind the bundled
                // v9, never a second fetch. Without this, twin loads of
                // one framework assembly split type identity (Rect !=
                // Rect) and interface maps fail ("get_BoundingRectangle
                // ... does not have an implementation"). The hook only
                // fires when default binding failed, so returning a
                // loaded same-name assembly can only unify, and the
                // on-demand model is name-keyed anyway (no side-by-side).
                string localName = WellKnownForkAssembly(name) ?? name;
                var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                    string.Equals(a.GetName().Name, localName, StringComparison.OrdinalIgnoreCase));
                if (loaded is not null)
                {
                    if (app is not null) NoteUsed(app, name);
                    return loaded;
                }
                if (app is null || !OperatingSystem.IsBrowser() || Missing.Contains(app + "/" + name)) return null;
                // AssemblyResolve is synchronous. Fetch only the name the CLR
                // requested, then supply its verified bytes before returning.
                bytes = DemandAssemblyPayload.Decode(FetchRequestedAssembly(app, name));
                if (bytes is null) { Missing.Add(app + "/" + name); return null; }
                lock (Gate) Bytes[name] = bytes;
                Console.WriteLine($"[AppAssemblyLoader] Requested {app}/{name} ({bytes.Length} bytes) at runtime.");
            }
            if (app is not null) NoteUsed(app, name);
            try
            {
                var loadedAsm = Assembly.Load(bytes);
                try { Console.WriteLine($"[AppAssemblyLoader] Loaded {app}/{name}."); } catch { }
                /* load beacon removed (endpoint ignored the flag; saved a manifest regen per assembly) */
                return loadedAsm;
            }
            catch (Exception ex)
            {
                // An exception out of an AssemblyResolve handler kills the
                // Mono runtime (exit 1): PDN's WPF AssemblyHelper.OnLoaded
                // throws NIE on ReflectionOnly during Load. Convert the
                // throwing load into a catchable miss (null) instead of
                // propagating; the request site sees FileNotFound.
                Console.WriteLine($"[AppAssemblyLoader] Load of {app}/{name} threw {ex.GetType().Name}, treating as missing.");
                return null;
            }
        }
        finally { lock (Gate) Resolving.Remove(name); }
    }

    private static readonly HashSet<string> FontApplied = new(StringComparer.OrdinalIgnoreCase);

    private static void ApplyDefaultFont(string app, ManifestDefaultFont? font)
    {
        if (font is null) return;
        lock (Gate)
        {
            if (!FontApplied.Add(app)) return;
        }
        try
        {
            global::System.Windows.Forms.Application.SetDefaultFont(new global::System.Drawing.Font(font.name, font.size));
        }
        catch (InvalidOperationException)
        {
            // A window already exists (app switch): the first app's font sticks.
            Console.WriteLine($"[AppAssemblyLoader] Default font for {app} skipped (windows exist).");
        }
    }

    private static void NoteUsed(string app, string name)
    {
        lock (Gate)
        {
            if (!UsedByApp.TryGetValue(app, out var used)) UsedByApp[app] = used = new(StringComparer.OrdinalIgnoreCase);
            used.Add(name);
        }
    }

    public static async Task<Assembly> EnsureAppAsync(string app, HttpClient http, IJSRuntime? js = null)
    {
        HookResolving();
        Task<(Assembly Assembly, AppManifest Manifest)> task;
        bool reused;
        lock (Gate)
        {
            _activeApp = app;
            reused = Apps.TryGetValue(app, out task!);
            if (!reused) Apps[app] = task = LoadAppAsync(app, http, js);
        }
        (Assembly Assembly, AppManifest Manifest) loaded;
        try { loaded = await task; }
        catch
        {
            // Faults must not poison a session: the next selection can retry.
            lock (Gate)
                if (Apps.TryGetValue(app, out var current) && ReferenceEquals(current, task)) Apps.Remove(app);
            throw;
        }
        if (reused)
        {
            string[] used;
            lock (Gate) used = UsedByApp.TryGetValue(app, out var names) ? names.ToArray() : [];
            foreach (string name in used)
            {
                byte[]? bytes;
                lock (Gate) Bytes.TryGetValue(name, out bytes);
                if (bytes is not null) await ReportAsync(js, bytes.Length, true);
            }
        }
        await RestoreContentAsync(app, http, js, loaded.Manifest.content);
        ApplyDefaultFont(app, loaded.Manifest.defaultFont);
        System.Windows.Forms.WasmAppIdentity.EntryFileName = loaded.Manifest.entry + ".exe";
        return loaded.Assembly;
    }

    private static async Task<(Assembly, AppManifest)> LoadAppAsync(string app, HttpClient http, IJSRuntime? js)
    {
        var manifest = await http.GetFromJsonAsync<AppManifest>("/api/appmanifest?app=" + Uri.EscapeDataString(app))
            ?? throw new InvalidOperationException("No manifest for app '" + app + "'.");
        // Kick the browser-cache warm first (zero WASM-thread cost): the
        // sync demand hook hits the same appresolve URLs, so each fault
        // becomes a cache hit instead of a transform + download stall.
        try
        {
            if (js is not null && manifest.prefetch is { Count: > 0 })
                await js.InvokeVoidAsync("wfwasmPrefetch", app,
                    manifest.prefetch.Select(f => Key(f.name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch { }
        await PhaseAsync(js, "Downloading app entry assembly");
        // Even a legacy manifest containing a folder/catalog cannot cause a
        // preload: only the entry is fetched here. Everything else is requested
        // by ResolveRequestedAssembly when the CLR actually needs that name.
        var file = manifest.files.SingleOrDefault(f => Key(f.name).Equals(manifest.entry, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Manifest has no entry assembly for '" + app + "'.");
        var lazy = new Lazy<Task<byte[]>>(() => FetchVerifiedAsync(http, file));
        var fetch = Fetches.GetOrAdd(file.sha256, lazy);
        byte[] entryBytes;
        try { entryBytes = await fetch.Value; }
        catch { Fetches.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(file.sha256, fetch)); throw; }
        lock (Gate) Bytes[Key(file.name)] = entryBytes;
        NoteUsed(app, Key(file.name));
        await ReportAsync(js, entryBytes.Length, !ReferenceEquals(fetch, lazy));
        // Warm the manifest closure in parallel without delaying first
        // paint: demand faults that arrive before warmup completes use the
        // sync hook as before. Fire-and-forget (all failures swallowed —
        // the hook stays the fallback); integrity comes from the response
        // header, never from a blind store.
        if (manifest.prefetch is { Count: > 0 })
        {
            var warm = manifest.prefetch
                .Where(f => !Key(f.name).Equals(manifest.entry, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _ = Task.Run(async () =>
            {
                using var gate = new SemaphoreSlim(6);
                long warmed = 0;
                await Task.WhenAll(warm.Select(async f =>
                {
                    await gate.WaitAsync();
                    try { warmed += await PrefetchOneAsync(http, f); }
                    catch { }
                    finally { gate.Release(); }
                }));
                try { Console.WriteLine($"[AppAssemblyLoader] Prefetch warmed {warmed} bytes for '{app}'."); } catch { }
            });
        }
        var entry = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
            string.Equals(a.GetName().Name, manifest.entry, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = Assembly.Load(entryBytes);
        }
        return (entry, manifest);

        // One prefetch lane: raw transformed bytes, stored only when the
        // server's content hash header matches. Anything else (miss,
        // optional assembly, drift) is left for the demand hook.
        async Task<long> PrefetchOneAsync(HttpClient client, ManifestPrefetch f)
        {
            string key = Key(f.name);
            lock (Gate) { if (Bytes.ContainsKey(key)) return 0; }
            using var resp = await client.GetAsync(f.url);
            if (!resp.IsSuccessStatusCode) return 0;
            byte[] body = await resp.Content.ReadAsByteArrayAsync();
            if (resp.Headers.TryGetValues("X-WfWasm-Sha256", out var values)
                && values.FirstOrDefault() is string sha
                && sha.Equals(Convert.ToHexString(SHA256.HashData(body)), StringComparison.OrdinalIgnoreCase))
            {
                lock (Gate) Bytes[key] = body;
                await ReportAsync(js, body.Length, false);
                return body.Length;
            }
            return 0;
        }
    }

    private static async Task<byte[]> FetchVerifiedAsync(HttpClient http, ManifestFile file)
    {
        byte[] bytes = await http.GetByteArrayAsync(file.url);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Assembly integrity check failed: " + file.name);
        Console.WriteLine($"[AppAssemblyLoader] Fetched {file.name} ({bytes.Length} bytes).");
        return bytes;
    }

    private static async Task RestoreContentAsync(string app, HttpClient http, IJSRuntime? js, List<ManifestContent>? files)
    {
        // Always reset/restore the VFS, even for apps whose assembly task was cached.
        // Otherwise A → B → A leaves B's resources registered for A.
        ContentSet? cached;
        lock (Gate) Content.TryGetValue(app, out cached);
        if (cached is not null)
        {
            AppFileSystem.Clear();
            foreach (var item in cached.Items) { Register(item); await ReportAsync(js, item.SourceSize, true); }
            Touch(app);
            return;
        }
        if (files is null || files.Count == 0) { AppFileSystem.Clear(); return; }
        await PhaseAsync(js, "Downloading and decoding app content");
        using var slots = new SemaphoreSlim(4);
        var items = await Task.WhenAll(files.Select(async file =>
        {
            await slots.WaitAsync();
            try
            {
                byte[] bytes = await http.GetByteArrayAsync(file.url);
                if (file.sha256 is not null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Content integrity check failed: " + file.name);
                await ReportAsync(js, bytes.Length, false);
                string ext = Path.GetExtension(file.name).ToLowerInvariant();
                if (js is not null && ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp")
                {
                    byte[] decoded = await js.InvokeAsync<byte[]>("wfwasm.decodeImageBytes", bytes);
                    if (decoded.Length < 8) throw new InvalidDataException("Invalid decoded image: " + file.name);
                    int w = BitConverter.ToInt32(decoded, 0), h = BitConverter.ToInt32(decoded, 4);
                    if (w <= 0 || h <= 0 || decoded.LongLength != 8L + (long)w * h * 4)
                        throw new InvalidDataException("Invalid decoded dimensions: " + file.name);
                    var rgba = decoded.AsSpan(8).ToArray();
                    return new ContentItem(file.name, null, new(w, h, rgba), bytes.Length);
                }
                return new ContentItem(file.name, bytes, null, bytes.Length);
            }
            finally { slots.Release(); }
        }));
        AppFileSystem.Clear();
        foreach (var item in items) Register(item);
        var set = new ContentSet(items.ToList(), items.Sum(i => i.Size));
        lock (Gate)
        {
            if (set.Size <= ContentBudget)
            {
                Content[app] = set;
                Touch(app);
                while (Content.Values.Sum(c => c.Size) > ContentBudget && ContentLru.First is not null)
                {
                    string oldest = ContentLru.First.Value;
                    ContentLru.RemoveFirst();
                    Content.Remove(oldest);
                }
            }
        }
    }

    private static void Touch(string app)
    {
        lock (Gate) { ContentLru.Remove(app); ContentLru.AddLast(app); }
    }
    private static void Register(ContentItem item)
    {
        if (item.Image is { } image) AppFileSystem.RegisterDecoded(item.Name, image.Width, image.Height, image.Rgba);
        else if (item.Bytes is { } bytes) AppFileSystem.Register(item.Name, bytes);
    }
    private static ValueTask PhaseAsync(IJSRuntime? js, string phase) => js is null ? ValueTask.CompletedTask : js.InvokeVoidAsync("preview.phase", phase);
    private static ValueTask ReportAsync(IJSRuntime? js, long bytes, bool reused) => js is null ? ValueTask.CompletedTask : js.InvokeVoidAsync("preview.asset", bytes, reused);
}
