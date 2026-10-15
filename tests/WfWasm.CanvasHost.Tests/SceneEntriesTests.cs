using WfWasm.CanvasHost;
using WfWasm.Shim;

public sealed class SceneEntriesTests
{
    [Fact]
    public void Every_Scene_Has_An_Entry_With_Valid_Source()
    {
        var state = new SceneState();
        var scenes = Enum.GetValues<AppScene>();
        Assert.Equal(scenes.Length, state.Entries.Count);
        foreach (var scene in scenes)
        {
            var entry = Assert.Single(state.Entries, e => e.Scene == scene);
            Assert.False(string.IsNullOrWhiteSpace(entry.Label));
            if (entry.SourceUrl is not null)
            {
                Assert.True(Uri.TryCreate(entry.SourceUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps, $"bad URL for {scene}: {entry.SourceUrl}");
                Assert.False(string.IsNullOrWhiteSpace(entry.SourceLabel));
            }
        }
    }

    [Fact]
    public void Advertised_References_Have_Real_Files_And_Honest_Capture_Records()
    {
        // Apps without native evidence must not advertise a fabricated baseline.
        string root = Path.GetFullPath("../../src/WfWasm.CanvasHost/wwwroot/groundtruth",
            Path.GetDirectoryName(SourceFile())!);
        using var records = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "references.json")));
        var state = new SceneState();
        foreach (var entry in state.Entries.Where(e => e.DesktopReferenceFile is not null))
        {
            string file = Path.Combine(root, entry.DesktopReferenceFile!);
            Assert.True(File.Exists(file), file);
            var record = Assert.Single(records.RootElement.EnumerateArray(),
                r => r.GetProperty("slug").GetString() == SceneState.ToSlug(entry.Scene));
            Assert.Equal(entry.DesktopReferenceFile, record.GetProperty("file").GetString());
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))),
                record.GetProperty("sha256").GetString());
            if (record.GetProperty("status").GetString() == "captured")
            {
                foreach (string field in new[] { "capturedAt", "method", "windowTitle", "executable", "executableSha256" })
                    Assert.False(string.IsNullOrWhiteSpace(record.GetProperty(field).GetString()), field);
            }
            else
            {
                Assert.Equal("unverified", record.GetProperty("status").GetString());
                Assert.Equal(System.Text.Json.JsonValueKind.Null, record.GetProperty("capturedAt").ValueKind);
            }
        }
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void Base_Checkout_Only_Shows_Smoke_And_Ignores_Unavailable_Selections()
    {
        var state = new SceneState();
        state.LimitToAvailableApps([]);
        Assert.Equal(AppScene.Smoke, Assert.Single(state.Entries).Scene);
        state.Select(AppScene.Paint);
        Assert.Equal(AppScene.Smoke, state.CurrentEntry.Scene);
    }

    [Fact]
    public void Supplied_App_Is_Added_Without_Advertising_Missing_Apps()
    {
        var state = new SceneState();
        state.LimitToAvailableApps(["calculator"]);
        Assert.Equal(2, state.Entries.Count);
        state.Select(AppScene.Calculator);
        Assert.Equal(AppScene.Calculator, state.CurrentEntry.Scene);
        Assert.DoesNotContain(state.Entries, e => e.Scene == AppScene.Paint);
    }

    [Fact]
    public void Toggling_Reference_Flips_And_Raises_Changed()
    {
        var state = new SceneState();
        Assert.False(state.ShowDesktopReference);
        bool raised = false;
        state.Changed += () => raised = true;
        state.ToggleDesktopReference();
        Assert.True(state.ShowDesktopReference);
        Assert.True(raised);
        state.ToggleDesktopReference();
        Assert.False(state.ShowDesktopReference);
    }

    [Fact]
    public void Scene_Slugs_Roundtrip_And_Reject_Unknown()
    {
        // Deep links (/app/paint): every scene maps to a unique slug and
        // back; unknown/empty slugs return false (falling back to Smoke).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in Enum.GetValues<AppScene>())
        {
            string slug = SceneState.ToSlug(scene);
            Assert.False(string.IsNullOrWhiteSpace(slug));
            Assert.True(seen.Add(slug), $"duplicate slug {slug}");
            Assert.True(SceneState.TryParseSlug(slug, out var parsed));
            Assert.Equal(scene, parsed);
            Assert.True(SceneState.TryParseSlug(slug.ToUpperInvariant(), out _));
        }

        Assert.False(SceneState.TryParseSlug("no-such-app", out var fallback));
        Assert.Equal(AppScene.Smoke, fallback);
        Assert.False(SceneState.TryParseSlug(null, out _));
        Assert.False(SceneState.TryParseSlug("", out _));
    }

    [Fact]
    public void Selecting_Updates_Current_Entry()
    {
        var state = new SceneState();
        Assert.Equal(AppScene.Smoke, state.Current);
        bool raised = false;
        state.Changed += () => raised = true;
        state.Select(AppScene.Paint);
        Assert.True(raised);
        Assert.Equal(AppScene.Paint, state.CurrentEntry.Scene);
        Assert.Equal("Paint", state.CurrentEntry.Label);
    }
}
