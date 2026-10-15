using WfWasm.Shim;

namespace WfWasm.CanvasHost;

/// <summary>
/// Shared scene selection: the left menu picks the app, Home paints it.
/// Each entry also carries the upstream repo the page mirrors (null when
/// the page is a pipeline-built demo with no upstream app).
/// </summary>
public sealed class SceneState
{
    public sealed record SceneEntry(
        AppScene Scene, string Label, string? SourceLabel, string? SourceUrl,
        string? DesktopReferenceFile, string? AppFolder);

    public IReadOnlyList<SceneEntry> Entries { get; private set; } = new[]
    {
        new SceneEntry(AppScene.Smoke, "Smoke form",
            "dotnet/samples · windowsforms/helloworld-sharedsource", "https://github.com/dotnet/samples",
            null, null),
        new SceneEntry(AppScene.Calendar, "Calendar",
            "justwesi/functional-calendar", "https://github.com/justwesi/functional-calendar",
            null, "calendar"),
        new SceneEntry(AppScene.Paint, "Paint",
            "Paint.NET v5.1.12 (Retargeted to WebAssembly)", "https://www.getpaint.net",
            null, "paintnet"),
        new SceneEntry(AppScene.Calculator, "Calculator",
            "albisnesstz/csharp-win-calculator", "https://github.com/albisnesstz/csharp-win-calculator",
            null, "calculator"),
        new SceneEntry(AppScene.Todo, "Todo list",
            "kasra10/simple-csharp-todolist", "https://github.com/kasra10/simple-csharp-todolist",
            null, "todo"),
        new SceneEntry(AppScene.TicTacToe, "Tic-tac-toe",
            "zakaria-sahki/tic-tac-toe-game", "https://github.com/zakaria-sahki/tic-tac-toe-game",
            null, "tictactoe"),
        new SceneEntry(AppScene.TelerikPhoto, "Telerik: Photo Album",
            "Telerik UI for WinForms demos", null,
            null, "telerik-photo"),
        new SceneEntry(AppScene.TelerikBookshelf, "Telerik: BookShelf",
            "Telerik UI for WinForms demos", null,
            null, "telerik-bookshelf"),
        new SceneEntry(AppScene.TelerikBugtracker, "Telerik: BugTracker",
            "Telerik UI for WinForms demos", null,
            null, "telerik-bugtracker"),
        new SceneEntry(AppScene.TelerikDemoAppsHub, "Telerik: DemoAppsHub",
            "Telerik UI for WinForms demos", null,
            null, "telerik-demoappshub"),
        new SceneEntry(AppScene.TelerikDiagram, "Telerik: Diagram",
            "Telerik UI for WinForms demos", null,
            null, "telerik-diagram"),
        new SceneEntry(AppScene.TelerikErp, "Telerik: ERP",
            "Telerik UI for WinForms demos", null,
            null, "telerik-erp"),
        new SceneEntry(AppScene.TelerikExportWithDpl, "Telerik: ExportWithDpl",
            "Telerik UI for WinForms demos", null,
            null, "telerik-exportwithdpl"),
        new SceneEntry(AppScene.TelerikFileExplorer, "Telerik: FileExplorer",
            "Telerik UI for WinForms demos", null,
            null, "telerik-fileexplorer"),
        new SceneEntry(AppScene.TelerikHotelApp, "Telerik: HotelApp",
            "Telerik UI for WinForms demos", null,
            null, "telerik-hotelapp"),
        new SceneEntry(AppScene.TelerikMedicalApp, "Telerik: MedicalApp",
            "Telerik UI for WinForms demos", null,
            null, "telerik-medicalapp"),
        new SceneEntry(AppScene.TelerikRichTextEditor, "Telerik: RichTextEditor",
            "Telerik UI for WinForms demos", null,
            null, "telerik-richtexteditor"),
        new SceneEntry(AppScene.TelerikRssReader, "Telerik: RssReader",
            "Telerik UI for WinForms demos", null,
            null, "telerik-rssreader"),
        new SceneEntry(AppScene.TelerikSpellCheck, "Telerik: SpellCheckAsYouType",
            "Telerik UI for WinForms demos", null,
            null, "telerik-spellcheckasyoutype"),
        new SceneEntry(AppScene.TelerikToast, "Telerik: ToastNotificationManager",
            "Telerik UI for WinForms demos", null,
            null, "telerik-toastnotificationmanagerexamples"),
    };

    public AppScene Current { get; private set; } = AppScene.Smoke;

    public void LimitToAvailableApps(IEnumerable<string> appIds)
    {
        var available = new HashSet<string>(appIds, StringComparer.OrdinalIgnoreCase);
        Entries = Entries.Where(e => e.AppFolder is null || available.Contains(e.AppFolder)).ToArray();
        if (!Entries.Any(e => e.Scene == Current)) Current = AppScene.Smoke;
        Changed?.Invoke();
    }

    public SceneEntry CurrentEntry => Entries.First(e => e.Scene == Current);

    /// <summary>URL slug per scene (`/app/paint`). Single source of truth
    /// for deep links: Home parses it on load/parameters-set and writes it
    /// back on every selection, so refresh returns to the same app.</summary>
    public static string ToSlug(AppScene scene) => scene switch
    {
        AppScene.Smoke => "smoke",
        AppScene.Calendar => "calendar",
        AppScene.Paint => "paint",
        AppScene.Calculator => "calculator",
        AppScene.Todo => "todo",
        AppScene.TicTacToe => "tic-tac-toe",
        AppScene.TelerikPhoto => "telerik-photo",
        AppScene.TelerikBookshelf => "telerik-bookshelf",
        AppScene.TelerikBugtracker => "telerik-bugtracker",
        AppScene.TelerikDemoAppsHub => "telerik-demoappshub",
        AppScene.TelerikDiagram => "telerik-diagram",
        AppScene.TelerikErp => "telerik-erp",
        AppScene.TelerikExportWithDpl => "telerik-exportwithdpl",
        AppScene.TelerikFileExplorer => "telerik-fileexplorer",
        AppScene.TelerikHotelApp => "telerik-hotelapp",
        AppScene.TelerikMedicalApp => "telerik-medicalapp",
        AppScene.TelerikRichTextEditor => "telerik-richtexteditor",
        AppScene.TelerikRssReader => "telerik-rssreader",
        AppScene.TelerikSpellCheck => "telerik-spellcheckasyoutype",
        AppScene.TelerikToast => "telerik-toastnotificationmanagerexamples",
        _ => "smoke",
    };

    public static bool TryParseSlug(string? slug, out AppScene scene)
    {
        foreach (var entry in Enum.GetValues<AppScene>())
        {
            if (string.Equals(slug, ToSlug(entry), StringComparison.OrdinalIgnoreCase))
            {
                scene = entry;
                return true;
            }
        }

        scene = AppScene.Smoke;
        return false;
    }

    /// <summary>Opens a separate desktop evidence panel alongside the live app.</summary>
    public bool ShowDesktopReference { get; private set; }

    public event Action? Changed;

    public void Select(AppScene scene)
    {
        if (!Entries.Any(e => e.Scene == scene))
            return;
        if (scene == Current)
            return;
        Current = scene;
        Changed?.Invoke();
    }

    public void ToggleDesktopReference()
    {
        ShowDesktopReference = !ShowDesktopReference;
        Changed?.Invoke();
    }
}
