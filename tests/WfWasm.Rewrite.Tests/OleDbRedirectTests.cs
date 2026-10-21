using Mono.Cecil;
using WfWasm.Rewrite;
using Xunit;

public sealed class OleDbRedirectTests
{
    [Fact]
    public void Redirect_Synthetic_OleDbAdapter_Moves_To_Stub()
    {
        // Hermetic: a fabricated field of System.Data-scoped
        // OleDbDataAdapter (the MedicalApp typed-DataSet field shape)
        // must move to the shipped headless System.Data.OleDb stub.
        string tempOut = Path.Combine(Path.GetTempPath(), $"OleDbSynth_{Guid.NewGuid()}.dll");
        try
        {
            var asmName = new AssemblyNameDefinition("OleDbSynth", new Version(1, 0, 0, 0));
            using (var asm = AssemblyDefinition.CreateAssembly(asmName, "OleDbSynth", ModuleKind.Dll))
            {
                ModuleDefinition module = asm.MainModule;
                var systemData = new AssemblyNameReference("System.Data", new Version(4, 0, 0, 0));
                module.AssemblyReferences.Add(systemData);
                var adapter = new TypeReference("System.Data.OleDb", "OleDbDataAdapter", module, systemData);
                var host = new TypeDefinition("OleDbSynth", "Host",
                    TypeAttributes.Public | TypeAttributes.Class,
                    module.TypeSystem.Object);
                host.Fields.Add(new FieldDefinition("_adapter", FieldAttributes.Public, adapter));
                module.Types.Add(host);
                asm.Write(tempOut);
            }

            using (var read = AssemblyDefinition.ReadAssembly(tempOut))
            {
                ModuleDefinition module = read.MainModule;
                AssemblyRewriter.RedirectOleDbTypeReferences(module);

                TypeReference adapter = module.GetTypeReferences().First(
                    t => t.FullName == "System.Data.OleDb.OleDbDataAdapter");
                var scope = Assert.IsType<AssemblyNameReference>(adapter.Scope);
                Assert.Equal("System.Data.OleDb", scope.Name);
                Assert.Equal(new Version(4, 0, 0, 0), scope.Version);
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
