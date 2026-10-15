using System.Reflection;
using WfWasm.Rewrite;

public sealed class RewriteTests
{
    private static string FixtureDll()
    {
        // Fixture builds to its own output dir (net9.0-windows).
        string dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "WfWasm.Rewrite.Fixture", "bin", "Debug", "net9.0-windows"));
        string dll = Path.Combine(dir, "WfWasm.Rewrite.Fixture.dll");
        Assert.True(File.Exists(dll), $"fixture not built: {dll}");
        return dll;
    }

    private static string ShimDll(string project, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim not built: {dll}");
        return dll;
    }

    private static Dictionary<string, string> ShimMap() => new(StringComparer.Ordinal)
    {
        ["System.Windows.Forms"] = ShimDll("WfWasm.Shim", "WfWasm.Shim"),
        ["System.Windows.Forms.Primitives"] = ShimDll("WfWasm.Shim", "WfWasm.Shim"),
        ["System.Drawing"] = ShimDll("WfWasm.Drawing", "WfWasm.Drawing"),
        ["System.Drawing.Common"] = ShimDll("WfWasm.Drawing", "WfWasm.Drawing"),
        ["System.Drawing.Primitives"] = ShimDll("WfWasm.Drawing", "WfWasm.Drawing"),
    };

    [Fact]
    public void Rewrite_Renames_Framework_References_To_Shim()
    {
        string input = FixtureDll();
        Assert.Contains("System.Windows.Forms", AssemblyRewriter.ReferenceNames(input));

        string output = Path.Combine(Path.GetTempPath(), "greeter.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        List<string> refs = AssemblyRewriter.ReferenceNames(output);
        Assert.Contains("WfWasm.Shim", refs);
        Assert.Contains("WfWasm.Drawing", refs);
        Assert.DoesNotContain("System.Windows.Forms", refs);
        Assert.DoesNotContain("System.Drawing.Common", refs);
    }

    [Fact]
    public void InspectNullableIL()
    {
        double? d = 5.0;
        double v = d.GetValueOrDefault(0.0);
        Assert.Equal(5.0, v);
    }

    private static Dictionary<string, (string Assembly, string Type)> BootTypeMap() => new(StringComparer.Ordinal)
    {
        ["System.Windows.Forms.Form"] = ("WfWasm.Shim", "WfWasm.Shim.Form"),
        ["System.Windows.Forms.Button"] = ("WfWasm.Shim", "WfWasm.Shim.Button"),
        ["System.Windows.Forms.TextBox"] = ("WfWasm.Shim", "WfWasm.Shim.TextBox"),
        ["System.Windows.Forms.Label"] = ("WfWasm.Shim", "WfWasm.Shim.Label"),
        ["System.Windows.Forms.Control"] = ("WfWasm.Shim", "WfWasm.Shim.Control"),
        ["System.Windows.Forms.Control+ControlCollection"] = ("WfWasm.Shim", "WfWasm.Shim.Control+ControlCollection"),
        ["System.Windows.Forms.MessageBox"] = ("WfWasm.Shim", "WfWasm.Shim.MessageBox"),
        ["System.Windows.Forms.DialogResult"] = ("WfWasm.Shim", "WfWasm.Shim.DialogResult"),
        ["System.Windows.Forms.Application"] = ("WfWasm.Shim", "WfWasm.Shim.Application"),
        ["System.Threading.ThreadExceptionEventHandler"] = ("WfWasm.Shim", "System.Threading.ThreadExceptionEventHandler"),
        ["System.Threading.ThreadExceptionEventArgs"] = ("WfWasm.Shim", "System.Threading.ThreadExceptionEventArgs"),
        ["System.Drawing.Size"] = ("WfWasm.Drawing", "System.Drawing.Size"),
    };

    private static string ShimBinDll(string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", name, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim bin not built: {dll}");
        return dll;
    }

    private static FieldInfo DeclaredField(object target, string name)
    {
        for (Type? t = target.GetType(); t is not null; t = t.BaseType)
        {
            FieldInfo? f = t.GetField(name,
                BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic);
            if (f is not null)
                return f;
        }

        throw new MissingFieldException(target.GetType().FullName, name);
    }

    [Fact]
    public void Rewritten_Fixture_Boots_Against_Shim()
    {
        // End-to-end of the type-level remap: the unmodified fixture IL,
        // rewritten only, instantiates and runs its click handler on shim
        // types (layout, Controls.Add, Text, MessageBox.Show all resolve).
        string output = Path.Combine(Path.GetTempPath(), "greeter.boot.dll");
        AssemblyRewriter.Rewrite(FixtureDll(), output, ShimMap(), BootTypeMap());

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WfWasm.Shim"] = ShimBinDll("WfWasm.Shim"),
            ["WfWasm.Drawing"] = ShimBinDll("WfWasm.Drawing"),
        };
        ResolveEventHandler? handler = (_, args) =>
            byName.TryGetValue(args.Name.Split(',')[0].Trim(), out string? path)
                ? Assembly.LoadFrom(path)
                : null;
        AppDomain.CurrentDomain.AssemblyResolve += handler;
        try
        {
            Assembly app = Assembly.LoadFrom(output);
            Type? formType = app.GetType("WfWasm.Rewrite.Fixture.GreeterForm");
            Assert.NotNull(formType);
            Assert.Equal("Form", formType.BaseType?.Name);

            dynamic form = Activator.CreateInstance(formType)!;
            Assert.Equal(3, form.Children.Count);
            Assert.Equal("TextBox", form.Children[0].GetType().Name);
            Assert.Equal("Button", form.Children[1].GetType().Name);
            Assert.Equal("Label", form.Children[2].GetType().Name);
            Assert.Equal("Greeter", form.Text);

            object name = DeclaredField(form, "_name").GetValue(form)!;
            object go = DeclaredField(form, "_go").GetValue(form)!;
            dynamic outputLabel = DeclaredField(form, "_out").GetValue(form)!;
            // Backing field, not the property: TextBox hides Control.Text
            // with new, so any property-level binding (dynamic or
            // GetProperty) can land on the base slot while the app IL
            // reads TextBox.Text, splitting the value in two.
            DeclaredField(name, "_text").SetValue(name, "Ada");

            var click = (MulticastDelegate)DeclaredField(go, "Click").GetValue(go)!;
            Assert.NotNull(click);
            click.DynamicInvoke(go, EventArgs.Empty);

            Assert.Equal("Hello, Ada!", (string)outputLabel.Text);
            Assembly shim = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => a.GetName().Name == "WfWasm.Shim");
            Type? box = shim.GetType("WfWasm.Shim.MessageBox");
            Assert.NotNull(box);
            Assert.Equal("Hello, Ada!", box.GetProperty("LastText")!.GetValue(null));
            Assert.Equal("Greeting", box.GetProperty("LastCaption")!.GetValue(null));
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= handler;
        }
    }

    [Fact]
    public void Rewrite_Accepts_Assemblies_With_ILOnly_Cleared()
    {
        // Paint.NET ships with the ILOnly CorFlag cleared, which stock
        // Cecil refuses to write. The rewriter restores the flag on the
        // rewritten copy so such binaries round-trip.
        string input = FixtureDll();
        string noIlOnly = Path.Combine(Path.GetTempPath(), "greeter.noilonly.dll");
        File.Copy(input, noIlOnly, overwrite: true);
        ClearILOnlyFlag(noIlOnly);

        using (var stock = Mono.Cecil.AssemblyDefinition.ReadAssembly(noIlOnly))
        {
            Assert.False((stock.MainModule.Attributes & Mono.Cecil.ModuleAttributes.ILOnly) != 0);
            Assert.Throws<System.NotSupportedException>(() =>
                stock.Write(Path.Combine(Path.GetTempPath(), "greeter.noilonly.stock.dll")));
        }

        string output = Path.Combine(Path.GetTempPath(), "greeter.noilonly.rewritten.dll");
        AssemblyRewriter.Rewrite(noIlOnly, output, ShimMap());
        Assert.Contains("WfWasm.Shim", AssemblyRewriter.ReferenceNames(output));
    }

    private static void ClearILOnlyFlag(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream,
            System.Reflection.PortableExecutable.PEStreamOptions.LeaveOpen);
        int rva = pe.PEHeaders.PEHeader!.CorHeaderTableDirectory.RelativeVirtualAddress;
        long flagsOffset = -1;
        foreach (var section in pe.PEHeaders.SectionHeaders)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.VirtualSize)
            {
                flagsOffset = section.PointerToRawData + (rva - section.VirtualAddress) + 16;
                break;
            }
        }
        Assert.True(flagsOffset > 0, "COR20 header not found.");
        stream.Seek(flagsOffset, SeekOrigin.Begin);
        int flags = ReadInt32(stream);
        flags &= ~0x1;
        stream.Seek(flagsOffset, SeekOrigin.Begin);
        WriteInt32(stream, flags);

        static int ReadInt32(Stream s)
        {
            Span<byte> buf = stackalloc byte[4];
            s.ReadExactly(buf);
            return BitConverter.ToInt32(buf);
        }

        static void WriteInt32(Stream s, int value)
        {
            Span<byte> buf = stackalloc byte[4];
            BitConverter.TryWriteBytes(buf, value);
            s.Write(buf);
        }
    }

    [Fact]
    public void Rewrite_Neuters_Win32_ParkHandle()
    {
        // Calendar month navigation rebuilds day controls, which hits
        // Application.ParkHandle; its WS_CHILD Debug.Assert fires on
        // emulated handles. The rewriter must reduce it to a bare ret.
        string input = Path.Combine(Path.GetTempPath(), "parkhandle.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var app = new Mono.Cecil.TypeDefinition(
                "System.Windows.Forms", "Application",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(app);
            var park = new Mono.Cecil.MethodDefinition(
                "ParkHandle",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Void);
            park.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Int32));
            app.Methods.Add(park);
            var il = park.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, "should be gone");
            il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "parkhandle.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "System.Windows.Forms.Application").Methods
            .First(m => m.Name == "ParkHandle");
        var instructions = method.Body.Instructions;
        Assert.Single(instructions);
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, instructions[0].OpCode);
    }

    [Fact]
    public void Rewrite_FontUtil_GetLogPixels_Uses_Return_Type_Ctor()
    {
        // The fixed-DPI rewrite must new up the method's return type: the
        // original body constructs several types (including exceptions), and
        // grabbing the first newobj produced invalid IL (a Win32Exception
        // where a Vector2Int32 belongs -> InvalidProgramException from
        // FontUtil.cctor on the first canvas drag).
        string input = Path.Combine(Path.GetTempPath(), "fontutil.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var point = module.ImportReference(typeof(System.Drawing.Point));
            var exCtor = module.ImportReference(typeof(Exception).GetConstructor(Type.EmptyTypes)!);
            var pointCtor = module.ImportReference(typeof(System.Drawing.Point).GetConstructor([typeof(int), typeof(int)])!);
            var util = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Drawing", "FontUtil",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(util);
            var get = new Mono.Cecil.MethodDefinition(
                "GetLogPixels",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                point);
            util.Methods.Add(get);
            var il = get.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, exCtor);
            il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_5);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_7);
            il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, pointCtor);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "fontutil.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Drawing.FontUtil").Methods
            .First(m => m.Name == "GetLogPixels");
        var newobjs = method.Body.Instructions
            .Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj)
            .ToList();
        var sole = Assert.Single(newobjs);
        var built = (Mono.Cecil.MethodReference)sole.Operand;
        Assert.Equal(method.ReturnType.FullName, built.DeclaringType.FullName);
    }

    [Fact]
    public void Rewrite_FileSystem_OpenStreamingFile_Uses_Managed_FileStream()
    {
        // History session files go through FileSystem.OpenStreamingFile,
        // whose body pinvokes CreateFileW for a handle. Every pinvoke is
        // stubbed in WASM and the stub answers a null handle, so the
        // FileStream wraps fd 0 and the commit dies at dispose-flush with
        // EBADF ("Bad file descriptor", as UnauthorizedAccessException).
        // The rewrite must open the managed way (Create/ReadWrite/Read,
        // caller-supplied buffer size, no async flags) with no pinvoke.
        string input = Path.Combine(Path.GetTempPath(), "openstreamingfile.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var fs = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.SystemLayer", "FileSystem",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(fs);
            var open = new Mono.Cecil.MethodDefinition(
                "OpenStreamingFile",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.ImportReference(typeof(System.IO.FileStream)));
            open.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.String));
            open.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.ImportReference(typeof(System.IO.FileAccess))));
            open.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.ImportReference(typeof(System.IO.FileShare))));
            open.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.ImportReference(typeof(System.IO.FileAttributes))));
            open.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.ImportReference(typeof(System.IO.FileOptions))));
            open.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Int32));
            fs.Methods.Add(open);
            var il = open.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "openstreamingfile.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.SystemLayer.FileSystem").Methods
            .First(m => m.Name == "OpenStreamingFile");
        var instructions = method.Body.Instructions;
        // ldarg.0, Create=2, ReadWrite=3, Read=1, ldarg.s 5, None=0, newobj, ret
        // (Cecil encodes small constants with the short forms.)
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldarg_0, Mono.Cecil.Cil.OpCodes.Ldc_I4_2,
                Mono.Cecil.Cil.OpCodes.Ldc_I4_3, Mono.Cecil.Cil.OpCodes.Ldc_I4_1,
                Mono.Cecil.Cil.OpCodes.Ldarg_S, Mono.Cecil.Cil.OpCodes.Ldc_I4_0,
                Mono.Cecil.Cil.OpCodes.Newobj, Mono.Cecil.Cil.OpCodes.Ret],
            instructions.Select(i => i.OpCode).ToList());
        var ctor = Assert.IsType<Mono.Cecil.MethodReference>(instructions[6].Operand);
        Assert.Equal("System.IO.FileStream", ctor.DeclaringType.FullName);
        Assert.Equal(".ctor", ctor.Name);
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "CreateFileW");
    }

    [Fact]
    public void Rewrite_GetGdiFontMap_Answers_Headless_Font_Map()
    {
        // The DWrite factory is neutered headless, so GetGdiFontMap
        // answers null and every TextTool activation dies in
        // FontMapExtensions (NRE), then poisons all pointer input with
        // the half-activated tool. The rewrite must answer a synthesized
        // map: any family resolves to default properties, inactive fonts
        // never hide.
        string input = Path.Combine(Path.GetTempPath(), "gdimap.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            module.AssemblyReferences.Add(new Mono.Cecil.AssemblyNameReference(
                "PaintDotNet.ComponentModel", new Version(1, 0, 0, 0)));
            var dw = "PaintDotNet.DirectWrite";
            Mono.Cecil.TypeDefinition Stub(string name, bool iface = false) => new(
                dw, name,
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                iface ? null : module.TypeSystem.Object);
            var ifont = Stub("IFontMap", iface: true);
            ifont.Attributes = Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Interface | Mono.Cecil.TypeAttributes.Abstract;
            var props = Stub("FontProperties");
            var tryGet = new Mono.Cecil.MethodDefinition(
                "TryGetFontProperties",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.Abstract,
                props);
            tryGet.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.String));
            ifont.Methods.Add(tryGet);
            module.Types.Add(ifont);
            var igdi = Stub("IGdiFontMap", iface: true);
            igdi.Attributes = Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Interface | Mono.Cecil.TypeAttributes.Abstract;
            igdi.Interfaces.Add(new Mono.Cecil.InterfaceImplementation(ifont));
            var hide = new Mono.Cecil.MethodDefinition(
                "get_HideInactiveFonts",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.Abstract,
                module.TypeSystem.Boolean);
            igdi.Methods.Add(hide);
            module.Types.Add(igdi);
            module.Types.Add(props);
            foreach (var ename in new[] { "FontWeight", "FontStretch", "FontStyle", "TextDecorations" })
                module.Types.Add(Stub(ename));
            var propsCtor = new Mono.Cecil.MethodDefinition(
                ".ctor",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig
                    | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            for (int i = 0; i < 6; i++)
                propsCtor.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            props.Methods.Add(propsCtor);
            var ext = new Mono.Cecil.TypeDefinition(
                dw, "DirectWriteFactoryExtensions",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(ext);
            var getMap = new Mono.Cecil.MethodDefinition(
                "GetGdiFontMap",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                igdi);
            getMap.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            getMap.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Boolean));
            ext.Methods.Add(getMap);
            var il = getMap.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "gdimap.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var types = rewritten.MainModule.GetTypes().ToList();
        var get = types.First(t => t.FullName == "PaintDotNet.DirectWrite.DirectWriteFactoryExtensions")
            .Methods.First(m => m.Name == "GetGdiFontMap");
        var instructions = get.Body.Instructions;
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Newobj, Mono.Cecil.Cil.OpCodes.Ret],
            instructions.Select(i => i.OpCode).ToList());
        var map = types.First(t => t.FullName == "PaintDotNet.DirectWrite.HeadlessGdiFontMap");
        Assert.Contains(map.Interfaces, i => i.InterfaceType.FullName == "PaintDotNet.DirectWrite.IGdiFontMap");
        var mapTryGet = map.Methods.First(m => m.Name == "TryGetFontProperties");
        Assert.Contains(
            mapTryGet.Body.Instructions,
            i => i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference mr && mr.Name == ".ctor"
                && mr.DeclaringType.FullName == "PaintDotNet.DirectWrite.FontProperties");
        var mapHide = map.Methods.First(m => m.Name == "get_HideInactiveFonts");
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldc_I4_0, Mono.Cecil.Cil.OpCodes.Ret],
            mapHide.Body.Instructions.Select(i => i.OpCode).ToList());
        // Headless QueryInterface answers true with a ref to self (the
        // toolbar font combo needs a working map): the Nullable<bool>
        // ctor must be an imported (compiler-grade) token, because the
        // WASM interpreter rejects hand-built Nullable ctor MemberRefs
        // ("Method not found", observed live).
        var mapTryCreate = map.Methods.First(m => m.Name == "TryCreateRef");
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldarg_2, Mono.Cecil.Cil.OpCodes.Ldarg_0,
             Mono.Cecil.Cil.OpCodes.Stind_Ref, Mono.Cecil.Cil.OpCodes.Ldc_I4_1,
             Mono.Cecil.Cil.OpCodes.Newobj, Mono.Cecil.Cil.OpCodes.Ret],
            mapTryCreate.Body.Instructions.Select(i => i.OpCode).ToList());
        var trueCtor = Assert.IsType<Mono.Cecil.MethodReference>(
            mapTryCreate.Body.Instructions[4].Operand);
        Assert.Equal(".ctor", trueCtor.Name);
        Assert.Contains("Nullable", trueCtor.DeclaringType.FullName);
        // CollectionChanged subscribe/unsubscribe exist as no-ops.
        foreach (var ename in new[] { "add_CollectionChanged", "remove_CollectionChanged" })
        {
            var em = map.Methods.First(m => m.Name == ename);
            Assert.Single(em.Parameters);
            Assert.Equal(
                [Mono.Cecil.Cil.OpCodes.Ret],
                em.Body.Instructions.Select(i => i.OpCode).ToList());
        }
    }

    [Fact]
    public void Rewrite_PdnResources_ResourcesDir_Avoids_Assembly_Location()
    {
        // Browser WASM assemblies have no Location (""): the original
        // get_ResourcesDir feeds it to new Uri, which throws
        // UriFormatException from PdnResources..cctor and prevents
        // AppWorkspace creation (empty form). The rewrite must return a
        // constant instead of touching Assembly.Location.
        string input = Path.Combine(Path.GetTempPath(), "pdnresources.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var location = module.ImportReference(
                typeof(System.Reflection.Assembly).GetProperty("Location")!.GetMethod!);
            var stringCtor = module.ImportReference(
                typeof(Uri).GetConstructor([typeof(string)])!);
            var res = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Resources", "PdnResources",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(res);
            var get = new Mono.Cecil.MethodDefinition(
                "get_ResourcesDir",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.String);
            res.Methods.Add(get);
            var il = get.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, location);
            il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, stringCtor);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "pdnresources.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Resources.PdnResources").Methods
            .First(m => m.Name == "get_ResourcesDir");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "get_Location");
        var first = Assert.Single(method.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr);
        Assert.NotEqual("", (string)first.Operand);
        Assert.True(Uri.TryCreate((string)first.Operand, UriKind.Absolute, out _));
    }

    [Fact]
    public void Rewrite_PdnInfo_GetApplicationDir_Avoids_Assembly_Location()
    {
        // Browser WASM assemblies have no on-disk location, so
        // GetApplicationDir's GetDirectoryName yields null and NotNull
        // throws InternalError. That kills ShapesTool activation (shape
        // catalog via plugin dirs) and wedges every later tool switch in
        // Deactivate. The rewrite must return a constant dir instead.
        string input = Path.Combine(Path.GetTempPath(), "pdninfo.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var location = module.ImportReference(
                typeof(System.Reflection.Assembly).GetProperty("Location")!.GetMethod!);
            var res = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Resources", "PdnInfo",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(res);
            var get = new Mono.Cecil.MethodDefinition(
                "GetApplicationDir",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.String);
            res.Methods.Add(get);
            var il = get.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, location);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "pdninfo.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Resources.PdnInfo").Methods
            .First(m => m.Name == "GetApplicationDir");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "get_Location");
        var first = Assert.Single(method.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr);
        Assert.NotEqual("", (string)first.Operand);
    }

    [Fact]
    public void Rewrite_WicHelpers_Extension_Lists_Are_Empty()
    {
        // No WIC codecs exist in the browser: extension enumeration dies
        // (NRE) and kills every built-in FileType .cctor when the Open
        // dialog lists file types. The rewrite must answer empty lists.
        string input = Path.Combine(Path.GetTempPath(), "wichelpers.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var res = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Data", "WicHelpers",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(res);
            foreach (var mname in new[] { "GetLoadExtensions", "GetSaveExtensions" })
            {
                var m = new Mono.Cecil.MethodDefinition(
                    mname,
                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                    module.TypeSystem.Object);
                res.Methods.Add(m);
                var il = m.Body.GetILProcessor();
                il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
                il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            }
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "wichelpers.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var methods = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Data.WicHelpers").Methods
            .Where(m => m.Name == "GetLoadExtensions" || m.Name == "GetSaveExtensions")
            .ToList();
        Assert.Equal(2, methods.Count);
        foreach (var method in methods)
        {
            Assert.Equal(
                [Mono.Cecil.Cil.OpCodes.Ldc_I4_0, Mono.Cecil.Cil.OpCodes.Newarr, Mono.Cecil.Cil.OpCodes.Ret],
                method.Body.Instructions.Select(i => i.OpCode).ToList());
            var elem = Assert.IsType<Mono.Cecil.TypeReference>(method.Body.Instructions[1].Operand);
            Assert.Equal("System.String", elem.FullName);
        }
    }

    [Fact]
    public void Rewrite_GraphicsSection_HWAccel_Default_Is_False()
    {
        // No GPU in the browser: the acceleration probe throws (caught)
        // on every boot. The rewrite answers false directly.
        string input = Path.Combine(Path.GetTempPath(), "graphicssection.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var outer = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Settings.App", "AppSettings",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(outer);
            var nested = new Mono.Cecil.TypeDefinition(
                "", "GraphicsSection",
                Mono.Cecil.TypeAttributes.NestedPublic | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            outer.NestedTypes.Add(nested);
            var m = new Mono.Cecil.MethodDefinition(
                "GetEnableHardwareAccelerationForUIDefault",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Boolean);
            nested.Methods.Add(m);
            var il = m.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "graphicssection.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Settings.App.AppSettings/GraphicsSection").Methods
            .First(m => m.Name == "GetEnableHardwareAccelerationForUIDefault");
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldc_I4_0, Mono.Cecil.Cil.OpCodes.Ret],
            method.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    [Fact]
    public void Rewrite_CpuDrawing_Skips_NonPdn_Modules()
    {
        // The fixture has no DrawingContext: passing the CPU helper
        // path must leave the module untouched (and succeed).
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string output = Path.Combine(Path.GetTempPath(), "fixture.cpuskip.dll");
        try
        {
            AssemblyRewriter.Rewrite(
                FixtureDll(), output,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
            Assert.Null(rewritten.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext"));
            Assert.Empty(rewritten.MainModule.AssemblyReferences
                .Where(r => r.Name == "WfWasm.PdnCpu"));
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    private static void AssertCalleesResolve(Mono.Cecil.ModuleDefinition module, Mono.Cecil.MethodDefinition method)
    {
        // Every callee token must resolve: hand-built MemberRefs bind
        // at write time but die on the loader (MissingMethod observed
        // live), so the rewrite only emits imported/harvested refs.
        foreach (var ins in method.Body.Instructions)
        {
            if (ins.Operand is Mono.Cecil.MethodReference mr2
                && mr2.DeclaringType.Scope is Mono.Cecil.AssemblyNameReference anr
                && anr.Name != "WfWasm.PdnCpu")
            {
                Mono.Cecil.MethodDefinition? resolved = null;
                Exception? failure = null;
                try { resolved = mr2.Resolve(); }
                catch (Exception ex) { failure = ex; }
                Assert.True(failure is null && resolved is not null,
                    $"{mr2.DeclaringType.FullName}::{mr2.Name} ({failure?.Message})");
            }
        }
    }

    [Fact]
    public void Rewrite_HalfSourceUpload_Widens_To_Bgra()
    {
        // DeviceBitmap upload assumes Bgra32 strides; Half sources
        // (checkbox bitmaps) widen through CreateBitmapFromHalfSource
        // instead of faulting RegionPtr stride validation.
        string corePath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Windows.Core.dll");
        if (!File.Exists(corePath)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"WindowsCore_HalfUp_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(
                corePath, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);

            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(corePath)!);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var createBitmap = ctx.Methods.First(m => m.Name == "CreateBitmap"
                && m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapSource");
            Assert.Contains(createBitmap.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "CreateBitmapFromHalfSource");
            AssertCalleesResolve(asm.MainModule, createBitmap);
        }
        finally { if (File.Exists(tempOut)) File.Delete(tempOut); }
    }

    [Fact]
    public void Rewrite_CpuDrawing_Synthesizes_Headless_Context()
    {
        // Brush stamps render through DrawingContext.Draw(RegionPtr):
        // the rewrite must bind a CPU subclass to the mask bytes and
        // route every brush virtual to WfWasm.PdnCpu.
        string corePath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Windows.Core.dll");
        if (!File.Exists(corePath)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"WindowsCore_Test_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(
                corePath, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);

            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(corePath)!);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var module = asm.MainModule;

            Assert.Contains(module.AssemblyReferences, r => r.Name == "WfWasm.PdnCpu");
            var ctx = module.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            // DrawingContext is sealed: the context implements
            // IDrawingContext over object instead of subclassing.
            Assert.Equal("System.Object", ctx.BaseType.FullName);
            Assert.Contains(ctx.Interfaces,
                i => i.InterfaceType.FullName == "PaintDotNet.Direct2D1.IDrawingContext");
            foreach (string name in new[]
                { "get_Transform", "set_Transform", "set_AntialiasMode", "get_AntialiasMode",
                  "CreateBitmap", "CreateSolidColorBrush", "DrawBitmap",
                  "FillRectangle", "FillEllipse" })
                Assert.Contains(ctx.Methods, m => m.Name == name && m.IsVirtual);
            Assert.Equal(2, ctx.Methods.Count(m => m.Name == "CreateBitmap"));
            Assert.NotNull(module.GetType("PaintDotNet.Direct2D1.CpuDeviceBitmap"));
            Assert.NotNull(module.GetType("PaintDotNet.Direct2D1.CpuDeviceBrush"));

            // Generic dither worker (brush sprites via GetBitmap with a
            // float pixel type): typed sources return as-is, Bgra32 and
            // Pbgra32 sources convert to Prgba128Float on the CPU.
            var genericImagingExt = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(genericImagingExt);
            var genericConverters = genericImagingExt.Methods
                .Where(m => m.Name == "CreateFormatConvertedBitmap"
                    && m.HasGenericParameters && m.Parameters.Count == 6)
                .ToList();
            Assert.NotEmpty(genericConverters);
            foreach (var converter in genericConverters)
            {
                Assert.DoesNotContain(converter.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName.Contains("ImagingFactory"));
                Assert.Contains(converter.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Isinst);
                Assert.Contains(converter.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.GenericInstanceMethod gim
                    && gim.Name == "Allocate"
                    && gim.ElementMethod.GenericParameters.Count == 1);
                Assert.Contains(converter.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                    && mr.Name == "ConvertBgra32ToPrgba128Float");
                Assert.Contains(converter.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                    && mr.Name == "ConvertPbgra32ToPrgba128Float");
            }

            // Identity format conversion short-circuits WIC: device
            // bitmap resources (brush sprites) convert Pbgra32 to
            // Pbgra32 through CreateFormatConvertedBitmap with a null
            // headless factory.
            var imagingExt = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(imagingExt);
            var converters = imagingExt.Methods
                .Where(m => m.Name == "CreateFormatConvertedBitmap"
                    && m.Parameters.Any(p => p.ParameterType is Mono.Cecil.ByReferenceType br
                        && br.ElementType.FullName == "PaintDotNet.Imaging.PixelFormat"))
                .ToList();
            Assert.NotEmpty(converters);
            foreach (var converter in converters)
            {
                Assert.DoesNotContain(converter.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName.Contains("ImagingFactory"));
                Assert.Contains(converter.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
                // The Bgra32<->Pbgra32 pair converts on the CPU: pixels
                // are copied into an allocated locked bitmap, then
                // (un)premultiplied in place. Both directions present.
                Assert.Contains(converter.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "PaintDotNet.Imaging.BitmapAllocator"
                    && mr.Name == "Allocate");
                Assert.Contains(converter.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                    && mr.Name == "PremultiplyInPlace");
                Assert.Contains(converter.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                    && mr.Name == "UnpremultiplyInPlace");
                // The Prgba128Float->Pbgra32 download (brush sprite mip
                // levels feed DrawBitmap): converted on the CPU into a
                // fresh target, scratch lock released, destination
                // returned.
                Assert.Contains(converter.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                    && mr.Name == "ConvertPrgba128FloatToPbgra32");
                Assert.Contains(converter.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                    && i.Operand is Mono.Cecil.MethodReference dmr
                    && dmr.DeclaringType.FullName == "System.IDisposable"
                    && dmr.Name == "Dispose");
            }

            // Both RegionPtr Draw overloads (plain + factory) route to
            // the CPU helper instead of the device.
            var drawCtx = module.GetType("PaintDotNet.Direct2D1.DrawingContext");
            Assert.NotNull(drawCtx);
            var draws = drawCtx.Methods
                .Where(m => m.Name == "Draw" && m.HasGenericParameters
                    && m.Parameters.Any(p => p.ParameterType is Mono.Cecil.GenericInstanceType gi
                        && gi.ElementType.FullName == "PaintDotNet.RegionPtr`1"))
                .ToList();
            Assert.Equal(2, draws.Count);
            foreach (var draw in draws)
            {
                Assert.Contains(draw.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                    && mr.Name == "CreateTarget");
                Assert.Contains(draw.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                    && i.Operand is Mono.Cecil.MethodReference nmr
                    && nmr.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuDrawingContext");
                Assert.DoesNotContain(draw.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "BeginDraw");
                // The action dispatches through Delegate.DynamicInvoke:
                // closed Action<IDrawingContext>.Invoke tokens do not
                // resolve on the WASM loader in any provenance.
                Assert.Contains(draw.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                    && i.Operand is Mono.Cecil.MethodReference dmr
                    && dmr.DeclaringType.FullName == "System.Delegate"
                    && dmr.Name == "DynamicInvoke");
                Assert.DoesNotContain(draw.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference imr
                    && imr.Name == "Invoke"
                    && imr.DeclaringType is Mono.Cecil.GenericInstanceType);
                // The DynamicInvoke wrapper is unwrapped (catch +
                // ExceptionDispatchInfo) so brush failures propagate
                // with their true type AND original stack instead of
                // TargetInvocationException (a plain throw would reset
                // the trace to Draw and hide the failing layer).
                Assert.Contains(draw.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference emr
                    && emr.DeclaringType.FullName ==
                        "System.Runtime.ExceptionServices.ExceptionDispatchInfo"
                    && emr.Name == "Capture");
                Assert.Contains(draw.Body.ExceptionHandlers, h =>
                    h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Catch);
                Assert.Contains(draw.Body.ExceptionHandlers, h =>
                    h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally);
                // Roslyn try/catch/finally nesting shape (mono
                // interpreter requirement, diffed against a reference
                // csc build): the finally-try must extend STRICTLY past
                // the catch handler end; try body and catch handler both
                // leave to a MID instruction inside the finally-try,
                // which alone leaves past the finally. A direct leave
                // out of try/catch to past the finally trips the
                // interpreter (transform.c assert -> InvalidProgram).
                var catchH = Assert.Single(draw.Body.ExceptionHandlers,
                    h => h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Catch);
                var finH = Assert.Single(draw.Body.ExceptionHandlers,
                    h => h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally);
                Assert.Equal("System.Reflection.TargetInvocationException",
                    catchH.CatchType.FullName);
                Assert.Equal(catchH.TryStart.Offset, finH.TryStart.Offset);
                Assert.Equal(finH.TryEnd.Offset, finH.HandlerStart.Offset);
                Assert.True(catchH.HandlerEnd.Offset < finH.TryEnd.Offset,
                    "finally-try must extend strictly past the catch handler end");
                var mid = catchH.HandlerEnd;
                Assert.Equal(Mono.Cecil.Cil.OpCodes.Leave, mid.OpCode);
                Assert.Same(finH.HandlerEnd, mid.Operand);
                Assert.Contains(draw.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Leave
                    && ReferenceEquals(mid, i.Operand)
                    && i.Offset < catchH.TryEnd.Offset);
                Assert.Contains(draw.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Rethrow);
            }

            // Called member tokens must not nest module-scoped
            // reference pieces: scope "Windows.Core.dll" never resolves
            // as the assembly, and the WASM loader reports it as
            // MissingMethod (observed live on Action.Invoke).
            var nested = new List<Mono.Cecil.TypeReference>();
            void Collect(Mono.Cecil.TypeReference t)
            {
                nested.Add(t);
                if (t is Mono.Cecil.GenericInstanceType gi)
                    foreach (var a in gi.GenericArguments) Collect(a);
                else if (t is Mono.Cecil.TypeSpecification spec)
                    Collect(spec.ElementType);
            }
            var fresh = new List<Mono.Cecil.MethodDefinition>();
            fresh.AddRange(ctx.Methods);
            var bmp = module.GetType("PaintDotNet.Direct2D1.CpuDeviceBitmap");
            Assert.NotNull(bmp);
            fresh.AddRange(bmp.Methods);
            var brush = module.GetType("PaintDotNet.Direct2D1.CpuDeviceBrush");
            Assert.NotNull(brush);
            fresh.AddRange(brush.Methods);
            fresh.AddRange(draws);
            foreach (var method in fresh)
            {
                Assert.True(method.Body is not null, method.FullName);
                foreach (var ins in method.Body.Instructions)
                {
                    if (ins.Operand is Mono.Cecil.MethodReference mr)
                    {
                        nested.Clear();
                        Collect(mr.DeclaringType);
                        Collect(mr.ReturnType);
                        foreach (var p in mr.Parameters) Collect(p.ParameterType);
                        foreach (var t in nested)
                        {
                            // Same-module definitions encode as TypeDef
                            // tokens and generic parameters as positional
                            // !!0/!0; both are fine. Module-scoped
                            // REFERENCES are the MissingMethod shape.
                            if (t is not Mono.Cecil.TypeDefinition
                                && t is not Mono.Cecil.GenericParameter
                                && t.Scope is Mono.Cecil.ModuleDefinition)
                                Assert.Fail(
                                    $"{method.FullName} -> {mr.Name} nests module-scoped {t.FullName}");
                        }
                    }
                    // Helper calls (new edge, verified live) and runtime
                    // imports (canonical by construction) resolve outside
                    // the PDN universe; everything else must.
                    if (ins.Operand is Mono.Cecil.MethodReference mr2
                        && mr2.DeclaringType.Scope is Mono.Cecil.AssemblyNameReference anr
                        && anr.Name != "WfWasm.PdnCpu"
                        && !anr.Name.StartsWith("System.", StringComparison.Ordinal)
                        && anr.Name != "mscorlib" && anr.Name != "netstandard")
                    {
                        Mono.Cecil.MethodDefinition? resolved = null;
                        Exception? failure = null;
                        try { resolved = mr2.Resolve(); }
                        catch (Exception ex) { failure = ex; }
                        Assert.True(failure is null && resolved is not null,
                            $"{method.FullName} -> {mr2.DeclaringType.FullName}::{mr2.Name} ({failure?.Message})");
                    }
                }
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void Rewrite_CpuDrawing_ResourceCache_Backs_Brush_Resources()
    {
        // Brush stamps pull white-pixel, sprite and solid-color
        // resources through the drawing context's resource cache: the
        // CPU context must answer GetCachedOrCreateResource with real
        // CPU objects instead of the headless-backend throw (the stroke
        // died here once the float-download fix unblocked sprites).
        string corePath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Windows.Core.dll");
        if (!File.Exists(corePath)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"WindowsCore_ResCache_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(
                corePath, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);

            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(corePath)!);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var module = asm.MainModule;
            var ctx = module.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var resCache = Assert.Single(ctx.Methods,
                m => m.Name == "GetCachedOrCreateResource" && m.IsVirtual);
            // Version-keyed Hashtable cache with a churn cap.
            foreach (string htOp in new[] { "get_Item", "set_Item", "get_Count", "Clear" })
                Assert.Contains(resCache.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "System.Collections.Hashtable"
                    && mr.Name == htOp);
            // Provider dispatch names both brush providers; anything
            // else throws loudly and names its type.
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string)i.Operand == "PaintDotNet.UI.Media.DeviceBitmap");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string)i.Operand == "PaintDotNet.UI.Media.SolidColorBrush");
            // Text button images (FontStyleFlagsButtonGroup) cache the
            // TextLayout provider itself: it already carries Text, and
            // the CPU text routes read it back by reflection.
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string)i.Operand == "PaintDotNet.UI.Media.TextLayout");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string)i.Operand == "get_Text");
            // The layout provider is not an IObjectRef, so the cache
            // builds a CPU text layout from its Text/FontSize/MaxWidth/
            // MaxHeight instead of returning the provider itself (a raw
            // return died live with InvalidCastException).
            foreach (string probe in new[] { "get_FontSize", "get_MaxWidth", "get_MaxHeight" })
                Assert.Contains(resCache.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                    && (string)i.Operand == probe);
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference nmr
                && nmr.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuTextLayout");
            var cpuText = module.GetType("PaintDotNet.Direct2D1.CpuTextLayout");
            Assert.NotNull(cpuText);
            Assert.Contains(cpuText.Interfaces,
                ii => ii.InterfaceType.FullName == "PaintDotNet.DirectWrite.ITextLayout");
            Assert.Contains(cpuText.Methods, m => m.Name == "get_Text");
            Assert.Contains(cpuText.Methods, m => m.Name == "get_Metrics");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference nmr
                && nmr.DeclaringType.FullName == "System.NotSupportedException");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
            AssertCalleesResolve(module, resCache);

            // White pixel: 1x1 CPU bitmap, -1 fill, wrapped as a device
            // bitmap; sprite uploads route to the CPU creator.
            Assert.Contains(resCache.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "CreateBitmapEmpty");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference nmr
                && nmr.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuDeviceBitmap");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodDefinition md
                && md.Name == "CreateBitmap" && md.Parameters.Count == 2);
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodDefinition md
                && md.Name == "CreateSolidColorBrush" && md.Parameters.Count == 2);
            // Cache answers are their own refs: both CPU device types
            // implement IObjectRef, and neither ref member throws.
            foreach (string deviceType in new[]
                { "PaintDotNet.Direct2D1.CpuDeviceBitmap", "PaintDotNet.Direct2D1.CpuDeviceBrush" })
            {
                var device = module.GetType(deviceType);
                Assert.NotNull(device);
                Assert.Contains(device.Interfaces,
                    ii => ii.InterfaceType.FullName == "PaintDotNet.ComponentModel.IObjectRef");
                // Backing fields stay public: sibling synthesized types
                // read them cross-class, and private fields throw
                // FieldAccessException there (observed live on _handle).
                foreach (var field in device.Fields)
                    Assert.True(field.IsPublic, $"{deviceType}.{field.Name} must be public");
                var tryCreate = Assert.Single(device.Methods, m => m.Name == "TryCreateRef");
                Assert.DoesNotContain(tryCreate.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
                Assert.Contains(device.Methods, m => m.Name == "InternalImplementationOnly");
            }

            // Draw(IBitmap, Action) and Draw(void*, ...) drivers (sprite
            // dab rendering): same CPU binding plus the shared EH tail,
            // with only resolvable callee tokens.
            var drawCtxT = module.GetType("PaintDotNet.Direct2D1.DrawingContext");
            Assert.NotNull(drawCtxT);
            var bitmapDraws = drawCtxT.Methods
                .Where(m => m.Name == "Draw"
                    && m.Parameters.Any(p => p.ParameterType.FullName == "PaintDotNet.Imaging.IBitmap"))
                .ToList();
            Assert.Equal(2, bitmapDraws.Count);
            var rawDraws = drawCtxT.Methods
                .Where(m => m.Name == "Draw"
                    && m.Parameters.Any(p => p.ParameterType is Mono.Cecil.PointerType ptr
                        && ptr.ElementType.FullName == "System.Void"))
                .ToList();
            Assert.Equal(2, rawDraws.Count);
            foreach (var draw in bitmapDraws.Concat(rawDraws))
            {
                Assert.Contains(draw.Body.Instructions, i =>
                    i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                    && mr.Name == "CreateTarget");
                Assert.Contains(draw.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                    && i.Operand is Mono.Cecil.MethodReference dmr
                    && dmr.DeclaringType.FullName == "System.Delegate"
                    && dmr.Name == "DynamicInvoke");
                Assert.Contains(draw.Body.ExceptionHandlers, h =>
                    h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Catch);
                Assert.Contains(draw.Body.ExceptionHandlers, h =>
                    h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally);
                AssertCalleesResolve(module, draw);

            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
    [Fact]
    public void Rewrite_HeadlessAnimation_UiaFactoriesAnswerCpuNoOps()
    {
        // COM UIAnimation has no WASM backend, so AnimationService..ctor
        // died in NotNull at boot; the UIA factories now answer CPU
        // no-ops whose Try* methods return E_NOTIMPL through the
        // callers' own error channels, while anything else stays loud.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Windows_Anim_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var module = asm.MainModule;
            var uia = module.GetType("PaintDotNet.Animation.UIA");
            Assert.NotNull(uia);
            (string Method, string Cpu)[] kinds =
            [
                ("CreateAnimationManager", "PaintDotNet.Animation.CpuAnimationManager"),
                ("CreateAnimationTimer", "PaintDotNet.Animation.CpuAnimationTimer"),
                ("CreateAnimationTransitionFactory", "PaintDotNet.Animation.CpuAnimationTransitionFactory"),
                ("CreateAnimationTransitionLibrary", "PaintDotNet.Animation.CpuAnimationTransitionLibrary"),
            ];
            foreach (var (method, cpu) in kinds)
            {
                var cpuType = module.GetType(cpu);
                Assert.NotNull(cpuType);
                var factory = Assert.Single(uia.Methods,
                    m => m.Name == method && m.Parameters.Count == 0);
                Assert.Contains(factory.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                    && i.Operand is Mono.Cecil.MethodReference nmr
                    && nmr.DeclaringType.FullName == cpu
                    && nmr.Name == ".ctor");
                AssertCalleesResolve(module, factory);
            }
            // Idle status so PollForAnimationVariableChanges loops zero
            // times without touching the (null) storyboard.
            var manager = module.GetType("PaintDotNet.Animation.CpuAnimationManager");
            var status = Assert.Single(manager.Methods, m => m.Name == "get_Status");
            Assert.Contains(status.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4 && (int)i.Operand == 0);
            // Errors flow through InteropErrorInfo, never exceptions.
            var tryCreate = manager.Methods.First(m => m.Name == "TryCreateAnimationVariable");
            Assert.DoesNotContain(tryCreate.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
            Assert.Contains(tryCreate.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.Name == "op_Implicit");
            AssertCalleesResolve(module, tryCreate);
            // Unknown backends stay loud: transitions have no headless form.
            var tFactory = module.GetType("PaintDotNet.Animation.CpuAnimationTransitionFactory");
            var createTransition = tFactory.Methods.First(m => m.Name == "CreateTransition");
            Assert.Contains(createTransition.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
    [Fact]
    public void Rewrite_InlinePost_PdnSyncContext_RunsInline()
    {
        // Single-threaded WASM has no message pump: the post queue
        // behind PdnSynchronizationContext.Post is never pumped, so
        // floating-tool-window Shows and deferred UI refreshes queue
        // forever. Post/PostUserApc now invoke inline (the caller is
        // already on the only thread).
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Base.dll");
        if (!File.Exists(input)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"Base_InlinePost_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal));
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var sync = asm.MainModule.GetType("PaintDotNet.Threading.PdnSynchronizationContext");
            Assert.NotNull(sync);
            var posts = sync.Methods.Where(m => m.Name == "Post" && m.HasBody).ToList();
            Assert.Equal(2, posts.Count);
            foreach (var post in posts)
            {
                var calls = post.Body.Instructions
                    .Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
                    .ToList();
                var invoke = Assert.Single(calls);
                Assert.Equal("Invoke", ((Mono.Cecil.MethodReference)invoke.Operand).Name);
                Assert.DoesNotContain(post.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
                AssertCalleesResolve(asm.MainModule, post);
            }
            var apc = Assert.Single(sync.Methods, m => m.Name == "PostUserApc" && m.HasBody);
            Assert.Contains(apc.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                && i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "Invoke");
            AssertCalleesResolve(asm.MainModule, apc);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
    [Fact]
    public void Rewrite_ControlDeviceContext_Answers_ScratchCpu()
    {
        // Wheel/effect resource creation needs a context object but
        // never draws: get_DeviceContext answers a CPU context over
        // the shared scratch target instead of null.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Framework.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Paint_DeviceCtx_{Guid.NewGuid()}.dll");
        string stage = Path.Combine(Path.GetTempPath(), "Stage_DeviceCtx_" + Guid.NewGuid());
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            Directory.CreateDirectory(stage);
            AssemblyRewriter.Rewrite(Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll"), Path.Combine(stage, "PaintDotNet.Windows.Core.dll"),
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(stage);
            resolver.AddSearchDirectory(Path.GetDirectoryName(cpuDll)!);
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var d2d = asm.MainModule.GetType("PaintDotNet.Controls.Direct2DControl");
            Assert.NotNull(d2d);
            var getDC = Assert.Single(d2d.Methods,
                m => m.Name == "get_DeviceContext" && m.Parameters.Count == 0);
            Assert.Contains(getDC.Body.Instructions, i =>
                (i.OpCode == Mono.Cecil.Cil.OpCodes.Call || i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
                && i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "GetControlScratchTarget");
            Assert.Contains(getDC.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference nmr
                && nmr.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.DoesNotContain(getDC.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldnull);
            AssertCalleesResolve(asm.MainModule, getDC);
            asm.Dispose();
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
            for (int i = 0; i < 10 && Directory.Exists(stage); i++)
            {
                try { Directory.Delete(stage, true); break; }
                catch (IOException) { System.Threading.Thread.Sleep(300); }
            }
        }
    }

    private static string PdnTargetDll(string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "targets", "paint.net", name));
        Assert.True(File.Exists(dll), $"pdn target missing: {dll}");
        return dll;
    }

    private static Mono.Cecil.MethodReference ImportCpu(
        Mono.Cecil.ModuleDefinition module, Mono.Cecil.AssemblyDefinition cpuAsm, string name, int argc)
    {
        var def = cpuAsm.MainModule.GetType("WfWasm.PdnCpu.CpuPaint")!.Methods
            .First(m => m.Name == name && m.Parameters.Count == argc);
        return module.ImportReference(def);
    }

    [Fact]
    public void Rewrite_HalfColorTransformer_Converts_On_Cpu()
    {
        // The checkbox path (VerticalImageStrip) converts Bgra32 art to
        // Half bitmaps through generic color-transformer overloads whose
        // WIC factory is null headless: the rule must stage a CPU
        // conversion instead of the blind identity (InvalidCast at the
        // wrapper). Runs against pristine Windows.Core, in-memory only.
        using var cpuAsm = Mono.Cecil.AssemblyDefinition.ReadAssembly(ShimBinDll("WfWasm.PdnCpu"));
        string corePath = PdnTargetDll("PaintDotNet.Windows.Core.dll");
        var resolver = new Mono.Cecil.DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(corePath)!);
        var readParams = new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver };
        using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(corePath, readParams);
        var module = asm.MainModule;
        var rule = typeof(AssemblyRewriter).GetMethod(
            "RewriteHalfColorTransformer", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(rule);
        rule.Invoke(null, new object[]
        {
            module,
            ImportCpu(module, cpuAsm, "PremultiplyInPlace", 4),
            ImportCpu(module, cpuAsm, "ConvertBgra32ToPrgba64Half", 6),
            ImportCpu(module, cpuAsm, "ConvertPbgra32ToPrgba64Half", 6),
            ImportCpu(module, cpuAsm, "ConvertBgra32ToPrgba128Float", 6),
            ImportCpu(module, cpuAsm, "ConvertPbgra32ToPrgba128Float", 6),
        });
        static bool Calls(Mono.Cecil.MethodDefinition m, string name) =>
            m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == name);
        var bse = module.GetType("PaintDotNet.Imaging.BitmapSourceExtensions");
        Assert.NotNull(bse);
        var genericBse = bse.Methods.Where(m => m.Name == "CreateColorTransformer"
            && m.HasGenericParameters && m.Parameters.Count == 3
            && m.Parameters[0].ParameterType is Mono.Cecil.GenericInstanceType).ToList();
        Assert.NotEmpty(genericBse);
        foreach (var m in genericBse)
        {
            Assert.False(Calls(m, "CastOrTransferRef"));
            Assert.True(Calls(m, "ConvertBgra32ToPrgba64Half"));
        }
        var ife = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
        Assert.NotNull(ife);
        var genericIfe = ife.Methods.Where(m => m.Name == "CreateColorTransformedBitmap"
            && m.HasGenericParameters && m.Parameters.Count == 4
            && m.ReturnType is Mono.Cecil.GenericInstanceType).ToList();
        Assert.NotEmpty(genericIfe);
        foreach (var m in genericIfe)
        {
            Assert.False(Calls(m, "CastOrTransferRef"));
            Assert.True(Calls(m, "ConvertBgra32ToPrgba64Half"));
        }
    }

    [Fact]
    public void Rewrite_CheckerEffects_Evaluate_On_Cpu()
    {
        // Procedural effects have no device, but their constructors
        // run for real: CreateEffect answers a CPU effect, so the
        // Properties bag is live and radio/color renders stop NREing.
        // Property writes stay inert (device bag), and DrawImage
        // evaluates crop(checker) and bare checker procedurally.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        var emptyTypes = new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal);
        string paintIn = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        string coreIn = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(paintIn) || !File.Exists(coreIn)) return;
        string paintOut = Path.Combine(Path.GetTempPath(), $"Paint_Checker_{Guid.NewGuid()}.dll");
        string coreOut = Path.Combine(Path.GetTempPath(), $"Core_Checker_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(paintIn, paintOut, empty, emptyTypes, cpuDll);
            AssemblyRewriter.Rewrite(coreIn, coreOut, empty, emptyTypes, cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            resolver.AddSearchDirectory(Path.GetDirectoryName(cpuDll)!);
            using var paint = Mono.Cecil.AssemblyDefinition.ReadAssembly(paintOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ctx = paint.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var drawImage = ctx.Methods.First(m => m.Name == "DrawImage");
            Assert.Contains(drawImage.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "DrawChecker");
            // Effect proxies (palette swatches through a color
            // management proxy) unwrap pass-through inputs to the
            // shared bitmap blit instead of throwing.
            Assert.Contains(drawImage.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference pmr &&             pmr.Name == "GetProxySourceImage");
            // Bare procedural effects (cropped CheckerboardEffect
            // swatches) rasterize headless instead of throwing.
            Assert.Contains(drawImage.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Call
            && i.Operand is Mono.Cecil.MethodReference dpmr &&             dpmr.Name == "DrawProceduralEffect");
            // Unwrapped command lists skip the draw (their pixels
            // already executed into the target): the CpuCommandList
            // check sits after the proxy unwrap, not before it.
            var ins = drawImage.Body.Instructions.ToList();
            int proxyPos = ins.FindIndex(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call
            && i.Operand is Mono.Cecil.MethodReference qmr && qmr.Name == "GetProxySourceImage");
            int cmdPos = proxyPos < 0 ? -1 : ins.FindIndex(proxyPos, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Isinst
            && i.Operand is Mono.Cecil.TypeReference ctr && ctr.Name == "CpuCommandList");
            Assert.True(proxyPos >= 0 && cmdPos > proxyPos);
            // Crop input resolves through the IDeviceEffect interface,
            // never through a MemberRef walk to the generic props: that
            // trips Mono class loading on WASM (null storage reads).
            Assert.Contains(drawImage.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                && i.Operand is Mono.Cecil.MethodReference iref
                && iref.Name == "GetInput"
                && iref.DeclaringType.FullName == "PaintDotNet.Direct2D1.IDeviceEffect");
            Assert.DoesNotContain(drawImage.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                && i.Operand is Mono.Cecil.MethodReference pref && pref.Name == "get_Properties");
            Assert.Contains(drawImage.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr2 && mr2.Name == "GetTargetSize");
            AssertCalleesResolve(paint.MainModule, drawImage);
            // Headless effect backend: CreateEffect answers a CPU
            // effect instead of throwing, so auto-registration builds
            // live Properties bags.
            var cpuEffect = paint.MainModule.GetType("PaintDotNet.Direct2D1.CpuDeviceEffect");
            Assert.NotNull(cpuEffect);
            Assert.Contains(cpuEffect.Interfaces, ii => ii.InterfaceType.FullName == "PaintDotNet.Direct2D1.IDeviceEffect");
            foreach (string member in new[] { "Dispose", "get_IsDisposed", "TryCreateRef", "GetInput", "SetInput", "IsInputSet", "get_InputCount", "set_InputCount", "get_PropertyCount" })
                Assert.Contains(cpuEffect.Methods, m => m.Name == member);
            var createEffect = ctx.Methods.First(m => m.Name == "CreateEffect");
            // Borrowed-grant Dispose must not poison later casts:
            // ByRefHandle<T>.Dispose runs on every accessor Set/Get, so
            // Dispose is a silent no-op and IsDisposed answers false.
            var fxDispose = cpuEffect.Methods.First(m => m.Name == "Dispose" && m.Parameters.Count == 0);
            Assert.Single(fxDispose.Body.Instructions);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, fxDispose.Body.Instructions[0].OpCode);
            var fxIsDisposed = cpuEffect.Methods.First(m => m.Name == "get_IsDisposed");
            Assert.DoesNotContain(fxIsDisposed.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldfld);
            Assert.DoesNotContain(createEffect.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
            Assert.Contains(createEffect.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference nr && nr.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuDeviceEffect");
            AssertCalleesResolve(paint.MainModule, createEffect);
            // COM-less creation: the factory overload that wraps the
            // native pointer answers CPU contexts with S_OK directly.
            string winIn = Path.Combine(paintDir, "PaintDotNet.Windows.dll");
            string winOut = Path.Combine(Path.GetTempPath(), $"Win_Fx_{Guid.NewGuid()}.dll");
            try
            {
                AssemblyRewriter.Rewrite(winIn, winOut, empty, emptyTypes, cpuDll);
                using var win = Mono.Cecil.AssemblyDefinition.ReadAssembly(winOut,
                    new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
                var fxExt = win.MainModule.GetType("PaintDotNet.Direct2D1.DeviceEffectFactoryExtensions");
                Assert.NotNull(fxExt);
                var tryCreate = fxExt.Methods.First(m => m.Name == "TryCreateEffect"
                    && m.Parameters.Count == 3
                    && m.Parameters[2].ParameterType.FullName == "PaintDotNet.Direct2D1.IDeviceEffect&");
                Assert.Equal(Mono.Cecil.Cil.OpCodes.Ldarg_0, tryCreate.Body.Instructions[0].OpCode);
                Assert.Contains(tryCreate.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Stind_Ref);
                AssertCalleesResolve(win.MainModule, tryCreate);
            }
            finally
            {
                if (File.Exists(winOut)) File.Delete(winOut);
            }
            string coreFxIn = Path.Combine(paintDir, "PaintDotNet.Core.dll");
            string coreFxOut = Path.Combine(Path.GetTempPath(), $"CoreFx_Checker_{Guid.NewGuid()}.dll");
            try
            {
                AssemblyRewriter.Rewrite(coreFxIn, coreFxOut, empty, emptyTypes, cpuDll);
                using var coreFx = Mono.Cecil.AssemblyDefinition.ReadAssembly(coreFxOut,
                    new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
                var checkerFx = coreFx.MainModule.GetType("PaintDotNet.VisualStyling.CheckerboardEffect");
                Assert.NotNull(checkerFx);
                // The constructor chains to the base effect .ctor (live
                // Properties); it is not a bare ret anymore.
                var checkerCtor = checkerFx.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 1);
                Assert.True(checkerCtor.Body.Instructions.Count > 1);
                Assert.Contains(checkerCtor.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                    && i.Operand is Mono.Cecil.MethodReference cr && cr.Name == ".ctor");
                AssertCalleesResolve(coreFx.MainModule, checkerCtor);
            }
            finally
            {
                if (File.Exists(coreFxOut)) File.Delete(coreFxOut);
            }
            using var core = Mono.Cecil.AssemblyDefinition.ReadAssembly(coreOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var crop = core.MainModule.GetType("PaintDotNet.Direct2D1.Effects.CropEffect");
            if (crop is not null)
            {
                var cropCtor = crop.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 1);
                Assert.True(cropCtor.Body.Instructions.Count > 1);
            }
            var acc = core.MainModule.GetType("PaintDotNet.Direct2D1.Effects.EffectPropertyAccessor`1");
            if (acc is not null)
            {
                var set = acc.Methods.First(m => m.Name == "SetValue" && m.Parameters.Count == 1);
                Assert.Single(set.Body.Instructions);
                Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, set.Body.Instructions[0].OpCode);
            }
        }
        finally
        {
            if (File.Exists(paintOut)) File.Delete(paintOut);
            if (File.Exists(coreOut)) File.Delete(coreOut);
        }
    }

    [Fact]
    public void Rewrite_SystemBrush_Resource_Backs_Theme_Brush()
    {
        // Separator lines pull theme brushes through the resource
        // cache: providers exposing a SystemColor getter decompose
        // through it to a CPU solid brush (duck-typed: no provider
        // type-name comparison is involved).
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Paint_SysBrush_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var resCache = ctx.Methods.First(m => m.Name == "GetCachedOrCreateResource");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string?)i.Operand == "get_SystemColor");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Isinst);
            AssertCalleesResolve(asm.MainModule, resCache);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
    [Fact]
    public void Rewrite_SpritePipeline_CommandList_Clear_DrawImage()
    {
        // Sprite dab rendering (brush tip): an inert command-list
        // token, a real Clear, the image blit-back and a null target
        // on the CPU context (Windows.Core); the circle hard shape
        // bypasses its factory-bound geometry path (paintdotnet); the
        // COM-bound IsDrawing check reports false (Windows.dll).
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        var emptyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var emptyTypes = new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal);

        string RewriteReal(string name)
        {
            string input = Path.Combine(paintDir, name);
            if (!File.Exists(input)) return "";
            string output = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(name)}_Sprite_{Guid.NewGuid()}.dll");
            AssemblyRewriter.Rewrite(input, output, emptyMap, emptyTypes, cpuDll);
            return output;
        }

        Mono.Cecil.AssemblyDefinition ReadReal(string path)
        {
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            return Mono.Cecil.AssemblyDefinition.ReadAssembly(path,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
        }

        string coreOut = RewriteReal("PaintDotNet.Windows.Core.dll");
        if (coreOut == "") return;
        try
        {
            using var asm = ReadReal(coreOut);
            var module = asm.MainModule;
            var ctx = module.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var cmdList = module.GetType("PaintDotNet.Direct2D1.CpuCommandList");
            Assert.NotNull(cmdList);
            Assert.Contains(cmdList.Interfaces,
                ii => ii.InterfaceType.FullName == "PaintDotNet.Direct2D1.ICommandList");
            var createCmdList = Assert.Single(ctx.Methods,
                m => m.Name == "CreateCommandList" && m.Parameters.Count == 0);
            Assert.Contains(createCmdList.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodDefinition md
                && md.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuCommandList");
            var clear = Assert.Single(ctx.Methods,
                m => m.Name == "Clear" && m.Parameters.Count == 1);
            Assert.Contains(clear.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "ClearTarget");
            var drawImage = Assert.Single(ctx.Methods,
                m => m.Name == "DrawImage" && m.Parameters.Count == 5);
            Assert.Contains(drawImage.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "DrawBitmap");
            var getTarget = Assert.Single(ctx.Methods,
                m => m.Name == "GetTarget" && m.Parameters.Count == 0);
            Assert.DoesNotContain(getTarget.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
            foreach (var m in new[] { createCmdList, clear, drawImage, getTarget })
                AssertCalleesResolve(module, m);
        }
        finally
        {
            if (File.Exists(coreOut)) File.Delete(coreOut);
        }

        string pdnOut = RewriteReal("paintdotnet.dll");
        if (pdnOut == "") return;
        try
        {
            using var asm = ReadReal(pdnOut);
            var circle = asm.MainModule.GetType("PaintDotNet.Brushes.Basic.SampledCircleBrushRenderer");
            Assert.NotNull(circle);
            var hardShape = Assert.Single(circle.Methods, m => m.Name == "DrawHardShape");
            Assert.Contains(hardShape.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.Name == "FillEllipse");
            Assert.DoesNotContain(hardShape.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr
                && (mr.Name == "CreateEllipseGeometry" || mr.Name == "FillGeometry"));
            AssertCalleesResolve(asm.MainModule, hardShape);
        }
        finally
        {
            if (File.Exists(pdnOut)) File.Delete(pdnOut);
        }

        string winOut = RewriteReal("PaintDotNet.Windows.dll");
        if (winOut == "") return;
        try
        {
            using var asm = ReadReal(winOut);
            var ext = asm.MainModule.GetType("PaintDotNet.Direct2D1.DeviceContextExtensions");
            Assert.NotNull(ext);
            var isDrawing = Assert.Single(ext.Methods, m => m.Name == "IsDrawing");
            var ops = isDrawing.Body.Instructions.Select(i => i.OpCode).ToList();
            Assert.Equal(2, ops.Count);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ldc_I4_0, ops[0]);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, ops[1]);
        }
        finally
        {
            if (File.Exists(winOut)) File.Delete(winOut);
        }
    }

    [Fact]
    public void CreateSpriteImage_Uses_CpuSprite()
    {
        // The brush-tip sprite effect chain (SrgbToLinear/GaussianBlur/
        // LinearToSrgb/Invert/CopyBToA) has no D2D backend headless: the
        // sprite body must call CpuPaint.CreateSprite/16 and wrap the
        // handle in the sibling core's CpuDeviceBitmap, with all geometry
        // (diameters, Lerp, measured size) from PDN's own math.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string coreInput = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        string pdnInput = Path.Combine(paintDir, "paintdotnet.dll");
        if (!File.Exists(coreInput) || !File.Exists(pdnInput)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        var emptyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var emptyTypes = new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal);
        string stage = Path.Combine(Path.GetTempPath(), $"CpuSprite_{Guid.NewGuid()}");
        Directory.CreateDirectory(stage);
        try
        {
            string coreOut = Path.Combine(stage, "PaintDotNet.Windows.Core.dll");
            string pdnOut = Path.Combine(stage, "paintdotnet.dll");
            // Bundle order rewrites paintdotnet before the sibling core,
            // so the rule must not require the rewritten core on disk.
            AssemblyRewriter.Rewrite(pdnInput, pdnOut, emptyMap, emptyTypes, cpuDll);
            AssemblyRewriter.Rewrite(coreInput, coreOut, emptyMap, emptyTypes, cpuDll);
            using var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(stage);
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(pdnOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var renderer = asm.MainModule.GetType("PaintDotNet.Brushes.Basic.BasicSampledBrushRenderer");
            Assert.NotNull(renderer);
            var sprite = Assert.Single(renderer.Methods,
                m => m.Name == "CreateSpriteImage" && m.Parameters.Count == 7);
            Assert.Contains(sprite.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "CreateSprite");
            Assert.Contains(sprite.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuDeviceBitmap");
            Assert.DoesNotContain(sprite.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr
                && (mr.Name == "GaussianBlur" || mr.Name == "SrgbToLinear"
                    || mr.Name == "LinearToSrgb" || mr.Name == "CopyBToA"));
            AssertCalleesResolve(asm.MainModule, sprite);
        }
        finally
        {
            Directory.Delete(stage, recursive: true);
        }
    }

    [Fact]
    public void FillExtensions_Use_CpuPaint()
    {
        // Icon/cursor rasterization fills vector geometries through
        // D2D: the extension fills must call CpuPaint (FillGeometry/
        // FillRect/FillEllipse), resolve every callee (including the
        // synthesized GetCpuTargetHandle), and drop the device calls.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string coreInput = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(coreInput)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        var emptyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var emptyTypes = new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal);
        string tempOut = Path.Combine(Path.GetTempPath(), $"WindowsCore_Fill_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(coreInput, tempOut, emptyMap, emptyTypes, cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ext = asm.MainModule.GetType("PaintDotNet.Direct2D1.DrawingContextExtensions");
            Assert.NotNull(ext);
            var fillGeo = Assert.Single(ext.Methods,
                m => m.Name == "FillGeometry" && m.Parameters.Count == 4);
            Assert.Contains(fillGeo.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "FillGeometry");
            var fillRect = Assert.Single(ext.Methods,
                m => m.Name == "FillRectangle" && m.Parameters.Count == 3
                    && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Rendering.RectDouble");
            Assert.Contains(fillRect.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "FillRect");
            Assert.Contains(fillRect.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "DrawChecker");
            var fillEll = Assert.Single(ext.Methods,
                m => m.Name == "FillEllipse" && m.Parameters.Count == 3
                    && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Rendering.EllipseDouble");
            Assert.Contains(fillEll.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "FillEllipse");
            foreach (var m in new[] { fillGeo, fillRect, fillEll })
            {
                Assert.DoesNotContain(m.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                    && i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "PaintDotNet.Direct2D1.IDeviceContext");
                AssertCalleesResolve(asm.MainModule, m);
            }
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            Assert.Contains(ctx.Methods, m => m.Name == "GetCpuTargetHandle"
                && m.ReturnType.FullName == "System.Int64");
            var useLayer = Assert.Single(ext.Methods,
                m => m.Name == "UseLayer" && m.Parameters.Count == 8);
            Assert.DoesNotContain(useLayer.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_2);
            Assert.Contains(useLayer.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldnull);
            AssertCalleesResolve(asm.MainModule, useLayer);
            var drawGeo = Assert.Single(ext.Methods,
                m => m.Name == "DrawGeometry" && m.Parameters.Count == 5);
            Assert.Contains(drawGeo.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "StrokeGeometry");
            var drawPen = Assert.Single(ext.Methods,
                m => m.Name == "DrawGeometry" && m.Parameters.Count == 3);
            Assert.Contains(drawPen.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "StrokeGeometry");
            foreach (var m in new[] { drawGeo, drawPen })
            {
                Assert.DoesNotContain(m.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                    && i.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.FullName == "PaintDotNet.Direct2D1.IDeviceContext");
                AssertCalleesResolve(asm.MainModule, m);
            }
            var drawText = Assert.Single(ext.Methods,
                m => m.Name == "DrawTextLayout" && m.Parameters.Count == 5);
            Assert.Contains(drawText.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "DrawText");
            Assert.DoesNotContain(drawText.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "PaintDotNet.Direct2D1.IDeviceContext");
            AssertCalleesResolve(asm.MainModule, drawText);
            // Null is the headless answer: every caller null-checks
            // (scopes go inert, the algorithm query defaults).
            var getParams = Assert.Single(ctx.Methods,
                m => m.Name == "GetTextRenderingParameters" && m.Parameters.Count == 0);
            Assert.Equal(2, getParams.Body.Instructions.Count);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ldnull, getParams.Body.Instructions[0].OpCode);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, getParams.Body.Instructions[1].OpCode);
            var textScope = asm.MainModule.GetType("PaintDotNet.Direct2D1.PushTextRenderingModeScope");
            Assert.NotNull(textScope);
            var scopeCtor = Assert.Single(textScope.Methods,
                m => m.Name == ".ctor" && m.Parameters.Count == 2);
            Assert.DoesNotContain(scopeCtor.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr
                && mr.Name == "GetTextRenderingParameters");
            AssertCalleesResolve(asm.MainModule, scopeCtor);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void PersistToDisk_Is_Headless_Noop()
    {
        // Settings persistence has no disk in the browser (and
        // BinaryFormatter is gone in net9): the flush must reduce to
        // Ret instead of throwing per boot.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string pdnInput = Path.Combine(paintDir, "paintdotnet.dll");
        if (!File.Exists(pdnInput)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        var emptyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var emptyTypes = new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal);
        string tempOut = Path.Combine(Path.GetTempPath(), $"PDN_Persist_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(pdnInput, tempOut, emptyMap, emptyTypes, cpuDll);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var persisted = asm.MainModule.GetType("PaintDotNet.Runtime.PersistedObject`1");
            Assert.NotNull(persisted);
            var persist = Assert.Single(persisted.Methods,
                m => m.Name == "PersistToDisk" && m.Parameters.Count == 1);
            Assert.Single(persist.Body.Instructions);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, persist.Body.Instructions[0].OpCode);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void AdjustFontSize_Measures_Without_D2D()
    {
        // Font-size fitting measures through the D2D layout; the
        // measure step must construct TextMetrics on the CPU while
        // the shrink loop stays pristine.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string uiInput = Path.Combine(paintDir, "PaintDotNet.UI.dll");
        if (!File.Exists(uiInput)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        var emptyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var emptyTypes = new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal);
        string tempOut = Path.Combine(Path.GetTempPath(), $"PDNUI_Adjust_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(uiInput, tempOut, emptyMap, emptyTypes, cpuDll);
            using var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var factory = asm.MainModule.GetType("PaintDotNet.UI.Media.UITextFactory");
            Assert.NotNull(factory);
            var adjust = Assert.Single(factory.Methods,
                m => m.Name == "AdjustFontSizeToFitLayoutSize" && m.Parameters.Count == 6);
            Assert.DoesNotContain(adjust.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.GenericInstanceMethod gim
                && gim.Name == "GetCachedOrCreateResource"
                && gim.GenericArguments.Count == 1
                && gim.GenericArguments[0].FullName == "PaintDotNet.DirectWrite.ITextLayout");
            Assert.Contains(adjust.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "PaintDotNet.DirectWrite.TextMetrics");
            // Inserted spans overflow short branches (the WASM
            // interpreter aborts transform on truncated offsets).
            Assert.DoesNotContain(adjust.Body.Instructions, i =>
                i.Operand is Mono.Cecil.Cil.Instruction
                && i.OpCode.Name.EndsWith("_S"));
            // The measure must read bottom-up: text before metrics.
            var seq = adjust.Body.Instructions;
            int textAt = seq.IndexOf(seq.First(i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                && i.Operand is Mono.Cecil.MethodReference tmr
                && tmr.Name == "get_Text"));
            int metricsAt = seq.IndexOf(seq.First(i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference nmr
                && nmr.DeclaringType.FullName == "PaintDotNet.DirectWrite.TextMetrics"));
            Assert.True(textAt >= 0 && metricsAt > textAt);
            AssertCalleesResolve(asm.MainModule, adjust);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void Rewrite_Friends_PdnCpu_For_Headless_Drawing()
    {
        // The CPU brush backing implements PDN's internal drawing
        // surface; the rewrite friends it per healed assembly.
        foreach (var asmName in new[]
            { "PaintDotNet.Windows.Core", "PaintDotNet.ComponentModel", "PaintDotNet.Windows" })
        {
            string input = Path.Combine(Path.GetTempPath(), "ivt.input.dll");
            using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
            using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
            {
                asm.Name.Name = asmName;
                asm.Write(input);
            }

            string output = Path.Combine(Path.GetTempPath(), "ivt.rewritten.dll");
            AssemblyRewriter.Rewrite(input, output, ShimMap());

            using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
            Assert.Contains(rewritten.CustomAttributes, a =>
                a.AttributeType.FullName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute"
                && a.ConstructorArguments.Count == 1
                && (string)a.ConstructorArguments[0].Value == "WfWasm.PdnCpu");
        }
    }

    [Fact]
    public void StubPInvokes_Neutralizes_Kernel32_Thread_Primatives()
    {
        // Paint boot calls TerraFX GetCurrentThreadId/OpenThread from
        // PdnSynchronizationContext..ctor and ControlDispatcher..ctor. In
        // WASM kernel32 is absent (DllNotFoundException -> empty form), and
        // OpenThread must return a non-NULL handle (the caller throws on
        // NULL). The stubber must yield thread-id 1 and a nonzero handle.
        string input = Path.Combine(Path.GetTempPath(), "threadprim.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var modRef = new Mono.Cecil.ModuleReference("kernel32.dll");
            module.ModuleReferences.Add(modRef);
            var handle = new Mono.Cecil.TypeDefinition(
                "PinvokeProbe", "HANDLE",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Sealed,
                module.ImportReference(typeof(ValueType)));
            handle.Fields.Add(new Mono.Cecil.FieldDefinition(
                "Value",
                Mono.Cecil.FieldAttributes.Public,
                module.TypeSystem.IntPtr));
            module.Types.Add(handle);
            var native = new Mono.Cecil.TypeDefinition(
                "PinvokeProbe", "Native",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(native);
            var openThread = new Mono.Cecil.MethodDefinition(
                "OpenThread",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.PInvokeImpl,
                handle);
            openThread.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.UInt32));
            openThread.PInvokeInfo = new Mono.Cecil.PInvokeInfo(
                Mono.Cecil.PInvokeAttributes.CallConvWinapi, "OpenThread", modRef);
            native.Methods.Add(openThread);
            var threadId = new Mono.Cecil.MethodDefinition(
                "GetCurrentThreadId",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.PInvokeImpl,
                module.TypeSystem.UInt32);
            threadId.PInvokeInfo = new Mono.Cecil.PInvokeInfo(
                Mono.Cecil.PInvokeAttributes.CallConvWinapi, "GetCurrentThreadId", modRef);
            native.Methods.Add(threadId);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "threadprim.stubbed.dll");
        AssemblyRewriter.StubPInvokes(input, output);

        using var stubbed = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var methods = stubbed.MainModule.GetTypes()
            .First(t => t.FullName == "PinvokeProbe.Native").Methods;
        var open = methods.First(m => m.Name == "OpenThread");
        Assert.False(open.IsPInvokeImpl);
        Assert.Contains(open.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4 && (int)i.Operand == 0x1000);
        var tid = methods.First(m => m.Name == "GetCurrentThreadId");
        Assert.False(tid.IsPInvokeImpl);
        Assert.Contains(tid.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
    }

    [Fact]
    public void Rewrite_PdnResources_InstalledLanguages_Skips_Disk_Probe()
    {
        // No satellite language files ship in the browser bundle:
        // enumerating the resources directory throws and breaks
        // AppSettings creation during bootstrap. The rewrite must report
        // the built-in language without touching the disk.
        string input = Path.Combine(Path.GetTempPath(), "installedlang.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var listIfc = module.ImportReference(typeof(System.Collections.Generic.IReadOnlyList<>));
            var langs = new Mono.Cecil.GenericInstanceType(listIfc);
            langs.GenericArguments.Add(module.ImportReference(typeof(System.Globalization.CultureInfo)));
            var enumerate = module.ImportReference(
                typeof(System.IO.Directory).GetMethod("EnumerateFiles", [typeof(string), typeof(string)])!);
            var res = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Resources", "PdnResources",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(res);
            var get = new Mono.Cecil.MethodDefinition(
                "GetInstalledLanguagesImpl",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                langs);
            res.Methods.Add(get);
            var il = get.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, "dir");
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, "*.resources");
            il.Emit(Mono.Cecil.Cil.OpCodes.Call, enumerate);
            il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "installedlang.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Resources.PdnResources").Methods
            .First(m => m.Name == "GetInstalledLanguagesImpl");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "EnumerateFiles");
        Assert.Contains(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Newarr
            && ((Mono.Cecil.TypeReference)i.Operand).FullName == "System.Globalization.CultureInfo");
    }

    [Fact]
    public void Rewrite_ResourceCache_GetService_Neutralizes_Multi_Instr_Throw()
    {
        // The throw-neutralizer must remove the WHOLE exception-building
        // expression. The original assumed "arg + newobj + throw"; with
        // "ldarg + callvirt + newobj + throw" it orphaned the ldarg, leaving
        // two values for ret -> InvalidProgramException (and a WASM
        // interpreter abort) on the first GetService call.
        string input = Path.Combine(Path.GetTempPath(), "resourcecache.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var toString = module.ImportReference(typeof(object).GetMethod("ToString")!);
            var exCtor = module.ImportReference(typeof(Exception).GetConstructor([typeof(string)])!);
            var cache = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.ObjectModel", "ResourceCache",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(cache);
            var get = new Mono.Cecil.MethodDefinition(
                "GetService",
                Mono.Cecil.MethodAttributes.Public,
                module.TypeSystem.Object);
            get.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            cache.Methods.Add(get);
            var il = get.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
            il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, toString);
            il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, exCtor);
            il.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "resourcecache.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.ObjectModel.ResourceCache").Methods
            .First(m => m.Name == "GetService");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Ldnull, method.Body.Instructions[0].OpCode);
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, method.Body.Instructions[^1].OpCode);
        // Exactly one value left for ret: ldnull, nops, ret.
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_1);
    }

    [Fact]
    public void PatchRuntimeProbes_Disables_Wpf_Tracing()
    {
        // WPF tracing reads the Windows registry from
        // DependencyObject..cctor, which breaks AppWorkspace creation in
        // the browser. The patch must answer "tracing disabled".
        string input = Path.Combine(Path.GetTempPath(), "avtrace.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var trace = new Mono.Cecil.TypeDefinition(
                "MS.Internal", "AvTrace",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(trace);
            var probe = new Mono.Cecil.MethodDefinition(
                "ShouldCreateTraceSources",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Boolean);
            trace.Methods.Add(probe);
            var il = probe.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "avtrace.patched.dll");
        AssemblyRewriter.PatchRuntimeProbes(input, output);

        using var patched = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = patched.MainModule.GetTypes()
            .First(t => t.FullName == "MS.Internal.AvTrace").Methods
            .First(m => m.Name == "ShouldCreateTraceSources");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
        Assert.Contains(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
    }

    [Fact]
    public void PatchRuntimeProbes_Neuters_DWrite_Factory()
    {
        // PresentationCore's DWrite factory acquires native COM pointers
        // and calls through them (calli): null in WASM traps the engine
        // ("null function", uncatchable). Leaf lookups must answer null.
        string input = Path.Combine(Path.GetTempPath(), "dwfactory.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var factory = new Mono.Cecil.TypeDefinition(
                "MS.Internal.Text.TextInterface", "Factory",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(factory);
            var create = new Mono.Cecil.MethodDefinition(
                "CreateFontFile",
                Mono.Cecil.MethodAttributes.Public,
                module.TypeSystem.Object);
            create.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.String));
            factory.Methods.Add(create);
            var createIl = create.Body.GetILProcessor();
            createIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            createIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            var init = new Mono.Cecil.MethodDefinition(
                "Initialize",
                Mono.Cecil.MethodAttributes.Public,
                module.TypeSystem.Void);
            factory.Methods.Add(init);
            var initIl = init.Body.GetILProcessor();
            initIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            initIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "dwfactory.patched.dll");
        AssemblyRewriter.PatchRuntimeProbes(input, output);

        using var patched = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var methods = patched.MainModule.GetTypes()
            .First(t => t.FullName == "MS.Internal.Text.TextInterface.Factory").Methods;
        var createMethod = methods.First(m => m.Name == "CreateFontFile");
        Assert.DoesNotContain(createMethod.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
        Assert.Contains(createMethod.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldnull);
        var initMethod = methods.First(m => m.Name == "Initialize");
        Assert.DoesNotContain(initMethod.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
    }

    [Fact]
    public void Rewrite_ToIcon_Answers_Null()
    {
        // Both GdipImageExtensions.ToIcon overloads build the Icon through
        // a GDI HICON round-trip (GetHiconSafe -> FromHandleSafe) that
        // throws NotSupportedException in the browser and breaks dialogs
        // such as NewFileDialog. The dialog caller (TryCreateIcon) already
        // treats null as "no icon" and Form.set_Icon resets to default on
        // null, so the rewrite must reduce both overloads to ldnull/ret.
        // Note: GdipImageExtensions ships in the managed PaintDotNet
        // closure, which only takes the Rewrite (--app) pass, never the
        // framework-closure PatchRuntimeProbes pass.
        string input = Path.Combine(Path.GetTempPath(), "toicon.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var ext = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Drawing", "GdipImageExtensions",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(ext);
            var one = new Mono.Cecil.MethodDefinition(
                "ToIcon",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Object);
            one.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            ext.Methods.Add(one);
            var oneIl = one.Body.GetILProcessor();
            oneIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            oneIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            var two = new Mono.Cecil.MethodDefinition(
                "ToIcon",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Object);
            two.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            two.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Boolean));
            ext.Methods.Add(two);
            var twoIl = two.Body.GetILProcessor();
            twoIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            twoIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "toicon.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var methods = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Drawing.GdipImageExtensions").Methods
            .Where(m => m.Name == "ToIcon").ToList();
        Assert.Equal(2, methods.Count);
        foreach (var method in methods)
        {
            Assert.DoesNotContain(method.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
            Assert.Equal(
                [Mono.Cecil.Cil.OpCodes.Ldnull, Mono.Cecil.Cil.OpCodes.Ret],
                method.Body.Instructions.Select(i => i.OpCode).ToList());
        }
    }

    [Fact]
    public void InstrumentProbes_Inserts_Enter_Exit_Breadcrumbs()
    {
        // Diagnostic pass for instant engine deaths (no managed trace):
        // listed methods get ENTER/EXIT WriteLine markers; unlisted
        // methods are untouched.
        string input = Path.Combine(Path.GetTempPath(), "probe.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var t = new Mono.Cecil.TypeDefinition(
                "ProbeNs", "ProbeType",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(t);
            var hit = new Mono.Cecil.MethodDefinition(
                "Target",
                Mono.Cecil.MethodAttributes.Public,
                module.TypeSystem.Void);
            t.Methods.Add(hit);
            var hitIl = hit.Body.GetILProcessor();
            hitIl.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            var miss = new Mono.Cecil.MethodDefinition(
                "Decoy",
                Mono.Cecil.MethodAttributes.Public,
                module.TypeSystem.Void);
            t.Methods.Add(miss);
            var missIl = miss.Body.GetILProcessor();
            missIl.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "probe.probed.dll");
        int probed = AssemblyRewriter.InstrumentProbes(input, output, ["ProbeNs.ProbeType::Target"]);

        Assert.Equal(1, probed);
        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var type = rewritten.MainModule.GetTypes().First(t => t.FullName == "ProbeNs.ProbeType");
        var hitMethod = type.Methods.First(m => m.Name == "Target");
        var ops = hitMethod.Body.Instructions.Select(i => i.OpCode).ToList();
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Ldstr, ops[0]);
        Assert.Equal("ENTER [probe] ProbeNs.ProbeType::Target",
            (string)hitMethod.Body.Instructions[0].Operand);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldstr, Mono.Cecil.Cil.OpCodes.Call, Mono.Cecil.Cil.OpCodes.Ret],
            ops.TakeLast(3).ToList());
        var missMethod = type.Methods.First(m => m.Name == "Decoy");
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ret],
            missMethod.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    [Fact]
    public void Rewrite_TextFactory_CreateLayout_Defaults_Dpi_When_Context_Null()
    {
        // Measure-only text layout (e.g. TaskProgressDialog buttons during
        // a brush commit) passes a null drawing context: no D2D device
        // exists in the browser, and upstream derefs dc.get_Dpi()
        // unconditionally (NRE). The rewrite must answer scale 1.0
        // (96 dpi) on the null path while leaving the device path
        // untouched. The join must land AFTER the scale store (stack
        // provably empty): the old rule joined before the 5th instruction,
        // splitting ldloca/call and failing verification (InvalidProgram
        // under the WASM interpreter), and guessed local 0 for the dpi
        // (real code keeps it elsewhere). The fixture therefore keeps the
        // dpi in V_1, the scale in V_2, and a non-double decoy in V_0.
        string input = Path.Combine(Path.GetTempPath(), "textfactory.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var dcIfx = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Direct2D1", "IDrawingContext",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Interface | Mono.Cecil.TypeAttributes.Abstract,
                null);
            var getDpi = new Mono.Cecil.MethodDefinition(
                "get_Dpi",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Abstract | Mono.Cecil.MethodAttributes.Virtual,
                module.TypeSystem.Double);
            dcIfx.Methods.Add(getDpi);
            module.Types.Add(dcIfx);
            var factory = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.UI.Media", "UITextFactory",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(factory);
            var getXStub = new Mono.Cecil.MethodDefinition(
                "GetXStub",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Double);
            factory.Methods.Add(getXStub);
            var target = new Mono.Cecil.MethodDefinition(
                "CreateLayout",
                Mono.Cecil.MethodAttributes.Public,
                module.TypeSystem.Object);
            target.Parameters.Add(new Mono.Cecil.ParameterDefinition(dcIfx));
            for (int i = 0; i < 7; i++)
                target.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            var decoyLocal = new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Object);
            var dpiLocal = new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Double);
            var scaleLocal = new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Double);
            target.Body.Variables.Add(decoyLocal);
            target.Body.Variables.Add(dpiLocal);
            target.Body.Variables.Add(scaleLocal);
            factory.Methods.Add(target);
            var til = target.Body.GetILProcessor();
            til.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
            til.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, getDpi);
            til.Emit(Mono.Cecil.Cil.OpCodes.Stloc_1);
            til.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, dpiLocal);
            til.Emit(Mono.Cecil.Cil.OpCodes.Call, getXStub);
            til.Emit(Mono.Cecil.Cil.OpCodes.Conv_R8);
            til.Emit(Mono.Cecil.Cil.OpCodes.Ldc_R8, 96.0);
            til.Emit(Mono.Cecil.Cil.OpCodes.Div);
            til.Emit(Mono.Cecil.Cil.OpCodes.Stloc_2);
            til.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            til.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            var decoy = new Mono.Cecil.MethodDefinition(
                "CreateLayout",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Object);
            decoy.Parameters.Add(new Mono.Cecil.ParameterDefinition(dcIfx));
            for (int i = 0; i < 7; i++)
                decoy.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            factory.Methods.Add(decoy);
            var dil = decoy.Body.GetILProcessor();
            dil.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            dil.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "textfactory.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var type = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.UI.Media.UITextFactory");
        var method = type.Methods.First(m => m.Name == "CreateLayout" && m.HasThis);
        var insns = method.Body.Instructions;
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldarg_1, Mono.Cecil.Cil.OpCodes.Brtrue_S,
                Mono.Cecil.Cil.OpCodes.Ldc_R8, Mono.Cecil.Cil.OpCodes.Stloc_2,
                Mono.Cecil.Cil.OpCodes.Br_S, Mono.Cecil.Cil.OpCodes.Nop],
            insns.Take(6).Select(i => i.OpCode).ToList());
        Assert.Equal(1.0, insns[2].Operand);
        // Brtrue lands on the nop guarding the original device path, and
        // the whole original 9-instruction prologue sits between that nop
        // and the join: nothing mid-expression is skipped on either path.
        var hasDc = (Mono.Cecil.Cil.Instruction)insns[1].Operand;
        Assert.Same(insns[5], hasDc);
        var join = (Mono.Cecil.Cil.Instruction)insns[4].Operand;
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Nop, join.OpCode);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldarg_1, Mono.Cecil.Cil.OpCodes.Callvirt,
                Mono.Cecil.Cil.OpCodes.Stloc_1, Mono.Cecil.Cil.OpCodes.Ldloca_S,
                Mono.Cecil.Cil.OpCodes.Call, Mono.Cecil.Cil.OpCodes.Conv_R8,
                Mono.Cecil.Cil.OpCodes.Ldc_R8, Mono.Cecil.Cil.OpCodes.Div,
                Mono.Cecil.Cil.OpCodes.Stloc_2],
            insns.Skip(6).Take(9).Select(i => i.OpCode).ToList());
        Assert.Same(join, insns[15]);
        // The join sits right after the div-path scale store (stack empty),
        // and the null path stores the same scale slot — never the V_0 decoy.
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Stloc_2, insns[14].OpCode);
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Ldnull, insns[16].OpCode);
        var staticDecoy = type.Methods.First(m => m.Name == "CreateLayout" && !m.HasThis);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldnull, Mono.Cecil.Cil.OpCodes.Ret],
            staticDecoy.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    [Fact]
    public void Rewrite_Clipboard_OpenAndUseWithRetry_Answers_Default()
    {
        // The clipboard probe (New dialog image-size default, paste)
        // opens the native clipboard and reads WIC streams: absent in
        // the browser (NRE in MaybeGetWicData, breaking NewImageAction
        // before the New dialog opens). With no synchronous clipboard in
        // WASM, "clipboard empty" (default(T)) is the honest answer, so
        // the rewrite must reduce the generic probe to initobj/ret.
        string input = Path.Combine(Path.GetTempPath(), "clipboard.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var ext = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Clipboard", "ClipboardExtensions",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(ext);
            var probe = new Mono.Cecil.MethodDefinition(
                "OpenAndUseWithRetry",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Object);
            var t = new Mono.Cecil.GenericParameter("T", probe);
            probe.GenericParameters.Add(t);
            probe.ReturnType = t;
            ext.Methods.Add(probe);
            var probeIl = probe.Body.GetILProcessor();
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "clipboard.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Clipboard.ClipboardExtensions").Methods
            .First(m => m.Name == "OpenAndUseWithRetry");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldloca_S, Mono.Cecil.Cil.OpCodes.Initobj,
                Mono.Cecil.Cil.OpCodes.Ldloc_0, Mono.Cecil.Cil.OpCodes.Ret],
            method.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    [Fact]
    public void Rewrite_DeviceContext_GetDefaultTextLayoutAlgorithm_Answers_DirectWrite()
    {
        // Measure-only text layout passes a null drawing context, and
        // GetDefaultTextLayoutAlgorithm derefs dc.get_TextAntialiasMode
        // (NRE), breaking brush commits during TaskProgressDialog
        // measurement. With no device there are no rendering parameters;
        // the method's own null-params fallback is DirectWrite (0), which
        // is also what the body computes against our inert stub context,
        // so the rewrite must reduce it to a constant 0.
        string input = Path.Combine(Path.GetTempPath(), "textalgorithm.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var ext = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Direct2D1", "DeviceContextExtensions",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(ext);
            var algorithm = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.DirectWrite", "TextLayoutAlgorithm",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(algorithm);
            var probe = new Mono.Cecil.MethodDefinition(
                "GetDefaultTextLayoutAlgorithm",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                algorithm);
            probe.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            ext.Methods.Add(probe);
            var probeIl = probe.Body.GetILProcessor();
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "textalgorithm.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Direct2D1.DeviceContextExtensions").Methods
            .First(m => m.Name == "GetDefaultTextLayoutAlgorithm");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldc_I4_0, Mono.Cecil.Cil.OpCodes.Ret],
            method.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    // Builds a fixture module mirroring the task machinery the
    // headless driver rule needs: PdnTaskManagerExtensions with the
    // Action-overload Create/Start pair, a nested FuncThreadTask`1,
    // PdnTask(SetState), PdnTask`1(set_TaskResult), and the Func
    // helpers (Bind/ToFunc/Eval) plus Result`1/Unit stand-ins.
    private static void BuildTaskDriverFixture(Mono.Cecil.ModuleDefinition module)
    {
        var aptState = module.ImportReference(typeof(System.Threading.ApartmentState));
        var actionRef = module.ImportReference(typeof(Action<>));
        var funcRef = module.ImportReference(typeof(Func<>));
        var pdnTask = new Mono.Cecil.TypeDefinition(
            "PaintDotNet.Threading.Tasks", "PdnTask",
            Mono.Cecil.TypeAttributes.Public,
            module.TypeSystem.Object);
        var setState = new Mono.Cecil.MethodDefinition(
            "SetState",
            Mono.Cecil.MethodAttributes.Public,
            module.TypeSystem.Void);
        setState.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Int32));
        setState.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
        pdnTask.Methods.Add(setState);
        module.Types.Add(pdnTask);
        var actionPdnTask = new Mono.Cecil.GenericInstanceType(actionRef);
        actionPdnTask.GenericArguments.Add(pdnTask);
        var pdnTaskGeneric = new Mono.Cecil.TypeDefinition(
            "PaintDotNet.Threading.Tasks", "PdnTask`1",
            Mono.Cecil.TypeAttributes.Public,
            pdnTask);
        pdnTaskGeneric.GenericParameters.Add(new Mono.Cecil.GenericParameter("T", pdnTaskGeneric));
        var unit = new Mono.Cecil.TypeDefinition(
            "PaintDotNet.Functional", "Unit",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Sealed,
            module.ImportReference(typeof(ValueType)));
        module.Types.Add(unit);
        var resultGeneric = new Mono.Cecil.TypeDefinition(
            "PaintDotNet.Functional", "Result`1",
            Mono.Cecil.TypeAttributes.Public,
            module.TypeSystem.Object);
        resultGeneric.GenericParameters.Add(new Mono.Cecil.GenericParameter("T", resultGeneric));
        module.Types.Add(resultGeneric);
        var resultUnit = new Mono.Cecil.GenericInstanceType(resultGeneric);
        resultUnit.GenericArguments.Add(unit);
        var setResult = new Mono.Cecil.MethodDefinition(
            "set_TaskResult",
            Mono.Cecil.MethodAttributes.Public,
            module.TypeSystem.Void);
        setResult.Parameters.Add(new Mono.Cecil.ParameterDefinition(resultUnit));
        setResult.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
        pdnTaskGeneric.Methods.Add(setResult);
        module.Types.Add(pdnTaskGeneric);
        var func = new Mono.Cecil.TypeDefinition(
            "PaintDotNet.Functional", "Func",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract
                | Mono.Cecil.TypeAttributes.Sealed,
            module.TypeSystem.Object);
        var bind = new Mono.Cecil.MethodDefinition(
            "Bind",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            module.ImportReference(typeof(Action)));
        bind.GenericParameters.Add(new Mono.Cecil.GenericParameter("T1", bind));
        var bindT1 = bind.GenericParameters[0];
        var bindAction = new Mono.Cecil.GenericInstanceType(actionRef);
        bindAction.GenericArguments.Add(bindT1);
        bind.Parameters.Add(new Mono.Cecil.ParameterDefinition(bindAction));
        bind.Parameters.Add(new Mono.Cecil.ParameterDefinition(bindT1));
        // No-op like the other helpers: the execution test proves
        // DriveInline runs end to end; the opcode test proves the
        // action flows into Bind. (A callvirt Invoke on the open
        // Action<T1> here fails even on desktop CoreCLR, so the
        // fixture deliberately does not invoke.)
        var bindIl = bind.Body.GetILProcessor();
        bindIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
        bindIl.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        func.Methods.Add(bind);
        var toFunc = new Mono.Cecil.MethodDefinition(
            "ToFunc",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            InlineTaskDriverFixtureGeneric(funcRef, unit));
        toFunc.Parameters.Add(new Mono.Cecil.ParameterDefinition(
            module.ImportReference(typeof(Action))));
        var toFuncIl = toFunc.Body.GetILProcessor();
        toFuncIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
        toFuncIl.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        func.Methods.Add(toFunc);
        // Return type mentions the method's own TRet, exactly like
        // the real API (a concrete return here would not match the
        // rule's reference and fails lookup).
        var eval = new Mono.Cecil.MethodDefinition(
            "Eval",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            module.TypeSystem.Void);
        eval.GenericParameters.Add(new Mono.Cecil.GenericParameter("TRet", eval));
        var evalT = eval.GenericParameters[0];
        eval.ReturnType = InlineTaskDriverFixtureGeneric(resultGeneric, evalT);
        eval.Parameters.Add(new Mono.Cecil.ParameterDefinition(
            InlineTaskDriverFixtureGeneric(funcRef, evalT)));
        var evalIl = eval.Body.GetILProcessor();
        evalIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
        evalIl.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        func.Methods.Add(eval);
        module.Types.Add(func);
        var manager = new Mono.Cecil.TypeDefinition(
            "PaintDotNet.Threading.Tasks", "PdnTaskManager",
            Mono.Cecil.TypeAttributes.Public,
            module.TypeSystem.Object);
        module.Types.Add(manager);
        var ext = new Mono.Cecil.TypeDefinition(
            "PaintDotNet.Threading.Tasks", "PdnTaskManagerExtensions",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract
                | Mono.Cecil.TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(ext);
        // ThreadTask`1 must stay derivable (it is in reality);
        // FuncThreadTask`1 is sealed in reality: the driver must not
        // derive from it (sealed base fails type load).
        var threadTask = new Mono.Cecil.TypeDefinition(
            "PaintDotNet.Threading.Tasks", "ThreadTask`1",
            Mono.Cecil.TypeAttributes.Public,
            module.TypeSystem.Object);
        threadTask.GenericParameters.Add(new Mono.Cecil.GenericParameter("T", threadTask));
        var threadTaskPdnT = new Mono.Cecil.GenericInstanceType(pdnTaskGeneric);
        threadTaskPdnT.GenericArguments.Add(threadTask.GenericParameters[0]);
        threadTask.BaseType = threadTaskPdnT;
        var threadTaskCtor = new Mono.Cecil.MethodDefinition(
            ".ctor",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig
                | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        threadTaskCtor.Parameters.Add(new Mono.Cecil.ParameterDefinition(manager));
        threadTaskCtor.Parameters.Add(new Mono.Cecil.ParameterDefinition(aptState));
        threadTaskCtor.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
        threadTask.Methods.Add(threadTaskCtor);
        var threadStart = new Mono.Cecil.MethodDefinition(
            "Start",
            Mono.Cecil.MethodAttributes.Public,
            module.TypeSystem.Void);
        threadTask.Methods.Add(threadStart);
        module.Types.Add(threadTask);
        var funcThreadTask = new Mono.Cecil.TypeDefinition(
            "", "FuncThreadTask`1",
            Mono.Cecil.TypeAttributes.NestedPublic | Mono.Cecil.TypeAttributes.Sealed,
            module.TypeSystem.Object);
        funcThreadTask.GenericParameters.Add(new Mono.Cecil.GenericParameter("T", funcThreadTask));
        var funcThreadTaskT = funcThreadTask.GenericParameters[0];
        var pdnTaskOfT = new Mono.Cecil.GenericInstanceType(pdnTaskGeneric);
        pdnTaskOfT.GenericArguments.Add(funcThreadTaskT);
        var func2Ref = module.ImportReference(typeof(Func<,>));
        var funcParam = new Mono.Cecil.GenericInstanceType(func2Ref);
        funcParam.GenericArguments.Add(pdnTaskOfT);
        funcParam.GenericArguments.Add(funcThreadTaskT);
        var baseCtor = new Mono.Cecil.MethodDefinition(
            ".ctor",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig
                | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        baseCtor.Parameters.Add(new Mono.Cecil.ParameterDefinition(manager));
        baseCtor.Parameters.Add(new Mono.Cecil.ParameterDefinition(funcParam));
        baseCtor.Parameters.Add(new Mono.Cecil.ParameterDefinition(aptState));
        funcThreadTask.Methods.Add(baseCtor);
        ext.NestedTypes.Add(funcThreadTask);
        var threadTaskUnit = new Mono.Cecil.GenericInstanceType(funcThreadTask);
        threadTaskUnit.GenericArguments.Add(unit);
        // Func-overload factory (generic: the rule must leave it
        // alone) constructs the bare task directly.
        var createFunc = new Mono.Cecil.MethodDefinition(
            "CreateThreadTask",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            threadTaskUnit);
        createFunc.GenericParameters.Add(new Mono.Cecil.GenericParameter("T", createFunc));
        var createFuncT = createFunc.GenericParameters[0];
        var pdnTaskOfCreateT = InlineTaskDriverFixtureGeneric(pdnTaskGeneric, createFuncT);
        var funcCreateParam = InlineTaskDriverFixtureGeneric(func2Ref, pdnTaskOfCreateT, createFuncT);
        createFunc.Parameters.Add(new Mono.Cecil.ParameterDefinition(manager));
        createFunc.Parameters.Add(new Mono.Cecil.ParameterDefinition(funcCreateParam));
        createFunc.Parameters.Add(new Mono.Cecil.ParameterDefinition(aptState));
        var createFuncIl = createFunc.Body.GetILProcessor();
        createFuncIl.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        createFuncIl.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
        createFuncIl.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
        createFuncIl.Emit(Mono.Cecil.Cil.OpCodes.Newobj, baseCtor);
        createFuncIl.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        ext.Methods.Add(createFunc);
        // Action-overload factory wraps and tail-calls it, like the
        // real one (display class omitted: irrelevant to the rule).
        var create = new Mono.Cecil.MethodDefinition(
            "CreateThreadTask",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            threadTaskUnit);
        create.Parameters.Add(new Mono.Cecil.ParameterDefinition(manager));
        create.Parameters.Add(new Mono.Cecil.ParameterDefinition(actionPdnTask));
        create.Parameters.Add(new Mono.Cecil.ParameterDefinition(aptState));
        var createIl = create.Body.GetILProcessor();
        createIl.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        createIl.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
        createIl.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
        createIl.Emit(Mono.Cecil.Cil.OpCodes.Call, createFunc);
        createIl.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        ext.Methods.Add(create);
        var startNew = new Mono.Cecil.MethodDefinition(
            "StartNewThreadTask",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
            threadTaskUnit);
        startNew.Parameters.Add(new Mono.Cecil.ParameterDefinition(manager));
        startNew.Parameters.Add(new Mono.Cecil.ParameterDefinition(actionPdnTask));
        startNew.Parameters.Add(new Mono.Cecil.ParameterDefinition(aptState));
        var startIl = startNew.Body.GetILProcessor();
        startIl.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        startIl.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
        startIl.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
        startIl.Emit(Mono.Cecil.Cil.OpCodes.Call, create);
        startIl.Emit(Mono.Cecil.Cil.OpCodes.Dup);
        var start = new Mono.Cecil.MethodDefinition(
            "Start",
            Mono.Cecil.MethodAttributes.Public,
            module.TypeSystem.Void);
        funcThreadTask.Methods.Add(start);
        startIl.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, start);
        startIl.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        ext.Methods.Add(startNew);
    }

    private static Mono.Cecil.GenericInstanceType InlineTaskDriverFixtureGeneric(
        Mono.Cecil.TypeReference element, params Mono.Cecil.TypeReference[] args)
    {
        var instance = new Mono.Cecil.GenericInstanceType(element);
        foreach (var arg in args)
            instance.GenericArguments.Add(arg);
        return instance;
    }

    private static Mono.Cecil.AssemblyDefinition RewriteTaskDriverFixture(string tag)
    {
        string input = Path.Combine(Path.GetTempPath(), tag + ".input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            BuildTaskDriverFixture(asm.MainModule);
            // Unique identity per tag: loading one rewritten fixture
            // must not satisfy (or poison) another test's LoadFrom.
            asm.Name.Name = "WfWasm.Rewrite.Fixture." + tag;
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), tag + ".rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());
        return Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
    }

    [Fact]
    public void Rewrite_CreateThreadTask_Constructs_Inline_Driver()
    {
        // The Action-overload factory must construct the headless
        // driver, not a bare FuncThreadTask: same signature, so the
        // rest of the pipeline (and StartNewThreadTask) is untouched.
        using var rewritten = RewriteTaskDriverFixture("createthreadtask");
        var module = rewritten.MainModule;
        var factories = module.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Threading.Tasks.PdnTaskManagerExtensions").Methods
            .Where(m => m.Name == "CreateThreadTask").ToList();
        var create = factories.First(m => !m.HasGenericParameters);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldarg_0, Mono.Cecil.Cil.OpCodes.Ldarg_2,
                Mono.Cecil.Cil.OpCodes.Newobj, Mono.Cecil.Cil.OpCodes.Ret],
            create.Body.Instructions.Select(i => i.OpCode).ToList());
        var ctorCall = create.Body.Instructions
            .First(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj);
        Assert.Contains("InlineFuncThreadTask",
            ((Mono.Cecil.MethodReference)ctorCall.Operand).DeclaringType.FullName);
        // The generic Func-overload factory keeps constructing the
        // bare task: its open-T shape is out of scope.
        var createFunc = factories.First(m => m.HasGenericParameters);
        var bareCall = createFunc.Body.Instructions
            .First(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj);
        Assert.Contains("FuncThreadTask`1",
            ((Mono.Cecil.MethodReference)bareCall.Operand).DeclaringType.FullName);
        Assert.DoesNotContain("InlineFuncThreadTask",
            ((Mono.Cecil.MethodReference)bareCall.Operand).DeclaringType.FullName);
    }

    [Fact]
    public void Rewrite_StartNewThreadTask_Drives_Inline_Without_Start()
    {
        // The stillborn Thread.Start must go: the created task is
        // driven inline with the caller's action instead.
        using var rewritten = RewriteTaskDriverFixture("startnewthreadtask");
        var module = rewritten.MainModule;
        var startNew = module.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Threading.Tasks.PdnTaskManagerExtensions").Methods
            .First(m => m.Name == "StartNewThreadTask");
        Assert.DoesNotContain(startNew.Body.Instructions, i =>
            i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "Start");
        var drive = startNew.Body.Instructions.First(i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Call
            && i.Operand is Mono.Cecil.MethodReference mr
            && mr.Name == "DriveInline");
        Assert.Contains("InlineFuncThreadTask",
            ((Mono.Cecil.MethodReference)drive.Operand).DeclaringType.FullName);
    }

    [Fact]
    public void Rewrite_Inline_Driver_Runs_Action_Through_Func_Eval()
    {
        // The synthesized driver marks running, runs the action via
        // Bind/ToFunc/Eval (Eval captures errors into the result like
        // the worker would), records TaskResult, marks finished: every
        // token closed, no open-generic calls for the interpreter.
        using var rewritten = RewriteTaskDriverFixture("inlinedriver");
        var module = rewritten.MainModule;
        var driver = module.GetTypes()
            .First(t => t.FullName.Contains("InlineFuncThreadTask"));
        Assert.Contains("ThreadTask`1",
            driver.BaseType.FullName);
        Assert.DoesNotContain("FuncThreadTask",
            driver.BaseType.FullName);
        var drive = driver.Methods.First(m => m.Name == "DriveInline");
        Assert.True(drive.IsPublic);
        string[] calls = drive.Body.Instructions
            .Where(i => i.Operand is Mono.Cecil.MethodReference)
            .Select(i => ((Mono.Cecil.MethodReference)i.Operand).Name)
            .ToArray();
        Assert.Equal(
            ["SetState", "Bind", "ToFunc", "Eval", "set_TaskResult", "SetState"],
            calls);
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Stfld,
            drive.Body.Instructions[2].OpCode);
        // The abstract OnExecute slot must be implemented for the
        // type to load; it replays the stashed action.
        var onExecute = driver.Methods.First(m => m.Name == "OnExecute");
        Assert.True(onExecute.IsVirtual);
        Assert.True(onExecute.IsFamily);
        Assert.False(onExecute.IsAbstract);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldarg_0, Mono.Cecil.Cil.OpCodes.Ldarg_0,
                Mono.Cecil.Cil.OpCodes.Ldfld, Mono.Cecil.Cil.OpCodes.Call,
                Mono.Cecil.Cil.OpCodes.Ldloca_S, Mono.Cecil.Cil.OpCodes.Initobj,
                Mono.Cecil.Cil.OpCodes.Ldloc_S, Mono.Cecil.Cil.OpCodes.Ret],
            onExecute.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    private static void DriveProbeAction<T>(T t) { }

    [Fact]
    public void Rewrite_Inline_Driver_Loads_And_Runs()
    {
        // End-to-end at the CLR level: the synthesized driver type
        // must load (a sealed base fails type load here, not just on
        // WASM), instantiate, and run DriveInline to completion with
        // no-op helpers. Wiring (action into Bind, Eval into
        // set_TaskResult) is asserted structurally above.
        RewriteTaskDriverFixture("driverload").Dispose();
        string path = Path.Combine(Path.GetTempPath(), "driverload.rewritten.dll");
        System.Reflection.Assembly loaded = System.Reflection.Assembly.LoadFrom(path);
        Type? driver = loaded.GetType("PaintDotNet.Threading.Tasks.InlineFuncThreadTask");
        Assert.NotNull(driver);
        Type? pdnTask = loaded.GetType("PaintDotNet.Threading.Tasks.PdnTask");
        Assert.NotNull(pdnTask);
        object? instance = Activator.CreateInstance(
            driver, new object?[] { null, System.Threading.ApartmentState.MTA });
        Assert.NotNull(instance);
        Type actionType = typeof(Action<>).MakeGenericType(pdnTask);
        System.Reflection.MethodInfo shim =
            typeof(RewriteTests).GetMethod(nameof(DriveProbeAction),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(pdnTask);
        Delegate action = shim.CreateDelegate(actionType);
        Exception? thrown = Record.Exception(() =>
            driver.GetMethod("DriveInline")!.Invoke(instance, new object?[] { action }));
        Assert.Null(thrown);
    }

    [Fact]
    public void Rewrite_TaskProgressDialog_Gains_No_ShowDialog_Methods()
    {
        // Form.ShowDialog is non-virtual in this WinForms version, so a
        // reuse-slot "override" on TaskProgressDialog would fail type load
        // and a shadowing method would never intercept the callvirt
        // Form::ShowDialog call sites: the rewriter must not synthesize
        // any. The ThreadTask.Start inline rule is the real fix (the task
        // finishes before OnExecute's first WaitOne, so the dialog is
        // never shown).
        string input = Path.Combine(Path.GetTempPath(), "showdialog.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var dialog = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Dialogs", "TaskProgressDialog",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(dialog);
            var probe = new Mono.Cecil.MethodDefinition(
                "Sync",
                Mono.Cecil.MethodAttributes.Public,
                module.TypeSystem.Void);
            dialog.Methods.Add(probe);
            var probeIl = probe.Body.GetILProcessor();
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "showdialog.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var type = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Dialogs.TaskProgressDialog");
        Assert.DoesNotContain(type.Methods, m => m.Name == "ShowDialog");
    }

    [Fact]
    public void Rewrite_TaskProgressDialog_Sync_Is_Noop()
    {
        // Progress sync drives the Windows taskbar (TaskbarList COM
        // activation → NRE in WASM) and the dialog progress bar: pure UI
        // feedback, so skipping it cannot affect the commit itself.
        string input = Path.Combine(Path.GetTempPath(), "tasksync.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var dialog = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Dialogs", "TaskProgressDialog",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(dialog);
            var probe = new Mono.Cecil.MethodDefinition(
                "Sync",
                Mono.Cecil.MethodAttributes.Public,
                module.TypeSystem.Void);
            dialog.Methods.Add(probe);
            var probeIl = probe.Body.GetILProcessor();
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "tasksync.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Dialogs.TaskProgressDialog").Methods
            .First(m => m.Name == "Sync");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ret],
            method.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    [Fact]
    public void Rewrite_PdnBaseForm_RefreshDisplayInformation_Is_Noop()
    {
        // Per-monitor display tracking (DPI awareness) needs the WinRT SDK
        // projection (FileNotFound in WASM), breaking TaskProgressDialog
        // construction during brush commits. Single fixed canvas: no
        // monitors to track, so the refresh is a no-op.
        string input = Path.Combine(Path.GetTempPath(), "displayinfo.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var form = new Mono.Cecil.TypeDefinition(
                "PaintDotNet", "PdnBaseForm",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(form);
            var probe = new Mono.Cecil.MethodDefinition(
                "RefreshDisplayInformation",
                Mono.Cecil.MethodAttributes.Public,
                module.TypeSystem.Void);
            form.Methods.Add(probe);
            var probeIl = probe.Body.GetILProcessor();
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "displayinfo.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.PdnBaseForm").Methods
            .First(m => m.Name == "RefreshDisplayInformation");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ret],
            method.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    [Fact]
    public void Rewrite_Clipboard_TryGetBitmapFromHandle_Answers_Null()
    {
        // Paste probing for a bitmap handle ends in GDI-handle interop
        // (NotSupportedException from the fork). "Try" + null is the
        // honest absent answer, like the sibling TryGetNativeData rule.
        string input = Path.Combine(Path.GetTempPath(), "clipboard.bitmap.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var util = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Clipboard", "ClipboardTransactionUtil",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(util);
            var bitmap = new Mono.Cecil.TypeDefinition(
                "System.Drawing", "Bitmap",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(bitmap);
            var probe = new Mono.Cecil.MethodDefinition(
                "TryGetBitmapFromHandle",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                bitmap);
            probe.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            util.Methods.Add(probe);
            var probeIl = probe.Body.GetILProcessor();
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "clipboard.bitmap.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Clipboard.ClipboardTransactionUtil").Methods
            .First(m => m.Name == "TryGetBitmapFromHandle");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldnull, Mono.Cecil.Cil.OpCodes.Ret],
            method.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    [Fact]
    public void Rewrite_PdnPushButton_MeasureAndDraw_Answers_Default_Size()
    {
        // Button measure+paint during TaskProgressDialog layout (brush
        // commits) dies in the measure callback: null-dc resource-cache
        // lookup, then a COM-only ITextLayout no rule can answer. With no
        // D2D device there is nothing to measure against, so the rewrite
        // answers the default size and dialog construction completes (the
        // commit then proceeds to the dialog-show wall, tracked
        // separately). Every path through here currently throws.
        string input = Path.Combine(Path.GetTempPath(), "pushbutton.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var button = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Controls", "PdnPushButton",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(button);
            // (Sealed-class stand-in: only the FullName matters to the
            // rule, and the fixture body is never executed.)
            var size = new Mono.Cecil.TypeDefinition(
                "System.Drawing", "Size",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(size);
            var probe = new Mono.Cecil.MethodDefinition(
                "MeasureAndDraw",
                Mono.Cecil.MethodAttributes.Public,
                size);
            for (int i = 0; i < 5; i++)
                probe.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            button.Methods.Add(probe);
            var probeIl = probe.Body.GetILProcessor();
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "pushbutton.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Controls.PdnPushButton").Methods
            .First(m => m.Name == "MeasureAndDraw");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldloca_S, Mono.Cecil.Cil.OpCodes.Initobj,
                Mono.Cecil.Cil.OpCodes.Ldloc_0, Mono.Cecil.Cil.OpCodes.Ret],
            method.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    [Fact]
    public void Rewrite_Clipboard_TryGetNativeData_Answers_Absent()
    {
        // Paste reaches ClipboardTransaction.TryGetNativeData directly,
        // bypassing the neutered generic OpenAndUseWithRetry<T> core: the
        // MaybeGetWicData read callback NREs on the bogus stream and the
        // throw escapes through the Action-overload retry wrapper (two
        // post-fix recurrences in the backend index). With no native
        // clipboard in WASM, "format absent" (false) is the honest answer,
        // so the MaybeGet* wrappers take their empty path.
        string input = Path.Combine(Path.GetTempPath(), "clipboard.native.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var tx = new Mono.Cecil.TypeDefinition(
                "PaintDotNet.Clipboard", "ClipboardTransaction",
                Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            module.Types.Add(tx);
            var probe = new Mono.Cecil.MethodDefinition(
                "TryGetNativeData",
                Mono.Cecil.MethodAttributes.Public,
                module.TypeSystem.Boolean);
            probe.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.UInt32));
            probe.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
            tx.Methods.Add(probe);
            var probeIl = probe.Body.GetILProcessor();
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            probeIl.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "clipboard.native.rewritten.dll");
        AssemblyRewriter.Rewrite(input, output, ShimMap());

        using var rewritten = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = rewritten.MainModule.GetTypes()
            .First(t => t.FullName == "PaintDotNet.Clipboard.ClipboardTransaction").Methods
            .First(m => m.Name == "TryGetNativeData");
        Assert.DoesNotContain(method.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
        Assert.Equal(
            [Mono.Cecil.Cil.OpCodes.Ldc_I4_0, Mono.Cecil.Cil.OpCodes.Ret],
            method.Body.Instructions.Select(i => i.OpCode).ToList());
    }

    [Fact]
    public void PatchRuntimeProbes_Neutralizes_Module_Initializer()
    {
        // PresentationCore's module init eagerly loads the native
        // DirectWrite stack, which cannot exist in the browser and breaks
        // the first WPF type touch (Binding..cctor during document init).
        // The patch must reduce Initialize to a no-op return.
        string input = Path.Combine(Path.GetTempPath(), "modinit.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var init = new Mono.Cecil.TypeDefinition(
                "", "ModuleInitializer",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(init);
            var initialize = new Mono.Cecil.MethodDefinition(
                "Initialize",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Void);
            init.Methods.Add(initialize);
            var il = initialize.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
            il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "modinit.patched.dll");
        AssemblyRewriter.PatchRuntimeProbes(input, output);

        using var patched = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var method = patched.MainModule.GetTypes()
            .First(t => t.FullName == "ModuleInitializer").Methods
            .First(m => m.Name == "Initialize");
        Assert.Single(method.Body.Instructions);
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, method.Body.Instructions[0].OpCode);
    }

    [Fact]
    public void GapAnalyzer_Against_Real_Framework_Has_No_False_Positives()
    {
        // Every member the fixture uses exists in desktop WinForms/Drawing,
        // so the analyzer must report (near) zero gaps there: validates the
        // mechanics before trusting its verdict on our shim.
        string desktop = @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App";
        string? winDir = Directory.Exists(desktop)
            ? Directory.GetDirectories(desktop).OrderByDescending(d => d).FirstOrDefault()
            : null;
        if (winDir is null)
            Assert.Fail("WindowsDesktop shared framework not found.");
        string coreDir = Path.Combine(
            @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App",
            Path.GetFileName(winDir));
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Windows.Forms"] = Path.Combine(winDir, "System.Windows.Forms.dll"),
            ["System.Drawing"] = Path.Combine(coreDir, "System.Drawing.dll"),
            ["System.Drawing.Common"] = Path.Combine(winDir, "System.Drawing.Common.dll"),
            ["System.Drawing.Primitives"] = Path.Combine(coreDir, "System.Drawing.Primitives.dll"),
        };
        foreach (var kv in map)
            Assert.True(File.Exists(kv.Value), $"missing {kv.Value}");

        List<Gap> gaps = GapAnalyzer.Analyze(FixtureDll(), map);
        Assert.True(gaps.Count == 0,
            "false positives:\n" + string.Join("\n", gaps.Take(20).Select(g => g.ToString())));
    }

    [Fact]
    public void GapAnalyzer_With_TypeMap_Reports_No_Gaps_For_Bootable_Fixture()
    {
        // The fixture boots (see Rewritten_Fixture_Boots_Against_Shim), so
        // the post-remap queue must be empty: every member the rewritten IL
        // touches resolves, including inherited ones (Button::set_Width via
        // Control) and the nested collection (Control+ControlCollection).
        List<Gap> gaps = GapAnalyzer.Analyze(FixtureDll(), ShimMap(), BootTypeMap());
        Assert.Empty(gaps);
    }

    [Fact]
    public void AutoMap_Drafts_Fixture_Map_By_Name()
    {
        var map = GapAnalyzer.AutoMap(FixtureDll(), ShimMap());
        Assert.Equal(("WfWasm.Shim", "WfWasm.Shim.Form"), map["System.Windows.Forms.Form"]);
        Assert.Equal(("WfWasm.Shim", "WfWasm.Shim.Button"), map["System.Windows.Forms.Button"]);
        Assert.Equal(("WfWasm.Shim", "WfWasm.Shim.TextBox"), map["System.Windows.Forms.TextBox"]);
        Assert.Equal(("WfWasm.Shim", "WfWasm.Shim.Control+ControlCollection"),
            map["System.Windows.Forms.Control+ControlCollection"]);
        Assert.Equal(("WfWasm.Drawing", "System.Drawing.Size"), map["System.Drawing.Size"]);
        // Facade-scoped types need explicit entries (production handmap).
        map["System.Threading.ThreadExceptionEventHandler"] = ("WfWasm.Shim", "System.Threading.ThreadExceptionEventHandler");
        map["System.Threading.ThreadExceptionEventArgs"] = ("WfWasm.Shim", "System.Threading.ThreadExceptionEventArgs");
        // The draft plus facade entries suffices for the fixture: analysis is clean.
        Assert.Empty(GapAnalyzer.Analyze(FixtureDll(), ShimMap(), map));
    }

    [Fact]
    public void GapAnalyzer_Flags_Forked_Signatures_Not_Just_Arity()
    {
        // A name-and-arity match with forked parameter types binds nothing
        // at runtime (MissingMethod): Paint.NET died exactly this way on
        // add_ThreadException, whose delegate arrives under the
        // System.Threading.Thread facade scope and is mapped by explicit
        // handmap entries. With those entries dropped (a forgotten handmap
        // line), the queue must report "signature" for the subscription,
        // which the old arity-only check could never emit.
        var types = new Dictionary<string, (string Assembly, string Type)>(BootTypeMap());
        types.Remove("System.Threading.ThreadExceptionEventHandler");
        types.Remove("System.Threading.ThreadExceptionEventArgs");
        List<Gap> gaps = GapAnalyzer.Analyze(FixtureDll(), ShimMap(), types);
        Assert.Contains(gaps, g => g.Kind == "signature" && g.Member == "add_ThreadException");
    }

    [Fact]
    public void GapAnalyzer_Against_Shim_Lists_Concrete_Work_Items()
    {
        List<Gap> gaps = GapAnalyzer.Analyze(FixtureDll(), ShimMap());
        Assert.NotEmpty(gaps);
        // Type names differ on purpose (Form vs Form): the analyzer
        // must say so plainly rather than silently pass.
        Assert.Contains(gaps, g => g.Kind == "type" && g.DeclaringType == "System.Windows.Forms.Form");
        foreach (Gap gap in gaps)
            Assert.StartsWith("System.", gap.Scope);
    }

    [Fact]
    public void StubPInvokes_Implements_Real_Memory_Allocation()
    {
        string input = TestPaths.Repo(@"targets\paint.net\TerraFX.Interop.Windows.dll");
        if (!File.Exists(input)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"TerraFX_Test_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.StubPInvokes(input, tempOut);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var module = asm.MainModule;

            var heapAlloc = module.Types.SelectMany(t => t.Methods).First(m => m.Name == "HeapAlloc");
            Assert.Contains(heapAlloc.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call && i.Operand.ToString()!.Contains("NativeMemory::AllocZeroed"));

            var virtAlloc = module.Types.SelectMany(t => t.Methods).First(m => m.Name == "VirtualAlloc");
            Assert.Contains(virtAlloc.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call && i.Operand.ToString()!.Contains("NativeMemory::AllocZeroed"));

            var heapFree = module.Types.SelectMany(t => t.Methods).First(m => m.Name == "HeapFree");
            Assert.Contains(heapFree.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call && i.Operand.ToString()!.Contains("NativeMemory::Free"));

            var virtFree = module.Types.SelectMany(t => t.Methods).First(m => m.Name == "VirtualFree");
            Assert.Contains(virtFree.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call && i.Operand.ToString()!.Contains("NativeMemory::Free"));

            var virtProtect = module.Types.SelectMany(t => t.Methods).First(m => m.Name == "VirtualProtect");
            Assert.Contains(virtProtect.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stind_I4);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void Direct2DControl_Constructors_Are_Stubbed()
    {
        string fwPath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Framework.dll");
        if (!File.Exists(fwPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"Framework_Test_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(fwPath, map);
            AssemblyRewriter.Rewrite(fwPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var d2d = asm.MainModule.Types.First(t => t.Name == "Direct2DControl");
            foreach (var ctor in d2d.Methods.Where(m => m.Name == ".ctor"))
            {
                Assert.DoesNotContain(ctor.Body.Instructions, i => i.Operand?.ToString()?.Contains("Direct2DFactory::Create") == true);
            }

            var internalD2D = asm.MainModule.Types.First(t => t.Name == "InternalDirect2DControl");
            foreach (var ctor in internalD2D.Methods.Where(m => m.Name == ".ctor"))
            {
                Assert.DoesNotContain(ctor.Body.Instructions, i => i.Operand?.ToString()?.Contains("RefHandle") == true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void Direct2DControl_Factory_Getters_Are_Stubbed()
    {
        string fwPath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Framework.dll");
        if (!File.Exists(fwPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"Framework_FactoryTest_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(fwPath, map);
            AssemblyRewriter.Rewrite(fwPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var d2d = asm.MainModule.Types.First(t => t.Name == "Direct2DControl");
            var getDwf = d2d.Methods.First(m => m.Name == "get_DirectWriteFactory");
            Assert.Contains(getDwf.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldnull);

            var getD2D = d2d.Methods.First(m => m.Name == "get_Direct2DFactory");
            Assert.Contains(getD2D.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldnull);

            var compositor = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Composition.PdnCompositor");
            var ctrlField = compositor.Fields.First(f => f.Name == "compositorController");
            Assert.Equal("System.Object", ctrlField.FieldType.FullName);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.Framework.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void CanvasRenderingSettings_GetAutoMode_Returns_Software()
    {
        string pdnPath = TestPaths.Repo(@"targets\paint.net\paintdotnet.dll");
        if (!File.Exists(pdnPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"paintdotnet_Test_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(pdnPath, map);
            AssemblyRewriter.Rewrite(pdnPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var crs = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Canvas.CanvasRenderingSettings");
            var getAuto = crs.Methods.First(m => m.Name == "GetAutoMode" && m.Parameters.Count == 0);
            Assert.Contains(getAuto.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_2);

            var docView = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Controls.DocumentView");
            var preRenderPump = docView.Methods.First(m => m.Name == "PreRenderWhilePumping");
            Assert.Contains(preRenderPump.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret);
            Assert.DoesNotContain(preRenderPump.Body.Instructions, i => i.Operand?.ToString()?.Contains("BeginModalUIFrame") == true);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "paintdotnet.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void DXGI_And_DxgiFactory7_Are_Stubbed()
    {
        string winPath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Windows.dll");
        if (!File.Exists(winPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"PaintDotNet_Windows_Test_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(winPath, map);
            AssemblyRewriter.Rewrite(winPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var dxgi = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Dxgi.DXGI");
            var createF7 = dxgi.Methods.First(m => m.Name == "CreateFactory7" && m.ReturnType.FullName == "PaintDotNet.Dxgi.IDxgiFactory7");
            Assert.Contains(createF7.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldnull);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.Windows.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void EffectCategories_Cctor_Bakes_Category_Strings()
    {
        string fwPath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Windows.Framework.dll");
        if (!File.Exists(fwPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"WindowsFw_EffectCat_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(fwPath, map);
            AssemblyRewriter.Rewrite(fwPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var cats = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Direct2D1.EffectCategories");
            var cctor = cats.Methods.First(m => m.Name == ".cctor");
            Assert.Empty(cctor.Body.ExceptionHandlers);
            Assert.DoesNotContain(cctor.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call);
            var stored = cctor.Body.Instructions
                .Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stsfld)
                .Select(i => ((Mono.Cecil.FieldReference)i.Operand).Name)
                .ToArray();
            Assert.Equal(
                ["analysis", "color", "composite", "filter", "photo", "source", "stylize", "transform", "transparency"],
                stored);
            var literals = cctor.Body.Instructions
                .Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr)
                .Select(i => (string)i.Operand)
                .ToArray();
            Assert.Equal(
                ["Analysis", "Color", "Composite", "Filter", "Photo", "Source", "Stylize", "Transform", "Transparency"],
                literals);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.Windows.Framework.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void AnimatedValue_AnimateRawValue_Emits_Correct_Generic_MethodReference()
    {
        string corePdn = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Core.dll");
        if (!File.Exists(corePdn)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"PaintDotNet_Core_Test_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(corePdn, map);
            AssemblyRewriter.Rewrite(corePdn, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var av = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Animation.AnimatedValue");
            var arv = av.Methods.First(m => m.Name == "AnimateRawValue");
            var gvodCall = arv.Body.Instructions.FirstOrDefault(i => i.Operand?.ToString()?.Contains("GetValueOrDefault") == true);
            Assert.NotNull(gvodCall);
            var mr = (Mono.Cecil.MethodReference)gvodCall.Operand;
            Assert.True(mr.ReturnType.IsGenericParameter, "ReturnType must be generic parameter !0");

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.Core.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void PresentationCore_ModuleInitializer_Is_Nooped()
    {
        string corePath = TestPaths.Repo(@".artifacts\rewritten-paint\PresentationCore.dll");
        if (File.Exists(corePath))
        {
            string tempCoreOut = Path.Combine(Path.GetTempPath(), $"PresentationCore_Test_{Guid.NewGuid()}.dll");
            try
            {
                AssemblyRewriter.Rewrite(corePath, tempCoreOut, new Dictionary<string, string>());
                using var a = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempCoreOut);
                var miType = a.MainModule.Types.FirstOrDefault(t => t.Name == "ModuleInitializer");
                var initM = miType?.Methods.FirstOrDefault(m => m.Name == "Initialize");
                Assert.NotNull(initM);
                Assert.Contains(initM.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret);

            }
            finally
            {
                try { if (File.Exists(tempCoreOut)) File.Delete(tempCoreOut); } catch {}
            }
        }
    }

    [Fact]
    public void PresentationCore_FontFamily_And_FamilyCollection_Initialize_Without_Exception()
    {
        string pristineCore = @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.20\PresentationCore.dll";
        if (!File.Exists(pristineCore)) return;

        string tempOut = Path.Combine(Path.GetTempPath(), $"PC_FixTest_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(pristineCore, tempOut, new Dictionary<string, string>());
            using (var pcore = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut))
            {
                var curType = pcore.MainModule.Types.First(t => t.FullName == "System.Windows.Input.Cursor");
                var helper = curType.Methods.First(m => m.Name == "LoadCursorHelper");
                Assert.Contains(helper.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && (i.Operand as Mono.Cecil.FieldReference)?.Name == "_cursorType");
                Assert.DoesNotContain(helper.Body.Instructions, i => i.Operand?.ToString()?.Contains("LoadCursor") == true);
            }

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PresentationCore.dll"), overwrite: true);
            }
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch {}
        }
    }

    [Fact]
    public void SystemFonts_And_SystemParameters_Are_Stubbed()
    {
        string fwPath = @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.20\PresentationFramework.dll";
        if (!File.Exists(fwPath))
        {
            fwPath = TestPaths.Repo(@".artifacts\rewritten-paint\PresentationFramework.dll");
            if (!File.Exists(fwPath)) return;
        }

        string tempOut = Path.Combine(Path.GetTempPath(), $"PF_Test_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(fwPath, tempOut, new Dictionary<string, string>());
            using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut))
            {
                var sf = asm.MainModule.Types.FirstOrDefault(t => t.FullName == "System.Windows.SystemFonts");
                Assert.NotNull(sf);

                var getMsgFont = sf.Methods.First(m => m.Name == "get_MessageFontFamily");
                Assert.Contains(getMsgFont.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && (string)i.Operand == "Segoe UI");
                Assert.DoesNotContain(getMsgFont.Body.Instructions, i => i.Operand?.ToString()?.Contains("NonClientMetrics") == true);

                var getMsgSize = sf.Methods.First(m => m.Name == "get_MessageFontSize");
                Assert.Contains(getMsgSize.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_R8 && (double)i.Operand == 12.0);

                var getMsgStyle = sf.Methods.First(m => m.Name == "get_MessageFontStyle");
                Assert.Contains(getMsgStyle.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call && i.Operand?.ToString()?.Contains("get_Normal") == true);

                var getMsgWeight = sf.Methods.First(m => m.Name == "get_MessageFontWeight");
                Assert.Contains(getMsgWeight.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4 && (int)i.Operand == 400);

                var sp = asm.MainModule.Types.FirstOrDefault(t => t.FullName == "System.Windows.SystemParameters");
                Assert.NotNull(sp);
                var getNcm = sp.Methods.First(m => m.Name == "get_NonClientMetrics");
                Assert.DoesNotContain(getNcm.Body.Instructions, i => i.Operand?.ToString()?.Contains("SystemParametersInfo") == true);
            }

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PresentationFramework.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void FastMath_CombinePolyLists_And_GpcWrapper_Are_Stubbed()
    {
        string sysLayerPath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.SystemLayer.dll");
        if (!File.Exists(sysLayerPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"PaintDotNet_SystemLayer_Test_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(sysLayerPath, map);
            AssemblyRewriter.Rewrite(sysLayerPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var fastMath = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.SystemLayer.FastMath");
            var combineMethod = fastMath.Methods.First(m => m.Name == "CombinePolyLists");
            Assert.Contains(combineMethod.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Newarr);

            var uiHelpers = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.SystemLayer.UIHelpers");
            var beginModal = uiHelpers.Methods.First(m => m.Name == "BeginModalUIFrame");
            Assert.Contains(beginModal.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret);
            var beginModalCore = uiHelpers.Methods.First(m => m.Name == "BeginModalUIFrameCore");
            Assert.Contains(beginModalCore.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.SystemLayer.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void WIC_Is_Stubbed_Without_Native_Codecs()
    {
        string winPath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Windows.dll");
        if (!File.Exists(winPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"PaintDotNet_Windows_WicTest_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(winPath, map);
            AssemblyRewriter.Rewrite(winPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var wic = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Imaging.WIC");
            var cctor = wic.Methods.First(m => m.Name == ".cctor");
            Assert.DoesNotContain(cctor.Body.Instructions, i => i.Operand?.ToString()?.Contains("windowscodecs") == true);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.Windows.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void ColorProfiles_And_ColorProfile_Are_Stubbed()
    {
        string corePath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Windows.Core.dll");
        if (!File.Exists(corePath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"PaintDotNet_Core_CpTest_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(corePath, map);
            AssemblyRewriter.Rewrite(corePath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var cp = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Imaging.ColorProfile");
            var emptyCtor = cp.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0);
            Assert.NotNull(emptyCtor);

            var cps = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Imaging.ColorProfiles");
            var getSrgb = cps.Methods.First(m => m.Name == "get_Srgb");
            Assert.Contains(getSrgb.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld);

            var tryLin = cp.Methods.First(m => m.Name == "TryLinearize");
            Assert.Contains(tryLin.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_0);

            var ticket = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Direct2D1.Direct2DFactoryPoolTicket");
            var getRef = ticket.Methods.First(m => m.Name == "get_Ref");
            Assert.Contains(getRef.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldnull);

            var dc = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Direct2D1.DrawingContext");
            var dcCctor = dc.Methods.First(m => m.Name == ".cctor");
            Assert.Single(dcCctor.Body.Instructions);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, dcCctor.Body.Instructions[0].OpCode);

            var trpc = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.DirectWrite.TextRenderingParametersCache");
            var trpcCctor = trpc.Methods.First(m => m.Name == ".cctor");
            Assert.Single(trpcCctor.Body.Instructions);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, trpcCctor.Body.Instructions[0].OpCode);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.Windows.Core.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void GeometryResourceCacheHost_Is_Stubbed()
    {
        string uiPath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.UI.dll");
        if (!File.Exists(uiPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"UI_Test_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(uiPath, map);
            AssemblyRewriter.Rewrite(uiPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var host = asm.MainModule.GetTypes().First(t => t.Name == "GeometryResourceCacheHost");
            var ctor = host.Methods.First(m => m.Name == ".ctor");
            Assert.Contains(ctor.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret);

            var geom = asm.MainModule.GetTypes().First(t => t.FullName == "PaintDotNet.UI.Media.Geometry");
            var getBounds = geom.Methods.First(m => m.Name == "get_Bounds");
            Assert.Contains(getBounds.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.UI.dll"), overwrite: true);

                string objModelSrc = TestPaths.Repo(@"targets\paint.net\PaintDotNet.ObjectModel.dll");
                if (File.Exists(objModelSrc))
                {
                    string objOut = Path.Combine(Path.GetTempPath(), $"ObjModel_{Guid.NewGuid()}.dll");
                    try
                    {
                        var objTypeMap = GapAnalyzer.AutoMap(objModelSrc, map);
                        AssemblyRewriter.Rewrite(objModelSrc, objOut, map, objTypeMap);
                        File.Copy(objOut, Path.Combine(pdnDir, "PaintDotNet.ObjectModel.dll"), overwrite: true);
                    }
                    finally { if (File.Exists(objOut)) File.Delete(objOut); }
                }

                string fundSrc = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Fundamentals.dll");
                if (File.Exists(fundSrc))
                {
                    string fundOut = Path.Combine(Path.GetTempPath(), $"Fund_{Guid.NewGuid()}.dll");
                    try
                    {
                        var fundTypeMap = GapAnalyzer.AutoMap(fundSrc, map);
                        AssemblyRewriter.Rewrite(fundSrc, fundOut, map, fundTypeMap);
                        File.Copy(fundOut, Path.Combine(pdnDir, "PaintDotNet.Fundamentals.dll"), overwrite: true);
                    }
                    finally { if (File.Exists(fundOut)) File.Delete(fundOut); }
                }
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void SystemParameters_Is_Stubbed()
    {
        string wfPath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Windows.Framework.dll");
        if (!File.Exists(wfPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"WF_Test_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(wfPath, map);
            AssemblyRewriter.Rewrite(wfPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var sp = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.UI.SystemParameters");
            var getScale = sp.Methods.First(m => m.Name == "GetCursorScale");
            Assert.Contains(getScale.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_R8);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.Windows.Framework.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void CachedGeometry_Bounds_And_BrushToolBase_Are_Stubbed()
    {
        string pdnPath = TestPaths.Repo(@"targets\paint.net\paintdotnet.dll");
        if (!File.Exists(pdnPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"PDN_Test_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(pdnPath, map);
            AssemblyRewriter.Rewrite(pdnPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var cg = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Shapes.CachedGeometry");
            var getBounds = cg.Methods.First(m => m.Name == "get_Bounds");
            Assert.Contains(getBounds.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret);

            var bt = asm.MainModule.Types.First(t => t.FullName.StartsWith("PaintDotNet.Tools.BrushBase.BrushToolBase`3"));
            var preJIT = bt.Methods.First(m => m.Name == "PreJIT");
            Assert.Single(preJIT.Body.Instructions);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, preJIT.Body.Instructions[0].OpCode);
            var preJITBg = bt.Methods.First(m => m.Name == "PreJITBackground");
            Assert.Single(preJITBg.Body.Instructions);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, preJITBg.Body.Instructions[0].OpCode);

            var bsbr = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Brushes.Basic.BasicSampledBrushRenderer");
            var msi = bsbr.Methods.First(m => m.Name == "MeasureSpriteImage");
            Assert.DoesNotContain(msi.Body.Instructions, i => i.Operand?.ToString()?.Contains("DrawNull") == true);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "paintdotnet.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void CleanupThread_Is_Stubbed()
    {
        string rtPath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Runtime.dll");
        if (!File.Exists(rtPath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"RT_Test_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(rtPath, map);
            AssemblyRewriter.Rewrite(rtPath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var ct = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Runtime.CleanupThread");
            var cctor = ct.Methods.First(m => m.Name == ".cctor");
            Assert.Single(cctor.Body.Instructions);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, cctor.Body.Instructions[0].OpCode);

            var isQueued = ct.Methods.First(m => m.Name == "IsQueued");
            Assert.Contains(isQueued.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0);

            var ensureEnqueued = ct.Methods.First(m => m.Name == "EnsureEnqueued");
            Assert.Contains(ensureEnqueued.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.Runtime.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void PaintDotNet_Base_SerializationFallbackBinder_Stubbed()
    {
        string basePath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Base.dll");
        if (!File.Exists(basePath)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"Base_Test_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(basePath, map);
            AssemblyRewriter.Rewrite(basePath, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var sfb = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.Serialization.SerializationFallbackBinder");
            var cctor = sfb.Methods.First(m => m.Name == ".cctor");
            Assert.DoesNotContain(cctor.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldtoken && i.Operand?.ToString()?.Contains("CodeDom") == true);
            Assert.DoesNotContain(asm.MainModule.AssemblyReferences, r => r.Name == "System.CodeDom");

            var rv = asm.MainModule.Types.First(t => t.FullName == "PaintDotNet.ReferenceValue");
            var cloneCore = rv.Methods.First(m => m.Name == "CloneCore");
            Assert.Contains(cloneCore.Body.Instructions, i => i.Operand?.ToString()?.Contains("MemberwiseClone") == true);
            Assert.DoesNotContain(cloneCore.Body.Instructions, i => i.Operand?.ToString()?.Contains("BinaryFormatter") == true);

            string pdnDir = TestPaths.Repo(@".artifacts\rewritten-paint");
            if (Directory.Exists(pdnDir))
            {
                File.Copy(tempOut, Path.Combine(pdnDir, "PaintDotNet.Base.dll"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void Rewrite_CheckerboardRefresh_Runs_Headless()
    {
        // ColorGradientControl renders through CheckerboardEffect:
        // Refresh recomputes effect props from the live Properties bag
        // (writes stay inert via the stubbed SetValue), so it runs for
        // real headless instead of a bare ret.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_Refresh_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var fx = asm.MainModule.GetType("PaintDotNet.VisualStyling.CheckerboardEffect");
            Assert.NotNull(fx);
            var refresh = fx.Methods.First(m => m.Name == "Refresh" && m.Parameters.Count == 0);
            Assert.True(refresh.Body.Instructions.Count > 1);
            Assert.Contains(refresh.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "GetColors");
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void Rewrite_ColorContext_CreateColorContext_Answers_Cpu()
    {
        // Effect dialogs (ManagedColor) and wheel bitmaps need a valid
        // default color context: every CreateColorContext overload
        // answers a synthesized CPU device color context instead of
        // throwing.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_ColorCtx_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var colorCtx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDeviceColorContext");
            Assert.NotNull(colorCtx);
            Assert.Contains(colorCtx.Interfaces,
                i => i.InterfaceType.FullName == "PaintDotNet.Direct2D1.IDeviceColorContext");
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var makers = ctx.Methods.Where(m => m.Name == "CreateColorContext").ToList();
            Assert.Equal(5, makers.Count);
            foreach (var maker in makers)
            {
                Assert.Contains(maker.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                    && i.Operand is Mono.Cecil.MethodReference mr && mr.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuDeviceColorContext");
                Assert.DoesNotContain(maker.Body.Instructions, i =>
                    i.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
                AssertCalleesResolve(asm.MainModule, maker);
            }
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void Rewrite_AutoColorSpace_Delegates_To_Inner_Provider()
    {
        // History/Layers icons select the sRGB variant through
        // AutoColorSpaceImageProvider: the CPU cache answers the inner
        // sRGB provider's resource (color management is identity).
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_AutoCs_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var resCache = ctx.Methods.First(m => m.Name == "GetCachedOrCreateResource");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string?)i.Operand == "PaintDotNet.Resources.ImageResourceUtil+AutoColorSpaceImageProvider");
            AssertCalleesResolve(asm.MainModule, resCache);
            // The provider dispatch chain must be forward-only: a
            // backward conditional jump re-enters its own check and
            // wedges the UI thread (no backward dispatch exists).
            var dispatch = resCache.Body.Instructions;
            for (int k = 0; k < dispatch.Count; k++)
            {
                var op = dispatch[k];
                if (op.OpCode.FlowControl == Mono.Cecil.Cil.FlowControl.Cond_Branch
                    && op.Operand is Mono.Cecil.Cil.Instruction tgt)
                {
                    Assert.True(dispatch.IndexOf(tgt) > k);
                }
            }
            // No reflection-field reads: GetField(string,BindingFlags)
            // faults Bad IL in the browser runtime; the inner provider
            // resolves through an imported metadata field instead.
            Assert.DoesNotContain(resCache.Body.Instructions, i =>
                (i.OpCode == Mono.Cecil.Cil.OpCodes.Call || i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.Name == "GetField" && mr.Parameters.Count == 2);
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldfld);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void Rewrite_AutoColorSpace_Inner_Field_Is_Public()
    {
        // The CPU cache unwraps AutoColorSpace through the inner sRGB
        // field from another class: the Framework pass ships it public.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Framework.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Fw_AutoPub_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var util = asm.MainModule.GetType("PaintDotNet.Resources.ImageResourceUtil");
            Assert.NotNull(util);
            var auto = util.NestedTypes.First(n => n.Name == "AutoColorSpaceImageProvider");
            var inner = auto.Fields.First(f => f.Name == "srgbImageProvider");
            Assert.True(inner.IsPublic);             var scaled = asm.MainModule.GetType("PaintDotNet.Resources.ScaledDeviceImageProvider");             Assert.NotNull(scaled);             Assert.True(scaled.Fields.First(f => f.Name == "source").IsPublic);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public void Rewrite_ProviderDispatch_Reaches_All_Branches()
    {
        // The solid-brush miss used to jump past the SystemColor duck
        // probe straight to tryText, leaving SystemBrush without a
        // backend; duck misses in turn skipped the Auto gate. Every
        // provider family must be reachable in order.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_Dispatch_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var resCache = ctx.Methods.First(m => m.Name == "GetCachedOrCreateResource");
            var code = resCache.Body.Instructions;
            Assert.Contains(code, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string?)i.Operand == "PaintDotNet.Resources.ScaledDeviceImageProvider");
            int SolidMissTarget()
            {
                for (int k = 2; k < code.Count; k++)
                {
                    if (code[k].OpCode.FlowControl != Mono.Cecil.Cil.FlowControl.Cond_Branch)
                        continue;
                    if (code[k - 2].OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                        && (string?)code[k - 2].Operand == "PaintDotNet.UI.Media.SolidColorBrush")
                        return code.IndexOf((Mono.Cecil.Cil.Instruction)code[k].Operand);
                }
                return -1;
            }
            int duckAt = SolidMissTarget();
            Assert.True(duckAt > 0);
            Assert.True(code[duckAt].OpCode == Mono.Cecil.Cil.OpCodes.Ldloc
                || code[duckAt].OpCode == Mono.Cecil.Cil.OpCodes.Nop);
            int probeAt = code[duckAt].OpCode == Mono.Cecil.Cil.OpCodes.Nop ? duckAt + 1 : duckAt;
            Assert.True(code[probeAt].OpCode == Mono.Cecil.Cil.OpCodes.Ldloc);
            Assert.True(code[probeAt + 1].OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string?)code[probeAt + 1].Operand == "get_SystemColor");
            Assert.True(code[probeAt + 2].OpCode == Mono.Cecil.Cil.OpCodes.Call);
            int autoGate = -1;
            for (int k = 2; k < code.Count; k++)
            {
                if (code[k].OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                    && (string?)code[k].Operand == "PaintDotNet.Resources.ImageResourceUtil+AutoColorSpaceImageProvider")
                { autoGate = k - 3; break; }
            }
            Assert.True(autoGate > 0);
            int duckMissHits = 0;
            foreach (var ins in code)
            {
                if (ins.OpCode.FlowControl == Mono.Cecil.Cil.FlowControl.Cond_Branch
                    && ins.Operand is Mono.Cecil.Cil.Instruction tgt
                    && code.IndexOf(tgt) == autoGate)
                    duckMissHits++;
            }
            Assert.True(duckMissHits >= 3);             int scaledGate = -1;             for (int k = 2; k < code.Count; k++)             {                 if (code[k].OpCode == Mono.Cecil.Cil.OpCodes.Ldstr                     && (string?)code[k].Operand == "PaintDotNet.Resources.ScaledDeviceImageProvider")                 { scaledGate = k - 3; break; }             }             Assert.True(scaledGate > 0);             int autoMissHits = 0;             foreach (var ins in code)             {                 if (ins.OpCode.FlowControl == Mono.Cecil.Cil.FlowControl.Cond_Branch                     && ins.Operand is Mono.Cecil.Cil.Instruction tgt2                     && code.IndexOf(tgt2) == scaledGate)                     autoMissHits++;             }             Assert.True(autoMissHits >= 2);            AssertCalleesResolve(asm.MainModule, resCache);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
    [Fact]
    public void Rewrite_SystemColor_Is_Public_For_Cpu_Unbox()
    {
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Framework.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Fw_SysPub_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var sys = asm.MainModule.GetType("PaintDotNet.Imaging.SystemColor");
            Assert.NotNull(sys);
            Assert.True(sys.IsPublic);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
    [Fact]
    public void Rewrite_CpuDeviceBitmap_IsDisposed_Tracks_Handle()
    {
        // Document strips verify every cached bitmap before drawing:
        // IsDisposed must answer handle==0 (Dispose zeroes it) instead
        // of throwing NotSupported.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_BmpDisp_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var bmp = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDeviceBitmap");
            Assert.NotNull(bmp);
            var isDisp = bmp.Methods.First(m => m.Name == "get_IsDisposed" && m.Parameters.Count == 0);
            Assert.Contains(isDisp.Body.Instructions, x => x.OpCode == Mono.Cecil.Cil.OpCodes.Ceq);
            Assert.DoesNotContain(isDisp.Body.Instructions, x => x.OpCode == Mono.Cecil.Cil.OpCodes.Throw);
            var dispose = bmp.Methods.First(m => m.Name == "Dispose" && m.Parameters.Count == 0);
            Assert.Contains(dispose.Body.Instructions, x => x.OpCode == Mono.Cecil.Cil.OpCodes.Stfld);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
    [Fact]
    public void Rewrite_ManagedColor_Create_Defaults_Null_Context()
    {
        // Document color contexts are null headless, which blocks every
        // effect dialog at ManagedColor.Create: the factory stands in a
        // CPU sRGB context (Exif sRGB truth, no profile bytes).
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_McCtx_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var cpu = asm.MainModule.GetType("PaintDotNet.Imaging.CpuColorContext");
            Assert.NotNull(cpu);
            var exif = cpu.Methods.First(m => m.Name == "get_ExifColorSpace" && m.Parameters.Count == 0);
            Assert.Contains(exif.Body.Instructions, x => x.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
            var managed = asm.MainModule.GetType("PaintDotNet.Imaging.ManagedColor");
            Assert.NotNull(managed);
            var create = managed.Methods.First(m => m.Name == "Create" && m.Parameters.Count == 2
                && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Imaging.IColorContext");
            var head = create.Body.Instructions.Take(5).ToList();
            Assert.True(head[0].OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_1);
            Assert.True(head[1].OpCode == Mono.Cecil.Cil.OpCodes.Brtrue_S);
            Assert.True(head[2].OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && ((Mono.Cecil.MethodReference)head[2].Operand).DeclaringType.FullName == "PaintDotNet.Imaging.CpuColorContext");
            Assert.True(head[3].OpCode == Mono.Cecil.Cil.OpCodes.Starg_S);
            AssertCalleesResolve(asm.MainModule, create);
            // The loader must accept every slot: a generic return built
            // the wrong way loads metadata-clean but fails vtable setup
            // (TypeLoad on first effect use). Load for real here.
            var alc = new System.Runtime.Loader.AssemblyLoadContext($"CcLoad_{Guid.NewGuid()}", true);
            try
            {
                alc.Resolving += (ctx, name) =>
                {
                    string probe = Path.Combine(paintDir, (name.Name ?? "") + ".dll");
                    return File.Exists(probe) ? ctx.LoadFromAssemblyPath(probe) : null;
                };
                var loaded = alc.LoadFromAssemblyPath(tempOut);
                var ccType = loaded.GetType("PaintDotNet.Imaging.CpuColorContext");
                Assert.NotNull(ccType);
                Assert.True(ccType.GetMethods().Length >= 8);
                var inst = Activator.CreateInstance(ccType);
                Assert.NotNull(inst);
                var exifM = ccType.GetMethod("get_ExifColorSpace");
                Assert.NotNull(exifM);
                Assert.True((ushort)exifM.Invoke(inst, null)! == 1);
                var iccT = ccType.GetInterfaces().First(t => t.Name == "IColorContext");
                var tcr = ccType.GetMethod("TryCreateRef")!;
                var args = new object?[] { iccT, null };
                Assert.True((bool?)tcr.Invoke(inst, args) == true);
                Assert.Same(inst, args[1]);
            }
            finally { alc.Unload(); }
        }
        finally
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void Rewrite_ControlInfo_Initialize_Survives()
    {
        // The host replays desktop startup (Program.StartPart2) by
        // reflecting ControlInfo.Initialize(paintdotnet) at boot: the
        // rewriter must leave it and get_ControlsAssembly untouched.
        // Without the call ControlsAssembly stays null and every effect
        // config UI dies in PropertyControlInfo..cctor (NotNull).
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_CtrlInit_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ctrl = asm.MainModule.GetType("PaintDotNet.IndirectUI.ControlInfo");
            Assert.NotNull(ctrl);
            var init = ctrl.Methods.First(m => m.Name == "Initialize" && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "System.Reflection.Assembly");
            Assert.True(init.IsStatic);
            var head = init.Body.Instructions.ToList();
            Assert.True(head.Count == 7);
            Assert.True(head[0].OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld
                && ((Mono.Cecil.FieldReference)head[0].Operand).Name == "controlsAssembly");
            Assert.True(head[5].OpCode == Mono.Cecil.Cil.OpCodes.Stsfld);
            var getter = ctrl.Methods.First(m => m.Name == "get_ControlsAssembly" && m.Parameters.Count == 0);
            Assert.True(getter.IsStatic);
            Assert.Contains(getter.Body.Instructions, x => x.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld);
            AssertCalleesResolve(asm.MainModule, init);
            AssertCalleesResolve(asm.MainModule, getter);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void PaintNet_MetadataLoadContext_Served_On_Demand()
    {
        // paintdotnet reflects over its own assembly in
        // PropertyControlInfo..cctor (Assembly.GetTypes): that scan
        // needs every statically-referenced assembly loadable, and
        // System.Reflection.MetadataLoadContext is not a framework
        // assembly the bundle otherwise deploys. Paint.NET ships
        // on demand, so the server manifest (not the client csproj)
        // must carry it, and the client must not bundle paint at all.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string pdn = Path.Combine(paintDir, "paintdotnet.dll");
        string csproj = TestPaths.Repo(@"src\WfWasm.CanvasHost\WfWasm.CanvasHost.csproj");
        string server = TestPaths.Repo(@"src\WfWasm.Host.Server\AppTransform.cs");
        if (!File.Exists(pdn) || !File.Exists(csproj) || !File.Exists(server)) return;
        var resolver = new Mono.Cecil.DefaultAssemblyResolver();
        resolver.AddSearchDirectory(paintDir);
        using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(pdn,
            new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
        Assert.Contains(asm.MainModule.AssemblyReferences,
            r => r.Name == "System.Reflection.MetadataLoadContext");
        string spec = File.ReadAllText(server);
        Assert.Contains("System.Reflection.MetadataLoadContext.dll", spec);
        string proj = File.ReadAllText(csproj);
        Assert.DoesNotContain("rewritten-paint", proj);
    }
    [Fact]
    public void Rewrite_EffectServiceProvider_Null_Context_Defaults()
    {
        // Effect runs hand a null document color context headless (no
        // WIC factory): EffectServiceProvider..ctor validates non-null
        // and every effect dies. Same CPU sRGB stand-in as the
        // ManagedColor factory default, synthesized into Effects.Core.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Effects.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Fx_SvcProv_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var cpu = asm.MainModule.GetType("PaintDotNet.Imaging.CpuColorContext");
            Assert.NotNull(cpu);
            var svc = asm.MainModule.GetType("PaintDotNet.Effects.EffectServiceProvider");
            Assert.NotNull(svc);
            var ctor = svc.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 2
                && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Imaging.IColorContext");
            var head = ctor.Body.Instructions.Take(5).ToList();
            Assert.True(head[0].OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_2);
            Assert.True(head[1].OpCode == Mono.Cecil.Cil.OpCodes.Brtrue_S);
            Assert.True(head[2].OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && ((Mono.Cecil.MethodReference)head[2].Operand).DeclaringType.FullName == "PaintDotNet.Imaging.CpuColorContext");
            Assert.True(head[3].OpCode == Mono.Cecil.Cil.OpCodes.Starg_S);
            AssertCalleesResolve(asm.MainModule, ctor);
            // The loader must accept the synthesized slots here too.
            var alc = new System.Runtime.Loader.AssemblyLoadContext($"SpLoad_{Guid.NewGuid()}", true);
            try
            {
                alc.Resolving += (ctx, name) =>
                {
                    string probe = Path.Combine(paintDir, (name.Name ?? "") + ".dll");
                    return File.Exists(probe) ? ctx.LoadFromAssemblyPath(probe) : null;
                };
                var loaded = alc.LoadFromAssemblyPath(tempOut);
                var ccType = loaded.GetType("PaintDotNet.Imaging.CpuColorContext");
                Assert.NotNull(ccType);
                var inst = Activator.CreateInstance(ccType);
                Assert.NotNull(inst);
                var exifM = ccType.GetMethod("get_ExifColorSpace");
                Assert.NotNull(exifM);
                Assert.True((ushort)exifM.Invoke(inst, null)! == 1);
            }
            finally { alc.Unload(); }
        }
        finally
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void Rewrite_GpuChoice_Software_Default()
    {
        // GPU effects pick a rendering device through a settings lookup
        // that is null headless: get_RenderingGpuChoice NREs and every
        // GPU effect dies. The synthesized setting answers the software
        // choice (Type==3), routing effects down the CPU branch — the
        // same path a device-less desktop takes.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Effects.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Fx_GpuChoice_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var setting = asm.MainModule.GetType("PaintDotNet.Gpu.CpuGpuChoiceSetting");
            Assert.NotNull(setting);
            Assert.Equal(2, setting.Methods.Count(m => m.Name == "get_Value"));
            Assert.Equal(2, setting.Methods.Count(m => m.Name == "set_Value"));
            Assert.Equal(2, setting.Methods.Count(m => m.Name == "get_DefaultValue"));
            Assert.Contains(setting.Methods, m => m.Name == "get_Path");
            Assert.Contains(setting.Methods, m => m.Name == "get_IsReadOnly");
            Assert.Contains(setting.Methods, m => m.Name == "get_ValueType");
            var value = setting.Methods.First(m => m.Name == "get_Value" && m.Parameters.Count == 0
                && m.ReturnType.FullName == "PaintDotNet.Gpu.GpuChoice");
            Assert.Contains(value.Body.Instructions, x => x.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && ((Mono.Cecil.MethodReference)x.Operand).DeclaringType.FullName == "PaintDotNet.Gpu.GpuChoice");
            var effect = asm.MainModule.GetType("PaintDotNet.Effects.Gpu.GpuEffect");
            Assert.NotNull(effect);
            var getter = effect.Methods.First(m => m.Name == "get_RenderingGpuChoice" && m.Parameters.Count == 0);
            var head = getter.Body.Instructions.ToList();
            Assert.True(head.Count == 2);
            Assert.True(head[0].OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && ((Mono.Cecil.MethodReference)head[0].Operand).DeclaringType.FullName == "PaintDotNet.Gpu.CpuGpuChoiceSetting");
            Assert.True(head[1].OpCode == Mono.Cecil.Cil.OpCodes.Ret);
            AssertCalleesResolve(asm.MainModule, value);
            AssertCalleesResolve(asm.MainModule, getter);
            // The loader must accept the generic-interface slots: a
            // wrongly-built closed interface loads metadata-clean but
            // fails vtable setup. Load for real here.
            var alc = new System.Runtime.Loader.AssemblyLoadContext($"GcLoad_{Guid.NewGuid()}", true);
            try
            {
                alc.Resolving += (ctx, name) =>
                {
                    string probe = Path.Combine(paintDir, (name.Name ?? "") + ".dll");
                    return File.Exists(probe) ? ctx.LoadFromAssemblyPath(probe) : null;
                };
                var loaded = alc.LoadFromAssemblyPath(tempOut);
                var stType = loaded.GetType("PaintDotNet.Gpu.CpuGpuChoiceSetting");
                Assert.NotNull(stType);
                var inst = Activator.CreateInstance(stType);
                Assert.NotNull(inst);
                var getValue = stType.GetMethods().First(m => m.Name == "get_Value" && m.GetParameters().Length == 0 && m.ReturnType.FullName == "PaintDotNet.Gpu.GpuChoice");
                Assert.NotNull(getValue);
                var choice = getValue.Invoke(inst, null);
                Assert.NotNull(choice);
                var typeProp = choice.GetType().GetProperty("Type");
                Assert.NotNull(typeProp);
                Assert.Equal(3, Convert.ToInt32(typeProp.GetValue(choice)));
            }
            finally { alc.Unload(); }
        }
        finally
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void Rewrite_Same_Inputs_Same_Bytes()
    {
        // The on-demand transform cache serves bytes by input hash:
        // the rewriter must be deterministic for the cache to be sound.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string out1 = Path.Combine(Path.GetTempPath(), $"Core_DetA_{Guid.NewGuid()}.dll");
        string out2 = Path.Combine(Path.GetTempPath(), $"Core_DetB_{Guid.NewGuid()}.dll");
        try
        {
            var empty = new Dictionary<string, string>(StringComparer.Ordinal);
            var emptyMap = new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal);
            AssemblyRewriter.Rewrite(input, out1, empty, emptyMap, cpuDll);
            AssemblyRewriter.Rewrite(input, out2, empty, emptyMap, cpuDll);
            Assert.Equal(File.ReadAllBytes(out1), File.ReadAllBytes(out2));
        }
        finally
        {
            try { if (File.Exists(out1)) File.Delete(out1); } catch { }
            try { if (File.Exists(out2)) File.Delete(out2); } catch { }
        }
    }
    [Fact]
    public void Rewrite_CpuImagingShortcuts_Return_Source()
    {
        // WIC has no headless factory: terminal clippers and color
        // transforms answer their source bitmap (refcounted custody
        // keeps it alive; color work is identity without a device).
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_ImgFx_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var bse = asm.MainModule.GetType("PaintDotNet.Imaging.BitmapSourceExtensions");
            Assert.NotNull(bse);
            var clip = bse.Methods.First(m => m.Name == "CreateClipper"
                && m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapSource");
            Assert.True(clip.Body.Instructions[0].OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_0);
            Assert.DoesNotContain(clip.Body.Instructions, x => x.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt);
            int transforms = 0;
            foreach (var m in bse.Methods)
            {
                if (m.Name == "CreateColorTransformer"
                    && m.Parameters.Count == 4
                    && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapSource")
                {
                    Assert.True(m.Body.Instructions[0].OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_0);
                    transforms++;
                }
            }
            Assert.True(transforms >= 2);
            var ife = asm.MainModule.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(ife);
            var generic = ife.Methods.First(m => m.Name == "CreateColorTransformedBitmap"
                && m.Parameters.Count == 4
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IImagingFactory"
                && m.Parameters[2].ParameterType.FullName == "PaintDotNet.Imaging.IColorContext");
            Assert.Contains(generic.Body.Instructions, x => x.OpCode == Mono.Cecil.Cil.OpCodes.Castclass);
            var term = ife.Methods.First(m => m.Name == "CreateColorTransformedBitmap"
                && m.Parameters.Count == 5
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IImagingFactory");
            Assert.True(term.Body.Instructions[0].OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_1);
            Assert.True(term.Body.ExceptionHandlers.Count == 0);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
    [Fact]
    public void Rewrite_CpuContextExtensions_Answer_Headless()
    {
        // The CPU context is software by definition; image measurements
        // answer local bounds for both rects (no measure effect). Tokens
        // harvest from the original bodies.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_CtxExt_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ext = asm.MainModule.GetType("PaintDotNet.Direct2D1.DeviceContextExtensions");
            Assert.NotNull(ext);
            var soft = ext.Methods.First(m => m.Name == "IsSoftwareContext" && m.Parameters.Count == 1);
            Assert.True(soft.Body.Instructions[0].OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
            var meas = ext.Methods.First(m => m.Name == "GetImageLocalMeasurements" && m.Parameters.Count == 2);
            Assert.Contains(meas.Body.Instructions, x => x.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && ((Mono.Cecil.MethodReference)x.Operand).DeclaringType.FullName.StartsWith("System.ValueTuple`2"));
            Assert.DoesNotContain(meas.Body.Instructions, x => x.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName.Contains("MeasureImageEffect"));
            var boundsCall = meas.Body.Instructions.First(x => x.Operand is Mono.Cecil.MethodReference mr2
                && mr2.Name == "GetImageLocalBounds");
            Assert.True(boundsCall.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt);
            AssertCalleesResolve(asm.MainModule, meas);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
    [Fact]
    public void Rewrite_MaximumBitmapSize_Answers_Hardware_Cap()
    {
        // BitmapImage tiles with Min(requested, MaximumBitmapSize):
        // the zero-init default divides by zero, so the CPU context
        // reports the desktop D2D cap (16384, never allocated).
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_MaxBmp_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var maxBmp = ctx.Methods.First(m => m.Name == "get_MaximumBitmapSize");
            Assert.Contains(maxBmp.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference mr && mr.DeclaringType.FullName == "PaintDotNet.Rendering.SizeInt32");
            AssertCalleesResolve(asm.MainModule, maxBmp);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
    [Fact]
    public void Rewrite_ColorTransform_ProfilePair_Returns_Source()
    {
        // The generic (factory, source, IColorProfile, IColorProfile) overload
        // serves non-sRGB pixel types (the layers strip's Half checkbox
        // bitmaps): headless, already-typed sources answer as-is while
        // Bgra32/Pbgra32 sources convert on the CPU, like the float
        // format rule. Regression: InvalidCastException |
        // BitmapSourceExtensions.CreateColorTransformer (the blind
        // identity returned the untyped source, whose wrapper cast to
        // IBitmapSource<Half> faulted every render).
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_ProfilePair_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ife = asm.MainModule.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(ife);
            var profilePair = ife.Methods.FirstOrDefault(m => m.Name == "CreateColorTransformedBitmap"
                && m.HasGenericParameters && m.GenericParameters.Count == 1
                && m.Parameters.Count == 4
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IImagingFactory"
                && m.Parameters[2].ParameterType.FullName == "PaintDotNet.Imaging.IColorProfile");
            Assert.NotNull(profilePair);
            var callees = profilePair.Body.Instructions
                .Select(i => i.Operand).OfType<Mono.Cecil.MethodReference>().Select(r => r.Name).ToList();
            Assert.DoesNotContain("CastOrTransferRef", callees);
            Assert.Contains("ConvertBgra32ToPrgba64Half", callees);
            Assert.Contains(profilePair.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Isinst);
            AssertCalleesResolve(asm.MainModule, profilePair);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void Rewrite_Drops_CurrentDirectory_Set()
    {
        // Browser WASM has no working directory: CoreLib throws
        // DirectoryNotFound for the virtual app folder out of
        // MainForm..ctor. The set is dropped at rewrite time.
        // Regression: DirectoryNotFoundException | Interop.CheckIo.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "paintdotnet.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Pdn_NoCwd_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            bool anySet = asm.MainModule.GetTypes()
                .SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Any(i => (i.OpCode == Mono.Cecil.Cil.OpCodes.Call || i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
                    && i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "set_CurrentDirectory"
                    && mr.DeclaringType.FullName == "System.Environment");
            Assert.False(anySet);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void Rewrite_WheelBitmap_LockFast_Chain()
    {
        // The color wheel builds its resources through
        // Allocate -> CreateReinterpretedBitmap -> LockFast. Headless
        // this NRE'd inside ReinterpretedBitmapSource.get_Size, so the
        // whole chain is replayed here on desktop: the bitmap must
        // allocate, wrap in a ref, reinterpret, and report its size.
        // Regression: NRE | BitmapExtensions.LockFast.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string forkDll = TestPaths.Repo(@".artifacts\runtime\WfWasm.WinForms.dll");
        string drawingDll = ShimBinDll("WfWasm.Drawing");
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string handmap = TestPaths.Repo(@"src\WfWasm.Rewrite.Tool\handmap.paint.net.txt");
        string[] closure = new[] { "PaintDotNet.Primitives.dll", "PaintDotNet.ComponentModel.dll", "PaintDotNet.Windows.Core.dll", "PaintDotNet.Windows.dll", "PaintDotNet.Windows.Framework.dll" };
        foreach (string f in closure.Concat(new[] { forkDll, drawingDll, cpuDll, handmap }))
            if (!File.Exists(f.StartsWith("PaintDotNet") ? Path.Combine(paintDir, f) : f)) return;
        string work = Path.Combine(Path.GetTempPath(), "WheelChain_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var assemblyMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = forkDll,
                ["System.Windows.Forms.Primitives"] = forkDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            foreach (string f in closure)
            {
                string input = Path.Combine(paintDir, f);
                var typeMap = WfWasm.Rewrite.GapAnalyzer.AutoMap(input, assemblyMap);
                foreach (string line in File.ReadAllLines(handmap))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                        continue;
                    string[] sides = trimmed.Split('=', 2);
                    if (sides.Length != 2)
                        continue;
                    string[] target = sides[1].Split(':', 2);
                    if (target.Length != 2)
                        continue;
                    typeMap[sides[0]] = (target[0], target[1]);
                }

                AssemblyRewriter.Rewrite(input, Path.Combine(work, f), assemblyMap, typeMap, cpuDll);
            }

            var alc = new System.Runtime.Loader.AssemblyLoadContext($"WheelChain_{Guid.NewGuid()}", true);
            try
            {
                string[] dirs = new[] { work, paintDir, Path.GetDirectoryName(forkDll)!, Path.GetDirectoryName(drawingDll)!, Path.GetDirectoryName(cpuDll)! };
                alc.Resolving += (ctx, name) =>
                {
                    foreach (string d in dirs)
                    {
                        string probe = Path.Combine(d, (name.Name ?? "") + ".dll");
                        if (File.Exists(probe))
                            return ctx.LoadFromAssemblyPath(probe);
                    }

                    return null;
                };
                var core = alc.LoadFromAssemblyPath(Path.Combine(work, "PaintDotNet.Windows.Core.dll"));
                var prims = alc.LoadFromAssemblyPath(Path.Combine(work, "PaintDotNet.Primitives.dll"));
                var wins = alc.LoadFromAssemblyPath(Path.Combine(work, "PaintDotNet.Windows.dll"));
                var bgraT = prims.GetType("PaintDotNet.Imaging.ColorBgra32");
                Assert.NotNull(bgraT);
                var sizeT = prims.GetType("PaintDotNet.Rendering.SizeInt32") ?? core.GetType("PaintDotNet.Rendering.SizeInt32");
                Assert.NotNull(sizeT);
                var sizeCtor = sizeT.GetConstructor(new[] { typeof(int), typeof(int) });
                Assert.NotNull(sizeCtor);
                var optsT = wins.GetType("PaintDotNet.Imaging.BitmapAllocationOptions");
                Assert.NotNull(optsT);
                var allocatorT = core.GetType("PaintDotNet.Imaging.BitmapAllocator");
                Assert.NotNull(allocatorT);
                var allocM = allocatorT.GetMethods().First(m => m.Name == "Allocate" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                var alloc = allocM.MakeGenericMethod(bgraT);
                object? locked = alloc.Invoke(null, new object?[] { sizeCtor.Invoke(new object?[] { 8, 8 }), Enum.ToObject(optsT, 0) });
                Assert.NotNull(locked);
                var srcIface = wins.GetType("PaintDotNet.Imaging.IBitmapSource");
                Assert.NotNull(srcIface);
                Assert.True(srcIface.IsInstanceOfType(locked));
                var crefT = wins.GetType("PaintDotNet.Imaging.ImagingObjectRefExtensions");
                Assert.NotNull(crefT);
                var crefM = crefT.GetMethods().First(m => m.Name == "CreateRef" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapSource");
                object? wrapped = crefM.Invoke(null, new object?[] { locked });
                Assert.NotNull(wrapped);
                var reinterpretT = core.GetType("PaintDotNet.Imaging.ReinterpretedBitmapSource");
                Assert.NotNull(reinterpretT);
                var pixfmt = locked.GetType().GetProperty("PixelFormat")!.GetValue(locked);
                var rctor = reinterpretT.GetConstructors().First(c => c.GetParameters().Length == 3);
                object? reinterpreted = rctor.Invoke(new object?[] { locked, pixfmt, null });
                Assert.NotNull(reinterpreted);
                var size = reinterpretT.GetProperty("Size")!.GetValue(reinterpreted);
                Assert.NotNull(size);
                Assert.Equal(8, (int)sizeT.GetProperty("Width")!.GetValue(size)!);
                Assert.Equal(8, (int)sizeT.GetProperty("Height")!.GetValue(size)!);
                var ctxT = core.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
                Assert.NotNull(ctxT);
                object? ctx = Activator.CreateInstance(ctxT, new object?[] { 0L });
                Assert.NotNull(ctx);
                var propsT = wins.GetType("PaintDotNet.Direct2D1.SharedBitmapProperties");
                Assert.NotNull(propsT);
                var sharedM = ctxT.GetMethods().First(m => m.Name == "CreateSharedBitmap" && m.GetParameters().Length == 2
                    && m.GetParameters()[0].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapLock");
                object? props = Activator.CreateInstance(propsT);
                object? deviceBmp = sharedM.Invoke(ctx, new object?[] { locked, props });
                Assert.NotNull(deviceBmp);
                var dbIface = deviceBmp.GetType().GetInterfaces().First(t => t.Name == "IDeviceBitmap");
                var pixelSize = dbIface.GetProperty("PixelSize")!.GetValue(deviceBmp);
                Assert.NotNull(pixelSize);
                Assert.Equal(8, (int)sizeT.GetProperty("Width")!.GetValue(pixelSize)!);
                Assert.Equal(8, (int)sizeT.GetProperty("Height")!.GetValue(pixelSize)!);
                // Full CreateImpl fidelity: the tile goes through
                // TryCastOrCreateRefOf<IBitmap> then the extension, exactly
                // like the browser path (proxy objects surface here).
                var ibitmapT = wins.GetType("PaintDotNet.Imaging.IBitmap");
                Assert.NotNull(ibitmapT);
                var objrefT = core.GetType("PaintDotNet.ComponentModel.ObjectRefExtensions");
                var compCore = alc.LoadFromAssemblyPath(Path.Combine(work, "PaintDotNet.ComponentModel.dll"));
                objrefT = compCore.GetType("PaintDotNet.ComponentModel.ObjectRefExtensions");
                Assert.NotNull(objrefT);
                // NOTE: TryCastOrCreateRef is internal, so public-only
                // GetMethods() hides it (verified: pristine shows the same
                // 10 public methods). NonPublic flags are required.
                var objFlags = System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic;
                var tryCastM = objrefT.GetMethods(objFlags).First(m => m.Name == "TryCastOrCreateRef" && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1);
                var tryCast = tryCastM.MakeGenericMethod(ibitmapT);
                object? handle = tryCast.Invoke(null, new object?[] { locked });
                Assert.NotNull(handle);
                var refHandleT = handle.GetType();
                object? ibitmap = refHandleT.GetMethod("get_Ref")!.Invoke(handle, null);
                Assert.NotNull(ibitmap);
                var ibitmapSize = srcIface.GetProperty("Size")!.GetValue(ibitmap);
                Assert.NotNull(ibitmapSize);
                Assert.Equal(8, (int)sizeT.GetProperty("Width")!.GetValue(ibitmapSize)!);
                var dceT = core.GetType("PaintDotNet.Direct2D1.DeviceContextExtensions");
                Assert.NotNull(dceT);
                var extM = dceT.GetMethods().First(m => m.Name == "CreateSharedBitmap" && m.IsStatic
                    && m.GetParameters().Length == 3
                    && m.GetParameters()[1].ParameterType.FullName == "PaintDotNet.Imaging.IBitmap");
                object? extProps = Activator.CreateInstance(propsT);
                object? extBmp = extM.Invoke(null, new object?[] { ctx, ibitmap, extProps });
                Assert.NotNull(extBmp);
                var extSize = dbIface.GetProperty("PixelSize")!.GetValue(extBmp);
                Assert.NotNull(extSize);
                Assert.Equal(8, (int)sizeT.GetProperty("Width")!.GetValue(extSize)!);
                // COM-style lifetime: the owner holds 1, every granted ref
                // adds one, and the pixels die with the last release (the
                // effect/image ownership-transfer flow depends on this).
                var bmpRefs = extBmp.GetType().GetField("_refs")!;
                Assert.Equal(1, (int)bmpRefs.GetValue(extBmp)!);
                var bmpTry = extBmp.GetType().GetMethod("TryCreateRef")!;
                object?[] bmpArgs = new object?[] { dbIface, null };
                Assert.Equal(true, ((bool?)bmpTry.Invoke(extBmp, bmpArgs))!.Value);
                Assert.Equal(2, (int)bmpRefs.GetValue(extBmp)!);
                extBmp.GetType().GetMethod("Dispose")!.Invoke(extBmp, null);
                Assert.Equal(1, (int)bmpRefs.GetValue(extBmp)!);
                Assert.False((bool)extBmp.GetType().GetMethod("get_IsDisposed")!.Invoke(extBmp, null)!);
                extBmp.GetType().GetMethod("Dispose")!.Invoke(extBmp, null);
                Assert.Equal(0, (int)bmpRefs.GetValue(extBmp)!);
                Assert.True((bool)extBmp.GetType().GetMethod("get_IsDisposed")!.Invoke(extBmp, null)!);
                // End to end, exactly like the wheel: CreateImageFromBitmap
                // runs CreateImpl (tiling, clippers, TryCast, shared upload)
                // over the allocated source. Guard: a bare
                // ReinterpretedBitmapSource is source-only (not IBitmap),
                // so TryCast yields an empty handle there by design.
                object? rHandle = tryCast.Invoke(null, new object?[] { reinterpreted });
                Assert.NotNull(rHandle);
                Assert.False((bool)rHandle.GetType().GetProperty("HasRef")!.GetValue(rHandle)!);
                var fw = alc.LoadFromAssemblyPath(Path.Combine(work, "PaintDotNet.Windows.Framework.dll"));
                var cibM = fw.GetType("PaintDotNet.Direct2D1.DeviceContextExtensions")!
                    .GetMethods().First(m => m.Name == "CreateImageFromBitmap" && m.GetParameters().Length == 4);
                var imgOptsT = fw.GetType("PaintDotNet.Direct2D1.BitmapImageOptions");
                Assert.NotNull(imgOptsT);
                object? img = cibM.Invoke(null, new object?[] { ctx, locked, null, Enum.ToObject(imgOptsT, 0) });
                Assert.NotNull(img);
                // BitmapImage carries no Size property: its wrapped tile
                // (the `image` field) must be the 8x8 upload.
                var inner = img.GetType().GetField("image", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(img);
                Assert.NotNull(inner);
                var innerSize = dbIface.GetProperty("PixelSize")!.GetValue(inner);
                Assert.NotNull(innerSize);
                Assert.Equal(8, (int)sizeT.GetProperty("Width")!.GetValue(innerSize)!);
                Assert.Equal(8, (int)sizeT.GetProperty("Height")!.GetValue(innerSize)!);
                // Multi-tile (wheel-sized): exercises sub-rect locks, the
                // tiled-effect ownership flow, and the TiledImagesEffect
                // path that single-tile skips.
                object? big = alloc.Invoke(null, new object?[] { sizeCtor.Invoke(new object?[] { 300, 300 }), Enum.ToObject(optsT, 0) });
                Assert.NotNull(big);
                object? bigImg = cibM.Invoke(null, new object?[] { ctx, big, null, Enum.ToObject(imgOptsT, 0) });
                Assert.NotNull(bigImg);
            }
            finally { alc.Unload(); }
        }
        finally
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); Directory.Delete(work, true); } catch { }
        }
    }
    [Fact]
    public void Rewrite_WheelBitmap_Empty_Source()
    {
        // Browser parity probe: an unsized (0x0) control bitmap must fail
        // the same way it does in the browser, so a layout cause reads as
        // layout instead of a support gap. The wheel allocates from its
        // ClientRectangle; when that is empty, tiling uploads a 0-size
        // lock and the upload reports it. Regression anchor for
        // NotSupported | CreateSharedBitmap | ... (empty).
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string forkDll = ShimBinDll("WfWasm.WinForms");
        string drawingDll = ShimBinDll("WfWasm.Drawing");
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string handmap = TestPaths.Repo(@"src\WfWasm.Rewrite.Tool\handmap.paint.net.txt");
        string[] closure = new[] { "PaintDotNet.Primitives.dll", "PaintDotNet.ComponentModel.dll", "PaintDotNet.Windows.Core.dll", "PaintDotNet.Windows.dll", "PaintDotNet.Windows.Framework.dll" };
        foreach (string f in closure.Concat(new[] { forkDll, drawingDll, cpuDll, handmap }))
            if (!File.Exists(f.StartsWith("PaintDotNet") ? Path.Combine(paintDir, f) : f)) return;
        string work = Path.Combine(Path.GetTempPath(), "WheelEmpty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var assemblyMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = forkDll,
                ["System.Windows.Forms.Primitives"] = forkDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            foreach (string f in closure)
            {
                string input = Path.Combine(paintDir, f);
                var typeMap = WfWasm.Rewrite.GapAnalyzer.AutoMap(input, assemblyMap);
                foreach (string line in File.ReadAllLines(handmap))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                        continue;
                    string[] sides = trimmed.Split('=', 2);
                    if (sides.Length != 2)
                        continue;
                    string[] target = sides[1].Split(':', 2);
                    if (target.Length != 2)
                        continue;
                    typeMap[sides[0]] = (target[0], target[1]);
                }

                AssemblyRewriter.Rewrite(input, Path.Combine(work, f), assemblyMap, typeMap, cpuDll);
            }

            var alc = new System.Runtime.Loader.AssemblyLoadContext($"WheelEmpty_{Guid.NewGuid()}", true);
            try
            {
                string[] dirs = new[] { work, paintDir, Path.GetDirectoryName(forkDll)!, Path.GetDirectoryName(drawingDll)!, Path.GetDirectoryName(cpuDll)! };
                alc.Resolving += (ctx, name) =>
                {
                    foreach (string d in dirs)
                    {
                        string probe = Path.Combine(d, (name.Name ?? "") + ".dll");
                        if (File.Exists(probe))
                            return ctx.LoadFromAssemblyPath(probe);
                    }

                    return null;
                };
                var core = alc.LoadFromAssemblyPath(Path.Combine(work, "PaintDotNet.Windows.Core.dll"));
                var prims = alc.LoadFromAssemblyPath(Path.Combine(work, "PaintDotNet.Primitives.dll"));
                var wins = alc.LoadFromAssemblyPath(Path.Combine(work, "PaintDotNet.Windows.dll"));
                var fw = alc.LoadFromAssemblyPath(Path.Combine(work, "PaintDotNet.Windows.Framework.dll"));
                var bgraT = prims.GetType("PaintDotNet.Imaging.ColorBgra32");
                Assert.NotNull(bgraT);
                var sizeT = prims.GetType("PaintDotNet.Rendering.SizeInt32") ?? core.GetType("PaintDotNet.Rendering.SizeInt32");
                Assert.NotNull(sizeT);
                var sizeCtor = sizeT.GetConstructor(new[] { typeof(int), typeof(int) });
                Assert.NotNull(sizeCtor);
                var optsT = wins.GetType("PaintDotNet.Imaging.BitmapAllocationOptions");
                Assert.NotNull(optsT);
                var allocatorT = core.GetType("PaintDotNet.Imaging.BitmapAllocator");
                Assert.NotNull(allocatorT);
                var allocM = allocatorT.GetMethods().First(m => m.Name == "Allocate" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                var alloc = allocM.MakeGenericMethod(bgraT);
                object? empty = alloc.Invoke(null, new object?[] { sizeCtor.Invoke(new object?[] { 0, 0 }), Enum.ToObject(optsT, 0) });
                Assert.NotNull(empty);
                var ctxT = core.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
                Assert.NotNull(ctxT);
                object? ctx = Activator.CreateInstance(ctxT, new object?[] { 0L });
                Assert.NotNull(ctx);
                var cibM = fw.GetType("PaintDotNet.Direct2D1.DeviceContextExtensions")!
                    .GetMethods().First(m => m.Name == "CreateImageFromBitmap" && m.GetParameters().Length == 4);
                var imgOptsT = fw.GetType("PaintDotNet.Direct2D1.BitmapImageOptions");
                Assert.NotNull(imgOptsT);
                var thrown = Assert.ThrowsAny<Exception>(() =>
                    cibM.Invoke(null, new object?[] { ctx, empty, null, Enum.ToObject(imgOptsT, 0) }));
                Exception inner = thrown is System.Reflection.TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException : thrown;
                Assert.IsType<System.NotSupportedException>(inner);
                Assert.Contains("(empty)", inner.Message);
            }
            finally { alc.Unload(); }
        }
        finally
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); Directory.Delete(work, true); } catch { }
        }
    }
    [Fact]
    public void Rewrite_Reinterpret_Answers_Source()
    {
        // BitmapImage.CreateReinterpretedBitmap wraps the source in a
        // COM-flavored ref whose inner comes back null headless, so the
        // wrapper faults on every later Size/Lock (color wheel). Its only
        // caller passes a same-bits-per-pixel format, so the source is
        // answered directly. Regression: NRE | BitmapExtensions.LockFast.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Framework.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Fw_Reinterpret_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var img = asm.MainModule.GetType("PaintDotNet.Direct2D1.BitmapImage");
            Assert.NotNull(img);
            var reinterpret = img.Methods.FirstOrDefault(m => m.Name == "CreateReinterpretedBitmap"
                && m.Parameters.Count == 2
                && m.ReturnType.FullName == "PaintDotNet.Imaging.IBitmapSource");
            Assert.NotNull(reinterpret);
            var ops = reinterpret.Body.Instructions.Select(i => i.OpCode).ToList();
            Assert.Equal(new[] { Mono.Cecil.Cil.OpCodes.Ldarg_0, Mono.Cecil.Cil.OpCodes.Ret }, ops);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void Rewrite_PostProcess_Answers_Image()
    {
        // BitmapImage.PostProcessImage tails into EnsurePrecision +
        // PremultiplyEffect whose factory (CreateEffect) has no headless
        // backend, so no device image could ever complete (black canvas).
        // Precision/premultiply are device concerns; the image is
        // answered as-is. Regression: NotSupported | CreateEffect via
        // PostProcessImage <- ProcessImage <- CreateImpl.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Framework.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Fw_PostProcess_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var img = asm.MainModule.GetType("PaintDotNet.Direct2D1.BitmapImage");
            Assert.NotNull(img);
            var post = img.Methods.FirstOrDefault(m => m.Name == "PostProcessImage"
                && m.Parameters.Count == 4
                && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Direct2D1.IDeviceImage"
                && m.ReturnType.FullName == "PaintDotNet.Direct2D1.IDeviceImage");
            Assert.NotNull(post);
            var ops = post.Body.Instructions.Select(i => i.OpCode).ToList();
            Assert.Equal(new[] { Mono.Cecil.Cil.OpCodes.Ldarg_1, Mono.Cecil.Cil.OpCodes.Ret }, ops);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void Rewrite_ComObjectRef_Answers()
    {
        // BitmapImage/BufferedImage ctors demand the IComObjectRef token
        // for their image (real COM only exists on Windows). The CPU
        // stand-in roots the device object and answers E_NOINTERFACE from
        // QueryInterface, so lifetime plumbing works and native use fails
        // loudly. Regression: ObjectDisposed | CreateRefError from
        // BitmapImage..ctor <- CreateImpl <- CreateImageFromBitmap.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_ComRef_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var com = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuComObjectRef");
            Assert.NotNull(com);
            Assert.Contains(com.Interfaces, i => i.InterfaceType.FullName == "PaintDotNet.Interop.IComObjectRef");
            Assert.NotNull(com.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.ComponentModel.IObjectRef"));
            var qi = com.Methods.FirstOrDefault(m => m.Name == "QueryInterface" && m.Parameters.Count == 2);
            Assert.NotNull(qi);
            var qiOps = qi.Body.Instructions.Select(i => i.OpCode).ToList();
            Assert.Contains(Mono.Cecil.Cil.OpCodes.Stind_I, qiOps);
            Assert.Contains(com.Methods, m => m.Name == "Dispose" && m.Parameters.Count == 0);
            Assert.Contains(com.Methods, m => m.Name == "get_IsDisposed" && m.Parameters.Count == 0);
            // The bitmap grants the stand-in (borrowed: the image's own
            // ref owns the pixels).
            var bmp = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDeviceBitmap");
            Assert.NotNull(bmp);
            Assert.NotNull(bmp.Fields.FirstOrDefault(f => f.Name == "_refs"));
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void Rewrite_SharedBitmap_Uploads_Lock()
    {
        // CpuDrawingContext.CreateSharedBitmap uploads the lock's live
        // buffer into a CPU device bitmap (the extension disposes the
        // lock in finally, so pixels are copied). Only 32-bit locks
        // upload; anything else keeps the loud NotSupported.
        // Regression: NotSupported | CpuDrawingContext.CreateSharedBitmap.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_Shared_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var shared = ctx.Methods.FirstOrDefault(m => m.Name == "CreateSharedBitmap"
                && m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapLock");
            Assert.NotNull(shared);
            var instrs = shared.Body.Instructions.ToList();
            Assert.Contains(instrs, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "CreateBitmap" && mr.Parameters.Count == 4);
            Assert.Contains(instrs, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference nm && nm.DeclaringType.Name == "CpuDeviceBitmap");
            Assert.Contains(instrs, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret);
            AssertCalleesResolve(asm.MainModule, shared);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void RewriteToBytes_Matches_Rewrite_File()
    {
        // The on-demand server serves RAM-only bytes: the twin must
        // emit exactly what the file-based Rewrite writes.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_BytesTwin_{Guid.NewGuid()}.dll");
        try
        {
            var empty = new Dictionary<string, string>(StringComparer.Ordinal);
            var emptyMap = new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal);
            AssemblyRewriter.Rewrite(input, tempOut, empty, emptyMap, cpuDll);
            byte[] fromBytes = AssemblyRewriter.RewriteToBytes(input, empty, emptyMap, cpuDll);
            Assert.Equal(File.ReadAllBytes(tempOut), fromBytes);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void Rewrite_BundledPluginLoader_ToleratesMissingPlugins()
    {
        // The four bundled codec plugins (Avif/DDS/JpegXL/WebP) need
        // native sidecars that cannot load in the browser, so their
        // registry factories always throw during discovery
        // (DllNotFoundException per dialog upstream). PdnFileTypes
        // routes every factory through
        // FileTypeHelpers.TryCreateWithErrorReporting, which treats a
        // null factory result as "no plugin" with no report. The
        // rewrite answers null from those four factory lambdas, so the
        // plugins are skipped with zero throws and no try/catch is
        // emitted at all. Besides the four direct factories there are
        // wrapper lambdas with the same return type (they delegate to
        // the helper); only the direct factories are asserted
        // structurally, while behavior covers every overload.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "paintdotnet.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Pdn_PluginTol_{Guid.NewGuid()}.dll");
        string[] plugins = new[]
        {
            "PaintDotNet.Data.AvifFileType",
            "PaintDotNet.Data.DdsFileType",
            "PaintDotNet.Data.JpegXLFileType",
            "PaintDotNet.Data.WebPFileType",
        };
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            // Structural: the four direct factories answer null. Shape:
            // 0-arg lambda under PdnFileTypes returning the plugin type
            // whose body is exactly ldnull/ret.
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var found = new List<string>();
            foreach (var top in asm.MainModule.Types)
            {
                foreach (var nested in top.NestedTypes)
                {
                    if (nested.DeclaringType is null || nested.DeclaringType.FullName != "PaintDotNet.Data.PdnFileTypes")
                        continue;
                    foreach (var m in nested.Methods)
                    {
                        if (!m.HasBody || m.Parameters.Count != 0 || !plugins.Contains(m.ReturnType.FullName))
                            continue;
                        var ops = m.Body.Instructions.Select(i => i.OpCode).ToList();
                        if (ops.Count == 2
                            && ops[0] == Mono.Cecil.Cil.OpCodes.Ldnull
                            && ops[1] == Mono.Cecil.Cil.OpCodes.Ret)
                        {
                            Assert.Empty(m.Body.ExceptionHandlers);
                            AssertCalleesResolve(asm.MainModule, m);
                            found.Add(m.ReturnType.FullName);
                        }
                    }
                }
            }
            Assert.Equal(4, found.Count);
            Assert.Equal(4, found.Distinct().Count());
            // Behavioral, pristine: each direct factory constructs, which
            // faults with no plugin environment. Rewritten: every
            // overload is silent and null (direct factories nulled, the
            // wrappers delegate to the error-reporting helper, which
            // honors the null factory result without reporting).
            foreach (string plugin in plugins)
            {
                var pristine = ProbePluginFactories(input, paintDir, plugin);
                Assert.True(pristine.Threw > 0, $"{plugin}: pristine factories silent?");
                var fixed_ = ProbePluginFactories(tempOut, paintDir, plugin);
                Assert.Equal(0, fixed_.Threw);
                Assert.True(fixed_.Nulled > 0, $"{plugin}: no nulled factory?");
            }
        }
        finally
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Rewrite_ColorProfile_Returns_CpuDeviceColorContext()
    {
        // Color controls resolve their ambient sRGB/scRGB profile
        // through the resource cache: the CPU backend answers a
        // CpuDeviceColorContext token carrying the profile's space
        // (CPU blending is identity in sRGB; downstream SetValue
        // writes stay inert). Regression: NotSupported |
        // GetCachedOrCreateResource for IColorProfileProxy from
        // ColorRectangleControl.OnRenderImpl,
        // ColorGradientControl.OnRenderAdvanced,
        // ColorPaletteControl.OnRenderAdvanced.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_ColorProfile_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var resCache = ctx.Methods.First(m => m.Name == "GetCachedOrCreateResource");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string?)i.Operand == "PaintDotNet.Imaging.Proxies.IColorProfileProxy");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuDeviceColorContext");
            AssertCalleesResolve(asm.MainModule, resCache);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Rewrite_LinearGradientBrush_Fills_On_Cpu()
    {
        // The color gradient control fills through a
        // LinearGradientBrush provider: the cache samples its stops
        // once per version into a CpuPaint LUT and answers a
        // CpuLinearGradientBrush resource, which FillRectangle stamps
        // per pixel. Regression: NotSupported |
        // GetCachedOrCreateResource for LinearGradientBrush from
        // ColorGradientControl.OnRenderAdvanced via FillRectangle.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_Gradient_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var grad = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuLinearGradientBrush");
            Assert.NotNull(grad);
            Assert.Contains(grad.Interfaces,
                i => i.InterfaceType.FullName == "PaintDotNet.Direct2D1.ILinearGradientBrush");
            foreach (string field in new[] { "_lut", "_x0", "_y0", "_x1", "_y1", "_opacity", "_spread" })
                Assert.Contains(grad.Fields, f => f.Name == field);
            var gradCtor = Assert.Single(grad.Methods, m => m.Name == ".ctor");
            Assert.Equal(7, gradCtor.Parameters.Count);
            Assert.Contains(grad.Methods, m => m.Name == "get_StartPoint" && m.Parameters.Count == 0);
            Assert.Contains(grad.Methods, m => m.Name == "get_EndPoint" && m.Parameters.Count == 0);
            AssertCalleesResolve(asm.MainModule, gradCtor);
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var resCache = ctx.Methods.First(m => m.Name == "GetCachedOrCreateResource");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string?)i.Operand == "PaintDotNet.UI.Media.LinearGradientBrush");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuLinearGradientBrush");
            Assert.Contains(resCache.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "CreateGradientLut");
            AssertCalleesResolve(asm.MainModule, resCache);
            var ext = asm.MainModule.GetType("PaintDotNet.Direct2D1.DrawingContextExtensions");
            Assert.NotNull(ext);
            var fillRect = Assert.Single(ext.Methods,
                m => m.Name == "FillRectangle" && m.Parameters.Count == 3
                    && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Rendering.RectDouble");
            Assert.Contains(fillRect.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                && mr.Name == "FillRectGradient");
            AssertCalleesResolve(asm.MainModule, fillRect);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Rewrite_EffectInputAccessor_Set_Avoids_Generic_Ref()
    {
        // Palette creation wires its crop input through
        // EffectInputAccessor.Set, which resolves storage through the
        // generic COM-ref helper
        // (TryCastOrCreateRefUnsafe<IDeviceEffect>): on the browser
        // runtime that resolution aborts the process (class.c
        // assertion, uncatchable). The rewrite reroutes through the
        // non-generic TryCreateRef (CPU stand-in for live objects,
        // skipped when unclaimable), so no generic token is built or
        // resolved here. Regression: class.c:2324 abort in
        // ColorPaletteControl.OnDeviceContextCreated <- Set.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Core_InputSet_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var acc = asm.MainModule.GetType("PaintDotNet.Direct2D1.Effects.EffectInputAccessor");
            Assert.NotNull(acc);
            var set = acc.Methods.First(m => m.Name == "Set" && m.Parameters.Count == 2);
            Assert.Empty(set.Body.ExceptionHandlers);
            Assert.DoesNotContain(set.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "TryCastOrCreateRefUnsafe");
            Assert.DoesNotContain(set.Body.Instructions, i =>
                i.Operand is Mono.Cecil.GenericInstanceMethod);
            Assert.Contains(set.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "TryCreateRef"
                && mr.Parameters.Count == 2);
            Assert.Contains(set.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt
                && i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "SetInput"
                && mr.Parameters.Count == 3);
            AssertCalleesResolve(asm.MainModule, set);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    private sealed record FactoryProbe(int Threw, int Nulled);

    // Invokes every 0-arg registry lambda returning one plugin type:
    // counts faults vs silent nulls. Direct factories fault pristine
    // (construction hits the missing plugin environment); the helper
    // wrappers may fault or swallow depending on their error-service
    // path, so only the direct-factory fault is required pristine.
    private static FactoryProbe ProbePluginFactories(string dllPath, string paintDir, string pluginFullName)
    {
        var alc = new System.Runtime.Loader.AssemblyLoadContext($"PluginTol_{Guid.NewGuid()}", true);
        try
        {
            alc.Resolving += (ctx, name) =>
            {
                string probe = Path.Combine(paintDir, (name.Name ?? "") + ".dll");
                return File.Exists(probe) ? ctx.LoadFromAssemblyPath(probe) : null;
            };
            var loaded = alc.LoadFromAssemblyPath(dllPath);
            var registry = loaded.GetType("PaintDotNet.Data.PdnFileTypes");
            Assert.NotNull(registry);
            // Production always supplies a host; the probe mirrors that
            // so the helper wrappers exercise their real path (a null
            // host would fault inside the helper, not the factory).
            var hostType = loaded.GetType("PaintDotNet.Data.FileTypeHost");
            Assert.NotNull(hostType);
            object? host = Activator.CreateInstance(hostType);
            Assert.NotNull(host);
            int threw = 0, nulled = 0, seen = 0;
            foreach (var nested in registry.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                foreach (var m in nested.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (m.ReturnType.FullName != pluginFullName || m.GetParameters().Length != 0)
                        continue;
                    seen++;
                    object owner = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(nested);
                    nested.GetField("host", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(owner, host);
                    try
                    {
                        object? result = m.Invoke(owner, null);
                        if (result is null)
                            nulled++;
                        else
                            threw++;
                    }
                    catch (TargetInvocationException) { threw++; }
                }
            }
            Assert.True(seen > 0, $"{pluginFullName}: no factory overloads?");
            return new FactoryProbe(threw, nulled);
        }
        finally { alc.Unload(); }
    }
    [Fact]
    public void Rewrite_Taskbar_Answers_Dummy_And_Null_Hwnd()
    {
        // The Win7 taskbar COM object cannot exist headless: its
        // factory NREs (caught, but first-chance noise per access)
        // and the main-window hwnd lookup NREs unguarded, escaping
        // TaskProgressDialog.OnShown and aborting every effect run.
        // The rewrite answers PDN's own DummyImpl directly plus a
        // null hwnd.
        // Regression: NullReferenceException | TaskbarList.CreateInstance
        // and NullReferenceException | TaskProgressDialog.OnShown from
        // Levels via EffectMenuBase.RunEffectImpl.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Windows.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Windows_Taskbar_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var taskbar = asm.MainModule.GetType("PaintDotNet.Taskbar.TaskbarList");
            Assert.NotNull(taskbar);
            var getter = Assert.Single(taskbar.Methods,
                m => m.Name == "get_Instance" && m.IsStatic && m.Parameters.Count == 0);
            Assert.DoesNotContain(getter.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr
                && mr.Name == "CreateInstance");
            Assert.Contains(getter.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "PaintDotNet.Taskbar.TaskbarList/DummyImpl");
            AssertCalleesResolve(asm.MainModule, getter);
            var ext = asm.MainModule.GetType("PaintDotNet.Taskbar.TaskbarListExtensions");
            Assert.NotNull(ext);
            var hwnd = Assert.Single(ext.Methods,
                m => m.Name == "GetMainWindowHwnd" && m.IsStatic && m.Parameters.Count == 0);
            Assert.DoesNotContain(hwnd.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "System.Diagnostics.Process");
            Assert.Contains(hwnd.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Conv_I);
            AssertCalleesResolve(asm.MainModule, hwnd);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
    [Fact]
    public void Rewrite_VerifyOutputPixelFormat_Uses_Cpu_PixelInfo()
    {
        // Effect init validated through WIC per-format info (null
        // factory headless -> NRE inside a static cctor -> no
        // Adjustments/Effects dialog can ever open). The rewrite
        // ports the check onto PDN's CPU table
        // (PixelFormats.TryGetPixelInfo -> IPixelInfo).
        // Regression: NullReferenceException |
        // PixelFormatInfo.TryGetRef from Levels via
        // GpuEffect.InitializeRenderInfo.
        string paintDir = TestPaths.Repo(@"targets\paint.net");
        string input = Path.Combine(paintDir, "PaintDotNet.Effects.Core.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"EffectsCore_Verify_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(paintDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var render = asm.MainModule.GetType("PaintDotNet.Effects.ReadOnlyBitmapEffectRenderInfo");
            Assert.NotNull(render);
            var verify = Assert.Single(render.Methods,
                m => m.Name == "VerifyOutputPixelFormat" && m.IsStatic && m.Parameters.Count == 1);
            Assert.Contains(verify.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && i.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "PaintDotNet.Imaging.PixelFormats"
                && mr.Name == "TryGetPixelInfo");
            Assert.DoesNotContain(verify.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr
                && mr.Name == "TryGetInfoNoAddRef");
            Assert.Contains(verify.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string?)i.Operand == "Indexed pixel formats are not supported");
            Assert.Contains(verify.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr
                && (string?)i.Operand == " is not a supported pixel format");
            // Balanced-branch shape: the enum parks in a local and every
            // compare reloads it (dup+beq leaves the taken path one slot
            // deeper -> InvalidProgramException at effect-init time).
            Assert.Equal(2, verify.Body.Variables.Count);
            Assert.DoesNotContain(verify.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Dup);
            AssertCalleesResolve(asm.MainModule, verify);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }



    [Fact]
    public void Rewrite_Telerik_FontResourceEx_Answers_OneFont()
    {
        // Telerik's ThemeResolutionService static init loads embedded
        // icon fonts through a private gdi32 extern
        // (AddFontMemResourceEx), then reads the *pcFonts out-param
        // into AddMemoryFont. Headless has no GDI, so the rewrite
        // stubs the extern: "one font" in the out-param, null handle
        // returned, the managed PrivateFontCollection path proceeds.
        // Regression: TypeInitializationException |
        // ThemeResolutionService..cctor (inner DllNotFoundException:
        // gdi32.dll) on the first Telerik demo form-create.
        string telerikDir = TestPaths.Repo(@"telerik\Telerik UI for WinForms demos\Bin\Release");
        string input = Path.Combine(telerikDir, "Telerik.WinControls.dll");
        if (!File.Exists(input)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"Telerik_FontEx_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(input, map);
            AssemblyRewriter.Rewrite(input, tempOut, map, typeMap, cpuDll);
            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(telerikDir);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var svc = asm.MainModule.GetType("Telerik.WinControls.ThemeResolutionService");
            Assert.NotNull(svc);
            var ext = Assert.Single(svc.Methods,
                m => m.Name == "AddFontMemResourceEx");
            Assert.False(ext.IsPInvokeImpl);
            Assert.Null(ext.PInvokeInfo);
            Assert.Contains(ext.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_3);
            Assert.Contains(ext.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Stind_I4);
            Assert.Contains(ext.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Conv_I);
            AssertCalleesResolve(asm.MainModule, ext);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void StubPInvokes_IsQuery_Predicates_Answer_False()
    {
        // Window-query predicates (IsZoomed, IsWindowVisible, ...) must
        // answer false headless: no native window exists, so every query
        // is negative. Success codes (ReleaseDC et al.) keep the true
        // default. Regression: DllNotFoundException: user32.dll |
        // ThemedFormBehavior.get_IsMaximized aborting the first Telerik
        // demo's layout (IsZoomed stayed a live import).
        string input = Path.Combine(Path.GetTempPath(), "isquery.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(FixtureDll()));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var modRef = new Mono.Cecil.ModuleReference("user32.dll");
            module.ModuleReferences.Add(modRef);
            var native = new Mono.Cecil.TypeDefinition(
                "PinvokeProbe", "Native",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(native);
            var zoomed = new Mono.Cecil.MethodDefinition(
                "IsZoomed",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.PInvokeImpl,
                module.TypeSystem.Boolean);
            zoomed.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.IntPtr));
            zoomed.PInvokeInfo = new Mono.Cecil.PInvokeInfo(
                Mono.Cecil.PInvokeAttributes.CallConvWinapi, "IsZoomed", modRef);
            native.Methods.Add(zoomed);
            var release = new Mono.Cecil.MethodDefinition(
                "ReleaseDC",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.PInvokeImpl,
                module.TypeSystem.Boolean);
            release.PInvokeInfo = new Mono.Cecil.PInvokeInfo(
                Mono.Cecil.PInvokeAttributes.CallConvWinapi, "ReleaseDC", modRef);
            native.Methods.Add(release);
            asm.Write(input);
        }

        string output = Path.Combine(Path.GetTempPath(), "isquery.stubbed.dll");
        AssemblyRewriter.StubPInvokes(input, output);

        using var stubbed = Mono.Cecil.AssemblyDefinition.ReadAssembly(output);
        var methods = stubbed.MainModule.GetTypes()
            .First(t => t.FullName == "PinvokeProbe.Native").Methods;
        var zoom = methods.First(m => m.Name == "IsZoomed");
        Assert.False(zoom.IsPInvokeImpl);
        Assert.Null(zoom.PInvokeInfo);
        Assert.Contains(zoom.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
        Assert.DoesNotContain(zoom.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
        var rel = methods.First(m => m.Name == "ReleaseDC");
        Assert.False(rel.IsPInvokeImpl);
        Assert.Contains(rel.Body.Instructions, i =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
    }

    [Fact]
    public void Rewrite_TelerikCommon_IsZoomed_Answers_False()
    {
        // The full rewrite must stub the natives it keeps: TelerikCommon
        // ships IsZoomed(user32) live, and RadForm layout calls it during
        // form-create, so the first call kills boot headless. The stub
        // answers false (same query rule as the file pass).
        // Regression: DllNotFoundException: user32.dll |
        // ThemedFormBehavior.get_IsMaximized on telerik-photo.
        string telerikDir = TestPaths.Repo(@"telerik\Telerik UI for WinForms demos\Bin\Release");
        string input = Path.Combine(telerikDir, "TelerikCommon.dll");
        if (!File.Exists(input)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"TelerikCommon_IsZoomed_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(input, map);
            AssemblyRewriter.Rewrite(input, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var native = asm.MainModule.GetType("Telerik.WinControls.NativeMethods");
            Assert.NotNull(native);
            var zoom = Assert.Single(native.Methods, m => m.Name == "IsZoomed");
            Assert.False(zoom.IsPInvokeImpl);
            Assert.Null(zoom.PInvokeInfo);
            Assert.Contains(zoom.Body.Instructions, i =>
                i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

}