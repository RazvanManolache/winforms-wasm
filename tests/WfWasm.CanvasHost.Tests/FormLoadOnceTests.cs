using WfWasm.CanvasHost;
using FormsForm = System.Windows.Forms.Form;

public sealed class FormLoadOnceTests
{
    [Fact]
    public void EnsureOnLoadOnce_Fires_Load_Exactly_Once()
    {
        using var form = new FormsForm();
        int loads = 0;
        form.Load += (_, _) => loads++;
        WinFormsAppHost.EnsureOnLoadOnce(form);
        WinFormsAppHost.EnsureOnLoadOnce(form);
        Assert.Equal(1, loads);
    }

    [Fact]
    public void EnsureOnLoadOnce_Skips_When_Native_Path_Already_Fired()
    {
        using var form = new FormsForm();
        int loads = 0;
        form.Load += (_, _) => loads++;
        typeof(FormsForm)
            .GetProperty("CalledOnLoad", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(form, true);
        WinFormsAppHost.EnsureOnLoadOnce(form);
        Assert.Equal(0, loads);
    }

    [Fact]
    public void EnsureOnLoadOnce_Second_Fire_Would_Duplicate_Bindings()
    {
        // Mirrors the syncfusion-grid-grouping failure: Form1_Load adds one
        // "Text" binding per TextBox, so a second Load throws
        // ArgumentException ("two bindings ... same property").
        using var form = new FormsForm();
        var box = new System.Windows.Forms.TextBox();
        form.Controls.Add(box);
        var source = new System.ComponentModel.BindingList<Row>();
        form.Load += (_, _) =>
        {
            box.DataBindings.Add("Text", source, "Value");
        };
        WinFormsAppHost.EnsureOnLoadOnce(form);
        WinFormsAppHost.EnsureOnLoadOnce(form);
        Assert.Single(box.DataBindings);
    }

    private sealed class Row
    {
        public string Value { get; set; } = string.Empty;
    }
}
