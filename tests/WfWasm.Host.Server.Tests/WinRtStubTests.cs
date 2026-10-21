using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using WfWasm.Host.Server;

namespace WfWasm.Host.Server.Tests;

public sealed class WinRtStubTests
{
    private const string App = "telerik-toastnotificationmanagerexamples";

    [Theory]
    [InlineData("Windows.Foundation.UniversalApiContract")]
    [InlineData("Windows.Foundation.FoundationContract")]
    public void WinRtStubs_Resolve_ForAnyApp(string stem)
    {
        // Contract stubs live beside the server, not in app folders, but
        // every app may resolve them (Telerik toast, UWP Toolkit, ...).
        foreach (string app in new[] { App, "telerik-photo", "calculator" })
        {
            Assert.True(AppTransform.TryGetSpec(app, out _), $"unknown app {app}");
            Assert.True(AppTransform.TryResolveAssembly(app, stem, out string? file), $"unresolved {stem} for {app}");
            Assert.Equal(stem + ".dll", file);
        }
    }

    [Fact]
    public void UniversalApiContract_Serves_With_Exact_Identity()
    {
        // The CLR binds the contract by exact name/version; a mismatched
        // identity would reintroduce the TypeLoadException this fixes.
        byte[] served = TransformFresh(App, "Windows.Foundation.UniversalApiContract.dll");
        Assert.NotEmpty(served);
        using var pe = new PEReader(new MemoryStream(served, writable: false));
        MetadataReader md = pe.GetMetadataReader();
        AssemblyDefinition def = md.GetAssemblyDefinition();
        Assert.Equal("Windows.Foundation.UniversalApiContract", md.GetString(def.Name));
        Assert.Equal(new Version(10, 0, 0, 0), def.Version);
        Assert.True(md.GetBlobBytes(def.PublicKey).Length == 0, "stub must be unsigned (PublicKeyToken=null)");
        Assert.Contains(
            md.TypeDefinitions.Select(h => md.GetTypeDefinition(h))
                .Select(t => md.GetString(t.Namespace) + "." + md.GetString(t.Name)),
            full => full == "Windows.UI.Notifications.ToastNotification");
    }

    [Fact]
    public void FoundationContract_Serves_With_Exact_Identity_And_Token()
    {
        byte[] served = TransformFresh(App, "Windows.Foundation.FoundationContract.dll");
        Assert.NotEmpty(served);
        using var pe = new PEReader(new MemoryStream(served, writable: false));
        MetadataReader md = pe.GetMetadataReader();
        AssemblyDefinition def = md.GetAssemblyDefinition();
        Assert.Equal("Windows.Foundation.FoundationContract", md.GetString(def.Name));
        Assert.Equal(new Version(4, 0, 0, 0), def.Version);
        string[] names = md.TypeDefinitions.Select(h => md.GetTypeDefinition(h))
            .Select(t => md.GetString(t.Namespace) + "." + md.GetString(t.Name)).ToArray();
        Assert.Contains("System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken", names);
        Assert.Contains("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal", names);
        Assert.Contains("Windows.Foundation.TypedEventHandler`2", names);
    }

    [Theory]
    [InlineData("System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken")]
    [InlineData("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal")]
    public void Rewritten_Toast_Assembly_Points_Interop_At_Stub(string fullName)
    {
        // End to end on real bytes: the rewritten toast manager must
        // reference WinRT interop helpers from the stub contract, not
        // from mscorlib (absent on the browser CoreLib).
        byte[] served = TransformFresh(App, "Telerik.WinControls.RadToastNotification.dll");
        Assert.NotEmpty(served);
        using var pe = new PEReader(new MemoryStream(served, writable: false));
        MetadataReader md = pe.GetMetadataReader();
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypeReferenceHandle h in md.TypeReferences)
        {
            TypeReference tr = md.GetTypeReference(h);
            if (md.GetString(tr.Namespace) + "." + md.GetString(tr.Name) != fullName)
                continue;
            string scope = tr.ResolutionScope.Kind == HandleKind.AssemblyReference
                ? md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name)
                : tr.ResolutionScope.Kind.ToString();
            scopes.Add(scope);
        }
        Assert.Single(scopes, "Windows.Foundation.FoundationContract");
    }

    [Fact]
    public void Toolkit_Headless_Init_Stubbed()
    {
        // The OS toast-identity bootstrap (COM/registry/shortcuts) is
        // replaced headless: Initialize seeds a constant AUMID, the
        // activator factory answers without touching the OS.
        byte[] served = TransformFresh(App, "Microsoft.Toolkit.Uwp.Notifications.dll");
        Assert.NotEmpty(served);
        using var pe = new PEReader(new MemoryStream(served, writable: false));
        MetadataReader md = pe.GetMetadataReader();
        byte[] initIl = IlOf(pe, md, "Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat", "Initialize");
        Assert.Equal(11, initIl.Length);
        Assert.Equal(0x72, initIl[0]); // ldstr
        Assert.Equal(0x80, initIl[5]); // stsfld
        Assert.Equal(0x2A, initIl[10]); // ret
        int token = initIl[1] | (initIl[2] << 8) | (initIl[3] << 16) | (initIl[4] << 24);
        Assert.Equal("WfWasm.Toast", md.GetUserString(MetadataTokens.UserStringHandle(token)));
        byte[] factoryIl = IlOf(pe, md, "Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat", "CreateAndRegisterActivator");
        Assert.Equal(17, factoryIl.Length);
        Assert.Equal(0x17, factoryIl[0]); // ldc.i4.1
        Assert.Equal(0x80, factoryIl[1]); // stsfld
        Assert.Equal(0xD0, factoryIl[6]); // ldtoken
        Assert.Equal(0x28, factoryIl[11]); // call GetTypeFromHandle
        Assert.Equal(0x2A, factoryIl[16]); // ret
    }

    private static byte[] IlOf(PEReader pe, MetadataReader md, string typeFullName, string methodName)
    {
        foreach (TypeDefinitionHandle th in md.TypeDefinitions)
        {
            TypeDefinition td = md.GetTypeDefinition(th);
            if (md.GetString(td.Namespace) + "." + md.GetString(td.Name) != typeFullName)
                continue;
            foreach (MethodDefinitionHandle mh in td.GetMethods())
            {
                MethodDefinition mdef = md.GetMethodDefinition(mh);
                if (md.GetString(mdef.Name) != methodName)
                    continue;
                return pe.GetMethodBody(mdef.RelativeVirtualAddress).GetILBytes();
            }
        }
        throw new Xunit.Sdk.XunitException($"method not found: {typeFullName}.{methodName}");
    }

    private static byte[] TransformFresh(string app, string name)
    {
        // Drop cached revs (older revisions predate the stub/ERT rules);
        // the cache is regenerable on demand.
        try
        {
            string dir = Path.Combine(AppTransform.CacheRoot, app);
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir, "*" + Path.GetFileNameWithoutExtension(name) + "*"))
                    File.Delete(f);
            }
        }
        catch { }
        return AppTransform.TransformOrCached(app, name);
    }
}
