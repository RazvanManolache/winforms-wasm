using Mono.Cecil;
using WfWasm.Rewrite;
using Xunit;

public sealed class IsolatedStorageRedirectTests
{
    [Fact]
    public void Redirect_Preserves_Framework_Enums_And_Exceptions()
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("IsoTypes", new Version(1, 0)), "IsoTypes", ModuleKind.Dll);
        var module = assembly.MainModule;
        var framework = new AssemblyNameReference("System.IO.IsolatedStorage", new Version(9, 0));
        module.AssemblyReferences.Add(framework);
        var host = new TypeDefinition("Test", "Host", TypeAttributes.Public, module.TypeSystem.Object);
        foreach (string name in new[] { "IsolatedStorageScope", "IsolatedStorageException" })
            host.Fields.Add(new FieldDefinition(name, FieldAttributes.Public,
                new TypeReference("System.IO.IsolatedStorage", name, module, framework)));
        module.Types.Add(host);
        using var stream = new MemoryStream();
        assembly.Write(stream); stream.Position = 0;
        using var read = AssemblyDefinition.ReadAssembly(stream);
        AssemblyRewriter.RedirectIsolatedStorageTypeReferences(read.MainModule);
        Assert.All(read.MainModule.GetTypeReferences().Where(t => t.Namespace == "System.IO.IsolatedStorage"),
            t => Assert.Equal("System.IO.IsolatedStorage", ((AssemblyNameReference)t.Scope).Name));
        Assert.DoesNotContain(read.MainModule.AssemblyReferences, r => r.Name == "WfWasm.IsolatedStorage");
    }

    [Fact]
    public void Redirect_Synthetic_IsoStore_Moves_To_Stub()
    {
        // Hermetic: a fabricated field of System-scoped
        // IsolatedStorageFile (the Telerik spellchecker shape) must move
        // to the shipped headless System.IO.IsolatedStorage stub.
        string tempOut = Path.Combine(Path.GetTempPath(), $"IsoSynth_{Guid.NewGuid()}.dll");
        try
        {
            var asmName = new AssemblyNameDefinition("IsoSynth", new Version(1, 0, 0, 0));
            using (var asm = AssemblyDefinition.CreateAssembly(asmName, "IsoSynth", ModuleKind.Dll))
            {
                ModuleDefinition module = asm.MainModule;
                var system = new AssemblyNameReference("System", new Version(4, 0, 0, 0));
                module.AssemblyReferences.Add(system);
                var store = new TypeReference("System.IO.IsolatedStorage", "IsolatedStorageFile", module, system);
                var host = new TypeDefinition("IsoSynth", "Host",
                    TypeAttributes.Public | TypeAttributes.Class,
                    module.TypeSystem.Object);
                host.Fields.Add(new FieldDefinition("_store", FieldAttributes.Public, store));
                module.Types.Add(host);
                asm.Write(tempOut);
            }

            using (var read = AssemblyDefinition.ReadAssembly(tempOut))
            {
                ModuleDefinition module = read.MainModule;
                AssemblyRewriter.RedirectIsolatedStorageTypeReferences(module);

                TypeReference store = module.GetTypeReferences().First(
                    t => t.FullName == "System.IO.IsolatedStorage.IsolatedStorageFile");
                var scope = Assert.IsType<AssemblyNameReference>(store.Scope);
                Assert.Equal("WfWasm.IsolatedStorage", scope.Name);
                Assert.Equal(new Version(1, 0, 0, 0), scope.Version);
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
