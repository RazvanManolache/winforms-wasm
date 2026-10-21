// Fork-owned headless replacement for upstream WebBrowser (Compile-
// Removed in WfWasm.WinForms.csproj). The upstream control forwards
// everything to IWebBrowser2 COM, which does not exist in the browser.
// This shape keeps the public surface apps bind to, retains DocumentText
// and the current Url as managed state, completes navigations
// synchronously (Navigating -> Navigated -> DocumentCompleted), and
// answers every shell verb with a benign default. The pane paints blank
// until a DocumentText renderer exists (generic phase-2 work).

using System.ComponentModel;
using System.IO;
using System.Text;

namespace System.Windows.Forms;

public class WebBrowser : WebBrowserBase
{
    private Uri? _url;
    private string _documentText = string.Empty;
    private string _statusText = string.Empty;

    public WebBrowser()
    {
    }

    public bool AllowNavigation { get; set; } = true;

    public bool AllowWebBrowserDrop { get; set; } = true;

    public bool ScriptErrorsSuppressed { get; set; }

    public bool WebBrowserShortcutsEnabled { get; set; } = true;

    public bool CanGoBack => false;

    public bool CanGoForward => false;

    // No DOM in the browser: document access is benign-null.
    public HtmlDocument? Document => null;

    public Stream? DocumentStream
    {
        get => _documentText.Length == 0 ? null : new MemoryStream(Encoding.UTF8.GetBytes(_documentText));
        set
        {
            if (value is null)
            {
                _documentText = string.Empty;
                return;
            }

            using var reader = new StreamReader(value, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            _documentText = reader.ReadToEnd().TrimEnd('\0');
        }
    }

    public string DocumentText
    {
        get => _documentText;
        set => _documentText = value ?? string.Empty;
    }

    public string DocumentTitle => string.Empty;

    public string DocumentType => string.Empty;

    public WebBrowserEncryptionLevel EncryptionLevel => WebBrowserEncryptionLevel.Insecure;

    public bool IsBusy => false;

    public bool IsOffline { get; set; }

    public bool IsWebBrowserContextMenuEnabled { get; set; } = true;

    public object? ObjectForScripting { get; set; }

    public new Padding Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    public new event EventHandler? PaddingChanged
    {
        add => base.PaddingChanged += value;
        remove => base.PaddingChanged -= value;
    }

    public WebBrowserReadyState ReadyState => WebBrowserReadyState.Complete;

    public virtual string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value ?? string.Empty;
            StatusTextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Uri? Url
    {
        get => _url;
        set => Navigate(value);
    }

    public Version Version => new(11, 0);

    public override bool Focused => base.Focused;

    public bool ScrollBarsEnabled { get; set; } = true;

    public bool GoBack() => false;

    public bool GoForward() => false;

    public void GoHome() => Navigate("about:home");

    public void GoSearch() => Navigate("about:search");

    public void Navigate(Uri? url) => NavigateCore(url);

    public void Navigate(string urlString) => NavigateCore(ToUri(urlString));

    public void Navigate(Uri? url, string? targetFrameName) => NavigateCore(url);

    public void Navigate(string urlString, string? targetFrameName) => NavigateCore(ToUri(urlString));

    public void Navigate(Uri? url, bool newWindow) => NavigateCore(url);

    public void Navigate(string urlString, bool newWindow) => NavigateCore(ToUri(urlString));

    public void Navigate(Uri? url, string? targetFrameName, byte[]? postData, string? additionalHeaders) => NavigateCore(url);

    public void Navigate(string urlString, bool newWindow, string? targetFrameName, byte[]? postData, string? additionalHeaders)
        => NavigateCore(ToUri(urlString));

    public void Print()
    {
    }

    public override void Refresh()
    {
    }

    public void Refresh(WebBrowserRefreshOption opt)
    {
    }

    public void ShowPageSetupDialog()
    {
    }

    public void ShowPrintDialog()
    {
    }

    public void ShowPrintPreviewDialog()
    {
    }

    public void ShowPropertiesDialog()
    {
    }

    public void ShowSaveAsDialog()
    {
    }

    public void Stop()
    {
    }

    public event EventHandler? CanGoBackChanged;

    public event EventHandler? CanGoForwardChanged;

    public event WebBrowserDocumentCompletedEventHandler? DocumentCompleted;

    public event EventHandler? DocumentTitleChanged;

    public event EventHandler? EncryptionLevelChanged;

    public event EventHandler? FileDownload;

    public event WebBrowserNavigatedEventHandler? Navigated;

    public event WebBrowserNavigatingEventHandler? Navigating;

    public event CancelEventHandler? NewWindow;

    public event WebBrowserProgressChangedEventHandler? ProgressChanged;

    public event EventHandler? StatusTextChanged;

    private static Uri? ToUri(string? urlString)
        => string.IsNullOrWhiteSpace(urlString) ? null : new Uri(urlString, UriKind.RelativeOrAbsolute);

    private void NavigateCore(Uri? url)
    {
        Uri target = url ?? new Uri("about:blank");
        var navigating = new WebBrowserNavigatingEventArgs(target, string.Empty);
        Navigating?.Invoke(this, navigating);
        if (navigating.Cancel)
        {
            return;
        }

        _url = target;
        _documentText = string.Empty;
        Navigated?.Invoke(this, new WebBrowserNavigatedEventArgs(target));
        DocumentCompleted?.Invoke(this, new WebBrowserDocumentCompletedEventArgs(target));
        DocumentTitleChanged?.Invoke(this, EventArgs.Empty);
    }
}
