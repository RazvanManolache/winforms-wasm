using Mono.Cecil;
using Mono.Cecil.Cil;

// Pins the deterministic thumbnail-delivery fix set (rev258 side-table,
// rev263 null-skip, rev264 render+drain): the drain must skip null-bitmap
// items with a marker (desktop never produces null here; a headless render
// failure does, and delivering it killed the session), keep a live Invoke
// for valid items, and the queue tail must drive render+drain inline
// (delivery is otherwise at the mercy of the threadpool post pump).
public sealed class DrainDeliveryTests
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

    private static MethodDefinition Drain(ModuleDefinition pdn)
    {
        var manager = pdn.GetType("PaintDotNet.Rendering.ThumbnailManager");
        Assert.NotNull(manager);
        var drain = manager.Methods.FirstOrDefault(
            m => m.Name == "DrainThumbnailReadyInvokeList" && m.HasBody);
        Assert.NotNull(drain);
        return drain;
    }

    [Fact]
    public void Queue_Sites_Route_Through_Reflection_Built_Handler()
    {
        string pdnOut = Path.Combine(Path.GetTempPath(), $"QueueH_{Guid.NewGuid():N}.dll");
        try
        {
            var pdn = Transform("paintdotnet.dll", pdnOut);
            var sites = new[]
            {
                ("PaintDotNet.Controls.DocumentStrip", "QueueThumbnailUpdate"),
                ("PaintDotNet.Controls.LayersStrip", "RefreshLayerThumbnail"),
            };
            foreach (var (typeName, methodName) in sites)
            {
                var type = pdn.GetType(typeName);
                Assert.NotNull(type);
                var method = type.Methods.FirstOrDefault(
                    m => m.Name == methodName && m.Parameters.Count == 1 && m.HasBody);
                Assert.NotNull(method);
                bool helperCall = false;
                bool rawNewobj = false;
                foreach (var ins in method.Body.Instructions)
                {
                    if (ins.OpCode == OpCodes.Call
                        && ins.Operand is MethodReference hmr && hmr.Name == "QueueThumbnail"
                        && hmr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint")
                        helperCall = true;
                    if (ins.OpCode == OpCodes.Newobj
                        && ins.Operand is MethodReference cmr && cmr.Name == ".ctor"
                        && cmr.DeclaringType.FullName.Contains("ValueEventHandler"))
                        rawNewobj = true;
                }
                Assert.True(helperCall, typeName + "::" + methodName + " must route through QueueThumbnail");
                Assert.False(rawNewobj, typeName + "::" + methodName + " must not construct the handler in IL");
            }
        }
        finally
        {
            try { File.Delete(pdnOut); } catch { }
        }
    }

    [Fact]
    public void Drain_Skips_Null_Bitmap_Delivery_With_Marker()
    {
        // rev278: the skip lives inside CpuPaint.InvokeReadyHandler (which
        // logs the same marker text); PDN IL carries no skip block at all
        // (a forward skip-branch fails Mono verification: rev268 IPE), so
        // the marker ldstr must be ABSENT here while delivery still routes
        // through the helper. The helper-side skip is pinned by
        // IsNullThumbnailBitmapTests and the in-vivo drain probes.
        string pdnOut = Path.Combine(Path.GetTempPath(), $"DrainNull_{Guid.NewGuid():N}.dll");
        try
        {
            var pdn = Transform("paintdotnet.dll", pdnOut);
            var drain = Drain(pdn);
            bool marker = false;
            bool invoke = false;
            foreach (var ins in drain.Body.Instructions)
            {
                if (ins.OpCode == OpCodes.Ldstr && (string)ins.Operand == "[DRAIN] skip null-bitmap item")
                    marker = true;
                if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                    && ins.Operand is MethodReference mr && mr.Name == "Invoke"
                    && mr.DeclaringType.FullName.Contains("ValueEventHandler"))
                    invoke = true;
            }
            Assert.False(marker, "skip block must live in the helper, not PDN IL");
            // rev267: the raw closed-generic Invoke is gone (interpreter
            // IPE); delivery goes through the reflection helper instead.
            // The old live-Invoke assertion is superseded, not dropped:
            // Drain_Dispatches_Through_Reflection_Helper pins the swap.
            Assert.False(invoke, "raw ValueEventHandler.Invoke must be rerouted");
        }
        finally
        {
            try { File.Delete(pdnOut); } catch { }
        }
    }

    [Fact]
    public void Drain_Dispatches_Through_Reflection_Helper()
    {
        // The drain's per-item dispatch through closed
        // ValueEventHandler<T>.Invoke IPEs in the interpreter (render
        // completes, then delivery dies and strips stay blank). The stack
        // is already (handler, sender, args), so the rule swaps the callee
        // for CpuPaint.InvokeReadyHandler and boxes struct slots.
        string pdnOut = Path.Combine(Path.GetTempPath(), $"DrainReflect_{Guid.NewGuid():N}.dll");
        try
        {
            var pdn = Transform("paintdotnet.dll", pdnOut);
            var drain = Drain(pdn);
            var ins = drain.Body.Instructions;
            Assert.DoesNotContain(ins, i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "Invoke"
                && mr.DeclaringType.Name.StartsWith("ValueEventHandler`1"));
            var call = ins.FirstOrDefault(i => i.OpCode == OpCodes.Call
                && i.Operand is MethodReference mr && mr.Name == "InvokeReadyHandler"
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint");
            Assert.True(call is not null, "reflection helper call missing from drain");
            // The dispatch triple arrives boxed-or-object already (handler
            // and args are reference types here); the rule boxes only
            // value-typed slots, so no unconditional Box is asserted.
        }
        finally
        {
            try { File.Delete(pdnOut); } catch { }
        }
    }

    [Fact]
    public void Drain_ItemProcessing_Is_BranchFree_Helper_Driven()
    {
        // rev278: the loop admits no forward skip-branch and no
        // closed-instantiation member access (Mono-WASM IPE at drain
        // entry: rev268 bisects c-g pinned the middle, rev274-277 the
        // skip block). The per-item work is straight-line: stash to
        // locals, InvokeReadyHandler (null-skipping internally), then
        // DisposeThumbnailItem; the pristine get_Value/Item2/dispose
        // member calls are gone, and no branch may target the loop's
        // MoveNext from the inserted region.
        string pdnOut = Path.Combine(Path.GetTempPath(), $"DrainVerif_{Guid.NewGuid():N}.dll");
        try
        {
            var pdn = Transform("paintdotnet.dll", pdnOut);
            var drain = Drain(pdn);
            foreach (var ins in drain.Body.Instructions)
            {
                if ((ins.OpCode == OpCodes.Ldloca || ins.OpCode == OpCodes.Ldloca_S)
                    && ins.Operand is VariableDefinition vd)
                    Assert.True(vd.VariableType.IsValueType,
                        "Ldloca on reference-typed local V_" + vd.Index + " (" + vd.VariableType.FullName + ")");
            }
            bool hasDisposeHelper = false;
            foreach (var ins in drain.Body.Instructions)
            {
                if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                    && ins.Operand is MethodReference hmr
                    && hmr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint")
                {
                    if (hmr.Name == "DisposeThumbnailItem")
                    {
                        Assert.True(hmr.Parameters.Count == 1
                            && hmr.Parameters[0].ParameterType.FullName == "System.Object",
                            "dispose helper must be object->void");
                        hasDisposeHelper = true;
                    }
                    Assert.False(hmr.Name == "IsNullThumbnailBitmap",
                        "null check lives inside InvokeReadyHandler, not PDN IL");
                }
                if ((ins.OpCode == OpCodes.Br || ins.OpCode == OpCodes.Br_S
                    || ins.OpCode == OpCodes.Brtrue || ins.OpCode == OpCodes.Brtrue_S
                    || ins.OpCode == OpCodes.Brfalse || ins.OpCode == OpCodes.Brfalse_S)
                    && ins.Operand is Mono.Cecil.Cil.Instruction tgt)
                {
                    foreach (var mv in drain.Body.Instructions)
                    {
                        if ((mv.OpCode == OpCodes.Call || mv.OpCode == OpCodes.Callvirt)
                            && mv.Operand is MethodReference mmr && mmr.Name == "MoveNext")
                            Assert.False(tgt == mv, "no inserted branch may target MoveNext");
                    }
                }
            }
            Assert.True(hasDisposeHelper, "DisposeThumbnailItem call missing from drain");
            int getValueCount = 0;
            foreach (var ins in drain.Body.Instructions)
            {
                if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                    && ins.Operand is MethodReference mr && mr.Name == "get_Value"
                    && mr.DeclaringType.FullName.Contains("ValueEventArgs"))
                    getValueCount++;
            }
            Assert.True(getValueCount == 0,
                "no closed-instantiation get_Value may remain, found " + getValueCount);
        }
        finally
        {
            try { File.Delete(pdnOut); } catch { }
        }
    }

}
