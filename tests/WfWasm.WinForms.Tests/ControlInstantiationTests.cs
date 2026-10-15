using System.Reflection;
using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

public class ControlInstantiationTests
{
    private static void WithAssemblyResolver(Action action)
    {
        ResolveEventHandler resolver = (sender, args) =>
        {
            var an = new AssemblyName(args.Name);
            if (an.Name == "System.Windows.Forms" || an.Name == "System.Windows.Forms.Primitives" || an.Name == "WfWasm.WinForms")
                return Assembly.LoadFrom(TestPaths.Repo(@".artifacts\runtime\WfWasm.WinForms.dll"));
            if (an.Name == "System.ComponentModel.Primitives")
                return typeof(System.ComponentModel.Component).Assembly;
            if (an.Name == "System.Drawing.Common")
                return typeof(System.Drawing.Font).Assembly;
            if (an.Name == "WfWasm.Drawing")
                return Assembly.LoadFrom(TestPaths.Repo(@"src\WfWasm.Drawing\bin\Debug\net9.0\WfWasm.Drawing.dll"));
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == an.Name);
        };
        var listeners = new System.Diagnostics.TraceListener[System.Diagnostics.Trace.Listeners.Count];
        System.Diagnostics.Trace.Listeners.CopyTo(listeners, 0);
        System.Diagnostics.Trace.Listeners.Clear();
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            action();
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            foreach (var l in listeners)
                System.Diagnostics.Trace.Listeners.Add(l);
        }
    }

    [Fact]
    public void CanInstantiateFormAndControls()
    {
        using var form = new Form();
        var button = new Button { Text = "Click me" };
        form.Controls.Add(button);
        Assert.Equal("Click me", button.Text);
        Assert.Single(form.Controls);
    }

    [Fact]
    public void CanCreateControlHandle()
    {
        using var form = new Form();
        form.Visible = true;
        Assert.NotNull(form);
    }

    [Fact]
    public void CanLoadAndRunRewrittenCalculator()
    {
        WithAssemblyResolver(() =>
        {
            var asm = Assembly.LoadFrom(TestPaths.Repo(@".artifacts\runtime\Calculator.dll"));
            var formType = asm.GetType("Calculator.Form1")!;
            using var form = (Form)Activator.CreateInstance(formType)!;
            form.Visible = true;
            var txtField = formType.GetField("txtDisplay", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var txt = (TextBox)txtField.GetValue(form)!;
            Assert.Equal("0", txt.Text);

            var btn1 = (Button)formType.GetField("btn1", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            var btnAdd = (Button)formType.GetField("btnAdd", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            var btn3 = (Button)formType.GetField("btn3", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            var btnEqual = (Button)formType.GetField("btnEqual", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

            btn1.PerformClick();
            btnAdd.PerformClick();
            btn3.PerformClick();
            btnEqual.PerformClick();

            Assert.Equal("4", txt.Text);
        });
    }

    [Fact]
    public void CanLoadAndRunRewrittenTicTacToe()
    {
        WithAssemblyResolver(() =>
        {
            var asm = Assembly.LoadFrom(TestPaths.Repo(@".artifacts\runtime\TicTacToe.dll"));
            var formType = asm.GetType("Tic_Tac_Toe.Form1")!;
            using var form = (Form)Activator.CreateInstance(formType)!;
            form.Visible = true;
            Assert.Equal("Tic Tac Toe", form.Text);

            var btn1 = (Button)formType.GetField("button1", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            var lblTurn = (Label)formType.GetField("lblPlayerTurn", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            Assert.Equal("Player 1", lblTurn.Text);

            btn1.PerformClick();

            Assert.Equal("X", btn1.Tag?.ToString());
            Assert.Equal("Player 2", lblTurn.Text);
        });
    }

    [Fact]
    public void CanLoadAndRunRewrittenSimpleDailyToDoList()
    {
        WithAssemblyResolver(() =>
        {
            var asm = Assembly.LoadFrom(TestPaths.Repo(@".artifacts\runtime\SimpleDailyToDoList.dll"));
            var formType = asm.GetType("SimpleDailyToDoList.ToDoListDashBoard_FR")!;
            using var form = (Form)Activator.CreateInstance(formType)!;
            form.Visible = true;
            Assert.Equal("DashBoard", form.Text);

            var tb = (TextBox)formType.GetField("Task_TB", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            var addBtn = (Button)formType.GetField("Add_BTN", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            var newTasks = (ListView)formType.GetField("NewTasks_LTV", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

            tb.Text = "Port WinForms to Wasm";
            addBtn.PerformClick();

            Assert.Single(newTasks.Items);
            Assert.Equal("Port WinForms to Wasm", newTasks.Items[0].Text);
            Assert.Equal(string.Empty, tb.Text);
        });
    }

    [Fact]
    public void CanLoadAndRunRewrittenCalendar()
    {
        WithAssemblyResolver(() =>
        {
            var asm = Assembly.LoadFrom(TestPaths.Repo(@".artifacts\runtime\Calendar.dll"));
            var formType = asm.GetType("grp2Calendar.Form1")!;
            using var form = (Form)Activator.CreateInstance(formType)!;
            form.Visible = true;
            Assert.Equal("Group 2 Calendar", form.Text);
            Assert.True(form.Controls.Count >= 10);
        });
    }
}
