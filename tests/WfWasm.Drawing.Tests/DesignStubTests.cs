extern alias WfDraw;

using WfPaintValueEventArgs = WfDraw::System.Drawing.Design.PaintValueEventArgs;
using WfRectangle = WfDraw::System.Drawing.Rectangle;
using WfToolboxItem = WfDraw::System.Drawing.Design.ToolboxItem;
using WfUITypeEditor = WfDraw::System.Drawing.Design.UITypeEditor;
using WfUITypeEditorEditStyle = WfDraw::System.Drawing.Design.UITypeEditorEditStyle;

public sealed class DesignStubTests
{
    private sealed class TestEditor : WfUITypeEditor
    {
    }

    [Fact]
    public void UITypeEditor_Answers_Desktop_Defaults()
    {
        var editor = new TestEditor();

        Assert.Equal(WfUITypeEditorEditStyle.None, editor.GetEditStyle());
        Assert.Equal(WfUITypeEditorEditStyle.None, editor.GetEditStyle(null));
        Assert.False(editor.GetPaintValueSupported());
        Assert.False(editor.GetPaintValueSupported(null));
        Assert.Same("v", editor.EditValue(null, "v"));
        Assert.Same("v", editor.EditValue(null, null, "v"));
        // No-op paints must not throw, even with nulls.
        editor.PaintValue(null);
        editor.PaintValue("v", null, new WfRectangle(0, 0, 4, 4));
    }

    [Fact]
    public void UITypeEditorEditStyle_Has_Desktop_Values()
    {
        Assert.Equal(1, (int)WfUITypeEditorEditStyle.None);
        Assert.Equal(2, (int)WfUITypeEditorEditStyle.Modal);
        Assert.Equal(3, (int)WfUITypeEditorEditStyle.DropDown);
    }

    [Fact]
    public void PaintValueEventArgs_Carries_All_Gap_Members()
    {
        var args = new WfPaintValueEventArgs(null, "v", null, new WfRectangle(1, 2, 3, 4));

        Assert.Equal(new WfRectangle(1, 2, 3, 4), args.Bounds);
        Assert.Null(args.Context);
        Assert.Null(args.Graphics);
        Assert.Same("v", args.Value);
    }

    [Fact]
    public void ToolboxItem_Answers_Headless_Defaults()
    {
        var item = new WfToolboxItem(typeof(string));

        Assert.Null(item.Bitmap);
        Assert.Equal(typeof(string).AssemblyQualifiedName, item.TypeName);
        Assert.Equal("String", item.DisplayName);
        Assert.Empty(item.CreateComponents());
        Assert.Empty(item.CreateComponents(null));
    }
}
