using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;

public sealed class SynthesizedIlTests
{
    private static string RepoRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string BuiltDll(string project, string tfm, string name)
    {
        string dll = Path.Combine(RepoRoot(), "src", project, "bin", "Debug", tfm, name + ".dll");
        Assert.True(File.Exists(dll), $"dependency not built: {dll}");
        return dll;
    }

    [Fact]
    public void CpuDrawingContext_Synthesized_Methods_Are_Stack_Balanced()
    {
        // The CPU drawing-context emitter once emitted a stray Ldloc whose
        // value nothing consumed: the stack merged 1-deep into a 0-deep
        // join and the JIT rejected the method (InvalidProgram at brush
        // time). Stack-verify every synthesized method so no emitter edit
        // can reintroduce an imbalance silently. Scope is the context only
        // (siblings are a later extension, same harness).
        string core = Path.Combine(RepoRoot(), "targets", "paint.net", "PaintDotNet.Windows.Core.dll");
        Assert.True(File.Exists(core), $"pristine PDN core missing: {core}");
        string forms = BuiltDll("WfWasm.WinForms", "net9.0-windows", "WfWasm.WinForms");
        string drawing = BuiltDll("WfWasm.Drawing", "net9.0", "WfWasm.Drawing");
        string pdncp = BuiltDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        var assemblyMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Windows.Forms"] = forms,
            ["System.Drawing"] = drawing,
        };
        var typeMap = GapAnalyzer.AutoMap(core, assemblyMap);
        string output = Path.Combine(Path.GetTempPath(), "core.cpu-stackgate.dll");
        AssemblyRewriter.Rewrite(core, output, assemblyMap, typeMap, pdncp);

        using var asm = AssemblyDefinition.ReadAssembly(output);
        var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
        Assert.NotNull(ctx);
        var failures = new List<string>();
        foreach (var m in ctx.Methods.Where(m => m.HasBody))
            failures.AddRange(StackVerify(m).Select(c => m.Name + ": " + c));
        Assert.True(failures.Count == 0,
            "stack imbalance in synthesized IL:\n" + string.Join("\n", failures));
    }

    // Abstract-interpret stack depth over the body (branch-merge fixpoint).
    // Reports merge conflicts, underflows, unmodelled behaviour, and
    // maxstack overruns. Behaviour names are matched as strings (Cecil
    // 0.11.5 lacks some enum members); anything unknown fails loud.
    private static List<string> StackVerify(MethodDefinition m)
    {
        var ins = m.Body.Instructions;
        var idx = new Dictionary<Instruction, int>();
        for (int i = 0; i < ins.Count; i++) idx[ins[i]] = i;
        int PopOf(OpCode o, object? opnd)
        {
            switch (o.StackBehaviourPop.ToString())
            {
                case "Pop0": return 0;
                case "Pop1":
                case "Popi":
                case "Popref": return 1;
                case "Varpop":
                    if (opnd is MethodReference mr)
                        return mr.Parameters.Count + ((mr.HasThis && o.Code != Code.Newobj) ? 1 : 0);
                    return 0;
                case "PopAll": return 0; // ret/throw/jmp: no successor anyway
                default:
                    string s = o.StackBehaviourPop.ToString();
                    if (s.StartsWith("Popref_popi_pop", StringComparison.Ordinal)) return 3;
                    if (s.Contains('_')) return 2;
                    throw new InvalidOperationException($"unmodelled pop {s} in {m.Name}");
            }
        }
        int PushOf(OpCode o, object? opnd)
        {
            switch (o.StackBehaviourPush.ToString())
            {
                case "Push0": return 0;
                case "Push1":
                case "Pushi":
                case "Pushref":
                case "Pushi8":
                case "Pushr4":
                case "Pushr8": return 1;
                case "Varpush":
                    if (opnd is MethodReference mr)
                        return mr.ReturnType.FullName == "System.Void" ? 0 : 1;
                    return 0;
                default:
                    string s = o.StackBehaviourPush.ToString();
                    if (s.Contains('_')) return 2;
                    throw new InvalidOperationException($"unmodelled push {s} in {m.Name}");
            }
        }
        var depth = new int[ins.Count];
        Array.Fill(depth, -1);
        depth[0] = 0;
        var conflicts = new List<string>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < ins.Count; i++)
            {
                if (depth[i] < 0) continue;
                var o = ins[i].OpCode;
                int d;
                if (o.FlowControl == FlowControl.Return || o.FlowControl == FlowControl.Throw)
                {
                    d = depth[i];
                }
                else
                {
                    int pop = PopOf(o, ins[i].Operand);
                    int push = PushOf(o, ins[i].Operand);
                    d = depth[i] - pop + push;
                    if (d < 0)
                    {
                        conflicts.Add($"UNDERFLOW @{ins[i].Offset:X4} {o}");
                        continue;
                    }
                }
                var sucs = new List<int>();
                if (o.FlowControl != FlowControl.Branch
                    && o.FlowControl != FlowControl.Return
                    && o.FlowControl != FlowControl.Throw
                    && i + 1 < ins.Count)
                    sucs.Add(i + 1);
                if (o.OperandType == OperandType.InlineBrTarget
                    || o.OperandType == OperandType.ShortInlineBrTarget)
                    sucs.Add(idx[(Instruction)ins[i].Operand]);
                else if (o.OperandType == OperandType.InlineSwitch)
                    foreach (var x in (Instruction[])ins[i].Operand) sucs.Add(idx[x]);
                foreach (int s in sucs)
                {
                    if (depth[s] == -1) { depth[s] = d; changed = true; }
                    else if (depth[s] != d)
                        conflicts.Add($"MERGE @{ins[s].Offset:X4} have={depth[s]} got={d} from {ins[i].Offset:X4}");
                }
            }
        }
        int max = depth.Max();
        if (max > m.Body.MaxStackSize)
            conflicts.Add($"MAXSTACK overrun: computed={max} header={m.Body.MaxStackSize}");
        return conflicts.Distinct().ToList();
    }
}
