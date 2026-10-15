using Mono.Cecil;
using WfWasm.Rewrite;
using Xunit;

public sealed class ConfigRedirectTests
{
    [Fact]
    public void Redirect_Synthetic_SettingsBase_Moves_To_Package()
    {
        // Hermetic: a fabricated field of System-scoped
        // ApplicationSettingsBase (the BookShelf.Properties.Settings base)
        // must move to the shipped ConfigurationManager package.
        string tempOut = Path.Combine(Path.GetTempPath(), $"CfgSynth_{Guid.NewGuid()}.dll");
        try
        {
            var asmName = new AssemblyNameDefinition("CfgSynth", new Version(1, 0, 0, 0));
            using (var asm = AssemblyDefinition.CreateAssembly(asmName, "CfgSynth", ModuleKind.Dll))
            {
                ModuleDefinition module = asm.MainModule;
                var system = new AssemblyNameReference("System", new Version(4, 0, 0, 0));
                module.AssemblyReferences.Add(system);
                var settings = new TypeReference("System.Configuration", "ApplicationSettingsBase", module, system);
                var host = new TypeDefinition("CfgSynth", "Host",
                    TypeAttributes.Public | TypeAttributes.Class,
                    module.TypeSystem.Object);
                host.Fields.Add(new FieldDefinition("settings", FieldAttributes.Public, settings));
                module.Types.Add(host);
                asm.Write(tempOut);
            }

            using (var read = AssemblyDefinition.ReadAssembly(tempOut))
            {
                ModuleDefinition module = read.MainModule;
                AssemblyRewriter.RedirectConfigurationTypeReferences(module);

                TypeReference settings = module.GetTypeReferences().First(
                    t => t.FullName == "System.Configuration.ApplicationSettingsBase");
                var scope = Assert.IsType<AssemblyNameReference>(settings.Scope);
                Assert.Equal("System.Configuration.ConfigurationManager", scope.Name);
                Assert.Equal(new Version(9, 0, 0, 0), scope.Version);
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
