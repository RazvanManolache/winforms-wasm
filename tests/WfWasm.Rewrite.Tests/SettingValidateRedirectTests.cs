using Mono.Cecil;
using Mono.Cecil.Cil;

// Pins the Setting<TValue>.OnValidateValueT headless retarget: direct
// callvirt on value-type generic instantiations throws
// InvalidProgramException under Mono, killing MainForm..ctor inside
// AppSettings validation. Virtual (callvirt) calls must route through
// CpuPaint.SettingValidateValueT; none may survive on the vendor token.
// Non-virtual `call` sites are base-calls with exact-implementation
// semantics (e.g. StringSetting calls base Setting<string>
// .OnValidateValueT): rerouting those through the virtual-dispatching
// helper recurses infinitely (override -> helper -> override: tab death
// by stack overflow, observed live). They must stay pristine, and they
// may only appear inside OnValidateValueT overrides (the base-call
// pattern). A helper call inside an OnValidateValueT override body is
// the recursion shape and fails the test.
public sealed class SettingValidateRedirectTests
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

    private static void CountValidateCalls(ModuleDefinition module,
        out int virtSurvivors, out List<string> virtSites,
        out int baseCalls, out List<string> baseSites,
        out int misplacedBase, out List<string> misplacedSites,
        out int helpers, out List<string> helperInOverride)
    {
        virtSurvivors = 0;
        baseCalls = 0;
        misplacedBase = 0;
        helpers = 0;
        virtSites = new List<string>();
        baseSites = new List<string>();
        misplacedSites = new List<string>();
        helperInOverride = new List<string>();
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                bool isValidateOverride = method.Name == "OnValidateValueT"
                    && method.IsVirtual && !method.IsNewSlot;
                foreach (var ins in method.Body.Instructions)
                {
                    if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                        && ins.Operand is MethodReference mr)
                    {
                        if (mr.Name == "OnValidateValueT"
                            && mr.DeclaringType is GenericInstanceType gi
                            && gi.ElementType.FullName.StartsWith(
                                "PaintDotNet.Settings.Setting", StringComparison.Ordinal))
                        {
                            if (ins.OpCode == OpCodes.Callvirt)
                            {
                                virtSurvivors++;
                                virtSites.Add(type.FullName + "::" + method.Name);
                            }
                            else
                            {
                                baseCalls++;
                                baseSites.Add(type.FullName + "::" + method.Name);
                                if (!isValidateOverride)
                                {
                                    misplacedBase++;
                                    misplacedSites.Add(type.FullName + "::" + method.Name);
                                }
                            }
                        }
                        else if (mr.Name == "SettingValidateValueT"
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
    public void Setting_OnValidateValueT_Rerouted_Through_Reflection_Helper()
    {
        string pdnOut = Path.Combine(Path.GetTempPath(), $"SetVal_{Guid.NewGuid():N}.dll");
        try
        {
            using var pdn = Transform("paintdotnet.dll", pdnOut);
            CountValidateCalls(pdn, out int virtSurvivors, out var virtSites,
                out int baseCalls, out var baseSites, out int misplacedBase, out var misplacedSites,
                out int helpers, out var helperInOverride);
            Assert.True(virtSurvivors == 0,
                "paintdotnet OnValidateValueT callvirt survivors: " + string.Join("; ", virtSites));
            Assert.True(helpers > 0,
                "paintdotnet must route OnValidateValueT through the helper");
            Assert.True(helperInOverride.Count == 0,
                "recursion shape: helper call inside OnValidateValueT override: "
                + string.Join("; ", helperInOverride));
            Assert.True(baseCalls > 0,
                "expected pristine base-calls (e.g. StringSetting) vanished");
            Assert.True(misplacedBase == 0,
                "base-call outside an OnValidateValueT override: " + string.Join("; ", misplacedSites));
        }
        finally
        {
            try { File.Delete(pdnOut); } catch { }
        }
    }
}
