// Browser-safe fallback for PaintDotNet's environment-inventory diagnostics
// (DiagnosticsGenerator.GetFormattedText). Headless, the inventory's
// GPU/package/service probes fault and the blocking .Result wraps the
// ORIGINAL error in an AggregateException, killing the error dialog the
// text was meant to fill. The original exception text is composed by PDN's
// caller and is unaffected; only the environment section degrades, and it
// says so explicitly. Total function: never throws, deterministic shape.
namespace WfWasm.Drawing;

public static class PdnDiagnostics
{
    public static string GetErrorDetailsFallback(object? settings, bool includeMachineInfo)
    {
        try
        {
            string runtime;
            string arch;
            int processors;
            try
            {
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            }
            catch
            {
                runtime = "unknown runtime";
            }
            try
            {
                arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
            }
            catch
            {
                arch = "unknown architecture";
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[WASM headless diagnostics]");
            sb.Append("Runtime: ").Append(runtime).Append(" (").Append(arch).AppendLine(")");
            if (includeMachineInfo)
            {
                try
                {
                    processors = System.Environment.ProcessorCount;
                }
                catch
                {
                    processors = 0;
                }
                sb.Append("Processors: ").Append(processors).AppendLine();
            }
            sb.AppendLine("Note: full environment inventory (GPU, package identity, " +
                "services, diagnostics settings) is unavailable in the browser; " +
                "only the reported error above is authoritative.");
            return sb.ToString();
        }
        catch
        {
            return "[WASM headless diagnostics] environment inventory unavailable in the browser.";
        }
    }
}
