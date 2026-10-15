using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;

public sealed class TictactoePaintTests
{
    private static FormsButton FindEmptyCell(FormsControl root)
    {
        foreach (FormsControl c in root.Controls)
        {
            if (c is FormsButton btn && btn.Tag?.ToString() == "?")
                return btn;
            FormsButton? nested = c.Controls.Count > 0 ? FindEmptyCell(c) : null;
            if (nested is not null)
                return nested;
        }

        throw new InvalidOperationException("No empty board cell found.");
    }

    private static int AbsX(FormsControl c, int formX)
    {
        int x = formX;
        for (FormsControl? current = c; current is not null && current is not FormsForm; current = current.Parent)
            x += current.Left;
        return x;
    }

    private static int AbsY(FormsControl c, int formY)
    {
        int y = formY + 30; // host title bar height, cf. WinFormsAppHost.Paint
        for (FormsControl? current = c; current is not null && current is not FormsForm; current = current.Parent)
            y += current.Top;
        return y;
    }

    [Fact]
    public void Move_Paints_Piece_Image_Command()
    {
        // Clicking a cell sets its piece image; the paint walk must then
        // emit a DrawImageCommand (covers the shared centered-bitmap path
        // used by Button and ToolStripItem).
        // Same regenerable-data coupling as the fork file reference in this
        // test project: the rewritten app must exist in E:\tmp.
        string app = TestPaths.Repo(@".artifacts\runtime\TicTacToe.dll");
        Assert.True(File.Exists(app), $"supply the optional local converted test fixture: {app}");
        var ttt = Assembly.LoadFrom(app);
        // The file is TicTacToe.dll but the assembly name kept its spaces
        // ("Tic Tac Toe"); the host looks it up as "TicTacToe" (which the
        // browser loader satisfies by file). Bridge the names here.
        ResolveEventHandler? resolver = (_, args) =>
            args.Name.Split(',')[0].Trim() == "TicTacToe" ? ttt : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            var host = new WinFormsAppHost();
            host.SelectScene(AppScene.TicTacToe);
            var form = host.GetOrCreateForm(AppScene.TicTacToe);
            Assert.NotNull(form);

            FormsButton cell = FindEmptyCell(form);
            host.OnClick(AbsX(cell, form.Location.X) + (cell.Width / 2),
                AbsY(cell, form.Location.Y) + (cell.Height / 2));
            Assert.Equal("X", cell.Tag?.ToString());

            var recorder = new RecordingRenderer();
            host.Paint(recorder);
            Assert.Contains(recorder.Commands, c => c is DrawImageCommand);
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }
}
