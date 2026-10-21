namespace WfWasm.WinForms.Tests;

// Headless WebBrowser overlay: upstream parenting drove the IE ActiveX
// state machine and threw InvalidCastException with no COM in the
// browser, killing form construction for every WebBrowser host.
// Regression: telerik-rssreader RadForm1.InitializeComponent at
// toolWindow1.Controls.Add(webBrowser1) (form-create FATAL).
public sealed class WebBrowserOverlayTests
{
    [Fact]
    public void WebBrowser_Parenting_Into_Form_Does_Not_Activate_ActiveX()
    {
        using var form = new System.Windows.Forms.Form();
        using var browser = new System.Windows.Forms.WebBrowser { Dock = System.Windows.Forms.DockStyle.Fill };
        form.Controls.Add(browser);

        Assert.Same(form, browser.Parent);
        Assert.Equal(System.Windows.Forms.WebBrowserReadyState.Complete, browser.ReadyState);
    }

    [Fact]
    public void WebBrowser_DocumentText_Is_Retained()
    {
        using var browser = new System.Windows.Forms.WebBrowser();
        browser.DocumentText = "<html><body>feed article</body></html>";

        Assert.Contains("feed article", browser.DocumentText);
        Assert.Null(browser.Document);
        Assert.Equal("", browser.DocumentTitle);
    }

    [Fact]
    public void WebBrowser_Navigate_Completes_Synchronously()
    {
        using var browser = new System.Windows.Forms.WebBrowser();
        var order = new List<string>();
        browser.Navigating += (_, _) => order.Add("navigating");
        browser.Navigated += (_, e) => order.Add("navigated:" + e.Url);
        browser.DocumentCompleted += (_, e) => order.Add("completed:" + e.Url);

        browser.Navigate("https://example.com/feed");

        Assert.Equal(new Uri("https://example.com/feed"), browser.Url);
        Assert.Equal(
            new[] { "navigating", "navigated:https://example.com/feed", "completed:https://example.com/feed" },
            order);
        Assert.False(browser.GoBack());
        Assert.False(browser.CanGoForward);
    }
}
