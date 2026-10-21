using WfWasm.CanvasHost;

public sealed class CaretOverlayTests
{
    private sealed class FakeCaret
    {
        public string Color => "Black";
        public System.Drawing.PointF Position => new(10, 20);
        public float Width => 2;
        public float Height => 22;
        public string Visibility => "Visible";
        public System.Drawing.Rectangle ControlBoundingRectangle => new(100, 50, 0, 0);
    }

    private sealed class FakePresenter
    {
        public FakeCaret Caret { get; } = new();
    }

    private sealed class FakeNode
    {
        private readonly object[] _kids;
        public FakeNode(params object[] kids) => _kids = kids;
        public System.Collections.Generic.List<object> Children => new(_kids);
    }

    private sealed class FakeControl
    {
        public object RootElement { get; }
        public FakeControl(object root) => RootElement = root;
    }

    [Fact]
    public void CaretProbeInfo_Resolves_Public_Tree_And_Geometry()
    {
        // crect=(100,50) pos=(10,20) size=2x22: the tick region for
        // that caret is x=100+10-2=108, y=50+20-22=48 (int casts).
        var control = new FakeControl(new FakeNode(new FakeNode(new FakePresenter())));
        var rc = new System.Drawing.Rectangle(108, 48, 30, 40);
        string info = WinFormsAppHost.CaretProbeInfo(control, rc);
        Assert.Contains("caret=FakeCaret", info);
        Assert.Contains("color=Black", info);
        Assert.Contains("pos=10.0,20.0", info);
        Assert.Contains("visible=Visible", info);
        Assert.Contains("geom=ok", info);
    }

    [Fact]
    public void CaretProbeInfo_Names_Misses()
    {
        // No RootElement member at all.
        Assert.Equal("no-root-element", WinFormsAppHost.CaretProbeInfo(new object(), new System.Drawing.Rectangle()));
        // Tree without any Caret property.
        var control = new FakeControl(new FakeNode(new FakeNode()));
        Assert.Equal("caret-not-found", WinFormsAppHost.CaretProbeInfo(control, new System.Drawing.Rectangle()));
    }

    [Fact]
    public void FindCaretElement_Stops_At_Empty_Subtrees()
    {
        Assert.Null(WinFormsAppHost.FindCaretElement(new FakeNode(), 0));
        var presenter = new FakePresenter();
        Assert.Same(presenter.Caret, WinFormsAppHost.FindCaretElement(presenter, 0));
    }
}
