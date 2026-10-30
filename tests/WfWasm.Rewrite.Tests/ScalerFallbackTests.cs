using Mono.Cecil;
using Mono.Cecil.Cil;

// Pins the null-factory WIC-mode scaler fallback (rev266): WIC imaging has
// no headless factory (ImagingFactory.instance is null), so the Mode2 1..4
// scaler branches (Linear, Cubic, Fant, HighQualityCubic) throw Validate
// ANE inside RenderThreadLoop -> the thumbnail handler never fires and the
// Layers/Document strips stay blank. The rule inserts a null-factory guard
// at the top of both Mode2 CreateBitmapScaler overloads (Context + Profile)
// remapping 1->5 (HighQualityLinear) and 2..4->7 (CubicSmooth, managed
// MagicScaler kernels PDN itself uses for those modes). A present factory
// (desktop) keeps the exact original mode: the WIC switch, the
// TransformBitmapSource path, and the MagicScaler branches all stay intact.
public sealed class ScalerFallbackTests
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

    [Fact]
    public void Mode2_Overloads_Remap_Wic_Modes_When_Factory_Null()
    {
        string coreOut = Path.Combine(Path.GetTempPath(), $"ScalerFallback_{Guid.NewGuid():N}.dll");
        try
        {
            var core = Transform("PaintDotNet.Windows.Core.dll", coreOut);
            var ife = core.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(ife);
            var targets = ife.Methods.Where(m =>
                m.Name == "CreateBitmapScaler" && m.HasBody
                && m.Parameters.Count == 6
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IImagingFactory"
                && m.Parameters[3].ParameterType.FullName == "PaintDotNet.Imaging.BitmapInterpolationMode2").ToList();
            // Context + Profile overloads.
            Assert.Equal(2, targets.Count);
            int remapped = 0, wrappers = 0;
            foreach (var m in targets)
            {
                var ins = m.Body.Instructions;
                bool hasSwitch = ins.Any(i => i.OpCode == OpCodes.Switch);
                if (!hasSwitch)
                {
                    // Wrapper overload: converts context to profile and
                    // delegates to the patched overload.
                    Assert.Contains(ins, i => i.Operand is MethodReference mr && mr.Name == "CreateBitmapScaler");
                    wrappers++;
                    continue;
                }
                // Null-factory guard at entry: factory load + cond branch
                // (Cecil round-trips ldarg.0/brtrue to short forms).
                Assert.True(ins.Count > 4);
                Assert.True(ins[0].OpCode == OpCodes.Ldarg_0 || ins[0].OpCode == OpCodes.Ldarg_S,
                    "entry guard loads factory, got " + ins[0].OpCode);
                Assert.Equal(FlowControl.Cond_Branch, ins[1].OpCode.FlowControl);
                // Linear (1) remaps to HighQualityLinear (5).
                Assert.Contains(ins, i => i.OpCode == OpCodes.Ldc_I4_5);
                // Cubic family (2..4) remaps to CubicSmooth (7).
                Assert.Contains(ins, i => i.OpCode == OpCodes.Ldc_I4_7);
                Assert.Contains(ins, i => i.OpCode == OpCodes.Starg_S);
                // Desktop path intact: WIC switch + both backends still present.
                Assert.Contains(ins, i => i.Operand is MethodReference mr && mr.Name == "TransformBitmapSource");
                Assert.Contains(ins, i => i.Operand is MethodReference mr && mr.Name == "CreateMagicScaler");
                remapped++;
            }
            Assert.Equal(1, remapped);
            Assert.Equal(1, wrappers);
        }
        finally
        {
            try { if (File.Exists(coreOut)) File.Delete(coreOut); } catch { }
        }
    }

    [Fact]
    public void PixelConverters_Use_Managed_Format_Table()
    {
        string coreOut = Path.Combine(Path.GetTempPath(), $"PixelConvert_{Guid.NewGuid():N}.dll");
        try
        {
            var core = Transform("PaintDotNet.Windows.Core.dll", coreOut);
            var ife = core.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(ife);
            var targets = ife.Methods.Where(m =>
                (m.Name == "ConvertBitmapToPixelSourcePbgra128Float"
                    || m.Name == "ConvertBitmapFromPrgba128Float") && m.HasBody).ToList();
            Assert.Equal(3, targets.Count);
            int remapped = 0, wrappers = 0;
            foreach (var m in targets)
            {
                bool converted = m.Body.Instructions.Any(i => i.Operand is MethodReference mr
                    && mr.Name == "TryGetPixelInfo"
                    && mr.DeclaringType.FullName == "PaintDotNet.Imaging.PixelFormats");
                if (!converted)
                {
                    // Short wrapper overload: delegates to the full one
                    // (patched there). A full converter without the managed
                    // table lookup fails here instead of silently passing.
                    Assert.Contains(m.Body.Instructions, i => i.Operand is MethodReference mr
                        && (mr.Name == "ConvertBitmapToPixelSourcePbgra128Float"
                            || mr.Name == "ConvertBitmapFromPrgba128Float"));
                    wrappers++;
                    continue;
                }
                // Info local now served from the managed table.
                Assert.True(m.Body.Variables.Count >= 2);
                Assert.Equal("PaintDotNet.Imaging.IPixelInfo",
                    m.Body.Variables[1].VariableType.FullName);
                var ins = m.Body.Instructions;
                // No WIC info lookup remains; managed table lookup present.
                Assert.DoesNotContain(ins, i => i.Operand is MethodReference mr
                    && mr.Name == "GetInfoNoAddRef"
                    && mr.DeclaringType.FullName == "PaintDotNet.Imaging.PixelFormatExtensions");
                Assert.Contains(ins, i => i.Operand is MethodReference mr
                    && mr.Name == "TryGetPixelInfo"
                    && mr.DeclaringType.FullName == "PaintDotNet.Imaging.PixelFormats");
                // Unknown formats fail explicitly instead of NRE-ing in WIC.
                Assert.Contains(ins, i => i.Operand is MethodReference mr
                    && mr.Name == ".ctor"
                    && mr.DeclaringType.FullName == "System.InvalidOperationException");
                // Every representation query goes to the managed interface.
                Assert.DoesNotContain(ins, i => i.Operand is MethodReference mr
                    && mr.Name == "get_NumericRepresentation"
                    && mr.DeclaringType.FullName == "PaintDotNet.Imaging.IPixelFormatInfo");
                Assert.Contains(ins, i => i.Operand is MethodReference mr
                    && mr.Name == "get_NumericRepresentation"
                    && mr.DeclaringType.FullName == "PaintDotNet.Imaging.IPixelInfo");
                remapped++;
            }
            Assert.Equal(2, remapped);
            Assert.Equal(1, wrappers);
        }
        finally
        {
            try { if (File.Exists(coreOut)) File.Delete(coreOut); } catch { }
        }
    }

    [Fact]
    public void ProfileHandle_Bypasses_LazyResult()
    {
        string coreOut = Path.Combine(Path.GetTempPath(), $"ProfileHandle_{Guid.NewGuid():N}.dll");
        try
        {
            var core = Transform("PaintDotNet.Windows.Core.dll", coreOut);
            var profile = core.GetType("PaintDotNet.Imaging.ColorProfile");
            Assert.NotNull(profile);
            var getter = profile.Methods.FirstOrDefault(m =>
                m.Name == "get_MagicProfileHandle" && m.HasBody && m.Parameters.Count == 0);
            Assert.True(getter is not null);
            var ins = getter.Body.Instructions;
            // Direct computation from ICC bytes: no lazy machinery remains.
            Assert.DoesNotContain(ins, i => i.Operand is FieldReference fr
                && fr.Name == "magicProfileHandle");
            Assert.DoesNotContain(ins, i => i.Operand is MethodReference mr
                && mr.Name == "get_Value");
            Assert.Contains(ins, i => i.Operand is MethodReference mr
                && mr.Name == "Create"
                && mr.DeclaringType.FullName == "PhotoSauce.MagicScaler.Experimental.ColorProfileHandle");
            Assert.Contains(ins, i => i.Operand is MethodReference mr && mr.Name == "get_Span");
        }
        finally
        {
            try { if (File.Exists(coreOut)) File.Delete(coreOut); } catch { }
        }
    }
}
