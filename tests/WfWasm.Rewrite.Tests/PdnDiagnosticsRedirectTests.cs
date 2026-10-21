using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

public sealed class PdnDiagnosticsRedirectTests
{
    [Fact]
    public void Redirect_Synthetic_GetFormattedText_Calls_Fallback()
    {
        // Hermetic: a fabricated DiagnosticsGenerator.GetFormattedText with
        // a throwing body must be replaced by a single call to the
        // browser-safe fallback (original error preserved by PDN's caller;
        // only the environment section degrades explicitly).
        string tempOut = Path.Combine(Path.GetTempPath(), $"PdnDiagSynth_{Guid.NewGuid()}.dll");
        try
        {
            var asmName = new AssemblyNameDefinition("PdnDiagSynth", new Version(1, 0, 0, 0));
            using (var asm = AssemblyDefinition.CreateAssembly(asmName, "PdnDiagSynth", ModuleKind.Dll))
            {
                ModuleDefinition module = asm.MainModule;
                var settings = new TypeDefinition("PaintDotNet.Settings.App", "AppSettings",
                    TypeAttributes.Public | TypeAttributes.Class,
                    module.TypeSystem.Object);
                module.Types.Add(settings);
                var diag = new TypeDefinition("PaintDotNet.Diagnostics", "DiagnosticsGenerator",
                    TypeAttributes.Public | TypeAttributes.Class,
                    module.TypeSystem.Object);
                var body = new MethodDefinition("GetFormattedText",
                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                    module.TypeSystem.String);
                body.Parameters.Add(new ParameterDefinition(settings));
                body.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
                var il = body.Body.GetILProcessor();
                il.Emit(OpCodes.Ldstr, "boom");
                il.Emit(OpCodes.Newobj, module.ImportReference(
                    typeof(InvalidOperationException).GetConstructor([typeof(string)])!));
                il.Emit(OpCodes.Throw);
                diag.Methods.Add(body);
                module.Types.Add(diag);
                asm.Write(tempOut);
            }

            using (var read = AssemblyDefinition.ReadAssembly(tempOut))
            {
                ModuleDefinition module = read.MainModule;
                AssemblyRewriter.RedirectPdnDiagnosticsFallback(module);

                TypeDefinition diag = module.GetType("PaintDotNet.Diagnostics.DiagnosticsGenerator");
                MethodDefinition m = diag.Methods.First(x => x.Name == "GetFormattedText");
                var calls = m.Body.Instructions
                    .Where(i => i.OpCode == OpCodes.Call)
                    .Select(i => (MethodReference)i.Operand)
                    .ToList();
                var only = Assert.Single(calls);
                Assert.Equal("GetErrorDetailsFallback", only.Name);
                Assert.Equal("WfWasm.Drawing.PdnDiagnostics", only.DeclaringType.FullName);
                Assert.Equal("WfWasm.Drawing", ((AssemblyNameReference)only.DeclaringType.Scope).Name);
                // Loader shape: settings, flag in; text out; nothing else.
                Assert.Equal(2, only.Parameters.Count);
                Assert.Equal("System.Object", only.Parameters[0].ParameterType.FullName);
                Assert.Equal("System.Boolean", only.Parameters[1].ParameterType.FullName);
                Assert.Equal("System.String", only.ReturnType.FullName);
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
