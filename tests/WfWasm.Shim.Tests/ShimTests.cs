using WfWasm.Shim;

public class MessageLoopTests
{
    [Fact]
    public void Post_PumpRunsInOrder()
    {
        var loop = new BrowserMessageLoop();
        var seen = new List<int>();
        loop.Post(() => seen.Add(1));
        loop.Post(() => seen.Add(2));

        Assert.Equal(2, loop.PendingCount);
        Assert.Equal(2, loop.Pump());
        Assert.Equal(new[] { 1, 2 }, seen);
        Assert.Equal(0, loop.Pump());
    }

    [Fact]
    public void Pump_RespectsMaxActions()
    {
        var loop = new BrowserMessageLoop();
        int ran = 0;
        loop.Post(() => ran++);
        loop.Post(() => ran++);

        Assert.Equal(1, loop.Pump(1));
        Assert.Equal(1, ran);
        Assert.Equal(1, loop.Pump(1));
        Assert.Equal(2, ran);
    }

    [Fact]
    public void Send_OnLoopThread_RunsInline()
    {
        var loop = new BrowserMessageLoop();
        Assert.False(loop.InvokeRequired);
        Assert.Equal(42, loop.Send(() => 42));
        Assert.Equal(0, loop.PendingCount);
    }

    [Fact]
    public async Task Send_FromBackgroundThread_CompletesAfterPump()
    {
        var loop = new BrowserMessageLoop();
        Task<int> task = Task.Run(() =>
        {
            Assert.True(loop.InvokeRequired);
            return loop.Send(() => 42);
        });

        Assert.True(SpinWait.SpinUntil(() => loop.PendingCount == 1, TimeSpan.FromSeconds(10)));
        Assert.Equal(1, loop.Pump());
        Assert.Equal(42, await task);
    }

    [Fact]
    public async Task Send_FromBackgroundThread_SurfacesException()
    {
        var loop = new BrowserMessageLoop();
        Task task = Task.Run(() =>
            loop.Send<object?>(() => throw new InvalidOperationException("boom")));

        Assert.True(SpinWait.SpinUntil(() => loop.PendingCount == 1, TimeSpan.FromSeconds(10)));
        loop.Pump();
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    [Fact]
    public async Task PostAsync_CompletesWithResultAndException()
    {
        var loop = new BrowserMessageLoop();
        Task<int> ok = loop.PostAsync(() => 7);
        Task<int> bad = loop.PostAsync<int>(() => throw new InvalidOperationException("bad"));

        loop.Pump();

        Assert.Equal(7, await ok);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bad);
    }

    [Fact]
    public void Post_Exception_SurfacesFromPump()
    {
        var loop = new BrowserMessageLoop();
        loop.Post(() => throw new InvalidOperationException("pump-boom"));
        Assert.Throws<InvalidOperationException>(() => loop.Pump());
    }

    [Fact]
    public async Task Pump_OffLoopThread_Throws()
    {
        var loop = new BrowserMessageLoop();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => loop.Pump()));
    }
}

public class DialogStackTests
{
    [Fact]
    public async Task ShowClose_ReturnsResult()
    {
        var stack = new ModalDialogStack<string, string>();
        int changed = 0;
        stack.Changed += (_, _) => changed++;

        Task<string> pending = stack.ShowAsync("page1");
        Assert.Equal("page1", stack.Top);
        Assert.True(stack.IsModalTop);

        stack.CloseTop("ok");
        Assert.Equal("ok", await pending);
        Assert.Equal(0, stack.Count);
        Assert.Equal(2, changed);
    }

    [Fact]
    public async Task NavigateTo_KeepsTaskReplacesPage()
    {
        var stack = new ModalDialogStack<string, string>();
        Task<string> pending = stack.ShowAsync("initial");
        stack.NavigateTo("progress");
        Assert.Equal("progress", stack.Top);
        Assert.False(pending.IsCompleted);

        stack.NavigateTo("finished");
        stack.CloseTop("done");
        Assert.Equal("done", await pending);
    }

    [Fact]
    public async Task Modeless_And_NestedOrder()
    {
        var stack = new ModalDialogStack<string, int>();
        Task<int> first = stack.ShowAsync("first", modal: false);
        Assert.False(stack.IsModalTop);
        Task<int> second = stack.ShowAsync("second");
        Assert.Equal("second", stack.Top);

        Assert.True(stack.TryCloseTop(2));
        Assert.Equal(2, await second);
        Assert.Equal("first", stack.Top);
        stack.CloseTop(1);
        Assert.Equal(1, await first);
    }

    [Fact]
    public void CloseEmpty_ReturnsFalseAndThrows()
    {
        var stack = new ModalDialogStack<string, string>();
        Assert.False(stack.TryCloseTop("x"));
        Assert.Throws<InvalidOperationException>(() => stack.CloseTop("x"));
        Assert.Throws<InvalidOperationException>(() => stack.Top);
        Assert.Throws<InvalidOperationException>(() => stack.NavigateTo("x"));
    }
}

public class DialogModelTests
{
    private static DialogPageModel TwoButtonPage()
    {
        var page = new DialogPageModel { Heading = "H", Text = "T" };
        page.Buttons.Add(new DialogButtonModel("yes", "&Yes") { IsDefault = true });
        page.Buttons.Add(new DialogButtonModel("no", "&No"));
        return page;
    }

    [Fact]
    public void Click_EnabledButton_ClosesWithId()
    {
        var page = TwoButtonPage();
        Assert.Equal(new DialogClickOutcome(true, "yes"), page.ClickButton("yes"));
        Assert.Equal("yes", page.DefaultButtonId);
    }

    [Fact]
    public void Click_DisabledOrMissingButton_IsNoOp()
    {
        var page = TwoButtonPage();
        page.Buttons[0].Enabled = false;
        Assert.Equal(new DialogClickOutcome(false, null), page.ClickButton("yes"));
        Assert.Equal(new DialogClickOutcome(false, null), page.ClickButton("nope"));
        Assert.Null(page.DefaultButtonId);
    }

    [Fact]
    public void Click_AllowCloseFalse_FiresButStaysOpen()
    {
        var page = TwoButtonPage();
        page.Buttons[1].AllowClose = false;
        Assert.Equal(new DialogClickOutcome(false, "no"), page.ClickButton("no"));
    }

    [Fact]
    public void Verification_Toggle_RaisesBothEvents()
    {
        var page = new DialogPageModel { VerificationText = "I know" };
        int pageChanged = 0, verificationChanged = 0;
        page.PageChanged += (_, _) => pageChanged++;
        page.VerificationChanged += (_, _) => verificationChanged++;

        page.VerificationChecked = true;
        Assert.Equal(1, verificationChanged);
        Assert.Equal(1, pageChanged);

        page.VerificationChecked = true;
        Assert.Equal(1, verificationChanged);
    }

    [Fact]
    public void LiveEdits_RaisePageChanged()
    {
        var page = new DialogPageModel();
        int count = 0;
        page.PageChanged += (_, _) => count++;

        page.Text = "Reconnecting in 4 seconds...";
        page.ProgressState = DialogProgressState.Normal;
        page.ProgressValue = 60;
        page.ExpanderText = "Progress: 60 %";
        Assert.Equal(4, count);
    }
}
