using System.Net;
using System.Text;

namespace WfWasm.ErrorLog;

/// <summary>
/// Renders the crash inventory (<c>GET /errors</c>) as a plain HTML table:
/// signature, status, count, first/last seen, source, message. No JS, no
/// CSS framework; readable from any browser or curl.
/// </summary>
public static class ErrorInventoryPage
{
    public static string Render(IReadOnlyList<ErrorIndexEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
        sb.Append("<title>WfWasm crash inventory</title></head><body>");
        sb.Append("<h1>WfWasm crash inventory</h1>");
        sb.Append("<p>").Append(entries.Count).Append(" known signatures.</p>");
        sb.Append("<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\">");
        sb.Append("<tr><th>Signature</th><th>Status</th><th>Count</th><th>Post-fix</th>");
        sb.Append("<th>Apps</th><th>First seen</th><th>Last seen</th><th>Source</th><th>Message</th></tr>");
        foreach (var e in entries.OrderByDescending(e => e.LastSeen))
        {
            sb.Append("<tr><td>").Append(E(e.Signature)).Append("</td><td>").Append(E(e.Status));
            if (!string.IsNullOrEmpty(e.FixedNote))
                sb.Append(" (").Append(E(e.FixedNote)).Append(')');
            sb.Append("</td><td>").Append(e.Count).Append("</td><td>").Append(e.PostFixCount).Append("</td><td>");
            sb.Append(E(string.Join(",", e.Apps ?? []))).Append("</td><td>");
            sb.Append(E(e.FirstSeen.ToString("u"))).Append("</td><td>").Append(E(e.LastSeen.ToString("u")));
            sb.Append("</td><td>").Append(E(e.Source)).Append("</td><td>").Append(E(e.Message)).Append("</td></tr>");
        }

        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
