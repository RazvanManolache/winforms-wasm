using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;

public sealed class DocumentPointerTests
{
    [Theory]
    [InlineData(FormsMouseButtons.Left, 0, 16, 1)]
    [InlineData(FormsMouseButtons.Left, 2, 16, 2)]
    [InlineData(FormsMouseButtons.Left, 1, 16, 0)]
    [InlineData(FormsMouseButtons.Right, 0, 32, 3)]
    [InlineData(FormsMouseButtons.Right, 2, 32, 4)]
    [InlineData(FormsMouseButtons.Right, 1, 32, 0)]
    [InlineData(FormsMouseButtons.Middle, 0, 16, 1)]
    public void Pointer_Buttons_Map_To_Pdn_Enums(
        FormsMouseButtons buttons, int kindInt, int btn, int change)
    {
        // The host forwards document strokes to PDN's pointer pipeline as
        // raw enum values; right must encode Right/pressed/released or the
        // tool draws primary. Regression: right-click drew nothing.
        // (kind: 0=Pressed, 1=Moved, 2=Released.)
        var kind = (WinFormsAppHost.PointerEventKind)kindInt;
        Assert.Equal((btn, change), WinFormsAppHost.ToPointerButtonInts(buttons, kind));
    }

    [Fact]
    public void RightDown_On_Document_Records_Right_Button_Stroke()
    {
        // The canvas absorbs right-click (preventDefault + ToButtons in
        // Home.razor) and the host routes it into a document stroke with
        // the right button, which PDN renders in the secondary color.
        // Live proof: right-drag erases a black stroke (white secondary).
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        host.SetDocumentPageBounds(100, 100, 400, 400);
        host.OnMouseDown(200, 200, FormsMouseButtons.Right);
        try
        {
            string trail = host.RecentInputContext();
            Assert.Contains("Right", trail);
            Assert.Contains("doc", trail);
            host.OnMouseUp(250, 200, FormsMouseButtons.Right);
            Assert.Contains("up", host.RecentInputContext());
        }
        finally
        {
            host.SetDocumentPageBounds(0, 0, 0, 0);
        }
    }
}
