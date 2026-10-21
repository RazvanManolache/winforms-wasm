using System.Drawing;
using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.CanvasHost.Tests.Fixtures;
using Xunit;

// Validate that the browser fixture's script bridge really exercises Graphics
// with the intended state. These CPU-reference tests do not certify a GPU.
public sealed class GpuContractFixtureTests
{
    public static IEnumerable<object[]> Cases()
    {
        const string white = "{\"op\":\"clear\",\"color\":[255,255,255,255]}";
        yield return ["brush mutation", $"[{white},{{\"op\":\"brushMutation\"}}]",
            new[] { (16, 16, Color.Red.ToArgb()), (48, 16, Color.Blue.ToArgb()) }];
        yield return ["image mutation and clone", $"[{white},{{\"op\":\"imageMutation\"}}]",
            new[] { (16, 16, Color.Red.ToArgb()), (48, 16, Color.Blue.ToArgb()), (80, 16, Color.Red.ToArgb()) }];
        yield return ["clip hole", $"[{white}," + """
            {"op":"clip","rect":[8,8,64,40]},
            {"op":"exclude","rect":[24,16,16,16]},
            {"op":"fill","rect":[0,0,128,96],"color":[255,0,0,255]}]
            """, new[] { (16, 20, Color.Red.ToArgb()), (30, 20, Color.White.ToArgb()), (80, 20, Color.White.ToArgb()) }];
        yield return ["device clip", $"[{white}," + """
            {"op":"transform","matrix":[1,0,0,1,20,10]},
            {"op":"clip","rect":[0,0,16,16]},
            {"op":"transform","matrix":[1,0,0,1,0,0]},
            {"op":"fill","rect":[0,0,80,60],"color":[255,0,0,255]}]
            """, new[] { (24, 14, Color.Red.ToArgb()), (4, 4, Color.White.ToArgb()), (44, 14, Color.White.ToArgb()) }];
        yield return ["clear ignores clip", $"[{white}," + """
            {"op":"clip","rect":[8,8,16,16]},
            {"op":"clear","color":[0,255,0,255]}]
            """, new[] { (16, 16, Color.Lime.ToArgb()), (100, 80, Color.Lime.ToArgb()) }];
        yield return ["transparent source copy", $"[{white}," + """
            {"op":"fill","rect":[8,8,48,32],"color":[255,0,0,255]},
            {"op":"composite","value":"copy"},
            {"op":"fill","rect":[24,8,16,32],"color":[0,0,0,0]}]
            """, new[] { (16, 20, Color.Red.ToArgb()), (30, 20, 0), (48, 20, Color.Red.ToArgb()) }];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Test_Program_Uses_Actual_Graphics_State(string name, string program, (int X, int Y, int Argb)[] samples)
    {
        try
        {
            WinFormsExecution.Run(() =>
            {
                using var control = new GpuContractControl();
                using var image = new Bitmap(128, 96);
                using var graphics = Graphics.FromImage(image);
                control.SetProgram(program);
                using var args = new System.Windows.Forms.PaintEventArgs(graphics, new Rectangle(0, 0, 128, 96));
                typeof(GpuContractControl).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [args]);
                Assert.Equal(1, control.ApplicationPaintCount);
                foreach (var sample in samples)
                    Assert.True(image.GetPixel(sample.X, sample.Y).ToArgb() == sample.Argb,
                        $"{name}: pixel {sample.X},{sample.Y} did not match the fixture contract");
            });
        }
        finally { System.Windows.Forms.WasmRepaintGate.DrainDirty(out _); }
    }
}
