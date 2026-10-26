using Mono.Cecil;
using Mono.Cecil.Cil;

// Pins the ScaledResource<T> headless retarget (rev195): the
// generic-instantiation callvirt (ScaledResource<Image>::get_Resource /
// get_ScaleFactor) trips Mono class loading headless ("Expected
// reference type but got type kind 17"), killing ArrowButton's whole
// OnPaint and the PdnToolBar/numeric self-paints through
// MethodBaseInvoker.InvokeWithOneArg. The trivial backing-field getters
// are rerouted through reflection helpers that carry no generic token
// (call + castclass/unbox of the original return-type token).
public sealed class ScaledResourceRedirectTests
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

    // Closed instantiations (concrete T, e.g. Image) must all be
    // rerouted. Open-context sites (the instantiation argument is or
    // contains a generic parameter, e.g. inside ScaledResource<T>
    // itself) stay on the vendor token: rewriting them would emit a
    // dangling generic-parameter cast and kill the interpreter.
    private static void CountGenericScaledCalls(ModuleDefinition module,
        out int closed, out int open)
    {
        closed = 0;
        open = 0;
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var ins in method.Body.Instructions)
                {
                    if (ins.OpCode == OpCodes.Callvirt
                        && ins.Operand is MethodReference mr
                        && (mr.Name == "get_Resource" || mr.Name == "get_ScaleFactor")
                        && mr.DeclaringType is GenericInstanceType gi
                        && gi.ElementType.FullName == "PaintDotNet.Resources.ScaledResource`1"
                        && gi.GenericArguments.Count == 1)
                    {
                        if (ContainsGenericParameter(gi.GenericArguments[0]))
                            open++;
                        else
                            closed++;
                    }
                }
            }
            foreach (var nested in type.NestedTypes)
                stack.Push(nested);
        }
    }

    private static bool ContainsGenericParameter(TypeReference tr)
    {
        if (tr is GenericParameter) return true;
        if (tr is GenericInstanceType gi)
        {
            foreach (var arg in gi.GenericArguments)
                if (ContainsGenericParameter(arg)) return true;
            return false;
        }
        if (tr is TypeSpecification spec)
            return spec.ElementType != null && ContainsGenericParameter(spec.ElementType);
        return false;
    }

    private static int CountHelperCalls(ModuleDefinition module, string name)
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
                    if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                        && ins.Operand is MethodReference mr && mr.Name == name
                        && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint")
                        n++;
                }
            }
            foreach (var nested in type.NestedTypes)
                stack.Push(nested);
        }
        return n;
    }

    // The getter's declared return is the open generic parameter T:
    // emitting it as a castclass/unbox operand kills the WASM
    // interpreter at transform time (whole-app ExitStatus death, no
    // managed exception). The follow-up token must be the inflated
    // instantiation argument, never a generic parameter.
    private static void AssertInflatedFollowups(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                var ins = method.Body.Instructions;
                for (int k = 0; k < ins.Count; k++)
                {
                    if ((ins[k].OpCode == OpCodes.Call)
                        && ins[k].Operand is MethodReference mr
                        && (mr.Name == "ScaledResourceGet" || mr.Name == "ScaledResourceFactor")
                        && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint")
                    {
                        Assert.True(k + 1 < ins.Count,
                            $"helper call at end of {method.FullName}");
                        var follow = ins[k + 1];
                        Assert.True(follow.OpCode == OpCodes.Castclass
                            || follow.OpCode == OpCodes.Unbox_Any,
                            $"helper call in {method.FullName} must be followed by castclass/unbox.any");
                        Assert.False(follow.Operand is GenericParameter,
                            $"follow-up token in {method.FullName} must be inflated, not generic parameter T");
                    }
                }
            }
            foreach (var nested in type.NestedTypes)
                stack.Push(nested);
        }
    }

    [Fact]
    public void ScaledResource_Getters_Rerouted_Through_Reflection_Helpers()
    {
        string fwOut = Path.Combine(Path.GetTempPath(), $"ScaledFw_{Guid.NewGuid():N}.dll");
        string pdnOut = Path.Combine(Path.GetTempPath(), $"ScaledPdn_{Guid.NewGuid():N}.dll");
        try
        {
            using var fw = Transform("PaintDotNet.Framework.dll", fwOut);
            CountGenericScaledCalls(fw, out int fwClosed, out int fwOpen);
            Assert.Equal(0, fwClosed);
            Assert.True(fwOpen > 0, "expected some open-context sites to stay on the vendor token");
            Assert.True(CountHelperCalls(fw, "ScaledResourceGet") > 0,
                "Framework must route ScaledResource getters through the helper");
            Assert.True(CountHelperCalls(fw, "ScaledResourceFactor") > 0,
                "Framework must route ScaledResource factors through the helper");

            using var pdn = Transform("paintdotnet.dll", pdnOut);
            CountGenericScaledCalls(pdn, out int pdnClosed, out int pdnOpen);
            Assert.Equal(0, pdnClosed);
            Assert.True(CountHelperCalls(pdn, "ScaledResourceGet") > 0,
                "paintdotnet must route ScaledResource getters through the helper");
            AssertInflatedFollowups(fw);
            AssertInflatedFollowups(pdn);
        }
        finally
        {
            try { File.Delete(fwOut); } catch { }
            try { File.Delete(pdnOut); } catch { }
        }
    }
}
