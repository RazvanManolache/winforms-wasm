using Mono.Cecil;
using Mono.Cecil.Cil;

// Pins the command-list target redirect (rev187): SetTarget(list) swaps
// the dc fill target to a scratch surface, GetTarget answers the active
// redirect, DrawImage(list) blits the scratch back in desktop order and
// consumes it (single-playback; the vendor palette draws the same list
// twice), Dispose stays a plain Ret (the vendor disposes the list
// before the final composite while the effect still holds it), the
// finalizer backstops never-drawn scratches. Without the redirect,
// immediate-mode fills land early and a later opaque DrawImage buries
// them (PDN Colors palette wells under the checkers layer).
public sealed class CommandListRedirectTests
{
    private static string PaintDir()
    {
        string dir = TestPaths.Repo(@"targets\paint.net");
        Assert.True(Directory.Exists(dir), "pristine PDN inputs missing: " + dir);
        return dir;
    }

    private static string BinDll(string project, string tfm, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", tfm, name + ".dll"));
        Assert.True(File.Exists(dll), "dependency not built: " + dll);
        return dll;
    }

    private static ModuleDefinition TransformCore(string tempOut)
    {
        string corePath = Path.Combine(PaintDir(), "PaintDotNet.Windows.Core.dll");
        Assert.True(File.Exists(corePath), "pristine PDN core missing: " + corePath);
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        WfWasm.Rewrite.AssemblyRewriter.Rewrite(
            corePath, tempOut,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
            cpuDll);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(PaintDir());
        return AssemblyDefinition.ReadAssembly(tempOut,
            new ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    private static List<Instruction> BodyOf(TypeDefinition type, string name, int argc)
    {
        var m = Assert.Single(
            type.Methods.Where(m => m.Name == name && m.Parameters.Count == argc && m.HasBody));
        return m.Body.Instructions.ToList();
    }

    private static bool Calls(List<Instruction> ins, string method)
    {
        return ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            && i.Operand is MethodReference mr && mr.Name == method);
    }

    [Fact]
    public void Redirect_SetTarget_Swaps_To_Scratch_And_Restores_On_Null()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"CmdRedirect_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = TransformCore(tempOut);
            var ctx = module.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            Assert.Contains(ctx.Fields, f => f.Name == "_redirList");

            var setTarget = BodyOf(ctx, "SetTarget", 1);
            Assert.Contains(setTarget, i => i.OpCode == OpCodes.Isinst
                && i.Operand is TypeReference tr && tr.Name == "CpuCommandList");
            Assert.True(Calls(setTarget, "CreateScratchTarget"),
                "SetTarget must allocate the redirect scratch");
            Assert.True(Calls(setTarget, "GetTransform"),
                "SetTarget must copy the live matrix into the scratch");
            Assert.True(Calls(setTarget, "SetTransform"),
                "SetTarget must copy the live matrix into the scratch");

            var getTarget = BodyOf(ctx, "GetTarget", 0);
            Assert.Contains(getTarget, i => i.OpCode == OpCodes.Ldfld
                && i.Operand is FieldReference fr && fr.Name == "_redirList");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Redirect_DrawImage_Blits_List_Scratch_And_Dispose_Frees()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"CmdBlit_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = TransformCore(tempOut);
            var list = module.GetType("PaintDotNet.Direct2D1.CpuCommandList");
            Assert.NotNull(list);
            Assert.Contains(list.Fields, f => f.Name == "_scratch");
            Assert.Contains(list.Fields, f => f.Name == "_saved");
            Assert.Contains(list.Interfaces, i => i.InterfaceType.FullName
                == "PaintDotNet.Direct2D1.IDeviceImage");

            // Vendor disposes the list before the final composite (the
            // effect holds it), so Dispose must stay a plain Ret while
            // the finalizer backstops never-drawn scratches.
            var dispose = BodyOf(list, "Dispose", 0);
            Assert.DoesNotContain(dispose, i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference dmr && dmr.Name == "FreeScratchTarget");
            var finalizer = Assert.Single(
                list.Methods.Where(m => m.Name == "Finalize" && m.Parameters.Count == 0 && m.HasBody));
            Assert.True(Calls(finalizer.Body.Instructions.ToList(), "FreeScratchTarget"),
                "Finalize must free a leftover redirect scratch");

            var ctx = module.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var drawImage = Assert.Single(ctx.Methods.Where(m => m.Name == "DrawImage"
                && m.Parameters.Count == 5
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Direct2D1.IDeviceImage"
                && m.HasBody));
            var ins = drawImage.Body.Instructions.ToList();
            Assert.Contains(ins, i => i.OpCode == OpCodes.Isinst
                && i.Operand is TypeReference tr && tr.Name == "CpuCommandList");
            Assert.True(Calls(ins, "BlitScratchOver"),
                "DrawImage must blit a redirected list scratch");
            Assert.True(Calls(ins, "FreeScratchTarget"),
                "DrawImage must consume the scratch on first playback");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Redirect_DrawImage_Forwards_Effect_Wrapped_List_Scratch()
    {
        // rev190: the palette wraps its filled list in a
        // ColorManagementEffect and draws the wrapper into another
        // list. The old identity no-op Ret dropped the fills (they sit
        // in the redirect scratch, never on the live surface), so the
        // composite replayed an empty list. The wrapper draw must pull
        // the wrapped list through Properties.Source (the vendor's own
        // channel: GetInput(0) NREs headless on the never-created inner
        // D2D effect) and blit its scratch.
        string tempOut = Path.Combine(Path.GetTempPath(), $"CmdCme_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = TransformCore(tempOut);
            var ctx = module.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var drawImage = Assert.Single(ctx.Methods.Where(m => m.Name == "DrawImage"
                && m.Parameters.Count == 5
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Direct2D1.IDeviceImage"
                && m.HasBody));
            var ins = drawImage.Body.Instructions.ToList();
            int cmeAt = ins.FindIndex(i => i.OpCode == OpCodes.Isinst
                && i.Operand is TypeReference tr && tr.Name == "ColorManagementEffect");
            Assert.True(cmeAt >= 0, "DrawImage must recognize the palette wrapper effect");
            int propsAt = ins.FindIndex(cmeAt, i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "get_Properties");
            Assert.True(propsAt > cmeAt,
                "DrawImage must read the wrapper effect properties");
            int srcAt = ins.FindIndex(propsAt, i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "get_Source");
            Assert.True(srcAt > propsAt,
                "DrawImage must read the wrapper Source input");
            int blitAt = ins.FindIndex(srcAt, i => i.OpCode == OpCodes.Call
                && i.Operand is MethodReference bmr && bmr.Name == "BlitScratchOver");
            Assert.True(blitAt > srcAt,
                "DrawImage must blit the wrapped list scratch into the target");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
