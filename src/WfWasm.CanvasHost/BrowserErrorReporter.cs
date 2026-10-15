using System.Net.Http.Json;
using System.Text;
using Microsoft.JSInterop;

namespace WfWasm.CanvasHost;

/// <summary>
/// Forwards caught managed exceptions (FirstChance probe, click handlers,
/// bootstrap failures) to the host server's <c>/api/client-errors</c>
/// endpoint, where they land in <c>logs/client-errors.jsonl</c> and the
/// <c>/errors</c> crash inventory. No-ops until <see cref="Initialize"/>
/// runs (Home.razor wires the app HttpClient on first render); console
/// logging always happens so nothing is lost when the backend is absent
/// (plain dev-server) or unreachable.
/// Signature scheme (shared with the JS hook, truncated server-side):
/// <c>ExceptionType|TopFrame|FirstMessageLine</c>.
/// </summary>
public static class BrowserErrorReporter
{
    private static HttpClient? s_http;

    public static void Initialize(HttpClient http) => s_http = http;

    // Input-trace hook: the host records pointer/keyboard actions in a
    // ring buffer; reports attach the recent slice so a crash carries
    // its repro steps (replay on desktop WinForms and compare).
    public static Func<string?>? InputContextProvider { get; set; }

    public static string ComputeSignature(Exception ex)
    {
        string type = ex.GetType().Name;
        string frame = TopFrame(ex.StackTrace);
        string line = (ex.Message ?? "").Split('\n')[0].Trim();
        return $"{type}|{frame}|{line}";
    }

    // StackTrace.GetFrame is unreliable under the WASM interpreter (it
    // yielded no frames, collapsing every site of an exception type into
    // one signature), while the rendered "at ..." text is present. Parse
    // the top frame from the text instead.
    public static string TopFrame(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
            return "";
        foreach (var raw in stackTrace.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("at ", StringComparison.Ordinal))
                continue;
            string method = line.Substring(3).Trim();
            int paren = method.IndexOf('(');
            if (paren >= 0)
                method = method.Substring(0, paren);
            method = StripBrackets(method);
            var parts = method.Split('.');
            return parts.Length >= 2
                ? parts[^2] + "." + parts[^1]
                : method;
        }

        return "";
    }

    // Stack text renders generic instantiations assembly-qualified
    // (Foo`1[[Bar, asm, ...]].Baz), whose dots and commas would poison
    // the site segment. Strip bracketed spans (innermost first, so
    // nesting collapses) before splitting.
    public static string StripBrackets(string value)
    {
        string s = value;
        int start;
        while ((start = s.LastIndexOf('[')) >= 0)
        {
            int end = s.IndexOf(']', start);
            if (end < 0)
                break;
            s = s.Remove(start, end - start + 1);
        }

        return s;
    }

    private static readonly object s_rateGate = new();
    private static readonly Dictionary<string, int> s_sentCounts = new(StringComparer.Ordinal);
    private const int MaxPostsPerSignature = 5;

    public static void Report(string source, Exception ex, string? topFrame = null)
    {
        // The report POST itself throws HttpRequestException/JSException/
        // TaskCanceledException when the backend is gone (notably during a
        // fatal runtime spiral): reporting those would amplify one crash
        // into thousands of fetch-error reports. Console only.
        if (ex is HttpRequestException || ex is JSException || ex is TaskCanceledException)
        {
            Console.WriteLine($"[report:{source}] suppressed-transport {ex.GetType().Name}: {(ex.Message ?? "").Split('\n')[0].Trim()}");
            return;
        }

        string signature;
        try
        {
            // Never TopFrame(ex.StackTrace) here: materializing an
            // exception's stack text resolves every frame's method, and
            // hostile frames abort the browser runtime (class.c
            // assertion, uncatchable). The site segment travels in the
            // explicit first-chance frame; other sources sign unlabeled.
            string frame = topFrame ?? "";
            string line = (ex.Message ?? "").Split('\n')[0].Trim();
            signature = $"{ex.GetType().Name}|{frame}|{line}";
        }
        catch
        {
            signature = "unknown||";
        }

        lock (s_rateGate)
        {
            s_sentCounts.TryGetValue(signature, out int sent);
            if (sent >= MaxPostsPerSignature)
            {
                Console.WriteLine($"[report:{source}] suppressed-rate {signature}");
                return;
            }

            s_sentCounts[signature] = sent + 1;
        }

        Console.WriteLine($"[report:{source}] {signature}");
        string chain = BuildInnerChainText(ex, out string[] innerLines);
        foreach (string innerLine in innerLines)
            Console.WriteLine($"[report:{source}] inner: {innerLine}");
        string? inputContext = null;
        try
        {
            inputContext = InputContextProvider?.Invoke();
            if (!string.IsNullOrEmpty(inputContext))
                Console.WriteLine($"[report:{source}] recent-input:\n{inputContext}");
        }
        catch
        {
            // Trace collection must never break reporting.
        }
        var http = s_http;
        if (http == null)
        {
            Console.WriteLine($"[report:{source}] no-backend {signature}");
            return;
        }
        // Inline dispatch, deliberately NOT Task.Run: single-threaded WASM
        // has no pool to run the queued work while the app is busy, so the
        // fetch never left the browser. The direct async call executes
        // synchronously up to the first await, which dispatches the fetch
        // immediately; completion needs no pump (fire-and-forget).
        _ = SendAsync();
        async Task SendAsync()
        {
            try
            {
                await http.PostAsJsonAsync("/api/client-errors", new[]
                {
                    new
                    {
                        signature,
                        message = ex.Message,
                        stack = chain,
                        source,
                        time = DateTimeOffset.UtcNow,
                        context = inputContext,
                    },
                });
            }
            catch
            {
                // Backend absent (dev-server) or unreachable: console line above is the record.
            }
        }
    }

    // Catch-site reports (click handlers, bootstrap) catch reflection
    // wrappers (TargetInvocationException) whose outer message/stack say
    // nothing: the actionable error is the inner chain. FirstChance does
    // not always surface it (observed: a calendar day-click filed only
    // the wrapper row, no inner row), so the chain travels inside the
    // posted stack text (server stores it as-is; existing signature rows
    // keep their outer-based identity). Never throws.
    private static string BuildInnerChainText(Exception ex, out string[] innerLines)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        try
        {
            var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance) { ex };
            int depth = 0;
            for (Exception? inner = ex.InnerException;
                inner != null && depth < 5 && seen.Add(inner);
                inner = inner.InnerException)
            {
                depth++;
                string firstLine = (inner.Message ?? "").Split('\n')[0].Trim();
                lines.Add($"{inner.GetType().Name}: {firstLine}");
                sb.AppendLine($"--- inner {depth}: {inner.GetType().FullName}: {firstLine}");
                // Stack text is never appended: materializing it
                // resolves every frame's method, and hostile frames
                // abort the browser runtime (class.c assertion,
                // uncatchable). Type+message lines above carry the
                // chain.
            }
        }
        catch
        {
            // Property getters on exotic exceptions must not break reporting.
        }

        innerLines = lines.ToArray();
        return sb.ToString();
    }
}
