namespace WfWasm.Rewrite.Fixture;

/// <summary>
/// Tiny real WinForms app used as rewrite-fixture input: a button that
/// greets the textbox content. Compiled against desktop WinForms; the
/// rewrite tests repoint it at the shim assemblies.
/// </summary>
public sealed class GreeterForm : Form
{
    private readonly TextBox _name = new() { Left = 12, Top = 12, Width = 200 };
    private readonly Button _go = new() { Left = 12, Top = 44, Text = "Greet" };
    private readonly Label _out = new() { Left = 12, Top = 80, Width = 200 };

    public GreeterForm()
    {
        Text = "Greeter";
        ClientSize = new System.Drawing.Size(230, 120);
        // Forked-identity guard: ThreadExceptionEventHandler lives under a
        // System.Threading facade scope, not Forms. The analyzer must match
        // the delegate signature, not just the add_ThreadException arity.
        Application.ThreadException += (_, _) => { };
        _go.Click += (_, _) =>
        {
            _out.Text = "Hello, " + _name.Text + "!";
            MessageBox.Show(_out.Text, "Greeting");
        };
        Controls.Add(_name);
        Controls.Add(_go);
        Controls.Add(_out);
    }
}

/// <summary>
/// Attribute-blob fixture: TypeConverter carries a Type-valued ctor arg
/// (typeof(SizeConverter)) encoded inline in the blob, mirroring Telerik's
/// [Editor(..., typeof(UITypeEditor))] on RadImageShape. The rewrite must
/// remap its scope to the fork.
/// Regression: Telerik theme BackgroundShape null from a dangling blob
/// scope (System.Drawing 4.0.0.0).
/// </summary>
[System.ComponentModel.TypeConverter(typeof(System.Drawing.SizeConverter))]
public sealed class ConverterFixture
{
}

/// <summary>
/// BaseDirectory fixture: data loaders ask AppContext (static) or the
/// current AppDomain (instance) where the exe lives; the browser answers
/// null, so the rewrite points both getters at WasmAppIdentity.
/// </summary>
public static class BaseDirectoryFixture
{
    public static string ViaAppContext() => System.AppContext.BaseDirectory;

    public static string ViaAppDomain() => System.AppDomain.CurrentDomain.BaseDirectory;
}
