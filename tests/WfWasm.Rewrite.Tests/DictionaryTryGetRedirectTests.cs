using Mono.Cecil;
using Mono.Cecil.Cil;

// Pins the Dictionary<K,V>.TryGetValue headless retarget: the
// Dictionary<Control,int> instantiation kind-17s under Mono (touch-logged
// live: UIHelpers.IsControlPaintingEnabled@0018 prints its touch, then the
// callvirt throws "Expected reference type but got type kind 17", killing
// PdnToolBar/ArrowButton/CaptionControl paints every boot; the identical
// shaped Dictionary<DocumentWorkspace,ImageItem> call executes fine).
// Closed instantiations reroute through CpuPaint.DictTryGetValue (exact
// IDictionary contract: miss reads null+false, null key throws
// ArgumentNullException); open-generic contexts stay on the vendor token.
public sealed class DictionaryTryGetRedirectTests
{
    private static string PaintDir()
    {
        string dir = @"E:\AI\Code\WindowsForms\targets\paint.net";
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

    private static ModuleDefinition Transform(string fileName, string tempOut)
    {
        string input = Path.Combine(PaintDir(), fileName);
        Assert.True(File.Exists(input), "pristine PDN input missing: " + input);
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        WfWasm.Rewrite.AssemblyRewriter.Rewrite(
            input, tempOut,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
            cpuDll);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(PaintDir());
        return AssemblyDefinition.ReadAssembly(tempOut,
            new ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    private static void CountDictTryGet(ModuleDefinition module,
        out int closed, out int open, out List<string> closedSites)
    {
        closed = 0;
        open = 0;
        closedSites = new List<string>();
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var ins in method.Body.Instructions)
                {
                    if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                        && ins.Operand is MethodReference mr && mr.Name == "TryGetValue"
                        && mr.DeclaringType is GenericInstanceType gi
                        && gi.ElementType.FullName == "System.Collections.Generic.Dictionary`2"
                        && gi.GenericArguments.Count == 2)
                    {
                        bool hasParam = gi.GenericArguments.Any(a =>
                            a is GenericParameter || (a is GenericInstanceType gai
                                && gai.GenericArguments.Any(ga => ga is GenericParameter)));
                        if (hasParam) open++;
                        else
                        {
                            closed++;
                            closedSites.Add(type.FullName + "::" + method.Name);
                        }
                    }
                }
            }
            foreach (var nested in type.NestedTypes)
                stack.Push(nested);
        }
    }

    private static int CountHelperCalls(ModuleDefinition module)
    {
        int n = 0;
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var ins in method.Body.Instructions)
                {
                    if (ins.OpCode == OpCodes.Call
                        && ins.Operand is MethodReference mr && mr.Name == "DictTryGetValue"
                        && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint")
                        n++;
                }
            }
            foreach (var nested in type.NestedTypes)
                stack.Push(nested);
        }
        return n;
    }

    [Fact]
    public void Dictionary_TryGetValue_Rerouted_Through_IDictionary_Helper()
    {
        string sysOut = Path.Combine(Path.GetTempPath(), $"DictSys_{Guid.NewGuid():N}.dll");
        string pdnOut = Path.Combine(Path.GetTempPath(), $"DictPdn_{Guid.NewGuid():N}.dll");
        try
        {
            using var sys = Transform("PaintDotNet.SystemLayer.dll", sysOut);
            CountDictTryGet(sys, out int sysClosed, out int sysOpen, out var sysSites);
            Assert.True(sysClosed == 0,
                "SystemLayer Dictionary.TryGetValue survivors: " + string.Join("; ", sysSites));
            Assert.True(CountHelperCalls(sys) > 0,
                "SystemLayer must route Dictionary.TryGetValue through the helper");

            using var pdn = Transform("paintdotnet.dll", pdnOut);
            CountDictTryGet(pdn, out int pdnClosed, out int pdnOpen, out var pdnSites);
            Assert.True(pdnClosed == 0,
                "paintdotnet Dictionary.TryGetValue survivors: " + string.Join("; ", pdnSites));
        }
        finally
        {
            try { File.Delete(sysOut); } catch { }
            try { File.Delete(pdnOut); } catch { }
        }
    }

    // rev254 CLOSEOUT: the rev240 guard neutralization is reverted
    // (exonerated in vivo by the oracle). Pin the pristine guard: it must
    // NOT be the 2-instruction constant-true stub.
    [Fact]
    public void ControlPaintingGuard_Restored_To_Pristine()
    {
        string sysOut = Path.Combine(Path.GetTempPath(), $"GuardSys_{Guid.NewGuid():N}.dll");
        try
        {
            using var sys = Transform("PaintDotNet.SystemLayer.dll", sysOut);
            var uiHelpers = sys.Types.FirstOrDefault(t =>
                t.FullName == "PaintDotNet.SystemLayer.UIHelpers");
            Assert.True(uiHelpers is not null, "UIHelpers missing from transformed SystemLayer");
            var guard = uiHelpers.Methods.FirstOrDefault(m =>
                m.Name == "IsControlPaintingEnabled" && m.Parameters.Count == 1);
            Assert.True(guard is not null && guard.HasBody,
                "IsControlPaintingEnabled missing from transformed SystemLayer");
            var ins = guard.Body.Instructions;
            Assert.False(ins.Count == 2 && ins[0].OpCode == OpCodes.Ldc_I4_1 && ins[1].OpCode == OpCodes.Ret,
                "CLOSEOUT: guard must not be the constant-true stub");
        }
        finally
        {
            try { File.Delete(sysOut); } catch { }
        }
    }
}
