using Mono.Cecil;
using WfWasm.Rewrite;
using Xunit;

public sealed class MediaRedirectTests
{
    [Fact]
    public void Redirect_Synthetic_SystemSounds_Moves_To_Stub()
    {
        // Hermetic: a fabricated field of System-scoped
        // SystemSounds (the Telerik RadMessageBox shape) must move to
        // the shipped headless System.Windows.Extensions stub.
        string tempOut = Path.Combine(Path.GetTempPath(), $"MediaSynth_{Guid.NewGuid()}.dll");
        try
        {
            var asmName = new AssemblyNameDefinition("MediaSynth", new Version(1, 0, 0, 0));
            using (var asm = AssemblyDefinition.CreateAssembly(asmName, "MediaSynth", ModuleKind.Dll))
            {
                ModuleDefinition module = asm.MainModule;
                var system = new AssemblyNameReference("System", new Version(4, 0, 0, 0));
                module.AssemblyReferences.Add(system);
                var sounds = new TypeReference("System.Media", "SystemSounds", module, system);
                var host = new TypeDefinition("MediaSynth", "Host",
                    TypeAttributes.Public | TypeAttributes.Class,
                    module.TypeSystem.Object);
                host.Fields.Add(new FieldDefinition("_sounds", FieldAttributes.Public, sounds));
                module.Types.Add(host);
                asm.Write(tempOut);
            }

            using (var read = AssemblyDefinition.ReadAssembly(tempOut))
            {
                ModuleDefinition module = read.MainModule;
                AssemblyRewriter.RedirectMediaTypeReferences(module);

                TypeReference sounds = module.GetTypeReferences().First(
                    t => t.FullName == "System.Media.SystemSounds");
                var scope = Assert.IsType<AssemblyNameReference>(sounds.Scope);
                Assert.Equal("System.Windows.Extensions", scope.Name);
                Assert.Equal(new Version(4, 0, 0, 0), scope.Version);
                // NOTE: framework-named stubs only survive bundling when the
                // wasm pack lacks that simple name; System.IO.IsolatedStorage
                // collides, so its twin lives in WfWasm.IsolatedStorage.
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
