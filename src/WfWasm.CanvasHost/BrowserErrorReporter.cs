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

    // App-attribution hook: Home.razor answers the current scene slug so
    // every report carries its app (server filters on it via ?app=).
    public static Func<string?>? AppProvider { get; set; }
    public static Func<string?>? AppLabelProvider { get; set; }
    public static Func<string?>? SessionProvider { get; set; }

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

    // WF-R-NRE-0730: JS-first transport seam. A void [JSImport] dispatch
    // creates no managed Task, so the report can never fault inside
    // JavaScriptExports.CompleteTask (previously: synchronous NRE there
    // took the runtime down, exit 1). Tests set this to observe/short-
    // circuit dispatch; production uses the canvas-bridge fetch hook.
    public static Func<string, string, bool>? JsTransportOverride;

    private static readonly System.Text.Json.JsonSerializerOptions s_payloadJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    // In-flight report tasks must stay rooted until the fetch settles:
    // a fire-and-forget POST whose Task is collected before the browser
    // completes it surfaces as a frameless NullReferenceException out of
    // JavaScriptExports.CompleteTask and kills the runtime (exit 1).
    private static readonly HashSet<Task> s_inflight = new();
    private static readonly object s_inflightGate = new();

    public static void Report(string source, Exception ex, string? topFrame = null)
    {
        // The report POST itself throws HttpRequestException/JSException/
        // TaskCanceledException when the backend is gone (notably during a
        // fatal runtime spiral): reporting those would amplify one crash
        // into thousands of fetch-error reports. Console only.
        if (source != "app-load" && (ex is HttpRequestException || ex is JSException || ex is TaskCanceledException))
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

        string? reportApp = Capture(AppProvider);
        string? reportLabel = Capture(AppLabelProvider);
        string? reportSession = Capture(SessionProvider);
        string rateKey = reportApp + "\n" + reportSession + "\n" + signature;
        lock (s_rateGate)
        {
            s_sentCounts.TryGetValue(rateKey, out int sent);
            if (sent >= MaxPostsPerSignature)
            {
                Console.WriteLine($"[report:{source}] suppressed-rate {signature}");
                return;
            }

            s_sentCounts[rateKey] = sent + 1;
        }

        Console.WriteLine(reportApp == null
            ? $"[report:{source}] {signature}"
            : $"[report:{source}] app={reportApp} {signature}");
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
        string url = http?.BaseAddress is null
            ? "/api/client-errors"
            : new Uri(http.BaseAddress, "api/client-errors").ToString();
        string json = BuildReportJson(signature, ex.Message, chain, source, DateTimeOffset.UtcNow,
            inputContext, reportApp, reportLabel, reportSession);
        // JS-first: the bridge fetch returns nothing to managed code, so no
        // Task ever crosses the interop boundary here (see the seam note).
        if (TryDispatchJs(url, json))
            return;
        if (http == null)
        {
            Console.WriteLine($"[report:{source}] no-backend {signature}");
            return;
        }
        // Inline dispatch, deliberately NOT Task.Run: single-threaded WASM
        // has no pool to run the queued work while the app is busy, so the
        // fetch never left the browser. The direct async call executes
        // synchronously up to the first await, which dispatches the fetch
        // immediately; completion needs no pump (fire-and-forget, but
        // rooted in s_inflight until it settles — see the field note).
        Task send;
        try
        {
            send = SendAsync(http, url, json);
        }
        catch
        {
            return;
        }
        lock (s_inflightGate) s_inflight.Add(send);
        try
        {
            send.ContinueWith(
                t =>
                {
                    lock (s_inflightGate) s_inflight.Remove(t);
                    try { _ = t.Exception; } catch { }
                },
                TaskScheduler.Default);
        }
        catch
        {
            // No pool to schedule the cleanup (single-threaded WASM):
            // the entry stays rooted for the session; the rate gate
            // bounds how many can accumulate per signature.
        }
        async Task SendAsync(HttpClient client, string postUrl, string payload)
        {
            try
            {
                using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                await client.PostAsync(postUrl, content);
            }
            catch
            {
                // Backend absent (dev-server) or unreachable: console line above is the record.
            }
        }
    }

    // Single-batch report payload, shared by the JS and HTTP transports so
    // both post byte-identical bodies (same field order, Web JSON defaults).
    public static string BuildReportJson(string signature, string? message, string stack, string source,
        DateTimeOffset time, string? context, string? app, string? appLabel, string? sessionId)
    {
        return System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new
            {
                signature,
                message,
                stack,
                source,
                time,
                context,
                app,
                appLabel,
                sessionId,
            },
        }, s_payloadJson);
    }

    private static bool TryDispatchJs(string url, string json)
    {
        // WF-R-FF2-0900 triage marker: console-only, identifies which
        // transport a report took (temporary; remove after the faulting
        // Task is identified).
        try
        {
            if (JsTransportOverride is not null)
                return JsTransportOverride(url, json);
            if (OperatingSystem.IsBrowser())
            {
                CanvasBridge.WfwasmReportError(url, json);
                Console.WriteLine("[report:transport js]");
                return true;
            }
        }
        catch (Exception dispatchEx)
        {
            // Hook absent (stale shell) or unreachable: HTTP fallback below.
            try { Console.WriteLine($"[report:transport http-fallback {dispatchEx.GetType().Name}]"); } catch { }
        }
        try { Console.WriteLine("[report:transport http]"); } catch { }
        return false;
    }

    private static string? Capture(Func<string?>? provider)
    {
        try { string? value = provider?.Invoke(); return string.IsNullOrWhiteSpace(value) ? null : value; }
        catch { return null; }
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
