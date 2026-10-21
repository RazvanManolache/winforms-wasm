using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins RedirectPdnCheckBoxMeasure against the real Framework binary
// (targets/ read-only: copied to temp first): GetPreferredSize answers
// from the in-module classic box path plus CPU text measurement, with
// no DWrite layout calls left and the graphics-dispose finally kept.
public sealed class PdnCheckBoxMeasureRedirectTests
{
    [Fact]
    public void Redirect_Real_CheckBoxMeasure_Answers_CpuText()
    {
        string src = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Framework.dll");
        string tmp = Path.Combine(Path.GetTempPath(), $"PdnBox_{Guid.NewGuid()}.dll");
        File.Copy(src, tmp);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.RedirectPdnCheckBoxMeasure(read.MainModule);

            var m = read.MainModule.GetType("PaintDotNet.Controls.PdnCheckBox").Methods
                .First(x => x.Name == "GetPreferredSize");
            var calls = m.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                .Select(i => ((MethodReference)i.Operand))
                .Select(r => r.DeclaringType.Name + "::" + r.Name)
                .ToList();
            Assert.Contains("PdnCheckBox::MeasureAndDrawClassicBox", calls);
            Assert.Contains("TextRenderer::MeasureText", calls);
            Assert.DoesNotContain(calls, c => c == "PdnCheckBox::MeasureAndDraw");
            Assert.DoesNotContain("UIText::CreateLayout", calls);
            Assert.DoesNotContain("ResourceCacheExtensions::GetCachedOrCreateResource", calls);
            // Every instance call (HasThis, except .ctor via newobj) must
            // observe an object reference pushed ahead of it: the verifier
            // rejects int-as-this (the shipped ConvertDipsToPixelsInt bug).
            var instrs = m.Body.Instructions;
            for (int i = 0; i < instrs.Count; i++)
            {
                if ((instrs[i].OpCode == OpCodes.Call || instrs[i].OpCode == OpCodes.Callvirt)
                    && instrs[i].Operand is MethodReference mr && mr.HasThis && mr.Name != ".ctor")
                {
                    bool pushed = false;
                    for (int j = Math.Max(0, i - 4); j < i; j++)
                    {
                        var o = instrs[j].OpCode;
                        if (o == OpCodes.Ldarg_0 || o == OpCodes.Ldarg_1 || o == OpCodes.Ldarg_2
                            || o == OpCodes.Ldloc_0 || o == OpCodes.Ldloc_1 || o == OpCodes.Ldloc_2
                            || o == OpCodes.Ldloc_3 || o == OpCodes.Ldloc_S
                            || o == OpCodes.Ldloca_S || o == OpCodes.Ldarga_S
                            || o == OpCodes.Call || o == OpCodes.Callvirt || o == OpCodes.Newobj
                            || o == OpCodes.Ldstr || o == OpCodes.Ldnull || o == OpCodes.Dup)
                        {
                            pushed = true;
                            break;
                        }
                    }
                    Assert.True(pushed, $"no instance for {mr.DeclaringType.Name}::{mr.Name} at {i}");
                }
            }
            Assert.Single(m.Body.ExceptionHandlers);
            Assert.Equal(
                new[]
                {
                    OpCodes.Ldarg_0, OpCodes.Call, OpCodes.Stloc_0,
                    OpCodes.Nop,
                    OpCodes.Ldarg_0, OpCodes.Ldloc_0, OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1,
                    OpCodes.Call, OpCodes.Stloc_1,
                    OpCodes.Call, OpCodes.Ldc_I4_3, OpCodes.Call, OpCodes.Stloc_2,
                    OpCodes.Ldarg_0, OpCodes.Callvirt, OpCodes.Ldarg_0, OpCodes.Callvirt,
                    OpCodes.Call, OpCodes.Stloc_3,
                    OpCodes.Ldloc_1, OpCodes.Ldloc_2, OpCodes.Add,
                    OpCodes.Ldloca_S, OpCodes.Call, OpCodes.Add,
                    OpCodes.Ldarg_0, OpCodes.Call, OpCodes.Stloc_S,
                    OpCodes.Ldloca_S, OpCodes.Call, OpCodes.Add,
                    OpCodes.Ldloca_S, OpCodes.Call,
                    OpCodes.Ldarg_0, OpCodes.Call, OpCodes.Stloc_S,
                    OpCodes.Ldloca_S, OpCodes.Call, OpCodes.Add,
                    OpCodes.Newobj, OpCodes.Stloc_S, OpCodes.Leave,
                    OpCodes.Nop,
                    OpCodes.Ldloc_0, OpCodes.Brfalse_S,
                    OpCodes.Ldloc_0, OpCodes.Callvirt, OpCodes.Nop, OpCodes.Endfinally,
                    OpCodes.Nop, OpCodes.Ldloc_S, OpCodes.Ret,
                },
                m.Body.Instructions.Select(i => i.OpCode).ToList());
            Assert.Equal(OpCodes.Ret, m.Body.Instructions.Last().OpCode);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
