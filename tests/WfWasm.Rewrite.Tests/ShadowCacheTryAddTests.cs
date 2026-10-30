using Mono.Cecil;
using Mono.Cecil.Cil;

// Pins the LayersStrip/ImageStrip shadow-cache Add retarget: the guarded
// Dictionary<SizeInt32,DeviceBitmap>.Add still throws (Key:50,50) on every
// render because the interpreter mis-evaluates value-type-key Find while
// Insert detects the duplicate — every strip render fails (caught -> blank
// Layers). ONLY the two shadow-cache Add sites route through
// CpuPaint.DictTryAddValue (scan-backed add-if-absent, never throws);
// every other Dictionary.Add in the module stays pristine (a global Add
// retarget would mask real dup bugs).
public sealed class ShadowCacheTryAddTests
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

    private static bool IsShadowAdd(MethodReference mr) =>
        mr.Name == "Add"
        && mr.DeclaringType is GenericInstanceType gi
        && gi.ElementType.FullName == "System.Collections.Generic.Dictionary`2"
        && gi.GenericArguments.Count == 2;

    [Fact]
    public void ShadowCache_Add_Sites_Routed_Through_TryAdd_Helper()
    {
        string pdnOut = Path.Combine(Path.GetTempPath(), $"ShadowAdd_{Guid.NewGuid():N}.dll");
        try
        {
            using var pdn = Transform("paintdotnet.dll", pdnOut);
            int helperCalls = 0;
            var shadowMethods = new HashSet<string>(StringComparer.Ordinal);
            int pristineShadowAdds = 0;
            int pristineElsewhere = 0;
            var stack = new Stack<TypeDefinition>(pdn.Types);
            while (stack.Count > 0)
            {
                var type = stack.Pop();
                bool inScope = type.Name == "VerticalImageStrip" || type.Name == "ImageStrip";
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    bool isShadow = inScope && (method.Name == "DrawItemImageShadow"
                        || method.Name == "DrawImageItemImageShadow");
                    foreach (var ins in method.Body.Instructions)
                    {
                        if (ins.OpCode == OpCodes.Call
                            && ins.Operand is MethodReference hmr && hmr.Name == "DictTryAddValue"
                            && hmr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint")
                        {
                            helperCalls++;
                            shadowMethods.Add(type.FullName + "::" + method.Name);
                        }
                        if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                            && ins.Operand is MethodReference mr && IsShadowAdd(mr))
                        {
                            if (isShadow) pristineShadowAdds++;
                            else pristineElsewhere++;
                        }
                    }
                }
                foreach (var nested in type.NestedTypes)
                    stack.Push(nested);
            }
            Assert.True(helperCalls >= 2,
                $"expected both shadow Add sites retargeted, got {helperCalls}: " + string.Join("; ", shadowMethods));
            Assert.True(pristineShadowAdds == 0,
                "pristine shadow-cache Add survivors must be zero");
            Assert.True(pristineElsewhere > 0,
                "unrelated Dictionary.Add sites must stay pristine");
        }
        finally
        {
            try { File.Delete(pdnOut); } catch { }
        }
    }

    private static bool IsShadowClear(MethodReference mr) =>
        mr.Name == "Clear"
        && mr.DeclaringType is GenericInstanceType gi
        && gi.ElementType.FullName == "System.Collections.Generic.Dictionary`2"
        && gi.GenericArguments.Count == 2;

    [Fact]
    public void ShadowCache_Clear_Sites_Routed_Through_ShadowClear_Helper()
    {
        string pdnOut = Path.Combine(Path.GetTempPath(), $"ShadowClear_{Guid.NewGuid():N}.dll");
        try
        {
            var pdn = Transform("paintdotnet.dll", pdnOut);
            int helperCalls = 0;
            int pristineShadowClears = 0;
            var clearMethods = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<TypeDefinition>(pdn.Types);
            while (stack.Count > 0)
            {
                var type = stack.Pop();
                bool inScope = type.Name == "VerticalImageStrip" || type.Name == "ImageStrip";
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    bool isShadow = inScope && (method.Name == "ClearDropShadowDeviceBitmapCache"
                        || method.Name == "OnDispose" || method.Name == "OnThemeConfigChanged");
                    foreach (var ins in method.Body.Instructions)
                    {
                        if (ins.OpCode == OpCodes.Call
                            && ins.Operand is MethodReference hmr && hmr.Name == "DictShadowClear"
                            && hmr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint")
                        {
                            helperCalls++;
                            clearMethods.Add(type.FullName + "::" + method.Name);
                        }
                        if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                            && ins.Operand is MethodReference mr && IsShadowClear(mr) && isShadow)
                        {
                            pristineShadowClears++;
                        }
                    }
                }
                foreach (var nested in type.NestedTypes)
                    stack.Push(nested);
            }
            Assert.True(helperCalls >= 3,
                $"expected all shadow Clear sites retargeted, got {helperCalls}: " + string.Join("; ", clearMethods));
            Assert.True(pristineShadowClears == 0,
                "pristine shadow-cache Clear survivors must be zero");
        }
        finally
        {
            try { File.Delete(pdnOut); } catch { }
        }
    }
}
