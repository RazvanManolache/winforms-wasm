extern alias WfDraw;

using System.Reflection;
using WfWasm.Shim;
using DrawImage = WfDraw::System.Drawing.Image;

// Guards the member shapes that static-shaped `call` refs bind against.
// Roslyn emits `call` without HasThis when the framework target is static
// (e.g. base.DefaultForeColor); the runtime resolves such refs only against
// static defs, so these members must stay public static to match WinForms.
public sealed class StaticShapeTests
{
    [Theory]
    [InlineData(typeof(Control), nameof(Control.DefaultBackColor))]
    [InlineData(typeof(Control), nameof(Control.DefaultForeColor))]
    [InlineData(typeof(Control), nameof(Control.DefaultFont))]
    public void Control_Defaults_Are_Public_Static(Type type, string name)
    {
        PropertyInfo prop = Assert.Single(type.GetProperties(BindingFlags.Public | BindingFlags.Static), p => p.Name == name);
        Assert.NotNull(prop.GetMethod);
        MethodInfo get = prop.GetMethod!;
        Assert.True(get.IsPublic);
        Assert.False(get.IsVirtual);
        Assert.False(get.IsFamily);
        Assert.Null(type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
    }

    // Inherited interface mapping only flows through virtual methods: a plain
    // non-virtual InvokeRequired leaves PDN interface slots (e.g.
    // IEffectConfigForm) unfilled and the type fails to load.
    [Fact]
    public void InvokeRequired_Is_Public_Virtual()
    {
        PropertyInfo prop = Assert.Single(
            typeof(Control).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.Name == nameof(Control.InvokeRequired));
        Assert.NotNull(prop.GetMethod);
        MethodInfo m = prop.GetMethod!;
        Assert.True(m.IsPublic);
        Assert.True(m.IsVirtual);
        Assert.False(m.IsStatic);
    }

    // App IL news controls up via explicit parameterless .ctor refs; a C#
    // optional parameter does not emit that metadata slot.
    [Theory]
    [InlineData(typeof(Control))]
    [InlineData(typeof(Form))]
    [InlineData(typeof(GroupBox))]
    [InlineData(typeof(TabPage))]
    [InlineData(typeof(ListBox))]
    [InlineData(typeof(ComboBox))]
    [InlineData(typeof(CheckBox))]
    [InlineData(typeof(RadioButton))]
    [InlineData(typeof(MenuStrip))]
    [InlineData(typeof(ToolStripStatusLabel))]
    [InlineData(typeof(ToolStripMenuItem))]
    public void Controls_Expose_Public_Parameterless_Ctor(Type type)
    {
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
    }

    // Rewritten apps callvirt base-class slots on derived instances without any
    // runtime type check; a wrong base type silently misreads field offsets.
    [Theory]
    [InlineData(typeof(MenuStrip), typeof(ToolStrip))]
    [InlineData(typeof(StatusStrip), typeof(ToolStrip))]
    [InlineData(typeof(ToolStrip), typeof(ScrollableControl))]
    [InlineData(typeof(ToolStripStatusLabel), typeof(ToolStripLabel))]
    [InlineData(typeof(ToolStripLabel), typeof(ToolStripItem))]
    [InlineData(typeof(Control.ControlCollection), typeof(ArrangedElementCollection))]
    [InlineData(typeof(Form), typeof(ContainerControl))]
    public void Strip_Hierarchy_Matches_WinForms(Type type, Type expectedBase)
    {
        Assert.Equal(expectedBase, type.BaseType);
    }

    [Fact]
    public void Controls_Enumerate_As_ArrangedElementCollection()
    {
        var parent = new Panel();
        parent.Controls.Add(new Label("a"));
        ArrangedElementCollection coll = parent.Controls;
        int n = 0;
        foreach (object? item in coll)
        {
            Assert.IsType<Label>(item);
            n++;
        }
        Assert.Equal(1, n);
    }

    [Fact]
    public void StatusLabel_Adds_To_Item_Collection()
    {
        var strip = new StatusStrip();
        strip.Items.Add(new ToolStripStatusLabel("ready"));
        Assert.Single(strip.Items);
    }

    [Fact]
    public void ToolStrip_Exposes_Internal_ToolTip()
    {
        MethodInfo? m = typeof(ToolStrip).GetMethod(
            "get_ToolTip",
            BindingFlags.NonPublic | BindingFlags.Instance,
            Type.EmptyTypes);
        Assert.NotNull(m);
        Assert.False(m.IsPublic);
        Assert.False(m.IsStatic);
    }

    [Fact]
    public void CreateDisabledImage_Is_Public_Static()
    {
        MethodInfo? m = typeof(ToolStripRenderer).GetMethod(
            nameof(ToolStripRenderer.CreateDisabledImage),
            BindingFlags.Public | BindingFlags.Static,
            new[] { typeof(DrawImage) });
        Assert.NotNull(m);
        Assert.False(m.IsVirtual);
        Assert.False(m.IsAbstract);
    }
}
