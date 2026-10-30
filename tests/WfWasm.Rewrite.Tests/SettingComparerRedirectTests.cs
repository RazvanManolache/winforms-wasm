using Mono.Cecil;
using Mono.Cecil.Cil;

// Pins the EqualityComparer<TValue> headless retarget: the static
// get_Default() on value-type instantiations throws
// InvalidProgramException under Mono, killing ScalarSetting/DoubleSetting
// construction inside Setting.Initialize (touch-attributed:
// OnGetStaticEqualityComparer@0000 is the last print before the throw),
// and the Equals/GetHashCode virtuals on the resulting comparer would
// throw the same way. All three must route through the CpuPaint helpers;
// none may survive on the vendor token.
public sealed class SettingComparerRedirectTests
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

    private static void CountComparerCalls(ModuleDefinition module,
        out int virtSurvivors, out List<string> virtSites,
        out int helpers, out List<string> helperInOverride)
    {
        virtSurvivors = 0;
        helpers = 0;
        virtSites = new List<string>();
        helperInOverride = new List<string>();
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                // Recursion shape only when the override IS the dispatched
                // method: an override of EqualityComparer<T>.Equals /
                // GetHashCode. Ordinary POCO overrides (SpriteRenderInfo,
                // ProgramMessage, anonymous types, ...) legitimately
                // contain helper calls for field comparison: the helper
                // dispatches on the comparer, never back into the POCO.
                bool isComparerOverride = (method.Name == "Equals" || method.Name == "GetHashCode")
                    && method.IsVirtual && !method.IsNewSlot
                    && type.FullName.Contains("EqualityComparer");
                foreach (var ins in method.Body.Instructions)
                {
                    if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                        && ins.Operand is MethodReference mr)
                    {
                        if (mr.DeclaringType is GenericInstanceType gi
                            && gi.GenericArguments.Count == 1
                            && (((mr.Name == "get_Default" && ins.OpCode == OpCodes.Call)
                                    || ((mr.Name == "Equals" || mr.Name == "GetHashCode")
                                        && ins.OpCode == OpCodes.Callvirt)
                                    || (mr.Name == "OnGetStaticEqualityComparer"
                                        && ins.OpCode == OpCodes.Callvirt))
                                && (gi.ElementType.FullName == "System.Collections.Generic.EqualityComparer`1"
                                    || (mr.Name == "OnGetStaticEqualityComparer"
                                        && gi.ElementType.FullName.StartsWith(
                                            "PaintDotNet.Settings.Setting", StringComparison.Ordinal)))))
                        {
                            virtSurvivors++;
                            virtSites.Add(type.FullName + "::" + method.Name + " " + mr.Name);
                        }
                        else if ((mr.Name == "SettingEqualityComparer"
                                || mr.Name == "SettingComparerEquals"
                                || mr.Name == "SettingComparerHash"
                                || mr.Name == "SettingGetComparer")
                            && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint")
                        {
                            helpers++;
                            if (isComparerOverride)
                                helperInOverride.Add(type.FullName + "::" + method.Name);
                        }
                    }
                }
            }
            foreach (var nested in type.NestedTypes)
                stack.Push(nested);
        }
    }

    [Fact]
    public void EqualityComparer_Rerouted_Through_CpuPaint_Helpers()
    {
        string pdnOut = Path.Combine(Path.GetTempPath(), $"SetCmp_{Guid.NewGuid():N}.dll");
        try
        {
            using var pdn = Transform("paintdotnet.dll", pdnOut);
            CountComparerCalls(pdn, out int survivors, out var sites, out int helpers, out var helperInOverride);
            Assert.True(survivors == 0,
                "paintdotnet EqualityComparer survivors: " + string.Join("; ", sites));
            Assert.True(helpers > 0,
                "paintdotnet must route EqualityComparer through the helpers");
            Assert.True(helperInOverride.Count == 0,
                "recursion shape: comparer helper inside Equals/GetHashCode override: "
                + string.Join("; ", helperInOverride));
        }
        finally
        {
            try { File.Delete(pdnOut); } catch { }
        }
    }

    private static void CountConverterValidateCalls(ModuleDefinition module,
        out int survivors, out List<string> sites,
        out int helpers, out List<string> helperInOverride)
    {
        survivors = 0;
        helpers = 0;
        sites = new List<string>();
        helperInOverride = new List<string>();
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                bool isValidateOverride = method.Name == "OnValidateValue"
                    && method.IsVirtual && !method.IsNewSlot;
                foreach (var ins in method.Body.Instructions)
                {
                    if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                        && ins.Operand is MethodReference mr)
                    {
                        if (mr.Name == "OnValidateValue" && mr.Parameters.Count == 2
                            && ins.OpCode == OpCodes.Callvirt
                            && (mr.DeclaringType.FullName == "PaintDotNet.Settings.SettingConverter"
                                || (mr.DeclaringType is GenericInstanceType gi
                                    && gi.ElementType.FullName == "PaintDotNet.Settings.SettingConverter`1"
                                    && gi.GenericArguments.Count == 1)))
                        {
                            survivors++;
                            sites.Add(type.FullName + "::" + method.Name);
                        }
                        else if (mr.Name == "SettingConverterValidateValue"
                            && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint")
                        {
                            helpers++;
                            if (isValidateOverride)
                                helperInOverride.Add(type.FullName + "::" + method.Name);
                        }
                    }
                }
            }
            foreach (var nested in type.NestedTypes)
                stack.Push(nested);
        }
    }

    [Fact]
    public void Converter_OnValidateValue_Rerouted_Through_CpuPaint_Helper()
    {
        string pdnOut = Path.Combine(Path.GetTempPath(), $"SetCnv_{Guid.NewGuid():N}.dll");
        try
        {
            using var pdn = Transform("paintdotnet.dll", pdnOut);
            CountConverterValidateCalls(pdn, out int survivors, out var sites, out int helpers, out var helperInOverride);
            Assert.True(survivors == 0,
                "paintdotnet converter OnValidateValue survivors: " + string.Join("; ", sites));
            Assert.True(helpers > 0,
                "paintdotnet must route converter OnValidateValue through the helper");
            Assert.True(helperInOverride.Count == 0,
                "recursion shape: converter helper inside OnValidateValue override: "
                + string.Join("; ", helperInOverride));
        }
        finally
        {
            try { File.Delete(pdnOut); } catch { }
        }
    }
}
