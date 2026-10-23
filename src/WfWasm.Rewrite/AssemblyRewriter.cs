using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace WfWasm.Rewrite;

/// <summary>
/// Rewrites desktop assemblies for the headless browser host: framework
/// references remap onto the managed fork, native imports stub out, and
/// per-assembly Ensure* passes neutralize device-bound factories.
/// </summary>
public static class AssemblyRewriter
{

    /// <summary>
    /// Rewrites <paramref name="inputPath"/> to <paramref name="outputPath"/>
    /// with an empty type map.
    /// </summary>
    public static void Rewrite(string inputPath, string outputPath, IReadOnlyDictionary<string, string> assemblyMap)
    {
        Rewrite(inputPath, outputPath, assemblyMap, new Dictionary<string, (string Assembly, string Type)>());
    }

    /// <summary>
    /// Rewrites <paramref name="inputPath"/> to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="assemblyMap">Framework assembly name to replacement file.</param>
    /// <param name="typeMap">Full type name to (assembly, type) target.</param>
    /// <param name="cpuPaintPath">Optional CPU paint helper assembly.</param>
    public static void Rewrite(
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string> assemblyMap,
        IReadOnlyDictionary<string, (string Assembly, string Type)> typeMap,
        string? cpuPaintPath = null)
    {
        File.WriteAllBytes(outputPath, RewriteToBytes(
            inputPath, assemblyMap, typeMap, cpuPaintPath,
            Path.GetExtension(outputPath)));
    }

    // CustomAttribute blobs encode Type values inline, not as TypeRef
    // rows (see call site): walk every attribute in the module and
    // remap blob-embedded TypeReferences with the same map.
    private static void RemapCustomAttributeTypeReferences(
        ModuleDefinition module,
        IReadOnlyDictionary<string, (string Assembly, string Type)> typeMap)
    {
        RemapAttributeList(module, module.Assembly.CustomAttributes, typeMap);
        RemapAttributeList(module, module.CustomAttributes, typeMap);
        RemapTypes(module, module.Types, typeMap);
    }

    private static void RemapTypes(
        ModuleDefinition module,
        Mono.Collections.Generic.Collection<Mono.Cecil.TypeDefinition> types,
        IReadOnlyDictionary<string, (string Assembly, string Type)> typeMap)
    {
        foreach (Mono.Cecil.TypeDefinition type in types)
        {
            RemapAttributeList(module, type.CustomAttributes, typeMap);
            foreach (Mono.Cecil.GenericParameter gp in type.GenericParameters)
                RemapAttributeList(module, gp.CustomAttributes, typeMap);
            foreach (Mono.Cecil.MethodDefinition method in type.Methods)
            {
                RemapAttributeList(module, method.CustomAttributes, typeMap);
                RemapAttributeList(module, method.MethodReturnType.CustomAttributes, typeMap);
                foreach (Mono.Cecil.ParameterDefinition p in method.Parameters)
                    RemapAttributeList(module, p.CustomAttributes, typeMap);
                foreach (Mono.Cecil.GenericParameter gp in method.GenericParameters)
                    RemapAttributeList(module, gp.CustomAttributes, typeMap);
            }
            foreach (Mono.Cecil.FieldDefinition field in type.Fields)
                RemapAttributeList(module, field.CustomAttributes, typeMap);
            foreach (Mono.Cecil.PropertyDefinition prop in type.Properties)
                RemapAttributeList(module, prop.CustomAttributes, typeMap);
            foreach (Mono.Cecil.EventDefinition ev in type.Events)
                RemapAttributeList(module, ev.CustomAttributes, typeMap);
            RemapTypes(module, type.NestedTypes, typeMap);
        }
    }

    private static void RemapAttributeList(
        ModuleDefinition module,
        Mono.Collections.Generic.Collection<Mono.Cecil.CustomAttribute> list,
        IReadOnlyDictionary<string, (string Assembly, string Type)> typeMap)
    {
        foreach (Mono.Cecil.CustomAttribute ca in list)
        {
            foreach (Mono.Cecil.CustomAttributeArgument arg in ca.ConstructorArguments)
                RemapAttributeValue(module, arg.Value, typeMap);
            foreach (Mono.Cecil.CustomAttributeNamedArgument named in ca.Properties)
                RemapAttributeValue(module, named.Argument.Value, typeMap);
            foreach (Mono.Cecil.CustomAttributeNamedArgument named in ca.Fields)
                RemapAttributeValue(module, named.Argument.Value, typeMap);
        }
    }

    private static void RemapAttributeValue(
        ModuleDefinition module,
        object? value,
        IReadOnlyDictionary<string, (string Assembly, string Type)> typeMap)
    {
        if (value is TypeReference tr)
        {
            if (tr is TypeSpecification || tr.Scope == module)
                return;
            RemapTypeReference(module, tr, typeMap);
        }
        else if (value is Mono.Cecil.CustomAttributeArgument[] elements)
        {
            foreach (Mono.Cecil.CustomAttributeArgument element in elements)
                RemapAttributeValue(module, element.Value, typeMap);
        }
    }

    // Shared TypeRef remap: point the reference at the mapped fork
    // assembly/type (namespace + nested path split). Used by the TypeRef
    // table loop and the attribute-blob pass below.
    private static void RemapTypeReference(
        ModuleDefinition module,
        TypeReference typeRef,
        IReadOnlyDictionary<string, (string Assembly, string Type)> typeMap)
    {
            string full = typeRef.FullName.Replace('/', '+');
            if (!typeMap.TryGetValue(full, out (string Assembly, string Type) mapped))
                return;
            AssemblyNameReference? scope =
                module.AssemblyReferences.FirstOrDefault(r => r.Name == mapped.Assembly);
            if (scope is null)
            {
                scope = new AssemblyNameReference(mapped.Assembly, new Version(1, 0, 0, 0));
                module.AssemblyReferences.Add(scope);
            }
            typeRef.Scope = scope;
            // Split "Ns.Outer+Inner": the namespace ends at the last '.'
            // before the first '+' (nested TypeRefs carry their parent via
            // DeclaringType, with an empty namespace of their own).
            int plus = mapped.Type.IndexOf('+');
            int dot = plus < 0
                ? mapped.Type.LastIndexOf('.')
                : mapped.Type.LastIndexOf('.', plus);
            // Namespace-less targets (global Interop/WMExtensions nests):
            // the whole name is the nested path, no namespace split.
            string ns = dot < 0 ? "" : mapped.Type[..dot];
            string[] nested = (dot < 0 ? mapped.Type : mapped.Type[(dot + 1)..]).Split('+');
            if (nested.Length == 1)
            {
                typeRef.Namespace = ns;
                typeRef.Name = nested[0];
                typeRef.DeclaringType = null;
            }
            else
            {
                TypeReference parent = new TypeReference(ns, nested[0], module, scope);
                for (int i = 1; i < nested.Length - 1; i++)
                    parent = new TypeReference("", nested[i], module, scope)
                    {
                        DeclaringType = parent,
                    };
                typeRef.Namespace = "";
                typeRef.Name = nested[^1];
                typeRef.DeclaringType = parent;
            }
    }

    public static byte[] RewriteToBytes(
        string inputPath,
        IReadOnlyDictionary<string, string> assemblyMap,
        IReadOnlyDictionary<string, (string Assembly, string Type)> typeMap,
        string? cpuPaintPath = null,
        string? outputExtension = null,
        IEnumerable<string>? extraDirs = null)
    {
        string? inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath));
        var mapDirs = assemblyMap.Values.Distinct(StringComparer.Ordinal).Select(Path.GetDirectoryName).OfType<string>();
        // extraDirs carries the app's resolution closure (lookup dir +
        // search folders): app-local DLLs whose siblings live only in the
        // shared pool (DiagramFirstLook's RadDiagram.dll -> pool UI.dll)
        // otherwise 500 in Cecil with AssemblyResolutionException.
        using var resolver = new FrameworkResolver(inputDir, mapDirs.Concat(extraDirs ?? []));
        using var assembly = AssemblyDefinition.ReadAssembly(inputPath,
            new ReaderParameters { AssemblyResolver = resolver });
        ModuleDefinition module = assembly.MainModule;
        // Some publishers ship with ILOnly cleared (e.g. Paint.NET). The
        // input copy is rewritten, never the original: restoring the flag
        // lets Cecil round-trip the IL; any native sections stay inert.
        module.Attributes |= ModuleAttributes.ILOnly;
        module.Attributes &= ~ModuleAttributes.Required32Bit;
        module.Attributes &= ~ModuleAttributes.Preferred32Bit;
        if ((outputExtension ?? Path.GetExtension(inputPath)).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            module.Kind = ModuleKind.Dll;
        }
        if ((assembly.Name.Name == "PaintDotNet.Windows.Core"
                || assembly.Name.Name == "PaintDotNet.ComponentModel"
                || assembly.Name.Name == "PaintDotNet.Windows")
            && !assembly.CustomAttributes.Any(a =>
                a.AttributeType.FullName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute"
                && a.ConstructorArguments.Count == 1
                && (a.ConstructorArguments[0].Value as string) == "WfWasm.PdnCpu"))
        {
            // The CPU brush backing (WfWasm.PdnCpu) implements PDN's
            // internal drawing/resource/query surface headless
            // (IDrawingContext, IObjectRef/IInternalImpl internals,
            // IDxgiSurface). Friend the helper assembly so those
            // interfaces stay internal to PDN.
            var ivtCtor = module.ImportReference(
                typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute)
                    .GetConstructor([typeof(string)])!);
            var ivt = new Mono.Cecil.CustomAttribute(ivtCtor);
            ivt.ConstructorArguments.Add(new Mono.Cecil.CustomAttributeArgument(
                module.TypeSystem.String, "WfWasm.PdnCpu"));
            assembly.CustomAttributes.Add(ivt);
        }
        if (assembly.Name.Name == "WfWasm.WinForms")
        {
            // RichEdit OLE callback needs COM (absent headless): every
            // RichTextBox handle creation dies in ComHelpers.GetComPointer
            // with a runtime PNS (Syncfusion calendar/diagram boot), and the
            // worker-thread instance escapes to the runtime and OOB-traps the
            // session. Keep only the DragAcceptFiles(false) tail (see rule).
            try { NeutralizeRichEditOleCallback(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // rev77: native EDITSTREAM function-pointer thunks trap
            // the wasm interpreter (diagram RichTextNode boot); both
            // Stream bodies become trampolines to managed WasmRtf.
            try { ReplaceRichTextStreamMethods(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "Telerik.WinControls")
        {
            // Embedded icon fonts register through gdi32's
            // AddFontMemResourceEx during ThemeResolutionService init;
            // headless has no GDI, so the extern answers "one font"
            // and the managed PrivateFontCollection path proceeds.
            try { EnsureTelerikFontResourceExStub(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // Popup animations spawn a worker thread and block the caller
            // in a start-handshake WaitOne; Thread.Start is neutered
            // headless, so grid right-click wedges forever. Animations off:
            // popups show instantly.
            try { EnsureTelerikNoAnimations(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // Layout posts drop headless: RadControl.InvokeLayoutCallback
            // is IsHandleCreated-gated (always false), so TPF measure/
            // arrange queues starve after boot and edited content paints
            // stale layout. Neutralize the handle check; the same-thread
            // BeginInvoke then runs the layout callback inline (rev59).
            try { NeutralizeModuleLayoutHandleGuard(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "Telerik.WinControls.RichTextEditor")
        {
            // The text measure/draw dispatchers ask Uniscribe whether a
            // run needs complex-script shaping; usp10 cannot exist
            // headless and the stubbed table pointer NREs the probe,
            // killing form creation. Answer false (simple text).
            try { EnsureUniscribeHeadlessFalse(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // GDI span painting has no headless DIB backend
            // (DrawTransparentText draws nothing); the sibling
            // DrawGdiPlus path paints through managed GDI+.
            try { RetargetTextBlockGdiPaint(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "paintdotnet")
        {
            // Effect rendering is single-threaded headless
            // (Thread.Start is neutered): driver spawn+WaitOne can
            // never complete, so effect preview/apply silently do
            // nothing. Run the worker inline (see method).
            try { EnsureEffectDriverInline(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // ShowDialog returns None immediately headless, so the
            // run-effect pipeline tears down before OK: note the menu
            // for the dialog (FormClosed resume applies it; see method).
            try { EnsureEffectApplyResume(module, cpuPaintPath); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // Update checks cannot install anything in a browser,
            // and their abandoned state-machine events (3 per boot)
            // finalize-crash the browser PAL at the first gen-2 GC
            // (stroke-allocated pressure trips it ~4 moves in: 3x
            // WaitSubsystem.DeleteHandle fatals). Skip them (see method).
            try { EnsureUpdatesDisabled(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // Session-prefetch Wait kills history undo headless (see method).
            try { NeutralizeSessionPrefetchWait(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // Prefetch continuations never run headless: run them inline.
            try { NeutralizeSessionPrefetchInline(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "PaintDotNet.Windows")
        {
            // The Win7 taskbar COM object cannot exist headless: its
            // factory NREs (caught -> DummyImpl first-chance noise on
            // every access) and the main-window hwnd lookup NREs outside
            // any guard (escapes TaskProgressDialog.OnShown and kills
            // every effect run). Answer PDN's own DummyImpl directly and
            // a null hwnd: identical contract, no native touch.
            try { EnsureCpuTaskbarDummy(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "PaintDotNet.Framework")
        {
            // DWrite text layout is null headless, so every
            // PdnCheckBox measure throws inside layout/show and aborts
            // dialog presentation: measure on the CPU instead (see method).
            try { RedirectPdnCheckBoxMeasure(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // The GPU tile reader dies in its .ctor on the null headless
            // factory: skip just the device span so the CPU tile hook can
            // serve pixels instead (see method).
            try { EnsureBitmapSourceCpuTolerant(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "PaintDotNet.Framework")
        {
            // Null-DWrite heading measurement kills dialog layout headless:
            // answer the font line height (see method).
            try { RedirectHeadingLabelPreferredHeight(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // The CPU resource cache (Windows.Core) unwraps AutoColorSpace
            // through the inner sRGB provider field from another class:
            // cross-class ldfld needs the field visible, so it ships public.
            TypeDefinition? autoUtil = module.GetType("PaintDotNet.Resources.ImageResourceUtil");
            FieldDefinition? autoInner = autoUtil?.NestedTypes
                .FirstOrDefault(n => n.Name == "AutoColorSpaceImageProvider")
                ?.Fields.FirstOrDefault(f => f.Name == "srgbImageProvider");
            if (autoInner is not null)
            {
                autoInner.IsPublic = true;
            }
            // ScaledDeviceImageProvider.source feeds the same CPU unwrap
            // from another class: ship it public too.
            FieldDefinition? scaledSrc = module.GetType("PaintDotNet.Resources.ScaledDeviceImageProvider")
                ?.Fields.FirstOrDefault(f => f.Name == "source");
            if (scaledSrc is not null)
            {
                scaledSrc.IsPublic = true;
            }
        }
        if (assembly.Name.Name == "PaintDotNet.Windows.Framework")
        {
            // The CPU brush backend (Windows.Core) unboxes SystemColor
            // through its conversion operator from another class: the
            // internal struct ships public so cross-class access verifies.
            TypeDefinition? sysColorDef = module.GetType("PaintDotNet.Imaging.SystemColor");
            if (sysColorDef is not null)
            {
                sysColorDef.IsPublic = true;
            }
        }
        if (assembly.Name.Name == "PaintDotNet.Windows.Framework")
        {
            try { EnsureCpuPostProcessIdentity(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // Half device uploads convert through WIC (see method):
            // convert managed instead (same Half bridge as the render).
            try { EnsureThumbnailConsumeManaged(module, cpuPaintPath); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "PaintDotNet.Windows.Core")
        {
            // WIC imaging has no headless factory: clippers and color
            // transforms answer the source (refcounted custody keeps it
            // alive; color work is identity without a device). Tiling
            // and context extensions take the CPU-backed shortcuts.
            try { EnsureCpuImagingShortcuts(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            try { EnsureCpuContextExtensions(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "PaintDotNet.Core")
        {
            // Document colors default through the CPU sRGB context when
            // no color context is supplied (null factory headless).
            try { EnsureCpuColorContextDefault(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "PaintDotNet.Effects.Core")
        {
            // Effect hosting validates its service provider and settings
            // (no WIC factory): EffectServiceProvider..ctor validates
            // non-null, which blocks every effect. Same CPU sRGB
            // stand-in as the ManagedColor factory default.
            try { EnsureEffectServiceProviderDefault(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }

            try { EnsureCpuGpuChoiceSetting(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }

            // The GPU quality default lookup is missing headless (see
            // method): every GPU effect NREs building its property
            // collection. Answer the device-less-desktop default.
            try { EnsureGpuQualitySettingFallback(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }

            // Effect render-info validation resolves through WIC's
            // per-format info (null factory headless): see
            // EnsureCpuVerifyOutputPixelFormat for the CPU-table port.
            try { EnsureCpuVerifyOutputPixelFormat(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }

            // The OK settle lands after ShowDialog already returned
            // (immediate None headless): hook FormClosed so the noted
            // menu reruns the product DoEffect then (see method).
            try { EnsureEffectDialogResume(module, cpuPaintPath); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // GPU effect tiles materialize in OnCopyPixels: serve Levels
            // tiles from the CPU renderer first (see method).
            try { EnsureGpuTileCpuRender(module, cpuPaintPath); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            } }
        if (assembly.Name.Name == "Microsoft.Toolkit.Uwp.Notifications")
        {
            // OS toast-identity bootstrap cannot run headless (COM
            // activation, CLSID registry, AppUserModelId shortcuts):
            // seed the AUMID constant and skip registration; the
            // WinRT stubs answer the notification calls instead.
            try { EnsureToolkitHeadlessInit(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "Syncfusion.Shared.Base")
        {
            // SfForm probes the Win11 build number from the registry at
            // construction; headless the key is missing and int.Parse(null)
            // kills every Syncfusion demo before its form exists.
            try { RedirectSyncfusionWin11Check(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "Syncfusion.Diagram.Base")
        {
            // Headless RichText paint (rev78): the metafile shell cannot
            // exist headless, so RichText nodes default to the managed
            // bitmap path and the native EM_FORMATRANGE Draw becomes a
            // trampoline to WasmRtfDraw (see rules).
            try { ForceDiagramBitmapText(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            try { ReplaceRtfDrawMethod(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "Syncfusion.SfInput.WinForms")
        {
            // TextBoxBase.SelectAll OOB-traps headless once the RichEdit
            // handle exists (calendar date-edit boot, numeric focus and
            // click paths); selection is affordance-only, so drop the
            // void call at every call site (see rule).
            try { NeutralizeTextBoxSelectAllCalls(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // TextBoxBase.get_TextLength traps headless through its generic
            // P/Invoke wrapper (same boot path); answer get_Text().Length
            // instead (see rule).
            try { RedirectTextBoxTextLengthToManaged(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "PaintDotNet.SystemLayer")
        {
            // Shell known folders answer "" headless (no SHGetFolderPath):
            // Save-As, app-data/cache/crash dirs and browse buttons all
            // degrade to the empty path. Answer the /vfs layout (see method).
            try { RedirectShellVirtualPaths(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "PaintDotNet.Data")
        {
            // Persisted per-filetype save tokens need the user settings
            // store (BinaryFormatter), unavailable headless: answer the
            // default token so Save-As reaches its dialog (see method).
            try { RedirectLastSaveConfigToken(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "paintdotnet")
        {
            // Error-dialog environment inventory faults headless and wraps
            // the original failure: degrade it explicitly (see method).
            // Error-dialog environment inventory faults headless and wraps
            // the original failure: degrade it explicitly (see method).
            try { RedirectPdnDiagnosticsFallback(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // WIC codec enumeration is empty headless: answer the desktop
            // extension table for the Microsoft built-ins (see method).
            try { RedirectFileTypeCctorTables(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // Bundled codec natives cannot load in the browser: their
            // registry factories answer null (skipped, no throw) instead
            // of faulting discovery per dialog.
            try { EnsureBundledPluginFactoryNull(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // Undo data must land synchronously headless (see method):
            // the async persist task never completes without a pump.
            try { EnsureHistoryPersistSynchronous(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // Pushed mementos are flushed immediately (see method):
            // headless that eviction loses the only copy (disk persist
            // is stubbed), so every undo IOEs at get_Object.
            try { EnsureHistoryFlushRetained(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // History rows with a non-null icon paint blank headless
            // (see method): text-only rows paint.
            try { EnsureHistoryRowTextOnly(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // The thumbnail render thread never starts headless
            // (Thread.Start is neutered), so layer previews stay
            // blank: drain the render loop inline (see method). The
            // Half bridge it feeds on needs the CPU paint helpers.
            try { EnsureThumbnailManagerInline(module, cpuPaintPath); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
            // One faulting ready-handler must not starve the other
            // strips sharing the drain batch (see method).
            try { EnsureThumbnailDeliveryRobust(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }
        if (assembly.Name.Name == "PaintDotNet.Runtime")
        {
            // Undo reads its memento data through PersistedObject<T>,
            // whose in-memory side is a SoftReference<T> (strong only
            // until the first gen-2 GC, weak after) with a gutted disk
            // fallback (PersistToDisk is a stubbed ret: BinaryFormatter
            // has no headless backend). Once the target promotes, every
            // undo dies at get_Object (IOE "current state"). Pin the
            // target strongly headless (see method): history data then
            // lives exactly as long as its memento on the undo stack.
            try { EnsureHistorySoftReferenceStrong(module); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }

        // Runs LAST among the shape-sensitive rules: it only appends
        // dup + DefuseWaitHandle after event-handle constructions, but
        // the exact-tail pins above (driver spawn, dialog resume) match
        // instruction adjacency and must see pristine code first.
        if (assembly.Name.Name.StartsWith("PaintDotNet", System.StringComparison.OrdinalIgnoreCase)
            || assembly.Name.Name == "paintdotnet")
        {
            // Abandoned kernel event handles finalize-crash the
            // browser PAL at the first gen-2 GC (see method): defuse
            // every event-handle construction in every PDN module.
            try { EnsureWaitHandlesDefused(module, cpuPaintPath); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
            }
        }

        // Native imports cannot bind headless: stub them with the same
        // defaults as the file pass (targeted Ensure* stubs above already
        // cleared PInvokeImpl, so this only touches the remainder).
        StubModulePInvokes(module);

        // Designer InitializeComponent runs inside the reflection-invoked
        // ctor; WASM interp cannot unwind across that boundary, so any
        // uncaught throw aborts the session silently. Guard the body:
        // stash the throw for the host (deferred report + fallback form).
        try { GuardInitializeComponents(module); }
        catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
        {
        }


        foreach (AssemblyNameReference reference in module.AssemblyReferences)
        {
            if (!assemblyMap.TryGetValue(reference.Name, out string? replacement))
                continue;
            using var target = AssemblyDefinition.ReadAssembly(replacement);
            reference.Name = target.Name.Name;
            reference.Version = target.Name.Version;
            reference.PublicKeyToken = Array.Empty<byte>();
        }

        foreach (TypeReference typeRef in module.GetTypeReferences())
        {
            // Skip specifications (their element types appear as own rows)
            // and self-references (the app's own types stay put).
            if (typeRef is TypeSpecification || typeRef.Scope == module)
                continue;
            RemapTypeReference(module, typeRef, typeMap);
        }


        // CustomAttribute blobs encode Type values inline
        // (ELEMENT_TYPE_CLASS + name), not as TypeRef rows, so
        // GetTypeReferences() never sees them: Editor/TypeConverter args
        // kept pointing at the pre-rewrite framework scopes (dangling --
        // the AssemblyRef itself was already renamed). The runtime then
        // fails the attribute bind and Telerik theme deserializer
        // swallows the TypeLoad, dropping the value (RadImageShape
        // .BackgroundShape -> null -> white selected tabs). Remap the
        // blob-embedded TypeReferences with the same map.
        // Regression: RadImageShape EditorAttribute typeof(UITypeEditor)
        // scope System.Drawing 4.0.0.0 <- Telerik theme BackgroundShape.
        RemapCustomAttributeTypeReferences(module, typeMap);
        // CAS permission types are missing from the browser runtime and
        // fatal at load; retarget to the fork stubs (same name, new scope).
        RedirectCasTypeReferences(module);

        // System.Configuration.* never resolves in the browser (netfx
        // scopes); retarget to the shipped ConfigurationManager package.
        RedirectConfigurationTypeReferences(module);

        // System.Data.OleDb.* lives in the netfx System.Data scope, but
        // the browser System.Data has no OleDb types; retarget to the
        // shipped headless stub assembly (Fill/Open degrade to empty).
        RedirectOleDbTypeReferences(module);

        // System.Media.* lives in the netfx System scope, but the
        // browser System facade forwards to System.Windows.Extensions,
        // which is not bundled; retarget to the shipped headless stub
        // assembly (play verbs are silent no-ops).
        RedirectMediaTypeReferences(module);

        // VisualStyleRenderer.DrawBackground is a UxTheme P/Invoke stubbed
        // to S_OK (HDC is null headless), so themed spin arrows vanish and
        // PDN's UpDownButtons keeps its Violet debug fill. Retarget both
        // overloads to the fork's GDI+ spin painter; every other
        // class/part stays a silent no-op inside the helper.
        RedirectSpinDrawBackground(module);

        // ControlPaint.DrawScrollButton classic ends in stubbed
        // DrawFrameControl: retarget both static overloads to the
        // fork's GDI+ scroll painter (PDN Colors spin chips).
        // RE-ENABLED: the kind-17 TLEs reproduce with this hook OFF and
        // with byte-identical app modules (4DDA==258F) and fork (c9sw);
        // victims include types with no rewritten calls; pre-session log
        // rows (10-19 PDN, 10-20 Syncfusion, today 14:38-15:28 PDN) prove
        // the pathology predates and is flaky independent of this rule.
        // The emitted MemberRef fully resolves under Cecil.
        RedirectScrollButtonDraw(module);

        // Cyotek ColorGrid GetColor/GetColorSource/SetColor off-by-one
        // guards (`> count` instead of `>=`): clicking the empty custom
        // well throws IndexOutOfRangeException and the selection never
        // commits. One-opcode ble->blt swap at the guarded sites.
        FixCyotekColorGridBounds(module);

        // DISABLED rev76: the pass retargets LoadLibraryEx/GetModuleHandle to
        // WasmNativeLibrary null-handle stubs, but that type was never shipped
        // in the fork, so every retargeted site TypeLoads (diagram
        // ScrollableControl.get_CreateParams, rev76 probe). The kernel32-OOB
        // theory was never isolated from the UpdateOleCallback invalid-IL
        // (now fixed); unwire until the loader path proves itself a managed
        // failure. The pass method stays for its hermetic tests.
        // RetargetNativeLoaderCalls(module);

        // System.IO.IsolatedStorage.* throws PlatformNotSupported in the
        // browser runtime; retarget to the shipped headless stub
        // assembly (per-assembly in-memory stores, honest FileModes).
        RedirectIsolatedStorageTypeReferences(module);

        // Safely redirect ResourceManager.GetObject to ResourceHelper.SafeGetObject
        // to prevent BinaryFormatter-removed crashes in legacy resx resources on .NET 9+
        MethodReference? safeGetObject1 = null;
        MethodReference? safeGetObject2 = null;
        MethodReference? safeGetString1 = null;
        MethodReference? safeGetString2 = null;
        MethodReference? safeGetStream1 = null;
        MethodReference? safeGetStream2 = null;
        MethodReference? safeGetResourceSet = null;
        MethodReference? envGetFolderPath1 = null;
        MethodReference? envGetFolderPath2 = null;
        MethodReference? safeApplyResources1 = null;
        MethodReference? safeApplyResources2 = null;
        MethodReference? vfsGetFiles1 = null;
        MethodReference? vfsGetFiles2 = null;
        MethodReference? vfsGetFiles3 = null;
        MethodReference? vfsExists = null;
        MethodReference? vfsOpenStream = null;
        MethodReference? vfsCopy2 = null;
        MethodReference? vfsCopy3 = null;
        MethodReference? vfsCreationTime = null;
        MethodReference? vfsReadText1 = null;
        MethodReference? vfsReadText2 = null;
        MethodReference? vfsReadBytes1 = null;
        MethodReference? wasmFileVersionGetInfo = null;
        MethodReference? baseDirStatic = null;
        MethodReference? baseDirInstance = null;
        Mono.Cecil.TypeReference? formsGateType = null;
        Mono.Cecil.TypeReference? formsControlType = null;
        Mono.Cecil.TypeReference? wasmRegistryType = null;

        AssemblyNameReference? drawingScope = module.AssemblyReferences.FirstOrDefault(r => r.Name == "WfWasm.Drawing");
        // Loose-file reads live in data-only class libraries (ERP.Repository
        // reads Data/*.json via AppDomain.BaseDirectory + File.Exists +
        // File.ReadAllText) that never reference the Drawing fork, so a
        // scope-gated VFS leaves their Exists on the real disk and the
        // panes load empty. Reference the fork like formsScope below:
        // the runtime always carries it (the entry closure loads it).
        if (drawingScope is null)
        {
            drawingScope = new AssemblyNameReference("WfWasm.Drawing", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(drawingScope);
        }
        if (drawingScope is not null)
        {
            var rhType = new TypeReference("WfWasm.Drawing", "ResourceHelper", module, drawingScope);
            // NOTE: GetObject retargets to the fork WasmResourceShim (built
            // below once formsScope exists), not to ResourceHelper directly.
            safeApplyResources1 = new MethodReference("SafeApplyResources", module.TypeSystem.Void, rhType)
            {
                HasThis = false,
                Parameters =
                {
                    new ParameterDefinition(module.ImportReference(typeof(System.ComponentModel.ComponentResourceManager))),
                    new ParameterDefinition(module.TypeSystem.Object),
                    new ParameterDefinition(module.TypeSystem.String)
                }
            };
            safeApplyResources2 = new MethodReference("SafeApplyResources", module.TypeSystem.Void, rhType)
            {
                HasThis = false,
                Parameters =
                {
                    new ParameterDefinition(module.ImportReference(typeof(System.ComponentModel.ComponentResourceManager))),
                    new ParameterDefinition(module.TypeSystem.Object),
                    new ParameterDefinition(module.TypeSystem.String),
                    new ParameterDefinition(module.ImportReference(typeof(System.Globalization.CultureInfo)))
                }
            };
        }

        if (drawingScope is not null)
        {
            // Loose-file enumeration has no browser disk: Directory.GetFiles
            // redirects to the AppFileSystem registry (same signatures).
            var vfsType = new TypeReference("WfWasm.Drawing", "AppFileSystem", module, drawingScope);
            var stringArray = new ArrayType(module.TypeSystem.String);
            vfsGetFiles1 = new MethodReference("GetFiles", stringArray, vfsType)
            {
                HasThis = false,
                Parameters = { new ParameterDefinition(module.TypeSystem.String) }
            };
            vfsGetFiles2 = new MethodReference("GetFiles", stringArray, vfsType)
            {
                HasThis = false,
                Parameters =
                {
                    new ParameterDefinition(module.TypeSystem.String),
                    new ParameterDefinition(module.TypeSystem.String)
                }
            };
            vfsGetFiles3 = new MethodReference("GetFiles", stringArray, vfsType)
            {
                HasThis = false,
                Parameters =
                {
                    new ParameterDefinition(module.TypeSystem.String),
                    new ParameterDefinition(module.TypeSystem.String),
                    new ParameterDefinition(module.ImportReference(typeof(System.IO.SearchOption)))
                }
            };
            // Loose data files (BookShelf's BackUp.xml next to the exe)
            // ship as VFS content: Exists/Read/Copy/CreationTime consult
            // the registry first and fall through to the real disk, so
            // cache writes and MEMFS files keep working.
            vfsExists = new MethodReference("Exists", module.TypeSystem.Boolean, vfsType)
            {
                HasThis = false,
                Parameters = { new ParameterDefinition(module.TypeSystem.String) }
            };
            vfsOpenStream = new MethodReference("OpenFileStream",
                module.ImportReference(typeof(System.IO.FileStream)), vfsType)
            {
                HasThis = false,
                Parameters =
                {
                    new ParameterDefinition(module.TypeSystem.String),
                    new ParameterDefinition(module.ImportReference(typeof(System.IO.FileMode)))
                }
            };
            vfsCopy2 = new MethodReference("CopyFile", module.TypeSystem.Void, vfsType)
            {
                HasThis = false,
                Parameters =
                {
                    new ParameterDefinition(module.TypeSystem.String),
                    new ParameterDefinition(module.TypeSystem.String)
                }
            };
            vfsCopy3 = new MethodReference("CopyFile", module.TypeSystem.Void, vfsType)
            {
                HasThis = false,
                Parameters =
                {
                    new ParameterDefinition(module.TypeSystem.String),
                    new ParameterDefinition(module.TypeSystem.String),
                    new ParameterDefinition(module.TypeSystem.Boolean)
                }
            };
            vfsCreationTime = new MethodReference("GetCreationTime",
                module.ImportReference(typeof(System.DateTime)), vfsType)
            {
                HasThis = false,
                Parameters = { new ParameterDefinition(module.TypeSystem.String) }
            };
            // JSON seed reads (ERP.Repository.LoadData: File.Exists, then
            // File.ReadAllText, then JsonConvert): Exists alone would flip
            // a silent skip into a FileNotFound on the backslash path, so
            // the text read follows Exists into the registry.
            vfsReadText1 = new MethodReference("ReadAllText", module.TypeSystem.String, vfsType)
            {
                HasThis = false,
                Parameters = { new ParameterDefinition(module.TypeSystem.String) }
            };
            vfsReadText2 = new MethodReference("ReadAllText", module.TypeSystem.String, vfsType)
            {
                HasThis = false,
                Parameters =
                {
                    new ParameterDefinition(module.TypeSystem.String),
                    new ParameterDefinition(module.ImportReference(typeof(System.Text.Encoding)))
                }
            };
            // Binary twin of the JSON-seed read (ERP InfoControl viewer:
            // RadPdfViewer.LoadDocument(backslash path) -> ReadAllBytes):
            // same registry-first, disk-fallback semantics.
            vfsReadBytes1 = new MethodReference("ReadAllBytes",
                new ArrayType(module.TypeSystem.Byte), vfsType)
            {
                HasThis = false,
                Parameters = { new ParameterDefinition(module.TypeSystem.String) }
            };
        }

        // Headless Invalidate() is a silent no-op (IsHandleCreated is never
        // true), freezing all timer/animation UI after the first frame. The
        // instruction loop below retargets every Control.Invalidate call to
        // the fork's WasmRepaintGate hook (same stack shapes); the canvas
        // host subscribes the gate and pumps a coalesced repaint.
        AssemblyNameReference? formsScope =
            module.AssemblyReferences.FirstOrDefault(r => r.Name == "WfWasm.WinForms");
        if (formsScope is null)
        {
            formsScope = new AssemblyNameReference("WfWasm.WinForms", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(formsScope);
        }

        // Browser BaseDirectory is null/empty (assemblies load from bytes):
        // AppDomain + AppContext getters funnel to WasmAppIdentity, which
        // passes the real value through and falls back to "/app" (the
        // VFS tail-match makes the fallback hit served content). The
        // instance getter keeps its `this` as the shim's object arg (same
        // stack shape as the Invalidate hook below, no Pop needed).
        var identityType = new TypeReference("System.Windows.Forms", "WasmAppIdentity", module, formsScope);
        baseDirStatic = new MethodReference("BaseDirectory", module.TypeSystem.String, identityType)
        {
            HasThis = false,
        };
        baseDirInstance = new MethodReference("BaseDirectory", module.TypeSystem.String, identityType)
        {
            HasThis = false,
            Parameters = { new ParameterDefinition(module.TypeSystem.Object) },
        };
        formsGateType = new TypeReference("System.Windows.Forms", "WasmRepaintGate", module, formsScope);
        formsControlType = new TypeReference("System.Windows.Forms", "Control", module, formsScope);
        wasmRegistryType = new TypeReference("System.Windows.Forms", "WasmRegistry", module, formsScope);
        // The browser framework ships FileVersionInfo as a throwing stub
        // (every member PNSEs, no backing fields). GetVersionInfo and all
        // getters/ToString are redirected to the fork's metadata-backed
        // WasmFileVersion readers (PlatformNotSupportedException aborts
        // OnLoad chains otherwise).
        var wasmFileVersionType = new TypeReference("System.Windows.Forms", "WasmFileVersion", module, formsScope);
        wasmFileVersionGetInfo = new MethodReference("GetVersionInfo",
            module.ImportReference(typeof(System.Diagnostics.FileVersionInfo)), wasmFileVersionType)
        {
            HasThis = false,
            Parameters = { new ParameterDefinition(module.TypeSystem.String) }
        };

        // ResourceManager.GetObject retargets to the fork shim (not the
        // Drawing helper directly): designer ".ImageStream" entries hydrate
        // into real ImageListStreamers there, everything else flows through.
        var shimType = new TypeReference("System.Windows.Forms", "WasmResourceShim", module, formsScope);
        safeGetObject1 = new MethodReference("SafeGetObject", module.TypeSystem.Object, shimType)
        {
            HasThis = false,
            Parameters =
            {
                new ParameterDefinition(module.ImportReference(typeof(System.Resources.ResourceManager))),
                new ParameterDefinition(module.TypeSystem.String)
            }
        };
        safeGetObject2 = new MethodReference("SafeGetObject", module.TypeSystem.Object, shimType)
        {
            HasThis = false,
            Parameters =
            {
                new ParameterDefinition(module.ImportReference(typeof(System.Resources.ResourceManager))),
                new ParameterDefinition(module.TypeSystem.String),
                new ParameterDefinition(module.ImportReference(typeof(System.Globalization.CultureInfo)))
            }
        };

        // ResourceManager/ComponentResourceManager.GetString funnels into
        // GetObject/GetResourceSet, which instantiates the legacy
        // ResourceReader for v1-header streams; vendor .resources that
        // name DeserializingResourceReader then throw NSE out of BCL with
        // no managed catch on the path (native OOB trap via the ctor).
        // Retarget to the fork shim's throw-free SafeGetString. Same for
        // GetStream (BCL would hand back an UnmanagedMemoryStream over a
        // set it cannot even open on those streams).
        safeGetString1 = new MethodReference("SafeGetString", module.TypeSystem.String, shimType)
        {
            HasThis = false,
            Parameters =
            {
                new ParameterDefinition(module.ImportReference(typeof(System.Resources.ResourceManager))),
                new ParameterDefinition(module.TypeSystem.String)
            }
        };
        safeGetString2 = new MethodReference("SafeGetString", module.TypeSystem.String, shimType)
        {
            HasThis = false,
            Parameters =
            {
                new ParameterDefinition(module.ImportReference(typeof(System.Resources.ResourceManager))),
                new ParameterDefinition(module.TypeSystem.String),
                new ParameterDefinition(module.ImportReference(typeof(System.Globalization.CultureInfo)))
            }
        };
        safeGetStream1 = new MethodReference("SafeGetStream", module.ImportReference(typeof(System.IO.Stream)), shimType)
        {
            HasThis = false,
            Parameters =
            {
                new ParameterDefinition(module.ImportReference(typeof(System.Resources.ResourceManager))),
                new ParameterDefinition(module.TypeSystem.String)
            }
        };
        safeGetStream2 = new MethodReference("SafeGetStream", module.ImportReference(typeof(System.IO.Stream)), shimType)
        {
            HasThis = false,
            Parameters =
            {
                new ParameterDefinition(module.ImportReference(typeof(System.Resources.ResourceManager))),
                new ParameterDefinition(module.TypeSystem.String),
                new ParameterDefinition(module.ImportReference(typeof(System.Globalization.CultureInfo)))
            }
        };

        // ResourceManager/ComponentResourceManager.GetResourceSet is the
        // remaining raw BCL read entry (Syncfusion SR wrappers call it in
        // SetResources/GetLocalizedResourceManager): same legacy-reader
        // NSE death on the vendor streams. Retarget to the fork shim's
        // derived-set answer.
        safeGetResourceSet = new MethodReference("SafeGetResourceSet", module.ImportReference(typeof(System.Resources.ResourceSet)), shimType)
        {
            HasThis = false,
            Parameters =
            {
                new ParameterDefinition(module.ImportReference(typeof(System.Resources.ResourceManager))),
                new ParameterDefinition(module.ImportReference(typeof(System.Globalization.CultureInfo))),
                new ParameterDefinition(module.TypeSystem.Boolean),
                new ParameterDefinition(module.TypeSystem.Boolean)
            }
        };

        // Environment.GetFolderPath answers "" for every folder on WASM,
        // which kills Framework-era path builders (Telerik PDF
        // FontsManager..cctor GetParents the System folder and throws on
        // the empty path). Retarget to the fork's virtual layout.
        var envType = new TypeReference("System.Windows.Forms", "WasmEnvironment", module, formsScope);
        envGetFolderPath1 = new MethodReference("GetFolderPath", module.TypeSystem.String, envType)
        {
            HasThis = false,
            Parameters =
            {
                new ParameterDefinition(module.ImportReference(typeof(System.Environment.SpecialFolder)))
            }
        };
        envGetFolderPath2 = new MethodReference("GetFolderPath", module.TypeSystem.String, envType)
        {
            HasThis = false,
            Parameters =
            {
                new ParameterDefinition(module.ImportReference(typeof(System.Environment.SpecialFolder))),
                new ParameterDefinition(module.ImportReference(typeof(System.Environment.SpecialFolderOption)))
            }
        };

        // UIAutomationCore.dll is absent headless: the DllImport behind
        // AutomationInteropProvider.ClientsAreListening throws DllNotFound
        // out of Telerik list adds and Invoke-marshaled UI updates. The
        // instruction loop below retargets the static getter to the fork
        // stub (no client can listen in the browser, so false is exact).
        var uiaType = new TypeReference("System.Windows.Forms", "WasmAutomation", module, formsScope);
        var uiaNoClients = new MethodReference("NoClientsAreListening", module.TypeSystem.Boolean, uiaType)
        {
            HasThis = false,
        };


            var modType = module.Types.FirstOrDefault(t => t.Name == "<Module>");
            if (modType != null)
            {
                var modCctor = modType.Methods.FirstOrDefault(m => m.Name == ".cctor");
                if (modCctor != null && modCctor.HasBody)
                {
                    // Keep same-module initializer calls (compiler-generated ModuleInitializer dispatch:
                    // managed hook wiring like WfWasmInit.Init). Drop calls into external assemblies
                    // (native CRT init etc. crash under WASM). Module cctors are call+ret only.
                    var keep = new List<Instruction>();
                    foreach (var ins in modCctor.Body.Instructions)
                        if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) &&
                            ins.Operand is MethodReference mr &&
                            mr.DeclaringType.Scope == module)
                            keep.Add(ins);
                    modCctor.Body.ExceptionHandlers.Clear();
                    modCctor.Body.Variables.Clear();
                    modCctor.Body.Instructions.Clear();
                    var cil = modCctor.Body.GetILProcessor();
                    foreach (var ins in keep)
                        modCctor.Body.Instructions.Add(cil.Create(ins.OpCode, (MethodReference)ins.Operand));
                    modCctor.Body.Instructions.Add(cil.Create(OpCodes.Ret));
                }
            }

            var colorProfileType = module.Types.FirstOrDefault(t => t.FullName == "PaintDotNet.Imaging.ColorProfile");
            var colorProfilesType = module.Types.FirstOrDefault(t => t.FullName == "PaintDotNet.Imaging.ColorProfiles");
            var iColorProfileType = module.Types.FirstOrDefault(t => t.FullName == "PaintDotNet.Imaging.IColorProfile");

            if (colorProfileType != null && colorProfilesType != null && iColorProfileType != null)
            {
                var emptyCtor = colorProfileType.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0);
                if (emptyCtor == null)
                {
                    var existingCtor = colorProfileType.Methods.FirstOrDefault(m => m.Name == ".ctor");
                    var refTrackedCtor = existingCtor?.Body.Instructions
                        .FirstOrDefault(i => i.OpCode == OpCodes.Call && (i.Operand as MethodReference)?.DeclaringType.Name == "RefTrackedObject")
                        ?.Operand as MethodReference;
                    var resIdGen = existingCtor?.Body.Instructions
                        .FirstOrDefault(i => i.OpCode == OpCodes.Call && (i.Operand as MethodReference)?.DeclaringType.Name == "ResourceID")
                        ?.Operand as MethodReference;
                    var resIdField = colorProfileType.Fields.FirstOrDefault(f => f.Name == "resourceID");

                    if (refTrackedCtor != null && resIdGen != null && resIdField != null)
                    {
                        emptyCtor = new MethodDefinition(".ctor",
                            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                            module.TypeSystem.Void);
                        colorProfileType.Methods.Add(emptyCtor);
                        var cil = emptyCtor.Body.GetILProcessor();
                        cil.Emit(OpCodes.Ldarg_0);
                        cil.Emit(OpCodes.Call, refTrackedCtor);
                        cil.Emit(OpCodes.Ldarg_0);
                        cil.Emit(OpCodes.Call, resIdGen);
                        cil.Emit(OpCodes.Stfld, resIdField);
                        cil.Emit(OpCodes.Ret);
                    }
                }

                var defaultField = colorProfilesType.Fields.FirstOrDefault(f => f.Name == "s_defaultProfile");
                if (defaultField == null)
                {
                    defaultField = new FieldDefinition("s_defaultProfile",
                        FieldAttributes.Public | FieldAttributes.Static,
                        iColorProfileType);
                    colorProfilesType.Fields.Add(defaultField);
                }

                if (emptyCtor != null)
                {
                    var cctor = colorProfilesType.Methods.FirstOrDefault(m => m.Name == ".cctor");
                    if (cctor != null && cctor.HasBody)
                    {
                        var il = cctor.Body.GetILProcessor();
                        cctor.Body.ExceptionHandlers.Clear();
                        cctor.Body.Variables.Clear();
                        cctor.Body.Instructions.Clear();
                        cctor.Body.Instructions.Add(il.Create(OpCodes.Newobj, emptyCtor));
                        cctor.Body.Instructions.Add(il.Create(OpCodes.Stsfld, defaultField));
                        cctor.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }

                    foreach (var method in colorProfilesType.Methods)
                    {
                        if (method.IsStatic && method.Name.StartsWith("get_") && method.ReturnType.FullName == "PaintDotNet.Imaging.IColorProfile" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldsfld, defaultField));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                foreach (var method in colorProfileType.Methods)
                {
                    if (method.Name == "TryLinearize" && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if (method.Name == "CreateDeviceColorContext" && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if ((method.Name == "get_HasRgbTrc" || method.Name == "get_HasRgbXyz" || method.Name == "get_HasTable") && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if (method.Name == "get_Description" && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldstr, "sRGB"));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if (method.Name == "get_ColorSpace" && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_1));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if (method.Name == "get_Flags" && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4, 512));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if (method.Name == "GetColorContext" && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if (method.Name == "get_Matrix" && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        var loc = new VariableDefinition(method.ReturnType);
                        method.Body.Variables.Add(loc);
                        method.Body.InitLocals = true;
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                        method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, loc));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if (method.Name == "get_ProfileBytes" && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        var loc = new VariableDefinition(method.ReturnType);
                        method.Body.Variables.Add(loc);
                        method.Body.InitLocals = true;
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                        method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, loc));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if (method.Name == "get_ProfileBytesBigHash" && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        var loc = new VariableDefinition(method.ReturnType);
                        method.Body.Variables.Add(loc);
                        method.Body.InitLocals = true;
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                        method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, loc));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if (method.Name == "get_ProfileBytesHash" && method.HasBody)
                    {
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                    else if (method.IsStatic && method.Name == "Create" && method.HasBody)
                    {
                        var getSrgb = colorProfilesType.Methods.FirstOrDefault(m => m.Name == "get_Srgb");
                        if (getSrgb != null)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Call, getSrgb));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }
            }

            foreach (TypeDefinition type in module.GetTypes().ToList())
            {
                foreach (var f in type.Fields)
                {
                    if (f.FieldType.FullName.StartsWith("MS.Internal.Text.TextInterface")
                        || f.FieldType.Scope?.Name?.Contains("DirectWrite") == true
                        || f.FieldType.Scope?.Name?.Contains("Microsoft.Windows.SDK.NET") == true)
                    {
                        if (f.FieldType.IsPointer)
                            f.FieldType = module.TypeSystem.IntPtr;
                        else if (f.FieldType.IsArray)
                            f.FieldType = new ArrayType(module.TypeSystem.Object);
                        else
                            f.FieldType = module.TypeSystem.Object;
                    }
                }

                if (type.FullName == "PaintDotNet.Composition.PdnCompositor")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == ".ctor" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            var objectCtor = module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Call, objectCtor));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if ((method.Name == "get_Compositor" || method.Name == "get_DispatcherQueue") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if ((method.Name == "Commit" || method.Name == "ResumeCommit" || method.Name == "SuspendCommit" || method.Name == "OnCompositorControllerCommitNeeded") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.UI.Media.Geometry+GeometryResourceCacheHost"
                    || type.FullName == "PaintDotNet.UI.Media.Geometry/GeometryResourceCacheHost")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == ".ctor" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            var disposableCtor = type.BaseType?.Resolve()?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0);
                            if (disposableCtor != null)
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, module.ImportReference(disposableCtor)));
                            }
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name == "Dispose" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name == "TryCreateServiceRef" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                MethodReference? getEmptyRectDouble = null;
                bool checkedEmptyRectDouble = false;

                if (type.FullName == "PaintDotNet.UI.Media.Geometry"
                    || type.BaseType?.FullName == "PaintDotNet.UI.Media.Geometry"
                    || type.BaseType?.FullName == "PaintDotNet.UI.Media.PathGeometry")
                {
                    if (!checkedEmptyRectDouble)
                    {
                        checkedEmptyRectDouble = true;
                        foreach (var t in module.GetTypes())
                        {
                            foreach (var m in t.Methods)
                            {
                                if (m.HasBody)
                                {
                                    foreach (var inst in m.Body.Instructions)
                                    {
                                        if (inst.OpCode == OpCodes.Call && inst.Operand is MethodReference mr
                                            && mr.DeclaringType.FullName == "PaintDotNet.Rendering.RectDouble"
                                            && mr.Name == "get_Empty")
                                        {
                                            getEmptyRectDouble = mr;
                                            break;
                                        }
                                    }
                                }
                                if (getEmptyRectDouble != null) break;
                            }
                            if (getEmptyRectDouble != null) break;
                        }
                    }

                    foreach (var method in type.Methods)
                    {
                        if ((method.Name == "GetCachedDirect2DGeometry" || method.Name == "CreateDirect2DGeometry") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if ((method.Name == "get_Bounds" || method.Name == "GetRawBounds" || method.Name == "GetRenderBounds") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            if (getEmptyRectDouble != null)
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, getEmptyRectDouble));
                            }
                            else
                            {
                                method.Body.InitLocals = true;
                                var loc = new VariableDefinition(method.ReturnType);
                                method.Body.Variables.Add(loc);
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                                method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, loc));
                            }
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if ((method.Name == "TryGetCachedBounds" || method.Name == "TryGetFastSimpleBoundsTransform") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.InitLocals = true;
                            var loc = new VariableDefinition(method.ReturnType);
                            method.Body.Variables.Add(loc);
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                            method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, loc));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Shapes.CachedGeometry")
                {
                    if (!checkedEmptyRectDouble)
                    {
                        checkedEmptyRectDouble = true;
                        foreach (var t in module.GetTypes())
                        {
                            foreach (var m in t.Methods)
                            {
                                if (m.HasBody)
                                {
                                    foreach (var inst in m.Body.Instructions)
                                    {
                                        if (inst.OpCode == OpCodes.Call && inst.Operand is MethodReference mr
                                            && mr.DeclaringType.FullName == "PaintDotNet.Rendering.RectDouble"
                                            && mr.Name == "get_Empty")
                                        {
                                            getEmptyRectDouble = mr;
                                            break;
                                        }
                                    }
                                }
                                if (getEmptyRectDouble != null) break;
                            }
                            if (getEmptyRectDouble != null) break;
                        }
                    }

                    foreach (var method in type.Methods)
                    {
                        if ((method.Name == "get_Bounds" || method.Name == "get_FastMaxBounds" || method.Name == "ComputeBounds") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            if (getEmptyRectDouble != null)
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, getEmptyRectDouble));
                            }
                            else
                            {
                                method.Body.InitLocals = true;
                                var loc = new VariableDefinition(method.ReturnType);
                                method.Body.Variables.Add(loc);
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                                method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, loc));
                            }
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if ((method.Name == "TryGetRenderBounds") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.InitLocals = true;
                            var loc = new VariableDefinition(method.ReturnType);
                            method.Body.Variables.Add(loc);
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                            method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, loc));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name == "GetCachedDirect2DGeometryNoAddRef" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.UI.SystemParameters")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == ".cctor" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if ((method.Name == "GetCursorScale" || method.Name == "get_CursorScale" || method.Name == "get_CursorScaleMin") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R8, 1.0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name == "get_CursorScaleMax" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R8, 8.0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name == "get_ClientAreaAnimation" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if ((method.Name == "get_MinimumHorizontalDragDistance" || method.Name == "get_MinimumVerticalDragDistance") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R8, 4.0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Runtime.CleanupThread")
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        if (method.ReturnType.FullName == "System.Boolean")
                        {
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                        }
                        else if (method.ReturnType.FullName != "System.Void")
                        {
                            if (method.ReturnType.IsValueType)
                            {
                                method.Body.InitLocals = true;
                                var loc = new VariableDefinition(method.ReturnType);
                                method.Body.Variables.Add(loc);
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                                method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, loc));
                            }
                            else
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            }
                        }
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                    }
                }

                if (type.FullName.StartsWith("PaintDotNet.Tools.BrushBase.BrushToolBase`3", StringComparison.Ordinal))
                {
                    foreach (var method in type.Methods)
                    {
                        if ((method.Name == "PreJIT" || method.Name == "PreJITBackground") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Direct2D1.Direct2DFactoryPoolTicket")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "get_Ref" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name == "Dispose" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Direct2D1.DrawingContext")
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        if (method.Name == ".cctor"
                            || method.Name == "ResetTextRenderingParametersCache"
                            || method.Name.StartsWith("Draw", StringComparison.Ordinal)
                            || method.Name.StartsWith("BeginDraw", StringComparison.Ordinal)
                            || method.Name.StartsWith("Wrap", StringComparison.Ordinal)
                            || method.Name == "GetDefaultTextRenderingParameters")
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            if (method.ReturnType.FullName == "System.Void")
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                            else if (method.ReturnType.IsValueType)
                            {
                                method.Body.InitLocals = true;
                                var loc = new VariableDefinition(method.ReturnType);
                                method.Body.Variables.Add(loc);
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                                method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, loc));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                            else
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                        }
                    }
                }

                if (type.FullName.StartsWith("PaintDotNet.DirectWrite.TextRenderingParametersCache", StringComparison.Ordinal))
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        var il = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        if (method.ReturnType.FullName == "System.Void")
                        {
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.ReturnType.FullName == "System.Boolean" || method.ReturnType.FullName == "System.Int32")
                        {
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.ReturnType.IsValueType)
                        {
                            method.Body.InitLocals = true;
                            var loc = new VariableDefinition(method.ReturnType);
                            method.Body.Variables.Add(loc);
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                            method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, loc));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else
                        {
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Brushes.Basic.BasicSampledBrushRenderer")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "MeasureSpriteImage" && method.HasBody)
                        {
                            var rectDoubleCtor = module.GetMemberReferences()
                                .FirstOrDefault(mr => mr.DeclaringType.FullName == "PaintDotNet.Rendering.RectDouble"
                                    && mr.Name == ".ctor" && mr is MethodReference mref && mref.Parameters.Count == 4) as MethodReference;
                            var point2DoubleCtor = module.GetMemberReferences()
                                .FirstOrDefault(mr => mr.DeclaringType.FullName == "PaintDotNet.Rendering.Point2Double"
                                    && mr.Name == ".ctor" && mr is MethodReference mref && mref.Parameters.Count == 2) as MethodReference;
                            var vtCtor = method.Body.Instructions
                                .FirstOrDefault(i => i.OpCode == OpCodes.Newobj && (i.Operand as MethodReference)?.DeclaringType.Name.StartsWith("ValueTuple", StringComparison.Ordinal) == true)
                                ?.Operand as MethodReference;

                            if (rectDoubleCtor != null && point2DoubleCtor != null && vtCtor != null)
                            {
                                var il = method.Body.GetILProcessor();
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                method.Body.InitLocals = true;

                                var locCenter = new VariableDefinition(point2DoubleCtor.DeclaringType);
                                var locBounds = new VariableDefinition(rectDoubleCtor.DeclaringType);
                                method.Body.Variables.Add(locCenter);
                                method.Body.Variables.Add(locBounds);

                                // center = new Point2Double(0.5, 0.5)
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R8, 0.5));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R8, 0.5));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newobj, point2DoubleCtor));
                                method.Body.Instructions.Add(il.Create(OpCodes.Stloc_0));

                                // bounds = new RectDouble(0.5 - renderDiameter / 2.0, 0.5 - renderDiameter / 2.0, renderDiameter, renderDiameter)
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R8, 0.5));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R8, 2.0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Div));
                                method.Body.Instructions.Add(il.Create(OpCodes.Sub));

                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R8, 0.5));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R8, 2.0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Div));
                                method.Body.Instructions.Add(il.Create(OpCodes.Sub));

                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newobj, rectDoubleCtor));
                                method.Body.Instructions.Add(il.Create(OpCodes.Stloc_1));

                                // return new ValueTuple<RectDouble, Point2Double>(bounds, center)
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloc_1));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloc_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newobj, vtCtor));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Serialization.SerializationFallbackBinder")
                {
                    var cctor = type.Methods.FirstOrDefault(m => m.Name == ".cctor");
                    if (cctor != null && cctor.HasBody)
                    {
                        foreach (var inst in cctor.Body.Instructions)
                        {
                            if (inst.OpCode == OpCodes.Ldtoken && inst.Operand is TypeReference tr && tr.FullName.Contains("CodeDom"))
                            {
                                inst.Operand = module.TypeSystem.Object;
                            }
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.ReferenceValue")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "CloneCore" && method.HasGenericParameters && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            var memberwiseCloneM = module.ImportReference(typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Call, memberwiseCloneM));
                            method.Body.Instructions.Add(il.Create(OpCodes.Unbox_Any, method.GenericParameters[0]));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.ObjectModel.ResourceCache")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "GetService" && method.Parameters.Count == 1 && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            for (int i = 0; i < method.Body.Instructions.Count; i++)
                            {
                                if (method.Body.Instructions[i].OpCode != OpCodes.Throw)
                                    continue;
                                // The throw expression may build its exception
                                // from several instructions (e.g. ldarg +
                                // callvirt get_FullName + newobj). Walk back
                                // over exactly the expression (net push of one
                                // slot) so no orphan loads unbalance the stack
                                // (which the JIT/interpreter rejects).
                                int depth = 1;
                                int j = i - 1;
                                for (; j >= 0; j--)
                                {
                                    if (!TryThrowExprStep(method.Body.Instructions[j], ref depth))
                                        break;
                                    if (depth == 0)
                                        break;
                                }
                                if (j < 0 || depth != 0)
                                    continue;
                                method.Body.Instructions[j].OpCode = OpCodes.Ldnull;
                                method.Body.Instructions[j].Operand = null;
                                for (int k = j + 1; k < i; k++)
                                {
                                    method.Body.Instructions[k].OpCode = OpCodes.Nop;
                                    method.Body.Instructions[k].Operand = null;
                                }
                                method.Body.Instructions[i].OpCode = OpCodes.Ret;
                            }
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.ServiceProviderExtensions")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "GetRequiredService" && method.HasGenericParameters && method.HasBody)
                        {
                            var getServiceM = type.Methods.FirstOrDefault(x => x.Name == "GetService" && x.HasGenericParameters && x.Parameters.Count == 1);
                            if (getServiceM != null)
                            {
                                var il = method.Body.GetILProcessor();
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                var genInst = new GenericInstanceMethod(module.ImportReference(getServiceM));
                                genInst.GenericArguments.Add(method.GenericParameters[0]);
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, genInst));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                        }
                    }
                }

                if (type.FullName == "System.Windows.Media.FontFamily")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "PreCreateDefaultFamilyCollection" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "MS.Internal.FontCache.FamilyCollection")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "FromWindowsFonts" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "System.Windows.Input.Cursor")
                {
                    var cursorTypeField = type.Fields.FirstOrDefault(f => f.Name == "_cursorType");
                    if (cursorTypeField != null)
                    {
                        var helper = type.Methods.FirstOrDefault(m => m.Name == "LoadCursorHelper");
                        if (helper != null && helper.HasBody)
                        {
                            var il = helper.Body.GetILProcessor();
                            helper.Body.ExceptionHandlers.Clear();
                            helper.Body.Variables.Clear();
                            helper.Body.Instructions.Clear();
                            helper.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                            helper.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                            helper.Body.Instructions.Add(il.Create(OpCodes.Stfld, cursorTypeField));
                            helper.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }

                        foreach (var helperName in new[] { "LoadFromFile", "LoadFromStream", "LegacyLoadFromStream" })
                        {
                            var m = type.Methods.FirstOrDefault(method => method.Name == helperName);
                            if (m != null && m.HasBody)
                            {
                                var il = m.Body.GetILProcessor();
                                m.Body.ExceptionHandlers.Clear();
                                m.Body.Variables.Clear();
                                m.Body.Instructions.Clear();
                                m.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                                m.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_2)); // CursorType.Arrow
                                m.Body.Instructions.Add(il.Create(OpCodes.Stfld, cursorTypeField));
                                m.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Canvas.CanvasRenderingSettings")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "GetAutoMode" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_2)); // CanvasRenderingMode.Software
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name == "GetEffectiveMode" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            var retArg = il.Create(OpCodes.Ldarg_0);
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Brtrue, retArg));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_2)); // CanvasRenderingMode.Software
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            method.Body.Instructions.Add(retArg);
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Dxgi.DXGI")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "CreateFactory7" && method.ReturnType.FullName == "PaintDotNet.Dxgi.IDxgiFactory7" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Dxgi.DxgiFactory7")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "CreateRef" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Imaging.WIC")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == ".cctor" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if ((method.Name == "CreateManagedImagingFactory" || method.Name == "get_ImagingFactory") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name == "CreateNativeImagingFactory" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Conv_U));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Imaging.ImagingFactory")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == ".cctor" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name == "Create" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Imaging.ColorContexts")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == ".cctor" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if ((method.Name.StartsWith("get_") || method.Name == "Get") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name.StartsWith("TryGet") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Stind_Ref));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.SystemLayer.FastMath")
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Name == "CombinePolyLists" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            if (method.ReturnType is GenericInstanceType git && git.GenericArguments.Count > 0)
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newarr, git.GenericArguments[0]));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                            else if (method.ReturnType is ArrayType arr)
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newarr, arr.ElementType));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                            else if (method.ReturnType.FullName == "System.Void")
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                            else
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.SystemLayer.GpcWrapper"
                    || type.FullName == "PaintDotNet.SystemLayer.GpcWrapper/NativeMethods"
                    || type.FullName == "PaintDotNet.SystemLayer.GpcWrapper.NativeMethods"
                    || (type.Name == "NativeMethods" && type.DeclaringType?.Name == "GpcWrapper"))
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            if (method.ReturnType.FullName == "System.Void")
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                            else if (method.ReturnType.IsValueType)
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                            else
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            }
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.SystemLayer.UIHelpers")
                {
                    foreach (var method in type.Methods)
                    {
                        if ((method.Name == "BeginModalUIFrame" || method.Name == "BeginModalUIFrameCore") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "PaintDotNet.Controls.DocumentView")
                {
                    foreach (var method in type.Methods)
                    {
                        if ((method.Name == "PreRenderWhilePumping" || method.Name == "PreRenderHwndBeforeVisible") && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                        else if (method.Name == "PreRenderAsync" && method.HasBody)
                        {
                            var il = method.Body.GetILProcessor();
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            var completedTaskProp = module.ImportReference(typeof(System.Threading.Tasks.Task).GetProperty("CompletedTask")!.GetMethod);
                            method.Body.Instructions.Add(il.Create(OpCodes.Call, completedTaskProp));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        }
                    }
                }

                if (type.FullName == "System.Windows.SystemFonts")
                {
                    var fontFamilyCtor = type.Methods
                        .SelectMany(m => m.Body?.Instructions ?? Enumerable.Empty<Instruction>())
                        .FirstOrDefault(i => i.OpCode == OpCodes.Newobj && (i.Operand as MethodReference)?.DeclaringType.Name == "FontFamily")
                        ?.Operand as MethodReference;

                    var fontStylesNormal = type.Methods
                        .SelectMany(m => m.Body?.Instructions ?? Enumerable.Empty<Instruction>())
                        .FirstOrDefault(i => (i.Operand as MethodReference)?.DeclaringType.Name == "FontStyles" && (i.Operand as MethodReference)?.Name == "get_Normal")
                        ?.Operand as MethodReference;

                    var fontWeightFromOpenType = type.Methods
                        .SelectMany(m => m.Body?.Instructions ?? Enumerable.Empty<Instruction>())
                        .FirstOrDefault(i => (i.Operand as MethodReference)?.DeclaringType.Name == "FontWeight" && (i.Operand as MethodReference)?.Name == "FromOpenTypeWeight")
                        ?.Operand as MethodReference;

                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        var il = method.Body.GetILProcessor();
                        if (method.Name.StartsWith("get_") && !method.Name.EndsWith("Key"))
                        {
                            if (method.ReturnType.FullName == "System.Windows.Media.FontFamily" && fontFamilyCtor != null)
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldstr, "Segoe UI"));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newobj, fontFamilyCtor));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                            if (method.ReturnType.FullName == "System.Double")
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R8, 12.0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                            if (method.ReturnType.FullName == "System.Windows.FontStyle" && fontStylesNormal != null)
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, fontStylesNormal));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                            if (method.ReturnType.FullName == "System.Windows.FontWeight" && fontWeightFromOpenType != null)
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4, 400));
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, fontWeightFromOpenType));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                            if (method.ReturnType.FullName == "System.Windows.TextDecorationCollection")
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                        }
                        else if (method.Name == "InvalidateIconMetrics" || method.Name == "InvalidateNonClientMetrics")
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                    }
                    continue;
                }

                if (type.FullName == "System.Windows.SystemParameters")
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        var il = method.Body.GetILProcessor();
                        if (method.Name == "get_NonClientMetrics")
                        {
                            var ncmField = type.Fields.FirstOrDefault(f => f.Name == "_ncm");
                            var ncmCtor = method.Body.Instructions
                                .FirstOrDefault(i => i.OpCode == OpCodes.Newobj && (i.Operand as MethodReference)?.DeclaringType.Name == "NONCLIENTMETRICS")
                                ?.Operand as MethodReference;
                            if (ncmField != null && ncmCtor != null)
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                var retLabel = il.Create(OpCodes.Ldsfld, ncmField);
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldsfld, ncmField));
                                method.Body.Instructions.Add(il.Create(OpCodes.Brtrue, retLabel));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newobj, ncmCtor));
                                method.Body.Instructions.Add(il.Create(OpCodes.Stsfld, ncmField));
                                method.Body.Instructions.Add(retLabel);
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                        }
                        else if (method.Name == "get_IconMetrics")
                        {
                            var imField = type.Fields.FirstOrDefault(f => f.Name == "_iconMetrics");
                            var imCtor = method.Body.Instructions
                                .FirstOrDefault(i => i.OpCode == OpCodes.Newobj && (i.Operand as MethodReference)?.DeclaringType.Name == "ICONMETRICS")
                                ?.Operand as MethodReference;
                            if (imField != null && imCtor != null)
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                var retLabel = il.Create(OpCodes.Ldsfld, imField);
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldsfld, imField));
                                method.Body.Instructions.Add(il.Create(OpCodes.Brtrue, retLabel));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newobj, imCtor));
                                method.Body.Instructions.Add(il.Create(OpCodes.Stsfld, imField));
                                method.Body.Instructions.Add(retLabel);
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                        }
                    }
                }

                // No ShowDialog override is injected for
                // TaskProgressDialog: Form.ShowDialog is non-virtual in
                // this WinForms version, so a reuse-slot "override" would
                // fail type load and a shadowing method would never
                // intercept the callvirt Form::ShowDialog call sites. The
                // ThreadTask.Start inline rule below is the real fix: the
                // task finishes before OnExecute's first WaitOne, so the
                // progress dialog is never constructed or shown.

                foreach (MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    var il = method.Body.GetILProcessor();
                    if ((type.Name == "ModuleInitializer" && method.Name == "Initialize")
                        || type.FullName == "MS.Internal.Text.TextInterface.DWriteLoader"
                        || type.FullName == "MS.Internal.NativeWPFDLLLoader")
                    {
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Threading.ThreadDispatcher")
                    {
                        if (method.Name == "CheckAccess")
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_1));
                            method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                            continue;
                        }
                        else if (method.Name == "VerifyAccess")
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                            continue;
                        }
                    }
                    if (type.FullName == "PaintDotNet.Drawing.GdipImageExtensions"
                        && method.Name == "ToIcon")
                    {
                        // Both ToIcon overloads build an Icon through a GDI
                        // HICON round-trip (GetHiconSafe -> FromHandleSafe)
                        // that cannot exist in the browser; the handle shim
                        // throws NotSupportedException and breaks dialogs
                        // such as NewFileDialog. Its dialog caller,
                        // TryCreateIcon, already treats null as "no icon"
                        // (missing image path) and Form.set_Icon resets to
                        // the default icon on null, so answer null. The
                        // disposeSource flag is skipped with it: there is no
                        // native resource to release in WASM.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Clipboard.ClipboardExtensions"
                        && method.Name == "OpenAndUseWithRetry"
                        && method.ReturnType is GenericParameter gp)
                    {
                        // The clipboard probe (New dialog image-size default,
                        // paste) opens the native clipboard and reads WIC
                        // streams: absent in the browser (NRE in
                        // MaybeGetWicData, breaking NewImageAction before
                        // the New dialog opens). There is no synchronous
                        // clipboard in WASM, so "clipboard empty"
                        // (default(T)) is the honest answer: NewImageAction
                        // falls back to the default size and paste sees
                        // nothing to paste.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        var loc = new VariableDefinition(gp);
                        method.Body.Variables.Add(loc);
                        method.Body.InitLocals = true;
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                        method.Body.Instructions.Add(il.Create(OpCodes.Initobj, gp));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldloc_0));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Direct2D1.DeviceContextExtensions"
                        && method.Name == "GetDefaultTextLayoutAlgorithm"
                        && method.Parameters.Count == 1
                        && method.ReturnType.FullName == "PaintDotNet.DirectWrite.TextLayoutAlgorithm")
                    {
                        // Measure-only layout (null drawing context) derefs
                        // dc.get_TextAntialiasMode (NRE), breaking brush
                        // commits in TaskProgressDialog measurement. With no
                        // device there are no rendering parameters, and the
                        // method's own null-params fallback is DirectWrite
                        // (0) â€” which is also what the body computes against
                        // our inert stub context (null params), so answering
                        // 0 outright preserves stub behavior and fixes null.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Clipboard.ClipboardTransactionUtil"
                        && method.Name == "TryGetBitmapFromHandle"
                        && method.Parameters.Count == 1
                        && method.ReturnType.FullName == "System.Drawing.Bitmap")
                    {
                        // Paste probing for a bitmap handle ends in
                        // GDI-handle interop (NotSupportedException: no GDI
                        // handles on web). "Try" + null is the honest
                        // absent answer; the paste chain takes its empty
                        // path like the sibling TryGetNativeData rule.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Clipboard.ClipboardTransaction"
                        && method.Name == "TryGetNativeData"
                        && method.Parameters.Count == 2
                        && method.ReturnType.FullName == "System.Boolean")
                    {
                        // No native clipboard in the browser: report "format
                        // absent" so the MaybeGet* wrappers take their empty
                        // path. The sibling OpenAndUseWithRetry<T> rule
                        // neuters only the generic core; paste reaches here
                        // directly and the MaybeGetWicData read callback NREs
                        // on the bogus stream (post-fix recurrence in the
                        // backend index proves it), throwing out through the
                        // Action-overload retry wrapper.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Threading.Tasks.PdnTaskManagerExtensions"
                        && (method.Name == "CreateThreadTask" || method.Name == "StartNewThreadTask")
                        && !method.HasGenericParameters
                        && method.Parameters.Count == 3
                        && IsActionOfPdnTask(method.Parameters[1].ParameterType))
                    {
                        // Background tasks never complete headless: Start()
                        // spawns a System.Threading.Thread that cannot run
                        // on the single-threaded WASM runtime, so the work
                        // sits queued forever and every commit path ends at
                        // a progress dialog fail-fast. The runner cannot be
                        // invoked from Start itself: it lives on the open
                        // generic ThreadTask<T> definition and the WASM
                        // interpreter resolves such callees without their T
                        // (InvalidOperationException: containing type not
                        // fully instantiated, observed live with call,
                        // callvirt, constrained.-callvirt and delegate
                        // Invoke alike). Drive the work from non-generic
                        // scope instead, where every token is closed: the
                        // synthesized InlineFuncThreadTask (a non-generic
                        // ThreadTask<Unit> subclass below) runs the
                        // action through the public Func helpers
                        // (Bind/ToFunc/Eval, which capture errors into the
                        // result exactly like the worker would) and records
                        // TaskResult through its own protected setter,
                        // which only family code may touch. Downstream,
                        // ResultAsync sees IsFinished and completes
                        // synchronously, Receive replays the stored result,
                        // and OnExecute's WaitOne takes the fast path
                        // without showing any dialog.
                        TypeDefinition? driver =
                            EnsureInlineFuncThreadTask(module, type);
                        if (driver is null)
                            continue;
                        if (method.Name == "CreateThreadTask")
                        {
                            // Construct the inline driver directly: it is
                            // a ThreadTask<Unit> like the factory promises,
                            // and StartNewThreadTask drives it. The
                            // display-class func wrapper is dropped with
                            // the old body; DriveInline runs the original
                            // action itself.
                            MethodDefinition? driverCtor = driver.Methods
                                .FirstOrDefault(m => m.Name == ".ctor"
                                    && m.Parameters.Count == 2);
                            if (driverCtor is null)
                                continue;
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_2));
                            method.Body.Instructions.Add(il.Create(OpCodes.Newobj, driverCtor));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                        else
                        {
                            // Skip the stillborn Start: drive inline on the
                            // created task. Stack was [task task]; feed the
                            // action too so DriveInline gets (task, action).
                            Instruction? startCall = method.Body.Instructions
                                .FirstOrDefault(i => (i.OpCode == OpCodes.Callvirt
                                        || i.OpCode == OpCodes.Call)
                                    && i.Operand is MethodReference mr
                                    && mr.Name == "Start"
                                    && mr.DeclaringType.FullName.Contains("ThreadTask`1"));
                            MethodDefinition? driveInline = driver.Methods
                                .FirstOrDefault(m => m.Name == "DriveInline"
                                    && m.Parameters.Count == 1);
                            if (startCall is null || driveInline is null)
                                continue;
                            il.InsertBefore(startCall, il.Create(OpCodes.Ldarg_1));
                            // Call: DriveInline is public non-virtual, so a
                            // static call is both correct and the most
                            // resolution-proof shape.
                            startCall.OpCode = OpCodes.Call;
                            startCall.Operand = driveInline;
                            continue;
                        }
                    }
                    if (type.FullName == "PaintDotNet.Dialogs.TaskProgressDialog"
                        && method.Name == "Sync"
                        && method.Parameters.Count == 0
                        && method.ReturnType.FullName == "System.Void")
                    {
                        // Progress sync drives the Windows taskbar
                        // (TaskbarList COM activation â†’ NRE in WASM) and the
                        // dialog progress bar. Pure UI feedback for an
                        // already-running task: skipping it cannot affect
                        // the commit itself. This unblocks the task
                        // machinery past dialog construction; whether the
                        // worker then runs headless is the next observation.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.PdnBaseForm"
                        && method.Name == "RefreshDisplayInformation"
                        && method.Parameters.Count == 0
                        && method.ReturnType.FullName == "System.Void")
                    {
                        // Per-monitor display tracking (DPI awareness) needs
                        // the WinRT SDK projection (FileNotFound in WASM).
                        // The canvas is a single fixed surface: there are no
                        // monitors to track, so refreshing is a no-op. This
                        // unblocks TaskProgressDialog construction during
                        // brush commits (OnLayout â†’ ClientSize â†’
                        // OnMonitorChanged â†’ here).
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Controls.PdnPushButton"
                        && method.Name == "MeasureAndDraw"
                        && method.HasThis
                        && method.Parameters.Count == 5
                        && method.ReturnType.FullName == "System.Drawing.Size")
                    {
                        // Button measure+paint during TaskProgressDialog
                        // layout (brush commits) dies in the measure
                        // callback (null-dc resource-cache lookup, then a
                        // COM-only ITextLayout with no managed implementor
                        // to answer metrics). With no D2D device in WASM
                        // there is nothing to measure against and painting
                        // never runs either: answer the default (empty)
                        // size so dialog construction completes. Every path
                        // through here currently throws, so this strictly
                        // improves; the commit then proceeds to the dialog
                        // show (fail-fast wall, tracked separately).
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.InitLocals = true;
                        var loc = new VariableDefinition(method.ReturnType);
                        method.Body.Variables.Add(loc);
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldloca_S, loc));
                        method.Body.Instructions.Add(il.Create(OpCodes.Initobj, method.ReturnType));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldloc_0));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.UI.Media.UITextFactory"
                        && method.Name == "CreateLayout"
                        && method.HasThis
                        && method.Parameters.Count == 8
                        && method.Parameters[0].ParameterType.FullName == "PaintDotNet.Direct2D1.IDrawingContext"
                        && TryMatchDpiScalePrologue(method, out int scaleSlot, out int prologueEnd))
                    {
                        // Measure-only text layout (PdnPushButton.MeasureAndDraw
                        // with drawing disabled, e.g. TaskProgressDialog during
                        // a brush commit) passes a null drawing context: there
                        // is no D2D device in the browser. Upstream derefs
                        // dc.get_Dpi() unconditionally (NRE), breaking the
                        // commit. Answer scale 1.0 (96 dpi) when dc is null;
                        // the device path below is untouched. The join lands
                        // AFTER the scale store (stack provably empty there):
                        // joining mid-expression splits the evaluation stack
                        // and the method fails verification (InvalidProgram
                        // under the WASM interpreter, attributed to the
                        // caller at whose call site the callee is JITed).
                        var insns = method.Body.Instructions;
                        var join = il.Create(OpCodes.Nop);
                        var hasDc = il.Create(OpCodes.Nop);
                        il.InsertBefore(insns[prologueEnd], join);
                        il.InsertBefore(insns[0], hasDc);
                        insns.Insert(0, il.Create(OpCodes.Br_S, join));
                        insns.Insert(0, CloneStloc(il, method, scaleSlot));
                        insns.Insert(0, il.Create(OpCodes.Ldc_R8, 1.0));
                        insns.Insert(0, il.Create(OpCodes.Brtrue_S, hasDc));
                        insns.Insert(0, il.Create(OpCodes.Ldarg_1));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Interop.RegistryKeyWatcher"
                        && method.Name == ".ctor")
                    {
                        // No registry on WebAssembly: skip key opening, keep
                        // the managed Disposable base construction. The
                        // watcher then simply never fires.
                        var baseCtor = type.BaseType.Resolve()?.Methods
                            .FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0);
                        if (baseCtor is not null)
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Call, module.ImportReference(baseCtor)));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                    }
                    if (type.FullName == "System.Windows.Forms.Application"
                        && method.Name == "ParkHandle")
                    {
                        // No HWNDs on WebAssembly: parking a handle in the
                        // Win32 parking window is meaningless, and the
                        // WS_CHILD Debug.Assert fires on emulated handles
                        // (breaks e.g. Calendar month navigation, which
                        // rebuilds day controls). Skip it.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Animation.AnimatedValue"
                        && ((method.Name == "AnimateRawValue"
                            && method.Parameters.Count == 2
                            && method.Parameters[1].ParameterType is GenericInstanceType)
                            || (method.Name == "AnimateRawValueTo"
                            && method.Parameters.Count == 3
                            && method.Parameters[0].ParameterType.FullName == "System.Double")))
                    {
                        // No animation engine on WebAssembly (needs the COM
                        // animation manager): apply the target value instantly,
                        // mirroring the duration==0 fast path already in the
                        // 3-arg overload. SetRawValue/get_RawValue are pure
                        // managed field access.
                        var setRaw = type.Methods.FirstOrDefault(m => m.Name == "SetRawValue"
                            && m.Parameters.Count == 2
                            && m.Parameters[0].ParameterType.FullName == "System.Double");
                        var getRaw = type.Methods.FirstOrDefault(m => m.Name == "get_RawValue"
                            && m.Parameters.Count == 0);
                        if (setRaw is not null && getRaw is not null)
                        {
                            MethodReference? gvodRef = null;
                            if (method.Name == "AnimateRawValue")
                            {
                                var nullableArg = (GenericInstanceType)method.Parameters[1].ParameterType;
                                var gp0 = nullableArg.ElementType.GenericParameters[0];
                                gvodRef = new MethodReference("GetValueOrDefault", gp0, nullableArg)
                                {
                                    HasThis = true,
                                };
                                gvodRef.Parameters.Add(new ParameterDefinition(gp0));
                            }
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            if (method.Name == "AnimateRawValue")
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarga, method.Parameters[1]));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, module.ImportReference(getRaw)));
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, gvodRef));
                            }
                            else
                            {
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                            }
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Call, module.ImportReference(setRaw)));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                    }
                    if ((type.FullName == "PaintDotNet.SystemLayer.UIHelpers" || type.FullName == "PaintDotNet.SystemLayer.UIUtil") && method.Name == "GetDeviceCaps")
                    {
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, 96));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.SystemLayer.FileSystem" && method.Name == "OpenStreamingFile")
                    {
                        // The original body pinvokes CreateFileW for a
                        // handle, but every pinvoke is stubbed in WASM and
                        // the stub answers a null handle: the FileStream
                        // then wraps fd 0 and every flush dies with
                        // EBADF ("Bad file descriptor", surfacing as
                        // UnauthorizedAccessException at dispose). This is
                        // the only caller path for history session files,
                        // so open the managed way with plain flags; the
                        // caller already guarantees a unique fresh path.
                        var streamCtor = module.ImportReference(typeof(System.IO.FileStream).GetConstructor(
                            [typeof(string), typeof(System.IO.FileMode), typeof(System.IO.FileAccess),
                                typeof(System.IO.FileShare), typeof(int), typeof(System.IO.FileOptions)])!);
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_2));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_3));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_1));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldarg_S, method.Parameters[5]));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Newobj, streamCtor));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.SystemLayer.FileSystem" && method.Name == "CreateTempFile")
                    {
                        // The original body pinvokes CreateFileW for a
                        // handle, but every pinvoke is stubbed in WASM and
                        // the stub answers a null handle: CreateTempFile
                        // only rejects INVALID_HANDLE_VALUE (-1), so the
                        // null slips through and the FileStream dies in
                        // ValidateHandle ("The handle is invalid"). That
                        // poisons FileSystem..cctor (which holds the
                        // session.lock probe stream in sessionToken) and
                        // with it every history/scratch path (no ink). Only
                        // two callers exist (..cctor probe, clipboard
                        // AddNativeData); both pass full paths from managed
                        // helpers, so open the managed way with the original
                        // flags: Create + ReadWrite + Read-share (lock-file
                        // semantics) + DeleteOnClose (temp semantics). Mirror
                        // of the OpenStreamingFile rule above.
                        var streamCtor = module.ImportReference(typeof(System.IO.FileStream).GetConstructor(
                            [typeof(string), typeof(System.IO.FileMode), typeof(System.IO.FileAccess),
                                typeof(System.IO.FileShare), typeof(int), typeof(System.IO.FileOptions)])!);
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_2));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_3));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_1));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldarg_1));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x04000000));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Newobj, streamCtor));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.DirectWrite.DirectWriteFactoryExtensions" && method.Name == "GetGdiFontMap")
                    {
                        // The DWrite factory is neutered headless, so this
                        // answers null and every TextTool activation dies in
                        // FontMapExtensions (NRE) and then poisons all
                        // pointer input with the half-activated tool. Answer
                        // a headless font map instead: any family resolves
                        // to default properties (no fonts exist in WASM, but
                        // activation, settings, and commit paths proceed).
                        // Fall through to the legacy null answer below when
                        // the module lacks the shape the fake needs.
                        TypeDefinition? fontMap = EnsureHeadlessGdiFontMap(module);
                        if (fontMap is not null)
                        {
                            var mapCtor = fontMap.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 0);
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Newobj, mapCtor));
                            method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                            continue;
                        }
                    }
                    if (type.FullName == "PaintDotNet.Drawing.FontUtil" && method.Name == "GetLogPixels")
                    {
                        // The original body news up several types (including
                        // exceptions); only the ctor matching the return type
                        // is a valid fixed-DPI replacement.
                        var ctor = method.Body.Instructions
                            .Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj)
                            .Select(i => (MethodReference)i.Operand)
                            .FirstOrDefault(mr => mr.DeclaringType.FullName == method.ReturnType.FullName);
                        if (ctor != null)
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, 96));
                            method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, 96));
                            method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Newobj, ctor));
                            method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                            continue;
                        }
                    }
                    if (type.FullName == "PaintDotNet.Resources.PdnResources" && method.Name == "GetInstalledLanguagesImpl")
                    {
                        // No satellite language files ship in the browser
                        // bundle: enumerating the resources directory throws,
                        // which breaks AppSettings creation during bootstrap.
                        // Report just the built-in language instead.
                        var cultureInfo = module.ImportReference(typeof(System.Globalization.CultureInfo));
                        var cultureCtor = module.ImportReference(typeof(System.Globalization.CultureInfo).GetConstructor([typeof(string)])!);
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_1));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Newarr, cultureInfo));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Dup));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldstr, "en-US"));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Newobj, cultureCtor));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Stelem_Ref));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Settings.App.AppSettings/GraphicsSection"
                        && method.Name == "GetEnableHardwareAccelerationForUIDefault")
                    {
                        // No GPU in the browser: probing hardware acceleration
                        // throws (NRE, caught below by the original body) on
                        // every boot. Answer false directly.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Data.WicHelpers"
                        && (method.Name == "GetLoadExtensions" || method.Name == "GetSaveExtensions"))
                    {
                        // No WIC codecs exist in the browser: enumerating
                        // container extensions dies (NRE) and kills every
                        // built-in FileType .cctor (Gif/Bmp/Tiff, seen when
                        // the File > Open dialog enumerates file types).
                        // Answer an empty list; the built-in FileType .cctors
                        // themselves are rewritten with literal tables by
                        // RedirectFileTypeCctorTables (a member-call switch
                        // over cross-assembly structs fails runtime member
                        // resolution headless: TypeLoadException kind 17).
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Newarr, module.TypeSystem.String));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Resources.PdnInfo" && method.Name == "GetApplicationDir")
                    {
                        // Browser WASM assemblies have no on-disk location:
                        // Assembly.Location returns "", GetDirectoryName
                        // yields null, and NotNull throws InternalError.
                        // That kills ShapesTool activation (shape catalog
                        // validates via plugin-dir enumeration) and leaves
                        // the half-activated tool stuck: every later switch
                        // NREs in Deactivate. Answer the same constant dir
                        // as get_ResourcesDir; plugin/shape probing then
                        // finds nothing and falls back to built-ins.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldstr, "C:\\PaintDotNet\\Wasm\\"));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Resources.PdnResources" && method.Name == "get_ResourcesDir")
                    {
                        // Browser WASM assemblies have no on-disk location:
                        // Assembly.Location returns "", and the original body
                        // feeds it to new Uri, which throws UriFormatException
                        // from PdnResources..cctor (no AppWorkspace, empty
                        // form). The directory is only used to probe locale
                        // folders on disk (all absent in WASM; strings fall
                        // back to manifest resources), so return a constant
                        // absolute path that always forms a valid file URI.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldstr, "C:\\PaintDotNet\\Wasm\\"));
                        method.Body.Instructions.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.DirectWrite.DWrite")
                    {
                        if (method.Name == ".cctor")
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                        if (method.Name == "CreateFactory7")
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                    }
                    if (type.FullName == "PaintDotNet.DirectWrite.DirectWriteFactory")
                    {
                        if (method.Name == ".cctor")
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                        if (method.Name == "CreateRef" || method.Name == "Create")
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                    }
                    if (type.FullName == "PaintDotNet.DirectWrite.DirectWriteFactoryExtensions")
                    {
                        if (method.Name == "GetGdiFontMap")
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                        if (method.Name == "GetSystemFonts")
                        {
                            var gsfType = module.Types.FirstOrDefault(t => t.FullName == "PaintDotNet.DirectWrite.GdiSystemFonts");
                            var gsfCtor = gsfType?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 1);
                            if (gsfCtor != null)
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newobj, gsfCtor));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                        }
                    }
                    if (type.FullName == "PaintDotNet.DirectWrite.GdiSystemFonts")
                    {
                        if (method.Name == ".ctor" && method.Parameters.Count == 1)
                        {
                            var baseCtor = type.BaseType.Resolve()?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0);
                            var refreshM = type.Methods.FirstOrDefault(m => m.Name == "Refresh");
                            if (baseCtor != null && refreshM != null)
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, module.ImportReference(baseCtor)));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, refreshM));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                        }
                        if (method.Name == "GetSystemFontProperties")
                        {
                            var getGdiSysFontM = type.Methods.FirstOrDefault(m => m.Name == "GetGdiSystemFont");
                            var gdiParamsType = module.Types.FirstOrDefault(t => t.FullName == "PaintDotNet.DirectWrite.GdiFontParameters");
                            var fpType = module.Types.FirstOrDefault(t => t.FullName == "PaintDotNet.DirectWrite.FontProperties");
                            var sfpType = module.Types.FirstOrDefault(t => t.FullName == "PaintDotNet.DirectWrite.SizedFontProperties");
                            var fpCtor = fpType?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 6);
                            var sfpCtor = sfpType?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 2);
                            var faceNameProp = gdiParamsType?.Methods.FirstOrDefault(m => m.Name == "get_FaceName");
                            var sizeProp = gdiParamsType?.Methods.FirstOrDefault(m => m.Name == "get_Size");

                            if (getGdiSysFontM != null && fpCtor != null && sfpCtor != null && faceNameProp != null && sizeProp != null)
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                var locGdi = new VariableDefinition(gdiParamsType);
                                method.Body.Variables.Add(locGdi);
                                method.Body.InitLocals = true;
                                method.Body.Instructions.Clear();
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, getGdiSysFontM));
                                method.Body.Instructions.Add(il.Create(OpCodes.Stloc_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloc_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Callvirt, faceNameProp));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloc_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Callvirt, faceNameProp));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4, 400));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4, 5));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newobj, fpCtor));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldloc_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Callvirt, sizeProp));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_R4, 12.0f));
                                var mathMaxRef = module.ImportReference(typeof(Math).GetMethod("Max", [typeof(float), typeof(float)]));
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, mathMaxRef));
                                method.Body.Instructions.Add(il.Create(OpCodes.Newobj, sfpCtor));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                        }
                    }
                    if (type.FullName == "PaintDotNet.Dialogs.ColorsForm"
                        && method.Name == "SetColorAddIcon"
                        && method.Parameters.Count == 2)
                    {
                        // The "+" color icon is rendered with Direct2D (needs
                        // the device factory, null on WebAssembly). Skip it;
                        // the dialog lays out without the icon.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Controls.PdnLabel"
                        && method.Name == "GetPreferredSize"
                        && method.Parameters.Count == 1
                        && method.ReturnType.FullName == "System.Drawing.Size")
                    {
                        // Text measuring via Direct2D is unavailable; fall back
                        // to the managed Label implementation for layout. The
                        // canvas host draws the text itself.
                        var labelRef = module.GetTypeReferences().FirstOrDefault(t => t.FullName == "System.Windows.Forms.Label"
                            && t is not TypeSpecification);
                        var sizeRef = module.GetTypeReferences().FirstOrDefault(t => t.FullName == "System.Drawing.Size"
                            && t is not TypeSpecification);
                        if (labelRef is not null && sizeRef is not null)
                        {
                            var baseM = new MethodReference("GetPreferredSize", sizeRef, labelRef)
                            {
                                HasThis = true,
                            };
                            baseM.Parameters.Add(new ParameterDefinition(sizeRef));
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                            method.Body.Instructions.Add(il.Create(OpCodes.Call, baseM));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                    }
                    if (type.FullName == "PaintDotNet.Dxgi.DXGI"
                        && method.Name == ".cctor")
                    {
                        // No dxgi.dll on WebAssembly: leave the native handle
                        // null (same shape as the DWrite stub below). Users of
                        // the handle fail only if device paths execute, which
                        // the canvas host never reaches.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Direct2D1.EffectCategories"
                        && method.Name == ".cctor")
                    {
                        // WebAssembly has no Direct2D: the static constructor
                        // reads each built-in effect's CATEGORY property
                        // (0x80000003) from the OS effect factory, which always
                        // fails headless (NotSupportedException out of
                        // ThrowOnError) and poisons every CustomEffect/GPU
                        // effect plus the Effects/Adjustments menus. The nine
                        // values are fixed English strings (Color/Photo/Stylize
                        // match the native 5.1.12 Effects menu groups; the rest
                        // follow the same field-name shape), so bake them in.
                        // Hlsl stays null: the native cctor never assigns it.
                        (string Field, string Value)[] categories =
                        [
                            ("analysis", "Analysis"),
                            ("color", "Color"),
                            ("composite", "Composite"),
                            ("filter", "Filter"),
                            ("photo", "Photo"),
                            ("source", "Source"),
                            ("stylize", "Stylize"),
                            ("transform", "Transform"),
                            ("transparency", "Transparency"),
                        ];
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        foreach (var (field, value) in categories)
                        {
                            var target = type.Fields.FirstOrDefault(f => f.Name == field)
                                ?? throw new InvalidOperationException(
                                    "EffectCategories: static field '" + field + "' missing");
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldstr, value));
                            method.Body.Instructions.Add(il.Create(OpCodes.Stsfld, target));
                        }
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (cpuPaintPath is not null
                        && type.FullName == "PaintDotNet.Brushes.Basic.BasicSampledBrushRenderer"
                        && method.Name == "CreateSpriteImage"
                        && method.Parameters.Count == 7
                        && method.HasBody)
                    {
                        // Headless brush sprite: the device effect chain
                        // (SrgbToLinear/GaussianBlur/LinearToSrgb/Invert/
                        // CopyBToA) has no D2D backend, so render the sprite
                        // on the CPU into a fresh Pbgra32 bitmap and hand
                        // back the sibling module's CpuDeviceBitmap (the
                        // DrawImage blit already accepts it). All geometry
                        // stays pristine: soft/blur diameters, Lerp and the
                        // sprite size come from PDN's own math.
                        var sil = method.Body.GetILProcessor();
                        MethodReference? softDiaCall = null;
                        MethodReference? blurDiaCall = null;
                        MethodReference? lerpCall = null;
                        foreach (var ins in method.Body.Instructions)
                        {
                            if ((ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)
                                || ins.Operand is not MethodReference callee)
                                continue;
                            if (callee.Name == "GetSoftDiameter" && callee.Parameters.Count == 2)
                                softDiaCall = callee;
                            else if (callee.Name == "GetBlurDiameter" && callee.Parameters.Count == 1)
                                blurDiaCall = callee;
                            else if (callee.Name == "Lerp" && callee.Parameters.Count == 3)
                                lerpCall = callee;
                        }
                        if (softDiaCall is null || blurDiaCall is null || lerpCall is null)
                            throw new InvalidOperationException(
                                "CpuSprite: sprite math callees missing in CreateSpriteImage");
                        MethodReference measureCall = VirtualCpuCall(
                            module, "PaintDotNet.Brushes.Basic.BasicSampledBrushRenderer",
                            "MeasureSpriteBitmap", 2);
                        MethodReference widthCall = StructCpuGetter(
                            module, "PaintDotNet.Rendering.SizeInt32", "get_Width");
                        MethodReference heightCall = StructCpuGetter(
                            module, "PaintDotNet.Rendering.SizeInt32", "get_Height");
                        MethodReference ptX = StructCpuGetter(module, "PaintDotNet.Rendering.Point2Double", "get_X");
                        MethodReference ptY = StructCpuGetter(module, "PaintDotNet.Rendering.Point2Double", "get_Y");
                        MethodReference colR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
                        MethodReference colG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
                        MethodReference colB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
                        MethodReference colA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
                        MethodReference createSprite = ImportCpuPaintMethod(module, cpuPaintPath, "CreateSprite", 16);
                        // The sibling Windows.Core rewrite (same tool, same
                        // --pdncp run) synthesizes CpuDeviceBitmap..ctor/3
                        // there, but this module rewrites before it (bundle
                        // order) and tests stage no sibling: reference the
                        // .ctor by shape on the module's own Windows.Core
                        // assembly reference (SafeGetObject precedent) so no
                        // disk order is required. The token resolves at load
                        // against the shipped rewritten core; the sprite pin
                        // test stages that core and resolves every callee.
                        var coreScope = module.AssemblyReferences.FirstOrDefault(r =>
                                r.Name == "PaintDotNet.Windows.Core")
                            ?? throw new InvalidOperationException(
                                "CpuSprite: PaintDotNet.Windows.Core reference missing");
                        var bmpTypeRef = new TypeReference(
                            "PaintDotNet.Direct2D1", "CpuDeviceBitmap",
                            module, coreScope);
                        var bmpCtor = new MethodReference(
                            ".ctor", module.TypeSystem.Void, bmpTypeRef) { HasThis = true };
                        bmpCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int64));
                        bmpCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                        bmpCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                        var softDiaLoc = new VariableDefinition(module.TypeSystem.Double);
                        var blurRLoc = new VariableDefinition(module.TypeSystem.Double);
                        var sizeLoc = new VariableDefinition(measureCall.ReturnType);
                        var wLoc = new VariableDefinition(module.TypeSystem.Int32);
                        var hLoc = new VariableDefinition(module.TypeSystem.Int32);
                        var cxLoc = new VariableDefinition(module.TypeSystem.Double);
                        var cyLoc = new VariableDefinition(module.TypeSystem.Double);
                        var spriteLoc = new VariableDefinition(module.TypeSystem.Int64);
                        foreach (var v in new[] { softDiaLoc, blurRLoc, sizeLoc, wLoc, hLoc, cxLoc, cyLoc, spriteLoc })
                            method.Body.Variables.Add(v);
                        method.Body.InitLocals = true;
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Instructions.Clear();
                        var centerParam = method.Parameters[1];
                        var diameterParam = method.Parameters[2];
                        var hardnessParam = method.Parameters[3];
                        var clearParam = method.Parameters[4];
                        var spriteParam = method.Parameters[5];
                        var invertParam = method.Parameters[6];
                        sil.Emit(OpCodes.Ldarg, diameterParam);
                        sil.Emit(OpCodes.Ldarg, hardnessParam);
                        sil.Emit(OpCodes.Call, softDiaCall);
                        sil.Emit(OpCodes.Stloc, softDiaLoc);
                        sil.Emit(OpCodes.Ldarg_0);
                        sil.Emit(OpCodes.Ldarg, diameterParam);
                        sil.Emit(OpCodes.Callvirt, blurDiaCall);
                        sil.Emit(OpCodes.Ldc_R8, 0.0);
                        sil.Emit(OpCodes.Ldarg, hardnessParam);
                        sil.Emit(OpCodes.Call, lerpCall);
                        sil.Emit(OpCodes.Ldc_R8, 2.0);
                        sil.Emit(OpCodes.Div);
                        sil.Emit(OpCodes.Stloc, blurRLoc);
                        sil.Emit(OpCodes.Ldarg_0);
                        sil.Emit(OpCodes.Ldarg, diameterParam);
                        sil.Emit(OpCodes.Ldarg, hardnessParam);
                        sil.Emit(OpCodes.Callvirt, measureCall);
                        sil.Emit(OpCodes.Stloc, sizeLoc);
                        sil.Emit(OpCodes.Ldloca, sizeLoc);
                        sil.Emit(OpCodes.Call, widthCall);
                        sil.Emit(OpCodes.Stloc, wLoc);
                        sil.Emit(OpCodes.Ldloca, sizeLoc);
                        sil.Emit(OpCodes.Call, heightCall);
                        sil.Emit(OpCodes.Stloc, hLoc);
                        sil.Emit(OpCodes.Ldarga, centerParam);
                        sil.Emit(OpCodes.Call, ptX);
                        sil.Emit(OpCodes.Stloc, cxLoc);
                        sil.Emit(OpCodes.Ldarga, centerParam);
                        sil.Emit(OpCodes.Call, ptY);
                        sil.Emit(OpCodes.Stloc, cyLoc);
                        sil.Emit(OpCodes.Ldloc, wLoc);
                        sil.Emit(OpCodes.Ldloc, hLoc);
                        sil.Emit(OpCodes.Ldloc, cxLoc);
                        sil.Emit(OpCodes.Ldloc, cyLoc);
                        sil.Emit(OpCodes.Ldloc, softDiaLoc);
                        sil.Emit(OpCodes.Ldloc, blurRLoc);
                        foreach (var cp in new[] { clearParam, spriteParam })
                        {
                            foreach (var getter in new[] { colR, colG, colB, colA })
                            {
                                sil.Emit(OpCodes.Ldarga, cp);
                                sil.Emit(OpCodes.Call, getter);
                            }
                        }
                        sil.Emit(OpCodes.Ldarg, invertParam);
                        sil.Emit(OpCodes.Ldarg, hardnessParam);
                        sil.Emit(OpCodes.Call, createSprite);
                        sil.Emit(OpCodes.Stloc, spriteLoc);
                        sil.Emit(OpCodes.Ldloc, spriteLoc);
                        sil.Emit(OpCodes.Ldloc, wLoc);
                        sil.Emit(OpCodes.Ldloc, hLoc);
                        sil.Emit(OpCodes.Newobj, bmpCtor);
                        sil.Emit(OpCodes.Ret);
                        method.Body.MaxStackSize = 32;
                        continue;
                    }
                    if (cpuPaintPath is not null
                        && type.FullName == "PaintDotNet.Direct2D1.DrawingContextExtensions"
                        && method.Name == "FillGeometry"
                        && method.Parameters.Count == 4
                        && method.HasBody)
                    {
                        // Icon/cursor rasterization (Shape image resources)
                        // fills vector geometries through the D2D backend,
                        // which is absent headless. The brush side already
                        // resolves on the CPU; the geometry provider walks
                        // in CpuPaint (duck-typed, no UI references here).
                        // Cache instantiations harvest from the pristine
                        // body so the generic tokens stay compiler-made.
                        var fil = method.Body.GetILProcessor();
                        MethodReference? brushCache = null;
                        MethodReference? opCache = null;
                        foreach (var ins in method.Body.Instructions)
                        {
                            if (ins.OpCode != OpCodes.Call
                                || ins.Operand is not Mono.Cecil.GenericInstanceMethod gim)
                                continue;
                            if (gim.Name == "GetCachedOrCreateResource"
                                && gim.GenericArguments.Count == 1
                                && gim.GenericArguments[0].FullName
                                    == "PaintDotNet.Direct2D1.IDeviceBrush")
                                brushCache = gim;
                            else if (gim.Name == "SafeGetCachedOrCreateResource"
                                && gim.GenericArguments.Count == 1
                                && gim.GenericArguments[0].FullName
                                    == "PaintDotNet.Direct2D1.IDeviceBrush")
                                opCache = gim;
                        }
                        if (brushCache is null || opCache is null)
                            throw new InvalidOperationException(
                                "CpuFill: FillGeometry brush cache calls missing");
                        TypeReference solidRef = OwnedCpuRef(module, "PaintDotNet.Direct2D1.ISolidColorBrush");
                        MethodReference getColor = VirtualCpuCall(module, "PaintDotNet.Direct2D1.ISolidColorBrush", "get_Color", 0);
                        MethodReference getOpacity = VirtualCpuCall(module, "PaintDotNet.Direct2D1.IDeviceBrush", "get_Opacity", 0);
                        MethodReference fillR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
                        MethodReference fillG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
                        MethodReference fillB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
                        MethodReference fillA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
                        TypeReference colorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.ColorRgba128Float");
                        var cpuCtxRef = new TypeReference(
                            "PaintDotNet.Direct2D1", "CpuDrawingContext", module, module);
                        var handleRef = new MethodReference(
                            "GetCpuTargetHandle", module.TypeSystem.Int64, cpuCtxRef) { HasThis = true };
                        MethodReference cpuFill = ImportCpuPaintMethod(module, cpuPaintPath, "FillGeometry", 7);
                        MethodReference nseCtor = module.ImportReference(
                            typeof(NotSupportedException).GetConstructor([typeof(string)])!);
                        var brushLoc = new VariableDefinition(method.Parameters[2].ParameterType);
                        var solidLoc = new VariableDefinition(solidRef);
                        var colorLoc = new VariableDefinition(colorRef);
                        var opLoc = new VariableDefinition(module.TypeSystem.Single);
                        var effLoc = new VariableDefinition(module.TypeSystem.Single);
                        var rLoc = new VariableDefinition(module.TypeSystem.Single);
                        var gLoc = new VariableDefinition(module.TypeSystem.Single);
                        var bLoc = new VariableDefinition(module.TypeSystem.Single);
                        var aLoc = new VariableDefinition(module.TypeSystem.Single);
                        var opBrushLoc = new VariableDefinition(method.Parameters[3].ParameterType);
                        var cpuLoc = new VariableDefinition(cpuCtxRef);
                        var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
                        foreach (var v in new[] { brushLoc, solidLoc, colorLoc, opLoc, effLoc, rLoc, gLoc, bLoc, aLoc, opBrushLoc, cpuLoc, handleLoc })
                            method.Body.Variables.Add(v);
                        method.Body.InitLocals = true;
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Instructions.Clear();
                        var notSolid = fil.Create(OpCodes.Nop);
                        var notCpu = fil.Create(OpCodes.Nop);
                        var noOpBrush = fil.Create(OpCodes.Nop);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Ldarg_2);
                        fil.Emit(OpCodes.Call, brushCache);
                        fil.Emit(OpCodes.Stloc, brushLoc);
                        fil.Emit(OpCodes.Ldloc, brushLoc);
                        fil.Emit(OpCodes.Isinst, solidRef);
                        fil.Emit(OpCodes.Stloc, solidLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Brfalse, notSolid);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getColor);
                        fil.Emit(OpCodes.Stloc, colorLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getOpacity);
                        fil.Emit(OpCodes.Stloc, opLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillR);
                        fil.Emit(OpCodes.Stloc, rLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillG);
                        fil.Emit(OpCodes.Stloc, gLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillB);
                        fil.Emit(OpCodes.Stloc, bLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillA);
                        fil.Emit(OpCodes.Stloc, aLoc);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Ldarg_3);
                        fil.Emit(OpCodes.Call, opCache);
                        fil.Emit(OpCodes.Stloc, opBrushLoc);
                        fil.Emit(OpCodes.Ldloc, opLoc);
                        fil.Emit(OpCodes.Stloc, effLoc);
                        fil.Emit(OpCodes.Ldloc, opBrushLoc);
                        fil.Emit(OpCodes.Brfalse, noOpBrush);
                        fil.Emit(OpCodes.Ldloc, opBrushLoc);
                        fil.Emit(OpCodes.Isinst, solidRef);
                        fil.Emit(OpCodes.Stloc, solidLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Brfalse, noOpBrush);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getOpacity);
                        fil.Emit(OpCodes.Ldloc, effLoc);
                        fil.Emit(OpCodes.Mul);
                        fil.Emit(OpCodes.Stloc, effLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getColor);
                        fil.Emit(OpCodes.Stloc, colorLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillA);
                        fil.Emit(OpCodes.Ldloc, effLoc);
                        fil.Emit(OpCodes.Mul);
                        fil.Emit(OpCodes.Stloc, effLoc);
                        fil.Append(noOpBrush);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Isinst, cpuCtxRef);
                        fil.Emit(OpCodes.Stloc, cpuLoc);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Brfalse, notCpu);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Callvirt, handleRef);
                        fil.Emit(OpCodes.Stloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, handleLoc);
                        fil.Emit(OpCodes.Ldarg_1);
                        fil.Emit(OpCodes.Ldloc, effLoc);
                        fil.Emit(OpCodes.Ldloc, rLoc);
                        fil.Emit(OpCodes.Ldloc, gLoc);
                        fil.Emit(OpCodes.Ldloc, bLoc);
                        fil.Emit(OpCodes.Ldloc, aLoc);
                        fil.Emit(OpCodes.Call, cpuFill);
                        fil.Emit(OpCodes.Ret);
                        fil.Append(notSolid);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: FillGeometry needs an ISolidColorBrush");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        fil.Append(notCpu);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: FillGeometry needs a CpuDrawingContext");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        method.Body.MaxStackSize = 32;
                        continue;
                    }
                    if (cpuPaintPath is not null
                        && type.FullName == "PaintDotNet.Direct2D1.DrawingContextExtensions"
                        && method.Name == "FillRectangle"
                        && method.Parameters.Count == 3
                        && method.HasBody)
                    {
                        // Rect fills (selection boxes, icon backplates)
                        // take the same CPU route as FillGeometry.
                        var fil = method.Body.GetILProcessor();
                        MethodReference? brushCache = null;
                        foreach (var ins in method.Body.Instructions)
                        {
                            if (ins.OpCode == OpCodes.Call
                                && ins.Operand is Mono.Cecil.GenericInstanceMethod gim
                                && gim.Name == "GetCachedOrCreateResource"
                                && gim.GenericArguments.Count == 1
                                && gim.GenericArguments[0].FullName
                                    == "PaintDotNet.Direct2D1.IDeviceBrush")
                                brushCache = gim;
                        }
                        if (brushCache is null)
                            throw new InvalidOperationException(
                                "CpuFill: FillRectangle brush cache call missing");
                        TypeReference solidRef = OwnedCpuRef(module, "PaintDotNet.Direct2D1.ISolidColorBrush");
                        MethodReference getColor = VirtualCpuCall(module, "PaintDotNet.Direct2D1.ISolidColorBrush", "get_Color", 0);
                        MethodReference getOpacity = VirtualCpuCall(module, "PaintDotNet.Direct2D1.IDeviceBrush", "get_Opacity", 0);
                        MethodReference fillR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
                        MethodReference fillG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
                        MethodReference fillB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
                        MethodReference fillA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
                        TypeReference colorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.ColorRgba128Float");
                        MethodReference rectX = StructCpuGetter(module, "PaintDotNet.Rendering.RectDouble", "get_X");
                        MethodReference rectY = StructCpuGetter(module, "PaintDotNet.Rendering.RectDouble", "get_Y");
                        MethodReference rectW = StructCpuGetter(module, "PaintDotNet.Rendering.RectDouble", "get_Width");
                        MethodReference rectH = StructCpuGetter(module, "PaintDotNet.Rendering.RectDouble", "get_Height");
                        var cpuCtxRef = new TypeReference(
                            "PaintDotNet.Direct2D1", "CpuDrawingContext", module, module);
                        var handleRef = new MethodReference(
                            "GetCpuTargetHandle", module.TypeSystem.Int64, cpuCtxRef) { HasThis = true };
                        MethodReference cpuFill = ImportCpuPaintMethod(module, cpuPaintPath, "FillRect", 10);
                        MethodReference nseCtor = module.ImportReference(
                            typeof(NotSupportedException).GetConstructor([typeof(string)])!);
                        var brushLoc = new VariableDefinition(method.Parameters[2].ParameterType);
                        var solidLoc = new VariableDefinition(solidRef);
                        var colorLoc = new VariableDefinition(colorRef);
                        var opLoc = new VariableDefinition(module.TypeSystem.Single);
                        var rLoc = new VariableDefinition(module.TypeSystem.Single);
                        var gLoc = new VariableDefinition(module.TypeSystem.Single);
                        var bLoc = new VariableDefinition(module.TypeSystem.Single);
                        var aLoc = new VariableDefinition(module.TypeSystem.Single);
                        var xLoc = new VariableDefinition(module.TypeSystem.Single);
                        var yLoc = new VariableDefinition(module.TypeSystem.Single);
                        var wLoc = new VariableDefinition(module.TypeSystem.Single);
                        var hLoc = new VariableDefinition(module.TypeSystem.Single);
                        var cpuLoc = new VariableDefinition(cpuCtxRef);
                        var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
                        foreach (var v in new[] { brushLoc, solidLoc, colorLoc, opLoc, rLoc, gLoc, bLoc, aLoc, xLoc, yLoc, wLoc, hLoc, cpuLoc, handleLoc })
                            method.Body.Variables.Add(v);
                        method.Body.InitLocals = true;
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Instructions.Clear();
                        // Bitmap fills (the checkerboard brush) have no device
                        // bitmap headless: paint the classic checker procedurally
                        // instead of resolving the brush.
                        var notBitmap = fil.Create(OpCodes.Nop);
                        MethodReference drawCheckerCpu = ImportCpuPaintMethod(module, cpuPaintPath, "DrawChecker", 8);
                        MethodReference objTypeRef = module.ImportReference(typeof(object).GetMethod("GetType", Type.EmptyTypes)!);
                        MethodReference typeNameRef = module.ImportReference(typeof(Type).GetMethod("get_FullName", Type.EmptyTypes)!);
                        MethodReference typeEqRef = module.ImportReference(typeof(string).GetMethod("op_Equality", new[] { typeof(string), typeof(string) })!);
                        fil.Emit(OpCodes.Ldarg_2);
                        fil.Emit(OpCodes.Callvirt, objTypeRef);
                        fil.Emit(OpCodes.Callvirt, typeNameRef);
                        fil.Emit(OpCodes.Ldstr, "PaintDotNet.UI.Media.BitmapBrush");
                        fil.Emit(OpCodes.Call, typeEqRef);
                        fil.Emit(OpCodes.Brfalse, notBitmap);
                        var bitmapRect = method.Parameters[1];
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Isinst, cpuCtxRef);
                        fil.Emit(OpCodes.Stloc, cpuLoc);
                        fil.Emit(OpCodes.Ldarga, bitmapRect);
                        fil.Emit(OpCodes.Call, rectX);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, xLoc);
                        fil.Emit(OpCodes.Ldarga, bitmapRect);
                        fil.Emit(OpCodes.Call, rectY);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, yLoc);
                        fil.Emit(OpCodes.Ldarga, bitmapRect);
                        fil.Emit(OpCodes.Call, rectW);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, wLoc);
                        fil.Emit(OpCodes.Ldarga, bitmapRect);
                        fil.Emit(OpCodes.Call, rectH);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, hLoc);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Callvirt, handleRef);
                        fil.Emit(OpCodes.Stloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, xLoc);
                        fil.Emit(OpCodes.Ldloc, yLoc);
                        fil.Emit(OpCodes.Ldloc, wLoc);
                        fil.Emit(OpCodes.Ldloc, hLoc);
                        fil.Emit(OpCodes.Ldc_I4, unchecked((int)0xFFFFFFFF));
                        fil.Emit(OpCodes.Ldc_I4, unchecked((int)0xFFC8C8C8));
                        fil.Emit(OpCodes.Ldc_I4_8);
                        fil.Emit(OpCodes.Call, drawCheckerCpu);
                        fil.Emit(OpCodes.Ret);
                        fil.Append(notBitmap);

                        var notSolid = fil.Create(OpCodes.Nop);
                        var notCpu = fil.Create(OpCodes.Nop);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Ldarg_2);
                        fil.Emit(OpCodes.Call, brushCache);
                        fil.Emit(OpCodes.Stloc, brushLoc);
                        fil.Emit(OpCodes.Ldloc, brushLoc);
                        fil.Emit(OpCodes.Isinst, solidRef);
                        fil.Emit(OpCodes.Stloc, solidLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Brfalse, notSolid);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getColor);
                        fil.Emit(OpCodes.Stloc, colorLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getOpacity);
                        fil.Emit(OpCodes.Stloc, opLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillR);
                        fil.Emit(OpCodes.Stloc, rLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillG);
                        fil.Emit(OpCodes.Stloc, gLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillB);
                        fil.Emit(OpCodes.Stloc, bLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillA);
                        fil.Emit(OpCodes.Stloc, aLoc);
                        var rectParam = method.Parameters[1];
                        fil.Emit(OpCodes.Ldarga, rectParam);
                        fil.Emit(OpCodes.Call, rectX);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, xLoc);
                        fil.Emit(OpCodes.Ldarga, rectParam);
                        fil.Emit(OpCodes.Call, rectY);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, yLoc);
                        fil.Emit(OpCodes.Ldarga, rectParam);
                        fil.Emit(OpCodes.Call, rectW);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, wLoc);
                        fil.Emit(OpCodes.Ldarga, rectParam);
                        fil.Emit(OpCodes.Call, rectH);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, hLoc);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Isinst, cpuCtxRef);
                        fil.Emit(OpCodes.Stloc, cpuLoc);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Brfalse, notCpu);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Callvirt, handleRef);
                        fil.Emit(OpCodes.Stloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, xLoc);
                        fil.Emit(OpCodes.Ldloc, yLoc);
                        fil.Emit(OpCodes.Ldloc, wLoc);
                        fil.Emit(OpCodes.Ldloc, hLoc);
                        fil.Emit(OpCodes.Ldloc, opLoc);
                        fil.Emit(OpCodes.Ldloc, rLoc);
                        fil.Emit(OpCodes.Ldloc, gLoc);
                        fil.Emit(OpCodes.Ldloc, bLoc);
                        fil.Emit(OpCodes.Ldloc, aLoc);
                        fil.Emit(OpCodes.Call, cpuFill);
                        fil.Emit(OpCodes.Ret);
                        fil.Append(notSolid);
                        var notGradient = fil.Create(OpCodes.Nop);
                        var gradTypeRef = new TypeReference(
                            "PaintDotNet.Direct2D1", "CpuLinearGradientBrush", module, module);
                        var gradLoc = new VariableDefinition(gradTypeRef);
                        method.Body.Variables.Add(gradLoc);
                        var gradLutRef = new FieldReference("_lut", module.TypeSystem.Int64, gradTypeRef);
                        var gradX0Ref = new FieldReference("_x0", module.TypeSystem.Single, gradTypeRef);
                        var gradY0Ref = new FieldReference("_y0", module.TypeSystem.Single, gradTypeRef);
                        var gradX1Ref = new FieldReference("_x1", module.TypeSystem.Single, gradTypeRef);
                        var gradY1Ref = new FieldReference("_y1", module.TypeSystem.Single, gradTypeRef);
                        var gradSpreadRef = new FieldReference("_spread", module.TypeSystem.Int32, gradTypeRef);
                        MethodReference cpuGradFill = ImportCpuPaintMethod(module, cpuPaintPath, "FillRectGradient", 11);
                        fil.Emit(OpCodes.Ldloc, brushLoc);
                        fil.Emit(OpCodes.Isinst, gradTypeRef);
                        fil.Emit(OpCodes.Stloc, gradLoc);
                        fil.Emit(OpCodes.Ldloc, gradLoc);
                        fil.Emit(OpCodes.Brfalse, notGradient);
                        fil.Emit(OpCodes.Ldarga, rectParam);
                        fil.Emit(OpCodes.Call, rectX);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, xLoc);
                        fil.Emit(OpCodes.Ldarga, rectParam);
                        fil.Emit(OpCodes.Call, rectY);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, yLoc);
                        fil.Emit(OpCodes.Ldarga, rectParam);
                        fil.Emit(OpCodes.Call, rectW);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, wLoc);
                        fil.Emit(OpCodes.Ldarga, rectParam);
                        fil.Emit(OpCodes.Call, rectH);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, hLoc);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Isinst, cpuCtxRef);
                        fil.Emit(OpCodes.Stloc, cpuLoc);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Brfalse, notCpu);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Callvirt, handleRef);
                        fil.Emit(OpCodes.Stloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, xLoc);
                        fil.Emit(OpCodes.Ldloc, yLoc);
                        fil.Emit(OpCodes.Ldloc, wLoc);
                        fil.Emit(OpCodes.Ldloc, hLoc);
                        fil.Emit(OpCodes.Ldloc, gradLoc);
                        fil.Emit(OpCodes.Ldfld, gradLutRef);
                        fil.Emit(OpCodes.Ldloc, gradLoc);
                        fil.Emit(OpCodes.Ldfld, gradX0Ref);
                        fil.Emit(OpCodes.Ldloc, gradLoc);
                        fil.Emit(OpCodes.Ldfld, gradY0Ref);
                        fil.Emit(OpCodes.Ldloc, gradLoc);
                        fil.Emit(OpCodes.Ldfld, gradX1Ref);
                        fil.Emit(OpCodes.Ldloc, gradLoc);
                        fil.Emit(OpCodes.Ldfld, gradY1Ref);
                        fil.Emit(OpCodes.Ldloc, gradLoc);
                        fil.Emit(OpCodes.Ldfld, gradSpreadRef);
                        fil.Emit(OpCodes.Call, cpuGradFill);
                        fil.Emit(OpCodes.Ret);
                        fil.Append(notGradient);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: FillRectangle needs a solid-color or gradient brush");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        fil.Append(notCpu);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: FillRectangle needs a CpuDrawingContext");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        method.Body.MaxStackSize = 32;
                        continue;
                    }
                    if (cpuPaintPath is not null
                        && type.FullName == "PaintDotNet.Direct2D1.DrawingContextExtensions"
                        && method.Name == "FillEllipse"
                        && method.Parameters.Count == 3
                        && method.HasBody)
                    {
                        // Ellipse fills (brush cursor rings, round icon
                        // parts) take the same CPU route as FillGeometry.
                        var fil = method.Body.GetILProcessor();
                        MethodReference? brushCache = null;
                        foreach (var ins in method.Body.Instructions)
                        {
                            if (ins.OpCode == OpCodes.Call
                                && ins.Operand is Mono.Cecil.GenericInstanceMethod gim
                                && gim.Name == "GetCachedOrCreateResource"
                                && gim.GenericArguments.Count == 1
                                && gim.GenericArguments[0].FullName
                                    == "PaintDotNet.Direct2D1.IDeviceBrush")
                                brushCache = gim;
                        }
                        if (brushCache is null)
                            throw new InvalidOperationException(
                                "CpuFill: FillEllipse brush cache call missing");
                        TypeReference solidRef = OwnedCpuRef(module, "PaintDotNet.Direct2D1.ISolidColorBrush");
                        MethodReference getColor = VirtualCpuCall(module, "PaintDotNet.Direct2D1.ISolidColorBrush", "get_Color", 0);
                        MethodReference getOpacity = VirtualCpuCall(module, "PaintDotNet.Direct2D1.IDeviceBrush", "get_Opacity", 0);
                        MethodReference fillR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
                        MethodReference fillG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
                        MethodReference fillB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
                        MethodReference fillA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
                        TypeReference colorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.ColorRgba128Float");
                        MethodReference ellC = StructCpuGetter(module, "PaintDotNet.Rendering.EllipseDouble", "get_Center");
                        MethodReference ellRx = StructCpuGetter(module, "PaintDotNet.Rendering.EllipseDouble", "get_RadiusX");
                        MethodReference ellRy = StructCpuGetter(module, "PaintDotNet.Rendering.EllipseDouble", "get_RadiusY");
                        MethodReference ptX = StructCpuGetter(module, "PaintDotNet.Rendering.Point2Double", "get_X");
                        MethodReference ptY = StructCpuGetter(module, "PaintDotNet.Rendering.Point2Double", "get_Y");
                        var cpuCtxRef = new TypeReference(
                            "PaintDotNet.Direct2D1", "CpuDrawingContext", module, module);
                        var handleRef = new MethodReference(
                            "GetCpuTargetHandle", module.TypeSystem.Int64, cpuCtxRef) { HasThis = true };
                        MethodReference cpuFill = ImportCpuPaintMethod(module, cpuPaintPath, "FillEllipse", 10);
                        MethodReference nseCtor = module.ImportReference(
                            typeof(NotSupportedException).GetConstructor([typeof(string)])!);
                        var brushLoc = new VariableDefinition(method.Parameters[2].ParameterType);
                        var solidLoc = new VariableDefinition(solidRef);
                        var colorLoc = new VariableDefinition(colorRef);
                        var opLoc = new VariableDefinition(module.TypeSystem.Single);
                        var rLoc = new VariableDefinition(module.TypeSystem.Single);
                        var gLoc = new VariableDefinition(module.TypeSystem.Single);
                        var bLoc = new VariableDefinition(module.TypeSystem.Single);
                        var aLoc = new VariableDefinition(module.TypeSystem.Single);
                        var ellParam = method.Parameters[1];
                        TypeReference ptRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Rendering.Point2Double");
                        var cLoc = new VariableDefinition(ptRef);
                        var xLoc = new VariableDefinition(module.TypeSystem.Single);
                        var yLoc = new VariableDefinition(module.TypeSystem.Single);
                        var rxLoc = new VariableDefinition(module.TypeSystem.Single);
                        var ryLoc = new VariableDefinition(module.TypeSystem.Single);
                        var cpuLoc = new VariableDefinition(cpuCtxRef);
                        var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
                        foreach (var v in new[] { brushLoc, solidLoc, colorLoc, opLoc, rLoc, gLoc, bLoc, aLoc, cLoc, xLoc, yLoc, rxLoc, ryLoc, cpuLoc, handleLoc })
                            method.Body.Variables.Add(v);
                        method.Body.InitLocals = true;
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Instructions.Clear();
                        var notSolid = fil.Create(OpCodes.Nop);
                        var notCpu = fil.Create(OpCodes.Nop);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Ldarg_2);
                        fil.Emit(OpCodes.Call, brushCache);
                        fil.Emit(OpCodes.Stloc, brushLoc);
                        fil.Emit(OpCodes.Ldloc, brushLoc);
                        fil.Emit(OpCodes.Isinst, solidRef);
                        fil.Emit(OpCodes.Stloc, solidLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Brfalse, notSolid);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getColor);
                        fil.Emit(OpCodes.Stloc, colorLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getOpacity);
                        fil.Emit(OpCodes.Stloc, opLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillR);
                        fil.Emit(OpCodes.Stloc, rLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillG);
                        fil.Emit(OpCodes.Stloc, gLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillB);
                        fil.Emit(OpCodes.Stloc, bLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillA);
                        fil.Emit(OpCodes.Stloc, aLoc);
                        fil.Emit(OpCodes.Ldarga, ellParam);
                        fil.Emit(OpCodes.Call, ellC);
                        fil.Emit(OpCodes.Stloc, cLoc);
                        fil.Emit(OpCodes.Ldloca, cLoc);
                        fil.Emit(OpCodes.Call, ptX);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, xLoc);
                        fil.Emit(OpCodes.Ldloca, cLoc);
                        fil.Emit(OpCodes.Call, ptY);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, yLoc);
                        fil.Emit(OpCodes.Ldarga, ellParam);
                        fil.Emit(OpCodes.Call, ellRx);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, rxLoc);
                        fil.Emit(OpCodes.Ldarga, ellParam);
                        fil.Emit(OpCodes.Call, ellRy);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, ryLoc);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Isinst, cpuCtxRef);
                        fil.Emit(OpCodes.Stloc, cpuLoc);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Brfalse, notCpu);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Callvirt, handleRef);
                        fil.Emit(OpCodes.Stloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, xLoc);
                        fil.Emit(OpCodes.Ldloc, yLoc);
                        fil.Emit(OpCodes.Ldloc, rxLoc);
                        fil.Emit(OpCodes.Ldloc, ryLoc);
                        fil.Emit(OpCodes.Ldloc, opLoc);
                        fil.Emit(OpCodes.Ldloc, rLoc);
                        fil.Emit(OpCodes.Ldloc, gLoc);
                        fil.Emit(OpCodes.Ldloc, bLoc);
                        fil.Emit(OpCodes.Ldloc, aLoc);
                        fil.Emit(OpCodes.Call, cpuFill);
                        fil.Emit(OpCodes.Ret);
                        fil.Append(notSolid);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: FillEllipse needs an ISolidColorBrush");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        fil.Append(notCpu);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: FillEllipse needs a CpuDrawingContext");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        method.Body.MaxStackSize = 32;
                        continue;
                    }
                    if (cpuPaintPath is not null
                        && type.FullName == "PaintDotNet.Direct2D1.DrawingContextExtensions"
                        && method.Name == "UseLayer"
                        && method.Parameters.Count == 8
                        && method.HasBody)
                    {
                        // Shape rasterization wraps its fills in a clip
                        // layer whose geometry has no CPU resource: feed
                        // the SafeGet a null provider so the layer pushes
                        // unclipped (PushLayer/PopLayer are headless
                        // no-ops). Exactly one ldarg.2 feeds that call.
                        var geoFeeds = method.Body.Instructions.Where(i =>
                            i.OpCode == OpCodes.Ldarg_2).ToList();
                        if (geoFeeds.Count != 1)
                            throw new InvalidOperationException(
                                "CpuFill: UseLayer geometry feed shape changed");
                        // In-place opcode swap: the Instruction object
                        // stays put, so branch targets and handler
                        // boundaries pointing at it stay valid.
                        geoFeeds[0].OpCode = OpCodes.Ldnull;
                        geoFeeds[0].Operand = null;
                        continue;
                    }
                    if (cpuPaintPath is not null
                        && type.FullName == "PaintDotNet.Direct2D1.DrawingContextExtensions"
                        && method.Name == "DrawGeometry"
                        && method.Parameters.Count == 5
                        && method.HasBody)
                    {
                        // Icon outlines stroke vector geometries: route
                        // the brush through the CPU cache and stamp the
                        // flattened loops in CpuPaint (dash/cap styles
                        // render solid; the style provider stays
                        // unresolved by design).
                        var fil = method.Body.GetILProcessor();
                        MethodReference? brushCache = null;
                        foreach (var ins in method.Body.Instructions)
                        {
                            if (ins.OpCode == OpCodes.Call
                                && ins.Operand is Mono.Cecil.GenericInstanceMethod gim
                                && gim.Name == "GetCachedOrCreateResource"
                                && gim.GenericArguments.Count == 1
                                && gim.GenericArguments[0].FullName
                                    == "PaintDotNet.Direct2D1.IDeviceBrush")
                                brushCache = gim;
                        }
                        if (brushCache is null)
                            throw new InvalidOperationException(
                                "CpuFill: DrawGeometry brush cache call missing");
                        TypeReference solidRef = OwnedCpuRef(module, "PaintDotNet.Direct2D1.ISolidColorBrush");
                        MethodReference getColor = VirtualCpuCall(module, "PaintDotNet.Direct2D1.ISolidColorBrush", "get_Color", 0);
                        MethodReference getOpacity = VirtualCpuCall(module, "PaintDotNet.Direct2D1.IDeviceBrush", "get_Opacity", 0);
                        MethodReference fillR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
                        MethodReference fillG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
                        MethodReference fillB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
                        MethodReference fillA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
                        TypeReference colorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.ColorRgba128Float");
                        var cpuCtxRef = new TypeReference(
                            "PaintDotNet.Direct2D1", "CpuDrawingContext", module, module);
                        var handleRef = new MethodReference(
                            "GetCpuTargetHandle", module.TypeSystem.Int64, cpuCtxRef) { HasThis = true };
                        MethodReference cpuStroke = ImportCpuPaintMethod(module, cpuPaintPath, "StrokeGeometry", 8);
                        MethodReference nseCtor = module.ImportReference(
                            typeof(NotSupportedException).GetConstructor([typeof(string)])!);
                        var brushLoc = new VariableDefinition(method.Parameters[2].ParameterType);
                        var solidLoc = new VariableDefinition(solidRef);
                        var colorLoc = new VariableDefinition(colorRef);
                        var opLoc = new VariableDefinition(module.TypeSystem.Single);
                        var wLoc = new VariableDefinition(module.TypeSystem.Single);
                        var rLoc = new VariableDefinition(module.TypeSystem.Single);
                        var gLoc = new VariableDefinition(module.TypeSystem.Single);
                        var bLoc = new VariableDefinition(module.TypeSystem.Single);
                        var aLoc = new VariableDefinition(module.TypeSystem.Single);
                        var cpuLoc = new VariableDefinition(cpuCtxRef);
                        var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
                        foreach (var v in new[] { brushLoc, solidLoc, colorLoc, opLoc, wLoc, rLoc, gLoc, bLoc, aLoc, cpuLoc, handleLoc })
                            method.Body.Variables.Add(v);
                        method.Body.InitLocals = true;
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Instructions.Clear();
                        var notSolid = fil.Create(OpCodes.Nop);
                        var notCpu = fil.Create(OpCodes.Nop);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Ldarg_2);
                        fil.Emit(OpCodes.Call, brushCache);
                        fil.Emit(OpCodes.Stloc, brushLoc);
                        fil.Emit(OpCodes.Ldloc, brushLoc);
                        fil.Emit(OpCodes.Isinst, solidRef);
                        fil.Emit(OpCodes.Stloc, solidLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Brfalse, notSolid);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getColor);
                        fil.Emit(OpCodes.Stloc, colorLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getOpacity);
                        fil.Emit(OpCodes.Stloc, opLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillR);
                        fil.Emit(OpCodes.Stloc, rLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillG);
                        fil.Emit(OpCodes.Stloc, gLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillB);
                        fil.Emit(OpCodes.Stloc, bLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillA);
                        fil.Emit(OpCodes.Stloc, aLoc);
                        fil.Emit(OpCodes.Ldarg_3);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, wLoc);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Isinst, cpuCtxRef);
                        fil.Emit(OpCodes.Stloc, cpuLoc);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Brfalse, notCpu);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Callvirt, handleRef);
                        fil.Emit(OpCodes.Stloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, handleLoc);
                        fil.Emit(OpCodes.Ldarg_1);
                        fil.Emit(OpCodes.Ldloc, wLoc);
                        fil.Emit(OpCodes.Ldloc, opLoc);
                        fil.Emit(OpCodes.Ldloc, rLoc);
                        fil.Emit(OpCodes.Ldloc, gLoc);
                        fil.Emit(OpCodes.Ldloc, bLoc);
                        fil.Emit(OpCodes.Ldloc, aLoc);
                        fil.Emit(OpCodes.Call, cpuStroke);
                        fil.Emit(OpCodes.Ret);
                        fil.Append(notSolid);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: DrawGeometry needs an ISolidColorBrush");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        fil.Append(notCpu);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: DrawGeometry needs a CpuDrawingContext");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        method.Body.MaxStackSize = 32;
                        continue;
                    }
                    if (cpuPaintPath is not null
                        && type.FullName == "PaintDotNet.Direct2D1.DrawingContextExtensions"
                        && method.Name == "DrawGeometry"
                        && method.Parameters.Count == 3
                        && method.HasBody)
                    {
                        // Pen-provided outlines: brush and width come off
                        // IPenProvider; the stroke style stays unresolved.
                        var fil = method.Body.GetILProcessor();
                        MethodReference? brushCache = null;
                        foreach (var ins in method.Body.Instructions)
                        {
                            if (ins.OpCode == OpCodes.Call
                                && ins.Operand is Mono.Cecil.GenericInstanceMethod gim
                                && gim.GenericArguments.Count == 1
                                && gim.GenericArguments[0].FullName
                                    == "PaintDotNet.Direct2D1.IDeviceBrush"
                                && (gim.Name == "SafeGetCachedOrCreateResource"
                                    || gim.Name == "GetCachedOrCreateResource"))
                                brushCache = gim;
                        }
                        if (brushCache is null)
                            throw new InvalidOperationException(
                                "CpuFill: DrawGeometry pen brush cache call missing");
                        TypeReference solidRef = OwnedCpuRef(module, "PaintDotNet.Direct2D1.ISolidColorBrush");
                        MethodReference getColor = VirtualCpuCall(module, "PaintDotNet.Direct2D1.ISolidColorBrush", "get_Color", 0);
                        MethodReference getOpacity = VirtualCpuCall(module, "PaintDotNet.Direct2D1.IDeviceBrush", "get_Opacity", 0);
                        MethodReference penBrush = VirtualCpuCall(module, "PaintDotNet.Direct2D1.IPenProvider", "get_Brush", 0);
                        MethodReference penWidth = VirtualCpuCall(module, "PaintDotNet.Direct2D1.IPenProvider", "get_StrokeWidth", 0);
                        MethodReference fillR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
                        MethodReference fillG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
                        MethodReference fillB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
                        MethodReference fillA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
                        TypeReference colorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.ColorRgba128Float");
                        var cpuCtxRef = new TypeReference(
                            "PaintDotNet.Direct2D1", "CpuDrawingContext", module, module);
                        var handleRef = new MethodReference(
                            "GetCpuTargetHandle", module.TypeSystem.Int64, cpuCtxRef) { HasThis = true };
                        MethodReference cpuStroke = ImportCpuPaintMethod(module, cpuPaintPath, "StrokeGeometry", 8);
                        MethodReference nseCtor = module.ImportReference(
                            typeof(NotSupportedException).GetConstructor([typeof(string)])!);
                        var penProvLoc = new VariableDefinition(penBrush.ReturnType);
                        var brushLoc = new VariableDefinition(module.TypeSystem.Object);
                        var solidLoc = new VariableDefinition(solidRef);
                        var colorLoc = new VariableDefinition(colorRef);
                        var opLoc = new VariableDefinition(module.TypeSystem.Single);
                        var wLoc = new VariableDefinition(module.TypeSystem.Single);
                        var rLoc = new VariableDefinition(module.TypeSystem.Single);
                        var gLoc = new VariableDefinition(module.TypeSystem.Single);
                        var bLoc = new VariableDefinition(module.TypeSystem.Single);
                        var aLoc = new VariableDefinition(module.TypeSystem.Single);
                        var cpuLoc = new VariableDefinition(cpuCtxRef);
                        var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
                        foreach (var v in new[] { penProvLoc, brushLoc, solidLoc, colorLoc, opLoc, wLoc, rLoc, gLoc, bLoc, aLoc, cpuLoc, handleLoc })
                            method.Body.Variables.Add(v);
                        method.Body.InitLocals = true;
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Instructions.Clear();
                        var notSolid = fil.Create(OpCodes.Nop);
                        var notCpu = fil.Create(OpCodes.Nop);
                        fil.Emit(OpCodes.Ldarg_2);
                        fil.Emit(OpCodes.Callvirt, penBrush);
                        fil.Emit(OpCodes.Stloc, penProvLoc);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Ldloc, penProvLoc);
                        fil.Emit(OpCodes.Call, brushCache);
                        fil.Emit(OpCodes.Stloc, brushLoc);
                        fil.Emit(OpCodes.Ldloc, brushLoc);
                        fil.Emit(OpCodes.Isinst, solidRef);
                        fil.Emit(OpCodes.Stloc, solidLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Brfalse, notSolid);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getColor);
                        fil.Emit(OpCodes.Stloc, colorLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getOpacity);
                        fil.Emit(OpCodes.Stloc, opLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillR);
                        fil.Emit(OpCodes.Stloc, rLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillG);
                        fil.Emit(OpCodes.Stloc, gLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillB);
                        fil.Emit(OpCodes.Stloc, bLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillA);
                        fil.Emit(OpCodes.Stloc, aLoc);
                        fil.Emit(OpCodes.Ldarg_2);
                        fil.Emit(OpCodes.Callvirt, penWidth);
                        fil.Emit(OpCodes.Stloc, wLoc);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Isinst, cpuCtxRef);
                        fil.Emit(OpCodes.Stloc, cpuLoc);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Brfalse, notCpu);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Callvirt, handleRef);
                        fil.Emit(OpCodes.Stloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, handleLoc);
                        fil.Emit(OpCodes.Ldarg_1);
                        fil.Emit(OpCodes.Ldloc, wLoc);
                        fil.Emit(OpCodes.Ldloc, opLoc);
                        fil.Emit(OpCodes.Ldloc, rLoc);
                        fil.Emit(OpCodes.Ldloc, gLoc);
                        fil.Emit(OpCodes.Ldloc, bLoc);
                        fil.Emit(OpCodes.Ldloc, aLoc);
                        fil.Emit(OpCodes.Call, cpuStroke);
                        fil.Emit(OpCodes.Ret);
                        fil.Append(notSolid);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: DrawGeometry needs an ISolidColorBrush");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        fil.Append(notCpu);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: DrawGeometry needs a CpuDrawingContext");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        method.Body.MaxStackSize = 32;
                        continue;
                    }
                    if (cpuPaintPath is not null
                        && type.FullName == "PaintDotNet.Direct2D1.DrawingContextExtensions"
                        && method.Name == "DrawTextLayout"
                        && method.Parameters.Count == 5
                        && method.HasBody)
                    {
                        // Icon lettering renders through DirectWrite,
                        // which is absent headless: the layout is first
                        // rasterized through the browser text engine
                        // (TextLayoutRaster reads the layout's own font
                        // by name, so no PDN bind), tinted and blitted
                        // by CpuPaint. Only when no raster comes back
                        // do short strings fall to the embedded 5x7
                        // font (the doubles overload delegates here, so
                        // one rule covers both; draw options stay
                        // unresolved).
                        var fil = method.Body.GetILProcessor();
                        MethodReference? brushCache = null;
                        foreach (var ins in method.Body.Instructions)
                        {
                            if (ins.OpCode == OpCodes.Call
                                && ins.Operand is Mono.Cecil.GenericInstanceMethod gim
                                && gim.Name == "GetCachedOrCreateResource"
                                && gim.GenericArguments.Count == 1
                                && gim.GenericArguments[0].FullName
                                    == "PaintDotNet.Direct2D1.IDeviceBrush")
                                brushCache = gim;
                        }
                        if (brushCache is null)
                            throw new InvalidOperationException(
                                "CpuFill: DrawTextLayout brush cache call missing");
                        TypeReference solidRef = OwnedCpuRef(module, "PaintDotNet.Direct2D1.ISolidColorBrush");
                        MethodReference getColor = VirtualCpuCall(module, "PaintDotNet.Direct2D1.ISolidColorBrush", "get_Color", 0);
                        MethodReference getOpacity = VirtualCpuCall(module, "PaintDotNet.Direct2D1.IDeviceBrush", "get_Opacity", 0);
                        MethodReference fillR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
                        MethodReference fillG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
                        MethodReference fillB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
                        MethodReference fillA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
                        TypeReference colorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.ColorRgba128Float");
                        MethodReference ptX = StructCpuGetter(module, "PaintDotNet.Rendering.Point2Double", "get_X");
                        MethodReference ptY = StructCpuGetter(module, "PaintDotNet.Rendering.Point2Double", "get_Y");
                        var cpuCtxRef = new TypeReference(
                            "PaintDotNet.Direct2D1", "CpuDrawingContext", module, module);
                        var handleRef = new MethodReference(
                            "GetCpuTargetHandle", module.TypeSystem.Int64, cpuCtxRef) { HasThis = true };
                        MethodReference cpuText = ImportCpuPaintMethod(module, cpuPaintPath, "DrawText", 9);
                        MethodReference cpuBlit = ImportCpuPaintMethod(module, cpuPaintPath, "BlitRaster", 11);
                        AssemblyNameReference? textDrawingScope = null;
                        foreach (var tdr in module.AssemblyReferences)
                        {
                            if (tdr.Name == "WfWasm.Drawing" || tdr.Name == "System.Drawing" || tdr.Name == "System.Drawing.Common")
                            { textDrawingScope = tdr; break; }
                        }
                        if (textDrawingScope is null)
                            throw new InvalidOperationException(
                                "CpuFill: DrawTextLayout drawing scope missing");
                        var layoutRasterRef = new TypeReference(
                            "System.Drawing", "LayoutRaster", module, textDrawingScope);
                        var textRasterRef = new TypeReference(
                            "System.Drawing", "TextLayoutRaster", module, textDrawingScope);
                        var tryRaster = new MethodReference(
                            "TryRasterLayout", layoutRasterRef, textRasterRef) { HasThis = false };
                        tryRaster.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                        tryRaster.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
                        tryRaster.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                        var rasterWidthFld = new FieldReference("Width", module.TypeSystem.Int32, layoutRasterRef);
                        var rasterHeightFld = new FieldReference("Height", module.TypeSystem.Int32, layoutRasterRef);
                        var rasterRgbaFld = new FieldReference("Rgba", new ArrayType(module.TypeSystem.Byte), layoutRasterRef);
                        MethodReference nseCtor = module.ImportReference(
                            typeof(NotSupportedException).GetConstructor([typeof(string)])!);
                        var brushLoc = new VariableDefinition(method.Parameters[3].ParameterType);
                        var solidLoc = new VariableDefinition(solidRef);
                        var colorLoc = new VariableDefinition(colorRef);
                        var opLoc = new VariableDefinition(module.TypeSystem.Single);
                        var rLoc = new VariableDefinition(module.TypeSystem.Single);
                        var gLoc = new VariableDefinition(module.TypeSystem.Single);
                        var bLoc = new VariableDefinition(module.TypeSystem.Single);
                        var aLoc = new VariableDefinition(module.TypeSystem.Single);
                        var ptParam = method.Parameters[1];
                        var xLoc = new VariableDefinition(module.TypeSystem.Single);
                        var yLoc = new VariableDefinition(module.TypeSystem.Single);
                        var cpuLoc = new VariableDefinition(cpuCtxRef);
                        var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
                        var rasterLoc = new VariableDefinition(layoutRasterRef);
                        foreach (var v in new[] { brushLoc, solidLoc, colorLoc, opLoc, rLoc, gLoc, bLoc, aLoc, xLoc, yLoc, cpuLoc, handleLoc, rasterLoc })
                            method.Body.Variables.Add(v);
                        method.Body.InitLocals = true;
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Instructions.Clear();
                        var notSolid = fil.Create(OpCodes.Nop);
                        var notCpu = fil.Create(OpCodes.Nop);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Ldarg_3);
                        fil.Emit(OpCodes.Call, brushCache);
                        fil.Emit(OpCodes.Stloc, brushLoc);
                        fil.Emit(OpCodes.Ldloc, brushLoc);
                        fil.Emit(OpCodes.Isinst, solidRef);
                        fil.Emit(OpCodes.Stloc, solidLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Brfalse, notSolid);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getColor);
                        fil.Emit(OpCodes.Stloc, colorLoc);
                        fil.Emit(OpCodes.Ldloc, solidLoc);
                        fil.Emit(OpCodes.Callvirt, getOpacity);
                        fil.Emit(OpCodes.Stloc, opLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillR);
                        fil.Emit(OpCodes.Stloc, rLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillG);
                        fil.Emit(OpCodes.Stloc, gLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillB);
                        fil.Emit(OpCodes.Stloc, bLoc);
                        fil.Emit(OpCodes.Ldloca, colorLoc);
                        fil.Emit(OpCodes.Call, fillA);
                        fil.Emit(OpCodes.Stloc, aLoc);
                        fil.Emit(OpCodes.Ldarga, ptParam);
                        fil.Emit(OpCodes.Call, ptX);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, xLoc);
                        fil.Emit(OpCodes.Ldarga, ptParam);
                        fil.Emit(OpCodes.Call, ptY);
                        fil.Emit(OpCodes.Conv_R4);
                        fil.Emit(OpCodes.Stloc, yLoc);
                        fil.Emit(OpCodes.Ldarg_0);
                        fil.Emit(OpCodes.Isinst, cpuCtxRef);
                        fil.Emit(OpCodes.Stloc, cpuLoc);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Brfalse, notCpu);
                        fil.Emit(OpCodes.Ldloc, cpuLoc);
                        fil.Emit(OpCodes.Callvirt, handleRef);
                        fil.Emit(OpCodes.Stloc, handleLoc);
                        var useCpuText = fil.Create(OpCodes.Nop);
                        fil.Emit(OpCodes.Ldarg_2);
                        fil.Emit(OpCodes.Ldc_R4, 96f);
                        fil.Emit(OpCodes.Ldc_I4, 2048);
                        fil.Emit(OpCodes.Call, tryRaster);
                        fil.Emit(OpCodes.Stloc, rasterLoc);
                        fil.Emit(OpCodes.Ldloc, rasterLoc);
                        fil.Emit(OpCodes.Brfalse, useCpuText);
                        fil.Emit(OpCodes.Ldloc, handleLoc);
                        fil.Emit(OpCodes.Ldloc, rasterLoc);
                        fil.Emit(OpCodes.Ldfld, rasterRgbaFld);
                        fil.Emit(OpCodes.Ldloc, rasterLoc);
                        fil.Emit(OpCodes.Ldfld, rasterWidthFld);
                        fil.Emit(OpCodes.Ldloc, rasterLoc);
                        fil.Emit(OpCodes.Ldfld, rasterHeightFld);
                        fil.Emit(OpCodes.Ldloc, xLoc);
                        fil.Emit(OpCodes.Ldloc, yLoc);
                        fil.Emit(OpCodes.Ldloc, opLoc);
                        fil.Emit(OpCodes.Ldloc, rLoc);
                        fil.Emit(OpCodes.Ldloc, gLoc);
                        fil.Emit(OpCodes.Ldloc, bLoc);
                        fil.Emit(OpCodes.Ldloc, aLoc);
                        fil.Emit(OpCodes.Call, cpuBlit);
                        fil.Emit(OpCodes.Ret);
                        fil.Append(useCpuText);
                        fil.Emit(OpCodes.Ldloc, handleLoc);
                        fil.Emit(OpCodes.Ldarg_2);
                        fil.Emit(OpCodes.Ldloc, xLoc);
                        fil.Emit(OpCodes.Ldloc, yLoc);
                        fil.Emit(OpCodes.Ldloc, opLoc);
                        fil.Emit(OpCodes.Ldloc, rLoc);
                        fil.Emit(OpCodes.Ldloc, gLoc);
                        fil.Emit(OpCodes.Ldloc, bLoc);
                        fil.Emit(OpCodes.Ldloc, aLoc);
                        fil.Emit(OpCodes.Call, cpuText);
                        fil.Emit(OpCodes.Ret);
                        fil.Append(notSolid);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: DrawTextLayout needs an ISolidColorBrush");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        fil.Append(notCpu);
                        fil.Emit(OpCodes.Ldstr, "CpuFill: DrawTextLayout needs a CpuDrawingContext");
                        fil.Emit(OpCodes.Newobj, nseCtor);
                        fil.Emit(OpCodes.Throw);
                        method.Body.MaxStackSize = 32;
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Runtime.PersistedObject`1"
                        && method.Name == "PersistToDisk"
                        && method.Parameters.Count == 1
                        && method.HasBody)
                    {
                        // Settings persistence has no disk headless (and
                        // BinaryFormatter is removed in net9): the
                        // background flush then turns into a silent skip
                        // instead of a per-boot PlatformNotSupported.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (cpuPaintPath is not null
                        && type.FullName == "PaintDotNet.Direct2D1.PushTextRenderingModeScope"
                        && method.Name == ".ctor"
                        && method.Parameters.Count == 2
                        && method.HasBody)
                    {
                        // The scope constructor reaches the D2D text
                        // parameter cache (null headless) even when the
                        // context reports no parameters: the scope is
                        // inert headless (Dispose null-guards), so leave
                        // its fields nulled (explicit stores keep both
                        // class and struct ctors verifiable).
                        var nullFields = type.Fields.Where(f =>
                            f.Name == "deviceContext" || f.Name == "oldTextParameters").ToList();
                        if (nullFields.Count != 2)
                            throw new InvalidOperationException(
                                "CpuFill: PushTextRenderingModeScope fields changed");
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        foreach (var nf in nullFields)
                        {
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Stfld, nf));
                        }
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (cpuPaintPath is not null
                        && type.FullName == "PaintDotNet.UI.Media.UITextFactory"
                        && method.Name == "AdjustFontSizeToFitLayoutSize"
                        && method.Parameters.Count == 6
                        && method.HasBody)
                    {
                        // Font-size fitting measures through the D2D text
                        // layout; headless, metrics come from the same
                        // 5x7 advance the DrawText path uses (len x 6/7 x
                        // size, single-line height). Only the 4-step
                        // measure is swapped; the shrink loop and the
                        // extents rescale stay pristine. Branch targets
                        // into the removed steps retarget to the swap.
                        var seq = method.Body.Instructions;
                        int at = -1;
                        for (int k = 0; k + 3 < seq.Count; k++)
                        {
                            if (seq[k].OpCode == OpCodes.Ldarg_1
                                && seq[k + 1].OpCode == OpCodes.Ldarg_2
                                && seq[k + 2].OpCode == OpCodes.Call
                                && seq[k + 2].Operand is Mono.Cecil.GenericInstanceMethod gim
                                && gim.Name == "GetCachedOrCreateResource"
                                && gim.GenericArguments.Count == 1
                                && gim.GenericArguments[0].FullName
                                    == "PaintDotNet.DirectWrite.ITextLayout"
                                && seq[k + 3].OpCode == OpCodes.Callvirt
                                && seq[k + 3].Operand is MethodReference getMet
                                && getMet.Name == "get_Metrics")
                            {
                                at = k;
                                break;
                            }
                        }
                        if (at < 0)
                            throw new InvalidOperationException(
                                "CpuFill: AdjustFontSize measure shape changed");
                        if (method.Body.ExceptionHandlers.Count != 0)
                            throw new InvalidOperationException(
                                "CpuFill: AdjustFontSize has handlers");
                        var removed = new HashSet<Mono.Cecil.Cil.Instruction>(
                            new[] { seq[at], seq[at + 1], seq[at + 2], seq[at + 3] });
                        var adj = method.Body.GetILProcessor();
                        TypeReference metricsRef = LocalOrOwnedCpuRef(module, "PaintDotNet.DirectWrite.TextMetrics");
                        MethodDefinition? metricsCtor = metricsRef.Resolve()?.Methods
                            .FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 10);
                        if (metricsCtor is null)
                            throw new InvalidOperationException(
                                "CpuFill: TextMetrics..ctor/10 missing");
                        MethodReference metricsNew = module.ImportReference(metricsCtor);
                        MethodReference getText = VirtualCpuCall(module, "PaintDotNet.UI.Media.TextLayout", "get_Text", 0);
                        MethodReference getSize = VirtualCpuCall(module, "PaintDotNet.UI.Media.TextFormat", "get_FontSize", 0);
                        MethodReference getLength = module.ImportReference(
                            typeof(string).GetMethod("get_Length", Type.EmptyTypes)!);
                        var first = adj.Create(OpCodes.Ldarg_2);
                        var fresh = new List<Mono.Cecil.Cil.Instruction>
                        {
                            first,
                            adj.Create(OpCodes.Callvirt, getText),
                            adj.Create(OpCodes.Callvirt, getLength),
                            adj.Create(OpCodes.Conv_R8),
                            adj.Create(OpCodes.Ldc_R8, 6.0),
                            adj.Create(OpCodes.Mul),
                            adj.Create(OpCodes.Ldarg_2),
                            adj.Create(OpCodes.Callvirt, getSize),
                            adj.Create(OpCodes.Mul),
                            adj.Create(OpCodes.Ldc_R8, 7.0),
                            adj.Create(OpCodes.Div),
                            adj.Create(OpCodes.Conv_R4),
                            adj.Create(OpCodes.Ldarg_2),
                            adj.Create(OpCodes.Callvirt, getSize),
                            adj.Create(OpCodes.Conv_R4),
                        };
                        // Stack is [w, h]: spill to temps, then push the
                        // ten ctor args (0,0,w,w,h,maxW,maxH,h,1,1); the
                        // shrink loop only reads Width/Height.
                        var wLoc = new VariableDefinition(module.TypeSystem.Single);
                        var hLoc = new VariableDefinition(module.TypeSystem.Single);
                        method.Body.Variables.Add(wLoc);
                        method.Body.Variables.Add(hLoc);
                        method.Body.InitLocals = true;
                        fresh.Add(adj.Create(OpCodes.Stloc, hLoc));
                        fresh.Add(adj.Create(OpCodes.Stloc, wLoc));
                        fresh.Add(adj.Create(OpCodes.Ldc_R4, 0f));
                        fresh.Add(adj.Create(OpCodes.Ldc_R4, 0f));
                        fresh.Add(adj.Create(OpCodes.Ldloc, wLoc));
                        fresh.Add(adj.Create(OpCodes.Ldloc, wLoc));
                        fresh.Add(adj.Create(OpCodes.Ldloc, hLoc));
                        fresh.Add(adj.Create(OpCodes.Ldloc, hLoc));
                        fresh.Add(adj.Create(OpCodes.Ldarg_3));
                        fresh.Add(adj.Create(OpCodes.Conv_R4));
                        // Parameters excludes this: [3] is layoutHeight.
                        fresh.Add(adj.Create(OpCodes.Ldarg_S, method.Parameters[3]));
                        fresh.Add(adj.Create(OpCodes.Conv_R4));
                        fresh.Add(adj.Create(OpCodes.Ldc_I4_1));
                        fresh.Add(adj.Create(OpCodes.Ldc_I4_1));
                        fresh.Add(adj.Create(OpCodes.Newobj, metricsNew));
                        // Rebuild the list outright: same Instruction
                        // objects, new order (branch targets stay valid).
                        var rebuilt = new List<Mono.Cecil.Cil.Instruction>();
                        for (int k = 0; k < at; k++)
                            rebuilt.Add(seq[k]);
                        rebuilt.AddRange(fresh);
                        for (int k = at; k < seq.Count; k++)
                        {
                            if (!removed.Contains(seq[k]))
                                rebuilt.Add(seq[k]);
                        }
                        method.Body.Instructions.Clear();
                        foreach (var ins in rebuilt)
                            method.Body.Instructions.Add(ins);
                        foreach (var ins in method.Body.Instructions)
                        {
                            if (ins.Operand is Mono.Cecil.Cil.Instruction tgt && removed.Contains(tgt))
                                ins.Operand = first;
                        }
                        // The insert widens branch spans past short-form
                        // range (the interpreter reads a truncated offset
                        // as garbage and aborts transform): widen every
                        // short branch in place (references stay intact).
                        foreach (var ins in method.Body.Instructions)
                        {
                            if (ins.OpCode == OpCodes.Br_S) ins.OpCode = OpCodes.Br;
                            else if (ins.OpCode == OpCodes.Brfalse_S) ins.OpCode = OpCodes.Brfalse;
                            else if (ins.OpCode == OpCodes.Brtrue_S) ins.OpCode = OpCodes.Brtrue;
                            else if (ins.OpCode == OpCodes.Beq_S) ins.OpCode = OpCodes.Beq;
                            else if (ins.OpCode == OpCodes.Bge_S) ins.OpCode = OpCodes.Bge;
                            else if (ins.OpCode == OpCodes.Bgt_S) ins.OpCode = OpCodes.Bgt;
                            else if (ins.OpCode == OpCodes.Ble_S) ins.OpCode = OpCodes.Ble;
                            else if (ins.OpCode == OpCodes.Blt_S) ins.OpCode = OpCodes.Blt;
                            else if (ins.OpCode == OpCodes.Bne_Un_S) ins.OpCode = OpCodes.Bne_Un;
                            else if (ins.OpCode == OpCodes.Bge_Un_S) ins.OpCode = OpCodes.Bge_Un;
                            else if (ins.OpCode == OpCodes.Bgt_Un_S) ins.OpCode = OpCodes.Bgt_Un;
                            else if (ins.OpCode == OpCodes.Ble_Un_S) ins.OpCode = OpCodes.Ble_Un;
                            else if (ins.OpCode == OpCodes.Blt_Un_S) ins.OpCode = OpCodes.Blt_Un;
                            else if (ins.OpCode == OpCodes.Leave_S) ins.OpCode = OpCodes.Leave;
                        }
                        method.Body.MaxStackSize = 32;
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Direct2D1.Direct2DFactory"
                        && method.Name == "Create")
                    {
                        // WebAssembly has no Direct2D (d2d1.dll): the factory
                        // would P/Invoke native D2D. Report absence with null
                        // (callers hold it in a RefHandle; controls must run
                        // without a device).
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Direct2D1.DeviceContextExtensions"
                        && method.Name == "IsDrawing"
                        && method.Parameters.Count == 1)
                    {
                        // The check reaches through COM for the D2D render
                        // target, which never exists headless: report "not
                        // drawing" so the command-list scope takes its
                        // passive path and dab rendering proceeds straight
                        // into the CPU target.
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Brushes.Basic.SampledCircleBrushRenderer"
                        && method.Name == "DrawHardShape"
                        && method.Parameters.Count == 4)
                    {
                        // Headless has no D2D factory for the high-quality
                        // ellipse-geometry path (FillGeometry): the plain
                        // FillEllipse below is its CPU twin (our FillEllipse
                        // carries a 1px AA rim), so the method reduces to it.
                        TypeReference ellipseDoubleRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Rendering.EllipseDouble");
                        MethodDefinition? ellipseCtorDef = ResolveCpuDef(ellipseDoubleRef).Methods
                            .FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 1)
                            ?? throw new InvalidOperationException("CpuPaint: EllipseDouble..ctor/1 missing");
                        var ellipseCtorRef = new MethodReference(ellipseCtorDef.Name, module.TypeSystem.Void, ellipseDoubleRef) { HasThis = true };
                        foreach (var p in ellipseCtorDef.Parameters)
                            ellipseCtorRef.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
                        MethodDefinition? explicitDef = ResolveCpuDef(ellipseDoubleRef).Methods
                            .FirstOrDefault(m => m.Name == "op_Explicit" && m.Parameters.Count == 1 && m.IsStatic)
                            ?? throw new InvalidOperationException("CpuPaint: EllipseDouble.op_Explicit missing");
                        var explicitRef = new MethodReference(explicitDef.Name, MapCpuType(module, explicitDef.ReturnType), ellipseDoubleRef) { HasThis = false };
                        foreach (var p in explicitDef.Parameters)
                            explicitRef.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
                        MethodReference fillEllipseCall = VirtualCpuCall(module, "PaintDotNet.Direct2D1.IDeviceContext", "FillEllipse", 2);
                        var il2 = method.Body.GetILProcessor();
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        il2.Emit(OpCodes.Ldarg_1);
                        il2.Emit(OpCodes.Ldarg_2);
                        il2.Emit(OpCodes.Newobj, ellipseCtorRef);
                        il2.Emit(OpCodes.Call, explicitRef);
                        il2.Emit(OpCodes.Ldarg_3);
                        il2.Emit(OpCodes.Callvirt, fillEllipseCall);
                        il2.Emit(OpCodes.Ret);
                        continue;
                    }
                    if (type.FullName == "PaintDotNet.Threading.PdnSynchronizationContext"
                        && method.HasBody
                        && (method.Name == "Post" || method.Name == "PostUserApc"))
                    {
                        // Single-threaded WebAssembly has no message pump:
                        // the post queue behind Post is never pumped, so
                        // every marshalled callback (floating-tool-window
                        // Shows, deferred UI refreshes) is queued forever
                        // and its feature silently never happens. There are
                        // no other threads (Thread.Start is neutered), so
                        // inline execution is the correct delivery: the
                        // caller already runs on the only thread.
                        MethodReference? inlineInvoke = null;
                        bool pushState = false;
                        if (method.Name == "PostUserApc"
                            && method.Parameters.Count == 1
                            && method.Parameters[0].ParameterType.FullName == "System.Action")
                        {
                            inlineInvoke = module.ImportReference(
                                typeof(Action).GetMethod("Invoke", Type.EmptyTypes)!);
                        }
                        else if (method.Name == "Post"
                            && method.Parameters.Count >= 2
                            && method.Parameters[0].ParameterType.FullName == "System.Threading.SendOrPostCallback"
                            && method.Parameters[1].ParameterType.FullName == "System.Object")
                        {
                            inlineInvoke = module.ImportReference(
                                typeof(SendOrPostCallback).GetMethod("Invoke", [typeof(object)])!);
                            pushState = true;
                        }
                        if (inlineInvoke is null)
                            throw new InvalidOperationException(
                                "InlinePost: unexpected " + method.FullName);
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                        if (pushState)
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_2));
                        method.Body.Instructions.Add(il.Create(OpCodes.Callvirt, inlineInvoke));
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    // Effect property writes land in the device bag headless
                    // (the CPU stamp paths use classic checker colors and
                    // target bounds, never the bag), so writes are inert.
                    // Construction and Refresh run for real: CreateEffect
                    // answers a CPU effect, so the Properties bag is live
                    // and every render that reads it (radio/color controls)
                    // stops NREing.
                    // COM-less effect creation: a CpuDrawingContext has no
                    // native ID2D1Effect, so the ComPtr/CreateRef tail would
                    // E_NOINTERFACE. Answer the CPU effect with S_OK directly;
                    // real factories run the original body untouched.
                    if (method.HasBody
                        && type.FullName == "PaintDotNet.Direct2D1.DeviceEffectFactoryExtensions"
                        && method.Name == "TryCreateEffect"
                        && method.Parameters.Count == 3
                        && method.Parameters[2].ParameterType.FullName == "PaintDotNet.Direct2D1.IDeviceEffect&")
                    {
                        TypeDefinition? fxFactoryDef = module.GetType("PaintDotNet.Direct2D1.IDeviceEffectFactory");
                        MethodDefinition? fxCreateDef = fxFactoryDef?.Methods.FirstOrDefault(m =>
                            m.Name == "CreateEffect" && m.Parameters.Count == 1);
                        TypeReference? hrRef = module.GetTypeReferences().FirstOrDefault(x =>
                            x.FullName == "TerraFX.Interop.Windows.HRESULT");
                        MethodDefinition? hrOkDef = hrRef is null ? null : ResolveCpuDef(hrRef).Methods.FirstOrDefault(m =>
                            m.Name == "op_Implicit" && m.IsStatic && m.Parameters.Count == 1
                            && m.Parameters[0].ParameterType.FullName == "System.Int32");
                        if (fxCreateDef is null || hrOkDef is null)
                            throw new InvalidOperationException("CpuPaint: TryCreateEffect bypass refs missing");
                        var origStart = method.Body.Instructions[0];
                        var seq = new[]
                        {
                            il.Create(OpCodes.Ldarg_0),
                            il.Create(OpCodes.Callvirt, module.ImportReference(typeof(object).GetMethod("GetType", Type.EmptyTypes)!)),
                            il.Create(OpCodes.Callvirt, module.ImportReference(typeof(Type).GetMethod("get_FullName", Type.EmptyTypes)!)),
                            il.Create(OpCodes.Ldstr, "PaintDotNet.Direct2D1.CpuDrawingContext"),
                            il.Create(OpCodes.Call, module.ImportReference(typeof(string).GetMethod("op_Equality", new[] { typeof(string), typeof(string) })!)),
                            il.Create(OpCodes.Brfalse_S, origStart),
                            il.Create(OpCodes.Ldarg_2),
                            il.Create(OpCodes.Ldarg_0),
                            il.Create(OpCodes.Ldarg_1),
                            il.Create(OpCodes.Callvirt, module.ImportReference(fxCreateDef)),
                            il.Create(OpCodes.Stind_Ref),
                            il.Create(OpCodes.Ldc_I4_0),
                            il.Create(OpCodes.Call, module.ImportReference(hrOkDef)),
                            il.Create(OpCodes.Ret),
                        };
                        for (int k = seq.Length - 1; k >= 0; k--)
                            method.Body.Instructions.Insert(0, seq[k]);
                        if (method.Body.MaxStackSize < 8)
                            method.Body.MaxStackSize = 8;
                        continue;
                    }
                    if (method.HasBody
                        && type.FullName == "PaintDotNet.Direct2D1.Effects.EffectPropertyAccessor`1"
                        && method.Name == "SetValue"
                        && method.Parameters.Count == 1)
                    {
                        // (body unchanged: clear + ret)
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                        continue;
                    }
                    if (method.HasBody
                        && type.FullName == "PaintDotNet.Direct2D1.Effects.EffectInputAccessor"
                        && method.Name == "Set"
                        && method.Parameters.Count == 2)
                    {
                        // Effect input wiring resolves the storage through
                        // the generic COM-ref helper
                        // (TryCastOrCreateRefUnsafe<IDeviceEffect>): on the
                        // browser runtime that resolution aborts the
                        // process (class.c assertion, uncatchable), the
                        // same Mono generic-walk fault as the wheel poison.
                        // Reroute through the non-generic TryCreateRef,
                        // which answers the CPU stand-in for live objects
                        // and null for unclaimable storage (skipped): no
                        // generic token is ever built or resolved here.
                        MethodDefinition storageGetter = type.Methods.First(m =>
                            m.Name == "get_Storage" && m.Parameters.Count == 0);
                        MethodDefinition indexGetter = type.Methods.First(m =>
                            m.Name == "get_Index" && m.Parameters.Count == 0);
                        TypeDefinition objExtDef = ResolveExternalDef(module,
                            "PaintDotNet.ComponentModel",
                            "PaintDotNet.ComponentModel.ObjectRefExtensions");
                        MethodDefinition tryCreateDef = objExtDef.Methods.First(m =>
                            m.Name == "TryCreateRef" && !m.HasGenericParameters
                                && m.Parameters.Count == 2
                                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.ComponentModel.IObjectRef"
                                && m.Parameters[1].ParameterType.FullName == "System.Type");
                        if (module.AssemblyReferences.All(r => r.Name != objExtDef.Module.Assembly.Name.Name))
                            module.AssemblyReferences.Add(new AssemblyNameReference(
                                objExtDef.Module.Assembly.Name.Name, objExtDef.Module.Assembly.Name.Version));
                        MethodReference tryCreateRef = module.ImportReference(tryCreateDef);
                        MethodReference setInput = VirtualCpuCall(module,
                            "PaintDotNet.Direct2D1.IDeviceEffect", "SetInput", 3);
                        MethodReference getTypeFromHandle = module.ImportReference(
                            typeof(Type).GetMethod("GetTypeFromHandle", [typeof(RuntimeTypeHandle)])!);
                        TypeReference effectRef = LocalOrOwnedCpuRef(module,
                            "PaintDotNet.Direct2D1.IDeviceEffect");
                        var effLoc = new VariableDefinition(effectRef);
                        method.Body.Variables.Clear();
                        method.Body.Variables.Add(effLoc);
                        method.Body.InitLocals = true;
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Instructions.Clear();
                        var setIl = method.Body.GetILProcessor();
                        var unclaimed = setIl.Create(OpCodes.Nop);
                        setIl.Emit(OpCodes.Ldarg_0);
                        setIl.Emit(OpCodes.Call, module.ImportReference(storageGetter));
                        setIl.Emit(OpCodes.Ldtoken, effectRef);
                        setIl.Emit(OpCodes.Call, getTypeFromHandle);
                        setIl.Emit(OpCodes.Call, tryCreateRef);
                        setIl.Emit(OpCodes.Isinst, effectRef);
                        setIl.Emit(OpCodes.Stloc, effLoc);
                        setIl.Emit(OpCodes.Ldloc, effLoc);
                        setIl.Emit(OpCodes.Brfalse, unclaimed);
                        setIl.Emit(OpCodes.Ldloc, effLoc);
                        setIl.Emit(OpCodes.Ldarg_0);
                        setIl.Emit(OpCodes.Call, module.ImportReference(indexGetter));
                        setIl.Emit(OpCodes.Ldarg_1);
                        setIl.Emit(OpCodes.Ldarg_2);
                        setIl.Emit(OpCodes.Callvirt, setInput);
                        // Input retention (rev173): the inner device
                        // effect this Set lands on is ReplaceRef-swapped
                        // for a fresh husk afterwards, so at draw time
                        // the husk's table is empty and strip chains
                        // (crop->opacity->composite) unwrap to nothing.
                        // The Properties bag lives on the wrapper and
                        // survives the swap: also note (bag, index) ->
                        // image into CpuPaint, which the draw-time walk
                        // reads first. Claimed path only (the note is a
                        // whole-body-guarded no-op otherwise). Best
                        // effort: without the helper the Set reroute
                        // above still stands on its own.
                        try
                        {
                            MethodReference noteInput = ImportCpuPaintMethod(module, cpuPaintPath, "NoteEffectInput", 3);
                            setIl.Emit(OpCodes.Ldarg_0);
                            setIl.Emit(OpCodes.Call, module.ImportReference(storageGetter));
                            setIl.Emit(OpCodes.Ldarg_0);
                            setIl.Emit(OpCodes.Call, module.ImportReference(indexGetter));
                            setIl.Emit(OpCodes.Ldarg_1);
                            setIl.Emit(OpCodes.Call, noteInput);
                        }
                        catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
                        {
                        }
                        setIl.Append(unclaimed);
                        setIl.Emit(OpCodes.Ret);
                        method.Body.MaxStackSize = 32;
                        continue;
                    }
                    if ((type.FullName == "PaintDotNet.Controls.Direct2DControl"
                            || type.FullName == "PaintDotNet.Controls.InternalDirect2DControl"
                            || type.FullName == "PaintDotNet.Controls.InternalDirect2DControlHW"
                            || type.FullName == "PaintDotNet.Controls.Direct2DPictureBox")
                        && (method.Name == ".ctor"
                            || method.Name == "get_DirectWriteFactory"
                            || method.Name == "OnHandleCreatedInternal"
                            || method.Name == "get_Direct2DFactory"
                            || method.Name == "get_ImagingFactory"
                            || method.Name == "get_DeviceContext"
                            || ((method.Name.StartsWith("set_Hwnd", StringComparison.Ordinal)
                                || method.Name == "set_AutoDrawBackground"
                                || method.Name == "set_UseBackColor")
                                && method.Parameters.Count == 1
                                && method.ReturnType.FullName == "System.Void")
                            || (method.Name.StartsWith("get_Hwnd", StringComparison.Ordinal)
                                && method.Parameters.Count == 0
                                && (method.ReturnType.FullName == "System.Boolean"
                                    || method.ReturnType.FullName == "System.String"
                                    || IsHwndEnum(method.ReturnType)))
                            || (method.Name == "OnSizeChanged"
                                && method.Parameters.Count == 1
                                && method.ReturnType.FullName == "System.Void")))
                    {
                        if (method.Name == "get_DirectWriteFactory"
                            || method.Name == "get_Direct2DFactory"
                            || method.Name == "get_ImagingFactory")
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                        if (method.Name == "get_DeviceContext"
                            && method.Parameters.Count == 0
                            && method.HasBody)
                        {
                            // Resource creation (wheel bitmaps, effect
                            // inputs) needs a context object but never
                            // draws: answer a CPU context over the shared
                            // scratch target instead of null.
                            var wcScope = module.AssemblyReferences.FirstOrDefault(a => a.Name == "PaintDotNet.Windows.Core");
                            TypeReference dcCtxRef = wcScope is null
                                ? throw new InvalidOperationException("CpuPaint: Windows.Core scope missing for DeviceContext")
                                : new TypeReference("PaintDotNet.Direct2D1", "CpuDrawingContext", module, wcScope);
                            var dcCtorRef = new MethodReference(".ctor", module.TypeSystem.Void, dcCtxRef) { HasThis = true };
                            dcCtorRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int64));
                            if (string.IsNullOrEmpty(cpuPaintPath) || !System.IO.File.Exists(cpuPaintPath))
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                            TypeDefinition dcCpuPaint;
                            try
                            {
                                dcCpuPaint = AssemblyDefinition.ReadAssembly(cpuPaintPath).MainModule.GetType("WfWasm.PdnCpu.CpuPaint")
                                    ?? throw new InvalidOperationException("CpuPaint missing");
                            }
                            catch (Exception ex) when (ex is not InvalidOperationException)
                            {
                                throw new InvalidOperationException("CpuPaint: cannot read " + cpuPaintPath, ex);
                            }
                            MethodReference dcScratchRef = module.ImportReference(dcCpuPaint.Methods
                                .First(m => m.Name == "GetControlScratchTarget" && m.Parameters.Count == 0));
                            var dcLoc = new VariableDefinition(module.TypeSystem.Int64);
                            method.Body.Variables.Add(dcLoc);
                            method.Body.InitLocals = true;
                            method.Body.MaxStackSize = 3;
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Call, dcScratchRef));
                            method.Body.Instructions.Add(il.Create(OpCodes.Stloc, dcLoc));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldloc, dcLoc));
                            method.Body.Instructions.Add(il.Create(OpCodes.Newobj, dcCtorRef));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                        // The device-backed impl is gone (see above), so the
                        if (method.Name == "OnHandleCreatedInternal"
                            && method.Parameters.Count == 0
                            && method.ReturnType.FullName == "System.Void")
                        {
                            // The device impl these controls would drive
                            // from here is gone (ctor rule above), so the
                            // relay would null-ref and take GdiControl's
                            // input relay down with it. The software path
                            // never needed this hook.
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                        // Hwnd/device forwarding accessors would null-ref.
                        // Setters are dropped; getters report defaults (all
                        // bool/string/enum: false, null, or the 0 member,
                        // which is the software/off variant in each enum).
                        if ((method.Name.StartsWith("set_Hwnd", StringComparison.Ordinal)
                                || method.Name == "set_AutoDrawBackground"
                                || method.Name == "set_UseBackColor")
                            && method.ReturnType.FullName == "System.Void")
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                        if (method.Name.StartsWith("get_Hwnd", StringComparison.Ordinal))
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            if (method.ReturnType.FullName == "System.String")
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldnull));
                            else
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldc_I4_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                        if (method.Name == "OnSizeChanged"
                            && method.Parameters.Count == 1)
                        {
                            // Keep the SizeChanged event contract but drop the
                            // device relay (no impl). Reuses the existing base
                            // call operand so no fork resolution is needed.
                            var baseCall = method.Body.Instructions.FirstOrDefault(i =>
                                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                && i.Operand is MethodReference cmr
                                && cmr.Name == "OnSizeChanged"
                                && cmr.DeclaringType.FullName == "System.Windows.Forms.Control");
                            if (baseCall?.Operand is MethodReference baseOnSizeChanged)
                            {
                                method.Body.ExceptionHandlers.Clear();
                                method.Body.Variables.Clear();
                                method.Body.Instructions.Clear();
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_1));
                                method.Body.Instructions.Add(il.Create(OpCodes.Call, baseOnSizeChanged));
                                method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                                continue;
                            }
                        }
                        // Same story one level up: skip Direct2DControlImpl
                        // creation (needs the device) and keep the managed
                        // GdiControl base construction.
                        var baseCtor = type.BaseType.Resolve()?.Methods
                            .FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0);
                        if (baseCtor is not null)
                        {
                            method.Body.ExceptionHandlers.Clear();
                            method.Body.Variables.Clear();
                            method.Body.Instructions.Clear();
                            method.Body.Instructions.Add(il.Create(OpCodes.Ldarg_0));
                            method.Body.Instructions.Add(il.Create(OpCodes.Call, module.ImportReference(baseCtor)));
                            method.Body.Instructions.Add(il.Create(OpCodes.Ret));
                            continue;
                        }
                    }
                    foreach (var instruction in method.Body.Instructions.ToList())
                    {
                        if ((instruction.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt || instruction.OpCode == Mono.Cecil.Cil.OpCodes.Call || instruction.OpCode == Mono.Cecil.Cil.OpCodes.Newobj)
                            && instruction.Operand is MethodReference mr)
                        {
                            // Self-exclusion: the shim's headless passthrough calls
                            // the same BCL method; retargeting it would recurse.
                            if (envGetFolderPath1 != null && mr.Name == "GetFolderPath"
                                && mr.DeclaringType.FullName == "System.Environment"
                                && !(type.FullName == "System.Windows.Forms.WasmEnvironment" && method.Name == "GetFolderPath"))
                            {
                                if (mr.Parameters.Count == 1)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = envGetFolderPath1;
                                }
                                else if (mr.Parameters.Count == 2 && envGetFolderPath2 != null)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = envGetFolderPath2;
                                }
                            }
                            if (safeGetObject1 != null && mr.Name == "GetObject"
                                && (mr.DeclaringType.FullName == "System.Resources.ResourceManager" || mr.DeclaringType.FullName == "System.ComponentModel.ComponentResourceManager"))
                            {
                                if (mr.Parameters.Count == 1)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = safeGetObject1;
                                }
                                else if (mr.Parameters.Count == 2)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = safeGetObject2;
                                }
                            }
                            else if (safeApplyResources1 != null && mr.Name == "ApplyResources"
                                && mr.DeclaringType.FullName == "System.ComponentModel.ComponentResourceManager")
                            {
                                if (mr.Parameters.Count == 2)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = safeApplyResources1;
                                }
                                else if (mr.Parameters.Count == 3)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = safeApplyResources2;
                                }
                            }
                            else if (safeGetResourceSet != null && mr.Name == "GetResourceSet"
                                && (mr.DeclaringType.FullName == "System.Resources.ResourceManager" || mr.DeclaringType.FullName == "System.ComponentModel.ComponentResourceManager")
                                && mr.Parameters.Count == 3)
                            {
                                instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                instruction.Operand = safeGetResourceSet;
                            }
                            else if (safeGetString1 != null && mr.Name == "GetString"
                                && (mr.DeclaringType.FullName == "System.Resources.ResourceManager" || mr.DeclaringType.FullName == "System.ComponentModel.ComponentResourceManager"))
                            {
                                if (mr.Parameters.Count == 1)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = safeGetString1;
                                }
                                else if (mr.Parameters.Count == 2)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = safeGetString2;
                                }
                            }
                            else if (safeGetStream1 != null && mr.Name == "GetStream"
                                && (mr.DeclaringType.FullName == "System.Resources.ResourceManager" || mr.DeclaringType.FullName == "System.ComponentModel.ComponentResourceManager"))
                            {
                                if (mr.Parameters.Count == 1)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = safeGetStream1;
                                }
                                else if (mr.Parameters.Count == 2)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = safeGetStream2;
                                }
                            }
                            else if (baseDirStatic != null && mr.Name == "get_BaseDirectory"
                                && mr.DeclaringType.FullName == "System.AppContext"
                                && !mr.HasThis && mr.Parameters.Count == 0)
                            {
                                instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                instruction.Operand = baseDirStatic;
                            }
                            else if (baseDirInstance != null && mr.Name == "get_BaseDirectory"
                                && mr.DeclaringType.FullName == "System.AppDomain"
                                && mr.HasThis && mr.Parameters.Count == 0)
                            {
                                instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                instruction.Operand = baseDirInstance;
                            }
                            else if (formsGateType != null && formsControlType != null && mr.Name == "Invalidate"
                                && mr.DeclaringType.FullName == "System.Windows.Forms.Control" && mr.HasThis)
                            {
                                // All six instance overloads funnel here (0, 1 or 2
                                // explicit args); the hook keeps each call site's
                                // own parameter typerefs so the static signature
                                // resolves exactly as the caller sees it.
                                if (mr.Parameters.Count <= 2)
                                {
                                    var hook = new MethodReference("RequestRepaint", module.TypeSystem.Void, formsGateType)
                                    {
                                        HasThis = false,
                                    };
                                    hook.Parameters.Add(new ParameterDefinition(formsControlType));
                                    foreach (var p in mr.Parameters)
                                        hook.Parameters.Add(new ParameterDefinition(p.ParameterType));
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = hook;
                                }
                            }
                            else if (vfsGetFiles1 != null && mr.Name == "GetFiles"
                                && mr.DeclaringType.FullName == "System.IO.Directory")
                            {
                                if (mr.Parameters.Count == 1)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = vfsGetFiles1;
                                }
                                else if (mr.Parameters.Count == 2)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = vfsGetFiles2;
                                }
                                else if (mr.Parameters.Count == 3 && vfsGetFiles3 != null)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = vfsGetFiles3;
                                }
                            }
                            else if (vfsExists != null && mr.Name == "Exists"
                                && mr.DeclaringType.FullName == "System.IO.File"
                                && mr.Parameters.Count == 1)
                            {
                                instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                instruction.Operand = vfsExists;
                            }
                            else if (vfsOpenStream != null && mr.Name == ".ctor"
                                && mr.DeclaringType.FullName == "System.IO.FileStream"
                                && mr.Parameters.Count == 2
                                && mr.Parameters[0].ParameterType.FullName == "System.String")
                            {
                                instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                instruction.Operand = vfsOpenStream;
                            }
                            else if (mr.Name == "Copy"
                                && mr.DeclaringType.FullName == "System.IO.File")
                            {
                                if (mr.Parameters.Count == 2 && vfsCopy2 != null)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = vfsCopy2;
                                }
                                else if (mr.Parameters.Count == 3 && vfsCopy3 != null)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = vfsCopy3;
                                }
                            }
                            else if (vfsCreationTime != null && mr.Name == "GetCreationTime"
                                && mr.DeclaringType.FullName == "System.IO.File"
                                && mr.Parameters.Count == 1)
                            {
                                instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                instruction.Operand = vfsCreationTime;
                            }
                            else if (mr.Name == "ReadAllText"
                                && mr.DeclaringType.FullName == "System.IO.File")
                            {
                                if (mr.Parameters.Count == 1 && vfsReadText1 != null)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = vfsReadText1;
                                }
                                else if (mr.Parameters.Count == 2 && vfsReadText2 != null)
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = vfsReadText2;
                                }
                            }
                            else if (mr.Name == "ReadAllBytes"
                                && mr.DeclaringType.FullName == "System.IO.File"
                                && mr.Parameters.Count == 1 && vfsReadBytes1 != null)
                            {
                                instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                instruction.Operand = vfsReadBytes1;
                            }
                            else if (mr.Name == "GetVersionInfo"
                                && mr.DeclaringType.FullName == "System.Diagnostics.FileVersionInfo"
                                && mr.Parameters.Count == 1 && wasmFileVersionGetInfo != null)
                            {
                                // Browser ships FileVersionInfo as a throwing
                                // stub (every member PNSEs): the factory must
                                // be answered from loaded-assembly metadata.
                                // Aborting whole OnLoad chains otherwise
                                // (Cyotek AboutDialog/MainForm: the
                                // Demonstrations tab is never built).
                                instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                instruction.Operand = wasmFileVersionGetInfo;
                            }
                            else if (mr.HasThis && mr.Parameters.Count == 0
                                && mr.DeclaringType.FullName == "System.Diagnostics.FileVersionInfo"
                                && (mr.Name == "ToString" || mr.Name.StartsWith("get_", StringComparison.Ordinal))
                                && wasmFileVersionGetInfo != null)
                            {
                                // The stub has no backing fields, so property
                                // reads on the shim-returned instance would
                                // still throw: route every getter/ToString to
                                // the metadata-backed readers (same name with
                                // a Read prefix, Describe for ToString). The
                                // instance becomes the first static arg.
                                string readerName = mr.Name == "ToString"
                                    ? "Describe"
                                    : "Read" + mr.Name.Substring(4);
                                string retFull = mr.ReturnType.FullName;
                                if (retFull == "System.String" || retFull == "System.Int32" || retFull == "System.Boolean")
                                {
                                    var reader = new MethodReference(readerName,
                                        module.ImportReference(mr.ReturnType), wasmFileVersionGetInfo.DeclaringType)
                                    {
                                        HasThis = false,
                                        Parameters = { new ParameterDefinition(module.ImportReference(typeof(System.Diagnostics.FileVersionInfo))) },
                                    };
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = reader;
                                }
                            }
                            else if (mr.Name == "get_ClientsAreListening"
                                && mr.DeclaringType.FullName == "System.Windows.Automation.Provider.AutomationInteropProvider"
                                && mr.Parameters.Count == 0)
                            {
                                instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                instruction.Operand = uiaNoClients;
                            }
                            else if (wasmRegistryType != null && mr.DeclaringType.FullName == "Microsoft.Win32.RegistryKey")
                            {
                                // Browser has no registry; the runtime NREs (hive
                                // getters return null). Retarget to the fork's
                                // missing-key stubs (same names, the instance
                                // becomes the first static arg, so null keys
                                // are fine).
                                bool regOk;
                                bool regThis;
                                if (!mr.HasThis)
                                {
                                    regOk = (mr.Name == "OpenBaseKey" && mr.Parameters.Count == 2)
                                        || (mr.Name == "OpenRemoteBaseKey" && (mr.Parameters.Count == 2 || mr.Parameters.Count == 3));
                                    regThis = false;
                                }
                                else
                                {
                                    regOk = mr.Name switch
                                    {
                                        "OpenSubKey" => mr.Parameters.Count is 1 or 2 or 3,
                                        "GetValue" => mr.Parameters.Count is 1 or 2 or 3,
                                        "SetValue" => mr.Parameters.Count is 2 or 3,
                                        "CreateSubKey" => mr.Parameters.Count is 1 or 3,
                                        "Close" or "Dispose" or "Flush" => mr.Parameters.Count == 0,
                                        "GetSubKeyNames" or "GetValueNames" => mr.Parameters.Count == 0,
                                        "DeleteValue" or "DeleteSubKey" => mr.Parameters.Count is 1 or 2,
                                        "DeleteSubKeyTree" or "GetValueKind" => mr.Parameters.Count == 1,
                                        "get_Name" or "get_ValueCount" or "get_SubKeyCount" or "get_Handle" or "get_View" => mr.Parameters.Count == 0,
                                        _ => false,
                                    };
                                    regThis = true;
                                }
                                if (regOk)
                                {
                                    var hook = new MethodReference(mr.Name, mr.ReturnType, wasmRegistryType)
                                    {
                                        HasThis = false,
                                    };
                                    if (regThis)
                                        hook.Parameters.Add(new ParameterDefinition(mr.DeclaringType));
                                    foreach (var p in mr.Parameters)
                                        hook.Parameters.Add(new ParameterDefinition(p.ParameterType));
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = hook;
                                }
                            }
                            else if (wasmRegistryType != null && mr.DeclaringType.FullName == "Microsoft.Win32.Registry" && !mr.HasThis)
                            {
                                if ((mr.Name == "GetValue" && mr.Parameters.Count == 3)
                                    || (mr.Name == "SetValue" && (mr.Parameters.Count == 3 || mr.Parameters.Count == 4)))
                                {
                                    var hook = new MethodReference(mr.Name, mr.ReturnType, wasmRegistryType)
                                    {
                                        HasThis = false,
                                    };
                                    foreach (var p in mr.Parameters)
                                        hook.Parameters.Add(new ParameterDefinition(p.ParameterType));
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                                    instruction.Operand = hook;
                                }
                            }
                            else if (mr.DeclaringType.FullName == "System.Threading.Thread")
                            {
                                if (mr.Name == "Start")
                                {
                                    if (mr.Parameters.Count == 0)
                                    {
                                        instruction.OpCode = Mono.Cecil.Cil.OpCodes.Pop;
                                        instruction.Operand = null;
                                    }
                                    else if (mr.Parameters.Count == 1)
                                    {
                                        instruction.OpCode = Mono.Cecil.Cil.OpCodes.Pop;
                                        instruction.Operand = null;
                                        var pop2 = il.Create(Mono.Cecil.Cil.OpCodes.Pop);
                                        il.InsertAfter(instruction, pop2);
                                    }
                                }
                                else if (mr.Name == "SetApartmentState")
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Pop;
                                    instruction.Operand = null;
                                    var pop2 = il.Create(Mono.Cecil.Cil.OpCodes.Pop);
                                    il.InsertAfter(instruction, pop2);
                                }
                                else if (mr.Name == "TrySetApartmentState")
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Pop;
                                    instruction.Operand = null;
                                    var pop2 = il.Create(Mono.Cecil.Cil.OpCodes.Pop);
                                    var pushTrue = il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                                    il.InsertAfter(instruction, pop2);
                                    il.InsertAfter(pop2, pushTrue);
                                }
                                else if (mr.Name == "GetApartmentState")
                                {
                                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Pop;
                                    instruction.Operand = null;
                                    var pushSta = il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                                    il.InsertAfter(instruction, pushSta);
                                }
                            }
                            else if (mr.Name == "set_CurrentDirectory"
                                && mr.DeclaringType.FullName == "System.Environment"
                                && mr.Parameters.Count == 1)
                            {
                                // Browser WASM has no working directory:
                                // CoreLib throws DirectoryNotFound for the
                                // virtual app folder. Apps resolve their
                                // dirs through absolute providers (PDN via
                                // PdnInfo), so drop the set entirely.
                                instruction.OpCode = Mono.Cecil.Cil.OpCodes.Pop;
                                instruction.Operand = null;
                            }
                        }
                    }
                }
            }

        if (cpuPaintPath is not null)
        {
            // CPU backing for content drawing (brush stamps): binds
            // DrawingContext.Draw(RegionPtr) to a synthesized
            // CpuDrawingContext. Runs after every neuter pass (it
            // overwrites the neutered Draw bodies) and before Write so
            // the new types skip the per-type loop. Loud on missing
            // pieces; silent only for non-PDN modules.
            EnsureCpuDrawingContext(module, cpuPaintPath);
        }

        // Headless animation services (COM UIAnimation has no WASM
        // backend): UIA factories answer CPU no-ops whose Try* methods
        // return E_NOTIMPL through the callers' own error channels.
        EnsureHeadlessAnimation(module);

        // Dead-ref sweep, last so strip passes settle first: dropping a
        // scope the writer still emits typerefs against orphans them
        // (Cecil re-emits dangling scopes against the wrong table -
        // observed: the DWF fake self-named refs re-emitted as
        // System.Private.CoreLib, fatal TypeLoad at bind time). Liveness
        // is reachability from live metadata, not table presence: strip
        // passes leave dead TypeRef rows the writer prunes.
        var liveScopes = LiveScopes(module);
        var dwfRef = module.AssemblyReferences.FirstOrDefault(r => r.Name == "DirectWriteForwarder");
        if (dwfRef != null && !liveScopes.Contains(dwfRef))
        {
            module.AssemblyReferences.Remove(dwfRef);
        }

        var codeDomRef = module.AssemblyReferences.FirstOrDefault(r => r.Name == "System.CodeDom");
        if (codeDomRef != null && !liveScopes.Contains(codeDomRef))
        {
            module.AssemblyReferences.Remove(codeDomRef);
        }

        using var ms = new MemoryStream();
        assembly.Write(ms);
        return ms.ToArray();
    }

    public static List<string> ReferenceNames(string assemblyPath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        return assembly.MainModule.AssemblyReferences.Select(r => r.Name).ToList();
    }

    private static bool IsHwndEnum(TypeReference type)
    {
        try
        {
            return type.Resolve()?.IsEnum == true;
        }
        catch
        {
            return false;
        }
    }

    // Matches the dpi-scale prologue `dpiX = dc.get_Dpi().X / 96` at the
    // head of UITextFactory.CreateLayout: ldarg.1, callvirt get_Dpi,
    // stloc dpi, ldloca dpi, call get_X, conv.r8, ldc.r8 96, div,
    // stloc scale. Returns the scale local's index and the instruction
    // index just past the scale store (a point where the evaluation stack
    // is provably empty, hence a legal splice join). Anything else â€”
    // different shape, unknown locals â€” returns false and the method is
    // left untouched: guessing a local slot or joining mid-expression
    // produces unverifiable IL (InvalidProgram under the interpreter).
    private static bool TryMatchDpiScalePrologue(MethodDefinition method, out int scaleSlot, out int prologueEnd)
    {
        scaleSlot = -1;
        prologueEnd = -1;
        var insns = method.Body.Instructions;
        if (insns.Count < 10
            || insns[0].OpCode != OpCodes.Ldarg_1
            || (insns[1].OpCode != OpCodes.Callvirt && insns[1].OpCode != OpCodes.Call)
            || insns[1].Operand is not MethodReference dpiCall
            || dpiCall.Name != "get_Dpi"
            || !TryStlocSlot(method, insns[2], out int dpiSlot)
            || !TryLdlocaSlot(method, insns[3], out int dpiLoadSlot)
            || dpiLoadSlot != dpiSlot
            || (insns[4].OpCode != OpCodes.Call && insns[4].OpCode != OpCodes.Callvirt)
            || insns[5].OpCode != OpCodes.Conv_R8
            || insns[6].OpCode != OpCodes.Ldc_R8
            || insns[7].OpCode != OpCodes.Div
            || !TryStlocSlot(method, insns[8], out scaleSlot))
        {
            return false;
        }

        prologueEnd = 9;
        return true;
    }

    private static bool TryStlocSlot(MethodDefinition method, Instruction ins, out int slot)
    {
        slot = -1;
        var code = ins.OpCode.Code;
        if (code >= Code.Stloc_0 && code <= Code.Stloc_3)
        {
            slot = code - Code.Stloc_0;
            return slot < method.Body.Variables.Count;
        }

        if (ins.OpCode == OpCodes.Stloc_S && ins.Operand is VariableDefinition varDef)
        {
            slot = method.Body.Variables.IndexOf(varDef);
            return slot >= 0;
        }

        return false;
    }

    private static bool TryLdlocaSlot(MethodDefinition method, Instruction ins, out int slot)
    {
        slot = -1;
        if (ins.OpCode == OpCodes.Ldloca_S && ins.Operand is VariableDefinition varDef)
        {
            slot = method.Body.Variables.IndexOf(varDef);
            return slot >= 0;
        }

        return false;
    }

    // Action<PdnTask>: the StartNewThreadTask/CreateThreadTask
    // overload whose closure the headless driver runs inline.
    private static bool IsActionOfPdnTask(TypeReference type) =>
        type is GenericInstanceType generic
        && generic.Name == "Action`1"
        && generic.GenericArguments.Count == 1
        && generic.GenericArguments[0].FullName == "PaintDotNet.Threading.Tasks.PdnTask";

    // Corlib reference in the TARGET module's own framework: never
    // ImportReference a host typeof() for these. The host runs net9
    // (System.Private.CoreLib) while targets may be netfx (mscorlib);
    // stamping the host scope into their signatures breaks runtime
    // method resolution ("Method not found", observed live).
    private static TypeReference? TargetCorlibRef(ModuleDefinition module, string ns, string name)
    {
        TypeReference? existing = module.GetTypeReferences()
            .FirstOrDefault(t => t.FullName == ns + "." + name);
        if (existing is not null)
            return existing;
        if (module.TypeSystem.Corlib is null)
            return null;
        return new TypeReference(ns, name, module, module.TypeSystem.Corlib);
    }

    // Definition lookup: same-module first, then sibling assemblies
    // through the module's own references (the reader resolver
    // already searches the input directory, so pristine siblings
    // resolve). Fixture modules define the stand-ins locally, real
    // modules reference them: both shapes must work.
    private static TypeDefinition? FindTaskType(ModuleDefinition module, string fullName)
    {
        TypeDefinition? local = module.GetType(fullName);
        if (local is not null)
            return local;
        try
        {
            return module.GetTypeReferences()
                .FirstOrDefault(t => t.FullName == fullName)?.Resolve();
        }
        catch
        {
            return null;
        }
    }

    // Synthesizes the headless task driver (see the
    // PdnTaskManagerExtensions rule): a non-generic
    // FuncThreadTask<Unit> subclass whose public DriveInline runs an
    // Action<PdnTask> to completion through the public Func
    // helpers and records TaskResult through its own protected
    // setter. Every token it touches is closed, which is what the
    // WASM interpreter needs. Returns null when any piece is
    // missing (rule then skips); idempotent.
    // A compiler-encoded GenericInstanceType proven to resolve at
    // runtime: prefer reusing the exact object the pristine assembly
    // already instantiates over synthesizing an equivalent one, so
    // the emitted encoding is byte-identical to working code.
    private static GenericInstanceType? FindProvenInstance(
        TypeDefinition extType, string elementName)
    {
        foreach (MethodDefinition method in extType.Methods)
        {
            if (!method.HasBody)
                continue;
            foreach (Instruction ins in method.Body.Instructions)
            {
                if (ins.Operand is MethodReference callee
                    && callee.DeclaringType is GenericInstanceType instance
                    && instance.ElementType.Name == elementName)
                    return instance;
            }
        }
        foreach (TypeDefinition nested in extType.NestedTypes)
        {
            GenericInstanceType? found = FindProvenInstance(nested, elementName);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static TypeDefinition? EnsureInlineFuncThreadTask(
        ModuleDefinition module, TypeDefinition extType)
    {
        // Top-level on purpose: nested generic instantiations are
        // exactly the shape the runtime chokes on, so the driver
        // itself must not add another nesting level to resolve.
        const string driverFullName = "PaintDotNet.Threading.Tasks.InlineFuncThreadTask";
        TypeDefinition? existing = module.GetType(driverFullName);
        if (existing is not null)
            return existing;

        // Base is ThreadTask<Unit> itself: the nested FuncThreadTask
        // is sealed and can never be derived (sealed base fails type
        // load, surfacing live as MissingMethodException on its .ctor).
        TypeDefinition? threadTask = module.GetTypes()
            .FirstOrDefault(t => t.FullName == "PaintDotNet.Threading.Tasks.ThreadTask`1");
        TypeDefinition? pdnTask =
            module.GetType("PaintDotNet.Threading.Tasks.PdnTask");
        TypeDefinition? pdnTaskGeneric = module.GetTypes()
            .FirstOrDefault(t => t.FullName == "PaintDotNet.Threading.Tasks.PdnTask`1");
        TypeDefinition? manager =
            module.GetType("PaintDotNet.Threading.Tasks.PdnTaskManager");
        TypeDefinition? funcDef = FindTaskType(module, "PaintDotNet.Functional.Func");
        TypeDefinition? resultDef = FindTaskType(module, "PaintDotNet.Functional.Result`1");
        TypeDefinition? unitDef = FindTaskType(module, "PaintDotNet.Functional.Unit");
        if (threadTask is null || threadTask.IsSealed
            || pdnTask is null || pdnTaskGeneric is null
            || manager is null || funcDef is null || resultDef is null || unitDef is null)
            return null;
        MethodDefinition? baseCtor = threadTask.Methods
            .FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 2);
        MethodDefinition? setState = pdnTask.Methods
            .FirstOrDefault(m => m.Name == "SetState" && m.Parameters.Count == 1);
        MethodDefinition? setResultDef = pdnTaskGeneric.Methods
            .FirstOrDefault(m => m.Name == "set_TaskResult" && m.Parameters.Count == 1);
        MethodDefinition? bindDef = funcDef.Methods
            .FirstOrDefault(m => m.Name == "Bind" && m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.Name == "Action`1");
        MethodDefinition? toFuncDef = funcDef.Methods
            .FirstOrDefault(m => m.Name == "ToFunc" && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "System.Action");
        MethodDefinition? evalDef = funcDef.Methods
            .FirstOrDefault(m => m.Name == "Eval" && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.Name == "Func`1");
        if (baseCtor is null || setState is null || setResultDef is null
            || bindDef is null || toFuncDef is null || evalDef is null)
            return null;

        // Cross-assembly references below reuse the TARGET module's
        // own TypeReferences (same scope objects as the rest of its
        // code), falling back to the local definition in fixture
        // modules. Never ImportReference a resolved foreign definition:
        // the import carries the source FILE's assembly identity, which
        // may differ from the target's reference and then resolves as
        // a different type at runtime ("Method not found", observed on
        // desktop CoreCLR as well as WASM). Same-module definitions
        // are used raw, exactly like compiler-written code.
        TypeReference TargetOrLocal(string fullName, TypeDefinition local)
        {
            TypeReference? existing = module.GetTypeReferences()
                .FirstOrDefault(t => t.FullName == fullName);
            return existing ?? (TypeReference)local;
        }
        TypeReference unit = TargetOrLocal("PaintDotNet.Functional.Unit", unitDef);
        TypeReference resultGeneric = TargetOrLocal("PaintDotNet.Functional.Result`1", resultDef);
        TypeReference funcGeneric = TargetOrLocal("PaintDotNet.Functional.Func", funcDef);
        var resultUnit = new GenericInstanceType(resultGeneric);
        resultUnit.GenericArguments.Add(unit);
        var pdnTaskUnit = new GenericInstanceType(pdnTaskGeneric);
        pdnTaskUnit.GenericArguments.Add(unit);
        var threadTaskUnit = new GenericInstanceType(threadTask);
        threadTaskUnit.GenericArguments.Add(unit);
        TypeReference? actionRef = TargetCorlibRef(module, "System", "Action`1");
        TypeReference? func1Ref = TargetCorlibRef(module, "System", "Func`1");
        TypeReference? plainActionRef = TargetCorlibRef(module, "System", "Action");
        TypeReference? aptState = TargetCorlibRef(module, "System.Threading", "ApartmentState");
        if (actionRef is null || func1Ref is null || plainActionRef is null || aptState is null)
            return null;
        var actionPdnTask = new GenericInstanceType(actionRef);
        actionPdnTask.GenericArguments.Add(pdnTask);

        var driver = new TypeDefinition(
            "PaintDotNet.Threading.Tasks", "InlineFuncThreadTask",
            TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Sealed,
            threadTaskUnit);

        var baseCtorRef = new MethodReference(
            ".ctor", module.TypeSystem.Void, threadTaskUnit) { HasThis = true };
        baseCtorRef.Parameters.Add(new ParameterDefinition(manager));
        baseCtorRef.Parameters.Add(new ParameterDefinition(aptState));
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition(manager));
        ctor.Parameters.Add(new ParameterDefinition(aptState));
        var ctorIl = ctor.Body.GetILProcessor();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Call, baseCtorRef);
        ctorIl.Emit(OpCodes.Ret);
        driver.Methods.Add(ctor);

        // Generic helpers are referenced manually on the target's
        // own Func reference (same reason as above): Bind and Eval
        // keep their own type parameters, closed here over PdnTask
        // and Unit respectively.
        var bindRef = new MethodReference("Bind", plainActionRef, funcGeneric);
        bindRef.GenericParameters.Add(new GenericParameter("T1", bindRef));
        var bindT1 = bindRef.GenericParameters[0];
        var bindActionT = new GenericInstanceType(actionRef);
        bindActionT.GenericArguments.Add(bindT1);
        bindRef.Parameters.Add(new ParameterDefinition(bindActionT));
        bindRef.Parameters.Add(new ParameterDefinition(bindT1));
        var bindSpec = new GenericInstanceMethod(bindRef);
        bindSpec.GenericArguments.Add(pdnTask);
        var evalFuncT = new GenericInstanceType(func1Ref);
        var evalRetT = new GenericInstanceType(resultGeneric);
        var evalRef = new MethodReference("Eval", evalRetT, funcGeneric);
        evalRef.GenericParameters.Add(new GenericParameter("TRet", evalRef));
        var evalT = evalRef.GenericParameters[0];
        evalFuncT.GenericArguments.Add(evalT);
        evalRetT.GenericArguments.Add(evalT);
        evalRef.Parameters.Add(new ParameterDefinition(evalFuncT));
        var evalSpec = new GenericInstanceMethod(evalRef);
        evalSpec.GenericArguments.Add(unit);
        // Parent the inherited protected setter at the driver
        // itself rather than at PdnTask<Unit>: same method, resolved
        // through the ordinary inheritance walk from a non-generic
        // parent, which sidesteps instantiation-qualified lookup of
        // the protected slot entirely.
        var setResultRef = new MethodReference(
            setResultDef.Name, module.TypeSystem.Void, driver) { HasThis = true };
        setResultRef.Parameters.Add(new ParameterDefinition(resultUnit));

        var actionField = new FieldDefinition("action", FieldAttributes.Private, actionPdnTask);
        driver.Fields.Add(actionField);

        var drive = new MethodDefinition(
            "DriveInline", MethodAttributes.Public, module.TypeSystem.Void);
        drive.Parameters.Add(new ParameterDefinition(actionPdnTask));
        var resultLoc = new VariableDefinition(resultUnit);
        drive.Body.Variables.Add(resultLoc);
        drive.Body.InitLocals = true;
        var db = drive.Body.GetILProcessor();
        db.Emit(OpCodes.Ldarg_0);
        db.Emit(OpCodes.Ldarg_1);
        db.Emit(OpCodes.Stfld, actionField);
        db.Emit(OpCodes.Ldarg_0);
        db.Emit(OpCodes.Ldc_I4_1);
        db.Emit(OpCodes.Call, setState);
        db.Emit(OpCodes.Ldarg_1);
        db.Emit(OpCodes.Ldarg_0);
        var toFuncRet = new GenericInstanceType(func1Ref);
        toFuncRet.GenericArguments.Add(unit);
        var toFuncRef = new MethodReference("ToFunc", toFuncRet, funcGeneric);
        toFuncRef.Parameters.Add(new ParameterDefinition(plainActionRef));
        db.Emit(OpCodes.Call, bindSpec);
        db.Emit(OpCodes.Call, toFuncRef);
        db.Emit(OpCodes.Call, evalSpec);
        db.Emit(OpCodes.Stloc_0);
        db.Emit(OpCodes.Ldarg_0);
        db.Emit(OpCodes.Ldloc_0);
        // Call, not callvirt: the setter is non-virtual (C# emits
        // call here too), and the worker's own b__4_0 proves this
        // exact shape resolves.
        db.Emit(OpCodes.Call, setResultRef);
        db.Emit(OpCodes.Ldarg_0);
        db.Emit(OpCodes.Ldc_I4_2);
        db.Emit(OpCodes.Call, setState);
        db.Emit(OpCodes.Ret);
        driver.Methods.Add(drive);

        // ThreadTask<T>.OnExecute is protected abstract: the driver
        // must implement it to load. It replays the stashed action
        // through DriveInline itself (a plain call, the
        // resolution-proof shape), so a driver stays coherent if
        // anything ever drives it through the virtual slot.
        var onExecute = new MethodDefinition(
            "OnExecute",
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            unit);
        var unitLoc = new VariableDefinition(unit);
        onExecute.Body.Variables.Add(unitLoc);
        onExecute.Body.InitLocals = true;
        var ob = onExecute.Body.GetILProcessor();
        ob.Emit(OpCodes.Ldarg_0);
        ob.Emit(OpCodes.Ldarg_0);
        ob.Emit(OpCodes.Ldfld, actionField);
        ob.Emit(OpCodes.Call, drive);
        ob.Emit(OpCodes.Ldloca_S, unitLoc);
        ob.Emit(OpCodes.Initobj, unit);
        ob.Emit(OpCodes.Ldloc_S, unitLoc);
        ob.Emit(OpCodes.Ret);
        driver.Methods.Add(onExecute);

        module.Types.Add(driver);
        return driver;
    }

    // CPU drawing-context backing (brush stamps): Paint.NET renders brush
    // sprites through DrawingContext.Draw(RegionPtr<ColorAlpha8>) into a
    // coverage mask. Headless there is no D2D device, so the static Draw
    // overloads over RegionPtr bind a synthesized CpuDrawingContext (a
    // DrawingContext subclass) to the mask bytes and route every virtual
    // through the primitives-only WfWasm.PdnCpu helper.
    //
    // Cross-module PDN members are referenced through the TARGET module's
    // own TypeReferences (same rule as EnsureInlineFuncThreadTask); the
    // closed-generic tokens (Nullable<RectFloat>, Action<IDrawingContext>)
    // and the !!0-form RegionPtr accessors are harvested from the module's
    // own proven call sites, built only as a fallback. Anything missing
    // aborts the rewrite loudly: a silent skip renders stamps as nothing.
    private static TypeReference MapCpuType(ModuleDefinition module, TypeReference t)
    {
        if (t is GenericParameter)
            return t;
        if (t is GenericInstanceType gi)
        {
            var mapped = new GenericInstanceType(MapCpuType(module, gi.ElementType));
            foreach (TypeReference a in gi.GenericArguments)
                mapped.GenericArguments.Add(MapCpuType(module, a));
            return mapped;
        }
        if (t is ByReferenceType br)
            return new ByReferenceType(MapCpuType(module, br.ElementType));
        if (t is PointerType pt)
            return new PointerType(MapCpuType(module, pt.ElementType));
        if (t is ArrayType at)
        {
            var mapped = new ArrayType(MapCpuType(module, at.ElementType));
            foreach (var d in at.Dimensions)
                mapped.Dimensions.Add(d);
            return mapped;
        }
        if (t is RequiredModifierType req)
            return new RequiredModifierType(
                MapCpuType(module, req.ModifierType), MapCpuType(module, req.ElementType));
        if (t is OptionalModifierType opt)
            return new OptionalModifierType(
                MapCpuType(module, opt.ModifierType), MapCpuType(module, opt.ElementType));
        if (t is TypeDefinition td && td.Module == module)
            return td;
        return t.FullName switch
        {
            "System.Void" => module.TypeSystem.Void,
            "System.Boolean" => module.TypeSystem.Boolean,
            "System.Byte" => module.TypeSystem.Byte,
            "System.SByte" => module.TypeSystem.SByte,
            "System.Int16" => module.TypeSystem.Int16,
            "System.UInt16" => module.TypeSystem.UInt16,
            "System.Int32" => module.TypeSystem.Int32,
            "System.UInt32" => module.TypeSystem.UInt32,
            "System.Int64" => module.TypeSystem.Int64,
            "System.UInt64" => module.TypeSystem.UInt64,
            "System.IntPtr" => module.TypeSystem.IntPtr,
            "System.UIntPtr" => module.TypeSystem.UIntPtr,
            "System.Single" => module.TypeSystem.Single,
            "System.Double" => module.TypeSystem.Double,
            "System.Char" => module.TypeSystem.Char,
            "System.String" => module.TypeSystem.String,
            "System.Object" => module.TypeSystem.Object,
            _ => module.GetTypeReferences().FirstOrDefault(x => x.FullName == t.FullName)
                ?? throw new InvalidOperationException(
                    $"CpuPaint: no target-owned reference for {t.FullName} in {module.Assembly.Name.Name}"),
        };
    }

        // Lenient twin of MapCpuType for wide contracts (ITextLayout
    // touches ClusterMetrics and friends the target module never
    // names): owned refs stay owned, anything else imports from the
    // contract's own module instead of throwing.
    private static TypeReference MapCpuTypeLenient(ModuleDefinition module, TypeReference t)
    {
        try { return MapCpuType(module, t); }
        catch (InvalidOperationException) { }
        if (t is GenericParameter)
            return t;
        if (t is GenericInstanceType gi)
        {
            var mapped = new GenericInstanceType(MapCpuTypeLenient(module, gi.ElementType));
            foreach (TypeReference a in gi.GenericArguments)
                mapped.GenericArguments.Add(MapCpuTypeLenient(module, a));
            return mapped;
        }
        if (t is ByReferenceType br)
            return new ByReferenceType(MapCpuTypeLenient(module, br.ElementType));
        if (t is PointerType pt)
            return new PointerType(MapCpuTypeLenient(module, pt.ElementType));
        if (t is ArrayType at)
        {
            var mapped = new ArrayType(MapCpuTypeLenient(module, at.ElementType));
            foreach (var d in at.Dimensions)
                mapped.Dimensions.Add(d);
            return mapped;
        }
        if (t is RequiredModifierType req)
            return new RequiredModifierType(
                MapCpuTypeLenient(module, req.ModifierType), MapCpuTypeLenient(module, req.ElementType));
        if (t is OptionalModifierType opt)
            return new OptionalModifierType(
                MapCpuTypeLenient(module, opt.ModifierType), MapCpuTypeLenient(module, opt.ElementType));
        return module.ImportReference(t);
    }
    private static TypeDefinition ResolveCpuDef(TypeReference tr)
    {
        return tr.Resolve()
            ?? throw new InvalidOperationException($"CpuPaint: cannot resolve {tr.FullName}");
    }

    // Definition from another PDN assembly by name: the resolver
    // loads it out of the input closure (version-agnostic). Used for
    // types the target never names, where no owned row exists.
    private static TypeDefinition ResolveExternalDef(ModuleDefinition module, string assemblyName, string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        var tr = new TypeReference(
            fullName.Substring(0, dot), fullName.Substring(dot + 1),
            module, new AssemblyNameReference(assemblyName, new Version(0, 0, 0, 0)));
        return ResolveCpuDef(tr);
    }

    private static TypeReference OwnedCpuRef(ModuleDefinition module, string fullName)
    {
        return module.GetTypeReferences().FirstOrDefault(x => x.FullName == fullName)
            ?? throw new InvalidOperationException(
                $"CpuPaint: no target-owned reference for {fullName} in {module.Assembly.Name.Name}");
    }

    private static TypeReference LocalOrOwnedCpuRef(ModuleDefinition module, string fullName)
    {
        return (TypeReference?)module.GetType(fullName) ?? OwnedCpuRef(module, fullName);
    }

    // Scope-created reference: builds a TypeReference against another
    // PDN assembly the target already references (for types the target
    // never names, where no owned row exists). Null when the target
    // does not reference that assembly: the rule then skips the shape
    // instead of breaking the regen.
    private static TypeReference? ScopedCpuRef(
        ModuleDefinition module, string assemblyName, string @namespace, string name)
    {
        TypeReference? local = module.GetType(@namespace + "." + name);
        if (local is not null)
            return local;
        var scope = module.AssemblyReferences.FirstOrDefault(a => a.Name == assemblyName);
        if (scope is null)
            return null;
        return new TypeReference(@namespace, name, module, scope);
    }

    private static TypeReference LocalOrOwnedCpuElement(ModuleDefinition module, string ns, string name)
    {
        TypeReference? local = module.GetType(ns + "." + name);
        if (local is not null)
            return local;
        return OwnedCpuElement(module, ns, name);
    }

    private static bool ImplementsCpuGeneric(TypeDefinition def, string elementFullName)
    {
        var stack = new Stack<TypeDefinition>();
        stack.Push(def);
        while (stack.Count > 0)
        {
            TypeDefinition current = stack.Pop();
            foreach (var ii in current.Interfaces)
            {
                if (ii.InterfaceType is GenericInstanceType gi
                    && gi.ElementType.FullName == elementFullName)
                    return true;
                TypeDefinition? d = ii.InterfaceType.Resolve();
                if (d is not null)
                    stack.Push(d);
            }
        }
        return false;
    }

    private static TypeReference OwnedCpuElement(ModuleDefinition module, string ns, string name)
    {
        return module.GetTypeReferences()
            .FirstOrDefault(x => x.Namespace == ns && x.Name == name && x is not TypeSpecification)
            ?? throw new InvalidOperationException(
                $"CpuPaint: no target-owned {ns}.{name} reference in {module.Assembly.Name.Name}");
    }


    // A proven call site for a closed-generic (or generic-var) member:
    // returns the original opcode plus the in-module operand, reused
    // verbatim so no token is ever hand-built for these shapes.
    private static (OpCode Op, MethodReference Ref)? HarvestCpuCall(
        ModuleDefinition module, string elementFullName, string methodName, bool genericArgument)
    {
        foreach (TypeDefinition t in module.GetTypes())
        {
            foreach (MethodDefinition m in t.Methods)
            {
                if (!m.HasBody)
                    continue;
                foreach (Instruction ins in m.Body.Instructions)
                {
                    if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                        && ins.Operand is MethodReference mr && mr.Name == methodName
                        && mr.DeclaringType is GenericInstanceType gi
                        && gi.ElementType.FullName == elementFullName
                        && (!genericArgument
                            || (gi.GenericArguments.Count == 1
                                && gi.GenericArguments[0] is GenericParameter)))
                        return (ins.OpCode, mr);
                }
            }
        }
        return null;
    }

    // Instance getter on a PDN value type (emitted with call, the way
    // the compiler calls struct accessors).
    private static readonly Dictionary<string, AssemblyDefinition> CpuPaintCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static MethodReference ImportCpuPaintMethod(
        ModuleDefinition module, string? cpuPaintPath, string name, int argc)
    {
        if (cpuPaintPath is null || !File.Exists(cpuPaintPath))
            throw new InvalidOperationException(
                $"CpuPaint: helper assembly missing for {name}/{argc}");
        if (!CpuPaintCache.TryGetValue(cpuPaintPath, out AssemblyDefinition? cpuAsm))
        {
            cpuAsm = AssemblyDefinition.ReadAssembly(cpuPaintPath);
            CpuPaintCache[cpuPaintPath] = cpuAsm;
        }
        if (module.AssemblyReferences.All(r => r.Name != cpuAsm.Name.Name))
            module.AssemblyReferences.Add(new AssemblyNameReference(cpuAsm.Name.Name, cpuAsm.Name.Version));
        MethodDefinition? def = cpuAsm.MainModule.GetType("WfWasm.PdnCpu.CpuPaint")?.Methods
            .FirstOrDefault(m => m.Name == name && m.Parameters.Count == argc)
            ?? throw new InvalidOperationException($"CpuPaint: CpuPaint.{name}/{argc} missing");
        return module.ImportReference(def);
    }

    private static MethodReference StructCpuGetter(ModuleDefinition module, string typeFull, string getterName)
    {
        TypeReference decl = LocalOrOwnedCpuRef(module, typeFull);
        MethodDefinition? impl = ResolveCpuDef(decl).Methods
            .FirstOrDefault(m => m.Name == getterName && m.Parameters.Count == 0 && !m.IsStatic)
            ?? throw new InvalidOperationException($"CpuPaint: {typeFull}.{getterName} missing");
        return new MethodReference(impl.Name, MapCpuType(module, impl.ReturnType), decl) { HasThis = true };
    }

    // Instance call on a PDN class/interface (emitted with callvirt).
    private static MethodReference VirtualCpuCall(
        ModuleDefinition module, string typeFull, string methodName, int paramCount)
    {
        TypeReference decl = LocalOrOwnedCpuRef(module, typeFull);
        MethodDefinition? impl = ResolveCpuDef(decl).Methods
            .FirstOrDefault(m => m.Name == methodName && m.Parameters.Count == paramCount && !m.IsStatic)
            ?? throw new InvalidOperationException(
                $"CpuPaint: {typeFull}.{methodName}/{paramCount} missing");
        var mr = new MethodReference(impl.Name, MapCpuType(module, impl.ReturnType), decl) { HasThis = true };
        foreach (var p in impl.Parameters)
            mr.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
        return mr;
    }

    private static MethodReference CtorCpuRef(ModuleDefinition module, string typeFull, int paramCount)
    {
        TypeReference decl = LocalOrOwnedCpuRef(module, typeFull);
        MethodDefinition? impl = ResolveCpuDef(decl).Methods
            .FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == paramCount)
            ?? throw new InvalidOperationException($"CpuPaint: {typeFull}..ctor/{paramCount} missing");
        var mr = new MethodReference(impl.Name, module.TypeSystem.Void, decl) { HasThis = true };
        foreach (var p in impl.Parameters)
            mr.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
        return mr;
    }

    // Lenient twin of CtorCpuRef for signatures naming types the
    // target module never references (missing rows import from the
    // contract's own module instead of throwing).
    private static MethodReference LenientCtorCpuRef(ModuleDefinition module, string typeFull, int paramCount)
    {
        TypeReference decl = LocalOrOwnedCpuRef(module, typeFull);
        MethodDefinition? impl = ResolveCpuDef(decl).Methods
            .FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == paramCount)
            ?? throw new InvalidOperationException($"CpuPaint: {typeFull}..ctor/{paramCount} missing");
        var mr = new MethodReference(impl.Name, module.TypeSystem.Void, decl) { HasThis = true };
        foreach (var p in impl.Parameters)
            mr.Parameters.Add(new ParameterDefinition(MapCpuTypeLenient(module, p.ParameterType)));
        return mr;
    }

    private static MethodDefinition FindBaseVirtual(
        TypeDefinition start, string name, Func<MethodDefinition, bool> match)
    {
        for (TypeDefinition? t = start; t is not null; t = t.BaseType?.Resolve())
        {
            MethodDefinition? m = t.Methods.FirstOrDefault(x =>
                x.Name == name && x.IsVirtual && !x.IsStatic && match(x));
            if (m is not null)
                return m;
        }
        throw new InvalidOperationException($"CpuPaint: base virtual {name} missing above {start.FullName}");
    }

    private static void EmitThrowNotSupported(ILProcessor il, ModuleDefinition module)
    {
        var ctor = module.ImportReference(typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Throw);
    }

    private static IEnumerable<MethodDefinition> InterfaceCpuMethods(TypeDefinition iface)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<TypeDefinition>();
        stack.Push(iface);
        while (stack.Count > 0)
        {
            TypeDefinition current = stack.Pop();
            foreach (MethodDefinition m in current.Methods)
            {
                string key = m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")";
                if (seen.Add(key))
                    yield return m;
            }
            foreach (var ii in current.Interfaces)
            {
                TypeDefinition? d = ii.InterfaceType.Resolve()
                    ?? throw new InvalidOperationException(
                        $"CpuPaint: cannot resolve base interface {ii.InterfaceType.FullName}");
                stack.Push(d);
            }
        }
    }

    // IObjectRef on CPU device objects: the resource cache returns the
    // created resource itself as its own ref (the generic extension's
    // unbox.any proves the runtime shape), so the CPU bitmap and brush
    // answer the two ref-tracking members. TryCreateRef stays neutral
    // (null = no claim): nothing on the stamp path consumes it, and a
    // wrong claim would poison ValidateRef-style checks elsewhere.
    private static void AddCpuObjectRef(TypeDefinition type, ModuleDefinition module)
    {
        // The device interfaces already extend IObjectRef, so the
        // synthesis loop above emitted same-named defaults for these
        // members: drop them (identical signatures would stay ambiguous
        // for name-based call sites) and install the real answers.
        foreach (var dup in type.Methods
            .Where(m => m.Name == "TryCreateRef" || m.Name == "InternalImplementationOnly").ToList())
            type.Methods.Remove(dup);
        TypeReference ioRef = LocalOrOwnedCpuRef(module, "PaintDotNet.ComponentModel.IObjectRef");
        type.Interfaces.Add(new InterfaceImplementation(ioRef));
        var nullableBool = new GenericInstanceType(module.ImportReference(typeof(Nullable<>)));
        nullableBool.GenericArguments.Add(module.TypeSystem.Boolean);
        var tryCreate = new MethodDefinition(
            "TryCreateRef",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            nullableBool);
        TypeReference sysType = LocalOrOwnedCpuRef(module, "System.Type");
        tryCreate.Parameters.Add(new ParameterDefinition(sysType));
        tryCreate.Parameters.Add(new ParameterDefinition(new ByReferenceType(ioRef)));
        var tri = tryCreate.Body.GetILProcessor();
        var nullLoc = new VariableDefinition(nullableBool);
        tryCreate.Body.Variables.Add(nullLoc);
        tryCreate.Body.InitLocals = true;
        // A live CPU stand-in answers refs to itself for interfaces
        // it implements (CreateRef throws when this answers null,
        // observed as ObjectDisposed out of ManagedColor.Create).
        // Anything else keeps the null default (can't provide).
        MethodReference isInstance = module.ImportReference(
            typeof(Type).GetMethod("IsInstanceOfType", new[] { typeof(object) })!);
        MethodReference nullableCtor = module.ImportReference(
            typeof(bool?).GetConstructor(new[] { typeof(bool) })!);
        // IComObjectRef has no CPU backend identity (real COM only), but
        // BitmapImage/BufferedImage ctors demand the token: answer a CPU
        // stand-in that roots the bitmap and fails QueryInterface loudly.
        // Borrowed (no refs++): the image's own ref owns the pixels.
        var notCom = tri.Create(OpCodes.Nop);
        MethodReference typeFullName = module.ImportReference(typeof(Type).GetMethod("get_FullName", Type.EmptyTypes)!);
        MethodReference strEq = module.ImportReference(typeof(string).GetMethod("op_Equality", new[] { typeof(string), typeof(string) })!);
        // Tolerant: while the com stand-in itself is under construction
        // it is not in the module yet; its own TryCreateRef answers
        // through IsInstanceOfType (it implements the interface).
        TypeDefinition? comDef = module.GetType("PaintDotNet.Direct2D1.CpuComObjectRef");
        MethodDefinition? comCtorDef = comDef?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 1);
        if (comCtorDef is not null)
        {
        tri.Emit(OpCodes.Ldarg_1);
        tri.Emit(OpCodes.Callvirt, typeFullName);
        tri.Emit(OpCodes.Ldstr, "PaintDotNet.Interop.IComObjectRef");
        tri.Emit(OpCodes.Call, strEq);
        tri.Emit(OpCodes.Brfalse_S, notCom);
        tri.Emit(OpCodes.Ldarg_2);
        tri.Emit(OpCodes.Ldarg_0);
        tri.Emit(OpCodes.Newobj, comCtorDef);
        tri.Emit(OpCodes.Stind_Ref);
        tri.Emit(OpCodes.Ldc_I4_1);
        tri.Emit(OpCodes.Newobj, nullableCtor);
        tri.Emit(OpCodes.Ret);
        }
        tri.Append(notCom);
        var no = tri.Create(OpCodes.Nop);
        tri.Emit(OpCodes.Ldarg_1);
        tri.Emit(OpCodes.Ldarg_0);
        tri.Emit(OpCodes.Callvirt, isInstance);
        tri.Emit(OpCodes.Brfalse_S, no);
        tri.Emit(OpCodes.Ldarg_2);
        tri.Emit(OpCodes.Ldarg_0);
        tri.Emit(OpCodes.Stind_Ref);
        tri.Emit(OpCodes.Ldc_I4_1);
        tri.Emit(OpCodes.Newobj, nullableCtor);
        tri.Emit(OpCodes.Ret);
        tri.Append(no);
        tri.Emit(OpCodes.Ldarg_2);
        tri.Emit(OpCodes.Ldnull);
        tri.Emit(OpCodes.Stind_Ref);
        tri.Emit(OpCodes.Ldloc, nullLoc);
        tri.Emit(OpCodes.Ret);
        tryCreate.Body.MaxStackSize = 32;
        type.Methods.Add(tryCreate);
        var marker = new MethodDefinition(
            "InternalImplementationOnly",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            module.TypeSystem.Void);
        marker.Body.GetILProcessor().Emit(OpCodes.Ret);
        type.Methods.Add(marker);
    }
    // CpuCommandList: SetTarget(list) redirects dc fills into a scratch
    // surface (the vendor CommandListDrawingScope saves/restores the
    // target around direct draws); DrawImage(list) blits the scratch
    // back in desktop order, Dispose frees it. Without the redirect,
    // immediate fills land early and a later opaque DrawImage buries
    // them (observed: PDN Colors palette wells under the checkers
    // layer). Anything else returning a value fails loudly.
    private static TypeDefinition SynthesizeCpuCommandList(ModuleDefinition module, MethodReference freeScratch)
    {
        TypeReference iface = LocalOrOwnedCpuRef(module, "PaintDotNet.Direct2D1.ICommandList");
        TypeDefinition ifaceDef = ResolveCpuDef(iface);
        var type = new TypeDefinition(
            "PaintDotNet.Direct2D1", "CpuCommandList",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        type.Interfaces.Add(new InterfaceImplementation(iface));
        // Vendor ICommandList extends IDeviceImage, so recorded lists can
        // travel through SetTarget/DrawImage as images. The contract walk
        // below already emits IDeviceImage member defaults (inheritance
        // walk); only the declaration is missing. Fixture modules without
        // the imaging universe keep today's shape (casts there never run
        // PDN paint).
        try
        {
            TypeReference imageIface = LocalOrOwnedCpuRef(module, "PaintDotNet.Direct2D1.IDeviceImage");
            type.Interfaces.Add(new InterfaceImplementation(imageIface));
        }
        catch (Exception ex) when (ex is InvalidOperationException
            || ex is Mono.Cecil.ResolutionException || ex is ArgumentException) { }
        // Redirect capture: SetTarget(list) swaps the dc target to a
        // scratch surface (created on first redirect, sized to the live
        // target) and remembers the displaced target here; DrawImage(list)
        // blits the scratch back; Dispose frees it. Per-paint lists are
        // disposed right after DrawImage, so nothing leaks in that flow.
        var scratchField = new FieldDefinition("_scratch", FieldAttributes.Public, module.TypeSystem.Int64);
        var savedField = new FieldDefinition("_saved", FieldAttributes.Public, module.TypeSystem.Int64);
        type.Fields.Add(scratchField);
        type.Fields.Add(savedField);
        MethodReference objectCtor = module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        var cil = ctor.Body.GetILProcessor();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, objectCtor);
        cil.Emit(OpCodes.Ret);
        type.Methods.Add(ctor);
        foreach (MethodDefinition contract in InterfaceCpuMethods(ifaceDef))
        {
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuType(module, contract.ReturnType));
            foreach (var px in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(px.Name, px.Attributes, MapCpuType(module, px.ParameterType)));
            var body = impl.Body.GetILProcessor();
            if (contract.ReturnType.FullName == "System.Void")
            {
                body.Emit(OpCodes.Ret);
            }
            else if (MapCpuType(module, contract.ReturnType).Resolve()?.IsValueType == true)
            {
                var zero = new VariableDefinition(MapCpuType(module, contract.ReturnType));
                impl.Body.Variables.Add(zero);
                impl.Body.InitLocals = true;
                body.Emit(OpCodes.Ldloca_S, zero);
                body.Emit(OpCodes.Initobj, MapCpuType(module, contract.ReturnType));
                body.Emit(OpCodes.Ldloc, zero);
                body.Emit(OpCodes.Ret);
            }
            else
            {
                body.Emit(OpCodes.Ldstr, "CpuCommandList." + contract.Name + " has no headless backend");
                body.Emit(OpCodes.Newobj, module.ImportReference(typeof(NotSupportedException).GetConstructor([typeof(string)])!));
                body.Emit(OpCodes.Throw);
            }
            impl.Body.MaxStackSize = 32;
            type.Methods.Add(impl);
        }
        // Scratch lifetime: vendor code disposes the command list BEFORE
        // the final composite (palette wraps the list in the effect,
        // disposes it, then draws), so Dispose must NOT free the scratch
        // (desktop Dispose only drops a reference; the recording stays
        // alive through the effect). DrawImage consumes the scratch on
        // first playback (blit+free+zero, single-playback headless); this
        // finalizer backstops lists that are never drawn.
        var finalizer = new MethodDefinition(
            "Finalize",
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        {
            var fil = finalizer.Body.GetILProcessor();
            var finDone = fil.Create(OpCodes.Nop);
            fil.Emit(OpCodes.Ldarg_0);
            fil.Emit(OpCodes.Ldfld, scratchField);
            fil.Emit(OpCodes.Brfalse, finDone);
            fil.Emit(OpCodes.Ldarg_0);
            fil.Emit(OpCodes.Ldfld, scratchField);
            fil.Emit(OpCodes.Call, freeScratch);
            fil.Emit(OpCodes.Ldarg_0);
            fil.Emit(OpCodes.Ldc_I4_0);
            fil.Emit(OpCodes.Conv_I8);
            fil.Emit(OpCodes.Stfld, scratchField);
            fil.Append(finDone);
            fil.Emit(OpCodes.Ret);
        }
        finalizer.Body.MaxStackSize = 32;
        type.Methods.Add(finalizer);
        // IObjectRef answers: EffectInputAccessor.Set routes every crop
        // input through SetInput -> CreateRef(IObjectRef), which throws
        // ObjectDisposed when TryCreateRef answers null (observed live:
        // ColorGradient/Rectangle/PaletteCommandList inputs died with ODE
        // although nothing was ever disposed). Every sibling stand-in
        // answers itself here; the list was the only one missing it.
        AddCpuObjectRef(type, module);
        module.Types.Add(type);
        return type;
    }
    private static TypeDefinition SynthesizeCpuDeviceBitmap(
        ModuleDefinition module,
        MethodReference createBitmapEmpty,
        MethodReference freeBitmap)
    {
        TypeReference iface = OwnedCpuRef(module, "PaintDotNet.Direct2D1.IDeviceBitmap");
        TypeDefinition ifaceDef = ResolveCpuDef(iface);
        var type = new TypeDefinition(
            "PaintDotNet.Direct2D1", "CpuDeviceBitmap",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        type.Interfaces.Add(new InterfaceImplementation(iface));
        // Backing fields are public: sibling synthesized types (the
        // drawing context) read them cross-class, and private fields
        // throw FieldAccessException there (observed live on _handle).
        var handleField = new FieldDefinition("_handle", FieldAttributes.Public, module.TypeSystem.Int64);
        var widthField = new FieldDefinition("_width", FieldAttributes.Public, module.TypeSystem.Int32);
        var heightField = new FieldDefinition("_height", FieldAttributes.Public, module.TypeSystem.Int32);
        // COM-style live count: CreateImpl hands ownership to effects/images
        // then disposes its locals (SetInput -> Dispose -> CreateRef), so
        // the pixels must survive until the last ref releases. The owner
        // holds 1 from construction; every granted ref adds one.
        var refsField = new FieldDefinition("_refs", FieldAttributes.Public, module.TypeSystem.Int32);
        type.Fields.Add(handleField);
        type.Fields.Add(widthField);
        type.Fields.Add(heightField);
        type.Fields.Add(refsField);

        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int64));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        var cil = ctor.Body.GetILProcessor();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, objectCtor);
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Ldarg_1);
        cil.Emit(OpCodes.Stfld, handleField);
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Ldarg_2);
        cil.Emit(OpCodes.Stfld, widthField);
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Ldarg_3);
        cil.Emit(OpCodes.Stfld, heightField);
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Ldc_I4_1);
        cil.Emit(OpCodes.Stfld, refsField);
        cil.Emit(OpCodes.Ret);
        type.Methods.Add(ctor);

        MethodReference? sizeCtor = null;
        foreach (MethodDefinition contract in InterfaceCpuMethods(ifaceDef))
        {
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuType(module, contract.ReturnType));
            foreach (var p in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
            var body = impl.Body.GetILProcessor();
            if (contract.Name == "Dispose" && contract.Parameters.Count == 0)
            {
                // One release per call; the pixels die with the last
                // ref (effects/images hold theirs past our locals).
                var realFree = body.Create(OpCodes.Nop);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Dup);
                body.Emit(OpCodes.Ldfld, refsField);
                body.Emit(OpCodes.Ldc_I4_1);
                body.Emit(OpCodes.Sub);
                body.Emit(OpCodes.Stfld, refsField);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, refsField);
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Ble_S, realFree);
                body.Emit(OpCodes.Ret);
                body.Append(realFree);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, handleField);
                body.Emit(OpCodes.Call, freeBitmap);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Conv_I8);
                body.Emit(OpCodes.Stfld, handleField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_PixelSize" && contract.Parameters.Count == 0)
            {
                sizeCtor ??= CtorCpuRef(module, "PaintDotNet.Rendering.SizeInt32", 2);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, widthField);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, heightField);
                body.Emit(OpCodes.Newobj, sizeCtor);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_Size" && contract.Parameters.Count == 0)
            {
                // SizeFloat(w, h): match the constructor's own element
                // types instead of transcribing them.
                TypeReference sizeFloat = OwnedCpuRef(module, "PaintDotNet.Rendering.SizeFloat");
                MethodDefinition? sc = ResolveCpuDef(sizeFloat).Methods
                    .FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 2)
                    ?? throw new InvalidOperationException("CpuPaint: SizeFloat..ctor/2 missing");
                var scRef = new MethodReference(sc.Name, module.TypeSystem.Void, sizeFloat) { HasThis = true };
                foreach (var p in sc.Parameters)
                    scRef.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, widthField);
                if (sc.Parameters[0].ParameterType.FullName == "System.Double")
                    body.Emit(OpCodes.Conv_R8);
                else
                    body.Emit(OpCodes.Conv_R4);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, heightField);
                if (sc.Parameters[1].ParameterType.FullName == "System.Double")
                    body.Emit(OpCodes.Conv_R8);
                else
                    body.Emit(OpCodes.Conv_R4);
                body.Emit(OpCodes.Newobj, scRef);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_IsDisposed" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, handleField);
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Conv_I8);
                body.Emit(OpCodes.Ceq);
                body.Emit(OpCodes.Ret);
            }
            else
            {
                EmitThrowNotSupported(body, module);
            }
            impl.Body.MaxStackSize = 32;
            type.Methods.Add(impl);
        }

        AddCpuObjectRef(type, module);
        GrantRefsOnTryCreateRef(type, module);
        module.Types.Add(type);
        return type;
    }

    // CpuComObjectRef: IComObjectRef over CPU device truth. BitmapImage
    // (and BufferedImage) ctors demand the COM token for their image, but
    // real COM identity only exists on Windows. The stand-in roots the
    // device object (GC lifetime) and answers E_NOINTERFACE from
    // QueryInterface: plumbing that merely holds the token (image
    // lifetime) works, any real native call fails loudly at use.
    // Disposal is a no-op: the target outlives through its own refs.
    private static TypeDefinition SynthesizeCpuComObjectRef(ModuleDefinition module)
    {
        TypeReference iface = OwnedCpuRef(module, "PaintDotNet.Interop.IComObjectRef");
        TypeDefinition ifaceDef = ResolveCpuDef(iface);
        TypeReference objRef = OwnedCpuRef(module, "PaintDotNet.ComponentModel.IObjectRef");
        var type = new TypeDefinition(
            "PaintDotNet.Direct2D1", "CpuComObjectRef",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        type.Interfaces.Add(new InterfaceImplementation(iface));
        var targetField = new FieldDefinition("_target", FieldAttributes.Public, objRef);
        type.Fields.Add(targetField);
        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition(objRef));
        var cil = ctor.Body.GetILProcessor();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, objectCtor);
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Ldarg_1);
        cil.Emit(OpCodes.Stfld, targetField);
        cil.Emit(OpCodes.Ret);
        type.Methods.Add(ctor);
        foreach (MethodDefinition contract in InterfaceCpuMethods(ifaceDef))
        {
            if ((contract.Name == "TryCreateRef" || contract.Name == "InternalImplementationOnly")
                && contract.Parameters.Count <= 2)
                continue;
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuType(module, contract.ReturnType));
            foreach (var p in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, MapCpuType(module, p.ParameterType)));
            var body = impl.Body.GetILProcessor();
            if (contract.Name == "QueryInterface" && contract.Parameters.Count == 2)
            {
                body.Emit(OpCodes.Ldarg_2);
                body.Emit(OpCodes.Ldnull);
                body.Emit(OpCodes.Stind_I);
                body.Emit(OpCodes.Ldc_I4, unchecked((int)0x80004002));
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "Dispose" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_IsDisposed" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Ret);
            }
            else
            {
                EmitThrowNotSupported(body, module);
            }
            impl.Body.MaxStackSize = 32;
            type.Methods.Add(impl);
        }
        AddCpuObjectRef(type, module);
        module.Types.Add(type);
        return type;
    }
    // CpuDeviceBitmap grants one live ref per successful TryCreateRef:
    // the generic helper answers without tracking, but every granted
    // ref must hold the pixels past the granter's Dispose (CreateImpl
    // hands ownership to effects/images, then disposes its locals).
    private static void GrantRefsOnTryCreateRef(TypeDefinition type, ModuleDefinition module)
    {
        var generic = type.Methods.First(m => m.Name == "TryCreateRef");
        type.Methods.Remove(generic);
        TypeReference ioRef = LocalOrOwnedCpuRef(module, "PaintDotNet.ComponentModel.IObjectRef");
        var nullableBool = new GenericInstanceType(module.ImportReference(typeof(Nullable<>)));
        nullableBool.GenericArguments.Add(module.TypeSystem.Boolean);
        TypeReference sysType = LocalOrOwnedCpuRef(module, "System.Type");
        var refsField = type.Fields.First(f => f.Name == "_refs");
        var tryCreate = new MethodDefinition(
            "TryCreateRef",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            nullableBool);
        tryCreate.Parameters.Add(new ParameterDefinition(sysType));
        tryCreate.Parameters.Add(new ParameterDefinition(new ByReferenceType(ioRef)));
        var tri = tryCreate.Body.GetILProcessor();
        var nullLoc = new VariableDefinition(nullableBool);
        tryCreate.Body.Variables.Add(nullLoc);
        tryCreate.Body.InitLocals = true;
        MethodReference isInstance = module.ImportReference(
            typeof(Type).GetMethod("IsInstanceOfType", new[] { typeof(object) })!);
        MethodReference nullableCtor = module.ImportReference(
            typeof(bool?).GetConstructor(new[] { typeof(bool) })!);
        // IComObjectRef has no CPU backend identity (real COM only), but
        // BitmapImage/BufferedImage ctors demand the token: answer a CPU
        // stand-in that roots the bitmap and fails QueryInterface loudly.
        // Borrowed (no refs++): the image's own ref owns the pixels.
        var notCom = tri.Create(OpCodes.Nop);
        MethodReference typeFullName = module.ImportReference(typeof(Type).GetMethod("get_FullName", Type.EmptyTypes)!);
        MethodReference strEq = module.ImportReference(typeof(string).GetMethod("op_Equality", new[] { typeof(string), typeof(string) })!);
        // Tolerant: while the com stand-in itself is under construction
        // it is not in the module yet; its own TryCreateRef answers
        // through IsInstanceOfType (it implements the interface).
        TypeDefinition? comDef = module.GetType("PaintDotNet.Direct2D1.CpuComObjectRef");
        MethodDefinition? comCtorDef = comDef?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 1);
        if (comCtorDef is not null)
        {
        tri.Emit(OpCodes.Ldarg_1);
        tri.Emit(OpCodes.Callvirt, typeFullName);
        tri.Emit(OpCodes.Ldstr, "PaintDotNet.Interop.IComObjectRef");
        tri.Emit(OpCodes.Call, strEq);
        tri.Emit(OpCodes.Brfalse_S, notCom);
        tri.Emit(OpCodes.Ldarg_2);
        tri.Emit(OpCodes.Ldarg_0);
        tri.Emit(OpCodes.Newobj, comCtorDef);
        tri.Emit(OpCodes.Stind_Ref);
        tri.Emit(OpCodes.Ldc_I4_1);
        tri.Emit(OpCodes.Newobj, nullableCtor);
        tri.Emit(OpCodes.Ret);
        }
        tri.Append(notCom);
        var no = tri.Create(OpCodes.Nop);
        tri.Emit(OpCodes.Ldarg_1);
        tri.Emit(OpCodes.Ldarg_0);
        tri.Emit(OpCodes.Callvirt, isInstance);
        tri.Emit(OpCodes.Brfalse_S, no);
        tri.Emit(OpCodes.Ldarg_2);
        tri.Emit(OpCodes.Ldarg_0);
        tri.Emit(OpCodes.Stind_Ref);
        tri.Emit(OpCodes.Ldarg_0);
        tri.Emit(OpCodes.Ldarg_0);
        tri.Emit(OpCodes.Ldfld, refsField);
        tri.Emit(OpCodes.Ldc_I4_1);
        tri.Emit(OpCodes.Add);
        tri.Emit(OpCodes.Stfld, refsField);
        tri.Emit(OpCodes.Ldc_I4_1);
        tri.Emit(OpCodes.Newobj, nullableCtor);
        tri.Emit(OpCodes.Ret);
        tri.Append(no);
        tri.Emit(OpCodes.Ldarg_2);
        tri.Emit(OpCodes.Ldnull);
        tri.Emit(OpCodes.Stind_Ref);
        tri.Emit(OpCodes.Ldloc, nullLoc);
        tri.Emit(OpCodes.Ret);
        tryCreate.Body.MaxStackSize = 32;
        type.Methods.Add(tryCreate);
    }

    private static TypeDefinition SynthesizeCpuDeviceBrush(ModuleDefinition module)
    {
        TypeReference iface = OwnedCpuRef(module, "PaintDotNet.Direct2D1.ISolidColorBrush");
        TypeDefinition ifaceDef = ResolveCpuDef(iface);
        TypeReference matrixRef = OwnedCpuRef(module, "PaintDotNet.Rendering.Matrix3x2Float");
        var type = new TypeDefinition(
            "PaintDotNet.Direct2D1", "CpuDeviceBrush",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        type.Interfaces.Add(new InterfaceImplementation(iface));
        // Public like the bitmap's: read cross-class by the context.
        var opacityField = new FieldDefinition("_opacity", FieldAttributes.Public, module.TypeSystem.Single);
        var rField = new FieldDefinition("_r", FieldAttributes.Public, module.TypeSystem.Single);
        var gField = new FieldDefinition("_g", FieldAttributes.Public, module.TypeSystem.Single);
        var bField = new FieldDefinition("_b", FieldAttributes.Public, module.TypeSystem.Single);
        var aField = new FieldDefinition("_a", FieldAttributes.Public, module.TypeSystem.Single);
        var matrixField = new FieldDefinition("_transform", FieldAttributes.Public, matrixRef);
        type.Fields.Add(opacityField);
        type.Fields.Add(rField);
        type.Fields.Add(gField);
        type.Fields.Add(bField);
        type.Fields.Add(aField);
        type.Fields.Add(matrixField);

        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(matrixRef));
        var cil = ctor.Body.GetILProcessor();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, objectCtor);
        FieldDefinition[] stores = [opacityField, rField, gField, bField, aField, matrixField];
        for (int i = 0; i < stores.Length; i++)
        {
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Ldarg, ctor.Parameters[i]);
            cil.Emit(OpCodes.Stfld, stores[i]);
        }
        cil.Emit(OpCodes.Ret);
        type.Methods.Add(ctor);

        foreach (MethodDefinition contract in InterfaceCpuMethods(ifaceDef))
        {
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuType(module, contract.ReturnType));
            foreach (var p in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
            var body = impl.Body.GetILProcessor();
            if (contract.Name == "Dispose" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_Opacity" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, opacityField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "set_Opacity" && contract.Parameters.Count == 1)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Stfld, opacityField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_Transform" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, matrixField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "set_Transform" && contract.Parameters.Count == 1)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Stfld, matrixField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_Color" && contract.Parameters.Count == 0)
            {
                MethodReference colorCtor = CtorCpuRef(module, "PaintDotNet.Imaging.ColorRgba128Float", 4);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, rField);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, gField);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, bField);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, aField);
                body.Emit(OpCodes.Newobj, colorCtor);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "set_Color" && contract.Parameters.Count == 1)
            {
                MethodReference getR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
                MethodReference getG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
                MethodReference getB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
                MethodReference getA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
                (string Getter, FieldDefinition Field)[] channels =
                    [("R", rField), ("G", gField), ("B", bField), ("A", aField)];
                MethodReference[] getters = [getR, getG, getB, getA];
                for (int i = 0; i < 4; i++)
                {
                    body.Emit(OpCodes.Ldarg_0);
                    body.Emit(OpCodes.Ldarga_S, impl.Parameters[0]);
                    body.Emit(OpCodes.Call, getters[i]);
                    body.Emit(OpCodes.Stfld, channels[i].Field);
                }
                body.Emit(OpCodes.Ret);
            }
            else
            {
                EmitThrowNotSupported(body, module);
            }
            impl.Body.MaxStackSize = 32;
            type.Methods.Add(impl);
        }

        AddCpuObjectRef(type, module);
        module.Types.Add(type);
        return type;
    }

    // CpuLinearGradientBrush: ILinearGradientBrush over a sampled CPU
    // gradient. The cache branch samples the provider's stops into a
    // CpuPaint LUT once per version; the resource carries the LUT
    // handle, the brush axis, opacity and spread as public fields so
    // the FillRectangle rewrite stamps it without resolving the
    // provider again. Transform writes persist but the fill math stays
    // identity until a flow needs them; GradientStopCollection has no
    // CPU shape and fails loudly like other device-only members.
    private static TypeDefinition SynthesizeCpuLinearGradientBrush(ModuleDefinition module)
    {
        TypeReference iface = OwnedCpuRef(module, "PaintDotNet.Direct2D1.ILinearGradientBrush");
        TypeDefinition ifaceDef = ResolveCpuDef(iface);
        TypeReference matrixRef = OwnedCpuRef(module, "PaintDotNet.Rendering.Matrix3x2Float");
        MethodReference pointCtor = CtorCpuRef(module, "PaintDotNet.Rendering.Point2Float", 2);
        var type = new TypeDefinition(
            "PaintDotNet.Direct2D1", "CpuLinearGradientBrush",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        type.Interfaces.Add(new InterfaceImplementation(iface));
        // Public like the bitmap's and solid brush's: read cross-class
        // by the context and the fill rewrite.
        var lutField = new FieldDefinition("_lut", FieldAttributes.Public, module.TypeSystem.Int64);
        var x0Field = new FieldDefinition("_x0", FieldAttributes.Public, module.TypeSystem.Single);
        var y0Field = new FieldDefinition("_y0", FieldAttributes.Public, module.TypeSystem.Single);
        var x1Field = new FieldDefinition("_x1", FieldAttributes.Public, module.TypeSystem.Single);
        var y1Field = new FieldDefinition("_y1", FieldAttributes.Public, module.TypeSystem.Single);
        var opacityField = new FieldDefinition("_opacity", FieldAttributes.Public, module.TypeSystem.Single);
        var spreadField = new FieldDefinition("_spread", FieldAttributes.Public, module.TypeSystem.Int32);
        var matrixField = new FieldDefinition("_transform", FieldAttributes.Public, matrixRef);
        type.Fields.Add(lutField);
        type.Fields.Add(x0Field);
        type.Fields.Add(y0Field);
        type.Fields.Add(x1Field);
        type.Fields.Add(y1Field);
        type.Fields.Add(opacityField);
        type.Fields.Add(spreadField);
        type.Fields.Add(matrixField);

        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int64));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        var cil = ctor.Body.GetILProcessor();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, objectCtor);
        FieldDefinition[] stores = [lutField, x0Field, y0Field, x1Field, y1Field, opacityField, spreadField];
        for (int i = 0; i < stores.Length; i++)
        {
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Ldarg, ctor.Parameters[i]);
            cil.Emit(OpCodes.Stfld, stores[i]);
        }
        cil.Emit(OpCodes.Ret);
        type.Methods.Add(ctor);

        foreach (MethodDefinition contract in InterfaceCpuMethods(ifaceDef))
        {
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuTypeLenient(module, contract.ReturnType));
            foreach (var p in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, MapCpuTypeLenient(module, p.ParameterType)));
            var body = impl.Body.GetILProcessor();
            if (contract.Name == "Dispose" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_Opacity" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, opacityField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "set_Opacity" && contract.Parameters.Count == 1)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Stfld, opacityField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_Transform" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, matrixField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "set_Transform" && contract.Parameters.Count == 1)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Stfld, matrixField);
                body.Emit(OpCodes.Ret);
            }
            else if ((contract.Name == "get_StartPoint" || contract.Name == "get_EndPoint")
                && contract.Parameters.Count == 0)
            {
                FieldDefinition xf = contract.Name == "get_StartPoint" ? x0Field : x1Field;
                FieldDefinition yf = contract.Name == "get_StartPoint" ? y0Field : y1Field;
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, xf);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, yf);
                body.Emit(OpCodes.Newobj, pointCtor);
                body.Emit(OpCodes.Ret);
            }
            else if ((contract.Name == "set_StartPoint" || contract.Name == "set_EndPoint")
                && contract.Parameters.Count == 1)
            {
                FieldDefinition xf = contract.Name == "set_StartPoint" ? x0Field : x1Field;
                FieldDefinition yf = contract.Name == "set_StartPoint" ? y0Field : y1Field;
                MethodReference getX = StructCpuGetter(module, "PaintDotNet.Rendering.Point2Float", "get_X");
                MethodReference getY = StructCpuGetter(module, "PaintDotNet.Rendering.Point2Float", "get_Y");
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldarga_S, impl.Parameters[0]);
                body.Emit(OpCodes.Call, getX);
                body.Emit(OpCodes.Stfld, xf);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldarga_S, impl.Parameters[0]);
                body.Emit(OpCodes.Call, getY);
                body.Emit(OpCodes.Stfld, yf);
                body.Emit(OpCodes.Ret);
            }
            else
            {
                EmitThrowNotSupported(body, module);
            }
            impl.Body.MaxStackSize = 32;
            type.Methods.Add(impl);
        }

        AddCpuObjectRef(type, module);
        module.Types.Add(type);
        return type;
    }

    // CpuDeviceColorContext: IDeviceColorContext over CPU color truth.
    // The default factory path (TryCreateDefaultColorContext) only
    // needs a valid token: ManagedColor.Create validates non-null and
    // every deeper consumer is native-only (BitmapImage, WIC). The
    // context wraps the managed Imaging IColorContext when one is
    // supplied (profile bytes + hash forward to it) and records the
    // requested color space otherwise (sRGB primaries stand in for the
    // simple profile; CPU blending is identity in sRGB).
    private static TypeDefinition SynthesizeCpuDeviceColorContext(ModuleDefinition module)
    {
        TypeReference iface = OwnedCpuRef(module, "PaintDotNet.Direct2D1.IDeviceColorContext");
        TypeDefinition ifaceDef = ResolveCpuDef(iface);
        TypeReference imagingRef = OwnedCpuRef(module, "PaintDotNet.Imaging.IColorContext");
        TypeDefinition imagingDef = ResolveCpuDef(imagingRef);
        TypeReference spaceRef = OwnedCpuRef(module, "PaintDotNet.Direct2D1.DeviceColorSpace");
        MethodReference pointCtor = CtorCpuRef(module, "PaintDotNet.Rendering.Point2Float", 2);
        // Lenient twin of CtorCpuRef: the gamma parameter type is
        // never named by the target module, so strict mapping would
        // throw (the token itself binds through the referenced PDN
        // assembly at load).
        MethodReference simpleCtor = LenientCtorCpuRef(module, "PaintDotNet.Direct2D1.SimpleColorProfile", 5);
        MethodReference imagingProfileBytes = module.ImportReference(imagingDef.Methods
            .First(m => m.Name == "GetProfileBytes" && m.Parameters.Count == 0));
        MethodReference imagingBigHash = module.ImportReference(imagingDef.Methods
            .First(m => m.Name == "get_ProfileBytesBigHash" && m.Parameters.Count == 0));
        var type = new TypeDefinition(
            "PaintDotNet.Direct2D1", "CpuDeviceColorContext",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        type.Interfaces.Add(new InterfaceImplementation(iface));
        var innerField = new FieldDefinition("_inner", FieldAttributes.Private, imagingRef);
        var spaceField = new FieldDefinition("_space", FieldAttributes.Private, spaceRef);
        var kindField = new FieldDefinition("_kind", FieldAttributes.Private, module.TypeSystem.Int32);
        type.Fields.Add(innerField);
        type.Fields.Add(spaceField);
        type.Fields.Add(kindField);
        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition(imagingRef));
        ctor.Parameters.Add(new ParameterDefinition(spaceRef));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        var ccil = ctor.Body.GetILProcessor();
        ccil.Emit(OpCodes.Ldarg_0);
        ccil.Emit(OpCodes.Call, objectCtor);
        (FieldDefinition Field, int Index)[] slots = [(innerField, 0), (spaceField, 1), (kindField, 2)];
        foreach (var (field, index) in slots)
        {
            ccil.Emit(OpCodes.Ldarg_0);
            ccil.Emit(OpCodes.Ldarg, ctor.Parameters[index]);
            ccil.Emit(OpCodes.Stfld, field);
        }
        ccil.Emit(OpCodes.Ret);
        type.Methods.Add(ctor);
        void ForwardInner(MethodDefinition impl, ILProcessor body, MethodReference target, MethodDefinition contract)
        {
            var innerLoc = new VariableDefinition(imagingRef);
            var emptyLoc = new VariableDefinition(MapCpuTypeLenient(module, contract.ReturnType));
            impl.Body.Variables.Add(innerLoc);
            impl.Body.Variables.Add(emptyLoc);
            impl.Body.InitLocals = true;
            var noInner = body.Create(OpCodes.Nop);
            body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Ldfld, innerField);
            body.Emit(OpCodes.Stloc, innerLoc);
            body.Emit(OpCodes.Ldloc, innerLoc);
            body.Emit(OpCodes.Brfalse, noInner);
            body.Emit(OpCodes.Ldloc, innerLoc);
            body.Emit(OpCodes.Callvirt, target);
            body.Emit(OpCodes.Ret);
            body.Append(noInner);
            body.Emit(OpCodes.Ldloca_S, emptyLoc);
            body.Emit(OpCodes.Initobj, MapCpuTypeLenient(module, contract.ReturnType));
            body.Emit(OpCodes.Ldloc, emptyLoc);
            body.Emit(OpCodes.Ret);
        }
        foreach (MethodDefinition contract in InterfaceCpuMethods(ifaceDef))
        {
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuTypeLenient(module, contract.ReturnType));
            foreach (var p in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, MapCpuTypeLenient(module, p.ParameterType)));
            var body = impl.Body.GetILProcessor();
            if (contract.Name == "get_Type" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, kindField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_ColorSpace" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, spaceField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_DxgiColorSpace" && contract.Parameters.Count == 0)
            {
                // sRGB maps to the full-range gamma-2.2 P709 space,
                // scRGB to gamma-1.0; anything else is custom.
                var notSrgb = body.Create(OpCodes.Nop);
                var notScRgb = body.Create(OpCodes.Nop);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, spaceField);
                body.Emit(OpCodes.Ldc_I4_1);
                body.Emit(OpCodes.Ceq);
                body.Emit(OpCodes.Brfalse, notSrgb);
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Ret);
                body.Append(notSrgb);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, spaceField);
                body.Emit(OpCodes.Ldc_I4_2);
                body.Emit(OpCodes.Ceq);
                body.Emit(OpCodes.Brfalse, notScRgb);
                body.Emit(OpCodes.Ldc_I4_1);
                body.Emit(OpCodes.Ret);
                body.Append(notScRgb);
                body.Emit(OpCodes.Ldc_I4_M1);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_SimpleColorProfile" && contract.Parameters.Count == 0)
            {
                // sRGB IEC 61966-2-1 primaries + D65 white, G22 transfer.
                (float X, float Y)[] primaries =
                    [(0.64f, 0.33f), (0.30f, 0.60f), (0.15f, 0.06f), (0.3127f, 0.3290f)];
                foreach (var (x, y) in primaries)
                {
                    body.Emit(OpCodes.Ldc_R4, x);
                    body.Emit(OpCodes.Ldc_R4, y);
                    body.Emit(OpCodes.Newobj, pointCtor);
                }
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Newobj, simpleCtor);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "GetProfileBytes" && contract.Parameters.Count == 0)
            {
                ForwardInner(impl, body, imagingProfileBytes, contract);
            }
            else if (contract.Name == "get_ProfileBytesBigHash" && contract.Parameters.Count == 0)
            {
                ForwardInner(impl, body, imagingBigHash, contract);
            }
            else if (contract.Name == "Dispose" && contract.Parameters.Count == 0)
            {
                // CPU color truth holds no device resources: CreateImpl
                // disposes the converted source color context on every
                // image build, so disposal must succeed silently.
                body.Emit(OpCodes.Ret);
            }
            else
            {
                EmitThrowNotSupported(body, module);
            }
            impl.Body.MaxStackSize = 32;
            type.Methods.Add(impl);
        }
        AddCpuObjectRef(type, module);
        module.Types.Add(type);
        return type;
    }

    // CpuDeviceEffect: the headless IDeviceEffect behind
    // CpuDrawingContext.CreateEffect. Procedural and color effects
    // construct through CustomEffect..ctor (auto-registration calls
    // TryCreateEffect, which answers this), so their Properties bag
    // is real and every radio/color render stops NREing. Inputs are
    // held in a Hashtable (EffectInputAccessor.Set lands here through
    // the DeviceEffect wrapper); value Set/Get are inert no-ops (the
    // CPU stamp paths use classic defaults, never the device bag);
    // property metadata answers empty; factory/COM members stay loud.
    private static TypeDefinition SynthesizeCpuDeviceEffect(ModuleDefinition module, string? cpuPaintPath)
    {
        TypeReference iface = LocalOrOwnedCpuRef(module, "PaintDotNet.Direct2D1.IDeviceEffect");
        TypeDefinition ifaceDef = ResolveCpuDef(iface);
        TypeReference htRef = module.ImportReference(typeof(System.Collections.Hashtable));
        MethodReference htCtor = module.ImportReference(
            typeof(System.Collections.Hashtable).GetConstructor(Type.EmptyTypes)!);
        MethodReference htGet = module.ImportReference(
            typeof(System.Collections.Hashtable).GetMethod("get_Item", new[] { typeof(object) })!);
        MethodReference htSet = module.ImportReference(
            typeof(System.Collections.Hashtable).GetMethod("set_Item", new[] { typeof(object), typeof(object) })!);
        MethodReference htContains = module.ImportReference(
            typeof(System.Collections.Hashtable).GetMethod("Contains", new[] { typeof(object) })!);
        var type = new TypeDefinition(
            "PaintDotNet.Direct2D1", "CpuDeviceEffect",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        type.Interfaces.Add(new InterfaceImplementation(iface));
        var inputsField = new FieldDefinition("_inputs", FieldAttributes.Private, htRef);
        var keptField = new FieldDefinition("_kept", FieldAttributes.Private, htRef);
        var inputCountField = new FieldDefinition("_inputCount", FieldAttributes.Private, module.TypeSystem.Int32);
        // No disposed flag: ByRefHandle<T>.Dispose releases a borrowed
        // grant on every accessor Set/Get, so a destroyed flag would
        // poison later casts (IsDisposed-sensitive). The bag is managed
        // only (GC reclaims); Dispose is a silent no-op like the color
        // context below.
        type.Fields.Add(inputsField);
        type.Fields.Add(keptField);
        type.Fields.Add(inputCountField);
        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        var ccil = ctor.Body.GetILProcessor();
        ccil.Emit(OpCodes.Ldarg_0);
        ccil.Emit(OpCodes.Call, objectCtor);
        ccil.Emit(OpCodes.Ldarg_0);
        ccil.Emit(OpCodes.Newobj, htCtor);
        ccil.Emit(OpCodes.Stfld, inputsField);
        ccil.Emit(OpCodes.Ldarg_0);
        ccil.Emit(OpCodes.Newobj, htCtor);
        ccil.Emit(OpCodes.Stfld, keptField);
        ccil.Emit(OpCodes.Ret);
        ctor.Body.MaxStackSize = 32;
        type.Methods.Add(ctor);
        foreach (MethodDefinition contract in InterfaceCpuMethods(ifaceDef))
        {
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuTypeLenient(module, contract.ReturnType));
            foreach (var p in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, MapCpuTypeLenient(module, p.ParameterType)));
            var body = impl.Body.GetILProcessor();
            if (contract.Name == "Dispose" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_IsDisposed" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_InputCount" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, inputCountField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "set_InputCount" && contract.Parameters.Count == 1)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Stfld, inputCountField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "IsInputSet" && contract.Parameters.Count == 1)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, inputsField);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Box, module.TypeSystem.Int32);
                body.Emit(OpCodes.Callvirt, htContains);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "GetInput" && contract.Parameters.Count == 1)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, inputsField);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Box, module.TypeSystem.Int32);
                body.Emit(OpCodes.Callvirt, htGet);
                body.Emit(OpCodes.Castclass, MapCpuTypeLenient(module, contract.ReturnType));
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "SetInput" && contract.Parameters.Count == 3)
            {
                // Ownership: the effect retains its inputs (desktop D2D
                // AddRefs through COM). Without this, CreateImpl's tile
                // loop frees the shared bitmap while the processed
                // effect still references it (blank wheel: render ran,
                // upload had a zero handle, only the nub painted).
                // The grant lives in _kept; _inputs keeps the ORIGINAL
                // (GetInput callers may dispose what it returns, so it
                // must stay identity-stable). Grants are ours alone:
                // each pairs with exactly one overwrite-release, so PDN
                // disposing the original can never double-release.
                // Dispose stays a no-op: grants held past the effect's
                // managed death leak one bitmap per effect lifetime
                // (bounded: effects die on resource invalidation).
                // Input retention: composite ctors call SetInput directly,
                // bypassing the EffectInputAccessor hook, so note
                // (this, index) -> image here too; the draw-time walk
                // splices husk notes in place of the husk. Best effort:
                // without the helper the retain logic above still stands.
                try
                {
                    MethodReference noteInput = ImportCpuPaintMethod(module, cpuPaintPath, "NoteEffectInput", 3);
                    body.Emit(OpCodes.Ldarg_0);
                    body.Emit(OpCodes.Ldarg_1);
                    body.Emit(OpCodes.Ldarg_2);
                    body.Emit(OpCodes.Call, noteInput);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
                {
                }
                TypeDefinition orExtDef = ResolveExternalDef(module, "PaintDotNet.ComponentModel", "PaintDotNet.ComponentModel.ObjectRefExtensions");
                MethodDefinition createRefDef = orExtDef.Methods.First(mm => mm.Name == "CreateRef" && !mm.HasGenericParameters && mm.Parameters.Count == 1 && mm.Parameters[0].ParameterType.FullName == "PaintDotNet.ComponentModel.IObjectRef");
                MethodReference createRef = module.ImportReference(createRefDef);
                MethodReference dispDispose = module.ImportReference(typeof(IDisposable).GetMethod("Dispose", Type.EmptyTypes)!);
                TypeReference dispRef = module.ImportReference(typeof(IDisposable));
                var oldLoc = new VariableDefinition(module.TypeSystem.Object);
                var newLoc = new VariableDefinition(module.TypeSystem.Object);
                impl.Body.Variables.Add(oldLoc);
                impl.Body.Variables.Add(newLoc);
                impl.Body.InitLocals = true;
                var noRetain = body.Create(OpCodes.Nop);
                var stored = body.Create(OpCodes.Nop);
                var released = body.Create(OpCodes.Nop);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, keptField);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Box, module.TypeSystem.Int32);
                body.Emit(OpCodes.Callvirt, htGet);
                body.Emit(OpCodes.Stloc, oldLoc);
                body.Emit(OpCodes.Ldarg_2);
                body.Emit(OpCodes.Brfalse, noRetain);
                body.Emit(OpCodes.Ldarg_2);
                body.Emit(OpCodes.Call, createRef);
                body.Emit(OpCodes.Stloc, newLoc);
                body.Emit(OpCodes.Br, stored);
                body.Append(noRetain);
                body.Emit(OpCodes.Ldnull);
                body.Emit(OpCodes.Stloc, newLoc);
                body.Append(stored);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, keptField);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Box, module.TypeSystem.Int32);
                body.Emit(OpCodes.Ldloc, newLoc);
                body.Emit(OpCodes.Callvirt, htSet);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, inputsField);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Box, module.TypeSystem.Int32);
                body.Emit(OpCodes.Ldarg_2);
                body.Emit(OpCodes.Callvirt, htSet);
                body.Emit(OpCodes.Ldloc, oldLoc);
                body.Emit(OpCodes.Isinst, dispRef);
                body.Emit(OpCodes.Brfalse, released);
                body.Emit(OpCodes.Ldloc, oldLoc);
                body.Emit(OpCodes.Isinst, dispRef);
                body.Emit(OpCodes.Callvirt, dispDispose);
                body.Append(released);
                body.Emit(OpCodes.Ret);
                impl.Body.MaxStackSize = System.Math.Max(impl.Body.MaxStackSize, 4);
            }
            else if (contract.Name == "get_PropertyCount" && contract.Parameters.Count == 0)
            {
                // No device-registered properties headless.
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Ret);
            }
            else if ((contract.Name == "TryGetSubProperties" || contract.Name == "TryGetPropertyName")
                && contract.Parameters.Count == 2)
            {
                body.Emit(OpCodes.Ldarg_2);
                body.Emit(OpCodes.Ldnull);
                body.Emit(OpCodes.Stind_Ref);
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Ret);
            }
            else if ((contract.Name == "TryGetPropertyIndex" || contract.Name == "TryGetPropertyType"
                    || contract.Name == "TryGetValueSize")
                && contract.Parameters.Count == 2)
            {
                body.Emit(OpCodes.Ldarg_2);
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Stind_I4);
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.ReturnType.FullName == "System.Void")
            {
                // Value Get/Set and bag writes are inert headless: the
                // CPU stamp paths use classic defaults, never the bag.
                body.Emit(OpCodes.Ret);
            }
            else
            {
                EmitThrowNotSupported(body, module);
            }
            impl.Body.MaxStackSize = 32;
            type.Methods.Add(impl);
        }
        AddCpuObjectRef(type, module);
        module.Types.Add(type);
        return type;
    }

        // CpuTextLayout: the device text layout is pure D2D, but every
    // headless consumer only needs what the provider already carries
    // (Text/FontSize/MaxWidth/MaxHeight): the cache dispatch builds one
    // of these from the provider so button images and the font-size
    // fitter measure without D2D. Metrics reuse the AdjustFontSize CPU
    // formula (w = len*6*fs/7, h = fs); every other member stays loud.
    // CPU sRGB color context: document color contexts are null
    // headless, and ManagedColor.Create validates non-null, which
    // blocks every effect dialog. The default answers Exif sRGB with
    // no profile bytes, so effects run color-managed as identity.
    // Value-type defaults use initobj: no constructor token (which
    // the interpreter rejects on hand-built generic references).
    private static TypeDefinition SynthesizeCpuColorContext(ModuleDefinition module)
    {
        TypeReference iccRef = OwnedCpuRef(module, "PaintDotNet.Imaging.IColorContext");
        TypeDefinition iccDef = ResolveCpuDef(iccRef);
        var type = new TypeDefinition(
            "PaintDotNet.Imaging", "CpuColorContext",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        type.Interfaces.Add(new InterfaceImplementation(iccRef));
        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        var cil = ctor.Body.GetILProcessor();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, objectCtor);
        cil.Emit(OpCodes.Ret);
        ctor.Body.MaxStackSize = 32;
        type.Methods.Add(ctor);
        MethodAttributes implAttrs = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
            | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
        TypeReference DriverClosed(Type openRuntime, TypeReference arg)
        {
            var closed = new GenericInstanceType(module.ImportReference(openRuntime));
            closed.GenericArguments.Add(arg);
            return closed;
        }
        void Mirror(MethodDefinition src, Action<Mono.Cecil.Cil.ILProcessor, MethodDefinition> emit, TypeReference? retOverride = null)
        {
            var impl = new MethodDefinition(src.Name, implAttrs, retOverride ?? MapCpuTypeLenient(module, src.ReturnType));
            foreach (var p in src.Parameters)
                impl.Parameters.Add(new ParameterDefinition(MapCpuTypeLenient(module, p.ParameterType)));
            emit(impl.Body.GetILProcessor(), impl);
            impl.Body.MaxStackSize = 32;
            impl.Body.InitLocals = true;
            type.Methods.Add(impl);
        }
        MethodDefinition Src(string name, int argc) =>
            iccDef.Methods.First(m => m.Name == name && m.Parameters.Count == argc);
        // ColorContextType.ExifColorSpace.
        Mirror(Src("get_Type", 0), (il, md) => { il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ret); });
        // ExifColorSpace.Srgb (ushort).
        Mirror(Src("get_ExifColorSpace", 0), (il, md) => { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Conv_U2); il.Emit(OpCodes.Ret); });
        TypeReference romRet = DriverClosed(typeof(ReadOnlyMemory<>), module.TypeSystem.Byte);
        TypeDefinition bigHashDef = ResolveExternalDef(module,
            "PaintDotNet.Windows", "PaintDotNet.Imaging.ColorProfileBigHash");
        TypeReference hashRet = DriverClosed(typeof(Nullable<>), module.ImportReference(bigHashDef));
        Mirror(Src("GetProfileBytes", 0), (il, md) =>
        {
            var loc = new VariableDefinition(md.ReturnType);
            md.Body.Variables.Add(loc);
            il.Emit(OpCodes.Ldloca_S, loc);
            il.Emit(OpCodes.Initobj, md.ReturnType);
            il.Emit(OpCodes.Ldloc, loc);
            il.Emit(OpCodes.Ret);
        }, romRet);
        Mirror(Src("get_ProfileBytesBigHash", 0), (il, md) =>
        {
            var loc = new VariableDefinition(md.ReturnType);
            md.Body.Variables.Add(loc);
            il.Emit(OpCodes.Ldloca_S, loc);
            il.Emit(OpCodes.Initobj, md.ReturnType);
            il.Emit(OpCodes.Ldloc, loc);
            il.Emit(OpCodes.Ret);
        }, hashRet);
        // Plain members (implicit name/signature matching): no-op
        // Dispose, undisposed, and equality by non-null identity.
        var dispose = new MethodDefinition("Dispose", implAttrs, module.TypeSystem.Void);
        dispose.Body.GetILProcessor().Emit(OpCodes.Ret);
        dispose.Body.MaxStackSize = 32;
        type.Methods.Add(dispose);
        var isDisp = new MethodDefinition("get_IsDisposed", implAttrs, module.TypeSystem.Boolean);
        var isil = isDisp.Body.GetILProcessor();
        isil.Emit(OpCodes.Ldc_I4_0);
        isil.Emit(OpCodes.Ret);
        isDisp.Body.MaxStackSize = 32;
        type.Methods.Add(isDisp);
        var equals = new MethodDefinition("Equals", implAttrs, module.TypeSystem.Boolean);
        equals.Parameters.Add(new ParameterDefinition(iccRef));
        var eqil = equals.Body.GetILProcessor();
        eqil.Emit(OpCodes.Ldarg_1);
        eqil.Emit(OpCodes.Ldnull);
        eqil.Emit(OpCodes.Cgt_Un);
        eqil.Emit(OpCodes.Ret);
        equals.Body.MaxStackSize = 32;
        type.Methods.Add(equals);
        AddCpuObjectRef(type, module);
        module.Types.Add(type);
        return type;
    }
    // Null-document-color-context funnel: the document hands null
    // headless (no WIC factory), and downstream validators reject it.
    // The CPU sRGB context stands in, so effects run color-managed
    // as identity. Emitted head shape (pinned by load tests):
    // ldarg.<slot>, brtrue.s ok, newobj cpuCtor, starg.s param, ok:.
    private static void PrefixNullColorContextDefault(MethodDefinition target, int paramIndex, MethodReference cpuCtor)
    {
        int slot = paramIndex + (target.IsStatic ? 0 : 1);
        var cil = target.Body.GetILProcessor();
        var first = target.Body.Instructions[0];
        Instruction ld = slot == 0 ? cil.Create(OpCodes.Ldarg_0)
            : slot == 1 ? cil.Create(OpCodes.Ldarg_1)
            : slot == 2 ? cil.Create(OpCodes.Ldarg_2)
            : cil.Create(OpCodes.Ldarg_S, target.Parameters[paramIndex]);
        var br = cil.Create(OpCodes.Brtrue_S, first);
        // Branch to the original head when a context is supplied;
        // otherwise fall into the default below. InsertBefore keeps
        // stacking at the head, so build the prefix in reverse.
        var ok = cil.Create(OpCodes.Nop);
        var st = cil.Create(OpCodes.Starg_S, target.Parameters[paramIndex]);
        var nw = cil.Create(OpCodes.Newobj, cpuCtor);
        cil.InsertBefore(first, ld);
        cil.InsertAfter(ld, br);
        cil.InsertAfter(br, nw);
        cil.InsertAfter(nw, st);
        cil.InsertAfter(st, ok);
        br.Operand = ok;
        if (target.Body.MaxStackSize < 8)
            target.Body.MaxStackSize = 8;
    }
    private static void EnsureCpuColorContextDefault(ModuleDefinition module)
    {
        TypeDefinition type = SynthesizeCpuColorContext(module);
        MethodDefinition ctor = type.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 0);
        // ManagedColor.Create(color, context): stand in the CPU sRGB
        // default when the document hands a null context.
        TypeDefinition managed = module.GetType("PaintDotNet.Imaging.ManagedColor")
            ?? throw new InvalidOperationException("CpuPaint: ManagedColor missing");
        MethodDefinition create = managed.Methods.First(m => m.Name == "Create" && m.Parameters.Count == 2
            && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Imaging.IColorContext");
        PrefixNullColorContextDefault(create, 1, ctor);
    }
    // Effect runs hand a null document color context for the same
    // reason: EffectServiceProvider..ctor validates non-null. Same
    // stand-in, synthesized into the effects module.
    private static void EnsureEffectServiceProviderDefault(ModuleDefinition module)
    {
        TypeDefinition svc = module.GetType("PaintDotNet.Effects.EffectServiceProvider")
            ?? throw new InvalidOperationException("CpuPaint: EffectServiceProvider missing");
        TypeDefinition type = module.GetType("PaintDotNet.Imaging.CpuColorContext")
            ?? SynthesizeCpuColorContext(module);
        MethodDefinition cpuCtor = type.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 0);
        MethodDefinition ctor = svc.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 2
            && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Imaging.IColorContext");
        PrefixNullColorContextDefault(ctor, 1, cpuCtor);
    }
    // VerifyOutputPixelFormat validates through WIC's per-format info
    // (PixelFormatExtensions.TryGetInfoNoAddRef -> PixelFormatInfo cache
    // -> ImagingFactory.CreateComponentInfo), whose factory is null
    // headless: every effect init NREs inside a static cctor, so no
    // Adjustments/Effects dialog can ever open. PDN ships a CPU-side
    // twin of that info table in Primitives (PixelFormats.TryGetPixelInfo
    // -> IPixelInfo with the same BitsPerPixel + NumericRepresentation),
    // provably WIC-free because Primitives never references
    // PaintDotNet.Windows. The replacement ports the original switch
    // verbatim onto the CPU table: same accepts, same two throw
    // messages, for every format the CPU renderer can service.
    // (Indexed=1, UnsignedInteger=2, Float=5: PixelFormatNumericRepresentation.)
    private static void EnsureCpuVerifyOutputPixelFormat(ModuleDefinition module)
    {
        TypeDefinition render = module.GetType("PaintDotNet.Effects.ReadOnlyBitmapEffectRenderInfo")
            ?? throw new InvalidOperationException("CpuPaint: ReadOnlyBitmapEffectRenderInfo missing");
        MethodDefinition verify = render.Methods.FirstOrDefault(m => m.Name == "VerifyOutputPixelFormat"
                && m.IsStatic && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.PixelFormat&")
            ?? throw new InvalidOperationException("CpuPaint: VerifyOutputPixelFormat missing");
        TypeDefinition pixelFormats = ResolveExternalDef(module, "PaintDotNet.Primitives", "PaintDotNet.Imaging.PixelFormats");
        MethodDefinition tryInfoDef = pixelFormats.Methods.First(m => m.Name == "TryGetPixelInfo"
            && m.Parameters.Count == 2
            && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Imaging.IPixelInfo&");
        MethodReference tryInfo = module.ImportReference(tryInfoDef);
        TypeDefinition pixelInfo = ResolveExternalDef(module, "PaintDotNet.Primitives", "PaintDotNet.Imaging.IPixelInfo");
        MethodReference getNumeric = module.ImportReference(pixelInfo.Methods.First(m => m.Name == "get_NumericRepresentation"));
        MethodReference getBpp = module.ImportReference(pixelInfo.Methods.First(m => m.Name == "get_BitsPerPixel"));
        TypeDefinition pixelFormat = ResolveExternalDef(module, "PaintDotNet.Primitives", "PaintDotNet.Imaging.PixelFormat");
        MethodReference getName = module.ImportReference(pixelFormat.Methods.First(m => m.Name == "GetName" && m.Parameters.Count == 0));
        TypeDefinition upe = ResolveExternalDef(module, "PaintDotNet.Windows", "PaintDotNet.Imaging.UnsupportedPixelFormatException");
        MethodReference upeCtor = module.ImportReference(upe.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 1
            && m.Parameters[0].ParameterType.FullName == "System.String"));
        MethodReference concat = module.ImportReference(typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) })!);

        verify.Body.ExceptionHandlers.Clear();
        verify.Body.Instructions.Clear();
        verify.Body.Variables.Clear();
        verify.Body.Variables.Add(new VariableDefinition(module.ImportReference(pixelInfo)));
        verify.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
        verify.Body.InitLocals = true;
        var il = verify.Body.GetILProcessor();
        Instruction done = il.Create(OpCodes.Ret);
        Instruction indexed = il.Create(OpCodes.Ldstr, "Indexed pixel formats are not supported");
        Instruction unsupported = il.Create(OpCodes.Ldarg_0);
        Instruction checkBpp = il.Create(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca_S, verify.Body.Variables[0]);
        il.Emit(OpCodes.Call, tryInfo);
        il.Emit(OpCodes.Brfalse, unsupported);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Brfalse, unsupported);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Callvirt, getNumeric);
        il.Emit(OpCodes.Stloc_1);
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, indexed);
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, checkBpp);
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Beq, checkBpp);
        il.Emit(OpCodes.Br, unsupported);
        il.Append(checkBpp);
        il.Emit(OpCodes.Callvirt, getBpp);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Br, unsupported);
        il.Append(indexed);
        il.Emit(OpCodes.Newobj, upeCtor);
        il.Emit(OpCodes.Throw);
        il.Append(unsupported);
        il.Emit(OpCodes.Call, getName);
        il.Emit(OpCodes.Ldstr, " is not a supported pixel format");
        il.Emit(OpCodes.Call, concat);
        il.Emit(OpCodes.Newobj, upeCtor);
        il.Emit(OpCodes.Throw);
        il.Append(done);
        if (verify.Body.MaxStackSize < 8)
            verify.Body.MaxStackSize = 8;
    }
    // TaskbarList.Instance headless: CreateInstance NREs inside the
    // CoCreateInstance deref (caught -> DummyImpl, but every access
    // spams a first-chance), and GetMainWindowHwnd NREs on the stub
    // Process with no guard (escapes TaskProgressDialog.OnShown and
    // aborts every effect run). PDN's own DummyImpl is the designed
    // fallback: answer it directly (same cached shape) and report a
    // null main-window hwnd. No native touch, identical contract.
    private static void EnsureCpuTaskbarDummy(ModuleDefinition module)
    {
        TypeDefinition taskbar = module.GetType("PaintDotNet.Taskbar.TaskbarList")
            ?? throw new InvalidOperationException("CpuPaint: TaskbarList missing");
        TypeDefinition dummy = taskbar.NestedTypes.FirstOrDefault(n => n.Name == "DummyImpl")
            ?? throw new InvalidOperationException("CpuPaint: TaskbarList.DummyImpl missing");
        MethodDefinition dummyCtor = dummy.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 0);
        FieldDefinition instance = taskbar.Fields.First(f => f.Name == "instance" && f.IsStatic);
        MethodDefinition getter = taskbar.Methods.First(m => m.Name == "get_Instance" && m.IsStatic && m.Parameters.Count == 0);
        getter.Body.ExceptionHandlers.Clear();
        getter.Body.Instructions.Clear();
        var gil = getter.Body.GetILProcessor();
        Instruction answer = gil.Create(OpCodes.Ldsfld, instance);
        gil.Emit(OpCodes.Ldsfld, instance);
        gil.Emit(OpCodes.Brtrue, answer);
        gil.Emit(OpCodes.Newobj, dummyCtor);
        gil.Emit(OpCodes.Stsfld, instance);
        gil.Append(answer);
        gil.Emit(OpCodes.Ret);
        if (getter.Body.MaxStackSize < 8)
            getter.Body.MaxStackSize = 8;
        TypeDefinition ext = module.GetType("PaintDotNet.Taskbar.TaskbarListExtensions")
            ?? throw new InvalidOperationException("CpuPaint: TaskbarListExtensions missing");
        MethodDefinition hwnd = ext.Methods.First(m => m.Name == "GetMainWindowHwnd" && m.IsStatic && m.Parameters.Count == 0);
        hwnd.Body.ExceptionHandlers.Clear();
        hwnd.Body.Instructions.Clear();
        var hil = hwnd.Body.GetILProcessor();
        hil.Emit(OpCodes.Ldc_I4_0);
        hil.Emit(OpCodes.Conv_I);
        hil.Emit(OpCodes.Ret);
        if (hwnd.Body.MaxStackSize < 8)
            hwnd.Body.MaxStackSize = 8;
    }
    // Telerik's ThemeResolutionService static init loads embedded icon
    // fonts through a private gdi32 extern
    // (AddFontMemResourceEx: (ptr, uint, ptr, ref uint) -> IntPtr),
    // then reads the *pcFonts out-param into AddMemoryFont. Headless
    // has no GDI, so the stub answers "one font installed" with a null
    // handle and moves the count out-param: the managed
    // PrivateFontCollection path proceeds as on font-full Windows.
    private static void EnsureTelerikFontResourceExStub(ModuleDefinition module)
    {
        TypeDefinition svc = module.GetType("Telerik.WinControls.ThemeResolutionService")
            ?? throw new InvalidOperationException("Telerik: ThemeResolutionService missing.");
        MethodDefinition? ext = svc.Methods.FirstOrDefault(m => m.Name == "AddFontMemResourceEx" && m.IsPInvokeImpl);
        if (ext is null)
        {
            return;
        }

        // Order matters: the PInvokeInfo setter re-arms IsPInvokeImpl
        // even for null (decompiled Cecil 0.11.5), so the attribute
        // clear must come last or the flag (and lazy image re-read)
        // silently restore the extern.
        ext.IsPInvokeImpl = false;
        ext.PInvokeInfo = null;
        ext.Attributes &= ~MethodAttributes.PInvokeImpl;
        ext.Body = new MethodBody(ext);
        ILProcessor il = ext.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ret);
        ext.Body.InitLocals = true;
        ext.Body.MaxStackSize = 8;
    }
    // Browser WASM is single-threaded and Thread.Start is neutered, so
    // Telerik's AnimationEngine.Start handshake (worker signals first,
    // caller WaitOnes) deadlocks: grid right-click wedges in ShowPopup.
    // AllowAnimations=false skips every animation branch (the only
    // AnimationEngine.Start call site is gated by it), so popups show
    // instantly with no threads and no stuck animating input gate. The
    // getter is hardcoded false; the static-ctor init is flipped too in
    // case of direct field reads (all known consumers use the property).
    private static void EnsureTelerikNoAnimations(Mono.Cecil.ModuleDefinition module)
    {
        Mono.Cecil.TypeDefinition svc = module.GetType("Telerik.WinControls.ThemeResolutionService")
            ?? throw new InvalidOperationException("Telerik: ThemeResolutionService missing.");
        Mono.Cecil.MethodDefinition? getter = svc.Methods.FirstOrDefault(m => m.Name == "get_AllowAnimations" && m.IsStatic && m.Parameters.Count == 0);
        if (getter is not null && getter.HasBody)
        {
            getter.Body.Instructions.Clear();
            Mono.Cecil.Cil.ILProcessor gil = getter.Body.GetILProcessor();
            gil.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
            gil.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            getter.Body.InitLocals = true;
            getter.Body.MaxStackSize = 8;
        }
        Mono.Cecil.MethodDefinition? cctor = svc.Methods.FirstOrDefault(m => m.Name == ".cctor" && m.HasBody);
        if (cctor is null)
        {
            return;
        }
        var insns = cctor.Body.Instructions;
        for (int i = 0; i + 1 < insns.Count; i++)
        {
            if (insns[i].OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1
                && insns[i + 1].OpCode == Mono.Cecil.Cil.OpCodes.Stsfld
                && insns[i + 1].Operand is Mono.Cecil.FieldReference fr
                && fr.Name == "allowAnimations")
            {
                insns[i].OpCode = Mono.Cecil.Cil.OpCodes.Ldc_I4_0;
            }
        }
    }
    // Uniscribe (usp10) cannot exist headless: the generic P/Invoke
    // stub no-ops ScriptGetProperties, leaving its table pointer null,
    // and ContainsComplexScript then NREs dereferencing it for every
    // non-empty string. Its only callers are the text measure/draw
    // dispatchers (TextBlock.MeasureRun/DrawRunGDI(Plus),
    // RadTextMeasurer.MeasureText) choosing simple vs shaped text;
    // headless has no shaper, so answer false (simple text).
    private static void EnsureUniscribeHeadlessFalse(ModuleDefinition module)
    {
        TypeDefinition uni = module.GetType("Telerik.WinForms.RichTextEditor.Uniscribe")
            ?? throw new InvalidOperationException("Telerik RTE: Uniscribe missing.");
        MethodDefinition probe = uni.Methods.FirstOrDefault(m => m.Name == "ContainsComplexScript"
            && m.Parameters.Count == 1
            && m.ReturnType.FullName == "System.Boolean"
            && m.HasBody)
            ?? throw new InvalidOperationException("Telerik RTE: ContainsComplexScript missing.");
        probe.Body.ExceptionHandlers.Clear();
        probe.Body.Variables.Clear();
        probe.Body.Instructions.Clear();
        probe.Body.Instructions.Add(probe.Body.GetILProcessor().Create(OpCodes.Ldc_I4_0));
        probe.Body.Instructions.Add(probe.Body.GetILProcessor().Create(OpCodes.Ret));
        probe.Body.InitLocals = true;
        probe.Body.MaxStackSize = 1;
    }
    // GPU effects choose a rendering device through a settings lookup
    // that is null headless (no settings service): every GPU effect
    // dies in get_RenderingGpuChoice. The synthesized setting answers
    // the software choice (Type==3), routing effects down the CPU
    // branch â€” the same path a device-less desktop takes.
    private static void EnsureCpuGpuChoiceSetting(ModuleDefinition module)
    {
        TypeDefinition choiceDef = ResolveExternalDef(module, "PaintDotNet.Framework", "PaintDotNet.Gpu.GpuChoice");
        TypeReference choiceRef = module.ImportReference(choiceDef);
        MethodDefinition choiceCtorDef = choiceDef.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 1
            && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Gpu.GpuChoiceType");
        MethodReference choiceCtor = module.ImportReference(choiceCtorDef);
        TypeDefinition isettingOpen = ResolveExternalDef(module, "PaintDotNet.Core", "PaintDotNet.AppModel.ISetting`1");
        var closed = new GenericInstanceType(module.ImportReference(isettingOpen));
        closed.GenericArguments.Add(choiceRef);
        TypeReference isettingRef = module.ImportReference(
            ResolveExternalDef(module, "PaintDotNet.Core", "PaintDotNet.AppModel.ISetting"));
        TypeReference iinternalRef = module.ImportReference(
            ResolveExternalDef(module, "PaintDotNet.ComponentModel", "PaintDotNet.IInternalImpl"));
        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        MethodReference typeFromHandle = module.ImportReference(
            typeof(Type).GetMethod("GetTypeFromHandle", new[] { typeof(RuntimeTypeHandle) })!);
        var type = module.GetType("PaintDotNet.Gpu.CpuGpuChoiceSetting");
        if (type is null)
        {
            type = new TypeDefinition(
                "PaintDotNet.Gpu", "CpuGpuChoiceSetting",
                TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
                module.TypeSystem.Object);
            type.Interfaces.Add(new InterfaceImplementation(closed));
            type.Interfaces.Add(new InterfaceImplementation(isettingRef));
            type.Interfaces.Add(new InterfaceImplementation(iinternalRef));
            var ctor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            var ccil = ctor.Body.GetILProcessor();
            ccil.Emit(OpCodes.Ldarg_0);
            ccil.Emit(OpCodes.Call, objectCtor);
            ccil.Emit(OpCodes.Ret);
            ctor.Body.MaxStackSize = 32;
            type.Methods.Add(ctor);
            MethodAttributes implAttrs = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
            void SoftwareValue(Mono.Cecil.Cil.ILProcessor il)
            {
                il.Emit(OpCodes.Ldc_I4_3);
                il.Emit(OpCodes.Newobj, choiceCtor);
                il.Emit(OpCodes.Ret);
            }
            void Impl(string name, TypeReference ret, TypeReference[] ps, Action<Mono.Cecil.Cil.ILProcessor> emit)
            {
                var md = new MethodDefinition(name, implAttrs, ret);
                foreach (var p in ps)
                    md.Parameters.Add(new ParameterDefinition(p));
                emit(md.Body.GetILProcessor());
                md.Body.MaxStackSize = 32;
                md.Body.InitLocals = true;
                type.Methods.Add(md);
            }
            TypeReference obj = module.TypeSystem.Object;
            Impl("get_Value", choiceRef, Array.Empty<TypeReference>(), SoftwareValue);
            Impl("set_Value", module.TypeSystem.Void, new[] { choiceRef }, il => il.Emit(OpCodes.Ret));
            Impl("get_DefaultValue", choiceRef, Array.Empty<TypeReference>(), SoftwareValue);
            Impl("get_Value", obj, Array.Empty<TypeReference>(), SoftwareValue);
            Impl("set_Value", module.TypeSystem.Void, new[] { obj }, il => il.Emit(OpCodes.Ret));
            Impl("get_DefaultValue", obj, Array.Empty<TypeReference>(), SoftwareValue);
            Impl("get_Path", module.TypeSystem.String, Array.Empty<TypeReference>(),
                il => { il.Emit(OpCodes.Ldstr, "Graphics/RenderingGpuChoice"); il.Emit(OpCodes.Ret); });
            Impl("get_IsReadOnly", module.TypeSystem.Boolean, Array.Empty<TypeReference>(),
                il => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); });
            Impl("get_ValueType", module.ImportReference(typeof(Type)), Array.Empty<TypeReference>(),
                il => { il.Emit(OpCodes.Ldtoken, choiceRef); il.Emit(OpCodes.Call, typeFromHandle); il.Emit(OpCodes.Ret); });
            Impl("InternalImplementationOnly", module.TypeSystem.Void, Array.Empty<TypeReference>(),
                il => il.Emit(OpCodes.Ret));
            module.Types.Add(type);
        }
        MethodDefinition settingCtor = type.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 0);
        TypeDefinition effect = module.GetType("PaintDotNet.Effects.Gpu.GpuEffect")
            ?? throw new InvalidOperationException("CpuPaint: GpuEffect missing");
        MethodDefinition getter = effect.Methods.First(m => m.Name == "get_RenderingGpuChoice" && m.Parameters.Count == 0);
        getter.Body.ExceptionHandlers.Clear();
        getter.Body.Instructions.Clear();
        var gil = getter.Body.GetILProcessor();
        gil.Emit(OpCodes.Newobj, settingCtor);
        gil.Emit(OpCodes.Ret);
        if (getter.Body.MaxStackSize < 8)
            getter.Body.MaxStackSize = 8;
    }

    // The GPU quality default comes from a settings lookup that is
    // missing headless (the Effects section registers it with a
    // GPU-probing lazy default that never resolves without a device):
    // every GPU effect NREs in OnCreatePropertyCollection dereferencing
    // it (live: RotateZoom, Bulge during Ctrl+Z/menu traffic). Answer the
    // device-less-desktop default (1, range 1..8) — the same value
    // GetDefaultQualityLevelGpu produces for a non-Good GPU. Public so
    // the rewrite tests pin it directly, like the Redirect* rules.
    public static void EnsureGpuQualitySettingFallback(ModuleDefinition module)
    {
        TypeDefinition scalarOpen = ResolveExternalDef(module, "PaintDotNet.Core", "PaintDotNet.AppModel.IScalarAppSetting`1");
        var closed = new GenericInstanceType(module.ImportReference(scalarOpen));
        closed.GenericArguments.Add(module.TypeSystem.Int32);
        TypeReference isettingRef = module.ImportReference(
            ResolveExternalDef(module, "PaintDotNet.Core", "PaintDotNet.AppModel.ISetting"));
        TypeReference iinternalRef = module.ImportReference(
            ResolveExternalDef(module, "PaintDotNet.ComponentModel", "PaintDotNet.IInternalImpl"));
        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        MethodReference typeFromHandle = module.ImportReference(
            typeof(Type).GetMethod("GetTypeFromHandle", new[] { typeof(RuntimeTypeHandle) })!);
        var type = module.GetType("PaintDotNet.Gpu.CpuGpuQualitySetting");
        if (type is null)
        {
            type = new TypeDefinition(
                "PaintDotNet.Gpu", "CpuGpuQualitySetting",
                TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
                module.TypeSystem.Object);
            type.Interfaces.Add(new InterfaceImplementation(closed));
            type.Interfaces.Add(new InterfaceImplementation(isettingRef));
            type.Interfaces.Add(new InterfaceImplementation(iinternalRef));
            var ctor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            var ccil = ctor.Body.GetILProcessor();
            ccil.Emit(OpCodes.Ldarg_0);
            ccil.Emit(OpCodes.Call, objectCtor);
            ccil.Emit(OpCodes.Ret);
            ctor.Body.MaxStackSize = 32;
            type.Methods.Add(ctor);
            MethodAttributes implAttrs = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
            void LdcInt(Mono.Cecil.Cil.ILProcessor il, int value)
            {
                switch (value)
                {
                    case 0: il.Emit(OpCodes.Ldc_I4_0); break;
                    case 1: il.Emit(OpCodes.Ldc_I4_1); break;
                    case 8: il.Emit(OpCodes.Ldc_I4_8); break;
                    default: il.Emit(OpCodes.Ldc_I4, value); break;
                }
            }
            TypeReference intRef = module.TypeSystem.Int32;
            TypeReference obj = module.TypeSystem.Object;
            void Impl(string name, TypeReference ret, TypeReference[] ps, Action<Mono.Cecil.Cil.ILProcessor> emit)
            {
                var md = new MethodDefinition(name, implAttrs, ret);
                foreach (var p in ps)
                    md.Parameters.Add(new ParameterDefinition(p));
                emit(md.Body.GetILProcessor());
                md.Body.MaxStackSize = 32;
                md.Body.InitLocals = true;
                type.Methods.Add(md);
            }
            Impl("get_Value", intRef, Array.Empty<TypeReference>(), il => { LdcInt(il, 1); il.Emit(OpCodes.Ret); });
            Impl("set_Value", module.TypeSystem.Void, new[] { intRef }, il => il.Emit(OpCodes.Ret));
            Impl("get_DefaultValue", intRef, Array.Empty<TypeReference>(), il => { LdcInt(il, 1); il.Emit(OpCodes.Ret); });
            Impl("get_MinValue", intRef, Array.Empty<TypeReference>(), il => { LdcInt(il, 1); il.Emit(OpCodes.Ret); });
            Impl("get_MaxValue", intRef, Array.Empty<TypeReference>(), il => { LdcInt(il, 8); il.Emit(OpCodes.Ret); });
            Impl("get_Value", obj, Array.Empty<TypeReference>(), il => { LdcInt(il, 1); il.Emit(OpCodes.Box, intRef); il.Emit(OpCodes.Ret); });
            Impl("set_Value", module.TypeSystem.Void, new[] { obj }, il => il.Emit(OpCodes.Ret));
            Impl("get_DefaultValue", obj, Array.Empty<TypeReference>(), il => { LdcInt(il, 1); il.Emit(OpCodes.Box, intRef); il.Emit(OpCodes.Ret); });
            Impl("get_Path", module.TypeSystem.String, Array.Empty<TypeReference>(),
                il => { il.Emit(OpCodes.Ldstr, "Effects/DefaultQualityLevelGpu"); il.Emit(OpCodes.Ret); });
            Impl("get_IsReadOnly", module.TypeSystem.Boolean, Array.Empty<TypeReference>(),
                il => { LdcInt(il, 0); il.Emit(OpCodes.Ret); });
            Impl("get_ValueType", module.ImportReference(typeof(Type)), Array.Empty<TypeReference>(),
                il => { il.Emit(OpCodes.Ldtoken, intRef); il.Emit(OpCodes.Call, typeFromHandle); il.Emit(OpCodes.Ret); });
            Impl("InternalImplementationOnly", module.TypeSystem.Void, Array.Empty<TypeReference>(),
                il => il.Emit(OpCodes.Ret));
            module.Types.Add(type);
        }
        MethodDefinition settingCtor = type.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 0);
        TypeDefinition effect = module.GetType("PaintDotNet.Effects.Gpu.GpuEffect")
            ?? throw new InvalidOperationException("CpuPaint: GpuEffect missing");
        MethodDefinition getter = effect.Methods.FirstOrDefault(m => m.Name == "get_DefaultQualityLevelGpu" && m.Parameters.Count == 0 && m.HasBody)
            ?? throw new InvalidOperationException("CpuPaint: GpuEffect quality getter missing");
        getter.Body.ExceptionHandlers.Clear();
        getter.Body.Instructions.Clear();
        var gil = getter.Body.GetILProcessor();
        gil.Emit(OpCodes.Newobj, settingCtor);
        gil.Emit(OpCodes.Ret);
        if (getter.Body.MaxStackSize < 8)
            getter.Body.MaxStackSize = 8;
    }


    // WIC imaging shortcuts: the factory instance is null headless,
    // so every terminal clip/transform answers its source bitmap.
    // The answer is a COUNTED ref (CreateRef): callers own the
    // returned clip and dispose it (CreateImpl tile loop, using
    // blocks, RefHandles). A bare alias lets one Dispose pool-return
    // a live bitmap (source-kill) or free a wrapper its own pipeline
    // still reads (use-after-free NRE in ReinterpretedBitmapSource).
    // Color work is identity without a device. Only exact-signature
    // matches are touched; anything else keeps its body (and its
    // exception).
    private static void EnsureCpuImagingShortcuts(ModuleDefinition module)
    {
        TypeDefinition Bse(string asm, string type) => ResolveExternalDef(module, asm, type);
        // Lazy: fixtures renamed to a PDN assembly name (Friends test)
        // carry no ComponentModel reference, and they never reach a
        // matching method, so resolve only on first actual use.
        Mono.Cecil.MethodReference? openRefImp = null;
        void Identity(MethodDefinition m, int sourceIndex)
        {
            m.Body.ExceptionHandlers.Clear();
            m.Body.Instructions.Clear();
            if (openRefImp is null)
            {
                TypeDefinition orExt = ResolveExternalDef(module, "PaintDotNet.ComponentModel", "PaintDotNet.ComponentModel.ObjectRefExtensions");
                MethodDefinition openRef = orExt.Methods.First(mm => mm.Name == "CreateRef" && mm.HasGenericParameters && mm.Parameters.Count == 1);
                openRefImp = module.ImportReference(openRef);
            }
            var gim = new Mono.Cecil.GenericInstanceMethod(openRefImp);
            gim.GenericArguments.Add(module.ImportReference(m.ReturnType));
            var il = m.Body.GetILProcessor();
            il.Emit(sourceIndex == 0 ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, gim);
            il.Emit(OpCodes.Ret);
            if (m.Body.MaxStackSize < 8)
                m.Body.MaxStackSize = 8;
        }
        bool Sig(MethodDefinition m, string p0, int argc) =>
            m.Parameters.Count == argc && m.Parameters[0].ParameterType.FullName == p0;
        TypeDefinition bse = module.GetType("PaintDotNet.Imaging.BitmapSourceExtensions") ?? throw new InvalidOperationException("CpuPaint: BitmapSourceExtensions missing");
        foreach (var m in bse.Methods)
        {
            if (m.Name == "CreateClipper" && Sig(m, "PaintDotNet.Imaging.IBitmapSource", 2))
                Identity(m, 0);
            if (m.Name == "CreateColorTransformer" && Sig(m, "PaintDotNet.Imaging.IBitmapSource", 4))
                Identity(m, 0);
        }
        TypeDefinition ife = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions") ?? throw new InvalidOperationException("CpuPaint: ImagingFactoryExtensions missing");
        foreach (var m in ife.Methods)
        {
            if (m.Name == "CreateBitmapClipper"
                && m.Parameters.Count == 3
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IImagingFactory")
                Identity(m, 1);
            if (m.Name == "CreateColorTransformedBitmap"
                && m.Parameters.Count == 5
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IImagingFactory")
                Identity(m, 1);
            if (m.Name == "CreateColorTransformedBitmap"
                && m.Parameters.Count == 4
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IImagingFactory"
                && m.Parameters[2].ParameterType.FullName == "PaintDotNet.Imaging.IColorContext")
                Identity(m, 1);
            if (m.Name == "CreateColorTransformedBitmap"
                && m.Parameters.Count == 4
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IImagingFactory"
                && m.Parameters[2].ParameterType.FullName == "PaintDotNet.Imaging.IColorProfile")
                Identity(m, 1);
        }
    }
    // PaintDotNet's environment-inventory diagnostics (DiagnosticsGenerator.
    // GetFormattedText) probes GPU/package/service state that faults headless;
    // the blocking .Result then wraps the ORIGINAL error in an
    // AggregateException, killing the error dialog the text was meant to
    // fill (Save-As failure invisibly fatal). Route every caller (error
    // dialog, diagnostics page, clipboard copy, effect errors) to the
    // browser-safe fallback: the original error text is preserved, only
    // the environment section degrades explicitly. Public so the rewrite
    // tests pin it directly, like the Redirect* rules.
    public static void RedirectPdnDiagnosticsFallback(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.AssemblyNameReference? drawingScope = module.AssemblyReferences
            .FirstOrDefault(r => r.Name == "WfWasm.Drawing");
        if (drawingScope is null)
        {
            drawingScope = new Mono.Cecil.AssemblyNameReference("WfWasm.Drawing", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(drawingScope);
        }
        var diagType = new Mono.Cecil.TypeReference(
            "WfWasm.Drawing", "PdnDiagnostics", module, drawingScope);
        var fallback = new Mono.Cecil.MethodReference("GetErrorDetailsFallback",
            module.TypeSystem.String, diagType)
        {
            HasThis = false,
            Parameters =
            {
                new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object),
                new Mono.Cecil.ParameterDefinition(module.TypeSystem.Boolean),
            }
        };
        Mono.Cecil.TypeDefinition diag = module.GetType("PaintDotNet.Diagnostics.DiagnosticsGenerator")
            ?? throw new InvalidOperationException("PdnDiag: DiagnosticsGenerator missing");
        bool any = false;
        foreach (Mono.Cecil.MethodDefinition m in diag.Methods)
        {
            if (m.Name != "GetFormattedText" || !m.HasBody)
                continue;
            if (m.Parameters.Count != 2
                || m.Parameters[1].ParameterType.FullName != "System.Boolean"
                || m.ReturnType.FullName != "System.String")
                continue;
            m.Body.ExceptionHandlers.Clear();
            m.Body.Variables.Clear();
            m.Body.Instructions.Clear();
            var il = m.Body.GetILProcessor();
            // AppSettings derives to object verifiably; the bool passes through.
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
            il.Emit(Mono.Cecil.Cil.OpCodes.Call, fallback);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            if (m.Body.MaxStackSize < 8)
                m.Body.MaxStackSize = 8;
            any = true;
        }
        if (!any)
            throw new InvalidOperationException("PdnDiag: GetFormattedText(AppSettings, bool) missing");
    }
    // Built-in FileType .cctors (Bmp/Gif/Jpeg/Png/Tiff) query WIC for their
    // extension lists; headless the query dies and every cctor with it
    // (Save-As dies in DoSaveAsDialog via a null DefaultSaveExtension).
    // Answer desktop-exact literal tables at the two WicHelpers call sites
    // of each cctor, keeping everything else byte-identical (ToFrozen,
    // the JPEG .jpg-to-front tweak, field stores). Extension strings read
    // off desktop encoders 2026-10-19; JPEG lists keep WIC order so the
    // cctor's own Remove+Insert(0) still fronts .jpg exactly like desktop.
    // Only in-module callees remain (FileTypeOptions setters); the argument
    // chains (ContainerFormats/VendorIDs) are removed with the calls.
    // Public so the rewrite tests pin it directly, like the Redirect* rules.
    public static void RedirectFileTypeCctorTables(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        (string Type, string[] Load, string[] Save)[] rows = new (string, string[], string[])[]
        {
            ("PaintDotNet.Data.BmpFileType",
                new[] { ".bmp", ".dib", ".rle" }, new[] { ".bmp", ".dib", ".rle" }),
            ("PaintDotNet.Data.GifFileType",
                new[] { ".gif" }, new[] { ".gif" }),
            ("PaintDotNet.Data.JpegFileType",
                new[] { ".jpeg", ".jpe", ".jpg", ".jfif", ".exif" },
                new[] { ".jpeg", ".jpe", ".jpg", ".jfif", ".exif" }),
            ("PaintDotNet.Data.PngFileType",
                new[] { ".png" }, new[] { ".png" }),
            ("PaintDotNet.Data.TiffFileType",
                new[] { ".tiff", ".tif" }, new[] { ".tiff", ".tif" }),
        };
        bool any = false;
        foreach (var row in rows)
        {
            // Skip absent types (PDN version skew): the zero-match throw
            // below still fails loudly when the rule fires nowhere.
            Mono.Cecil.TypeDefinition? t = module.GetType(row.Type);
            if (t is null)
                continue;
            Mono.Cecil.MethodDefinition? cctor = null;
            foreach (var m in t.Methods)
                if (m.Name == ".cctor" && m.HasBody) { cctor = m; break; }
            if (cctor is null)
                throw new InvalidOperationException("CpuPaint: " + row.Type + ".cctor missing");
            // Save site first so the earlier load index stays valid.
            EmitCctorTable(cctor, row.Type, "GetSaveExtensions", row.Save);
            EmitCctorTable(cctor, row.Type, "GetLoadExtensions", row.Load);
            any = true;
        }
        if (!any)
            throw new InvalidOperationException("CpuPaint: no FileType cctor matched");
    }

    // FileType.GetLastSaveConfigToken reads the persisted per-filetype
    // token (BinaryFormatter over the user settings store) and throws
    // InvalidOperationException headless when the settings store is
    // unavailable -- Save-As dies in set_FileType before the dialog shows.
    // Answer the default token instead: CreateDefaultSaveConfigToken is a
    // non-virtual helper that dispatches to each type's virtual
    // OnCreateDefaultSaveConfigToken, so per-type defaults are preserved
    // exactly (verified: no overrides/shadows of the helper in PDN).
    // Only the "remember my last save settings" persistence degrades.
    // Public so the rewrite tests pin it directly, like the Redirect* rules.
    public static void RedirectLastSaveConfigToken(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition ft = module.GetType("PaintDotNet.FileType")
            ?? throw new InvalidOperationException("SaveToken: PaintDotNet.FileType missing");
        Mono.Cecil.MethodDefinition? target = null;
        Mono.Cecil.MethodDefinition? factory = null;
        foreach (Mono.Cecil.MethodDefinition m in ft.Methods)
        {
            if (m.Name == "GetLastSaveConfigToken" && m.HasBody && m.Parameters.Count == 0
                && m.ReturnType.FullName == "PaintDotNet.SaveConfigToken")
                target = m;
            if (m.Name == "CreateDefaultSaveConfigToken" && m.Parameters.Count == 0
                && !m.IsStatic && m.ReturnType.FullName == "PaintDotNet.SaveConfigToken")
                factory = m;
        }
        if (target is null)
            throw new InvalidOperationException("SaveToken: GetLastSaveConfigToken() missing");
        if (factory is null)
            throw new InvalidOperationException("SaveToken: CreateDefaultSaveConfigToken() missing");
        target.Body.ExceptionHandlers.Clear();
        target.Body.Variables.Clear();
        target.Body.Instructions.Clear();
        var il = target.Body.GetILProcessor();
        // Mirrors the blocked-plugin branch of the original body.
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, factory);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        if (target.Body.MaxStackSize < 8)
            target.Body.MaxStackSize = 8;
    }

    // PaintDotNet.SystemLayer.ShellUtil.GetVirtualPath answers shell
    // known folders via SHGetFolderPath: headless it answers "" (no
    // throw), so every consumer degrades to the empty path -- Save-As
    // dies in GetDefaultSavePath, app-data/cache/crash dirs vanish.
    // Answer the fork's /vfs layout instead (cf. WasmEnvironment), honoring
    // the create flag with an idempotent guarded CreateDirectory.
    // Only mscorlib callees remain, so no headless member resolution.
    // Public so the rewrite tests pin it directly, like the Redirect* rules.
    public static void RedirectShellVirtualPaths(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition util = module.GetType("PaintDotNet.SystemLayer.ShellUtil")
            ?? throw new InvalidOperationException("ShellVfs: ShellUtil missing");
        Mono.Cecil.MethodDefinition? target = null;
        foreach (Mono.Cecil.MethodDefinition m in util.Methods)
        {
            if (m.Name == "GetVirtualPath" && m.HasBody && m.Parameters.Count == 2
                && m.Parameters[1].ParameterType.FullName == "System.Boolean"
                && m.ReturnType.FullName == "System.String")
                target = m;
        }
        if (target is null)
            throw new InvalidOperationException("ShellVfs: GetVirtualPath(VirtualFolderName, bool) missing");
        var createDir = module.ImportReference(typeof(System.IO.Directory).GetMethod(
            "CreateDirectory", new[] { typeof(string) })!);
        var argEx = module.ImportReference(typeof(ArgumentOutOfRangeException).GetConstructor(
            new[] { typeof(string) })!);
        var exType = module.ImportReference(typeof(Exception));
        target.Body.ExceptionHandlers.Clear();
        target.Body.Variables.Clear();
        target.Body.Instructions.Clear();
        target.Body.Variables.Add(new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.String));
        target.Body.InitLocals = true;
        var il = target.Body.GetILProcessor();
        // Index mirrors PaintDotNet.SystemLayer.VirtualFolderName.
        string[] paths = new[]
        {
            "/vfs/desktop", "/vfs/documents", "/vfs/pictures",
            "/vfs/local-app-data", "/vfs/app-data", "/vfs/program-files",
            "/vfs/windows/Fonts",
        };
        var doCreate = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
        var ret = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
        for (int i = 0; i < paths.Length; i++)
        {
            var next = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, i);
            il.Emit(Mono.Cecil.Cil.OpCodes.Bne_Un, next);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, paths[i]);
            il.Emit(Mono.Cecil.Cil.OpCodes.Stloc_0);
            il.Emit(Mono.Cecil.Cil.OpCodes.Br, doCreate);
            il.Append(next);
        }
        // Unknown folder (unreachable from PDN: 0..6 only).
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, "folderName");
        il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, argEx);
        il.Emit(Mono.Cecil.Cil.OpCodes.Throw);
        il.Append(doCreate);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
        il.Emit(Mono.Cecil.Cil.OpCodes.Brfalse, ret);
        var tryStart = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
        il.Append(tryStart);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, createDir);
        il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
        il.Emit(Mono.Cecil.Cil.OpCodes.Leave, ret);
        var handlerStart = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
        il.Append(handlerStart);
        il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
        il.Emit(Mono.Cecil.Cil.OpCodes.Leave, ret);
        il.Append(ret);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        target.Body.ExceptionHandlers.Add(new Mono.Cecil.Cil.ExceptionHandler(
            Mono.Cecil.Cil.ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = ret,
            CatchType = exType,
        });
        if (target.Body.MaxStackSize < 8)
            target.Body.MaxStackSize = 8;
    }

    // PdnCheckBox measure/layout/show all run through DWrite text layout,
    // which is null headless: every checkbox measure throws, the throw is
    // caught for layout but aborts WM_SHOWWINDOW delivery, so config
    // dialogs never finish showing. Measure on the CPU instead: the box
    // width comes from the pure-for-measure in-module classic path and the
    // text from TextRenderer.MeasureText (the fork CPU-measures headless;
    // PdnNumericSpin already measures this way with no faults), with the
    // original 3-DIP gap and padding math. Member refs are reused from the
    // module (no new scopes); anything missing fails loudly. Draw paths
    // are untouched (still fault-and-blank) -- this unblocks measure only.
    // Public so the rewrite tests pin it directly, like the Redirect* rules.
    // Headless prefetches can never overlap (single thread, no pump), so
    // queueing a chunk-read continuation only defers it to a .Result that
    // blocks forever (PNSE ObjWait in TryGetNextChunkWhileLocked). With
    // ExecuteSynchronously an already-complete antecedent runs the
    // continuation inline: chained sequential reads always have a complete
    // antecedent (the chain starts from a pre-completed TCS task), so by
    // induction every prefetch executes synchronously at Prefetch time and
    // the queued task is already complete when .Result takes it. Same code,
    // same order, same locks (re-entrant on this thread); desktop keeps
    // the one-arg overload and its pool overlap. Idempotent like the Wait
    // rule above: reruns find two-arg sites and patch nothing.
    public static int NeutralizeSessionPrefetchInline(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition? read = null;
        foreach (Mono.Cecil.TypeDefinition t in module.GetTypes())
        {
            if (t.FullName == "PaintDotNet.AppModel.WriteOnceSessionFile/ChunkedReadStream")
            {
                read = t;
                break;
            }
        }
        if (read is null)
            throw new InvalidOperationException("PDNUndoInline: ChunkedReadStream missing");
        Mono.Cecil.MethodDefinition? prefetch = null;
        foreach (Mono.Cecil.MethodDefinition m in read.Methods)
        {
            if (m.Name == "PrefetchNextChunkImpl" && m.HasBody && m.Parameters.Count == 0
                && m.ReturnType.FullName == "System.Void")
            {
                prefetch = m;
                break;
            }
        }
        if (prefetch is null)
            throw new InvalidOperationException("PDNUndoInline: PrefetchNextChunkImpl missing");
        int patched = 0;
        foreach (var ins in prefetch.Body.Instructions.ToList())
        {
            if ((ins.OpCode != Mono.Cecil.Cil.OpCodes.Call && ins.OpCode != Mono.Cecil.Cil.OpCodes.Callvirt)
                || ins.Operand is not Mono.Cecil.GenericInstanceMethod gim
                || gim.Name != "ContinueWith" || gim.Parameters.Count != 1
                || !gim.DeclaringType.FullName.StartsWith("System.Threading.Tasks.Task`1", StringComparison.Ordinal))
            {
                continue;
            }
            Mono.Cecil.TypeDefinition? taskDef;
            try { taskDef = gim.DeclaringType.Resolve(); }
            catch { taskDef = null; }
            Mono.Cecil.MethodDefinition? two = null;
            if (taskDef is not null)
            {
                foreach (Mono.Cecil.MethodDefinition m in taskDef.Methods)
                {
                    if (m.Name == "ContinueWith" && m.Parameters.Count == 2
                        && m.GenericParameters.Count == gim.GenericArguments.Count
                        && m.Parameters[1].ParameterType.FullName == "System.Threading.Tasks.TaskContinuationOptions")
                    {
                        two = m;
                        break;
                    }
                }
            }
            if (two is null)
                throw new InvalidOperationException("PDNUndoInline: two-arg ContinueWith overload missing");
            // The replacement must keep BOTH instantiations: the method's
            // own generic args AND the declaring Task<T> instantiation.
            // An open Task<T> declarer dies in the interpreter ("containing
            // type is not fully instantiated") when the menu shortcut
            // reaches the read path. Derived from the existing coherent
            // reference (name/return/declarer/args copied; generic slots
            // bind positionally, so only the non-generic options parameter
            // is fresh): never by mutating an ImportReference (same-module
            // imports alias the definition and corrupt the fixture, and the
            // writer may drop a patched-in declarer).
            if (gim.DeclaringType is not Mono.Cecil.GenericInstanceType)
                throw new InvalidOperationException("PDNUndoInline: ContinueWith declarer is not instantiated");
            var elem = new Mono.Cecil.MethodReference(gim.Name, gim.ReturnType, gim.DeclaringType);
            elem.HasThis = true;
            elem.ExplicitThis = gim.ExplicitThis;
            elem.GenericParameters.Add(new Mono.Cecil.GenericParameter("TNew", elem));
            foreach (var p in gim.Parameters)
                elem.Parameters.Add(p);
            elem.Parameters.Add(new Mono.Cecil.ParameterDefinition(
                module.ImportReference(two.Parameters[1].ParameterType)));
            var ngim = new Mono.Cecil.GenericInstanceMethod(elem);
            foreach (var ga in gim.GenericArguments)
                ngim.GenericArguments.Add(ga);
            var il = prefetch.Body.GetILProcessor();
            il.InsertBefore(ins, il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, 524288));
            ins.Operand = ngim;
            patched++;
        }
        if (patched > 2)
            throw new InvalidOperationException("PDNUndoInline: expected at most 2 ContinueWith sites, found " + patched);
        if (patched > 0)
        {
            prefetch.Body.MaxStackSize++;
            Console.WriteLine("PDNUndoInline: " + patched + " prefetch continuation(s) run inline");
        }
        return patched;
    }
    // Paint.NET undo waits on session prefetch tasks that can never
    // complete headless (single thread, no pump): ChunkedReadStream.Dispose
    // drains nextChunkTaskQueue with a blocking Task.Wait per task, so
    // every history-undo died with PNSE ObjWait ("Cannot wait on monitors
    // on this runtime"). The queued prefetches are abandoned read-aheads
    // (nobody consumes them after Dispose); dropping them without waiting
    // keeps the drain shape with the identical stack effect (pop swaps the
    // void Wait call in place, no branch or handler moves) and frees every
    // resource exactly as before. Abandoned tasks never run headless, so
    // nothing races the freed buffers. Desktop bytes are untouched.
    // Idempotent: a rerun over already-patched bytes finds no Wait site
    // and returns 0 (pipeline reruns are routine); a missing type/method
    // still throws, as do multiple sites.
    public static int NeutralizeSessionPrefetchWait(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition? read = null;
        foreach (Mono.Cecil.TypeDefinition t in module.GetTypes())
        {
            if (t.FullName == "PaintDotNet.AppModel.WriteOnceSessionFile/ChunkedReadStream")
            {
                read = t;
                break;
            }
        }
        if (read is null)
            throw new InvalidOperationException("PDNUndo: ChunkedReadStream missing");
        Mono.Cecil.MethodDefinition? dispose = null;
        foreach (Mono.Cecil.MethodDefinition m in read.Methods)
        {
            if (m.Name == "Dispose" && m.HasBody && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "System.Boolean")
            {
                dispose = m;
                break;
            }
        }
        if (dispose is null)
            throw new InvalidOperationException("PDNUndo: ChunkedReadStream.Dispose(Boolean) missing");
        int patched = 0;
        foreach (var ins in dispose.Body.Instructions)
        {
            if ((ins.OpCode == Mono.Cecil.Cil.OpCodes.Call || ins.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
                && ins.Operand is Mono.Cecil.MethodReference mr
                && mr.DeclaringType.FullName == "System.Threading.Tasks.Task"
                && mr.Name == "Wait" && mr.Parameters.Count == 0)
            {
                ins.OpCode = Mono.Cecil.Cil.OpCodes.Pop;
                ins.Operand = null;
                patched++;
            }
        }
        if (patched > 1)
            throw new InvalidOperationException("PDNUndo: expected 1 Task.Wait site, found " + patched);
        if (patched == 1)
            Console.WriteLine("PDNUndo: dropped 1 prefetch Wait in ChunkedReadStream.Dispose");
        return patched;
    }
    public static void RedirectPdnCheckBoxMeasure(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition box = module.GetType("PaintDotNet.Controls.PdnCheckBox")
            ?? throw new InvalidOperationException("PdnBox: PdnCheckBox missing");
        Mono.Cecil.MethodDefinition? target = null;
        Mono.Cecil.MethodDefinition? classic = null;
        foreach (Mono.Cecil.MethodDefinition m in box.Methods)
        {
            if (m.Name == "GetPreferredSize" && m.HasBody && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "System.Drawing.Size"
                && m.ReturnType.FullName == "System.Drawing.Size")
                target = m;
            if (m.Name == "MeasureAndDrawClassicBox" && m.Parameters.Count == 3)
                classic = m;
        }
        if (target is null)
            throw new InvalidOperationException("PdnBox: GetPreferredSize(Size) missing");
        if (classic is null)
            throw new InvalidOperationException("PdnBox: MeasureAndDrawClassicBox missing");
        Mono.Cecil.MethodReference Req(string type, string name, params string[] ps)
        {
            foreach (Mono.Cecil.TypeDefinition t in AllTypes(module))
            {
                foreach (Mono.Cecil.MethodDefinition m in t.Methods)
                {
                    if (!m.HasBody) continue;
                    foreach (var i in m.Body.Instructions)
                    {
                        if (i.Operand is Mono.Cecil.MethodReference r
                            && r.DeclaringType.FullName == type && r.Name == name
                            && r.Parameters.Count == ps.Length)
                        {
                            bool ok = true;
                            for (int k = 0; k < ps.Length; k++)
                                if (r.Parameters[k].ParameterType.FullName != ps[k]) { ok = false; break; }
                            if (ok) return r;
                        }
                    }
                }
            }
            throw new InvalidOperationException("PdnBox: " + type + "::" + name + " missing");
        }
        var createGraphics = Req("System.Windows.Forms.Control", "CreateGraphics");
        var getText = Req("System.Windows.Forms.Control", "get_Text");
        var getFont = Req("System.Windows.Forms.Control", "get_Font");
        var measureText = Req("System.Windows.Forms.TextRenderer", "MeasureText", "System.String", "System.Drawing.Font");
        var convertDips = Req("PaintDotNet.UIScaleFactor", "ConvertDipsToPixelsInt", "System.Int32");
        var getCurrentScale = Req("PaintDotNet.UIScaleFactor", "get_Current");
        var sizeGetWidth = Req("System.Drawing.Size", "get_Width");
        var sizeGetHeight = Req("System.Drawing.Size", "get_Height");
        var sizeCtor = Req("System.Drawing.Size", ".ctor", "System.Int32", "System.Int32");
        var getPadding = Req("System.Windows.Forms.Control", "get_Padding");
        var padH = Req("System.Windows.Forms.Padding", "get_Horizontal");
        var padV = Req("System.Windows.Forms.Padding", "get_Vertical");
        var dispose = Req("System.IDisposable", "Dispose");
        target.Body.ExceptionHandlers.Clear();
        target.Body.Variables.Clear();
        target.Body.Instructions.Clear();
        var gfxType = createGraphics.ReturnType;
        var sizeType = sizeCtor.DeclaringType;
        var padType = getPadding.ReturnType;
        target.Body.Variables.Add(new Mono.Cecil.Cil.VariableDefinition(gfxType));
        target.Body.Variables.Add(new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Int32));
        target.Body.Variables.Add(new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Int32));
        target.Body.Variables.Add(new Mono.Cecil.Cil.VariableDefinition(sizeType));
        target.Body.Variables.Add(new Mono.Cecil.Cil.VariableDefinition(padType));
        target.Body.Variables.Add(new Mono.Cecil.Cil.VariableDefinition(sizeType));
        target.Body.InitLocals = true;
        var il = target.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, createGraphics);
        il.Emit(Mono.Cecil.Cil.OpCodes.Stloc_0);
        var tryStart = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
        il.Append(tryStart);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, classic);
        il.Emit(Mono.Cecil.Cil.OpCodes.Stloc_1);
        // ConvertDipsToPixelsInt is an instance method (like the original
        // classic-box body): the instance comes from the static Current.
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, getCurrentScale);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_3);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, convertDips);
        il.Emit(Mono.Cecil.Cil.OpCodes.Stloc_2);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, getText);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, getFont);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, measureText);
        il.Emit(Mono.Cecil.Cil.OpCodes.Stloc_3);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc_1);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc_2);
        il.Emit(Mono.Cecil.Cil.OpCodes.Add);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, target.Body.Variables[3]);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, sizeGetWidth);
        il.Emit(Mono.Cecil.Cil.OpCodes.Add);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, getPadding);
        il.Emit(Mono.Cecil.Cil.OpCodes.Stloc_S, target.Body.Variables[4]);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, target.Body.Variables[4]);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, padH);
        il.Emit(Mono.Cecil.Cil.OpCodes.Add);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, target.Body.Variables[3]);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, sizeGetHeight);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, getPadding);
        il.Emit(Mono.Cecil.Cil.OpCodes.Stloc_S, target.Body.Variables[4]);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, target.Body.Variables[4]);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, padV);
        il.Emit(Mono.Cecil.Cil.OpCodes.Add);
        il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, sizeCtor);
        il.Emit(Mono.Cecil.Cil.OpCodes.Stloc_S, target.Body.Variables[5]);
        var ok = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
        il.Emit(Mono.Cecil.Cil.OpCodes.Leave, ok);
        var handlerStart = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
        il.Append(handlerStart);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc_0);
        var noDispose = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
        il.Emit(Mono.Cecil.Cil.OpCodes.Brfalse_S, noDispose);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, dispose);
        il.Append(noDispose);
        il.Emit(Mono.Cecil.Cil.OpCodes.Endfinally);
        il.Append(ok);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc_S, target.Body.Variables[5]);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        target.Body.ExceptionHandlers.Add(new Mono.Cecil.Cil.ExceptionHandler(
            Mono.Cecil.Cil.ExceptionHandlerType.Finally)
        {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = ok,
        });
        if (target.Body.MaxStackSize < 8)
            target.Body.MaxStackSize = 8;
    }

    // Syncfusion SfForm checks the Win11 build number from
    // HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\CurrentBuild in
    // its constructor; headless the value is missing and int.Parse(null)
    // throws before any Syncfusion demo form exists. Answer false: no
    // DWM/Mica exists headless, so classic chrome is the honest path
    // (and the only one our CPU paint supports). 46-byte body replaced
    // with ldc.i4.0 + ret; no callees remain.
    // Public so the rewrite tests pin it directly, like the Redirect* rules.
    // RichEdit OLE callback needs COM (absent headless): UpdateOleCallback
    // dies in ComHelpers.GetComPointer with a runtime PNS during every
    // RichTextBox handle creation (Syncfusion calendar/diagram boot), and the
    // worker-thread instance escapes to the runtime and OOB-traps the whole
    // session. Keep only the DragAcceptFiles(false) tail: no OLE callback is
    // the graceful headless state (MsftEdit default), plain editing works.
    // Public so the rewrite tests pin it directly, like the Redirect* rules.
    // File wrapper for the rev69 neutralization: applies it to bytes
    // that ship without passing through the rewriter (raw build output
    // embedded in the browser bundle). Idempotent: an already-neutral
    // body still contains its DragAcceptFiles call and rewrites cleanly.
    public static void NeutralizeFileRichEditOleCallback(string assemblyPath)
    {
        // Buffer fully first: Cecil keeps the read stream open for
        // lazy metadata, which self-locks a same-path write.
        var bytes = File.ReadAllBytes(assemblyPath);
        using var stream = new MemoryStream(bytes);
        string? dir = Path.GetDirectoryName(assemblyPath);
        using var resolver = new FrameworkResolver(dir, [AppContext.BaseDirectory]);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(stream, readerParams);
        NeutralizeRichEditOleCallback(assembly.MainModule);
        assembly.Write(assemblyPath);
    }
    public static void NeutralizeRichEditOleCallback(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition box = module.GetType("System.Windows.Forms.RichTextBox")
            ?? throw new InvalidOperationException("OleCb: RichTextBox missing");
        Mono.Cecil.MethodDefinition? target = null;
        foreach (Mono.Cecil.MethodDefinition m in box.Methods)
        {
            if (m.Name == "UpdateOleCallback" && m.HasBody && m.Parameters.Count == 0
                && m.ReturnType.FullName == "System.Void")
                target = m;
        }
        if (target is null)
            throw new InvalidOperationException("OleCb: UpdateOleCallback() missing");
        Mono.Cecil.MethodReference? callSite = null;
        foreach (var i in target.Body.Instructions)
        {
            if (i.Operand is Mono.Cecil.MethodReference r && r.Name == "DragAcceptFiles"
                && r.Parameters.Count == 2)
            {
                callSite = r;
                break;
            }
        }
        if (callSite is null)
            throw new InvalidOperationException("OleCb: DragAcceptFiles call missing");
        // Pin the raw (HWND, BOOL) overload on the callee's own type: a
        // generic DragAcceptFiles<T> wrapper takes the control directly, and
        // the original body calls it, but emitting constructed HWND/BOOL
        // against !!T is unverifiable (OOB-89A follow-up).
        Mono.Cecil.TypeDefinition? pinvoke = module.GetType(callSite.DeclaringType.FullName)
            ?? throw new InvalidOperationException("OleCb: PInvokeCore missing");
        Mono.Cecil.MethodDefinition? dragAccept = pinvoke.Methods.FirstOrDefault(m => m.Name == "DragAcceptFiles"
                && m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "Windows.Win32.Foundation.HWND"
                && m.Parameters[1].ParameterType.FullName == "Windows.Win32.Foundation.BOOL")
            ?? throw new InvalidOperationException("OleCb: DragAcceptFiles(HWND, BOOL) missing");
        Mono.Cecil.TypeDefinition controlType = module.GetType("System.Windows.Forms.Control")
            ?? throw new InvalidOperationException("OleCb: Control missing");
        Mono.Cecil.MethodDefinition getHandle = controlType.Methods.FirstOrDefault(m => m.Name == "get_Handle" && !m.HasParameters && m.ReturnType.FullName == "System.IntPtr")
            ?? throw new InvalidOperationException("OleCb: Control.get_Handle missing");
        Mono.Cecil.TypeDefinition hwndType = module.GetType("Windows.Win32.Foundation.HWND")
            ?? throw new InvalidOperationException("OleCb: HWND missing");
        Mono.Cecil.MethodDefinition hwndCtor = hwndType.Methods.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.IntPtr")
            ?? throw new InvalidOperationException("OleCb: HWND(nint) missing");
        Mono.Cecil.TypeDefinition boolType = module.GetType("Windows.Win32.Foundation.BOOL")
            ?? throw new InvalidOperationException("OleCb: BOOL missing");
        Mono.Cecil.MethodDefinition boolCtor = boolType.Methods.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int32")
            ?? throw new InvalidOperationException("OleCb: BOOL(int) missing");
        target.Body.ExceptionHandlers.Clear();
        target.Body.Variables.Clear();
        target.Body.Instructions.Clear();
        var il = target.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, getHandle);
        il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, hwndCtor);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, boolCtor);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, dragAccept);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        if (target.Body.MaxStackSize < 8)
            target.Body.MaxStackSize = 8;
    }

    // Headless EDITSTREAM replacement (rev77): RichTextBox.StreamIn(Stream,
    // uint), StreamOut(Stream, uint, bool) and the private GetTextEx
    // reader drive native RichEdit through
    // Marshal.GetFunctionPointerForDelegate, whose thunk traps the wasm
    // interpreter with "memory access out of bounds" and no managed frame
    // (Syncfusion diagram RichTextNode boot: set_RichText -> set_Rtf ->
    // StreamIn). The stubbed SendMessage could never deliver the bytes
    // anyway. Both bodies become 4-instruction trampolines to the managed
    // equivalents on the nested WasmRtf helper (same module, fork-owned
    // C#): same ArgumentException on a missing RTF tag, same MaxLength
    // truncation, text in the same WindowText storage (the GetTextEx
    // reader too: the stubbed EM_GETTEXTEX would report every box
    // blank). Public so the rewrite tests pin it directly, like the
    // Neutralize* rules.
    public static void ReplaceRichTextStreamMethods(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition box = module.GetType("System.Windows.Forms.RichTextBox")
            ?? throw new InvalidOperationException("Rtf: RichTextBox missing");
        Mono.Cecil.TypeDefinition? wasm = null;
        foreach (Mono.Cecil.TypeDefinition nested in box.NestedTypes)
        {
            if (nested.Name == "WasmRtf")
            {
                wasm = nested;
                break;
            }
        }

        if (wasm is null)
        {
            throw new InvalidOperationException("Rtf: RichTextBox/WasmRtf missing");
        }

        Mono.Cecil.MethodDefinition? streamInHelper = null;
        Mono.Cecil.MethodDefinition? streamOutHelper = null;
        Mono.Cecil.MethodDefinition? getTextHelper = null;
        foreach (Mono.Cecil.MethodDefinition m in wasm.Methods)
        {
            if (m.Name == "StreamInManaged" && m.IsStatic && m.Parameters.Count == 3)
            {
                streamInHelper = m;
            }

            if (m.Name == "StreamOutManaged" && m.IsStatic && m.Parameters.Count == 4)
            {
                streamOutHelper = m;
            }

            if (m.Name == "GetTextManaged" && m.IsStatic && m.Parameters.Count == 2)
            {
                getTextHelper = m;
            }
        }

        if (streamInHelper is null)
        {
            throw new InvalidOperationException("Rtf: WasmRtf.StreamInManaged missing");
        }

        if (streamOutHelper is null)
        {
            throw new InvalidOperationException("Rtf: WasmRtf.StreamOutManaged missing");
        }

        if (getTextHelper is null)
        {
            throw new InvalidOperationException("Rtf: WasmRtf.GetTextManaged missing");
        }

        Mono.Cecil.MethodDefinition? streamIn = null;
        Mono.Cecil.MethodDefinition? streamOut = null;
        Mono.Cecil.MethodDefinition? getTextEx = null;
        foreach (Mono.Cecil.MethodDefinition m in box.Methods)
        {
            if (!m.HasBody)
            {
                continue;
            }

            if (m.Name == "StreamIn" && m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "System.IO.Stream"
                && m.Parameters[1].ParameterType.FullName == "System.UInt32"
                && m.ReturnType.FullName == "System.Void")
            {
                streamIn = m;
            }

            if (m.Name == "StreamOut" && m.Parameters.Count == 3
                && m.Parameters[0].ParameterType.FullName == "System.IO.Stream"
                && m.Parameters[1].ParameterType.FullName == "System.UInt32"
                && m.Parameters[2].ParameterType.FullName == "System.Boolean"
                && m.ReturnType.FullName == "System.Void")
            {
                streamOut = m;
            }

            if (m.Name == "GetTextEx" && m.Parameters.Count == 1
                && m.ReturnType.FullName == "System.String")
            {
                getTextEx = m;
            }
        }

        if (streamIn is null)
        {
            throw new InvalidOperationException("Rtf: StreamIn(Stream, uint) missing");
        }

        if (streamOut is null)
        {
            throw new InvalidOperationException("Rtf: StreamOut(Stream, uint, bool) missing");
        }

        if (getTextEx is null)
        {
            throw new InvalidOperationException("Rtf: GetTextEx missing");
        }

        ReplaceWithTrampoline(streamIn, streamInHelper, 3);
        ReplaceWithTrampoline(streamOut, streamOutHelper, 4);
        ReplaceWithTrampoline(getTextEx, getTextHelper, 2);
    }

    private static void ReplaceWithTrampoline(
        Mono.Cecil.MethodDefinition target,
        Mono.Cecil.MethodDefinition helper,
        int argCount)
    {
        target.Body.ExceptionHandlers.Clear();
        target.Body.Variables.Clear();
        target.Body.Instructions.Clear();
        var il = target.Body.GetILProcessor();
        var loads = new[]
        {
            Mono.Cecil.Cil.OpCodes.Ldarg_0, Mono.Cecil.Cil.OpCodes.Ldarg_1,
            Mono.Cecil.Cil.OpCodes.Ldarg_2, Mono.Cecil.Cil.OpCodes.Ldarg_3,
        };
        for (int i = 0; i < argCount; i++)
        {
            il.Emit(loads[i]);
        }

        il.Emit(Mono.Cecil.Cil.OpCodes.Call, helper);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        if (target.Body.MaxStackSize < argCount)
        {
            target.Body.MaxStackSize = argCount;
        }
    }


    // Headless RichText paint (rev78): diagram RichTextNode renders its RTF
    // through RichTextBoxDrawing.Draw, which drives native RichEdit via
    // EM_FORMATRANGE (its own user32 SendMessage); both node paths
    // (RenderRTFToMetafile, RenderRTFToBitmap) funnel through it, and the
    // metafile shell itself cannot exist headless (record-mode ctor throws
    // in the Drawing fork). The Draw(Graphics, Rectangle) body becomes a
    // trampoline to the managed WasmRtfDraw equivalent (box font/color/
    // word-wrap via the Drawing fork); the 8-argument EM_FORMATRANGE
    // overload stays native and unreached. Scoped to the diagram assembly
    // (see hook); throws when the shape is absent so tests pin it.
    public static void ReplaceRtfDrawMethod(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition drawing = module.GetType("Syncfusion.Windows.Forms.Diagram.RichTextBoxDrawing")
            ?? throw new InvalidOperationException("RtfDraw: RichTextBoxDrawing missing");
        Mono.Cecil.MethodDefinition? draw = null;
        foreach (Mono.Cecil.MethodDefinition m in drawing.Methods)
        {
            if (!m.HasBody)
            {
                continue;
            }

            if (m.Name == "Draw" && m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "System.Drawing.Graphics"
                && m.Parameters[1].ParameterType.FullName == "System.Drawing.Rectangle"
                && m.ReturnType.FullName == "System.Void")
            {
                draw = m;
            }
        }

        if (draw is null)
        {
            throw new InvalidOperationException("RtfDraw: Draw(Graphics, Rectangle) missing");
        }

        // Cross-assembly trampoline into the fork: the rewriter remaps the
        // System.Windows.Forms/System.Drawing namespaces onto the
        // WfWasm.WinForms/WfWasm.Drawing assemblies, so the references below
        // use those scopes with the desktop namespaces. Requires a fork
        // migration carrying WasmRtfDraw to run before app transforms.
        Mono.Cecil.AssemblyNameReference? formsRef = null;
        foreach (Mono.Cecil.AssemblyNameReference r in module.AssemblyReferences)
        {
            if (r.Name == "WfWasm.WinForms")
            {
                formsRef = r;
            }
        }

        if (formsRef is null)
        {
            throw new InvalidOperationException("RtfDraw: WfWasm.WinForms reference missing");
        }

        // Reuse the module's own type references (correct scopes AND
        // IsValueType flags: hand-built struct references default to
        // CLASS and misbind at runtime) instead of constructing new
        // ones. RichTextBoxDrawing derives from RichTextBox, so its
        // base type is the exact parameter type the helper wants.
        var boxType = drawing.BaseType;
        var gfxType = draw.Parameters[0].ParameterType;
        var rectType = draw.Parameters[1].ParameterType;
        var helperType = new Mono.Cecil.TypeReference("System.Windows.Forms", "WasmRtfDraw", module, formsRef);
        var helper = new Mono.Cecil.MethodReference("DrawRtfManaged", module.TypeSystem.Void, helperType)
        {
            HasThis = false,
        };
        helper.Parameters.Add(new Mono.Cecil.ParameterDefinition(boxType));
        helper.Parameters.Add(new Mono.Cecil.ParameterDefinition(gfxType));
        helper.Parameters.Add(new Mono.Cecil.ParameterDefinition(rectType));
        Mono.Cecil.MethodReference helperRef = module.ImportReference(helper);

        draw.Body.ExceptionHandlers.Clear();
        draw.Body.Variables.Clear();
        draw.Body.Instructions.Clear();
        var il = draw.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, helperRef);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        if (draw.Body.MaxStackSize < 3)
        {
            draw.Body.MaxStackSize = 3;
        }
    }

    // Bitmap-mode default for diagram RichText (rev78): DrawInterior renders
    // via RenderRTFToMetafile unless m_bUseBitmap, and the metafile shell
    // cannot exist headless. The rewriter serves wasm only (pristine vendor
    // bytes stay on disk), so defaulting the flag in every RichTextNode
    // constructor degrades vector recording to the managed bitmap path
    // instead of dying; the public UseBitmap setter still overrides.
    public static void ForceDiagramBitmapText(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition node = module.GetType("Syncfusion.Windows.Forms.Diagram.RichTextNode")
            ?? throw new InvalidOperationException("RtfDraw: RichTextNode missing");
        Mono.Cecil.FieldDefinition? flag = null;
        foreach (Mono.Cecil.FieldDefinition f in node.Fields)
        {
            if (f.Name == "m_bUseBitmap")
            {
                flag = f;
            }
        }

        if (flag is null)
        {
            throw new InvalidOperationException("RtfDraw: m_bUseBitmap missing");
        }

        foreach (Mono.Cecil.MethodDefinition ctor in node.Methods)
        {
            if (ctor.Name != ".ctor" || !ctor.HasBody)
            {
                continue;
            }

            var il = ctor.Body.GetILProcessor();
            var rets = new System.Collections.Generic.List<Mono.Cecil.Cil.Instruction>();
            foreach (Mono.Cecil.Cil.Instruction instr in ctor.Body.Instructions)
            {
                if (instr.OpCode.Code == Mono.Cecil.Cil.Code.Ret)
                {
                    rets.Add(instr);
                }
            }

            foreach (Mono.Cecil.Cil.Instruction ret in rets)
            {
                il.InsertBefore(ret, il.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
                il.InsertBefore(ret, il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_1));
                il.InsertBefore(ret, il.Create(Mono.Cecil.Cil.OpCodes.Stfld, flag));
            }

            if (ctor.Body.MaxStackSize < 2)
            {
                ctor.Body.MaxStackSize = 2;
            }
        }
    }
    // File wrapper for the rev77 replacement: applies it to bytes that ship
    // without passing through the rewriter. Idempotent: an already-replaced
    // body is still a StreamIn(Stream, uint) shape and rewrites cleanly.
    public static void ManagedRtfFile(string assemblyPath)
    {
        var bytes = File.ReadAllBytes(assemblyPath);
        using var stream = new MemoryStream(bytes);
        string? dir = Path.GetDirectoryName(assemblyPath);
        using var resolver = new FrameworkResolver(dir, [AppContext.BaseDirectory]);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(stream, readerParams);
        ReplaceRichTextStreamMethods(assembly.MainModule);
        assembly.Write(assemblyPath);
    }

    // Wraps every parameterless void InitializeComponent method body and
    // every parameterless instance .ctor in an outer catch-all that
    // stashes the throw in WasmCtorGuard and returns degraded (see call
    // site for why). Existing instructions
    // are never moved or rewritten (a bare ret inside try is legal IL),
    // so existing handlers keep their ranges; the appended handler block
    // is unreachable by fallthrough (every valid body ends in a transfer).
    public static void GuardInitializeComponents(Mono.Cecil.ModuleDefinition module)
    {
        Mono.Cecil.AssemblyNameReference? formsScope =
            module.AssemblyReferences.FirstOrDefault(r => r.Name == "WfWasm.WinForms");
        if (formsScope is null)
        {
            formsScope = new Mono.Cecil.AssemblyNameReference("WfWasm.WinForms", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(formsScope);
        }
        var guardType = new Mono.Cecil.TypeReference("System.Windows.Forms", "WasmCtorGuard", module, formsScope);
        var stash = new Mono.Cecil.MethodReference("Stash", module.TypeSystem.Void, guardType)
        {
            HasThis = false,
        };
        stash.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.ImportReference(typeof(Exception))));
        GuardModuleTypes(module, module.Types, stash);
    }

    private static void GuardModuleTypes(
        Mono.Cecil.ModuleDefinition module,
        Mono.Collections.Generic.Collection<Mono.Cecil.TypeDefinition> types,
        Mono.Cecil.MethodReference stash)
    {
        foreach (Mono.Cecil.TypeDefinition type in types)
        {
            // Ctor scope: designer types only (a parameterless void
            // InitializeComponent declared on the type). Guarding every
            // parameterless .ctor broke services that depend on
            // propagation (PDN provider/color/plugin defaults); the
            // derived .ctor body still hosts the base-ctor call, so one
            // designer type's guard covers its whole base chain.
            bool designer = false;
            foreach (Mono.Cecil.MethodDefinition probe in type.Methods)
            {
                if (probe.Name == "InitializeComponent" && probe.Parameters.Count == 0
                    && probe.ReturnType.FullName == "System.Void")
                {
                    designer = true;
                    break;
                }
            }
            foreach (Mono.Cecil.MethodDefinition method in type.Methods)
            {
                // Designer InitializeComponent plus the parameterless
                // instance ctor of designer types (its body hosts the
                // base-ctor call, field initializers and post-Init custom
                // code). Parameterized ctors are only reached from those
                // two guarded contexts.
                bool initializes = method.Name == "InitializeComponent" && !method.IsStatic;
                bool constructs = designer && method.Name == ".ctor" && !method.IsStatic;
                if (!(initializes || constructs) || method.Parameters.Count != 0
                    || method.ReturnType.FullName != "System.Void" || !method.HasBody)
                    continue;
                Mono.Cecil.Cil.Instruction first = method.Body.Instructions[0];
                if (constructs)
                {
                    // A .ctor handler verified with `this` unassigned must
                    // not return normally (InvalidProgram on desktop,
                    // native unwind trap in WASM): the base-.ctor call and
                    // any inlined field initializers before it stay outside
                    // the guarded region, so the try starts only once
                    // `this` is definitely assigned. A throw from the base
                    // chain itself propagates exactly as unguarded.
                    foreach (Mono.Cecil.Cil.Instruction probe in method.Body.Instructions)
                    {
                        if ((probe.OpCode == Mono.Cecil.Cil.OpCodes.Call
                                || probe.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
                            && probe.Operand is Mono.Cecil.MethodReference mr
                            && mr.Name == ".ctor")
                        {
                            if (probe.Next is not null)
                                first = probe.Next;
                            break;
                        }
                    }
                }
                var il = method.Body.GetILProcessor();
                Mono.Cecil.Cil.Instruction handlerStart = il.Create(Mono.Cecil.Cil.OpCodes.Call, stash);
                Mono.Cecil.Cil.Instruction done = il.Create(Mono.Cecil.Cil.OpCodes.Ret);
                il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Leave, done));
                il.Append(handlerStart);
                il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Leave, done));
                il.Append(done);
                // A raw ret covered by the new try region is unverifiable
                // (desktop InvalidProgramException, native unwind trap in
                // WASM even though the bytes look consistent): route every
                // in-try return through the shared tail. The terminal done
                // ret itself stays.
                Mono.Cecil.Cil.Instruction? cursor = first;
                while (cursor is not null && cursor != handlerStart)
                {
                    Mono.Cecil.Cil.Instruction? next = cursor.Next;
                    if (cursor.OpCode == Mono.Cecil.Cil.OpCodes.Ret && cursor != done)
                        il.Replace(cursor, il.Create(Mono.Cecil.Cil.OpCodes.Leave, done));
                    cursor = next;
                }
                method.Body.ExceptionHandlers.Add(new Mono.Cecil.Cil.ExceptionHandler(Mono.Cecil.Cil.ExceptionHandlerType.Catch)
                {
                    TryStart = first,
                    TryEnd = handlerStart,
                    HandlerStart = handlerStart,
                    HandlerEnd = done,
                    CatchType = module.ImportReference(typeof(Exception)),
                });
                if (method.Body.MaxStackSize < 2)
                    method.Body.MaxStackSize = 2;
            }
            GuardModuleTypes(module, type.NestedTypes, stash);
        }
    }

    public static void RedirectSyncfusionWin11Check(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition form = module.GetType("Syncfusion.WinForms.Controls.SfForm")
            ?? throw new InvalidOperationException("SfWin11: SfForm missing");
        Mono.Cecil.MethodDefinition? target = null;
        foreach (Mono.Cecil.MethodDefinition m in form.Methods)
        {
            if (m.Name == "CheckIsWindows11" && m.HasBody && m.Parameters.Count == 0
                && m.ReturnType.FullName == "System.Boolean")
                target = m;
        }
        if (target is null)
            throw new InvalidOperationException("SfWin11: CheckIsWindows11() missing");
        target.Body.ExceptionHandlers.Clear();
        target.Body.Variables.Clear();
        target.Body.Instructions.Clear();
        var il = target.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        if (target.Body.MaxStackSize < 8)
            target.Body.MaxStackSize = 8;
    }

    // TextBoxBase.SelectAll routes through the live RichEdit selection
    // (EM_SETSEL against the created handle plus UIA automation events);
    // headless the selection path OOB-traps the session. Proven at
    // SfDateTimeEdit.SetFocusInEditingMode during Syncfusion calendar
    // boot (clean runs + click proof with the call dropped), same family
    // in SfNumericTextBox focus/click/shortcut paths. Drop the void call,
    // keeping the instance evaluation: selection is affordance-only, no
    // content or layout effect. Shrinking the body cannot overflow short
    // branches. Returns neutralized call sites.
    public static int NeutralizeTextBoxSelectAllCalls(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        int count = 0;
        foreach (Mono.Cecil.TypeDefinition type in AllTypes(module))
        {
            foreach (Mono.Cecil.MethodDefinition method in type.Methods)
            {
                if (!method.HasBody)
                    continue;
                var il = method.Body.GetILProcessor();
                foreach (Mono.Cecil.Cil.Instruction instr in method.Body.Instructions.ToArray())
                {
                    if (instr.OpCode != Mono.Cecil.Cil.OpCodes.Call
                        && instr.OpCode != Mono.Cecil.Cil.OpCodes.Callvirt)
                        continue;
                    if (instr.Operand is not Mono.Cecil.MethodReference target
                        || target.Name != "SelectAll"
                        || target.Parameters.Count != 0
                        || target.ReturnType.FullName != "System.Void"
                        || target.DeclaringType.Name != "TextBoxBase")
                        continue;
                    il.Replace(instr, il.Create(Mono.Cecil.Cil.OpCodes.Pop));
                    count++;
                }
            }
        }
        return count;
    }

    // TextBoxBase.get_TextLength routes through a generic P/Invoke
    // wrapper (GetWindowTextLength<T>) that the WASM interpreter cannot
    // transform (open-generic mint-type trap); headless it OOB-kills the
    // session. Proven at SfDateTimeEdit.RepositionFields during Syncfusion
    // calendar boot (bypassing the call loads, paints and clicks clean).
    // Rewrite it as get_Text().Length: on this fork both read the same
    // underlying text in every handle state (window-text storage when
    // created, the text field otherwise), so the value is identical and
    // the trap is gone. Growing the body can overflow short branches, so
    // the synthetic test pins desktop-JIT proof of the splice and the real-file
    // test pins shapes plus branch/handler/maxstack integrity. Returns rewritten
    // call sites.
    public static int RedirectTextBoxTextLengthToManaged(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.MethodReference? getLength = null;
        int count = 0;
        foreach (Mono.Cecil.TypeDefinition type in AllTypes(module))
        {
            foreach (Mono.Cecil.MethodDefinition method in type.Methods)
            {
                if (!method.HasBody)
                    continue;
                var il = method.Body.GetILProcessor();
                foreach (Mono.Cecil.Cil.Instruction instr in method.Body.Instructions.ToArray())
                {
                    if (instr.OpCode != Mono.Cecil.Cil.OpCodes.Call
                        && instr.OpCode != Mono.Cecil.Cil.OpCodes.Callvirt)
                        continue;
                    if (instr.Operand is not Mono.Cecil.MethodReference target
                        || target.Name != "get_TextLength"
                        || target.Parameters.Count != 0
                        || target.ReturnType.FullName != "System.Int32"
                        || target.DeclaringType.Name != "TextBoxBase")
                        continue;
                    getLength ??= module.ImportReference(
                        typeof(string).GetMethod("get_Length")!);
                    var getText = new Mono.Cecil.MethodReference(
                        "get_Text", module.TypeSystem.String, target.DeclaringType)
                    {
                        HasThis = true,
                    };
                    var call = il.Create(Mono.Cecil.Cil.OpCodes.Callvirt, getText);
                    il.Replace(instr, call);
                    il.InsertAfter(call, il.Create(Mono.Cecil.Cil.OpCodes.Callvirt, getLength));
                    count++;
                }
            }
        }
        return count;
    }

    private static System.Collections.Generic.IEnumerable<Mono.Cecil.TypeDefinition> AllTypes(
        Mono.Cecil.ModuleDefinition module)
    {
        var stack = new System.Collections.Generic.Stack<Mono.Cecil.TypeDefinition>();
        foreach (Mono.Cecil.TypeDefinition t in module.Types)
            stack.Push(t);
        while (stack.Count > 0)
        {
            Mono.Cecil.TypeDefinition t = stack.Pop();
            yield return t;
            foreach (Mono.Cecil.TypeDefinition n in t.NestedTypes)
                stack.Push(n);
        }
    }

    private static void EmitCctorTable(
        Mono.Cecil.MethodDefinition cctor, string owner, string siteName, string[] exts)
    {
        int site = -1;
        var instrs = cctor.Body.Instructions;
        for (int i = 0; i < instrs.Count; i++)
        {
            var op = instrs[i].Operand as Mono.Cecil.MethodReference;
            if (op is not null && op.DeclaringType.Name == "WicHelpers" && op.Name == siteName)
            { site = i; break; }
        }
        if (site < 0)
            throw new InvalidOperationException("CpuPaint: " + owner + ".cctor has no " + siteName);
        // Walk back over the argument chain (locals, vendor boxing);
        // terminate at the ContainerFormats getter that opens it.
        int head = site;
        bool headed = false;
        while (head > 0)
        {
            var prev = instrs[head - 1].Operand as Mono.Cecil.MethodReference;
            if (prev is not null && prev.DeclaringType.Name == "ContainerFormats")
            { head--; headed = true; break; }
            string code = instrs[head - 1].OpCode.Code.ToString();
            bool ok = code == "Call" || code == "Newobj"
                || code.StartsWith("Stloc") || code.StartsWith("Ldloc");
            if (!ok)
                throw new InvalidOperationException("CpuPaint: " + owner + ".cctor chain holds " + code);
            head--;
        }
        if (!headed)
            throw new InvalidOperationException("CpuPaint: " + owner + ".cctor " + siteName + " has no format chain");
        var il = cctor.Body.GetILProcessor();
        var seq = new System.Collections.Generic.List<Mono.Cecil.Cil.Instruction>();
        seq.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, exts.Length));
        seq.Add(il.Create(Mono.Cecil.Cil.OpCodes.Newarr, cctor.Module.TypeSystem.String));
        for (int i = 0; i < exts.Length; i++)
        {
            seq.Add(il.Create(Mono.Cecil.Cil.OpCodes.Dup));
            seq.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, i));
            seq.Add(il.Create(Mono.Cecil.Cil.OpCodes.Ldstr, exts[i]));
            seq.Add(il.Create(Mono.Cecil.Cil.OpCodes.Stelem_Ref));
        }
        for (int i = site; i >= head; i--)
            il.Remove(instrs[head]);
        var anchor = instrs[head];
        foreach (var ni in seq)
            il.InsertBefore(anchor, ni);
        if (cctor.Body.MaxStackSize < 8)
            cctor.Body.MaxStackSize = 8;
    }
    // HeadingLabel measures its heading through a null DirectWrite context
    // (DrawNull): headless the layout never materializes, the cached height
    // stays null, and every layout pass dies on Nullable.Value (the PNG
    // SaveConfigDialog never constructs). Answer the WinForms font line
    // height instead: single-line headings meter by font ascent (our
    // Font.Height is a pure 96dpi computation, no native metrics). A few
    // pixels off DWrite's exact ascent is cosmetically harmless; a dead
    // dialog is not. Only external callees are our own fork assemblies
    // (proven-safe redirect targets, cf. ResourceHelper).
    // Public so the rewrite tests pin it directly, like the Redirect* rules.
    public static void RedirectHeadingLabelPreferredHeight(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Mono.Cecil.TypeDefinition label = module.GetType("PaintDotNet.Controls.HeadingLabel")
            ?? throw new InvalidOperationException("CpuPaint: HeadingLabel missing");
        Mono.Cecil.MethodDefinition? target = null;
        foreach (var m in label.Methods)
            if (m.Name == "GetPreferredTextHeight" && m.HasBody && m.Parameters.Count == 0)
            { target = m; break; }
        if (target is null)
            throw new InvalidOperationException("CpuPaint: GetPreferredTextHeight missing");
        Mono.Cecil.AssemblyNameReference? formsScope = null;
        Mono.Cecil.AssemblyNameReference? drawingScope = null;
        foreach (var r in module.AssemblyReferences)
        {
            if (r.Name == "WfWasm.Shim" || r.Name == "WfWasm.WinForms" || r.Name == "System.Windows.Forms")
                formsScope ??= r;
            if (r.Name == "WfWasm.Drawing" || r.Name == "System.Drawing" || r.Name == "System.Drawing.Common")
                drawingScope ??= r;
        }
        if (formsScope is null || drawingScope is null)
            throw new InvalidOperationException("CpuPaint: fork scopes missing");
        var controlRef = new Mono.Cecil.TypeReference(
            "System.Windows.Forms", "Control", module, formsScope);
        var fontRef = new Mono.Cecil.TypeReference(
            "System.Drawing", "Font", module, drawingScope);
        var getFont = new Mono.Cecil.MethodReference("get_Font", fontRef, controlRef)
        { HasThis = true };
        var getHeight = new Mono.Cecil.MethodReference("get_Height", module.TypeSystem.Int32, fontRef)
        { HasThis = true };
        target.Body.ExceptionHandlers.Clear();
        target.Body.Variables.Clear();
        target.Body.Instructions.Clear();
        var il = target.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, getFont);
        il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, getHeight);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        if (target.Body.MaxStackSize < 8)
            target.Body.MaxStackSize = 8;
    }
    // BitmapImage.PostProcessImage answers its image: the tail is an
    // EnsurePrecision + PremultiplyEffect device chain whose factory
    // (CpuDrawingContext.CreateEffect) has no headless backend, so no
    // device image could ever complete. Premultiply/precision are
    // device concerns; CPU bitmaps upload as-is (same identity
    // rationale as the clipper/transform shortcuts). Blending fidelity
    // of unpremultiplied sources is revisited once pixels flow.
    // Effect rendering is single-threaded headless (Thread.Start is
    // neutered): EffectDriver.Start's spawn+WaitOne handshake can never
    // complete, so effect preview and apply silently do nothing (Levels
    // OK closes the dialog with zero canvas change, zero errors). Run
    // the worker inline on the caller thread instead, and run the tile
    // pool Enqueue inline with its Joins popped (each tile finishes
    // before Enqueue returns, so the waits are vacuous). One generic
    // base patch covers Classic and Bitmap drivers.
    private static bool IsBranchOp(OpCode op)
    {
        switch (op.Code.ToString())
        {
            case "Br": case "Br_S":
            case "Brfalse": case "Brfalse_S":
            case "Brtrue": case "Brtrue_S":
            case "Beq": case "Beq_S":
            case "Bge": case "Bge_S": case "Bge_Un": case "Bge_Un_S":
            case "Bgt": case "Bgt_S": case "Bgt_Un": case "Bgt_Un_S":
            case "Ble": case "Ble_S": case "Ble_Un": case "Ble_Un_S":
            case "Blt": case "Blt_S": case "Blt_Un": case "Blt_Un_S":
            case "Bne_Un": case "Bne_Un_S":
            case "Leave": case "Leave_S":
            case "Jmp": case "Switch":
            case "Throw": case "Rethrow":
            case "Endfilter": case "Endfinally":
                return true;
            default:
                return false;
        }
    }

    // The GPU tile reader builds a device-backed buffer in its .ctor and
    // dies headless on the null factory (CreateRef(null) in
    // BufferedDeviceContext..ctor). Guard just the device span: with a
    // null factory the field stays null and construction succeeds; the
    // CPU tile hook below serves pixels instead, and Dispose frees null
    // safely. The span is straight-line (asserted): ldarg.0, ldarg.1,
    // ... newobj BufferedDeviceContext, stfld.
    private static void EnsureBitmapSourceCpuTolerant(ModuleDefinition module)
    {
        TypeDefinition src = module.GetType("PaintDotNet.Direct2D1.Direct2DBitmapSourceBgra32")
            ?? throw new InvalidOperationException("CpuFx: Direct2DBitmapSourceBgra32 missing");
        MethodDefinition ctor = src.Methods
            .FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 5 && m.HasBody)
            ?? throw new InvalidOperationException("CpuFx: bitmap source .ctor missing");
        var sins = ctor.Body.Instructions;
        int jni = -1;
        for (int k = 0; k < sins.Count; k++)
        {
            if (sins[k].OpCode == OpCodes.Newobj
                && sins[k].Operand is MethodReference nmr
                && nmr.DeclaringType.FullName == "PaintDotNet.Direct2D1.BufferedDeviceContext")
            { jni = k; break; }
        }
        if (jni < 0 || jni + 1 >= sins.Count)
            throw new InvalidOperationException("CpuFx: buffered context creation missing");
        if (!(sins[jni + 1].Operand is FieldReference sfr) || sfr.Name != "bufferedDeviceContext")
            throw new InvalidOperationException("CpuFx: buffered context store missing");
        int span = jni;
        while (span > 0 && sins[span - 1].OpCode != OpCodes.Pop)
            span--;
        if (span + 1 >= jni || sins[span].OpCode != OpCodes.Ldarg_0 || sins[span + 1].OpCode != OpCodes.Ldarg_1)
            throw new InvalidOperationException("CpuFx: bitmap source device span shape changed");
        for (int k = span; k <= jni + 1; k++)
        {
            if (IsBranchOp(sins[k].OpCode))
                throw new InvalidOperationException("CpuFx: bitmap source device span not straight-line");
        }
        foreach (var h in ctor.Body.ExceptionHandlers)
        {
            foreach (var edge in new[] { h.TryStart, h.TryEnd, h.HandlerStart, h.HandlerEnd, h.FilterStart })
            {
                if (edge is null)
                    continue;
                for (int k = span; k <= jni + 1; k++)
                {
                    if (edge == sins[k])
                        throw new InvalidOperationException("CpuFx: handler edge inside device span");
                }
            }
        }
        Instruction skip = sins[jni + 2];
        var sil = ctor.Body.GetILProcessor();
        Instruction anchor = sins[span];
        sil.InsertBefore(anchor, sil.Create(OpCodes.Ldarg_1));
        sil.InsertBefore(anchor, sil.Create(OpCodes.Brfalse_S, skip));
        if (ctor.Body.MaxStackSize < 2)
            ctor.Body.MaxStackSize = 2;
    }

    // GPU effect tiles materialize as CPU pixels in OnCopyPixels. Route
    // them through the CPU tile renderer first (Levels today, others
    // fall back to the product path): ldarg.0/1/2, box the value-type
    // tile args, call, brfalse into the original body, ret on handled.
    // The prefix sits before the handler-protected body (branching into
    // a try is legal; the prefix itself is outside every handler).
    private static void EnsureGpuTileCpuRender(ModuleDefinition module, string? cpuPaintPath)
    {
        TypeDefinition renderer = module.GetType("PaintDotNet.Effects.Gpu.GpuEffect/Renderer")
            ?? throw new InvalidOperationException("CpuFx: GpuEffect Renderer missing");
        MethodDefinition copy = renderer.Methods
            .FirstOrDefault(m => m.Name == "OnCopyPixels" && m.Parameters.Count == 2 && m.HasBody)
            ?? throw new InvalidOperationException("CpuFx: Renderer.OnCopyPixels missing");
        if (!copy.Parameters[0].ParameterType.FullName.Contains("RegionPtr")
            || !copy.Parameters[1].ParameterType.FullName.Contains("Point2Int32"))
            throw new InvalidOperationException("CpuFx: Renderer.OnCopyPixels shape changed");
        MethodReference render = ImportCpuPaintMethod(module, cpuPaintPath, "TryRenderGpuTileCpu", 3);
        var sil = copy.Body.GetILProcessor();
        Instruction anchor = copy.Body.Instructions[0];
        sil.InsertBefore(anchor, sil.Create(OpCodes.Ldarg_0));
        sil.InsertBefore(anchor, sil.Create(OpCodes.Ldarg_1));
        sil.InsertBefore(anchor, sil.Create(OpCodes.Box, copy.Parameters[0].ParameterType));
        sil.InsertBefore(anchor, sil.Create(OpCodes.Ldarg_2));
        sil.InsertBefore(anchor, sil.Create(OpCodes.Box, copy.Parameters[1].ParameterType));
        sil.InsertBefore(anchor, sil.Create(OpCodes.Call, render));
        sil.InsertBefore(anchor, sil.Create(OpCodes.Brfalse_S, anchor));
        sil.InsertBefore(anchor, sil.Create(OpCodes.Ret));
        if (copy.Body.MaxStackSize < 4)
            copy.Body.MaxStackSize = 4;
    }

    private static void EnsureEffectDriverInline(ModuleDefinition module)
    {
        TypeDefinition driver = module.GetType("PaintDotNet.Effects.EffectDriver`1")
            ?? throw new InvalidOperationException("CpuFx: EffectDriver`1 missing");
        MethodDefinition start = driver.Methods
            .FirstOrDefault(m => m.Name == "Start" && m.Parameters.Count == 1 && m.HasBody)
            ?? throw new InvalidOperationException("CpuFx: EffectDriver.Start missing");
        var sins = start.Body.Instructions;
        int startCall = -1;
        for (int k = 0; k < sins.Count; k++)
        {
            if (sins[k].OpCode == OpCodes.Callvirt
                && sins[k].Operand is MethodReference smr
                && smr.DeclaringType.FullName == "System.Threading.Thread"
                && smr.Name == "Start")
                startCall = k;
        }
        if (startCall < 0)
            throw new InvalidOperationException("CpuFx: EffectDriver.Start spawn missing");
        // Pristine tail shape: ldarg.0, ldarg.0, ldftn ThreadFunction,
        // newobj ThreadStart, newobj Thread, stfld thread, ldarg.0,
        // ldfld thread, ldstr, callvirt set_Name, ldarg.0, ldc.i4.0,
        // newobj ManualResetEvent, stfld threadInitialized, ldarg.0,
        // ldfld thread, callvirt Start, ldarg.0, ldfld threadInitialized,
        // callvirt WaitOne, pop (the Close/null/ret after stays: the
        // event still exists for it). The doomed range MUST include the
        // Thread construction: leaving newobj Thread behind leaves two
        // values on the stack at ret (InvalidProgram in Start, observed
        // live as the Levels DoEffect Shown-handler death).
        int tail = startCall - 11;
        if (tail < 5)
            throw new InvalidOperationException("CpuFx: EffectDriver.Start shape short");
        if (sins[tail - 5].OpCode != OpCodes.Ldarg_0
            || sins[tail - 4].OpCode != OpCodes.Ldarg_0)
            throw new InvalidOperationException("CpuFx: EffectDriver.Start spawn/delegate object missing");
        if (sins[tail - 3].OpCode != OpCodes.Ldftn
            || !(sins[tail - 3].Operand is MethodReference fnRef)
            || fnRef.Name != "ThreadFunction")
            throw new InvalidOperationException("CpuFx: EffectDriver.Start threadfn missing");
        foreach (int ni in new[] { tail - 2, tail - 1 })
        {
            if (sins[ni].OpCode != OpCodes.Newobj
                || !(sins[ni].Operand is MethodReference nctor))
                throw new InvalidOperationException("CpuFx: EffectDriver.Start thread construction missing");
        }
        AssertFxOp(sins[tail], OpCodes.Stfld, "thread");
        AssertFxOp(sins[tail + 4], OpCodes.Callvirt, "set_Name");
        if (sins[tail + 7].OpCode != OpCodes.Newobj
            || !(sins[tail + 7].Operand is MethodReference mreCtor)
            || mreCtor.DeclaringType.FullName != "System.Threading.ManualResetEvent")
            throw new InvalidOperationException("CpuFx: EffectDriver.Start event missing");
        AssertFxOp(sins[tail + 8], OpCodes.Stfld, "threadInitialized");
        AssertFxOp(sins[startCall + 3], OpCodes.Callvirt, "WaitOne");
        if (sins[startCall + 4].OpCode != OpCodes.Pop)
            throw new InvalidOperationException("CpuFx: EffectDriver.Start wait-pop missing");
        Instruction firstDoomed = sins[tail - 5];
        Instruction lastDoomed = sins[startCall + 4];
        AssertFxRangeFree(start, firstDoomed, lastDoomed);
        MethodReference? onStarting = null;
        foreach (var ins in sins)
        {
            if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                && ins.Operand is MethodReference omr
                && omr.Name == "OnStartingRendering")
                onStarting = omr;
        }
        if (onStarting is null)
            throw new InvalidOperationException("CpuFx: EffectDriver.OnStartingRendering missing");
        var threadFn = new MethodReference(
            "ThreadFunction", module.TypeSystem.Void, onStarting.DeclaringType) { HasThis = true };
        var sil = start.Body.GetILProcessor();
        var n1 = sil.Create(OpCodes.Ldarg_0);
        var n2 = sil.Create(OpCodes.Ldc_I4_0);
        var n3 = sil.Create(OpCodes.Newobj, mreCtor);
        var n4 = sil.Create(OpCodes.Stfld, (FieldReference)sins[tail + 8].Operand);
        var n5 = sil.Create(OpCodes.Ldarg_0);
        var n6 = sil.Create(OpCodes.Call, threadFn);
        sil.InsertBefore(firstDoomed, n1);
        sil.InsertBefore(firstDoomed, n2);
        sil.InsertBefore(firstDoomed, n3);
        sil.InsertBefore(firstDoomed, n4);
        sil.InsertBefore(firstDoomed, n5);
        sil.InsertBefore(firstDoomed, n6);
        var doomed = new System.Collections.Generic.List<Instruction>();
        bool inRange = false;
        foreach (var ins in sins)
        {
            if (ins == firstDoomed)
                inRange = true;
            if (inRange)
                doomed.Add(ins);
            if (ins == lastDoomed)
                break;
        }
        foreach (var d in doomed)
            sil.Remove(d);
        if (start.Body.MaxStackSize < 8)
            start.Body.MaxStackSize = 8;
        // ThreadFunction pool Joins are vacuous once Enqueue runs inline.
        MethodDefinition worker = driver.Methods
            .FirstOrDefault(m => m.Name == "ThreadFunction" && m.HasBody)
            ?? throw new InvalidOperationException("CpuFx: EffectDriver.ThreadFunction missing");
        var joins = new System.Collections.Generic.List<Instruction>();
        foreach (var ins in worker.Body.Instructions)
        {
            if (ins.OpCode == OpCodes.Callvirt
                && ins.Operand is MethodReference jmr
                && jmr.Name == "Join"
                && jmr.DeclaringType.FullName == "PaintDotNet.Effects.EffectRendererWorkItemQueue")
                joins.Add(ins);
        }
        if (joins.Count != 3)
            throw new InvalidOperationException($"CpuFx: ThreadFunction joins changed ({joins.Count})");
        var wil = worker.Body.GetILProcessor();
        foreach (var j in joins)
            wil.Replace(j, wil.Create(OpCodes.Pop));
        // Tile Enqueue runs the tile synchronously on the caller.
        TypeDefinition queue = module.GetType("PaintDotNet.Effects.EffectRendererWorkItemQueue")
            ?? throw new InvalidOperationException("CpuFx: EffectRendererWorkItemQueue missing");
        MethodDefinition enqueue = queue.Methods
            .FirstOrDefault(m => m.Name == "Enqueue" && m.Parameters.Count == 1 && m.HasBody)
            ?? throw new InvalidOperationException("CpuFx: tile Enqueue missing");
        bool sawValidate = false;
        TypeReference? actionScope = null;
        bool sawNotify = false;
        foreach (var ins in enqueue.Body.Instructions)
        {
            if (ins.Operand is MethodReference emr)
            {
                if (emr.Name == "IsNotNull")
                    sawValidate = true;
                if (emr.DeclaringType is GenericInstanceType queueGit
                    && queueGit.ElementType.FullName == "System.Collections.Concurrent.ConcurrentQueue`1"
                    && emr.Name == "Enqueue"
                    && queueGit.GenericArguments.Count == 1)
                    actionScope = queueGit.GenericArguments[0];
                if (emr.Name == "UpdateNotifyWorkItemsQueued")
                    sawNotify = true;
            }
        }
        if (!sawValidate || actionScope is null || !sawNotify)
            throw new InvalidOperationException("CpuFx: tile Enqueue shape changed");
        // System.Action arrives via the queue's own generic argument
        // (System.Runtime forwarders on modern targets, mscorlib on
        // netfx): reuse that exact scope instead of guessing one.
        var invokeRef = new MethodReference("Invoke", module.TypeSystem.Void, actionScope) { HasThis = true };
        enqueue.Body.ExceptionHandlers.Clear();
        enqueue.Body.Variables.Clear();
        enqueue.Body.Instructions.Clear();
        var eil = enqueue.Body.GetILProcessor();
        eil.Emit(OpCodes.Ldarg_1);
        eil.Emit(OpCodes.Callvirt, invokeRef);
        eil.Emit(OpCodes.Ret);
        if (enqueue.Body.MaxStackSize < 8)
            enqueue.Body.MaxStackSize = 8;
    }

    // Cecil keeps short/long loads distinct (ldloc.s != ldloc):
    // shape matches must accept both.
    private static bool IsLdlocOp(OpCode op) => op == OpCodes.Ldloc || op == OpCodes.Ldloc_S;

    private static void AssertFxOp(Instruction ins, OpCode op, string member)
    {
        if (ins.OpCode != op)
            throw new InvalidOperationException($"CpuFx: expected {op}, saw {ins.OpCode}");
        string name = (ins.Operand as MemberReference)?.Name ?? string.Empty;
        if (name != member)
            throw new InvalidOperationException($"CpuFx: expected {member}, saw {name}");
    }

    private static void AssertFxRangeFree(MethodDefinition method, Instruction first, Instruction last)
    {
        bool inRange = false;
        var doomed = new System.Collections.Generic.HashSet<Instruction>();
        foreach (var ins in method.Body.Instructions)
        {
            if (ins == first)
                inRange = true;
            if (inRange)
                doomed.Add(ins);
            if (ins == last)
                break;
        }
        foreach (var ins in method.Body.Instructions)
        {
            if (ins.Operand is Instruction target && doomed.Contains(target) && !doomed.Contains(ins))
                throw new InvalidOperationException($"CpuFx: branch into patched range at {ins.OpCode}");
        }
        foreach (var h in method.Body.ExceptionHandlers)
        {
            foreach (var edge in new[] { h.TryStart, h.TryEnd, h.HandlerStart, h.HandlerEnd, h.FilterStart })
            {
                if (edge is not null && doomed.Contains(edge) && edge != last.Next)
                    throw new InvalidOperationException("CpuFx: handler edge inside patched range");
            }
        }
    }

    // ShowDialog returns None immediately headless, so RunEffectImpl's
    // post-dialog pipeline (DoEffect apply) tears down before the user
    // settles the dialog. Note the owning menu on the dialog here; the
    // FormClosed hook (EnsureEffectDialogResume) resumes the product
    // DoEffect then. Inserted after the SetEffect call: stack-neutral,
    // idempotent, safe under any branch shape.
    private static void EnsureEffectApplyResume(ModuleDefinition module, string? cpuPaintPath)
    {
        TypeDefinition menu = module.GetType("PaintDotNet.Menus.EffectMenuBase")
            ?? throw new InvalidOperationException("CpuFx: EffectMenuBase missing");
        MethodDefinition run = menu.Methods
            .FirstOrDefault(m => m.Name == "RunEffectImpl" && m.Parameters.Count == 1 && m.HasBody)
            ?? throw new InvalidOperationException("CpuFx: RunEffectImpl missing");
        int at = -1;
        var seq = run.Body.Instructions;
        for (int k = 3; k < seq.Count; k++)
        {
            if (seq[k].OpCode == OpCodes.Callvirt
                && seq[k].Operand is MethodReference smr
                && smr.Name == "SetEffect"
                && IsLdlocOp(seq[k - 3].OpCode)
                && seq[k - 2].OpCode == OpCodes.Ldfld
                && seq[k - 2].Operand is FieldReference sfr
                && sfr.Name == "configDialog"
                && IsLdlocOp(seq[k - 1].OpCode))
            { at = k; break; }
        }
        if (at < 0)
            throw new InvalidOperationException("CpuFx: RunEffectImpl SetEffect site missing");
        MethodReference note = ImportCpuPaintMethod(module, cpuPaintPath, "NoteEffectMenu", 2);
        var ril = run.Body.GetILProcessor();
        // Anchor once: InsertBefore mutates the live collection, so
        // re-reading seq[at+1] per insert chains onto the just-inserted
        // instruction and emits the sequence REVERSED (call on an empty
        // stack: InvalidProgram in RunEffectImpl, observed live when
        // opening Adjustments/Levels).
        var anchorFx = seq[at + 1];
        ril.InsertBefore(anchorFx, ril.Create(OpCodes.Ldloc, (VariableDefinition)seq[at - 3].Operand));
        ril.InsertBefore(anchorFx, ril.Create(OpCodes.Ldfld, (FieldReference)seq[at - 2].Operand));
        ril.InsertBefore(anchorFx, ril.Create(OpCodes.Ldarg_0));
        ril.InsertBefore(anchorFx, ril.Create(OpCodes.Call, note));
        if (run.Body.MaxStackSize < 8)
            run.Body.MaxStackSize = 8;
    }

    // The OK settle lands after ShowDialog already returned (immediate
    // None headless): subscribe FormClosed to a synthesized adapter that
    // routes the noted dialog through CpuPaint.ResumeEffectApply. The
    // adapter lives on EffectConfigForm so the delegate binds in-module;
    // the CpuPaint call itself is an exact import (like DrawText).
    private static void EnsureEffectDialogResume(ModuleDefinition module, string? cpuPaintPath)
    {
        TypeDefinition form = module.GetType("PaintDotNet.Effects.EffectConfigForm")
            ?? throw new InvalidOperationException("CpuFx: EffectConfigForm missing");
        // GPU effect dialogs (Levels and friends) do NOT inherit the base
        // body: EffectConfigForm<T,E> overrides IEffectConfigForm.ShowDialog
        // with its own Form.ShowDialog call, so the hook must land on both
        // bodies or GPU OK settles never resume DoEffect.
        TypeDefinition generic = module.GetType("PaintDotNet.Effects.EffectConfigForm`2")
            ?? throw new InvalidOperationException("CpuFx: EffectConfigForm<T> missing");
        var shows = new[]
        {
            form.Methods.FirstOrDefault(m => m.Name.EndsWith(".ShowDialog") && m.Parameters.Count == 1 && m.HasBody)
                ?? throw new InvalidOperationException("CpuFx: IEffectConfigForm.ShowDialog missing"),
            generic.Methods.FirstOrDefault(m => m.Name.EndsWith(".ShowDialog") && m.Parameters.Count == 1 && m.HasBody)
                ?? throw new InvalidOperationException("CpuFx: generic IEffectConfigForm.ShowDialog missing"),
        };
        AssemblyNameReference? swfScope = null;
        foreach (var r in module.AssemblyReferences)
        {
            if (r.Name == "System.Windows.Forms" || r.Name == "WfWasm.WinForms")
            { swfScope = r; break; }
        }
        if (swfScope is null)
            throw new InvalidOperationException("CpuFx: dialog resume forms scope missing");
        var fceaRef = new TypeReference("System.Windows.Forms", "FormClosedEventArgs", module, swfScope);
        var adapter = new MethodDefinition(
            "ResumeEffectApplyAdap",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        adapter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        adapter.Parameters.Add(new ParameterDefinition(fceaRef));
        form.Methods.Add(adapter);
        MethodReference resume = ImportCpuPaintMethod(module, cpuPaintPath, "ResumeEffectApply", 1);
        var ail = adapter.Body.GetILProcessor();
        ail.Emit(OpCodes.Ldarg_0);
        ail.Emit(OpCodes.Call, resume);
        ail.Emit(OpCodes.Pop);
        ail.Emit(OpCodes.Ret);
        var fcehRef = new TypeReference("System.Windows.Forms", "FormClosedEventHandler", module, swfScope);
        var fcehCtor = new MethodReference(".ctor", module.TypeSystem.Void, fcehRef) { HasThis = true };
        fcehCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        fcehCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
        var formRef = new TypeReference("System.Windows.Forms", "Form", module, swfScope);
        var addClosed = new MethodReference("add_FormClosed", module.TypeSystem.Void, formRef) { HasThis = true };
        addClosed.Parameters.Add(new ParameterDefinition(fcehRef));
        foreach (MethodDefinition show in shows)
        {
            var sseq = show.Body.Instructions;
            if (sseq.Count != 4
                || sseq[0].OpCode != OpCodes.Ldarg_0
                || sseq[1].OpCode != OpCodes.Ldarg_1
                || sseq[2].OpCode != OpCodes.Call
                || !(sseq[2].Operand is MethodReference baseShow)
                || baseShow.Name != "ShowDialog"
                || sseq[3].OpCode != OpCodes.Ret)
                throw new InvalidOperationException("CpuFx: IEffectConfigForm.ShowDialog shape changed");
            show.Body.Instructions.Clear();
            var sil2 = show.Body.GetILProcessor();
            sil2.Emit(OpCodes.Ldarg_0);
            sil2.Emit(OpCodes.Dup);
            sil2.Emit(OpCodes.Ldftn, adapter);
            sil2.Emit(OpCodes.Newobj, fcehCtor);
            sil2.Emit(OpCodes.Callvirt, addClosed);
            sil2.Emit(OpCodes.Ldarg_0);
            sil2.Emit(OpCodes.Ldarg_1);
            sil2.Emit(OpCodes.Call, baseShow);
            sil2.Emit(OpCodes.Ret);
            if (show.Body.MaxStackSize < 8)
                show.Body.MaxStackSize = 8;
        }
    }

    // Every abandoned kernel event handle finalize-crashes the browser
    // PAL (WaitSubsystem.DeleteHandle NRE is fatal on a finalizer
    // thread): per-render driver gates, dispatcher gates, pool rents,
    // and the update state machines all abandon theirs, so the tab
    // dies at the first gen-2 GC once stroke/tile pressure trips a
    // collection (~4 stroke-moves in). Suppress the SafeHandle
    // finalizer right after every PDN event-handle construction via
    // CpuPaint.DefuseWaitHandle (dup; call: stack-neutral, verifiable;
    // MaxStackSize bumped). Safe by construction (see helper): Dispose
    // still releases normally, and nothing waits on these headless.
    // Mutexes/semaphores excluded (ownership semantics); event-handle
    // Slims allocate lazily inside CoreLib and are covered only if
    // their paths never block (verified live, see rev123).
    private static void EnsureWaitHandlesDefused(ModuleDefinition module, string? cpuPaintPath)
    {
        MethodReference defuse = ImportCpuPaintMethod(module, cpuPaintPath, "DefuseWaitHandle", 1);
        int sites = 0;
        foreach (var type in module.Types)
            sites += DefuseInType(type, defuse);
        Console.WriteLine($"CpuFx: defused {sites} wait-handle sites in {module.Name}");
    }

    private static int DefuseInType(TypeDefinition type, MethodReference defuse)
    {
        int n = 0;
        foreach (var m in type.Methods)
        {
            if (!m.HasBody)
                continue;
            var news = new System.Collections.Generic.List<Instruction>();
            foreach (var ins in m.Body.Instructions)
            {
                if (ins.OpCode == OpCodes.Newobj
                    && ins.Operand is MethodReference mr
                    && (mr.DeclaringType.FullName == "System.Threading.ManualResetEvent"
                        || mr.DeclaringType.FullName == "System.Threading.AutoResetEvent"
                        || mr.DeclaringType.FullName == "System.Threading.EventWaitHandle"))
                    news.Add(ins);
            }
            if (news.Count == 0)
                continue;
            var il = m.Body.GetILProcessor();
            foreach (var nw in news)
            {
                var dup = il.Create(OpCodes.Dup);
                il.InsertAfter(nw, dup);
                il.InsertAfter(dup, il.Create(OpCodes.Call, defuse));
                n++;
            }
            m.Body.MaxStackSize = m.Body.MaxStackSize + 1;
        }
        foreach (var nt in type.NestedTypes)
            n += DefuseInType(nt, defuse);
        return n;
    }

    // History undo data never lands headless: BitmapHistoryMemento's
    // ctor persists via SetData(data, async: true), and the
    // TaskFactory.StartNew save never completes without a thread pump,
    // so get_Object hits an unwritten write-once file (IOE "current
    // state") on every undo, for product and replicated mementos
    // alike. Persist synchronously instead (one ldc flip at the single
    // async call site): the save runs inline in the ctor, the task is
    // born completed, and get_Object reads from disk (or the live
    // soft ref). Small canvases make the block negligible; exact-tail
    // pins are unaffected (no stack change).
    private static void EnsureHistoryPersistSynchronous(ModuleDefinition module)
    {
        var memento = module.GetType("PaintDotNet.HistoryMementos.BitmapHistoryMemento");
        if (memento is null)
            throw new InvalidOperationException("history persist needs BitmapHistoryMemento");
        int flips = 0;
        foreach (var m in memento.Methods)
        {
            if (m.Name != ".ctor" || !m.HasBody)
                continue;
            var list = new System.Collections.Generic.List<Instruction>(m.Body.Instructions);
            for (int k = 2; k < list.Count; k++)
            {
                if ((list[k].OpCode != OpCodes.Call && list[k].OpCode != OpCodes.Callvirt)
                    || list[k].Operand is not MethodReference mr
                    || mr.FullName != "System.Void PaintDotNet.HistoryMementos.HistoryMemento::SetData(PaintDotNet.HistoryMementos.HistoryMementoData,System.Boolean)")
                    continue;
                if (list[k - 1].OpCode != OpCodes.Ldc_I4_1)
                    throw new InvalidOperationException("history persist bool already flipped?");
                var il = m.Body.GetILProcessor();
                il.Replace(list[k - 1], il.Create(OpCodes.Ldc_I4_0));
                flips++;
            }
        }
        if (flips == 0)
            throw new InvalidOperationException("history persist found no async SetData site");
        Console.WriteLine($"CpuFx: history persist synchronous ({flips} site(s))");
    }

    // SoftReference<T> keeps its target strongly only while it is
    // young (GC generation 0): after the first gen-2 GC the strong
    // slot is cleared and only the WeakReference remains. Headless
    // that demotion is fatal for undo: PersistedObject<T>.get_Object
    // falls through to the session-file read, and PersistToDisk is a
    // stubbed ret (BinaryFormatter has no headless backend), so the
    // file path was never set and OpenForReading throws IOE "current
    // state" on every stroke/effect undo. The rewrite pins the target
    // in a spare strong slot: the data then lives exactly as long as
    // the SoftReference holder (the memento on the history stack),
    // which is the bound the stack depth already enforces. Only the
    // ctor and get_Target are touched: the slot is object-typed with
    // box/unbox at the boundary, the exact shapes the pristine
    // forwarding methods already used, so the browser interpreter
    // transforms nothing it has never seen. The TryGetTarget
    // overloads stay pristine on purpose: routing them through the
    // slot needs stobj/stind on the generic parameter, and that
    // combination throws InvalidCastException at runtime even on the
    // desktop CLR (plus risks the interpreter path). Nobody on the
    // undo path calls them (get_Object uses get_Target only). The
    // slot is public: the browser runtime's generic visibility check
    // rejects an added private field even from the type's own ctor
    // (FieldAccessException live). Post-dispose reads can observe a
    // stale target instead of null; PDN never reads disposed
    // mementos off-stack, so this is accepted.
    private static void EnsureHistorySoftReferenceStrong(ModuleDefinition module)
    {
        var soft = module.GetType("PaintDotNet.Runtime.SoftReference`1")
            ?? throw new InvalidOperationException("strong softref needs SoftReference<T>");
        if (soft.GenericParameters.Count != 1)
            throw new InvalidOperationException("strong softref generic arity changed");
        var gp = soft.GenericParameters[0];
        var objType = module.TypeSystem.Object;
        var fld = soft.Fields.FirstOrDefault(f => f.Name == "headlessStrongTarget");
        if (fld is null)
        {
            fld = new FieldDefinition("headlessStrongTarget", FieldAttributes.Public, objType);
            soft.Fields.Add(fld);
        }
        var getter = soft.Methods.FirstOrDefault(m => m.Name == "get_Target" && m.Parameters.Count == 0)
            ?? throw new InvalidOperationException("strong softref getter missing");
        int fixed_ = 0;
        foreach (var m in soft.Methods)
        {
            if (!m.HasBody)
                continue;
            if (m.Name == ".ctor" && m.Parameters.Count == 1)
            {
                var ins = m.Body.Instructions;
                if (ins.Count == 0 || ins[ins.Count - 1].OpCode != OpCodes.Ret)
                    throw new InvalidOperationException("strong softref ctor tail changed");
                var il = m.Body.GetILProcessor();
                var ret = ins[ins.Count - 1];
                il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
                il.InsertBefore(ret, il.Create(OpCodes.Box, gp));
                il.InsertBefore(ret, il.Create(OpCodes.Stfld, fld));
                m.Body.MaxStackSize = System.Math.Max(m.Body.MaxStackSize, 2);
                fixed_++;
            }
            else if (m == getter)
            {
                m.Body.ExceptionHandlers.Clear();
                m.Body.Variables.Clear();
                m.Body.Instructions.Clear();
                var il = m.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fld);
                il.Emit(OpCodes.Unbox_Any, gp);
                il.Emit(OpCodes.Ret);
                m.Body.MaxStackSize = System.Math.Max(m.Body.MaxStackSize, 1);
                fixed_++;
            }
        }
        if (fixed_ != 2)
            throw new InvalidOperationException($"strong softref shape changed ({fixed_})");
        Console.WriteLine("CpuFx: SoftReference<T> pinned strongly headless");
    }

    // HistoryControl.OnRenderAdvanced resolves each row's icon through
    // ImageResourceUtil.GetDeviceImage and lays the text out AFTER the
    // icon bounds. Observed headless: any row with a non-null icon
    // paints as a blank selected band (the icon path leaves no usable
    // text rect, so AdjustFontSizeToFitLayoutSize shrinks the name to
    // invisible), while icon-less rows paint their text fine. Hence
    // every tool row (Paintbrush, New Image, Select All, ...) is blank
    // while our own icon-less Levels row paints. All get_Image callers (row
    // paint, OnUndo/redo propagation, CompoundHistoryMemento,
    // TransactedTool commit paths) pass the value into another memento
    // ctor or branch on null: none dereferences it. Returning null
    // makes every row text-only, matching the established Levels
    // precedent; the setter still stores (harmless).
    private static void EnsureHistoryRowTextOnly(ModuleDefinition module)
    {
        var history = module.GetType("PaintDotNet.HistoryMementos.HistoryMemento")
            ?? throw new InvalidOperationException("history text needs HistoryMemento");
        var getter = history.Methods.FirstOrDefault(m => m.Name == "get_Image" && m.Parameters.Count == 0 && m.HasBody)
            ?? throw new InvalidOperationException("history text getter missing");
        getter.Body.ExceptionHandlers.Clear();
        getter.Body.Variables.Clear();
        getter.Body.Instructions.Clear();
        var il = getter.Body.GetILProcessor();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        Console.WriteLine("CpuFx: HistoryMemento.Image nulled headless (text-only rows)");
    }

    // HistoryStack.PushNewMemento calls HistoryMemento.Flush right
    // after pushing, and PersistedObject<T>.Flush waits for the disk
    // save then nulls softObjectRef to free memory: on desktop the
    // data is safely on disk and get_Object re-reads it on undo.
    // Headless the save is a stubbed ret (BinaryFormatter has no
    // backend), so the flush evicts the ONLY copy seconds after the
    // push: our replicated Levels memento is born unreadable
    // (get_Data IOEs immediately at push time, "born-threw", and at
    // every undo). With the SoftReference pin (see above) the memory
    // is already bounded by the history stack depth, so retaining it
    // is safe: Flush reduces to ret and the data lives as long as
    // its memento on the stack.
    private static void EnsureHistoryFlushRetained(ModuleDefinition module)
    {
        var persisted = module.GetType("PaintDotNet.Runtime.PersistedObject`1")
            ?? throw new InvalidOperationException("history flush needs PersistedObject<T>");
        var flush = persisted.Methods.FirstOrDefault(m => m.Name == "Flush" && m.Parameters.Count == 0 && m.HasBody)
            ?? throw new InvalidOperationException("history flush method missing");
        bool evicts = flush.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Stfld && i.Operand is FieldReference fr
            && fr.Name == "softObjectRef" && fr.DeclaringType.FullName.Contains("PersistedObject"));
        if (!evicts)
            throw new InvalidOperationException("history flush eviction shape changed");
        flush.Body.ExceptionHandlers.Clear();
        flush.Body.Variables.Clear();
        flush.Body.Instructions.Clear();
        flush.Body.GetILProcessor().Emit(OpCodes.Ret);
        Console.WriteLine("CpuFx: PersistedObject<T>.Flush retained (no headless eviction)");
    }

    // Layer previews stay blank headless: ThumbnailManager renders them
    // on a dedicated render thread (new Thread(RenderThread) in .ctor),
    // but Thread.Start is neutered browser-wide (pop, no worker), so
    // RenderThreadLoop never runs, the queue only grows via
    // QueueThumbnailUpdate + Monitor.Pulse, and OnThumbnailReady never
    // fires. Nothing throws, so the error inventory stays silent.
    // The loop body itself is single-thread-safe (Monitor Enter/Exit
    // re-enter on the caller, waits only fire on an empty queue, the
    // per-item catch classifies exceptions internally), and its callees
    // self-pump: CopyPixelsParallel funnels into
    // CountdownWorkItemQueue.JoinWhileExecutingWorkItems, which executes
    // queued tiles on the CALLING thread. So drain the loop inline at
    // the end of QueueThumbnailUpdate (UI thread): while the queue is
    // non-empty, run one RenderThreadLoop pass; stop when it reports
    // false (disposed). The empty-queue Monitor.Wait path is never
    // reached because of the count guard. SleepUntilUtc (a 200 ms
    // debounce Thread.Sleep for the background loop) reduces to ret:
    // it would stall the UI thread per thumbnail, and nothing else
    // calls it (asserted: every call site lives in ThumbnailManager).
    // Mutation order is load-bearing: opacity (harmless alone), then
    // the Half-bridge swap (safe alone), then sleep, then the drain
    // last. Any abort leaves at most safe mutations; drain without
    // the swap would run ToScRgb/WIC and OOB the runtime (rev131).
    private static void EnsureThumbnailManagerInline(ModuleDefinition module, string? cpuPaintPath)
    {
        var manager = module.GetType("PaintDotNet.Rendering.ThumbnailManager")
            ?? throw new InvalidOperationException("thumbnail inline needs ThumbnailManager");
        var strip = module.GetType("PaintDotNet.Controls.LayersStrip")
            ?? throw new InvalidOperationException("thumbnail inline strip missing");
        // InsertLayer zeroes the new strip item's ImageOpacity so the
        // later OnThumbnailUpdated can fade it in over 0.5 s. Headless
        // that fade never advances (observed live: op=0 forever with
        // the image set), so DrawItemImage skips on opacity product 0
        // and the strip shows its empty band. Start at the end state
        // (1) instead: desktop converges there after the fade.
        var insert = strip.Methods.FirstOrDefault(m => m.Name == "InsertLayer" && m.HasBody)
            ?? throw new InvalidOperationException("thumbnail inline insert missing");
        bool opPatched = false;
        {
            var iins = insert.Body.Instructions.ToList();
            for (int k = 0; k < iins.Count; k++)
            {
                if ((iins[k].OpCode == OpCodes.Callvirt || iins[k].OpCode == OpCodes.Call)
                    && iins[k].Operand is MethodReference imr
                    && imr.Name == "get_ImageOpacity")
                {
                    for (int j = k + 1; j < iins.Count; j++)
                    {
                        if (iins[j].OpCode == OpCodes.Ldc_R8)
                        {
                            if (!(iins[j].Operand is double d && d == 0.0))
                                throw new InvalidOperationException("thumbnail opacity init changed");
                            var iil = insert.Body.GetILProcessor();
                            iil.Replace(iins[j], iil.Create(OpCodes.Ldc_R8, 1.0));
                            opPatched = true;
                            break;
                        }
                    }
                    break;
                }
            }
        }
        if (!opPatched)
            throw new InvalidOperationException("thumbnail opacity init missing");
        Console.WriteLine("CpuFx: LayersStrip item ImageOpacity starts at 1 headless (no fade-in)");
        // Render swap (rev134): the default CreateThumbnailBitmapScRgb
        // converts Bgra32->Half through ToScRgb and the WIC imaging
        // factory, whose native COM sidecars cannot load in the
        // browser (OOB trap observed live). Route the render through
        // the managed Half bridge instead: same stack shape
        // (provider, side, priority, token), object result recast to
        // the interface return type.
        var loop = manager.Methods.FirstOrDefault(m => m.Name == "RenderThreadLoop" && m.Parameters.Count == 0 && m.HasBody)
            ?? throw new InvalidOperationException("thumbnail inline loop missing");
        Instruction? scrgbIns = null;
        MethodReference? scrgbSig = null;
        foreach (var i in loop.Body.Instructions)
        {
            if ((i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference smr
                && smr.Name == "CreateThumbnailBitmapScRgb")
            {
                scrgbIns = i;
                scrgbSig = smr;
                break;
            }
        }
        if (scrgbIns is null || scrgbSig is null)
            throw new InvalidOperationException("thumbnail scrgb call missing");
        // The interface call leaves (provider, sideLength, priority,
        // token) on the stack with sideLength as int32 and priority
        // as an enum: both must be re-pushed in boxable form for the
        // object-typed bridge (passing raw int32 bits as an object
        // reference OOBs the interpreter: rev134). Spill to locals,
        // box the enum, and call.
        var bridge = ImportCpuPaintMethod(module, cpuPaintPath, "RenderLayerThumbnail", 4);
        // Instance-method Parameters exclude `this`: [0]=sideLength,
        // [1]=priority, [2]=token.
        if (scrgbSig.Parameters.Count != 3
            || scrgbSig.Parameters[0].ParameterType.FullName != "System.Int32"
            || scrgbSig.Parameters[1].ParameterType.FullName != "PaintDotNet.Threading.WorkItemQueuePriority")
            throw new InvalidOperationException("thumbnail scrgb signature changed");
        var priType = module.ImportReference(scrgbSig.Parameters[1].ParameterType);
        var provLoc = new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Object);
        var sideLoc = new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Int32);
        var priLoc = new Mono.Cecil.Cil.VariableDefinition(priType);
        var tokLoc = new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Object);
        loop.Body.Variables.Add(provLoc);
        loop.Body.Variables.Add(sideLoc);
        loop.Body.Variables.Add(priLoc);
        loop.Body.Variables.Add(tokLoc);
        loop.Body.InitLocals = true;
        var bl = loop.Body.GetILProcessor();
        bl.InsertBefore(scrgbIns, bl.Create(OpCodes.Stloc, tokLoc));
        bl.InsertBefore(scrgbIns, bl.Create(OpCodes.Stloc, priLoc));
        bl.InsertBefore(scrgbIns, bl.Create(OpCodes.Stloc, sideLoc));
        bl.InsertBefore(scrgbIns, bl.Create(OpCodes.Stloc, provLoc));
        bl.InsertBefore(scrgbIns, bl.Create(OpCodes.Ldloc, provLoc));
        bl.InsertBefore(scrgbIns, bl.Create(OpCodes.Ldloc, sideLoc));
        bl.InsertBefore(scrgbIns, bl.Create(OpCodes.Ldloc, priLoc));
        bl.InsertBefore(scrgbIns, bl.Create(OpCodes.Box, priType));
        bl.InsertBefore(scrgbIns, bl.Create(OpCodes.Ldloc, tokLoc));
        scrgbIns.OpCode = OpCodes.Call;
        scrgbIns.Operand = bridge;
        bl.InsertAfter(scrgbIns, bl.Create(OpCodes.Castclass, module.ImportReference(scrgbSig.ReturnType)));
        loop.Body.MaxStackSize = System.Math.Max(loop.Body.MaxStackSize, 4);
        Console.WriteLine("CpuFx: thumbnail render uses managed Half bridge (no WIC)");
        var sleep = manager.Methods.FirstOrDefault(m => m.Name == "SleepUntilUtc" && m.Parameters.Count == 1 && m.HasBody)
            ?? throw new InvalidOperationException("thumbnail inline sleep missing");
        foreach (var t in module.GetTypes())
        {
            foreach (var m in t.Methods)
            {
                if (!m.HasBody)
                    continue;
                foreach (var i in m.Body.Instructions)
                {
                    if ((i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                        && i.Operand is MethodReference mr
                        && mr.Name == "SleepUntilUtc"
                        && mr.DeclaringType.FullName == "PaintDotNet.Rendering.ThumbnailManager"
                        && m.DeclaringType.FullName != "PaintDotNet.Rendering.ThumbnailManager")
                        throw new InvalidOperationException("thumbnail sleep caller outside manager: " + m.FullName);
                }
            }
        }
        sleep.Body.ExceptionHandlers.Clear();
        sleep.Body.Variables.Clear();
        sleep.Body.Instructions.Clear();
        sleep.Body.GetILProcessor().Emit(OpCodes.Ret);
        var queue = manager.Methods.FirstOrDefault(m => m.Name == "QueueThumbnailUpdate" && m.HasBody)
            ?? throw new InvalidOperationException("thumbnail inline queue missing");
        var queueField = manager.Fields.FirstOrDefault(f => f.Name == "renderQueue")
            ?? throw new InvalidOperationException("thumbnail inline queue field missing");
        if (!queueField.FieldType.FullName.StartsWith("PaintDotNet.Collections.Deque", StringComparison.Ordinal))
            throw new InvalidOperationException("thumbnail inline queue type changed");
        MethodReference? countGetter = null;
        foreach (var i in loop.Body.Instructions)
        {
            if (i.OpCode == OpCodes.Callvirt
                && i.Operand is MethodReference mr
                && mr.Name == "get_Count"
                && mr.DeclaringType.FullName.StartsWith("PaintDotNet.Collections.Deque", StringComparison.Ordinal))
            {
                countGetter = module.ImportReference(mr);
                break;
            }
        }
        if (countGetter is null)
            throw new InvalidOperationException("thumbnail inline count getter missing");
        var tail = queue.Body.Instructions[queue.Body.Instructions.Count - 1];
        if (tail.OpCode != OpCodes.Ret)
            throw new InvalidOperationException("thumbnail inline queue tail changed");
        // The try body exits via leave.s to the final ret: without
        // retargeting, the unwind jumps OVER an appended drain and
        // the queue grows forever (observed live: 3..10, never
        // drained). So retarget the ORIGINAL branches first (they
        // alone target the tail at this point), then append the
        // drain whose own exits keep targeting the tail.
        var il = queue.Body.GetILProcessor();
        var top = il.Create(OpCodes.Ldarg_0);
        foreach (var i in queue.Body.Instructions)
        {
            if (i.Operand is Instruction target && target == tail)
                i.Operand = top;
        }
        il.InsertBefore(tail, top);
        il.InsertBefore(tail, il.Create(OpCodes.Ldfld, queueField));
        il.InsertBefore(tail, il.Create(OpCodes.Callvirt, countGetter));
        il.InsertBefore(tail, il.Create(OpCodes.Brfalse_S, tail));
        il.InsertBefore(tail, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(tail, il.Create(OpCodes.Call, loop));
        il.InsertBefore(tail, il.Create(OpCodes.Brfalse_S, tail));
        il.InsertBefore(tail, il.Create(OpCodes.Br_S, top));
        queue.Body.MaxStackSize = System.Math.Max(queue.Body.MaxStackSize, 2);
        Console.WriteLine("CpuFx: ThumbnailManager drains inline headless (layer previews)");
        // (Opacity init + render swap run first, above; the drain they
        // feed lands here, last.)
    }

    // One poisoned thumbnail batch must not kill document setup:
    // DrainThumbnailReadyInvokeList invokes every ready handler
    // (LayersStrip, DocumentStrip, ...) in one pass, and any fault
    // inside it (observed live: NRE in the snapshot/dispose phase,
    // DocumentStrip ANE on a null bitmap) propagates through
    // RenderThreadLoop and QueueThumbnailUpdate into the layer/document
    // wiring that queued it, aborting boot setup (blank canvas, empty
    // history). Headless, a faulting batch is dropped after logging
    // nothing: the first-chance inventory keeps the signal, the app
    // survives, and the next invalidation re-queues. This wraps the
    // whole body (narrow handler guards cannot cover snapshot-phase
    // faults); the existing enumerator finally still runs on unwind.
    private static void EnsureThumbnailDeliveryRobust(ModuleDefinition module)
    {
        var manager = module.GetType("PaintDotNet.Rendering.ThumbnailManager")
            ?? throw new InvalidOperationException("thumbnail delivery needs ThumbnailManager");
        var drain = manager.Methods.FirstOrDefault(m => m.Name == "DrainThumbnailReadyInvokeList" && m.HasBody)
            ?? throw new InvalidOperationException("thumbnail delivery drain missing");
        var dil = drain.Body.GetILProcessor();
        var first = drain.Body.Instructions[0];
        var pop = dil.Create(OpCodes.Pop);
        var done = dil.Create(OpCodes.Ret);
        dil.Append(pop);
        dil.Append(done);
        drain.Body.ExceptionHandlers.Add(new Mono.Cecil.Cil.ExceptionHandler(Mono.Cecil.Cil.ExceptionHandlerType.Catch)
        {
            TryStart = first,
            TryEnd = pop,
            HandlerStart = pop,
            HandlerEnd = done,
            CatchType = module.ImportReference(typeof(System.Exception)),
        });
        drain.Body.MaxStackSize = System.Math.Max(drain.Body.MaxStackSize, 1);
        Console.WriteLine("CpuFx: thumbnail delivery drops faulting batches headless");
    }

    // Half-bitmap consumption (rev134): BitmapImage.CreateImpl routes
    // Half sources through CreateWithFormatConversion, which converts
    // via the WIC color transformer (native COM sidecars cannot load
    // headless: OOB trap, same family as the ToScRgb render death).
    // Headless, 64-bit sources instead recurse into CreateImpl with a
    // managed Bgra32 conversion (CpuPaint.ConvertHalfSourceToBgra),
    // landing on the proven Bgra32 device-bitmap path (HALFUP upload).
    // A null conversion throws NotSupported (loud, managed, no trap).
    private static void EnsureThumbnailConsumeManaged(ModuleDefinition module, string? cpuPaintPath)
    {
        var img = module.GetType("PaintDotNet.Direct2D1.BitmapImage")
            ?? throw new InvalidOperationException("thumbnail consume needs BitmapImage");
        var conv = img.Methods.FirstOrDefault(m => m.Name == "CreateWithFormatConversion" && m.HasBody)
            ?? throw new InvalidOperationException("thumbnail consume convert missing");
        if (conv.Parameters.Count != 6
            || conv.Parameters[1].ParameterType.FullName != "PaintDotNet.Imaging.IBitmapSource")
            throw new InvalidOperationException("thumbnail consume signature changed");
        var createImpl = img.Methods.FirstOrDefault(m => m.Name == "CreateImpl" && m.HasBody && m.Parameters.Count == 5
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Direct2D1.IDeviceContext"
                && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapSource")
            ?? throw new InvalidOperationException("thumbnail consume createimpl missing");
        var getFormat = VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "get_PixelFormat", 0);
        var getBpp = StructCpuGetter(module, "PaintDotNet.Imaging.PixelFormat", "GetBitsPerPixel");
        var fmtType = OwnedCpuRef(module, "PaintDotNet.Imaging.PixelFormat");
        var convert = ImportCpuPaintMethod(module, cpuPaintPath, "ConvertHalfSourceToBgra", 1);
        var nseCtor = module.ImportReference(
            typeof(NotSupportedException).GetConstructor(new[] { typeof(string) })!);
        var fmtLoc = new Mono.Cecil.Cil.VariableDefinition(fmtType);
        var convLoc = new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Object);
        conv.Body.Variables.Add(fmtLoc);
        conv.Body.Variables.Add(convLoc);
        conv.Body.InitLocals = true;
        var il = conv.Body.GetILProcessor();
        var first = conv.Body.Instructions[0];
        var is32 = il.Create(OpCodes.Nop);
        var convOk = il.Create(OpCodes.Nop);
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Callvirt, getFormat));
        il.InsertBefore(first, il.Create(OpCodes.Stloc, fmtLoc));
        il.InsertBefore(first, il.Create(OpCodes.Ldloca_S, fmtLoc));
        il.InsertBefore(first, il.Create(OpCodes.Call, getBpp));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_S, (sbyte)64));
        il.InsertBefore(first, il.Create(OpCodes.Bne_Un, is32));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Call, convert));
        il.InsertBefore(first, il.Create(OpCodes.Stloc, convLoc));
        il.InsertBefore(first, il.Create(OpCodes.Ldloc, convLoc));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue_S, convOk));
        il.InsertBefore(first, il.Create(OpCodes.Ldstr, "CpuPaint: no headless backend for Half thumbnail upload"));
        il.InsertBefore(first, il.Create(OpCodes.Newobj, nseCtor));
        il.InsertBefore(first, il.Create(OpCodes.Throw));
        il.InsertBefore(first, convOk);
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldloc, convLoc));
        il.InsertBefore(first, il.Create(OpCodes.Castclass, conv.Parameters[1].ParameterType));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_2));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_S, conv.Parameters[4]));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_S, conv.Parameters[5]));
        il.InsertBefore(first, il.Create(OpCodes.Call, createImpl));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
        il.InsertBefore(first, is32);
        conv.Body.MaxStackSize = System.Math.Max(conv.Body.MaxStackSize, 6);
        Console.WriteLine("CpuFx: Half device upload converts managed headless (no WIC)");
    }

    // Update checks cannot install anything headless, and the boot
    // update flow abandons its state-machine ManualResetEvents (3 per
    // boot in StateMachineExecutor, 2 more in CheckingState): with no
    // Dispose they die at the first gen-2 GC, and the browser PAL
    // NREs in WaitSubsystem.DeleteHandle once per handle (fatal in a
    // finalizer, so the tab dies ~4 stroke-moves in when stroke
    // allocations trip the collection). Neuter the flow at its roots:
    // InitializeUpdates (boot trigger) and PerformUpdateCheck (manual
    // menu trigger) both reduce to Ret. Journal/expiry logic reads
    // flags, never the machine, so nothing else depends on it.
    private static void EnsureUpdatesDisabled(ModuleDefinition module)
    {
        TypeDefinition svc = module.GetType("PaintDotNet.Updates.UpdatesService")
            ?? throw new InvalidOperationException("CpuFx: UpdatesService missing");
        int neutered = 0;
        foreach (var m in svc.Methods)
        {
            if ((m.Name == "InitializeUpdates" || m.Name == "PerformUpdateCheck")
                && m.Parameters.Count == 0
                && m.HasBody)
            {
                m.Body.ExceptionHandlers.Clear();
                m.Body.Variables.Clear();
                m.Body.Instructions.Clear();
                var il = m.Body.GetILProcessor();
                il.Emit(OpCodes.Ret);
                neutered++;
            }
        }
        if (neutered != 2)
            throw new InvalidOperationException($"CpuFx: update roots changed ({neutered})");
    }

    private static void EnsureCpuPostProcessIdentity(ModuleDefinition module)
    {
        TypeDefinition img = module.GetType("PaintDotNet.Direct2D1.BitmapImage")
            ?? throw new InvalidOperationException("CpuPaint: BitmapImage missing");
        foreach (var m in img.Methods)
        {
            if (m.Name != "PostProcessImage" || !m.HasBody)
                continue;
            if (m.Parameters.Count != 4
                || m.Parameters[1].ParameterType.FullName != "PaintDotNet.Direct2D1.IDeviceImage"
                || m.ReturnType.FullName != "PaintDotNet.Direct2D1.IDeviceImage")
                continue;
            m.Body.ExceptionHandlers.Clear();
            m.Body.Variables.Clear();
            m.Body.Instructions.Clear();
            var il = m.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
            if (m.Body.MaxStackSize < 8)
                m.Body.MaxStackSize = 8;
        }
    }
    // The four bundled codec plugins (Avif/DDS/JpegXL/WebP) need native
    // sidecars that cannot load in the browser, so their registry
    // factories always throw during discovery (DllNotFoundException per
    // dialog upstream). PdnFileTypes routes every factory through
    // FileTypeHelpers.TryCreateWithErrorReporting, which treats a null
    // factory result as "no plugin" with no report. Returning null from
    // the four factory lambdas (instead of constructing) skips the
    // plugins with zero throws. No try/catch is emitted on purpose:
    // the exact 4-instruction factory shape (ldarg.0, ldfld host,
    // newobj .ctor(host), ret) is replaced by ldnull/ret, and anything
    // else is left untouched (count mismatch fails loudly).
    private static void EnsureBundledPluginFactoryNull(ModuleDefinition module)
    {
        string[] plugins = new[]
        {
            "PaintDotNet.Data.AvifFileType",
            "PaintDotNet.Data.DdsFileType",
            "PaintDotNet.Data.JpegXLFileType",
            "PaintDotNet.Data.WebPFileType",
        };
        int neutered = 0;
        foreach (TypeDefinition top in module.Types)
        {
            foreach (TypeDefinition type in top.NestedTypes)
            {
                if (type.DeclaringType is null || type.DeclaringType.FullName != "PaintDotNet.Data.PdnFileTypes")
                    continue;
                foreach (MethodDefinition m in type.Methods)
                {
                    if (!m.HasBody || m.Body.Instructions.Count != 4)
                        continue;
                    var ins = m.Body.Instructions;
                    if (ins[0].OpCode != OpCodes.Ldarg_0
                        || ins[1].OpCode != OpCodes.Ldfld
                        || ins[2].OpCode != OpCodes.Newobj
                        || ins[3].OpCode != OpCodes.Ret)
                        continue;
                    if (ins[2].Operand is not MethodReference ctor
                        || ctor.Parameters.Count != 1
                        || ctor.Parameters[0].ParameterType.FullName != "PaintDotNet.IFileTypeHost"
                        || !plugins.Contains(ctor.DeclaringType.FullName)
                        || m.ReturnType.FullName != ctor.DeclaringType.FullName)
                        continue;
                    m.Body.ExceptionHandlers.Clear();
                    m.Body.Variables.Clear();
                    m.Body.Instructions.Clear();
                    var il = m.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ret);
                    if (m.Body.MaxStackSize < 8)
                        m.Body.MaxStackSize = 8;
                    neutered++;
                }
            }
        }
        if (neutered != 4)
            throw new InvalidOperationException($"Pdn: expected 4 plugin factories, neutered {neutered}");
    }
    // UWP Toolkit headless init: Initialize() walks COM activation,
    // CLSID/AppUserModelId registry, and shortcut identity - none of
    // which exist in the browser. Seed a constant AUMID and answer the
    // activator factory without touching the OS; notification calls
    // then run against the WinRT stubs.
    private static void EnsureToolkitHeadlessInit(ModuleDefinition module)
    {
        TypeDefinition? compat = module.GetType("Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat");
        if (compat is null)
            throw new InvalidOperationException("Toolkit: ToastNotificationManagerCompat not found");
        FieldDefinition? aumid = compat.Fields.FirstOrDefault(f => f.Name == "_win32Aumid");
        FieldDefinition? registered = compat.Fields.FirstOrDefault(f => f.Name == "_registeredOnActivated");
        MethodDefinition? init = compat.Methods.FirstOrDefault(m => m.Name == "Initialize" && m.IsStatic && m.Parameters.Count == 0);
        MethodDefinition? factory = compat.Methods.FirstOrDefault(m => m.Name == "CreateAndRegisterActivator" && m.IsStatic && m.Parameters.Count == 0);
        if (aumid is null || registered is null || init is null || factory is null || !init.HasBody || !factory.HasBody)
            throw new InvalidOperationException("Toolkit: headless-init targets not found");
        MethodReference getTypeFromHandle = module.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle", new[] { typeof(RuntimeTypeHandle) })!);
        init.Body.ExceptionHandlers.Clear();
        init.Body.Variables.Clear();
        init.Body.Instructions.Clear();
        var initIl = init.Body.GetILProcessor();
        initIl.Emit(OpCodes.Ldstr, "WfWasm.Toast");
        initIl.Emit(OpCodes.Stsfld, aumid);
        initIl.Emit(OpCodes.Ret);
        if (init.Body.MaxStackSize < 8)
            init.Body.MaxStackSize = 8;
        factory.Body.ExceptionHandlers.Clear();
        factory.Body.Variables.Clear();
        factory.Body.Instructions.Clear();
        var factoryIl = factory.Body.GetILProcessor();
        factoryIl.Emit(OpCodes.Ldc_I4_1);
        factoryIl.Emit(OpCodes.Stsfld, registered);
        factoryIl.Emit(OpCodes.Ldtoken, compat);
        factoryIl.Emit(OpCodes.Call, getTypeFromHandle);
        factoryIl.Emit(OpCodes.Ret);
        if (factory.Body.MaxStackSize < 8)
            factory.Body.MaxStackSize = 8;
    }
    // CPU drawing-context extensions: the context is software by
    // definition, and image measurements answer local bounds for
    // both rects. Tokens harvest from the original bodies: never
    // hand-build generic references the interpreter rejects.
    private static void EnsureCpuContextExtensions(ModuleDefinition module)
    {
        TypeDefinition ext = module.GetType("PaintDotNet.Direct2D1.DeviceContextExtensions")
            ?? throw new InvalidOperationException("CpuPaint: DeviceContextExtensions missing");
        foreach (var m in ext.Methods)
        {
            if (m.Name == "IsSoftwareContext" && m.Parameters.Count == 1 && m.HasBody)
            {
                m.Body.ExceptionHandlers.Clear();
                m.Body.Instructions.Clear();
                var il = m.Body.GetILProcessor();
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);
                if (m.Body.MaxStackSize < 8)
                    m.Body.MaxStackSize = 8;
            }
            if (m.Name == "GetImageLocalMeasurements" && m.Parameters.Count == 2 && m.HasBody)
            {
                MethodReference? getBounds = null;
                MethodReference? tupleCtor = null;
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.Operand is not MethodReference mr)
                        continue;
                    if (mr.Name == "GetImageLocalBounds" && getBounds is null)
                        getBounds = mr;
                    if (mr.Name == ".ctor" && mr.DeclaringType.FullName.StartsWith("System.ValueTuple`2", StringComparison.Ordinal) && tupleCtor is null)
                        tupleCtor = mr;
                }
                if (getBounds is null || tupleCtor is null)
                    throw new InvalidOperationException("CpuPaint: measurement tokens missing");
                m.Body.ExceptionHandlers.Clear();
                m.Body.Instructions.Clear();
                var il = m.Body.GetILProcessor();
                var loc = new VariableDefinition(((Mono.Cecil.GenericInstanceType)m.ReturnType).GenericArguments[0]);
                m.Body.Variables.Add(loc);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                // Interface targets reject non-virtual dispatch: `call`
                // on them is invalid IL (observed as BadImageFormat).
                bool boundsIsIface = false;
                try { boundsIsIface = getBounds.DeclaringType.Resolve()?.IsInterface == true; }
                catch { }
                il.Emit(boundsIsIface ? OpCodes.Callvirt : OpCodes.Call, getBounds);
                il.Emit(OpCodes.Stloc, loc);
                il.Emit(OpCodes.Ldloc, loc);
                il.Emit(OpCodes.Ldloc, loc);
                il.Emit(OpCodes.Newobj, tupleCtor);
                il.Emit(OpCodes.Ret);
                m.Body.MaxStackSize = 8;
                m.Body.InitLocals = true;
            }
        }
    }
    private static TypeDefinition SynthesizeCpuTextLayout(ModuleDefinition module)
    {
        TypeReference iface = OwnedCpuRef(module, "PaintDotNet.DirectWrite.ITextLayout");
        TypeDefinition ifaceDef = ResolveCpuDef(iface);
        var type = new TypeDefinition(
            "PaintDotNet.Direct2D1", "CpuTextLayout",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        type.Interfaces.Add(new InterfaceImplementation(iface));
        // Public like the bitmap's: the cache dispatch fills them, and
        // the CPU text routes read Text back by reflection.
        var textField = new FieldDefinition("_text", FieldAttributes.Public, module.TypeSystem.String);
        var fontSizeField = new FieldDefinition("_fontSize", FieldAttributes.Public, module.TypeSystem.Single);
        var maxWField = new FieldDefinition("_maxW", FieldAttributes.Public, module.TypeSystem.Single);
        var maxHField = new FieldDefinition("_maxH", FieldAttributes.Public, module.TypeSystem.Single);
        type.Fields.Add(textField);
        type.Fields.Add(fontSizeField);
        type.Fields.Add(maxWField);
        type.Fields.Add(maxHField);
        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
        var cil = ctor.Body.GetILProcessor();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, objectCtor);
        FieldDefinition[] stores = [textField, fontSizeField, maxWField, maxHField];
        for (int i = 0; i < stores.Length; i++)
        {
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Ldarg, ctor.Parameters[i]);
            cil.Emit(OpCodes.Stfld, stores[i]);
        }
        cil.Emit(OpCodes.Ret);
        ctor.Body.MaxStackSize = 32;
        type.Methods.Add(ctor);
        // Text is not on the device contract, but the CPU DrawText
        // route reflects get_Text off whatever layout it is handed.
        var getText = new MethodDefinition(
            "get_Text",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            module.TypeSystem.String);
        var gtil = getText.Body.GetILProcessor();
        gtil.Emit(OpCodes.Ldarg_0);
        gtil.Emit(OpCodes.Ldfld, textField);
        gtil.Emit(OpCodes.Ret);
        getText.Body.MaxStackSize = 32;
        type.Methods.Add(getText);
        MethodReference metricsCtor = CtorCpuRef(module, "PaintDotNet.DirectWrite.TextMetrics", 10);
        MethodReference strLen = module.ImportReference(typeof(string).GetMethod("get_Length", Type.EmptyTypes)!);
        foreach (MethodDefinition contract in InterfaceCpuMethods(ifaceDef))
        {
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuTypeLenient(module, contract.ReturnType));
            foreach (var p in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, MapCpuTypeLenient(module, p.ParameterType)));
            var body = impl.Body.GetILProcessor();
            if (contract.Name == "Dispose" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_FontSize" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, fontSizeField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_MaxWidth" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, maxWField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_MaxHeight" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, maxHField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "set_MaxWidth" && contract.Parameters.Count == 1)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Stfld, maxWField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "set_MaxHeight" && contract.Parameters.Count == 1)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Stfld, maxHField);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_MinWidth" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ldc_R4, 0f);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "get_Metrics" && contract.Parameters.Count == 0)
            {
                var hasText = body.Create(OpCodes.Nop);
                var gotLen = body.Create(OpCodes.Nop);
                body.Emit(OpCodes.Ldc_R4, 0f);
                body.Emit(OpCodes.Ldc_R4, 0f);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, textField);
                body.Emit(OpCodes.Dup);
                body.Emit(OpCodes.Brtrue, hasText);
                body.Emit(OpCodes.Pop);
                body.Emit(OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Br, gotLen);
                body.Append(hasText);
                body.Emit(OpCodes.Callvirt, strLen);
                body.Append(gotLen);
                body.Emit(OpCodes.Conv_R4);
                body.Emit(OpCodes.Ldc_R4, 6f);
                body.Emit(OpCodes.Mul);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, fontSizeField);
                body.Emit(OpCodes.Mul);
                body.Emit(OpCodes.Ldc_R4, 7f);
                body.Emit(OpCodes.Div);
                body.Emit(OpCodes.Dup);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, fontSizeField);
                body.Emit(OpCodes.Dup);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, maxWField);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, maxHField);
                body.Emit(OpCodes.Ldc_I4_1);
                body.Emit(OpCodes.Ldc_I4_1);
                body.Emit(OpCodes.Newobj, metricsCtor);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.ReturnType.FullName == "System.Void")
            {
                body.Emit(OpCodes.Ret);
            }
            else if (contract.ReturnType.Resolve()?.IsValueType == true)
            {
                var zero = new VariableDefinition(MapCpuTypeLenient(module, contract.ReturnType));
                impl.Body.Variables.Add(zero);
                impl.Body.InitLocals = true;
                body.Emit(OpCodes.Ldloca_S, zero);
                body.Emit(OpCodes.Initobj, MapCpuTypeLenient(module, contract.ReturnType));
                body.Emit(OpCodes.Ldloc, zero);
                body.Emit(OpCodes.Ret);
            }
            else
            {
                body.Emit(OpCodes.Ldstr, "CpuTextLayout." + contract.Name + " has no headless backend");
                body.Emit(OpCodes.Newobj, module.ImportReference(typeof(NotSupportedException).GetConstructor([typeof(string)])!));
                body.Emit(OpCodes.Throw);
            }
            impl.Body.MaxStackSize = 32;
            type.Methods.Add(impl);
        }
        AddCpuObjectRef(type, module);
        module.Types.Add(type);
        return type;
    }
    private static bool ImplementsCpu(TypeDefinition def, string ifaceFullName)
    {
        var stack = new Stack<TypeDefinition>();
        stack.Push(def);
        while (stack.Count > 0)
        {
            TypeDefinition current = stack.Pop();
            foreach (var ii in current.Interfaces)
            {
                if (ii.InterfaceType.FullName == ifaceFullName)
                    return true;
                TypeDefinition? d = ii.InterfaceType.Resolve();
                if (d is not null)
                    stack.Push(d);
            }
        }
        return false;
    }

    // WIC format conversion without WIC: ImagingFactory.Instance is
    // null headless, so every converter throws on the factory before
    // comparing formats. Identity answers the source itself; the
    // Bgra32<->Pbgra32 pair converts on the CPU into a freshly
    // allocated locked bitmap, as does the Prgba128Float->Pbgra32
    // download (brush sprite mip levels feed DrawBitmap); anything
    // else fails loudly and names the pair (a real conversion needs
    // WIC resampling).
    private static void RewriteIdentityFormatConverter(
        ModuleDefinition module, MethodReference premulRef, MethodReference unpremulRef,
        MethodReference cvtBgraRef, MethodReference cvtPbgraRef, MethodReference cvtFloatRef)
    {
        TypeDefinition? ext = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
        if (ext is null)
            return;
        TypeReference pixelFormat = OwnedCpuRef(module, "PaintDotNet.Imaging.PixelFormat");
        MethodDefinition? equality = ResolveCpuDef(pixelFormat).Methods
            .FirstOrDefault(m => m.Name == "op_Equality" && m.Parameters.Count == 2 && m.IsStatic)
            ?? throw new InvalidOperationException("CpuPaint: PixelFormat.op_Equality missing");
        var equalityRef = new MethodReference(equality.Name, module.TypeSystem.Boolean, pixelFormat);
        foreach (var p in equality.Parameters)
            equalityRef.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
        MethodReference getPixelFormat =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "get_PixelFormat", 0);
        MethodReference notSupported = module.ImportReference(
            typeof(NotSupportedException).GetConstructor([typeof(string)])!);
        MethodReference toString = module.ImportReference(
            typeof(object).GetMethod("ToString", Type.EmptyTypes)!);
        MethodReference concat = module.ImportReference(typeof(string).GetMethod(
            "Concat", [typeof(string), typeof(string), typeof(string), typeof(string)])!);

        void EmitNamedThrow(ILProcessor il, VariableDefinition srcFmt, ParameterDefinition dstFmt)
        {
            il.Emit(OpCodes.Ldstr, "WIC conversion unavailable headless: ");
            il.Emit(OpCodes.Ldloc, srcFmt);
            il.Emit(OpCodes.Box, pixelFormat);
            il.Emit(OpCodes.Callvirt, toString);
            il.Emit(OpCodes.Ldstr, " -> ");
            il.Emit(OpCodes.Ldarg, dstFmt);
            il.Emit(OpCodes.Ldobj, pixelFormat);
            il.Emit(OpCodes.Box, pixelFormat);
            il.Emit(OpCodes.Callvirt, toString);
            il.Emit(OpCodes.Call, concat);
            il.Emit(OpCodes.Newobj, notSupported);
            il.Emit(OpCodes.Throw);
        }

        // Shared CPU-conversion references for the Bgra32<->Pbgra32
        // path: allocate a locked target, CopyPixels the source into
        // it, then premultiply (or unpremultiply) in place. The clip
        // argument is a zeroed Nullable<RectInt32> (whole bitmap);
        // locals are zero-initialized so no initobj is needed.
        TypeReference formatsElem = OwnedCpuElement(module, "PaintDotNet.Imaging", "PixelFormats");
        MethodReference Bgra32Value(string name)
        {
            MethodDefinition? def = ResolveCpuDef(formatsElem).Methods
                .FirstOrDefault(m => m.Name == name && m.Parameters.Count == 0 && m.IsStatic)
                ?? throw new InvalidOperationException($"CpuPaint: PixelFormats.{name} missing");
            return new MethodReference(def.Name, MapCpuType(module, def.ReturnType), formatsElem);
        }
        MethodReference bgra32Ref = Bgra32Value("get_Bgra32");
        MethodReference pbgra32Ref = Bgra32Value("get_Pbgra32");
        TypeReference allocatorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.BitmapAllocator");
        MethodDefinition? allocateDef = ResolveCpuDef(allocatorRef).Methods
            .FirstOrDefault(m => m.Name == "Allocate" && m.Parameters.Count == 3 && m.IsStatic)
            ?? throw new InvalidOperationException("CpuPaint: BitmapAllocator.Allocate/3 missing");
        var allocateRef = new MethodReference(allocateDef.Name,
            MapCpuType(module, allocateDef.ReturnType), allocatorRef);
        foreach (var p in allocateDef.Parameters)
            allocateRef.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
        TypeDefinition lockDef = ResolveCpuDef(
            allocateDef.ReturnType is GenericInstanceType allocGi
                ? allocGi.ElementType : allocateDef.ReturnType);
        if (!ImplementsCpu(lockDef, "PaintDotNet.Imaging.IBitmapSource"))
            throw new InvalidOperationException("CpuPaint: allocated lock is not an IBitmapSource");
        MethodReference lockBuffer =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_Buffer", 0);
        MethodReference lockStride =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_BufferStride", 0);
        MethodReference getSize =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "get_Size", 0);
        MethodReference copyPixels =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "CopyPixels", 4);
        MethodReference sizeW = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Width");
        MethodReference sizeH = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Height");
        TypeReference sizeType = LocalOrOwnedCpuRef(module, "PaintDotNet.Rendering.SizeInt32");
        TypeReference rectType = LocalOrOwnedCpuRef(module, "PaintDotNet.Rendering.RectInt32");
        var nullableClip = new GenericInstanceType(module.ImportReference(typeof(Nullable<>)));
        nullableClip.GenericArguments.Add(rectType);

        void EmitConvert(ILProcessor il, MethodDefinition method,
            ParameterDefinition sourceParam, ParameterDefinition formatParam,
            VariableDefinition sizeLoc, VariableDefinition wLoc, VariableDefinition hLoc,
            VariableDefinition dstLoc, VariableDefinition ptrLoc, VariableDefinition strideLoc,
            VariableDefinition clipLoc, MethodReference premul)
        {
            // sizeLoc already holds source.Size on entry.
            il.Emit(OpCodes.Ldloca_S, sizeLoc);
            il.Emit(OpCodes.Call, sizeW);
            il.Emit(OpCodes.Stloc, wLoc);
            il.Emit(OpCodes.Ldloca_S, sizeLoc);
            il.Emit(OpCodes.Call, sizeH);
            il.Emit(OpCodes.Stloc, hLoc);
            il.Emit(OpCodes.Ldloc, sizeLoc);
            il.Emit(OpCodes.Ldarg, formatParam);
            il.Emit(OpCodes.Ldc_I4_0); // BitmapAllocationOptions.Default
            il.Emit(OpCodes.Call, allocateRef);
            il.Emit(OpCodes.Stloc, dstLoc);
            il.Emit(OpCodes.Ldloc, dstLoc);
            il.Emit(OpCodes.Callvirt, lockBuffer);
            il.Emit(OpCodes.Stloc, ptrLoc);
            il.Emit(OpCodes.Ldloc, dstLoc);
            il.Emit(OpCodes.Callvirt, lockStride);
            il.Emit(OpCodes.Stloc, strideLoc);
            // source.CopyPixels(ptr, stride, stride*h, null-clip).
            il.Emit(OpCodes.Ldarg, sourceParam);
            il.Emit(OpCodes.Ldloc, ptrLoc);
            il.Emit(OpCodes.Ldloc, strideLoc);
            il.Emit(OpCodes.Ldloc, strideLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Ldloca_S, clipLoc);
            il.Emit(OpCodes.Callvirt, copyPixels);
            il.Emit(OpCodes.Ldloc, ptrLoc);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Ldloc, strideLoc);
            il.Emit(OpCodes.Call, premul);
        }

        MethodReference prgba128ValRef = Bgra32Value("get_Prgba128Float");
        MethodReference disposeCpu = module.ImportReference(
            typeof(IDisposable).GetMethod("Dispose", Type.EmptyTypes)!);
        foreach (MethodDefinition method in ext.Methods
            .Where(m => m.Name == "CreateFormatConvertedBitmap" && m.HasBody))
        {
            int sourceIndex = -1;
            int formatIndex = -1;
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                TypeReference pt = method.Parameters[i].ParameterType;
                if (pt.FullName == "PaintDotNet.Imaging.IBitmapSource")
                    sourceIndex = i;
                if (pt is ByReferenceType br
                    && br.ElementType.FullName == "PaintDotNet.Imaging.PixelFormat")
                    formatIndex = i;
            }
            if (sourceIndex < 0 || formatIndex < 0)
                continue;
            var il = method.Body.GetILProcessor();
            method.Body.ExceptionHandlers.Clear();
            method.Body.Variables.Clear();
            method.Body.Instructions.Clear();
            var formatLoc = new VariableDefinition(pixelFormat);
            var srcFmtAlias = formatLoc;
            var bgraVal = new VariableDefinition(pixelFormat);
            var pbgraVal = new VariableDefinition(pixelFormat);
            var dstScratch = new VariableDefinition(pixelFormat);
            var sizeLoc = new VariableDefinition(sizeType);
            var wLoc = new VariableDefinition(module.TypeSystem.Int32);
            var hLoc = new VariableDefinition(module.TypeSystem.Int32);
            var dstLoc = new VariableDefinition(MapCpuType(module, allocateDef.ReturnType));
            var ptrLoc = new VariableDefinition(module.TypeSystem.IntPtr);
            var strideLoc = new VariableDefinition(module.TypeSystem.Int32);
            var clipLoc = new VariableDefinition(nullableClip);
            var toPbgraLoc = new VariableDefinition(module.TypeSystem.Int32);
            var toBgraLoc = new VariableDefinition(module.TypeSystem.Int32);
            var prgbaVal = new VariableDefinition(pixelFormat);
            var tmpLoc = new VariableDefinition(MapCpuType(module, allocateDef.ReturnType));
            var tmpPtrLoc = new VariableDefinition(module.TypeSystem.IntPtr);
            var tmpStrideLoc = new VariableDefinition(module.TypeSystem.Int32);
            var toPbgraFloatLoc = new VariableDefinition(module.TypeSystem.Int32);
            foreach (var v in new[] { formatLoc, bgraVal, pbgraVal, dstScratch,
                sizeLoc, wLoc, hLoc, dstLoc, ptrLoc, strideLoc, clipLoc,
                toPbgraLoc, toBgraLoc, prgbaVal, tmpLoc, tmpPtrLoc, tmpStrideLoc,
                toPbgraFloatLoc })
                method.Body.Variables.Add(v);
            method.Body.InitLocals = true;
            ParameterDefinition sourceParam = method.Parameters[sourceIndex];
            ParameterDefinition formatParam = method.Parameters[formatIndex];
            il.Emit(OpCodes.Ldarg, sourceParam);
            il.Emit(OpCodes.Callvirt, getPixelFormat);
            il.Emit(OpCodes.Stloc, formatLoc);
            il.Emit(OpCodes.Ldloca_S, formatLoc);
            il.Emit(OpCodes.Ldarg, formatParam);
            il.Emit(OpCodes.Call, equalityRef);
            var convertLabel = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, convertLabel);
            il.Emit(OpCodes.Ldarg, sourceParam);
            il.Emit(OpCodes.Ret);
            il.Append(convertLabel);
            // Non-identity: static format values, then the pair checks.
            il.Emit(OpCodes.Call, bgra32Ref);
            il.Emit(OpCodes.Stloc, bgraVal);
            il.Emit(OpCodes.Call, pbgra32Ref);
            il.Emit(OpCodes.Stloc, pbgraVal);
            il.Emit(OpCodes.Call, prgba128ValRef);
            il.Emit(OpCodes.Stloc, prgbaVal);
            il.Emit(OpCodes.Ldarg, sourceParam);
            il.Emit(OpCodes.Callvirt, getSize);
            il.Emit(OpCodes.Stloc, sizeLoc);
            // toPbgra = (src == Bgra32) && (dst == Pbgra32).
            il.Emit(OpCodes.Ldloca_S, srcFmtAlias);
            il.Emit(OpCodes.Ldloca_S, bgraVal);
            il.Emit(OpCodes.Call, equalityRef);
            var pbgraFalse = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, pbgraFalse);
            il.Emit(OpCodes.Ldarg, formatParam);
            il.Emit(OpCodes.Ldobj, pixelFormat);
            il.Emit(OpCodes.Stloc, dstScratch);
            il.Emit(OpCodes.Ldloca_S, dstScratch);
            il.Emit(OpCodes.Ldloca_S, pbgraVal);
            il.Emit(OpCodes.Call, equalityRef);
            var pbgraDone = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, pbgraDone);
            il.Append(pbgraFalse);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Append(pbgraDone);
            il.Emit(OpCodes.Stloc, toPbgraLoc);
            // toBgra = (src == Pbgra32) && (dst == Bgra32).
            il.Emit(OpCodes.Ldloca_S, srcFmtAlias);
            il.Emit(OpCodes.Ldloca_S, pbgraVal);
            il.Emit(OpCodes.Call, equalityRef);
            var bgraFalse = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, bgraFalse);
            il.Emit(OpCodes.Ldarg, formatParam);
            il.Emit(OpCodes.Ldobj, pixelFormat);
            il.Emit(OpCodes.Stloc, dstScratch);
            il.Emit(OpCodes.Ldloca_S, dstScratch);
            il.Emit(OpCodes.Ldloca_S, bgraVal);
            il.Emit(OpCodes.Call, equalityRef);
            var bgraDone = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, bgraDone);
            il.Append(bgraFalse);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Append(bgraDone);
            il.Emit(OpCodes.Stloc, toBgraLoc);
            // toPbgraFloat = (src == Prgba128Float) && (dst == Pbgra32).
            il.Emit(OpCodes.Ldloca_S, srcFmtAlias);
            il.Emit(OpCodes.Ldloca_S, prgbaVal);
            il.Emit(OpCodes.Call, equalityRef);
            var floatFalse = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, floatFalse);
            il.Emit(OpCodes.Ldarg, formatParam);
            il.Emit(OpCodes.Ldobj, pixelFormat);
            il.Emit(OpCodes.Stloc, dstScratch);
            il.Emit(OpCodes.Ldloca_S, dstScratch);
            il.Emit(OpCodes.Ldloca_S, pbgraVal);
            il.Emit(OpCodes.Call, equalityRef);
            var floatDone = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, floatDone);
            il.Append(floatFalse);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Append(floatDone);
            il.Emit(OpCodes.Stloc, toPbgraFloatLoc);
            // Dispatch: premultiply, unpremultiply, float download, or a named throw.
            il.Emit(OpCodes.Ldloc, toPbgraLoc);
            var unpremulLabel = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, unpremulLabel);
            EmitConvert(il, method, sourceParam, formatParam, sizeLoc, wLoc, hLoc,
                dstLoc, ptrLoc, strideLoc, clipLoc, premulRef);
            il.Emit(OpCodes.Ldloc, dstLoc);
            il.Emit(OpCodes.Ret);
            il.Append(unpremulLabel);
            il.Emit(OpCodes.Ldloc, toBgraLoc);
            var floatLabel = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, floatLabel);
            EmitConvert(il, method, sourceParam, formatParam, sizeLoc, wLoc, hLoc,
                dstLoc, ptrLoc, strideLoc, clipLoc, unpremulRef);
            il.Emit(OpCodes.Ldloc, dstLoc);
            il.Emit(OpCodes.Ret);
            il.Append(floatLabel);
            il.Emit(OpCodes.Ldloc, toPbgraFloatLoc);
            var throwLabel = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, throwLabel);
            // Float download: fresh Pbgra32 target plus a float scratch
            // copy of the source, converted on the CPU, scratch lock
            // released, destination returned. w/h are per-arm (each
            // EmitConvert reads its own), so read them here too;
            // without this h stays 0 and CopyPixels gets size 0.
            il.Emit(OpCodes.Ldloca_S, sizeLoc);
            il.Emit(OpCodes.Call, sizeW);
            il.Emit(OpCodes.Stloc, wLoc);
            il.Emit(OpCodes.Ldloca_S, sizeLoc);
            il.Emit(OpCodes.Call, sizeH);
            il.Emit(OpCodes.Stloc, hLoc);
            il.Emit(OpCodes.Ldloc, sizeLoc);
            il.Emit(OpCodes.Ldarg, formatParam);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, allocateRef);
            il.Emit(OpCodes.Stloc, dstLoc);
            il.Emit(OpCodes.Ldloc, dstLoc);
            il.Emit(OpCodes.Callvirt, lockBuffer);
            il.Emit(OpCodes.Stloc, ptrLoc);
            il.Emit(OpCodes.Ldloc, dstLoc);
            il.Emit(OpCodes.Callvirt, lockStride);
            il.Emit(OpCodes.Stloc, strideLoc);
            il.Emit(OpCodes.Ldloc, sizeLoc);
            il.Emit(OpCodes.Ldloca_S, prgbaVal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, allocateRef);
            il.Emit(OpCodes.Stloc, tmpLoc);
            il.Emit(OpCodes.Ldloc, tmpLoc);
            il.Emit(OpCodes.Callvirt, lockBuffer);
            il.Emit(OpCodes.Stloc, tmpPtrLoc);
            il.Emit(OpCodes.Ldloc, tmpLoc);
            il.Emit(OpCodes.Callvirt, lockStride);
            il.Emit(OpCodes.Stloc, tmpStrideLoc);
            il.Emit(OpCodes.Ldarg, sourceParam);
            il.Emit(OpCodes.Ldloc, tmpPtrLoc);
            il.Emit(OpCodes.Ldloc, tmpStrideLoc);
            il.Emit(OpCodes.Ldloc, tmpStrideLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Ldloca_S, clipLoc);
            il.Emit(OpCodes.Callvirt, copyPixels);
            il.Emit(OpCodes.Ldloc, tmpPtrLoc);
            il.Emit(OpCodes.Ldloc, tmpStrideLoc);
            il.Emit(OpCodes.Ldloc, ptrLoc);
            il.Emit(OpCodes.Ldloc, strideLoc);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Call, cvtFloatRef);
            il.Emit(OpCodes.Ldloc, tmpLoc);
            il.Emit(OpCodes.Callvirt, disposeCpu);
            il.Emit(OpCodes.Ldloc, dstLoc);
            il.Emit(OpCodes.Ret);
            il.Append(throwLabel);
            EmitNamedThrow(il, formatLoc, formatParam);
        }

        // Generic dither worker CreateFormatConvertedBitmap[TPixelDst]:
        // the brush-sprite path (MipMappedDeviceBitmap.GetBitmap) lands
        // here with TPixelDst = ColorPrgba128Float. The factory is dead
        // headless, so: already-typed sources return as-is; Bgra32 and
        // Pbgra32 sources convert to Prgba128Float on the CPU; anything
        // else names its pair loudly. Dither and palette arguments are
        // ignored (float targets need no dithering), like WIC without
        // color contexts there is no gamma curve either.
        MethodReference prgba128Ref = Bgra32Value("get_Prgba128Float");
        MethodReference ipixelFormatGetter =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IPixelInfo", "get_PixelFormat", 0);
        TypeReference lockedElem =
            LocalOrOwnedCpuElement(module, "PaintDotNet.Imaging", "ILockedBitmap`1");
        MethodDefinition? gallocDef = ResolveCpuDef(allocatorRef).Methods
            .FirstOrDefault(m => m.Name == "Allocate" && m.Parameters.Count == 2
                && m.GenericParameters.Count == 1 && m.IsStatic)
            ?? throw new InvalidOperationException("CpuPaint: BitmapAllocator.Allocate<T>/2 missing");
        if (!ImplementsCpuGeneric(ResolveCpuDef(lockedElem), "PaintDotNet.Imaging.IBitmapSource`1"))
            throw new InvalidOperationException("CpuPaint: ILockedBitmap<T> is not an IBitmapSource<T>");

        void EmitNamedThrowLocal(ILProcessor il, VariableDefinition srcFmt, VariableDefinition dstFmt)
        {
            il.Emit(OpCodes.Ldstr, "WIC conversion unavailable headless: ");
            il.Emit(OpCodes.Ldloc, srcFmt);
            il.Emit(OpCodes.Box, pixelFormat);
            il.Emit(OpCodes.Callvirt, toString);
            il.Emit(OpCodes.Ldstr, " -> ");
            il.Emit(OpCodes.Ldloc, dstFmt);
            il.Emit(OpCodes.Box, pixelFormat);
            il.Emit(OpCodes.Callvirt, toString);
            il.Emit(OpCodes.Call, concat);
            il.Emit(OpCodes.Newobj, notSupported);
            il.Emit(OpCodes.Throw);
        }

        foreach (MethodDefinition method in ext.Methods
            .Where(m => m.Name == "CreateFormatConvertedBitmap" && m.HasBody
                && m.HasGenericParameters && m.GenericParameters.Count == 1
                && m.Parameters.Count == 6
                && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapSource"
                && m.ReturnType is GenericInstanceType))
        {
            GenericParameter workerGP = method.GenericParameters[0];
            // Token provenance: reuse the compiler-built generic instances
            // (the method's own return type, the allocator's declared
            // return) instead of hand-built ones over a local TypeDef.
            var typedSrc = (GenericInstanceType)method.ReturnType;
            var allocRet = (GenericInstanceType)MapCpuType(module, gallocDef.ReturnType);
            var lockedTyped = new GenericInstanceType(allocRet.ElementType);
            lockedTyped.GenericArguments.Add(workerGP);
            var gallocRef = new MethodReference(gallocDef.Name, lockedTyped, allocatorRef);
            // The open reference must declare the generic arity itself:
            // a GenericInstanceMethod over a 0-arity element never
            // resolves (live MissingMethod on the sprite path).
            gallocRef.GenericParameters.Add(new GenericParameter("TPixel", gallocRef));
            foreach (var p in gallocDef.Parameters)
                gallocRef.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
            var gallocInst = new GenericInstanceMethod(gallocRef);
            gallocInst.GenericArguments.Add(workerGP);

            var il = method.Body.GetILProcessor();
            method.Body.ExceptionHandlers.Clear();
            method.Body.Variables.Clear();
            method.Body.Instructions.Clear();
            var tSrcFmt = new VariableDefinition(pixelFormat);
            var tDstFmt = new VariableDefinition(pixelFormat);
            var tVal = new VariableDefinition(workerGP);
            var vBgra = new VariableDefinition(pixelFormat);
            var vPbgra = new VariableDefinition(pixelFormat);
            var vPrgba = new VariableDefinition(pixelFormat);
            var tSize = new VariableDefinition(sizeType);
            var tW = new VariableDefinition(module.TypeSystem.Int32);
            var tH = new VariableDefinition(module.TypeSystem.Int32);
            var tDst = new VariableDefinition(lockedTyped);
            var tTmp = new VariableDefinition(MapCpuType(module, allocateDef.ReturnType));
            var tTmpPtr = new VariableDefinition(module.TypeSystem.IntPtr);
            var tTmpStride = new VariableDefinition(module.TypeSystem.Int32);
            var tDstPtr = new VariableDefinition(module.TypeSystem.IntPtr);
            var tDstStride = new VariableDefinition(module.TypeSystem.Int32);
            var tClip = new VariableDefinition(nullableClip);
            var fDstFloat = new VariableDefinition(module.TypeSystem.Int32);
            var fSrcBgra = new VariableDefinition(module.TypeSystem.Int32);
            var fSrcPbgra = new VariableDefinition(module.TypeSystem.Int32);
            var fToFloat = new VariableDefinition(module.TypeSystem.Int32);
            foreach (var v in new[] { tSrcFmt, tDstFmt, tVal, vBgra, vPbgra, vPrgba,
                tSize, tW, tH, tDst, tTmp, tTmpPtr, tTmpStride, tDstPtr, tDstStride,
                tClip, fDstFloat, fSrcBgra, fSrcPbgra, fToFloat })
                method.Body.Variables.Add(v);
            method.Body.InitLocals = true;
            ParameterDefinition tSource = method.Parameters[1];

            // Already-typed sources need no conversion at all.
            il.Emit(OpCodes.Ldarg, tSource);
            il.Emit(OpCodes.Isinst, typedSrc);
            il.Emit(OpCodes.Dup);
            var typedRet = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brtrue, typedRet);
            il.Emit(OpCodes.Pop);
            var convertEntry = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, convertEntry);
            il.Append(typedRet);
            il.Emit(OpCodes.Ret);
            il.Append(convertEntry);

            // Source format, static formats, target format from TPixelDst
            // (same default(T)+constrained pattern as the original body).
            il.Emit(OpCodes.Ldarg, tSource);
            il.Emit(OpCodes.Callvirt, getPixelFormat);
            il.Emit(OpCodes.Stloc, tSrcFmt);
            il.Emit(OpCodes.Call, bgra32Ref);
            il.Emit(OpCodes.Stloc, vBgra);
            il.Emit(OpCodes.Call, pbgra32Ref);
            il.Emit(OpCodes.Stloc, vPbgra);
            il.Emit(OpCodes.Call, prgba128Ref);
            il.Emit(OpCodes.Stloc, vPrgba);
            il.Emit(OpCodes.Ldloca_S, tVal);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Initobj, workerGP);
            il.Emit(OpCodes.Constrained, workerGP);
            il.Emit(OpCodes.Callvirt, ipixelFormatGetter);
            il.Emit(OpCodes.Stloc, tDstFmt);

            // toFloat = (dst == Prgba128) && (src == Bgra32 || Pbgra32).
            il.Emit(OpCodes.Ldloca_S, tDstFmt);
            il.Emit(OpCodes.Ldloca_S, vPrgba);
            il.Emit(OpCodes.Call, equalityRef);
            il.Emit(OpCodes.Stloc, fDstFloat);
            il.Emit(OpCodes.Ldloca_S, tSrcFmt);
            il.Emit(OpCodes.Ldloca_S, vBgra);
            il.Emit(OpCodes.Call, equalityRef);
            il.Emit(OpCodes.Stloc, fSrcBgra);
            il.Emit(OpCodes.Ldloca_S, tSrcFmt);
            il.Emit(OpCodes.Ldloca_S, vPbgra);
            il.Emit(OpCodes.Call, equalityRef);
            il.Emit(OpCodes.Stloc, fSrcPbgra);
            il.Emit(OpCodes.Ldloc, fDstFloat);
            il.Emit(OpCodes.Ldloc, fSrcBgra);
            il.Emit(OpCodes.Ldloc, fSrcPbgra);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, fToFloat);
            // Unconvertible pairs throw inline here so no branch spans the
            // conversion body: Cecil never expands short branches, and a
            // span over 127 bytes silently writes a bad target
            // (InvalidProgram at JIT). All emitted branches use the long
            // form for the same reason.
            il.Emit(OpCodes.Ldloc, fToFloat);
            var tConvertBody = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brtrue, tConvertBody);
            EmitNamedThrowLocal(il, tSrcFmt, tDstFmt);
            il.Append(tConvertBody);

            // Staged conversion: source bytes into a Bgra32 lock,
            // premultiplied when straight, then scaled to float.
            il.Emit(OpCodes.Ldarg, tSource);
            il.Emit(OpCodes.Callvirt, getSize);
            il.Emit(OpCodes.Stloc, tSize);
            il.Emit(OpCodes.Ldloca_S, tSize);
            il.Emit(OpCodes.Call, sizeW);
            il.Emit(OpCodes.Stloc, tW);
            il.Emit(OpCodes.Ldloca_S, tSize);
            il.Emit(OpCodes.Call, sizeH);
            il.Emit(OpCodes.Stloc, tH);
            il.Emit(OpCodes.Ldloc, tSize);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, gallocInst);
            il.Emit(OpCodes.Stloc, tDst);
            il.Emit(OpCodes.Ldloc, tSize);
            il.Emit(OpCodes.Ldloca_S, vBgra);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, allocateRef);
            il.Emit(OpCodes.Stloc, tTmp);
            il.Emit(OpCodes.Ldloc, tTmp);
            il.Emit(OpCodes.Callvirt, lockBuffer);
            il.Emit(OpCodes.Stloc, tTmpPtr);
            il.Emit(OpCodes.Ldloc, tTmp);
            il.Emit(OpCodes.Callvirt, lockStride);
            il.Emit(OpCodes.Stloc, tTmpStride);
            il.Emit(OpCodes.Ldarg, tSource);
            il.Emit(OpCodes.Ldloc, tTmpPtr);
            il.Emit(OpCodes.Ldloc, tTmpStride);
            il.Emit(OpCodes.Ldloc, tTmpStride);
            il.Emit(OpCodes.Ldloc, tH);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Ldloca_S, tClip);
            il.Emit(OpCodes.Callvirt, copyPixels);
            il.Emit(OpCodes.Ldloc, fSrcBgra);
            var tSkipPremul = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, tSkipPremul);
            il.Emit(OpCodes.Ldloc, tTmpPtr);
            il.Emit(OpCodes.Ldloc, tW);
            il.Emit(OpCodes.Ldloc, tH);
            il.Emit(OpCodes.Ldloc, tTmpStride);
            il.Emit(OpCodes.Call, premulRef);
            il.Append(tSkipPremul);
            il.Emit(OpCodes.Ldloc, tDst);
            il.Emit(OpCodes.Callvirt, lockBuffer);
            il.Emit(OpCodes.Stloc, tDstPtr);
            il.Emit(OpCodes.Ldloc, tDst);
            il.Emit(OpCodes.Callvirt, lockStride);
            il.Emit(OpCodes.Stloc, tDstStride);
            il.Emit(OpCodes.Ldloc, fSrcBgra);
            var tUsePbgra = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, tUsePbgra);
            il.Emit(OpCodes.Ldloc, tTmpPtr);
            il.Emit(OpCodes.Ldloc, tTmpStride);
            il.Emit(OpCodes.Ldloc, tDstPtr);
            il.Emit(OpCodes.Ldloc, tDstStride);
            il.Emit(OpCodes.Ldloc, tW);
            il.Emit(OpCodes.Ldloc, tH);
            il.Emit(OpCodes.Call, cvtBgraRef);
            var tConvertDone = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, tConvertDone);
            il.Append(tUsePbgra);
            il.Emit(OpCodes.Ldloc, tTmpPtr);
            il.Emit(OpCodes.Ldloc, tTmpStride);
            il.Emit(OpCodes.Ldloc, tDstPtr);
            il.Emit(OpCodes.Ldloc, tDstStride);
            il.Emit(OpCodes.Ldloc, tW);
            il.Emit(OpCodes.Ldloc, tH);
            il.Emit(OpCodes.Call, cvtPbgraRef);
            il.Append(tConvertDone);
            il.Emit(OpCodes.Ldloc, tDst);
            il.Emit(OpCodes.Ret);
        }
    }

    // Headless color-transform generics: BitmapSourceExtensions +
    // ImagingFactoryExtensions generic CreateColorTransformer /
    // CreateColorTransformedBitmap answer IBitmapSource<TPixel> but the
    // WIC factory is null headless, and the blind identity shortcut
    // returns the untyped source so the wrapper's cast to Half faults
    // (checkbox glyphs, every render). Like the float format rule:
    // already-typed sources return as-is; Bgra32/Pbgra32 sources
    // convert to Half (or Float) on the CPU; same-format wrappers
    // cast; anything else names its pair loudly. Runs after the
    // identity shortcuts above, overwriting their bodies.
    private static void RewriteHalfColorTransformer(
        ModuleDefinition module,
        MethodReference premulRef,
        MethodReference cvtHalfBgraRef, MethodReference cvtHalfPbgraRef,
        MethodReference cvtFloatBgraRef, MethodReference cvtFloatPbgraRef)
    {
        TypeDefinition? bse = module.GetType("PaintDotNet.Imaging.BitmapSourceExtensions");
        TypeDefinition? ife = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
        if (bse is null || ife is null)
            return;
        TypeReference pixelFormat = OwnedCpuRef(module, "PaintDotNet.Imaging.PixelFormat");
        MethodDefinition? equality = ResolveCpuDef(pixelFormat).Methods
            .FirstOrDefault(m => m.Name == "op_Equality" && m.Parameters.Count == 2 && m.IsStatic)
            ?? throw new InvalidOperationException("CpuPaint: PixelFormat.op_Equality missing");
        var equalityRef = new MethodReference(equality.Name, module.TypeSystem.Boolean, pixelFormat);
        foreach (var p in equality.Parameters)
            equalityRef.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
        MethodReference getPixelFormat =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "get_PixelFormat", 0);
        MethodReference ipixelFormatGetter =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IPixelInfo", "get_PixelFormat", 0);
        MethodReference notSupported = module.ImportReference(
            typeof(NotSupportedException).GetConstructor([typeof(string)])!);
        MethodReference toString = module.ImportReference(
            typeof(object).GetMethod("ToString", Type.EmptyTypes)!);
        MethodReference concat = module.ImportReference(typeof(string).GetMethod(
            "Concat", [typeof(string), typeof(string), typeof(string), typeof(string)])!);
        MethodReference getTypeFromHandle = module.ImportReference(
            typeof(Type).GetMethod("GetTypeFromHandle", [typeof(RuntimeTypeHandle)])!);
        MethodReference getFullName = module.ImportReference(
            typeof(Type).GetProperty("FullName")!.GetGetMethod()!);
        MethodReference contains = module.ImportReference(
            typeof(string).GetMethod("Contains", [typeof(string)])!);
        TypeReference formatsElem = OwnedCpuElement(module, "PaintDotNet.Imaging", "PixelFormats");
        MethodReference HalfFormat(string name)
        {
            MethodDefinition? def = ResolveCpuDef(formatsElem).Methods
                .FirstOrDefault(m => m.Name == name && m.Parameters.Count == 0 && m.IsStatic)
                ?? throw new InvalidOperationException($"CpuPaint: PixelFormats.{name} missing");
            return new MethodReference(def.Name, MapCpuType(module, def.ReturnType), formatsElem);
        }
        MethodReference bgra32Ref = HalfFormat("get_Bgra32");
        MethodReference pbgra32Ref = HalfFormat("get_Pbgra32");
        TypeReference allocatorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.BitmapAllocator");
        MethodDefinition? allocateDef = ResolveCpuDef(allocatorRef).Methods
            .FirstOrDefault(m => m.Name == "Allocate" && m.Parameters.Count == 3 && m.IsStatic)
            ?? throw new InvalidOperationException("CpuPaint: BitmapAllocator.Allocate/3 missing");
        var allocateRef = new MethodReference(allocateDef.Name,
            MapCpuType(module, allocateDef.ReturnType), allocatorRef);
        foreach (var p in allocateDef.Parameters)
            allocateRef.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
        MethodDefinition? gallocDef = ResolveCpuDef(allocatorRef).Methods
            .FirstOrDefault(m => m.Name == "Allocate" && m.Parameters.Count == 2
                && m.GenericParameters.Count == 1 && m.IsStatic)
            ?? throw new InvalidOperationException("CpuPaint: BitmapAllocator.Allocate<T>/2 missing");
        TypeReference lockedElem =
            LocalOrOwnedCpuElement(module, "PaintDotNet.Imaging", "ILockedBitmap`1");
        if (!ImplementsCpuGeneric(ResolveCpuDef(lockedElem), "PaintDotNet.Imaging.IBitmapSource`1"))
            throw new InvalidOperationException("CpuPaint: ILockedBitmap<T> is not an IBitmapSource<T>");
        MethodReference lockBuffer =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_Buffer", 0);
        MethodReference lockStride =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_BufferStride", 0);
        MethodReference getSize =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "get_Size", 0);
        MethodReference copyPixels =
            VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "CopyPixels", 4);
        MethodReference sizeW = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Width");
        MethodReference sizeH = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Height");
        TypeReference sizeType = LocalOrOwnedCpuRef(module, "PaintDotNet.Rendering.SizeInt32");
        TypeReference rectType = LocalOrOwnedCpuRef(module, "PaintDotNet.Rendering.RectInt32");
        var nullableClip = new GenericInstanceType(module.ImportReference(typeof(Nullable<>)));
        nullableClip.GenericArguments.Add(rectType);

        void EmitNamedThrowH(ILProcessor il, VariableDefinition srcFmt, VariableDefinition dstFmt)
        {
            il.Emit(OpCodes.Ldstr, "WIC color transform unavailable headless: ");
            il.Emit(OpCodes.Ldloc, srcFmt);
            il.Emit(OpCodes.Box, pixelFormat);
            il.Emit(OpCodes.Callvirt, toString);
            il.Emit(OpCodes.Ldstr, " -> ");
            il.Emit(OpCodes.Ldloc, dstFmt);
            il.Emit(OpCodes.Box, pixelFormat);
            il.Emit(OpCodes.Callvirt, toString);
            il.Emit(OpCodes.Call, concat);
            il.Emit(OpCodes.Newobj, notSupported);
            il.Emit(OpCodes.Throw);
        }

        void RewriteOne(MethodDefinition method, int sourceIndex)
        {
            GenericParameter workerGP = method.GenericParameters[0];
            var typedRet = (GenericInstanceType)method.ReturnType;
            var allocRet = (GenericInstanceType)MapCpuType(module, gallocDef.ReturnType);
            var lockedTyped = new GenericInstanceType(allocRet.ElementType);
            lockedTyped.GenericArguments.Add(workerGP);
            var gallocRef = new MethodReference(gallocDef.Name, lockedTyped, allocatorRef);
            gallocRef.GenericParameters.Add(new GenericParameter("TPixel", gallocRef));
            foreach (var p in gallocDef.Parameters)
                gallocRef.Parameters.Add(new ParameterDefinition(MapCpuType(module, p.ParameterType)));
            var gallocInst = new GenericInstanceMethod(gallocRef);
            gallocInst.GenericArguments.Add(workerGP);

            var il = method.Body.GetILProcessor();
            method.Body.ExceptionHandlers.Clear();
            method.Body.Variables.Clear();
            method.Body.Instructions.Clear();
            var tSrcFmt = new VariableDefinition(pixelFormat);
            var tDstFmt = new VariableDefinition(pixelFormat);
            var tVal = new VariableDefinition(workerGP);
            var tTypeName = new VariableDefinition(module.TypeSystem.String);
            var vBgra = new VariableDefinition(pixelFormat);
            var vPbgra = new VariableDefinition(pixelFormat);
            var tSize = new VariableDefinition(sizeType);
            var tW = new VariableDefinition(module.TypeSystem.Int32);
            var tH = new VariableDefinition(module.TypeSystem.Int32);
            var tDst = new VariableDefinition(lockedTyped);
            var tTmp = new VariableDefinition(MapCpuType(module, allocateDef.ReturnType));
            var tTmpPtr = new VariableDefinition(module.TypeSystem.IntPtr);
            var tTmpStride = new VariableDefinition(module.TypeSystem.Int32);
            var tDstPtr = new VariableDefinition(module.TypeSystem.IntPtr);
            var tDstStride = new VariableDefinition(module.TypeSystem.Int32);
            var tClip = new VariableDefinition(nullableClip);
            var fHalf = new VariableDefinition(module.TypeSystem.Int32);
            var fFloat = new VariableDefinition(module.TypeSystem.Int32);
            var fSrcBgra = new VariableDefinition(module.TypeSystem.Int32);
            var fSrcPbgra = new VariableDefinition(module.TypeSystem.Int32);
            var fConvert = new VariableDefinition(module.TypeSystem.Int32);
            foreach (var v in new[] { tSrcFmt, tDstFmt, tVal, tTypeName, vBgra, vPbgra,
                tSize, tW, tH, tDst, tTmp, tTmpPtr, tTmpStride, tDstPtr, tDstStride,
                tClip, fHalf, fFloat, fSrcBgra, fSrcPbgra, fConvert })
                method.Body.Variables.Add(v);
            method.Body.InitLocals = true;
            if (method.Body.MaxStackSize < 8)
                method.Body.MaxStackSize = 8;
            ParameterDefinition tSource = method.Parameters[sourceIndex];

            // Already-typed sources need no conversion at all.
            il.Emit(OpCodes.Ldarg, tSource);
            il.Emit(OpCodes.Isinst, typedRet);
            il.Emit(OpCodes.Dup);
            var typedRetLabel = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brtrue, typedRetLabel);
            il.Emit(OpCodes.Pop);
            var convertEntry = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, convertEntry);
            il.Append(typedRetLabel);
            il.Emit(OpCodes.Ret);
            il.Append(convertEntry);
            // Destination pixel name (Half/Float dispatch) + formats.
            il.Emit(OpCodes.Ldtoken, workerGP);
            il.Emit(OpCodes.Call, getTypeFromHandle);
            il.Emit(OpCodes.Callvirt, getFullName);
            il.Emit(OpCodes.Stloc, tTypeName);
            il.Emit(OpCodes.Ldloc, tTypeName);
            il.Emit(OpCodes.Ldstr, "Prgba64Half");
            il.Emit(OpCodes.Callvirt, contains);
            il.Emit(OpCodes.Stloc, fHalf);
            il.Emit(OpCodes.Ldloc, tTypeName);
            il.Emit(OpCodes.Ldstr, "Prgba128Float");
            il.Emit(OpCodes.Callvirt, contains);
            il.Emit(OpCodes.Stloc, fFloat);
            il.Emit(OpCodes.Ldarg, tSource);
            il.Emit(OpCodes.Callvirt, getPixelFormat);
            il.Emit(OpCodes.Stloc, tSrcFmt);
            il.Emit(OpCodes.Call, bgra32Ref);
            il.Emit(OpCodes.Stloc, vBgra);
            il.Emit(OpCodes.Call, pbgra32Ref);
            il.Emit(OpCodes.Stloc, vPbgra);
            il.Emit(OpCodes.Ldloca_S, tVal);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Initobj, workerGP);
            il.Emit(OpCodes.Constrained, workerGP);
            il.Emit(OpCodes.Callvirt, ipixelFormatGetter);
            il.Emit(OpCodes.Stloc, tDstFmt);
            il.Emit(OpCodes.Ldloca_S, tSrcFmt);
            il.Emit(OpCodes.Ldloca_S, vBgra);
            il.Emit(OpCodes.Call, equalityRef);
            il.Emit(OpCodes.Stloc, fSrcBgra);
            il.Emit(OpCodes.Ldloca_S, tSrcFmt);
            il.Emit(OpCodes.Ldloca_S, vPbgra);
            il.Emit(OpCodes.Call, equalityRef);
            il.Emit(OpCodes.Stloc, fSrcPbgra);
            il.Emit(OpCodes.Ldloc, fHalf);
            il.Emit(OpCodes.Ldloc, fFloat);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Ldloc, fSrcBgra);
            il.Emit(OpCodes.Ldloc, fSrcPbgra);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, fConvert);
            il.Emit(OpCodes.Ldloc, fConvert);
            var convertBody = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brtrue, convertBody);
            // Same-format wrappers keep original semantics (cast).
            il.Emit(OpCodes.Ldloca_S, tSrcFmt);
            il.Emit(OpCodes.Ldloca_S, tDstFmt);
            il.Emit(OpCodes.Call, equalityRef);
            var sameFormat = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brtrue, sameFormat);
            EmitNamedThrowH(il, tSrcFmt, tDstFmt);
            il.Append(sameFormat);
            il.Emit(OpCodes.Ldarg, tSource);
            il.Emit(OpCodes.Castclass, typedRet);
            il.Emit(OpCodes.Ret);
            il.Append(convertBody);

            // Staged conversion via a Bgra32 lock, premultiplied when
            // straight, then widened to the Half/Float target.
            il.Emit(OpCodes.Ldarg, tSource);
            il.Emit(OpCodes.Callvirt, getSize);
            il.Emit(OpCodes.Stloc, tSize);
            il.Emit(OpCodes.Ldloca_S, tSize);
            il.Emit(OpCodes.Call, sizeW);
            il.Emit(OpCodes.Stloc, tW);
            il.Emit(OpCodes.Ldloca_S, tSize);
            il.Emit(OpCodes.Call, sizeH);
            il.Emit(OpCodes.Stloc, tH);
            il.Emit(OpCodes.Ldloc, tSize);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, gallocInst);
            il.Emit(OpCodes.Stloc, tDst);
            il.Emit(OpCodes.Ldloc, tSize);
            il.Emit(OpCodes.Ldloca_S, vBgra);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, allocateRef);
            il.Emit(OpCodes.Stloc, tTmp);
            il.Emit(OpCodes.Ldloc, tTmp);
            il.Emit(OpCodes.Callvirt, lockBuffer);
            il.Emit(OpCodes.Stloc, tTmpPtr);
            il.Emit(OpCodes.Ldloc, tTmp);
            il.Emit(OpCodes.Callvirt, lockStride);
            il.Emit(OpCodes.Stloc, tTmpStride);
            il.Emit(OpCodes.Ldarg, tSource);
            il.Emit(OpCodes.Ldloc, tTmpPtr);
            il.Emit(OpCodes.Ldloc, tTmpStride);
            il.Emit(OpCodes.Ldloc, tTmpStride);
            il.Emit(OpCodes.Ldloc, tH);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Ldloca_S, tClip);
            il.Emit(OpCodes.Callvirt, copyPixels);
            il.Emit(OpCodes.Ldloc, fSrcBgra);
            var skipPremul = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, skipPremul);
            il.Emit(OpCodes.Ldloc, tTmpPtr);
            il.Emit(OpCodes.Ldloc, tW);
            il.Emit(OpCodes.Ldloc, tH);
            il.Emit(OpCodes.Ldloc, tTmpStride);
            il.Emit(OpCodes.Call, premulRef);
            il.Append(skipPremul);
            il.Emit(OpCodes.Ldloc, tDst);
            il.Emit(OpCodes.Callvirt, lockBuffer);
            il.Emit(OpCodes.Stloc, tDstPtr);
            il.Emit(OpCodes.Ldloc, tDst);
            il.Emit(OpCodes.Callvirt, lockStride);
            il.Emit(OpCodes.Stloc, tDstStride);
            // Half vs Float target, Bgra vs Pbgra source.
            il.Emit(OpCodes.Ldloc, tTmpPtr);
            il.Emit(OpCodes.Ldloc, tTmpStride);
            il.Emit(OpCodes.Ldloc, tDstPtr);
            il.Emit(OpCodes.Ldloc, tDstStride);
            il.Emit(OpCodes.Ldloc, tW);
            il.Emit(OpCodes.Ldloc, tH);
            il.Emit(OpCodes.Ldloc, fHalf);
            var useFloat = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, useFloat);
            il.Emit(OpCodes.Ldloc, fSrcBgra);
            var useHalfPbgra = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, useHalfPbgra);
            il.Emit(OpCodes.Call, cvtHalfBgraRef);
            var convertDone = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, convertDone);
            il.Append(useHalfPbgra);
            il.Emit(OpCodes.Call, cvtHalfPbgraRef);
            il.Emit(OpCodes.Br, convertDone);
            il.Append(useFloat);
            il.Emit(OpCodes.Ldloc, fSrcBgra);
            var useFloatPbgra = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, useFloatPbgra);
            il.Emit(OpCodes.Call, cvtFloatBgraRef);
            il.Emit(OpCodes.Br, convertDone);
            il.Append(useFloatPbgra);
            il.Emit(OpCodes.Call, cvtFloatPbgraRef);
            il.Append(convertDone);
            il.Emit(OpCodes.Ldloc, tDst);
            il.Emit(OpCodes.Ret);
        }

        foreach (MethodDefinition method in bse.Methods
            .Where(m => m.Name == "CreateColorTransformer" && m.HasBody
                && m.HasGenericParameters && m.GenericParameters.Count == 1
                && m.Parameters.Count == 3
                && m.Parameters[0].ParameterType is GenericInstanceType p0
                && p0.ElementType.FullName == "PaintDotNet.Imaging.IBitmapSource`1"
                && m.ReturnType is GenericInstanceType))
            RewriteOne(method, 0);

        foreach (MethodDefinition method in ife.Methods
            .Where(m => m.Name == "CreateColorTransformedBitmap" && m.HasBody
                && m.HasGenericParameters && m.GenericParameters.Count == 1
                && m.Parameters.Count == 4
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IImagingFactory"
                && (m.Parameters[2].ParameterType.FullName == "PaintDotNet.Imaging.IColorProfile"
                    || m.Parameters[2].ParameterType.FullName == "PaintDotNet.Imaging.IColorContext")
                && m.ReturnType is GenericInstanceType))
            RewriteOne(method, 1);
    }
    // Headless animation services: the COM UIAnimation factories have
    // no WASM backend, so AnimationService..ctor dies in NotNull at boot
    // (caught, but the service stays dead and every boot logs it).
    // Callers already degrade through InteropErrorInfo (e.g.
    // TryEnsureAnimationVariableIsCreated returns the error when the out
    // is null), so every Try* answers E_NOTIMPL and anything else stays
    // loud, except a tiny allowlist of benign getters (Status = Idle).
    private static TypeDefinition SynthesizeHeadlessAnimationService(
        ModuleDefinition module,
        string ifaceFullName,
        string typeName,
        IReadOnlyDictionary<string, int>? constGetters)
    {
        TypeReference iface = LocalOrOwnedCpuRef(module, ifaceFullName);
        TypeDefinition ifaceDef = ResolveCpuDef(iface);
        var type = new TypeDefinition(
            "PaintDotNet.Animation", typeName,
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        type.Interfaces.Add(new InterfaceImplementation(iface));
        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        var cc = ctor.Body.GetILProcessor();
        cc.Emit(OpCodes.Ldarg_0);
        cc.Emit(OpCodes.Call, objectCtor);
        cc.Emit(OpCodes.Ret);
        ctor.Body.MaxStackSize = 32;
        type.Methods.Add(ctor);
        MethodDefinition? errConv = null;
        foreach (MethodDefinition contract in InterfaceCpuMethods(ifaceDef))
        {
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuTypeLenient(module, contract.ReturnType));
            foreach (var p in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, MapCpuTypeLenient(module, p.ParameterType)));
            var body = impl.Body.GetILProcessor();
            if (contract.Name == "Dispose" && contract.Parameters.Count == 0)
            {
                body.Emit(OpCodes.Ret);
            }
            else if (constGetters != null
                && contract.Parameters.Count == 0
                && constGetters.TryGetValue(contract.Name, out int constVal))
            {
                body.Emit(OpCodes.Ldc_I4, constVal);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.ReturnType.FullName == "PaintDotNet.Interop.InteropErrorInfo")
            {
                for (int pi = 0; pi < contract.Parameters.Count; pi++)
                {
                    var pt = contract.Parameters[pi].ParameterType;
                    if (!pt.IsByReference)
                        continue;
                    var elem = MapCpuTypeLenient(module, ((ByReferenceType)pt).ElementType);
                    body.Emit(OpCodes.Ldarg, impl.Parameters[pi]);
                    if (elem.Resolve()?.IsValueType == true)
                        body.Emit(OpCodes.Initobj, elem);
                    else
                    {
                        body.Emit(OpCodes.Ldnull);
                        body.Emit(OpCodes.Stind_Ref);
                    }
                }
                errConv ??= module.GetType("PaintDotNet.Interop.InteropErrorInfo").Methods
                    .First(m => m.Name == "op_Implicit"
                        && m.Parameters.Count == 1
                        && m.Parameters[0].ParameterType.FullName == "System.Int32");
                body.Emit(OpCodes.Ldc_I4, unchecked((int)0x80004001));
                body.Emit(OpCodes.Call, errConv);
                body.Emit(OpCodes.Ret);
            }
            else if (contract.ReturnType.FullName == "System.Void")
            {
                body.Emit(OpCodes.Ret);
            }
            else if (contract.ReturnType.Resolve()?.IsValueType == true)
            {
                var zero = new VariableDefinition(MapCpuTypeLenient(module, contract.ReturnType));
                impl.Body.Variables.Add(zero);
                impl.Body.InitLocals = true;
                body.Emit(OpCodes.Ldloca_S, zero);
                body.Emit(OpCodes.Initobj, MapCpuTypeLenient(module, contract.ReturnType));
                body.Emit(OpCodes.Ldloc, zero);
                body.Emit(OpCodes.Ret);
            }
            else
            {
                body.Emit(OpCodes.Ldstr, typeName + "." + contract.Name + " has no headless backend");
                body.Emit(OpCodes.Newobj, module.ImportReference(typeof(NotSupportedException).GetConstructor([typeof(string)])!));
                body.Emit(OpCodes.Throw);
            }
            impl.Body.MaxStackSize = 32;
            type.Methods.Add(impl);
        }
        AddCpuObjectRef(type, module);
        module.Types.Add(type);
        return type;
    }

    // Rewrites the four UIA COM factories to return the headless
    // services above. Silent only for modules without PDN animation.
    private static bool EnsureHeadlessAnimation(ModuleDefinition module)
    {
        TypeDefinition? uia = module.GetType("PaintDotNet.Animation.UIA");
        if (uia is null)
            return false;
        (string Method, string Iface, string Cpu, IReadOnlyDictionary<string, int>? Consts)[] kinds =
        [
            ("CreateAnimationManager", "PaintDotNet.Animation.IAnimationManager", "CpuAnimationManager",
                new Dictionary<string, int>(StringComparer.Ordinal) { ["get_Status"] = 0 }),
            ("CreateAnimationTimer", "PaintDotNet.Animation.IAnimationTimer", "CpuAnimationTimer", null),
            ("CreateAnimationTransitionFactory", "PaintDotNet.Animation.IAnimationTransitionFactory", "CpuAnimationTransitionFactory", null),
            ("CreateAnimationTransitionLibrary", "PaintDotNet.Animation.IAnimationTransitionLibrary", "CpuAnimationTransitionLibrary", null),
        ];
        var ctors = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);
        foreach (var k in kinds)
            ctors[k.Method] = SynthesizeHeadlessAnimationService(module, k.Iface, k.Cpu, k.Consts)
                .Methods.First(m => m.Name == ".ctor");
        foreach (var k in kinds)
        {
            MethodDefinition? factory = uia.Methods
                .FirstOrDefault(m => m.Name == k.Method && m.Parameters.Count == 0);
            if (factory is null || !factory.HasBody)
                throw new InvalidOperationException($"Animation: UIA.{k.Method} missing");
            factory.Body.Instructions.Clear();
            factory.Body.ExceptionHandlers.Clear();
            factory.Body.Variables.Clear();
            var il = factory.Body.GetILProcessor();
            il.Emit(OpCodes.Newobj, ctors[k.Method]);
            il.Emit(OpCodes.Ret);
            factory.Body.MaxStackSize = 32;
        }
        return true;
    }
    private static bool EnsureCpuDrawingContext(ModuleDefinition module, string cpuPaintPath)
    {
        TypeDefinition? drawCtx = module.GetType("PaintDotNet.Direct2D1.DrawingContext");
        if (drawCtx is null)
            return false;
        if (module.GetType("PaintDotNet.Direct2D1.CpuDrawingContext") is not null)
            return true; // Already processed: converter rewritten too.

        AssemblyDefinition cpuAsm;
        try
        {
            cpuAsm = AssemblyDefinition.ReadAssembly(cpuPaintPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"CpuPaint: cannot read {cpuPaintPath}", ex);
        }
        TypeDefinition? cpuPaint = cpuAsm.MainModule.GetType("WfWasm.PdnCpu.CpuPaint")
            ?? throw new InvalidOperationException(
                $"CpuPaint: WfWasm.PdnCpu.CpuPaint missing in {cpuPaintPath}");
        if (module.AssemblyReferences.All(r => r.Name != cpuAsm.Name.Name))
            module.AssemblyReferences.Add(new AssemblyNameReference(cpuAsm.Name.Name, cpuAsm.Name.Version));
        MethodReference Cpu(string name, int argc)
        {
            MethodDefinition? def = cpuPaint.Methods
                .FirstOrDefault(m => m.Name == name && m.Parameters.Count == argc)
                ?? throw new InvalidOperationException($"CpuPaint: CpuPaint.{name}/{argc} missing");
            return module.ImportReference(def);
        }
        MethodReference createTarget = Cpu("CreateTarget", 5);
        MethodReference freeContext = Cpu("FreeContext", 1);
        MethodReference setTransformCpu = Cpu("SetTransform", 7);
        MethodReference createBitmapCpu = Cpu("CreateBitmap", 4);
        MethodReference uploadHalfShared = Cpu("UploadHalfSharedBitmap", 4);
        MethodReference createBitmapEmpty = Cpu("CreateBitmapEmpty", 2);
        MethodReference freeBitmap = Cpu("FreeBitmap", 1);
        MethodReference lockBitmap = Cpu("LockBitmap", 1);
        MethodReference unlockBitmap = Cpu("UnlockBitmap", 1);
        MethodReference drawBitmapCpu = Cpu("DrawBitmap", 14);
        MethodReference fillRectCpu = Cpu("FillRect", 10);
        MethodReference fillEllipseCpu = Cpu("FillEllipse", 10);
        MethodReference createScratchCpu = Cpu("CreateScratchTarget", 2);
        MethodReference blitScratchCpu = Cpu("BlitScratchOver", 2);
        MethodReference freeScratchCpu = Cpu("FreeScratchTarget", 1);
        MethodReference getTransformCpu = Cpu("GetTransform", 7);
        MethodReference getTargetSizeRedirectCpu = Cpu("GetTargetSize", 3);
        RewriteIdentityFormatConverter(
            module,
            Cpu("PremultiplyInPlace", 4),
            Cpu("UnpremultiplyInPlace", 4),
            Cpu("ConvertBgra32ToPrgba128Float", 6),
            Cpu("ConvertPbgra32ToPrgba128Float", 6),
            Cpu("ConvertPrgba128FloatToPbgra32", 6));
        RewriteHalfColorTransformer(
            module,
            Cpu("PremultiplyInPlace", 4),
            Cpu("ConvertBgra32ToPrgba64Half", 6),
            Cpu("ConvertPbgra32ToPrgba64Half", 6),
            Cpu("ConvertBgra32ToPrgba128Float", 6),
            Cpu("ConvertPbgra32ToPrgba128Float", 6));
        MethodReference objectCtor =
            module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);

        // CPU COM stand-in for BitmapImage/BufferedImage ctors (sibling to
        // the bitmap: same module, same lifetime rules).
        SynthesizeCpuComObjectRef(module);
        TypeDefinition cpuBmp = SynthesizeCpuDeviceBitmap(module, createBitmapEmpty, freeBitmap);
        TypeDefinition cpuBrush = SynthesizeCpuDeviceBrush(module);
        TypeDefinition cpuGradient = SynthesizeCpuLinearGradientBrush(module);
        TypeDefinition cpuCmdList = SynthesizeCpuCommandList(module, freeScratchCpu);
        MethodDefinition cmdListCtor = cpuCmdList.Methods.First(m => m.Name == ".ctor");
        FieldDefinition bmpHandleField = cpuBmp.Fields.First(f => f.Name == "_handle");
        FieldDefinition bmpWidthField = cpuBmp.Fields.First(f => f.Name == "_width");
        FieldDefinition bmpHeightField = cpuBmp.Fields.First(f => f.Name == "_height");
        MethodDefinition bmpCtor = cpuBmp.Methods.First(m => m.Name == ".ctor");
        FieldDefinition brushOpacityField = cpuBrush.Fields.First(f => f.Name == "_opacity");
        FieldDefinition brushRField = cpuBrush.Fields.First(f => f.Name == "_r");
        FieldDefinition brushGField = cpuBrush.Fields.First(f => f.Name == "_g");
        FieldDefinition brushBField = cpuBrush.Fields.First(f => f.Name == "_b");
        FieldDefinition brushAField = cpuBrush.Fields.First(f => f.Name == "_a");
        MethodDefinition brushCtor = cpuBrush.Methods.First(m => m.Name == ".ctor");
        TypeDefinition cpuTextLayout = SynthesizeCpuTextLayout(module);
        MethodDefinition textLayoutCtor = cpuTextLayout.Methods.First(m => m.Name == ".ctor");
        TypeDefinition cpuColorCtx = SynthesizeCpuDeviceColorContext(module);
        TypeDefinition cpuEffect = SynthesizeCpuDeviceEffect(module, cpuPaintPath);
        MethodDefinition cpuEffectCtor = cpuEffect.Methods.First(m => m.Name == ".ctor");
        MethodDefinition cpuGradientCtor = cpuGradient.Methods.First(m => m.Name == ".ctor");

        // CpuDrawingContext implements IDrawingContext over an opaque CPU
        // target: subclassing is impossible because DrawingContext is sealed
        // (an illegal subclass never legally loads; the WASM interpreter
        // merely tolerated it while device dispatch silently swallowed every
        // stamp). Stamp-path members get real CPU bodies below; the rest
        // answer device-neutral defaults. Mirrors SynthesizeCpuDeviceBitmap.
        TypeReference idrawingRef =
            LocalOrOwnedCpuRef(module, "PaintDotNet.Direct2D1.IDrawingContext");
        TypeDefinition idrawingDef = ResolveCpuDef(idrawingRef);
        // IDeviceContext (BeginDraw/FromBitmap pipelines) + the resource-cache
        // holder the DrawingContext ctor demands (GetResourceCache(InnerRef)).
        TypeReference ideviceRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Direct2D1.IDeviceContext");
        TypeDefinition ideviceDef = ResolveCpuDef(ideviceRef);
        TypeReference iresHolderRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Direct2D1.IResourceCacheHolder");
        TypeDefinition iresHolderDef = ResolveCpuDef(iresHolderRef);
        TypeReference icompRef =
            LocalOrOwnedCpuRef(module, "PaintDotNet.Direct2D1.ICompatibleDeviceContext");
        TypeDefinition icompDef = ResolveCpuDef(icompRef);
        // RefTrackedObject base: managed, parameterless, no abstract members.
        TypeReference refTrackedRef = LocalOrOwnedCpuRef(module, "PaintDotNet.ComponentModel.RefTrackedObject");
        ResolveCpuDef(refTrackedRef);
        MethodReference refTrackedCtor = CtorCpuRef(module, "PaintDotNet.ComponentModel.RefTrackedObject", 0);
        var ctx = new TypeDefinition(
            "PaintDotNet.Direct2D1", "CpuDrawingContext",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            refTrackedRef);
        ctx.Interfaces.Add(new InterfaceImplementation(idrawingRef));
        ctx.Interfaces.Add(new InterfaceImplementation(ideviceRef));
        ctx.Interfaces.Add(new InterfaceImplementation(iresHolderRef));
        // ICompatibleDeviceContext: CreateCompatibleDeviceContext answers
        // this same object shape (plus a Bitmap view over its target).
        ctx.Interfaces.Add(new InterfaceImplementation(icompRef));
        var targetField = new FieldDefinition("_target", FieldAttributes.Private, module.TypeSystem.Int64);
        var m11Field = new FieldDefinition("_m11", FieldAttributes.Private, module.TypeSystem.Single);
        var m12Field = new FieldDefinition("_m12", FieldAttributes.Private, module.TypeSystem.Single);
        var m21Field = new FieldDefinition("_m21", FieldAttributes.Private, module.TypeSystem.Single);
        var m22Field = new FieldDefinition("_m22", FieldAttributes.Private, module.TypeSystem.Single);
        var dxField = new FieldDefinition("_dx", FieldAttributes.Private, module.TypeSystem.Single);
        var dyField = new FieldDefinition("_dy", FieldAttributes.Private, module.TypeSystem.Single);
        ctx.Fields.Add(targetField);
        ctx.Fields.Add(m11Field);
        ctx.Fields.Add(m12Field);
        ctx.Fields.Add(m21Field);
        ctx.Fields.Add(m22Field);
        ctx.Fields.Add(dxField);
        ctx.Fields.Add(dyField);
        // IResourceCacheHolder backing: the single resource cache the
        // DrawingContext .ctor installs via TrySet (null until then).
        var holderCacheField = new FieldDefinition("_holderCache", FieldAttributes.Private, module.TypeSystem.Object);
        ctx.Fields.Add(holderCacheField);
        // Active command-list redirect (SetTarget(list) swaps _target to
        // the list scratch; null when direct). GetTarget answers it so
        // the vendor save/restore scope round-trips.
        var redirField = new FieldDefinition("_redirList", FieldAttributes.Private, module.TypeSystem.Object);
        ctx.Fields.Add(redirField);

        // Corlib glue (Hashtable cache, reflection reads, messages) goes
        // through ImportReference like the rest of this file: host and
        // PDN targets share the net9 universe, so the stamped tokens bind
        // (hand-built MemberRefs do not reliably resolve on the loader).
        TypeReference resCacheHtRef = module.ImportReference(typeof(System.Collections.Hashtable));
        var resCacheField = new FieldDefinition("_resCache", FieldAttributes.Private, resCacheHtRef);
        ctx.Fields.Add(resCacheField);
        // The base constructors need the device; the CPU context only
        // carries the target handle, so construction stops at object.
        var ctxCtor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctxCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int64));
        var ctorIl = ctxCtor.Body.GetILProcessor();
        ctorIl.Emit(OpCodes.Ldarg_0);
        // RefTrackedObject base: the parameterless .ctor seeds refCount=1.
        ctorIl.Emit(OpCodes.Call, refTrackedCtor);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, targetField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldc_R4, 1f);
        ctorIl.Emit(OpCodes.Stfld, m11Field);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldc_R4, 1f);
        ctorIl.Emit(OpCodes.Stfld, m22Field);
        ctorIl.Emit(OpCodes.Ret);
        ctx.Methods.Add(ctxCtor);
        // FillGeometry/FillRectangle/FillEllipse rewrites run in the
        // per-type loop before this synthesis, so they bind this
        // accessor by shape (self-scope TypeRef + name/signature):
        // it exposes the raw CPU target behind the context.
        var getHandle = new MethodDefinition(
            "GetCpuTargetHandle",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            module.TypeSystem.Int64);
        var getHandleIl = getHandle.Body.GetILProcessor();
        getHandleIl.Emit(OpCodes.Ldarg_0);
        getHandleIl.Emit(OpCodes.Ldfld, targetField);
        getHandleIl.Emit(OpCodes.Ret);
        ctx.Methods.Add(getHandle);

        List<MethodDefinition> contracts = InterfaceCpuMethods(idrawingDef).ToList();
        // Union: IDrawingContext already walks IDeviceContext, but
        // ICompatibleDeviceContext (+Bitmap) and the internal
        // IResourceCacheHolder (TryGet/TrySet) sit outside that walk.
        // Same key shape as InterfaceCpuMethods so inherited slots dedupe.
        var unionSeen = new HashSet<string>(contracts.Select(c =>
            c.Name + "(" + string.Join(",", c.Parameters.Select(p => p.ParameterType.FullName)) + ")"));
        foreach (TypeDefinition extraIface in new[] { ideviceDef, icompDef, iresHolderDef })
        {
            foreach (MethodDefinition extra in InterfaceCpuMethods(extraIface))
            {
                string extraKey = extra.Name + "(" + string.Join(",", extra.Parameters.Select(p => p.ParameterType.FullName)) + ")";
                if (unionSeen.Add(extraKey))
                    contracts.Add(extra);
            }
        }
        var implemented = new HashSet<MethodDefinition>();
        MethodDefinition Contract(string name, Func<MethodDefinition, bool> match)
        {
            MethodDefinition? hit = contracts.FirstOrDefault(m => m.Name == name && match(m))
                ?? throw new InvalidOperationException("CpuPaint: IDrawingContext." + name + " missing");
            implemented.Add(hit);
            return hit;
        }
        MethodDefinition Impl(MethodDefinition contract)
        {
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuType(module, contract.ReturnType));
            foreach (var p in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(
                    p.Name, p.Attributes, MapCpuType(module, p.ParameterType)));
            // Synthesized bodies push deep stacks (DrawBitmap: 14 slots);
            // fresh MethodBodies default too small for them.
            impl.Body.MaxStackSize = 32;
            ctx.Methods.Add(impl);
            implemented.Add(contract);
            return impl;
        }

        // -- Transform: recorded in fields and pushed to the CPU target.
        MethodDefinition contractGetTransform =
            Contract( "get_Transform", m => m.Parameters.Count == 0);
        var getTransform = Impl(contractGetTransform);
        {
            var il = getTransform.Body.GetILProcessor();
            MethodReference matrixCtor = CtorCpuRef(module, "PaintDotNet.Rendering.Matrix3x2Float", 6);
            foreach (var f in new[] { m11Field, m12Field, m21Field, m22Field, dxField, dyField })
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, f);
            }
            il.Emit(OpCodes.Newobj, matrixCtor);
            il.Emit(OpCodes.Ret);
        }

        MethodDefinition contractSetTransform =
            Contract( "set_Transform", m => m.Parameters.Count == 1);
        var setTransform = Impl(contractSetTransform);
        {
            var il = setTransform.Body.GetILProcessor();
            MethodReference getM11 = StructCpuGetter(module, "PaintDotNet.Rendering.Matrix3x2Float", "get_M11");
            MethodReference getM12 = StructCpuGetter(module, "PaintDotNet.Rendering.Matrix3x2Float", "get_M12");
            MethodReference getM21 = StructCpuGetter(module, "PaintDotNet.Rendering.Matrix3x2Float", "get_M21");
            MethodReference getM22 = StructCpuGetter(module, "PaintDotNet.Rendering.Matrix3x2Float", "get_M22");
            MethodReference getDX = StructCpuGetter(module, "PaintDotNet.Rendering.Matrix3x2Float", "get_OffsetX");
            MethodReference getDY = StructCpuGetter(module, "PaintDotNet.Rendering.Matrix3x2Float", "get_OffsetY");
            (MethodReference Getter, FieldDefinition Field)[] parts =
                [(getM11, m11Field), (getM12, m12Field), (getM21, m21Field),
                 (getM22, m22Field), (getDX, dxField), (getDY, dyField)];
            foreach (var (getter, field) in parts)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarga_S, setTransform.Parameters[0]);
                il.Emit(OpCodes.Call, getter);
                il.Emit(OpCodes.Stfld, field);
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            foreach (var f in new[] { m11Field, m12Field, m21Field, m22Field, dxField, dyField })
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, f);
            }
            il.Emit(OpCodes.Call, setTransformCpu);
            il.Emit(OpCodes.Ret);
        }

        // -- Antialias mode: recorded nowhere (coverage math is fixed);
        // the getter answers default instead of dereferencing a device.
        MethodDefinition contractSetAA =
            Contract( "set_AntialiasMode", m => m.Parameters.Count == 1);
        var setAA = Impl(contractSetAA);
        setAA.Body.GetILProcessor().Emit(OpCodes.Ret);

        MethodDefinition contractGetAA =
            Contract( "get_AntialiasMode", m => m.Parameters.Count == 0);
        var getAA = Impl(contractGetAA);
        {
            var il = getAA.Body.GetILProcessor();
            var loc = new VariableDefinition(MapCpuType(module, contractGetAA.ReturnType));
            getAA.Body.Variables.Add(loc);
            getAA.Body.InitLocals = true;
            il.Emit(OpCodes.Ldloca_S, loc);
            il.Emit(OpCodes.Initobj, MapCpuType(module, contractGetAA.ReturnType));
            il.Emit(OpCodes.Ldloc, loc);
            il.Emit(OpCodes.Ret);
        }

        // -- CreateBitmap(SizeInt32, void*, int, props&): empty or seed
        // upload straight into a CPU bitmap.
        MethodDefinition contractCreateEmpty = Contract( "CreateBitmap",
            m => m.Parameters.Count == 4
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Rendering.SizeInt32");
        var createEmpty = Impl(contractCreateEmpty);
        {
            var il = createEmpty.Body.GetILProcessor();
            MethodReference getW = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Width");
            MethodReference getH = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Height");
            var wLoc = new VariableDefinition(module.TypeSystem.Int32);
            var hLoc = new VariableDefinition(module.TypeSystem.Int32);
            var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
            createEmpty.Body.Variables.Add(wLoc);
            createEmpty.Body.Variables.Add(hLoc);
            createEmpty.Body.Variables.Add(handleLoc);
            createEmpty.Body.InitLocals = true;
            il.Emit(OpCodes.Ldarga_S, createEmpty.Parameters[0]);
            il.Emit(OpCodes.Call, getW);
            il.Emit(OpCodes.Stloc, wLoc);
            il.Emit(OpCodes.Ldarga_S, createEmpty.Parameters[0]);
            il.Emit(OpCodes.Call, getH);
            il.Emit(OpCodes.Stloc, hLoc);
            il.Emit(OpCodes.Ldarg_2);
            var emptyLabel = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, emptyLabel);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Call, createBitmapCpu);
            il.Emit(OpCodes.Stloc, handleLoc);
            var newLabel = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, newLabel);
            il.Append(emptyLabel);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Call, createBitmapEmpty);
            il.Emit(OpCodes.Stloc, handleLoc);
            il.Append(newLabel);
            il.Emit(OpCodes.Ldloc, handleLoc);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Newobj, bmpCtor);
            il.Emit(OpCodes.Ret);
        }

        // -- CreateBitmap(IBitmapSource, props&): pixel upload through a
        // pinned CPU buffer (CopyPixels writes unmanaged memory).
        MethodDefinition contractCreateSource = Contract( "CreateBitmap",
            m => m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapSource");
        var createSource = Impl(contractCreateSource);
        {
            var il = createSource.Body.GetILProcessor();
            MethodReference getSize = VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "get_Size", 0);
            MethodReference copyPixels =
                VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "CopyPixels", 4);
            MethodReference getW = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Width");
            MethodReference getH = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Height");
            TypeReference sizeType = OwnedCpuRef(module, "PaintDotNet.Rendering.SizeInt32");
            TypeReference clipType = MapCpuType(module, copyPixels.Parameters[3].ParameterType);
            var sizeLoc = new VariableDefinition(sizeType);
            var wLoc = new VariableDefinition(module.TypeSystem.Int32);
            var hLoc = new VariableDefinition(module.TypeSystem.Int32);
            var strideLoc = new VariableDefinition(module.TypeSystem.Int32);
            var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
            var ptrLoc = new VariableDefinition(module.TypeSystem.IntPtr);
            var clipLoc = new VariableDefinition(clipType);
            foreach (var v in new[] { sizeLoc, wLoc, hLoc, strideLoc, handleLoc, ptrLoc, clipLoc })
                createSource.Body.Variables.Add(v);
            createSource.Body.InitLocals = true;
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, getSize);
            il.Emit(OpCodes.Stloc, sizeLoc);
            il.Emit(OpCodes.Ldloca_S, sizeLoc);
            il.Emit(OpCodes.Call, getW);
            il.Emit(OpCodes.Stloc, wLoc);
            il.Emit(OpCodes.Ldloca_S, sizeLoc);
            il.Emit(OpCodes.Call, getH);
            il.Emit(OpCodes.Stloc, hLoc);
            // Half sources (checkbox bitmaps) upload through a
            // widening helper: the device Bitmap is Bgra32-only.
            MethodReference halfSrcFormat =
                VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "get_PixelFormat", 0);
            MethodReference halfBitsPerPixel = StructCpuGetter(module, "PaintDotNet.Imaging.PixelFormat", "GetBitsPerPixel");
            TypeReference halfFormatType = OwnedCpuRef(module, "PaintDotNet.Imaging.PixelFormat");
            MethodReference uploadHalfCpu = Cpu("CreateBitmapFromHalfSource", 3);
            MethodReference halfNseCtor = module.ImportReference(
                typeof(NotSupportedException).GetConstructor([typeof(string)])!);
            var halfFmtLoc = new VariableDefinition(halfFormatType);
            var halfBppLoc = new VariableDefinition(module.TypeSystem.Int32);
            var halfHandleLoc = new VariableDefinition(module.TypeSystem.Int64);
            foreach (var v in new[] { halfFmtLoc, halfBppLoc, halfHandleLoc })
                createSource.Body.Variables.Add(v);
            var is32Source = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, halfSrcFormat);
            il.Emit(OpCodes.Stloc, halfFmtLoc);
            il.Emit(OpCodes.Ldloca_S, halfFmtLoc);
            il.Emit(OpCodes.Call, halfBitsPerPixel);
            il.Emit(OpCodes.Stloc, halfBppLoc);
            il.Emit(OpCodes.Ldloc, halfBppLoc);
            il.Emit(OpCodes.Ldc_I4, 64);
            il.Emit(OpCodes.Bne_Un, is32Source);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Call, uploadHalfCpu);
            il.Emit(OpCodes.Stloc, halfHandleLoc);
            il.Emit(OpCodes.Ldloc, halfHandleLoc);
            var halfUploadFailed = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, halfUploadFailed);
            il.Emit(OpCodes.Ldloc, halfHandleLoc);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Newobj, bmpCtor);
            il.Emit(OpCodes.Ret);
            il.Append(halfUploadFailed);
            il.Emit(OpCodes.Ldstr, "CpuDrawingContext.CreateBitmap has no headless backend for 64-bit upload");
            il.Emit(OpCodes.Newobj, halfNseCtor);
            il.Emit(OpCodes.Throw);
            il.Append(is32Source);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Stloc, strideLoc);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Call, createBitmapEmpty);
            il.Emit(OpCodes.Stloc, handleLoc);
            il.Emit(OpCodes.Ldloc, handleLoc);
            il.Emit(OpCodes.Call, lockBitmap);
            il.Emit(OpCodes.Stloc, ptrLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, ptrLoc);
            il.Emit(OpCodes.Ldloc, strideLoc);
            il.Emit(OpCodes.Ldloc, strideLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Ldloca_S, clipLoc);
            il.Emit(OpCodes.Callvirt, copyPixels);
            il.Emit(OpCodes.Ldloc, handleLoc);
            il.Emit(OpCodes.Call, unlockBitmap);
            il.Emit(OpCodes.Ldloc, handleLoc);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Newobj, bmpCtor);
            il.Emit(OpCodes.Ret);
        }

        // -- CreateSharedBitmap(IBitmapLock, props&): upload the lock's
        // live buffer straight into a CPU bitmap. The extension disposes
        // the lock in finally, so pixels are copied, not shared. 32-bit
        // locks upload packed (Bgra/Pbgra); 64-bit Half locks convert
        // through UploadHalfSharedBitmap; anything else keeps today's
        // NotSupported.
        MethodDefinition contractShared = Contract("CreateSharedBitmap",
            m => m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapLock");
        var shared = Impl(contractShared);
        {
            var il = shared.Body.GetILProcessor();
            MethodReference lockSize = VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_Size", 0);
            MethodReference lockStride = VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_BufferStride", 0);
            MethodReference lockBuffer = VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_Buffer", 0);
            MethodReference lockFormat = VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_PixelFormat", 0);
            MethodReference bitsPerPixel = StructCpuGetter(module, "PaintDotNet.Imaging.PixelFormat", "GetBitsPerPixel");
            MethodReference sharedGetW = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Width");
            MethodReference sharedGetH = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Height");
            TypeReference sizeType = OwnedCpuRef(module, "PaintDotNet.Rendering.SizeInt32");
            TypeReference formatType = OwnedCpuRef(module, "PaintDotNet.Imaging.PixelFormat");
            var sizeLoc = new VariableDefinition(sizeType);
            var formatLoc = new VariableDefinition(formatType);
            var bppLoc = new VariableDefinition(module.TypeSystem.Int32);
            var wLoc = new VariableDefinition(module.TypeSystem.Int32);
            var hLoc = new VariableDefinition(module.TypeSystem.Int32);
            var strideLoc = new VariableDefinition(module.TypeSystem.Int32);
            var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
            foreach (var v in new[] { sizeLoc, formatLoc, bppLoc, wLoc, hLoc, strideLoc, handleLoc })
                shared.Body.Variables.Add(v);
            shared.Body.InitLocals = true;
            var badFormat = il.Create(OpCodes.Nop);
            var badAlloc = il.Create(OpCodes.Nop);
            var emptySize = il.Create(OpCodes.Nop);
            var gotBitmap = il.Create(OpCodes.Nop);
            var is32 = il.Create(OpCodes.Nop);
            var is64 = il.Create(OpCodes.Nop);
            var checkAlloc = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, lockFormat);
            il.Emit(OpCodes.Stloc, formatLoc);
            il.Emit(OpCodes.Ldloca_S, formatLoc);
            il.Emit(OpCodes.Call, bitsPerPixel);
            il.Emit(OpCodes.Stloc, bppLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, lockSize);
            il.Emit(OpCodes.Stloc, sizeLoc);
            il.Emit(OpCodes.Ldloca_S, sizeLoc);
            il.Emit(OpCodes.Call, sharedGetW);
            il.Emit(OpCodes.Stloc, wLoc);
            il.Emit(OpCodes.Ldloca_S, sizeLoc);
            il.Emit(OpCodes.Call, sharedGetH);
            il.Emit(OpCodes.Stloc, hLoc);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Blt, emptySize);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Blt, emptySize);
            // 32-bit locks take the packed upload; 64-bit Half locks
            // convert through UploadHalfSharedBitmap; anything else
            // keeps today's NotSupported.
            il.Emit(OpCodes.Ldloc, bppLoc);
            il.Emit(OpCodes.Ldc_I4, 32);
            il.Emit(OpCodes.Beq, is32);
            il.Emit(OpCodes.Ldloc, bppLoc);
            il.Emit(OpCodes.Ldc_I4, 64);
            il.Emit(OpCodes.Bne_Un, badFormat);
            il.Append(is64);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, lockBuffer);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, lockStride);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Call, uploadHalfShared);
            il.Emit(OpCodes.Stloc, handleLoc);
            il.Emit(OpCodes.Br, checkAlloc);
            il.Append(is32);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, lockStride);
            il.Emit(OpCodes.Stloc, strideLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, lockBuffer);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldloc, strideLoc);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Call, createBitmapCpu);
            il.Emit(OpCodes.Stloc, handleLoc);
            il.Append(checkAlloc);
            il.Emit(OpCodes.Ldloc, handleLoc);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Bne_Un, gotBitmap);
            MethodReference sharedNoBackend = module.ImportReference(typeof(NotSupportedException).GetConstructor([typeof(string)])!);
            il.Append(badAlloc);
            il.Emit(OpCodes.Ldstr, "CpuDrawingContext.CreateSharedBitmap has no headless backend (upload failed)");
            il.Emit(OpCodes.Newobj, sharedNoBackend);
            il.Emit(OpCodes.Throw);
            il.Append(emptySize);
            il.Emit(OpCodes.Ldstr, "CpuDrawingContext.CreateSharedBitmap has no headless backend (empty)");
            il.Emit(OpCodes.Newobj, sharedNoBackend);
            il.Emit(OpCodes.Throw);
            il.Append(badFormat);
            il.Emit(OpCodes.Ldstr, "CpuDrawingContext.CreateSharedBitmap has no headless backend (pixel format)");
            il.Emit(OpCodes.Newobj, sharedNoBackend);
            il.Emit(OpCodes.Throw);
            il.Append(gotBitmap);
            il.Emit(OpCodes.Ldloc, handleLoc);
            il.Emit(OpCodes.Ldloc, wLoc);
            il.Emit(OpCodes.Ldloc, hLoc);
            il.Emit(OpCodes.Newobj, bmpCtor);
            il.Emit(OpCodes.Ret);
        }

        // -- CreateSolidColorBrush(color, props&): decomposed floats.
        MethodDefinition contractCreateBrush = Contract( "CreateSolidColorBrush",
            m => m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.ColorRgba128Float");
        var createBrush = Impl(contractCreateBrush);
        {
            var il = createBrush.Body.GetILProcessor();
            MethodReference getR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
            MethodReference getG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
            MethodReference getB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
            MethodReference getA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
            TypeReference matrixRef = OwnedCpuRef(module, "PaintDotNet.Rendering.Matrix3x2Float");
            var matrixLoc = new VariableDefinition(matrixRef);
            createBrush.Body.Variables.Add(matrixLoc);
            createBrush.Body.InitLocals = true;
            il.Emit(OpCodes.Ldc_R4, 1f);
            foreach (var g in new[] { getR, getG, getB, getA })
            {
                il.Emit(OpCodes.Ldarga_S, createBrush.Parameters[0]);
                il.Emit(OpCodes.Call, g);
            }
            il.Emit(OpCodes.Ldloc, matrixLoc);
            il.Emit(OpCodes.Newobj, brushCtor);
            il.Emit(OpCodes.Ret);
        }

        // -- GetCachedOrCreateResource(provider, type): headless device-
        // resource cache. The brush renderer pulls its white-pixel, sprite
        // and solid-color resources through the drawing context (which is
        // the D2D resource cache). Entries key on the provider with the
        // provider's own version beside the resource, mirroring the
        // -- CreateColorContext: CPU device color context (sRGB truth).
        // TryCreateDefaultColorContext only needs a valid token to feed
        // ManagedColor (effect dialogs), so every overload answers a
        // CpuDeviceColorContext: the requested space passes through,
        // IColorContext inputs map sRGB Exif to Srgb, Dxgi sRGB-full to
        // Srgb, and profile/simple inputs stay Custom.
        MethodDefinition colorCtxCtor = cpuColorCtx.Methods.First(m => m.Name == ".ctor");
        MethodReference exifGetter = module.ImportReference(ResolveCpuDef(
            LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.IColorContext")).Methods
            .First(m => m.Name == "get_ExifColorSpace" && m.Parameters.Count == 0));
        MethodDefinition contractCcSpace = Contract("CreateColorContext",
            m => m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Direct2D1.DeviceColorSpace");
        var ccSpace = Impl(contractCcSpace);
        {
            var il = ccSpace.Body.GetILProcessor();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, colorCtxCtor);
            il.Emit(OpCodes.Ret);
        }
        MethodDefinition contractCcSpan = Contract("CreateColorContext",
            m => m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "System.ReadOnlySpan`1<System.Byte>");
        var ccSpan = Impl(contractCcSpan);
        {
            // Profile bytes have no CPU consumer (GetProfileBytes
            // answers empty here); the space is custom by definition.
            var il = ccSpan.Body.GetILProcessor();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, colorCtxCtor);
            il.Emit(OpCodes.Ret);
        }
        MethodDefinition contractCcImaging = Contract("CreateColorContext",
            m => m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IColorContext");
        var ccImaging = Impl(contractCcImaging);
        {
            var il = ccImaging.Body.GetILProcessor();
            var isCustom = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, exifGetter);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, isCustom);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, colorCtxCtor);
            il.Emit(OpCodes.Ret);
            il.Append(isCustom);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, colorCtxCtor);
            il.Emit(OpCodes.Ret);
        }
        MethodDefinition contractCcDxgi = Contract("CreateColorContext",
            m => m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Dxgi.DxgiColorSpace");
        var ccDxgi = Impl(contractCcDxgi);
        {
            var il = ccDxgi.Body.GetILProcessor();
            var isCustom = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, isCustom);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newobj, colorCtxCtor);
            il.Emit(OpCodes.Ret);
            il.Append(isCustom);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newobj, colorCtxCtor);
            il.Emit(OpCodes.Ret);
        }
        MethodDefinition contractCcSimple = Contract("CreateColorContext",
            m => m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Direct2D1.SimpleColorProfile");
        var ccSimple = Impl(contractCcSimple);
        {
            var il = ccSimple.Body.GetILProcessor();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newobj, colorCtxCtor);
            il.Emit(OpCodes.Ret);
        }
        // -- MaximumBitmapSize: the zero-init default divides BitmapImage
        // tiling math by zero. Desktop D2D reports 16384; the CPU target
        // never allocates it (CreateImpl tiles with Min like hardware).
        MethodDefinition contractMaxBmp = Contract("get_MaximumBitmapSize",
            m => m.Parameters.Count == 0);
        var maxBmp = Impl(contractMaxBmp);
        {
            var il = maxBmp.Body.GetILProcessor();
            MethodReference sizeCtor = CtorCpuRef(module, "PaintDotNet.Rendering.SizeInt32", 2);
            il.Emit(OpCodes.Ldc_I4, 16384);
            il.Emit(OpCodes.Ldc_I4, 16384);
            il.Emit(OpCodes.Newobj, sizeCtor);
            il.Emit(OpCodes.Ret);
        }

        // generational cache: sprite bitmaps invalidate per stroke, and a
        // 32-entry cap stops version churn from growing the table without
        // bound. DeviceBitmap providers upload through CreateBitmap (a
        // null source means the 1x1 white pixel, built inline exactly the
        // way CreateWhitePixelBitmap builds it); SolidColorBrush providers
        // decompose through CreateSolidColorBrush. Anything else fails
        // loudly and names the provider type. Provider properties read
        // through corlib reflection so this module needs no reference to
        // the UI assembly that owns them.
        MethodDefinition contractResCache = Contract("GetCachedOrCreateResource",
            m => m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.ObjectModel.IResourceProvider"
                && m.Parameters[1].ParameterType.FullName == "System.Type");
        var resCache = Impl(contractResCache);
        {
            var il = resCache.Body.GetILProcessor();
            TypeReference htRef = resCacheHtRef;
            TypeReference sysTypeRef = LocalOrOwnedCpuRef(module, "System.Type");
            TypeReference miRef = module.ImportReference(typeof(System.Reflection.MethodInfo));
            MethodReference htCtor = module.ImportReference(typeof(System.Collections.Hashtable).GetConstructor(Type.EmptyTypes)!);
            MethodReference htGet = module.ImportReference(typeof(System.Collections.Hashtable).GetMethod("get_Item", [typeof(object)])!);
            MethodReference htSet = module.ImportReference(typeof(System.Collections.Hashtable).GetMethod("set_Item", [typeof(object), typeof(object)])!);
            MethodReference htCount = module.ImportReference(typeof(System.Collections.Hashtable).GetMethod("get_Count", Type.EmptyTypes)!);
            MethodReference htClear = module.ImportReference(typeof(System.Collections.Hashtable).GetMethod("Clear", Type.EmptyTypes)!);
            MethodReference objGetType = module.ImportReference(typeof(object).GetMethod("GetType", Type.EmptyTypes)!);
            MethodReference typeFullName = module.ImportReference(typeof(Type).GetMethod("get_FullName", Type.EmptyTypes)!);
            MethodReference strEq = module.ImportReference(typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
            MethodReference typeGetMethod = module.ImportReference(typeof(Type).GetMethod("GetMethod", [typeof(string)])!);

            var objArrType = new ArrayType(module.TypeSystem.Object);
            MethodReference miInvoke = module.ImportReference(typeof(System.Reflection.MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
            MethodReference strConcat = module.ImportReference(typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
            MethodReference nseCtor = module.ImportReference(typeof(NotSupportedException).GetConstructor([typeof(string)])!);
            MethodReference provGetVersion = VirtualCpuCall(module, "PaintDotNet.ObjectModel.IResourceProvider", "get_ResourceVersion", 0);
            TypeReference ibmpSrcRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.IBitmapSource");
            TypeReference colorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.ColorRgba128Float");
            TypeReference? sysColorRef = null;
            MethodReference? sysToRgbaRef = null;
            VariableDefinition? sysLoc = null;
            try
            {
                sysColorRef = module.ImportReference(ResolveExternalDef(module, "PaintDotNet.Windows.Framework", "PaintDotNet.Imaging.SystemColor"));
                    sysToRgbaRef = module.ImportReference(ResolveCpuDef(sysColorRef).Methods
                        .First(m => m.Name == "op_Implicit"
                            && m.Parameters.Count == 1
                            && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.SystemColor"
                            && m.ReturnType.FullName == "PaintDotNet.Imaging.ColorRgba128Float"));
                    sysLoc = new VariableDefinition(sysColorRef);
                    resCache.Body.Variables.Add(sysLoc);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
                {
                    sysColorRef = null;
                    sysToRgbaRef = null;
                    sysLoc = null;
                }
            // Props travel as modreq-qualified `in` byrefs: strip the
            // modifiers to reach the byref, then its element type.
            TypeReference ByrefElem(TypeReference t)
            {
                while (t is RequiredModifierType req) t = req.ElementType;
                while (t is OptionalModifierType opt) t = opt.ElementType;
                return ((ByReferenceType)t).ElementType;
            }
            TypeReference bmpPropsRef = ByrefElem(createSource.Parameters[1].ParameterType);
            TypeReference brushPropsRef = ByrefElem(createBrush.Parameters[1].ParameterType);
            var entryLoc = new VariableDefinition(module.TypeSystem.Object);
            var arrLoc = new VariableDefinition(objArrType);
            var verLoc = new VariableDefinition(module.TypeSystem.Int64);
            var srcLoc = new VariableDefinition(ibmpSrcRef);
            var resLoc = new VariableDefinition(module.TypeSystem.Object);
            var provTypeLoc = new VariableDefinition(sysTypeRef);
            var miLoc = new VariableDefinition(miRef);
            var whiteHandleLoc = new VariableDefinition(module.TypeSystem.Int64);
            var whitePtrLoc = new VariableDefinition(module.TypeSystem.IntPtr);
            var colorLoc = new VariableDefinition(colorRef);
            var bmpPropsLoc = new VariableDefinition(bmpPropsRef);
            var brushPropsLoc = new VariableDefinition(brushPropsRef);
            foreach (var v in new[] { entryLoc, arrLoc, verLoc, srcLoc, resLoc, provTypeLoc, miLoc, whiteHandleLoc, whitePtrLoc, colorLoc, bmpPropsLoc, brushPropsLoc })
                resCache.Body.Variables.Add(v);
            resCache.Body.InitLocals = true;
            var textLoc = new VariableDefinition(module.TypeSystem.String);
            var fsLoc = new VariableDefinition(module.TypeSystem.Single);
            var maxWLoc = new VariableDefinition(module.TypeSystem.Single);
            var maxHLoc = new VariableDefinition(module.TypeSystem.Single);
            resCache.Body.Variables.Add(textLoc);
            resCache.Body.Variables.Add(fsLoc);
            resCache.Body.Variables.Add(maxWLoc);
            resCache.Body.Variables.Add(maxHLoc);
            var gPtObjLoc = new VariableDefinition(module.TypeSystem.Object);
            var gPtTypeLoc = new VariableDefinition(sysTypeRef);
            var gx0Loc = new VariableDefinition(module.TypeSystem.Single);
            var gy0Loc = new VariableDefinition(module.TypeSystem.Single);
            var gx1Loc = new VariableDefinition(module.TypeSystem.Single);
            var gy1Loc = new VariableDefinition(module.TypeSystem.Single);
            var gOpLoc = new VariableDefinition(module.TypeSystem.Single);
            var gSpreadLoc = new VariableDefinition(module.TypeSystem.Int32);
            var gInterpLoc = new VariableDefinition(module.TypeSystem.Int32);
            var gSpaceLoc = new VariableDefinition(module.TypeSystem.Int32);
            var gStopsLoc = new VariableDefinition(module.TypeSystem.Object);
            var gLutLoc = new VariableDefinition(module.TypeSystem.Int64);
            foreach (var v in new[] { gPtObjLoc, gPtTypeLoc, gx0Loc, gy0Loc, gx1Loc, gy1Loc, gOpLoc, gSpreadLoc, gInterpLoc, gSpaceLoc, gStopsLoc, gLutLoc })
                resCache.Body.Variables.Add(v);
            var hasCache = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resCacheField);
            il.Emit(OpCodes.Brtrue, hasCache);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, htCtor);
            il.Emit(OpCodes.Stfld, resCacheField);
            il.Append(hasCache);
            var noClear = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resCacheField);
            il.Emit(OpCodes.Callvirt, htCount);
            il.Emit(OpCodes.Ldc_I4, 32);
            il.Emit(OpCodes.Ble, noClear);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resCacheField);
            il.Emit(OpCodes.Callvirt, htClear);
            il.Append(noClear);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, provGetVersion);
            il.Emit(OpCodes.Stloc, verLoc);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resCacheField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, htGet);
            il.Emit(OpCodes.Stloc, entryLoc);
            var miss = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, entryLoc);
            il.Emit(OpCodes.Brfalse, miss);
            il.Emit(OpCodes.Ldloc, entryLoc);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Unbox_Any, module.TypeSystem.Int64);
            il.Emit(OpCodes.Ldloc, verLoc);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, miss);
            il.Emit(OpCodes.Ldloc, entryLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass, resCache.ReturnType);
            il.Emit(OpCodes.Ret);
            var depthLoc = new VariableDefinition(module.TypeSystem.Int32);
            resCache.Body.Variables.Add(depthLoc);

            il.Append(miss);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, depthLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, objGetType);
            il.Emit(OpCodes.Stloc, provTypeLoc);
            var tryBrush = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Callvirt, typeFullName);
            il.Emit(OpCodes.Ldstr, "PaintDotNet.UI.Media.DeviceBitmap");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, tryBrush);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Ldstr, "get_BitmapSource");
            il.Emit(OpCodes.Call, typeGetMethod);
            il.Emit(OpCodes.Stloc, miLoc);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, miInvoke);
            il.Emit(OpCodes.Castclass, ibmpSrcRef);
            il.Emit(OpCodes.Stloc, srcLoc);
            var hasSrc = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, srcLoc);
            il.Emit(OpCodes.Brtrue, hasSrc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, createBitmapEmpty);
            il.Emit(OpCodes.Stloc, whiteHandleLoc);
            il.Emit(OpCodes.Ldloc, whiteHandleLoc);
            il.Emit(OpCodes.Call, lockBitmap);
            il.Emit(OpCodes.Stloc, whitePtrLoc);
            il.Emit(OpCodes.Ldloc, whitePtrLoc);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Stind_I4);
            il.Emit(OpCodes.Ldloc, whiteHandleLoc);
            il.Emit(OpCodes.Call, unlockBitmap);
            il.Emit(OpCodes.Ldloc, whiteHandleLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newobj, bmpCtor);
            il.Emit(OpCodes.Stloc, resLoc);
            var stored = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, stored);
            il.Append(hasSrc);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, srcLoc);
            il.Emit(OpCodes.Ldloca_S, bmpPropsLoc);
            il.Emit(OpCodes.Call, createSource);
            il.Emit(OpCodes.Stloc, resLoc);
            il.Emit(OpCodes.Br, stored);
            il.Append(tryBrush);
            var tryText = il.Create(OpCodes.Nop);
            var unknown = il.Create(OpCodes.Nop);
            var autoNext = il.Create(OpCodes.Nop);
            var duckProbe = il.Create(OpCodes.Nop);
            var scaledProbe = il.Create(OpCodes.Nop);
            var tryProfile = il.Create(OpCodes.Nop);
            var tryGradient = il.Create(OpCodes.Nop);
            var tryTextLayout = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Callvirt, typeFullName);
            il.Emit(OpCodes.Ldstr, "PaintDotNet.UI.Media.SolidColorBrush");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, (sysColorRef != null && sysToRgbaRef != null && sysLoc != null) ? duckProbe : tryText);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Ldstr, "get_Color");
            il.Emit(OpCodes.Call, typeGetMethod);
            il.Emit(OpCodes.Stloc, miLoc);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, miInvoke);
            il.Emit(OpCodes.Unbox_Any, colorRef);
            il.Emit(OpCodes.Stloc, colorLoc);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, colorLoc);
            il.Emit(OpCodes.Ldloca_S, brushPropsLoc);
            il.Emit(OpCodes.Call, createBrush);
            il.Emit(OpCodes.Stloc, resLoc);
            il.Emit(OpCodes.Br, stored);
            if (sysColorRef != null && sysToRgbaRef != null && sysLoc != null)
            {
            var objLoc = new VariableDefinition(module.TypeSystem.Object);
            resCache.Body.Variables.Add(objLoc);
            il.Append(duckProbe);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Ldstr, "get_SystemColor");
            il.Emit(OpCodes.Call, typeGetMethod);
            il.Emit(OpCodes.Stloc, miLoc);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Brfalse, tryText);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, miInvoke);
            il.Emit(OpCodes.Stloc, objLoc);
            il.Emit(OpCodes.Ldloc, objLoc);
            il.Emit(OpCodes.Brfalse, tryText);
            il.Emit(OpCodes.Ldloc, objLoc);
            il.Emit(OpCodes.Isinst, sysColorRef!);
            il.Emit(OpCodes.Brfalse, tryText);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, miInvoke);
            il.Emit(OpCodes.Unbox_Any, sysColorRef!);
            il.Emit(OpCodes.Stloc, sysLoc!);
            il.Emit(OpCodes.Ldloc, sysLoc!);
            il.Emit(OpCodes.Call, sysToRgbaRef!);
            il.Emit(OpCodes.Stloc, colorLoc);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, colorLoc);
            il.Emit(OpCodes.Ldloca_S, brushPropsLoc);
            il.Emit(OpCodes.Call, createBrush);
            il.Emit(OpCodes.Stloc, resLoc);
            il.Emit(OpCodes.Br, stored);
            }
            // The inner sRGB provider resolves through metadata, not
            // reflection: Type.GetField(string,BindingFlags) faults Bad IL
            // in the browser runtime (bisect3 convicted the call), so the
            // nested type + field import from PaintDotNet.Framework at
            // rewrite time (Cecil carries the scope; Core needs no
            // compile-time reference). Redispatch is a direct non-virtual
            // call into this same body: the inner provider is concrete,
            // so recursion depth is one.
            TypeReference? autoRef = null;
            FieldReference? autoInnerFld = null;
            try
            {
                TypeDefinition autoDef = ResolveExternalDef(module,
                    "PaintDotNet.Framework", "PaintDotNet.Resources.ImageResourceUtil")
                    .NestedTypes.First(n => n.Name == "AutoColorSpaceImageProvider");
                autoRef = module.ImportReference(autoDef);
                autoInnerFld = module.ImportReference(
                    autoDef.Fields.First(f => f.Name == "srgbImageProvider"));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
                autoRef = null;
                autoInnerFld = null;
            }
            // ScaledDeviceImageProvider wraps a source provider with a UI
            // scale (icons): the CPU cache answers the source at native
            // size (identity scale), mirroring the Auto unwrap above.
            TypeReference? scaledRef = null;
            FieldReference? scaledSrcFld = null;
            try
            {
                TypeDefinition scaledDef = ResolveExternalDef(module,
                    "PaintDotNet.Framework", "PaintDotNet.Resources.ScaledDeviceImageProvider");
                scaledRef = module.ImportReference(scaledDef);
                scaledSrcFld = module.ImportReference(
                    scaledDef.Fields.First(f => f.Name == "source"));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            {
                scaledRef = null;
                scaledSrcFld = null;
            }
            TypeReference iresProvRef = LocalOrOwnedCpuRef(module, "PaintDotNet.ObjectModel.IResourceProvider");
            var innerLoc = new VariableDefinition(iresProvRef);
            resCache.Body.Variables.Add(innerLoc);
            il.Append(tryText);
            if (autoRef != null && autoInnerFld != null)
            {
                il.Emit(OpCodes.Ldloc, provTypeLoc);
                il.Emit(OpCodes.Callvirt, typeFullName);
                il.Emit(OpCodes.Ldstr, "PaintDotNet.Resources.ImageResourceUtil+AutoColorSpaceImageProvider");
                il.Emit(OpCodes.Call, strEq);
                il.Emit(OpCodes.Brfalse, scaledProbe);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Castclass, autoRef);
                il.Emit(OpCodes.Ldfld, autoInnerFld);
                il.Emit(OpCodes.Stloc, innerLoc);
                il.Emit(OpCodes.Ldloc, innerLoc);
                il.Emit(OpCodes.Brfalse, scaledProbe);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, innerLoc);
                il.Emit(OpCodes.Ldloc, innerLoc);
                il.Emit(OpCodes.Callvirt, objGetType);
                il.Emit(OpCodes.Call, resCache);
                il.Emit(OpCodes.Stloc, resLoc);
                il.Emit(OpCodes.Br, stored);
            }
            il.Append(scaledProbe);
            if (scaledRef != null && scaledSrcFld != null)
            {
                il.Emit(OpCodes.Ldloc, provTypeLoc);
                il.Emit(OpCodes.Callvirt, typeFullName);
                il.Emit(OpCodes.Ldstr, "PaintDotNet.Resources.ScaledDeviceImageProvider");
                il.Emit(OpCodes.Call, strEq);
                il.Emit(OpCodes.Brfalse, autoNext);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Castclass, scaledRef);
                il.Emit(OpCodes.Ldfld, scaledSrcFld);
                il.Emit(OpCodes.Stloc, innerLoc);
                il.Emit(OpCodes.Ldloc, innerLoc);
                il.Emit(OpCodes.Brfalse, autoNext);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, innerLoc);
                il.Emit(OpCodes.Ldloc, innerLoc);
                il.Emit(OpCodes.Callvirt, objGetType);
                il.Emit(OpCodes.Call, resCache);
                il.Emit(OpCodes.Stloc, resLoc);
                il.Emit(OpCodes.Br, stored);
            }
            il.Append(autoNext);
            MethodReference toLower = module.ImportReference(typeof(string).GetMethod("ToLowerInvariant", Type.EmptyTypes)!);
            MethodReference strContains = module.ImportReference(typeof(string).GetMethod("Contains", [typeof(string)])!);
            MethodReference convertToInt = module.ImportReference(typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
            MethodReference createGradientLut = ImportCpuPaintMethod(module, cpuPaintPath, "CreateGradientLut", 3);
            // One provider property through corlib reflection, leaving
            // the invoked value on the stack (missing -> unknown NSE).
            void RequireGetter(string getter)
            {
                il.Emit(OpCodes.Ldloc, provTypeLoc);
                il.Emit(OpCodes.Ldstr, getter);
                il.Emit(OpCodes.Call, typeGetMethod);
                il.Emit(OpCodes.Stloc, miLoc);
                il.Emit(OpCodes.Ldloc, miLoc);
                il.Emit(OpCodes.Brfalse, unknown);
                il.Emit(OpCodes.Ldloc, miLoc);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Callvirt, miInvoke);
            }
            // A Point2Double provider property (StartPoint/EndPoint)
            // into two float locals through corlib reflection.
            void ReadGradientPoint(string getter, VariableDefinition xo, VariableDefinition yo)
            {
                RequireGetter(getter);
                il.Emit(OpCodes.Stloc, gPtObjLoc);
                il.Emit(OpCodes.Ldloc, gPtObjLoc);
                il.Emit(OpCodes.Callvirt, objGetType);
                il.Emit(OpCodes.Stloc, gPtTypeLoc);
                il.Emit(OpCodes.Ldloc, gPtTypeLoc);
                il.Emit(OpCodes.Ldstr, "get_X");
                il.Emit(OpCodes.Call, typeGetMethod);
                il.Emit(OpCodes.Stloc, miLoc);
                il.Emit(OpCodes.Ldloc, miLoc);
                il.Emit(OpCodes.Brfalse, unknown);
                il.Emit(OpCodes.Ldloc, miLoc);
                il.Emit(OpCodes.Ldloc, gPtObjLoc);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Callvirt, miInvoke);
                il.Emit(OpCodes.Unbox_Any, module.TypeSystem.Double);
                il.Emit(OpCodes.Conv_R4);
                il.Emit(OpCodes.Stloc, xo);
                il.Emit(OpCodes.Ldloc, gPtTypeLoc);
                il.Emit(OpCodes.Ldstr, "get_Y");
                il.Emit(OpCodes.Call, typeGetMethod);
                il.Emit(OpCodes.Stloc, miLoc);
                il.Emit(OpCodes.Ldloc, miLoc);
                il.Emit(OpCodes.Brfalse, unknown);
                il.Emit(OpCodes.Ldloc, miLoc);
                il.Emit(OpCodes.Ldloc, gPtObjLoc);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Callvirt, miInvoke);
                il.Emit(OpCodes.Unbox_Any, module.TypeSystem.Double);
                il.Emit(OpCodes.Conv_R4);
                il.Emit(OpCodes.Stloc, yo);
            }
            // Ambient color profiles (sRGB/scRGB) request device color
            // contexts: CPU blending is identity in sRGB, so answer a
            // context token carrying the profile's space. The
            // description names the stock profile (scRGB vs sRGB);
            // anything else (or a missing getter) defaults to sRGB.
            // Stock singletons answer their concrete class
            // (PaintDotNet.Imaging.ColorProfile), not the proxy
            // interface, so every known profile carrier matches.
            il.Append(tryProfile);
            var gotProfile = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Callvirt, typeFullName);
            il.Emit(OpCodes.Ldstr, "PaintDotNet.Imaging.Proxies.IColorProfileProxy");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brtrue, gotProfile);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Callvirt, typeFullName);
            il.Emit(OpCodes.Ldstr, "PaintDotNet.Imaging.ColorProfile");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brtrue, gotProfile);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Callvirt, typeFullName);
            il.Emit(OpCodes.Ldstr, "PaintDotNet.Direct2D1.SimpleColorProfile");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, tryGradient);
            il.Append(gotProfile);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, gSpaceLoc);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Ldstr, "get_Description");
            il.Emit(OpCodes.Call, typeGetMethod);
            il.Emit(OpCodes.Stloc, miLoc);
            il.Emit(OpCodes.Ldloc, miLoc);
            var hasDescr = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brtrue, hasDescr);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, textLoc);
            var gotDescr = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, gotDescr);
            il.Append(hasDescr);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, miInvoke);
            il.Emit(OpCodes.Castclass, module.TypeSystem.String);
            il.Emit(OpCodes.Stloc, textLoc);
            il.Append(gotDescr);
            il.Emit(OpCodes.Ldloc, textLoc);
            var isSrgb = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, isSrgb);
            il.Emit(OpCodes.Ldloc, textLoc);
            il.Emit(OpCodes.Callvirt, toLower);
            il.Emit(OpCodes.Ldstr, "scrgb");
            il.Emit(OpCodes.Callvirt, strContains);
            il.Emit(OpCodes.Brfalse, isSrgb);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Stloc, gSpaceLoc);
            il.Append(isSrgb);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldloc, gSpaceLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newobj, colorCtxCtor);
            il.Emit(OpCodes.Stloc, resLoc);
            il.Emit(OpCodes.Br, stored);
            // LinearGradientBrush providers: sample the stops once per
            // version into a CpuPaint LUT and answer a CPU gradient
            // resource carrying the LUT handle, axis, opacity and spread.
            il.Append(tryGradient);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Callvirt, typeFullName);
            il.Emit(OpCodes.Ldstr, "PaintDotNet.UI.Media.LinearGradientBrush");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, tryTextLayout);
            ReadGradientPoint("get_StartPoint", gx0Loc, gy0Loc);
            ReadGradientPoint("get_EndPoint", gx1Loc, gy1Loc);
            RequireGetter("get_Opacity");
            il.Emit(OpCodes.Unbox_Any, module.TypeSystem.Double);
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stloc, gOpLoc);
            RequireGetter("get_SpreadMethod");
            il.Emit(OpCodes.Call, convertToInt);
            il.Emit(OpCodes.Stloc, gSpreadLoc);
            RequireGetter("get_ColorInterpolationSpace");
            il.Emit(OpCodes.Call, convertToInt);
            il.Emit(OpCodes.Stloc, gInterpLoc);
            RequireGetter("get_GradientStops");
            il.Emit(OpCodes.Stloc, gStopsLoc);
            il.Emit(OpCodes.Ldloc, gStopsLoc);
            il.Emit(OpCodes.Ldloc, gOpLoc);
            il.Emit(OpCodes.Ldloc, gInterpLoc);
            il.Emit(OpCodes.Call, createGradientLut);
            il.Emit(OpCodes.Stloc, gLutLoc);
            il.Emit(OpCodes.Ldloc, gLutLoc);
            il.Emit(OpCodes.Ldloc, gx0Loc);
            il.Emit(OpCodes.Ldloc, gy0Loc);
            il.Emit(OpCodes.Ldloc, gx1Loc);
            il.Emit(OpCodes.Ldloc, gy1Loc);
            il.Emit(OpCodes.Ldloc, gOpLoc);
            il.Emit(OpCodes.Ldloc, gSpreadLoc);
            il.Emit(OpCodes.Newobj, cpuGradientCtor);
            il.Emit(OpCodes.Stloc, resLoc);
            il.Emit(OpCodes.Br, stored);
            il.Append(tryTextLayout);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Callvirt, typeFullName);
            il.Emit(OpCodes.Ldstr, "PaintDotNet.UI.Media.TextLayout");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, unknown);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Ldstr, "get_Text");
            il.Emit(OpCodes.Call, typeGetMethod);
            il.Emit(OpCodes.Stloc, miLoc);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Brfalse, unknown);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, miInvoke);
            il.Emit(OpCodes.Castclass, module.TypeSystem.String);
            il.Emit(OpCodes.Stloc, textLoc);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Ldstr, "get_FontSize");
            il.Emit(OpCodes.Call, typeGetMethod);
            il.Emit(OpCodes.Stloc, miLoc);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Brfalse, unknown);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, miInvoke);
            il.Emit(OpCodes.Unbox_Any, module.TypeSystem.Double);
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stloc, fsLoc);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Ldstr, "get_MaxWidth");
            il.Emit(OpCodes.Call, typeGetMethod);
            il.Emit(OpCodes.Stloc, miLoc);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Brfalse, unknown);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, miInvoke);
            il.Emit(OpCodes.Unbox_Any, module.TypeSystem.Double);
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stloc, maxWLoc);
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Ldstr, "get_MaxHeight");
            il.Emit(OpCodes.Call, typeGetMethod);
            il.Emit(OpCodes.Stloc, miLoc);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Brfalse, unknown);
            il.Emit(OpCodes.Ldloc, miLoc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, miInvoke);
            il.Emit(OpCodes.Unbox_Any, module.TypeSystem.Double);
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stloc, maxHLoc);
            il.Emit(OpCodes.Ldloc, textLoc);
            il.Emit(OpCodes.Ldloc, fsLoc);
            il.Emit(OpCodes.Ldloc, maxWLoc);
            il.Emit(OpCodes.Ldloc, maxHLoc);
            il.Emit(OpCodes.Newobj, textLayoutCtor);
            il.Emit(OpCodes.Stloc, resLoc);
            il.Emit(OpCodes.Br, stored);
            il.Append(unknown);
            il.Emit(OpCodes.Ldstr, "CpuDrawingContext.GetCachedOrCreateResource has no headless backend for ");
            il.Emit(OpCodes.Ldloc, provTypeLoc);
            il.Emit(OpCodes.Callvirt, typeFullName);
            il.Emit(OpCodes.Call, strConcat);
            il.Emit(OpCodes.Newobj, nseCtor);
            il.Emit(OpCodes.Throw);
            il.Append(stored);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, module.TypeSystem.Object);
            il.Emit(OpCodes.Stloc, arrLoc);
            il.Emit(OpCodes.Ldloc, arrLoc);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, verLoc);
            il.Emit(OpCodes.Box, module.TypeSystem.Int64);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Ldloc, arrLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldloc, resLoc);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resCacheField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, arrLoc);
            il.Emit(OpCodes.Callvirt, htSet);
            il.Emit(OpCodes.Ldloc, resLoc);
            il.Emit(OpCodes.Castclass, resCache.ReturnType);
            il.Emit(OpCodes.Ret);
        }
        // Shared nullable probes: harvested whole (opcode included) so
        // the byref-nullable calling convention stays compiler-grade;
        // built driver-style on the target's own Nullable`1 reference
        // when no proven call site survives neutering.
        (OpCode Op, MethodReference Ref) NeedNullableProbe(string arg, string probe)
        {
            var hit = HarvestCpuCall(module, "System.Nullable`1", probe, false);
            if (hit is { } h && h.Ref.DeclaringType is GenericInstanceType hgi
                && hgi.GenericArguments.Count == 1
                && hgi.GenericArguments[0].FullName == arg)
                return h;
            // Nullable probes prefer a harvested compiler-grade call site;
            // the fallback builds on the module's own Nullable`1 reference.
            TypeReference elem = OwnedCpuElement(module, "System", "Nullable`1");
            TypeReference a = OwnedCpuRef(module, arg);
            var inst = new GenericInstanceType(elem);
            inst.GenericArguments.Add(a);
            // T-returning probes keep the open generic parameter (!0 of
            // Nullable<T> itself): pre-substituting the concrete argument
            // into the return mismatches CoreLib's declaration and the call
            // never resolves (live MissingMethod on get_Value while the
            // bool-returning get_HasValue beside it resolves fine).
            TypeReference ret = probe == "get_HasValue"
                ? module.TypeSystem.Boolean
                : ResolveCpuDef(elem).GenericParameters[0];
            var mr = new MethodReference(probe, ret, inst) { HasThis = true };
            return (OpCodes.Call, mr);
        }
        (OpCode Op, MethodReference Ref) rectHas =
            NeedNullableProbe("PaintDotNet.Rendering.RectFloat", "get_HasValue");
        (OpCode Op, MethodReference Ref) rectValue =
            NeedNullableProbe("PaintDotNet.Rendering.RectFloat", "get_Value");
        (OpCode Op, MethodReference Ref) perspHas =
            NeedNullableProbe("PaintDotNet.Rendering.Matrix4x4Float", "get_HasValue");

        // -- DrawBitmap(bitmap, destRect&, opacity, interp, srcRect&,
        // perspective&): the sprite blit. A set perspective is out of
        // scope for stamps and skips; null rects mean whole bitmap.
        MethodDefinition contractDrawBitmap = Contract( "DrawBitmap",
            m => m.Parameters.Count == 6
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Direct2D1.IDeviceBitmap");
        var drawBitmap = Impl(contractDrawBitmap);
        {
            var il = drawBitmap.Body.GetILProcessor();
            MethodReference rectX = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_X");
            MethodReference rectY = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Y");
            MethodReference rectW = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Width");
            MethodReference rectH = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Height");
            TypeReference rectType = OwnedCpuRef(module, "PaintDotNet.Rendering.RectFloat");
            var bmpLoc = new VariableDefinition(cpuBmp);
            var rectLoc = new VariableDefinition(rectType);
            var dxLoc = new VariableDefinition(module.TypeSystem.Single);
            var dyLoc = new VariableDefinition(module.TypeSystem.Single);
            var dwLoc = new VariableDefinition(module.TypeSystem.Single);
            var dhLoc = new VariableDefinition(module.TypeSystem.Single);
            var hasDestLoc = new VariableDefinition(module.TypeSystem.Int32);
            var sxLoc = new VariableDefinition(module.TypeSystem.Single);
            var syLoc = new VariableDefinition(module.TypeSystem.Single);
            var swLoc = new VariableDefinition(module.TypeSystem.Single);
            var shLoc = new VariableDefinition(module.TypeSystem.Single);
            var hasSrcLoc = new VariableDefinition(module.TypeSystem.Int32);
            foreach (var v in new[] { bmpLoc, rectLoc, dxLoc, dyLoc, dwLoc, dhLoc, hasDestLoc,
                sxLoc, syLoc, swLoc, shLoc, hasSrcLoc })
                drawBitmap.Body.Variables.Add(v);
            drawBitmap.Body.InitLocals = true;

            MethodReference getProxySourceBmp = Cpu("GetProxySourceImage", 1);
            var isBmp = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, cpuBmp);
            il.Emit(OpCodes.Stloc, bmpLoc);
            il.Emit(OpCodes.Ldloc, bmpLoc);
            il.Emit(OpCodes.Brtrue, isBmp);
            // ImageStrip item images (layer/history thumbnails) arrive
            // as BitmapImage/effect shells, not raw uploads: unwrap to
            // the shared bitmap exactly like DrawImage does. Anything
            // else keeps today's silent skip.
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, getProxySourceBmp);
            il.Emit(OpCodes.Isinst, cpuBmp);
            il.Emit(OpCodes.Stloc, bmpLoc);
            il.Emit(OpCodes.Ldloc, bmpLoc);
            var doneLabel = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, doneLabel);
            il.Append(isBmp);
            // Perspective: anything set is beyond stamp scope.
            il.Emit(OpCodes.Ldarg, drawBitmap.Parameters[5]);
            il.Emit(perspHas.Op, perspHas.Ref);
            il.Emit(OpCodes.Brtrue, doneLabel);

            // destRect (arg1) or zeros + flag.
            il.Emit(OpCodes.Ldarg, drawBitmap.Parameters[1]);
            il.Emit(rectHas.Op, rectHas.Ref);
            var noDestLabel = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, noDestLabel);
            il.Emit(OpCodes.Ldarg, drawBitmap.Parameters[1]);
            il.Emit(rectValue.Op, rectValue.Ref);
            il.Emit(OpCodes.Stloc, rectLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectX);
            il.Emit(OpCodes.Stloc, dxLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectY);
            il.Emit(OpCodes.Stloc, dyLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectW);
            il.Emit(OpCodes.Stloc, dwLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectH);
            il.Emit(OpCodes.Stloc, dhLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, hasDestLoc);
            il.Append(noDestLabel);

            // srcRect (arg4) or zeros + flag.
            il.Emit(OpCodes.Ldarg, drawBitmap.Parameters[4]);
            il.Emit(rectHas.Op, rectHas.Ref);
            var noSrcLabel = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, noSrcLabel);
            il.Emit(OpCodes.Ldarg, drawBitmap.Parameters[4]);
            il.Emit(rectValue.Op, rectValue.Ref);
            il.Emit(OpCodes.Stloc, rectLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectX);
            il.Emit(OpCodes.Stloc, sxLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectY);
            il.Emit(OpCodes.Stloc, syLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectW);
            il.Emit(OpCodes.Stloc, swLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectH);
            il.Emit(OpCodes.Stloc, shLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, hasSrcLoc);
            il.Append(noSrcLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Ldloc, bmpLoc);
            il.Emit(OpCodes.Ldfld, bmpHandleField);
            il.Emit(OpCodes.Ldloc, dxLoc);
            il.Emit(OpCodes.Ldloc, dyLoc);
            il.Emit(OpCodes.Ldloc, dwLoc);
            il.Emit(OpCodes.Ldloc, dhLoc);
            il.Emit(OpCodes.Ldloc, hasDestLoc);
            il.Emit(OpCodes.Ldloc, sxLoc);
            il.Emit(OpCodes.Ldloc, syLoc);
            il.Emit(OpCodes.Ldloc, swLoc);
            il.Emit(OpCodes.Ldloc, shLoc);
            il.Emit(OpCodes.Ldloc, hasSrcLoc);
            il.Emit(OpCodes.Ldarg, drawBitmap.Parameters[2]);
            il.Emit(OpCodes.Ldarg, drawBitmap.Parameters[3]);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Cgt);
            il.Emit(OpCodes.Call, drawBitmapCpu);
            il.Append(doneLabel);
            il.Emit(OpCodes.Ret);
        }

        // -- FillRectangle(rect, brush): hard square stamps.
        MethodDefinition contractFillRect = Contract( "FillRectangle",
            m => m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Rendering.RectFloat");
        var fillRect = Impl(contractFillRect);
        EmitCpuFill(module, fillRect, targetField, cpuBrush,
            brushOpacityField, brushRField, brushGField, brushBField, brushAField,
            fillRectCpu, isEllipse: false);

        // -- FillEllipse(ellipse, brush): hard round stamps.
        MethodDefinition contractFillEllipse = Contract( "FillEllipse",
            m => m.Parameters.Count == 2
                && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Direct2D1.IDeviceBrush");
        var fillEllipse = Impl(contractFillEllipse);
        EmitCpuFill(module, fillEllipse, targetField, cpuBrush,
            brushOpacityField, brushRField, brushGField, brushBField, brushAField,
            fillEllipseCpu, isEllipse: true);

        // -- CreateCommandList(): deferred recordings are meaningless
        // headless (draws execute immediately), so this answers an inert
        // token; DrawImage skips it, Close/Stream are no-ops.
        MethodDefinition contractCreateCmdList = Contract("CreateCommandList", m => m.Parameters.Count == 0);
        var createCmdList = Impl(contractCreateCmdList);
        {
            var il = createCmdList.Body.GetILProcessor();
            il.Emit(OpCodes.Newobj, cmdListCtor);
            il.Emit(OpCodes.Ret);
        }
        // -- GetTarget(): the active redirect list, else null (no device
        // target exists headless). The vendor CommandListDrawingScope
        // saves this and restores it via SetTarget, round-tripping the
        // redirect below.
        MethodDefinition contractGetTarget = Contract("GetTarget", m => m.Parameters.Count == 0);
        var getTarget = Impl(contractGetTarget);
        {
            var il = getTarget.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, redirField);
            il.Emit(OpCodes.Castclass, MapCpuType(module, contractGetTarget.ReturnType));
            il.Emit(OpCodes.Ret);
        }
        // -- SetTarget(image): command-list redirect. SetTarget(list)
        // swaps the fill target to the list scratch (created on first
        // redirect, sized to the live target, live matrix copied in);
        // the vendor scope restore SetTarget(null) swaps back. Nested
        // redirects and foreign images keep today's no-op. Fixture
        // modules without the contract keep the tail-loop default.
        MethodDefinition? setTargetContract = contracts.FirstOrDefault(
            m => m.Name == "SetTarget" && m.Parameters.Count == 1);
        if (setTargetContract != null)
        {
            implemented.Add(setTargetContract);
            var setTarget = new MethodDefinition(
                "SetTarget",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                module.TypeSystem.Void);
            foreach (var p in setTargetContract.Parameters)
                setTarget.Parameters.Add(new ParameterDefinition(
                    p.Name, p.Attributes, MapCpuType(module, p.ParameterType)));
            setTarget.Body.MaxStackSize = 32;
            ctx.Methods.Add(setTarget);
            var sil = setTarget.Body.GetILProcessor();
            var listLoc = new VariableDefinition(cpuCmdList);
            var stwLoc = new VariableDefinition(module.TypeSystem.Int32);
            var sthLoc = new VariableDefinition(module.TypeSystem.Int32);
            var stmpLoc = new VariableDefinition(module.TypeSystem.Int64);
            var stm11Loc = new VariableDefinition(module.TypeSystem.Single);
            var stm12Loc = new VariableDefinition(module.TypeSystem.Single);
            var stm21Loc = new VariableDefinition(module.TypeSystem.Single);
            var stm22Loc = new VariableDefinition(module.TypeSystem.Single);
            var st31Loc = new VariableDefinition(module.TypeSystem.Single);
            var st32Loc = new VariableDefinition(module.TypeSystem.Single);
            foreach (var v in new[] { listLoc, stwLoc, sthLoc, stmpLoc, stm11Loc, stm12Loc, stm21Loc, stm22Loc, st31Loc, st32Loc })
                setTarget.Body.Variables.Add(v);
            setTarget.Body.InitLocals = true;
            FieldDefinition scratchF = cpuCmdList.Fields.First(f => f.Name == "_scratch");
            FieldDefinition savedF = cpuCmdList.Fields.First(f => f.Name == "_saved");
            var notList = sil.Create(OpCodes.Nop);
            var haveScratch = sil.Create(OpCodes.Nop);
            var doneST = sil.Create(OpCodes.Nop);
            sil.Emit(OpCodes.Ldarg_1);
            sil.Emit(OpCodes.Isinst, cpuCmdList);
            sil.Emit(OpCodes.Stloc, listLoc);
            sil.Emit(OpCodes.Ldloc, listLoc);
            sil.Emit(OpCodes.Brfalse, notList);
            sil.Emit(OpCodes.Ldarg_0);
            sil.Emit(OpCodes.Ldfld, redirField);
            sil.Emit(OpCodes.Brtrue, doneST);
            sil.Emit(OpCodes.Ldloc, listLoc);
            sil.Emit(OpCodes.Ldarg_0);
            sil.Emit(OpCodes.Ldfld, targetField);
            sil.Emit(OpCodes.Stfld, savedF);
            sil.Emit(OpCodes.Ldloc, listLoc);
            sil.Emit(OpCodes.Ldfld, scratchF);
            sil.Emit(OpCodes.Brtrue, haveScratch);
            sil.Emit(OpCodes.Ldarg_0);
            sil.Emit(OpCodes.Ldfld, targetField);
            sil.Emit(OpCodes.Ldloca_S, stwLoc);
            sil.Emit(OpCodes.Ldloca_S, sthLoc);
            sil.Emit(OpCodes.Call, getTargetSizeRedirectCpu);
            sil.Emit(OpCodes.Ldloc, stwLoc);
            sil.Emit(OpCodes.Ldloc, sthLoc);
            sil.Emit(OpCodes.Call, createScratchCpu);
            sil.Emit(OpCodes.Stloc, stmpLoc);
            sil.Emit(OpCodes.Ldloc, listLoc);
            sil.Emit(OpCodes.Ldloc, stmpLoc);
            sil.Emit(OpCodes.Stfld, scratchF);
            sil.Emit(OpCodes.Ldarg_0);
            sil.Emit(OpCodes.Ldfld, targetField);
            sil.Emit(OpCodes.Ldloca_S, stm11Loc);
            sil.Emit(OpCodes.Ldloca_S, stm12Loc);
            sil.Emit(OpCodes.Ldloca_S, stm21Loc);
            sil.Emit(OpCodes.Ldloca_S, stm22Loc);
            sil.Emit(OpCodes.Ldloca_S, st31Loc);
            sil.Emit(OpCodes.Ldloca_S, st32Loc);
            sil.Emit(OpCodes.Call, getTransformCpu);
            sil.Emit(OpCodes.Ldloc, stmpLoc);
            sil.Emit(OpCodes.Ldloc, stm11Loc);
            sil.Emit(OpCodes.Ldloc, stm12Loc);
            sil.Emit(OpCodes.Ldloc, stm21Loc);
            sil.Emit(OpCodes.Ldloc, stm22Loc);
            sil.Emit(OpCodes.Ldloc, st31Loc);
            sil.Emit(OpCodes.Ldloc, st32Loc);
            sil.Emit(OpCodes.Call, setTransformCpu);
            sil.Append(haveScratch);
            sil.Emit(OpCodes.Ldarg_0);
            sil.Emit(OpCodes.Ldloc, listLoc);
            sil.Emit(OpCodes.Ldfld, scratchF);
            sil.Emit(OpCodes.Stfld, targetField);
            sil.Emit(OpCodes.Ldarg_0);
            sil.Emit(OpCodes.Ldloc, listLoc);
            sil.Emit(OpCodes.Stfld, redirField);
            sil.Emit(OpCodes.Br, doneST);
            sil.Append(notList);
            sil.Emit(OpCodes.Ldarg_1);
            sil.Emit(OpCodes.Brtrue, doneST);
            sil.Emit(OpCodes.Ldarg_0);
            sil.Emit(OpCodes.Ldfld, redirField);
            sil.Emit(OpCodes.Isinst, cpuCmdList);
            sil.Emit(OpCodes.Stloc, listLoc);
            sil.Emit(OpCodes.Ldloc, listLoc);
            sil.Emit(OpCodes.Brfalse, doneST);
            sil.Emit(OpCodes.Ldarg_0);
            sil.Emit(OpCodes.Ldloc, listLoc);
            sil.Emit(OpCodes.Ldfld, savedF);
            sil.Emit(OpCodes.Stfld, targetField);
            sil.Emit(OpCodes.Ldarg_0);
            sil.Emit(OpCodes.Ldnull);
            sil.Emit(OpCodes.Stfld, redirField);
            sil.Append(doneST);
            sil.Emit(OpCodes.Ret);
        }
        // -- Clear(color?): fills the whole target (transparent when the
        // color is absent). Fresh bitmaps start zeroed, but reused ones
        // (sprite scratch) need a real clear.
        MethodReference clearTargetCpu = Cpu("ClearTarget", 6);
        MethodDefinition contractClear = Contract("Clear",
            m => m.Parameters.Count == 1
                && UnwrapByref(m.Parameters[0].ParameterType) is ByReferenceType clearBr
                && clearBr.ElementType.FullName == "System.Nullable`1<PaintDotNet.Imaging.ColorRgba128Float>");
        var clear = Impl(contractClear);
        {
            var il = clear.Body.GetILProcessor();
            (OpCode Op, MethodReference Ref) colorHas = NeedNullableProbe("PaintDotNet.Imaging.ColorRgba128Float", "get_HasValue");
            (OpCode Op, MethodReference Ref) colorValue = NeedNullableProbe("PaintDotNet.Imaging.ColorRgba128Float", "get_Value");
            MethodReference getR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
            MethodReference getG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
            MethodReference getB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
            MethodReference getA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
            TypeReference colorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.ColorRgba128Float");
            var tmpColorLoc = new VariableDefinition(colorRef);
            var crLoc = new VariableDefinition(module.TypeSystem.Single);
            var cgLoc = new VariableDefinition(module.TypeSystem.Single);
            var cbLoc = new VariableDefinition(module.TypeSystem.Single);
            var caLoc = new VariableDefinition(module.TypeSystem.Single);
            foreach (var v in new[] { tmpColorLoc, crLoc, cgLoc, cbLoc, caLoc })
                clear.Body.Variables.Add(v);
            clear.Body.InitLocals = true;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Ldarg, clear.Parameters[0]);
            il.Emit(colorHas.Op, colorHas.Ref);
            var noColor = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, noColor);
            il.Emit(OpCodes.Ldarg, clear.Parameters[0]);
            il.Emit(colorValue.Op, colorValue.Ref);
            il.Emit(OpCodes.Stloc, tmpColorLoc);
            il.Emit(OpCodes.Ldloca_S, tmpColorLoc);
            il.Emit(OpCodes.Call, getR);
            il.Emit(OpCodes.Stloc, crLoc);
            il.Emit(OpCodes.Ldloca_S, tmpColorLoc);
            il.Emit(OpCodes.Call, getG);
            il.Emit(OpCodes.Stloc, cgLoc);
            il.Emit(OpCodes.Ldloca_S, tmpColorLoc);
            il.Emit(OpCodes.Call, getB);
            il.Emit(OpCodes.Stloc, cbLoc);
            il.Emit(OpCodes.Ldloca_S, tmpColorLoc);
            il.Emit(OpCodes.Call, getA);
            il.Emit(OpCodes.Stloc, caLoc);
            il.Emit(OpCodes.Ldloc, crLoc);
            il.Emit(OpCodes.Ldloc, cgLoc);
            il.Emit(OpCodes.Ldloc, cbLoc);
            il.Emit(OpCodes.Ldloc, caLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            var clearDone = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, clearDone);
            il.Append(noColor);
            il.Emit(OpCodes.Ldc_R4, 0f);
            il.Emit(OpCodes.Ldc_R4, 0f);
            il.Emit(OpCodes.Ldc_R4, 0f);
            il.Emit(OpCodes.Ldc_R4, 0f);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Append(clearDone);
            il.Emit(OpCodes.Call, clearTargetCpu);
            il.Emit(OpCodes.Ret);
        }
        // -- DrawImage(image, destPoint?, srcRect?, interp, composite):
        // effect/command-list output composited back (sprite dab). CPU
        // bitmaps blit like DrawBitmap at full opacity; command lists
        // with a redirect scratch blit it back and consume it
        // (single-playback headless); anything else names its type.
        MethodDefinition contractDrawImage = Contract("DrawImage",
            m => m.Parameters.Count == 5
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Direct2D1.IDeviceImage");
        var drawImage = Impl(contractDrawImage);
        {
            // Procedural checker evaluation: CropEffect(checker) and
            // bare-checker draws evaluate on the CPU (classic colors,
            // target bounds when the crop rect is device-bound). The
            // branch only emits when every token resolves in this
            // module; otherwise the loud unknown-image throw stays.
            MethodReference drawCheckerCpu = Cpu("DrawChecker", 8);
            MethodReference drawProceduralCpu = Cpu("DrawProceduralEffect", 6);
            MethodReference drawCompositeCpu = Cpu("DrawCompositeImage", 12);
            MethodReference getTargetSizeCpu = Cpu("GetTargetSize", 3);
            TypeReference? cropFxRef = module.GetType("PaintDotNet.Direct2D1.Effects.CropEffect");
            TypeReference? checkerFxRef = null;
            TypeReference? imageRef = null;
            MethodReference? devFxGetInputRef = null;
            bool hasCheckerPath = false;
            try
            {
                checkerFxRef ??= module.ImportReference(ResolveExternalDef(module, "PaintDotNet.Core", "PaintDotNet.VisualStyling.CheckerboardEffect"));
                imageRef ??= module.ImportReference(ResolveExternalDef(module, "PaintDotNet.Windows", "PaintDotNet.Direct2D1.IDeviceImage"));
                if (cropFxRef is null || checkerFxRef is null || imageRef is null)
                    throw new InvalidOperationException("checker refs missing");
                // Input via the IDeviceEffect interface: GetInput delegates to
                // the inner CPU effect. (A MemberRef walk up to
                // DeviceEffect<CropProps>.get_Properties trips Mono class
                // loading on WASM, surfacing as null storage at the input
                // accessor, so the props chain is not called here.)
                TypeDefinition devFxDef = ResolveCpuDef(LocalOrOwnedCpuRef(module, "PaintDotNet.Direct2D1.IDeviceEffect"));
                MethodDefinition? devFxGetInputDef = devFxDef.Methods.FirstOrDefault(m => m.Name == "GetInput" && m.Parameters.Count == 1);
                if (devFxGetInputDef is null)
                    throw new InvalidOperationException("IDeviceEffect.GetInput missing");
                devFxGetInputRef = module.ImportReference(devFxGetInputDef);
                hasCheckerPath = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Mono.Cecil.ResolutionException || ex is ArgumentException)
            { hasCheckerPath = false; }
            // ColorManagementEffect used to Ret here as a headless identity
            // no-op ("already-painted source"), but that blanks primary
            // blits like the color wheel: the effect falls through to the
            // proxy unwrap below (GetProxySourceImage allowlists
            // ColorManagementEffect and blits its Source input; the srgb/scRGB
            // transform itself stays identity headless).
            var il = drawImage.Body.GetILProcessor();
            MethodReference rectX = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_X");
            MethodReference rectY = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Y");
            MethodReference rectW = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Width");
            MethodReference rectH = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Height");
            (OpCode Op, MethodReference Ref) ptHas = NeedNullableProbe("PaintDotNet.Rendering.Point2Float", "get_HasValue");
            (OpCode Op, MethodReference Ref) ptValue = NeedNullableProbe("PaintDotNet.Rendering.Point2Float", "get_Value");
            MethodReference ptX = StructCpuGetter(module, "PaintDotNet.Rendering.Point2Float", "get_X");
            MethodReference ptY = StructCpuGetter(module, "PaintDotNet.Rendering.Point2Float", "get_Y");
            TypeReference ptRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Rendering.Point2Float");
            TypeReference rectType = OwnedCpuRef(module, "PaintDotNet.Rendering.RectFloat");
            var imgLoc = new VariableDefinition(cpuBmp);
            var pointLoc = new VariableDefinition(ptRef);
            var rectLoc = new VariableDefinition(rectType);
            var dxLoc = new VariableDefinition(module.TypeSystem.Single);
            var dyLoc = new VariableDefinition(module.TypeSystem.Single);
            var dwLoc = new VariableDefinition(module.TypeSystem.Single);
            var dhLoc = new VariableDefinition(module.TypeSystem.Single);
            var hasDestLoc = new VariableDefinition(module.TypeSystem.Int32);
            var sxLoc = new VariableDefinition(module.TypeSystem.Single);
            var syLoc = new VariableDefinition(module.TypeSystem.Single);
            var swLoc = new VariableDefinition(module.TypeSystem.Single);
            var shLoc = new VariableDefinition(module.TypeSystem.Single);
            var hasSrcLoc = new VariableDefinition(module.TypeSystem.Int32);
            var procTwLoc = new VariableDefinition(module.TypeSystem.Int32);
            var procThLoc = new VariableDefinition(module.TypeSystem.Int32);
            foreach (var v in new[] { imgLoc, pointLoc, rectLoc, dxLoc, dyLoc, dwLoc, dhLoc, hasDestLoc, sxLoc, syLoc, swLoc, shLoc, hasSrcLoc, procTwLoc, procThLoc })
                drawImage.Body.Variables.Add(v);
            drawImage.Body.InitLocals = true;
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, cpuBmp);
            il.Emit(OpCodes.Stloc, imgLoc);
            var hasBmp = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, imgLoc);
            il.Emit(OpCodes.Brtrue, hasBmp);
            var imgDone = il.Create(OpCodes.Nop);
            // Direct DrawImage(list): blit a recorded redirect scratch
            // back in desktop order (the palette replays its layer this
            // way). Lists without a scratch painted direct (or stayed
            // empty): today's skip. Long branches: short forms here
            // silently truncated once the method grew (rev187 gap target).
            var earlyListLoc = new VariableDefinition(cpuCmdList);
            var earlyScratchLoc = new VariableDefinition(module.TypeSystem.Int64);
            drawImage.Body.Variables.Add(earlyListLoc);
            drawImage.Body.Variables.Add(earlyScratchLoc);
            FieldDefinition earlyScratchF = cpuCmdList.Fields.First(f => f.Name == "_scratch");
            var notListEarly = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, cpuCmdList);
            il.Emit(OpCodes.Stloc, earlyListLoc);
            il.Emit(OpCodes.Ldloc, earlyListLoc);
            il.Emit(OpCodes.Brfalse, notListEarly);
            il.Emit(OpCodes.Ldloc, earlyListLoc);
            il.Emit(OpCodes.Ldfld, earlyScratchF);
            il.Emit(OpCodes.Stloc, earlyScratchLoc);
            il.Emit(OpCodes.Ldloc, earlyScratchLoc);
            il.Emit(OpCodes.Brfalse, imgDone);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Ldloc, earlyScratchLoc);
            il.Emit(OpCodes.Call, blitScratchCpu);
            // Consume on first playback (single-playback headless): the
            // vendor palette draws the same list again right after (raw
            // list after the wrapped effect); a second blit would double-
            // paint. Leftovers die in the finalizer.
            il.Emit(OpCodes.Ldloc, earlyScratchLoc);
            il.Emit(OpCodes.Call, freeScratchCpu);
            il.Emit(OpCodes.Ldloc, earlyListLoc);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stfld, earlyScratchF);
            il.Emit(OpCodes.Br, imgDone);
            il.Append(notListEarly);
            if (hasCheckerPath)
            {
                var fxLoc = new VariableDefinition(cropFxRef!);
                var inImgLoc = new VariableDefinition(imageRef!);
                var checkerLoc = new VariableDefinition(checkerFxRef!);
                var twLoc = new VariableDefinition(module.TypeSystem.Int32);
                var thLoc = new VariableDefinition(module.TypeSystem.Int32);
                foreach (var v in new[] { fxLoc, inImgLoc, checkerLoc, twLoc, thLoc })
                    drawImage.Body.Variables.Add(v);
                var toThrow = il.Create(OpCodes.Nop);
                var tryDirect = il.Create(OpCodes.Nop);
                var noSrc2 = il.Create(OpCodes.Nop);
                var blit2 = il.Create(OpCodes.Nop);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Isinst, cropFxRef);
                il.Emit(OpCodes.Stloc, fxLoc);
                il.Emit(OpCodes.Ldloc, fxLoc);
                il.Emit(OpCodes.Brfalse, tryDirect);
                il.Emit(OpCodes.Ldloc, fxLoc);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Callvirt, devFxGetInputRef!);
                il.Emit(OpCodes.Stloc, inImgLoc);
                il.Emit(OpCodes.Ldloc, inImgLoc);
                il.Emit(OpCodes.Isinst, checkerFxRef);
                il.Emit(OpCodes.Stloc, checkerLoc);
                il.Emit(OpCodes.Ldloc, checkerLoc);
                il.Emit(OpCodes.Brfalse, toThrow);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, targetField);
                il.Emit(OpCodes.Ldloca_S, twLoc);
                il.Emit(OpCodes.Ldloca_S, thLoc);
                il.Emit(OpCodes.Call, getTargetSizeCpu);
                il.Emit(OpCodes.Ldc_R4, 0f);
                il.Emit(OpCodes.Stloc, sxLoc);
                il.Emit(OpCodes.Ldc_R4, 0f);
                il.Emit(OpCodes.Stloc, syLoc);
                il.Emit(OpCodes.Ldloc, twLoc);
                il.Emit(OpCodes.Conv_R4);
                il.Emit(OpCodes.Stloc, swLoc);
                il.Emit(OpCodes.Ldloc, thLoc);
                il.Emit(OpCodes.Conv_R4);
                il.Emit(OpCodes.Stloc, shLoc);
                il.Emit(OpCodes.Br, blit2);
                il.Append(tryDirect);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Isinst, checkerFxRef);
                il.Emit(OpCodes.Stloc, checkerLoc);
                il.Emit(OpCodes.Ldloc, checkerLoc);
                il.Emit(OpCodes.Brfalse, toThrow);
                il.Emit(OpCodes.Ldarg, drawImage.Parameters[2]);
                il.Emit(rectHas.Op, rectHas.Ref);
                il.Emit(OpCodes.Brfalse, noSrc2);
                il.Emit(OpCodes.Ldarg, drawImage.Parameters[2]);
                il.Emit(rectValue.Op, rectValue.Ref);
                il.Emit(OpCodes.Stloc, rectLoc);
                il.Emit(OpCodes.Ldloca_S, rectLoc);
                il.Emit(OpCodes.Call, rectX);
                il.Emit(OpCodes.Stloc, sxLoc);
                il.Emit(OpCodes.Ldloca_S, rectLoc);
                il.Emit(OpCodes.Call, rectY);
                il.Emit(OpCodes.Stloc, syLoc);
                il.Emit(OpCodes.Ldloca_S, rectLoc);
                il.Emit(OpCodes.Call, rectW);
                il.Emit(OpCodes.Stloc, swLoc);
                il.Emit(OpCodes.Ldloca_S, rectLoc);
                il.Emit(OpCodes.Call, rectH);
                il.Emit(OpCodes.Stloc, shLoc);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Stloc, hasSrcLoc);
                il.Emit(OpCodes.Br, blit2);
                il.Append(noSrc2);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, targetField);
                il.Emit(OpCodes.Ldloca_S, twLoc);
                il.Emit(OpCodes.Ldloca_S, thLoc);
                il.Emit(OpCodes.Call, getTargetSizeCpu);
                il.Emit(OpCodes.Ldc_R4, 0f);
                il.Emit(OpCodes.Stloc, sxLoc);
                il.Emit(OpCodes.Ldc_R4, 0f);
                il.Emit(OpCodes.Stloc, syLoc);
                il.Emit(OpCodes.Ldloc, twLoc);
                il.Emit(OpCodes.Conv_R4);
                il.Emit(OpCodes.Stloc, swLoc);
                il.Emit(OpCodes.Ldloc, thLoc);
                il.Emit(OpCodes.Conv_R4);
                il.Emit(OpCodes.Stloc, shLoc);
                il.Append(blit2);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, targetField);
                il.Emit(OpCodes.Ldloc, sxLoc);
                il.Emit(OpCodes.Ldloc, syLoc);
                il.Emit(OpCodes.Ldloc, swLoc);
                il.Emit(OpCodes.Ldloc, shLoc);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)0xFFFFFFFF));
                il.Emit(OpCodes.Ldc_I4, unchecked((int)0xFFC8C8C8));
                il.Emit(OpCodes.Ldc_I4_8);
                il.Emit(OpCodes.Call, drawCheckerCpu);
                il.Emit(OpCodes.Ret);
                il.Append(toThrow);
            }
            // Effect proxies (palette swatches draw through a color
            // management proxy): unwrap pass-through inputs to the
            // shared bitmap blit; anything else stays loud below.
            var srcLoc = new VariableDefinition(module.TypeSystem.Object);
            drawImage.Body.Variables.Add(srcLoc);
            MethodReference getProxySourceCpu = Cpu("GetProxySourceImage", 1);
            var noProxy = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, getProxySourceCpu);
            il.Emit(OpCodes.Stloc, srcLoc);
            il.Emit(OpCodes.Ldloc, srcLoc);
            il.Emit(OpCodes.Brfalse, noProxy);
            il.Emit(OpCodes.Ldloc, srcLoc);
            il.Emit(OpCodes.Isinst, cpuBmp);
            il.Emit(OpCodes.Stloc, imgLoc);
            il.Emit(OpCodes.Ldloc, imgLoc);
            // Non-bitmap proxy results (procedural effects) share the
            // rect parse, then take the procedural render at blit.
            il.Emit(OpCodes.Brfalse, hasBmp);
            il.Emit(OpCodes.Br, hasBmp);
            il.Append(noProxy);
            // ColorManagementEffect identity no-op (gradient strips): the
            // source was already painted into the target by the preceding
            // FillRectangle, so a failed unwrap Retains instead of throwing
            // (the throw magenta-placeholders the whole control). Other
            // effects keep the loud unknown-image throw. Guarded: modules
            // without the Windows.Core reference keep today's behavior.
            TypeReference? colorMgmtRef = module.GetType("PaintDotNet.Direct2D1.Effects.ColorManagementEffect");
            TypeReference? colorMgmtProxyRef = module.GetType("PaintDotNet.Direct2D1.Effects.Proxies.IColorManagementEffectProxy");
            if (colorMgmtRef == null || colorMgmtProxyRef == null)
            {
                var coreScope = module.AssemblyReferences.FirstOrDefault(r => r.Name == "PaintDotNet.Windows.Core");
                if (coreScope != null)
                {
                    colorMgmtRef ??= new TypeReference("PaintDotNet.Direct2D1.Effects", "ColorManagementEffect", module, coreScope);
                    colorMgmtProxyRef ??= new TypeReference("PaintDotNet.Direct2D1.Effects.Proxies", "IColorManagementEffectProxy", module, coreScope);
                }
            }
            if (colorMgmtRef != null || colorMgmtProxyRef != null)
            {
                var doRet = il.Create(OpCodes.Ret);
                var realThrow = il.Create(OpCodes.Nop);
                // Forward (palette wells): a ColorManagementEffect
                // wrapping a redirected command list must replay the
                // list scratch into the current target. The old bare
                // Ret dropped it: palette fills sit in the list scratch
                // (never on the live surface), so the no-op composited
                // an empty wrapper list. Non-list sources keep the
                // no-op (gradient strips). Long branches: this method
                // outgrew short range (rev187 gap target).
                // rev190: read the wrapped list through
                // Properties.Source (the vendor's own channel) instead
                // of GetInput(0): the device-effect input slot is null
                // headless (the inner D2D effect is never created), so
                // GetInput NREs and the vendor render guard swallows the
                // whole composite. The proxy keeps the no-op (its
                // property shape is unknown).
                // rev191: the get_Properties call goes through the
                // NON-GENERIC DeviceEffect base (exactly what the
                // generic DeviceEffect<Props>.get_Properties does
                // internally: base call + unbox; here base call +
                // Castclass to Props). No generic token appears in the
                // emitted IL at all: generic-interface dispatch trips
                // Mono class loading on WASM, and the suite's nested
                // module-scope check rejects hand-built generic
                // instantiations (both pinned by the checker test).
                TypeDefinition? cmePropsDef = module.GetType("PaintDotNet.Direct2D1.Effects.ColorManagementEffect/Props");
                TypeDefinition? fxAccDef = module.GetType("PaintDotNet.Direct2D1.Effects.EffectInputAccessor");
                TypeDefinition? devFxBaseDef = module.GetType("PaintDotNet.Direct2D1.Effects.DeviceEffect");
                MethodDefinition? baseGetPropsDef = devFxBaseDef?.Methods.FirstOrDefault(
                    m => m.Name == "get_Properties" && m.Parameters.Count == 0);
                MethodDefinition? getSourceDef = cmePropsDef?.Methods.FirstOrDefault(
                    m => m.Name == "get_Source" && m.Parameters.Count == 0);
                MethodDefinition? accGetDef = fxAccDef?.Methods.FirstOrDefault(
                    m => m.Name == "Get" && m.Parameters.Count == 0);
                MethodDefinition? accIsSetDef = fxAccDef?.Methods.FirstOrDefault(
                    m => m.Name == "get_IsSet" && m.Parameters.Count == 0);
                bool canForward = colorMgmtRef != null && cmePropsDef != null && fxAccDef != null
                    && baseGetPropsDef != null && getSourceDef != null && accGetDef != null && accIsSetDef != null;
                TypeReference? propsRef = null;
                MethodReference? getPropsRef = null;
                MethodReference? getSourceRef = null;
                MethodReference? accGetRef = null;
                MethodReference? accIsSetRef = null;
                VariableDefinition? cmeAccLoc = null;
                {
                    var cmeListLoc = new VariableDefinition(cpuCmdList);
                    var cmeScrLoc = new VariableDefinition(module.TypeSystem.Int64);
                    drawImage.Body.Variables.Add(cmeListLoc);
                    drawImage.Body.Variables.Add(cmeScrLoc);
                    if (canForward)
                    {
                        propsRef = module.ImportReference(cmePropsDef);
                        var accRef = module.ImportReference(fxAccDef);
                        getPropsRef = module.ImportReference(baseGetPropsDef);
                        getSourceRef = module.ImportReference(getSourceDef);
                        accGetRef = module.ImportReference(accGetDef);
                        accIsSetRef = module.ImportReference(accIsSetDef);
                        cmeAccLoc = new VariableDefinition(accRef);
                        drawImage.Body.Variables.Add(cmeAccLoc);
                    }
                    // haveCme is appended AFTER the proxy check: the
                    // failed-CME fallthrough must reach the proxy
                    // check (or realThrow), never the forward. (Appending
                    // it right after Brtrue let non-CME images fall into
                    // the forward with a null effect: FWD fx=null, NRE
                    // on get_Properties, composite swallowed.)
                    Instruction? haveCme = null;
                    VariableDefinition? cmeLoc = null;
                    if (colorMgmtRef != null)
                    {
                        cmeLoc = new VariableDefinition(colorMgmtRef);
                        drawImage.Body.Variables.Add(cmeLoc);
                        haveCme = il.Create(OpCodes.Nop);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Isinst, colorMgmtRef);
                        il.Emit(OpCodes.Stloc, cmeLoc);
                        il.Emit(OpCodes.Ldloc, cmeLoc);
                        il.Emit(OpCodes.Brtrue, haveCme);
                        if (colorMgmtProxyRef == null)
                            il.Emit(OpCodes.Br, realThrow);
                    }
                    // rev193: the palette wraps its filled list in a
                    // PROXY (CreateRef answers IColorManagementEffectProxy,
                    // a concrete class with its own non-generic
                    // get_Properties), so the proxy draw needs the same
                    // Properties.Source forward as the concrete effect.
                    // The old plain Ret dropped the fills.
                    MethodDefinition? proxyGetPropsDef =
                        (colorMgmtProxyRef as TypeDefinition)?.Methods.FirstOrDefault(
                            m => m.Name == "get_Properties" && m.Parameters.Count == 0);
                    bool canForwardProxy = canForward && proxyGetPropsDef != null;
                    MethodReference? proxyGetPropsRef = canForwardProxy
                        ? module.ImportReference(proxyGetPropsDef) : null;
                    if (colorMgmtProxyRef != null)
                    {
                        var proxyLoc = new VariableDefinition(colorMgmtProxyRef);
                        drawImage.Body.Variables.Add(proxyLoc);
                        var haveProxy = il.Create(OpCodes.Nop);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Isinst, colorMgmtProxyRef);
                        il.Emit(OpCodes.Stloc, proxyLoc);
                        il.Emit(OpCodes.Ldloc, proxyLoc);
                        il.Emit(OpCodes.Brtrue, haveProxy);
                        il.Emit(OpCodes.Br, realThrow);
                        il.Append(haveProxy);
                        if (canForwardProxy)
                        {
                            il.Emit(OpCodes.Ldloc, proxyLoc);
                            il.Emit(OpCodes.Callvirt, proxyGetPropsRef);
                            il.Emit(OpCodes.Castclass, propsRef);
                            il.Emit(OpCodes.Callvirt, getSourceRef);
                            il.Emit(OpCodes.Stloc, cmeAccLoc);
                            il.Emit(OpCodes.Ldloca_S, cmeAccLoc);
                            il.Emit(OpCodes.Call, accIsSetRef);
                            il.Emit(OpCodes.Brfalse, doRet);
                            il.Emit(OpCodes.Ldloca_S, cmeAccLoc);
                            il.Emit(OpCodes.Call, accGetRef);
                            il.Emit(OpCodes.Isinst, cpuCmdList);
                            il.Emit(OpCodes.Stloc, cmeListLoc);
                            il.Emit(OpCodes.Ldloc, cmeListLoc);
                            il.Emit(OpCodes.Brfalse, doRet);
                            il.Emit(OpCodes.Ldloc, cmeListLoc);
                            il.Emit(OpCodes.Ldfld, earlyScratchF);
                            il.Emit(OpCodes.Stloc, cmeScrLoc);
                            il.Emit(OpCodes.Ldloc, cmeScrLoc);
                            il.Emit(OpCodes.Brfalse, doRet);
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, targetField);
                            il.Emit(OpCodes.Ldloc, cmeScrLoc);
                            il.Emit(OpCodes.Call, blitScratchCpu);
                        }
                        il.Emit(OpCodes.Br, doRet);
                    }
                    if (haveCme != null)
                    {
                        il.Append(haveCme);
                        if (canForward)
                        {
                            il.Emit(OpCodes.Ldloc, cmeLoc);
                            il.Emit(OpCodes.Callvirt, getPropsRef);
                            il.Emit(OpCodes.Castclass, propsRef);
                            il.Emit(OpCodes.Callvirt, getSourceRef);
                            il.Emit(OpCodes.Stloc, cmeAccLoc);
                            il.Emit(OpCodes.Ldloca_S, cmeAccLoc);
                            il.Emit(OpCodes.Call, accIsSetRef);
                            il.Emit(OpCodes.Brfalse, doRet);
                            il.Emit(OpCodes.Ldloca_S, cmeAccLoc);
                            il.Emit(OpCodes.Call, accGetRef);
                            il.Emit(OpCodes.Isinst, cpuCmdList);
                            il.Emit(OpCodes.Stloc, cmeListLoc);
                            il.Emit(OpCodes.Ldloc, cmeListLoc);
                            il.Emit(OpCodes.Brfalse, doRet);
                            il.Emit(OpCodes.Ldloc, cmeListLoc);
                            il.Emit(OpCodes.Ldfld, earlyScratchF);
                            il.Emit(OpCodes.Stloc, cmeScrLoc);
                            il.Emit(OpCodes.Ldloc, cmeScrLoc);
                            il.Emit(OpCodes.Brfalse, doRet);
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, targetField);
                            il.Emit(OpCodes.Ldloc, cmeScrLoc);
                            il.Emit(OpCodes.Call, blitScratchCpu);
                        }
                        il.Emit(OpCodes.Br, doRet);
                    }
                }
                il.Append(doRet);
                il.Append(realThrow);
            }
            il.Emit(OpCodes.Ldstr, "CpuDrawingContext.DrawImage has no headless backend for ");
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(object).GetMethod("GetType", Type.EmptyTypes)!));
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(Type).GetMethod("get_FullName", Type.EmptyTypes)!));
            il.Emit(OpCodes.Call, module.ImportReference(typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!));
            il.Emit(OpCodes.Newobj, module.ImportReference(typeof(NotSupportedException).GetConstructor([typeof(string)])!));
            il.Emit(OpCodes.Throw);
            il.Append(hasBmp);
            il.Emit(OpCodes.Ldarga, drawImage.Parameters[1]);
            il.Emit(ptHas.Op, ptHas.Ref);
            var noDst = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, noDst);
            il.Emit(OpCodes.Ldarga, drawImage.Parameters[1]);
            il.Emit(ptValue.Op, ptValue.Ref);
            il.Emit(OpCodes.Stloc, pointLoc);
            il.Emit(OpCodes.Ldloca_S, pointLoc);
            il.Emit(OpCodes.Call, ptX);
            il.Emit(OpCodes.Stloc, dxLoc);
            il.Emit(OpCodes.Ldloca_S, pointLoc);
            il.Emit(OpCodes.Call, ptY);
            il.Emit(OpCodes.Stloc, dyLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, hasDestLoc);
            il.Append(noDst);
            il.Emit(OpCodes.Ldarg, drawImage.Parameters[2]);
            il.Emit(rectHas.Op, rectHas.Ref);
            var noSrc = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, noSrc);
            il.Emit(OpCodes.Ldarg, drawImage.Parameters[2]);
            il.Emit(rectValue.Op, rectValue.Ref);
            il.Emit(OpCodes.Stloc, rectLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectX);
            il.Emit(OpCodes.Stloc, sxLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectY);
            il.Emit(OpCodes.Stloc, syLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectW);
            il.Emit(OpCodes.Stloc, swLoc);
            il.Emit(OpCodes.Ldloca_S, rectLoc);
            il.Emit(OpCodes.Call, rectH);
            il.Emit(OpCodes.Stloc, shLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, hasSrcLoc);
            var blit = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br, blit);
            il.Append(noSrc);
            il.Emit(OpCodes.Ldloc, imgLoc);
            var useTargetSize = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse_S, useTargetSize);
            il.Emit(OpCodes.Ldloc, imgLoc);
            il.Emit(OpCodes.Ldfld, bmpWidthField);
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stloc, swLoc);
            il.Emit(OpCodes.Ldloc, imgLoc);
            il.Emit(OpCodes.Ldfld, bmpHeightField);
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stloc, shLoc);
            var sizeDone = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Br_S, sizeDone);
            il.Append(useTargetSize);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Ldloca_S, procTwLoc);
            il.Emit(OpCodes.Ldloca_S, procThLoc);
            il.Emit(OpCodes.Call, getTargetSizeCpu);
            il.Emit(OpCodes.Ldloc, procTwLoc);
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stloc, swLoc);
            il.Emit(OpCodes.Ldloc, procThLoc);
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stloc, shLoc);
            il.Append(sizeDone);
            il.Append(blit);
            var doBlit = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldloc, imgLoc);
            // Long form: the doBlit target sits past the composite /
            // procedural / proxy section, out of short-branch range
            // (an explicit _S here silently truncated to a gap target).
            il.Emit(OpCodes.Brtrue, doBlit);
            var tryProc = il.Create(OpCodes.Nop);
            // Composite item images (checker + thumbnail): draw both
            // before the procedural fallback swallows the thumbnail.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, dxLoc);
            il.Emit(OpCodes.Ldloc, dyLoc);
            il.Emit(OpCodes.Ldloc, dwLoc);
            il.Emit(OpCodes.Ldloc, dhLoc);
            il.Emit(OpCodes.Ldloc, hasDestLoc);
            il.Emit(OpCodes.Ldloc, sxLoc);
            il.Emit(OpCodes.Ldloc, syLoc);
            il.Emit(OpCodes.Ldloc, swLoc);
            il.Emit(OpCodes.Ldloc, shLoc);
            il.Emit(OpCodes.Ldloc, hasSrcLoc);
            il.Emit(OpCodes.Call, drawCompositeCpu);
            il.Emit(OpCodes.Brfalse, tryProc);
            il.Emit(OpCodes.Ret);
            var listLoc2 = new VariableDefinition(cpuCmdList);
            var scratchHLoc = new VariableDefinition(module.TypeSystem.Int64);
            drawImage.Body.Variables.Add(listLoc2);
            drawImage.Body.Variables.Add(scratchHLoc);
            drawImage.Body.InitLocals = true;
            FieldDefinition scratchF2 = cpuCmdList.Fields.First(f => f.Name == "_scratch");
            il.Emit(OpCodes.Ldloc, srcLoc);
            il.Emit(OpCodes.Isinst, cpuCmdList);
            il.Emit(OpCodes.Stloc, listLoc2);
            il.Emit(OpCodes.Ldloc, listLoc2);
            il.Emit(OpCodes.Brfalse, tryProc);
            // Redirected lists carry their fills in the scratch surface:
            // blit it back in desktop order. Lists without a scratch
            // painted direct (or stayed empty): today's skip.
            il.Emit(OpCodes.Ldloc, listLoc2);
            il.Emit(OpCodes.Ldfld, scratchF2);
            il.Emit(OpCodes.Stloc, scratchHLoc);
            il.Emit(OpCodes.Ldloc, scratchHLoc);
            il.Emit(OpCodes.Brfalse, imgDone);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Ldloc, scratchHLoc);
            il.Emit(OpCodes.Call, blitScratchCpu);
            il.Emit(OpCodes.Ldloc, scratchHLoc);
            il.Emit(OpCodes.Call, freeScratchCpu);
            il.Emit(OpCodes.Ldloc, listLoc2);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stfld, scratchF2);
            il.Emit(OpCodes.Br, imgDone);
            il.Append(tryProc);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Ldloc, srcLoc);
            il.Emit(OpCodes.Ldloc, dxLoc);
            il.Emit(OpCodes.Ldloc, dyLoc);
            il.Emit(OpCodes.Ldloc, swLoc);
            il.Emit(OpCodes.Ldloc, shLoc);
            il.Emit(OpCodes.Call, drawProceduralCpu);
            il.Emit(OpCodes.Brfalse, noProxy);
            il.Emit(OpCodes.Ret);
            il.Append(doBlit);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Ldloc, imgLoc);
            il.Emit(OpCodes.Ldfld, bmpHandleField);
            il.Emit(OpCodes.Ldloc, dxLoc);
            il.Emit(OpCodes.Ldloc, dyLoc);
            il.Emit(OpCodes.Ldloc, swLoc);
            il.Emit(OpCodes.Ldloc, shLoc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldloc, sxLoc);
            il.Emit(OpCodes.Ldloc, syLoc);
            il.Emit(OpCodes.Ldloc, swLoc);
            il.Emit(OpCodes.Ldloc, shLoc);
            il.Emit(OpCodes.Ldloc, hasSrcLoc);
            il.Emit(OpCodes.Ldc_R4, 1f);
            il.Emit(OpCodes.Ldarg, drawImage.Parameters[3]);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Call, drawBitmapCpu);
            il.Emit(OpCodes.Ret);
            il.Append(imgDone);
            il.Emit(OpCodes.Ret);
        // -- Cross-module call helper: resolve through the input closure
        // (version-agnostic) and import with the real scope. Only public
        // members: emitted code runs as Windows.Core, so PDN internals
        // (struct fields, GetInnermostRef) stay out of reach by construction.
        TypeReference XRef(string asm, string full)
        {
            int xdot = full.LastIndexOf('.');
            var xscope = module.AssemblyReferences.FirstOrDefault(a => a.Name == asm)
                ?? throw new InvalidOperationException("CpuPaint: assembly reference missing: " + asm);
            return new TypeReference(full.Substring(0, xdot), full.Substring(xdot + 1), module, xscope);
        }
        MethodReference XCall(string asm, string typeFull, string name, int argc, bool statik = false)
        {
            TypeDefinition xdef = ResolveCpuDef(XRef(asm, typeFull));
            MethodDefinition? xhit = xdef.Methods.FirstOrDefault(x =>
                    x.Name == name && x.Parameters.Count == argc && x.IsStatic == statik && !x.HasGenericParameters)
                ?? throw new InvalidOperationException("CpuPaint: " + typeFull + "." + name + "/" + argc + " missing");
            return module.ImportReference(xhit);
        }
        MethodReference XCtor(string asm, string typeFull, int argc)
        {
            TypeDefinition xdef = ResolveCpuDef(XRef(asm, typeFull));
            MethodDefinition? xhit = xdef.Methods.FirstOrDefault(x => x.Name == ".ctor" && x.Parameters.Count == argc)
                ?? throw new InvalidOperationException("CpuPaint: " + typeFull + "..ctor/" + argc + " missing");
            return module.ImportReference(xhit);
        }
        TypeReference sizeIntRef = module.ImportReference(ResolveCpuDef(XRef("PaintDotNet.Primitives", "PaintDotNet.Rendering.SizeInt32")));
        TypeReference rectIntRef = module.ImportReference(ResolveCpuDef(XRef("PaintDotNet.Primitives", "PaintDotNet.Rendering.RectInt32")));
        TypeReference pixelFormatRef = module.ImportReference(ResolveCpuDef(XRef("PaintDotNet.Primitives", "PaintDotNet.Imaging.PixelFormat")));
        TypeReference ibitmapRef = module.ImportReference(ResolveCpuDef(XRef("PaintDotNet.Windows", "PaintDotNet.Imaging.IBitmap")));
        TypeReference ibitmapLockRef = module.ImportReference(ResolveCpuDef(XRef("PaintDotNet.Windows", "PaintDotNet.Imaging.IBitmapLock")));
        MethodReference getSizeCall = XCall("PaintDotNet.Windows", "PaintDotNet.Imaging.IBitmapSource", "get_Size", 0);
        MethodReference getPixelFormatCall = XCall("PaintDotNet.Windows", "PaintDotNet.Imaging.IBitmapSource", "get_PixelFormat", 0);
        MethodReference copyPixelsCall = XCall("PaintDotNet.Windows", "PaintDotNet.Imaging.IBitmapSource", "CopyPixels", 4);
        MethodReference lockCall = XCall("PaintDotNet.Windows", "PaintDotNet.Imaging.IBitmap", "Lock", 2);
        MethodReference getBufferCall = XCall("PaintDotNet.Windows", "PaintDotNet.Imaging.IBitmapLock", "get_Buffer", 0);
        MethodReference getBufferStrideCall = XCall("PaintDotNet.Windows", "PaintDotNet.Imaging.IBitmapLock", "get_BufferStride", 0);
        MethodReference getWidthCall = XCall("PaintDotNet.Primitives", "PaintDotNet.Rendering.SizeInt32", "get_Width", 0);
        MethodReference getHeightCall = XCall("PaintDotNet.Primitives", "PaintDotNet.Rendering.SizeInt32", "get_Height", 0);
        MethodReference getBppCall = XCall("PaintDotNet.Primitives", "PaintDotNet.Imaging.PixelFormat", "GetBitsPerPixel", 0);
        MethodReference rectCtor = XCtor("PaintDotNet.Primitives", "PaintDotNet.Rendering.RectInt32", 4);
        MethodReference cpuHtCtor = module.ImportReference(typeof(System.Collections.Hashtable).GetConstructor(Type.EmptyTypes)!);
        MethodReference cpuHtAdd = module.ImportReference(typeof(System.Collections.Hashtable).GetMethod("Add")!);
        MethodReference cpuHtContains = module.ImportReference(typeof(System.Collections.Hashtable).GetMethod("ContainsKey")!);
        MethodReference cpuHtGet = module.ImportReference(typeof(System.Collections.Hashtable).GetMethod("get_Item")!);
        MethodReference disposableDispose = module.ImportReference(typeof(IDisposable).GetMethod("Dispose")!);
        MethodReference getTargetWidthCpu = Cpu("GetTargetWidth", 1);
        MethodReference getTargetHeightCpu = Cpu("GetTargetHeight", 1);
        MethodReference downloadCpu = Cpu("DownloadBitmap", 3);
        var nullableOpen = module.ImportReference(typeof(Nullable<>));
        var optRectType = new GenericInstanceType(nullableOpen);
        optRectType.GenericArguments.Add(rectIntRef);
        // -- Write-back: EndDraw/Dispose copy painted pixels back into the
        // FromBitmap source (registered in _resCache by CpuFromBitmap).
        var writeback = new MethodDefinition(
            "WritebackIfRegistered",
            MethodAttributes.Private | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        {
            var wil = writeback.Body.GetILProcessor();
            var wbCache = new VariableDefinition(resCacheHtRef);
            var wbKey = new VariableDefinition(module.TypeSystem.Object);
            var wbBmp = new VariableDefinition(ibitmapRef);
            var wbSize = new VariableDefinition(sizeIntRef);
            var wbW = new VariableDefinition(module.TypeSystem.Int32);
            var wbH = new VariableDefinition(module.TypeSystem.Int32);
            var wbRect = new VariableDefinition(rectIntRef);
            var wbLock = new VariableDefinition(ibitmapLockRef);
            writeback.Body.Variables.Add(wbCache);
            writeback.Body.Variables.Add(wbKey);
            writeback.Body.Variables.Add(wbBmp);
            writeback.Body.Variables.Add(wbSize);
            writeback.Body.Variables.Add(wbW);
            writeback.Body.Variables.Add(wbH);
            writeback.Body.Variables.Add(wbRect);
            writeback.Body.Variables.Add(wbLock);
            writeback.Body.InitLocals = true;
            var wbDone = wil.Create(OpCodes.Nop);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, resCacheField);
            wil.Emit(OpCodes.Dup);
            wil.Emit(OpCodes.Stloc, wbCache);
            wil.Emit(OpCodes.Brfalse, wbDone);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, targetField);
            wil.Emit(OpCodes.Box, module.TypeSystem.Int64);
            wil.Emit(OpCodes.Stloc, wbKey);
            wil.Emit(OpCodes.Ldloc, wbCache);
            wil.Emit(OpCodes.Ldloc, wbKey);
            wil.Emit(OpCodes.Callvirt, cpuHtContains);
            wil.Emit(OpCodes.Brfalse, wbDone);
            wil.Emit(OpCodes.Ldloc, wbCache);
            wil.Emit(OpCodes.Ldloc, wbKey);
            wil.Emit(OpCodes.Callvirt, cpuHtGet);
            wil.Emit(OpCodes.Isinst, ibitmapRef);
            wil.Emit(OpCodes.Stloc, wbBmp);
            wil.Emit(OpCodes.Ldloc, wbBmp);
            wil.Emit(OpCodes.Brfalse, wbDone);
            wil.Emit(OpCodes.Ldloc, wbBmp);
            wil.Emit(OpCodes.Callvirt, getSizeCall);
            wil.Emit(OpCodes.Stloc, wbSize);
            wil.Emit(OpCodes.Ldloca_S, wbSize);
            wil.Emit(OpCodes.Call, getWidthCall);
            wil.Emit(OpCodes.Stloc, wbW);
            wil.Emit(OpCodes.Ldloca_S, wbSize);
            wil.Emit(OpCodes.Call, getHeightCall);
            wil.Emit(OpCodes.Stloc, wbH);
            wil.Emit(OpCodes.Ldc_I4_0);
            wil.Emit(OpCodes.Ldc_I4_0);
            wil.Emit(OpCodes.Ldloc, wbW);
            wil.Emit(OpCodes.Ldloc, wbH);
            wil.Emit(OpCodes.Newobj, rectCtor);
            wil.Emit(OpCodes.Stloc, wbRect);
            wil.Emit(OpCodes.Ldloc, wbBmp);
            wil.Emit(OpCodes.Ldloc, wbRect);
            wil.Emit(OpCodes.Ldc_I4_2);
            wil.Emit(OpCodes.Callvirt, lockCall);
            wil.Emit(OpCodes.Stloc, wbLock);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, targetField);
            wil.Emit(OpCodes.Ldloc, wbLock);
            wil.Emit(OpCodes.Callvirt, getBufferCall);
            wil.Emit(OpCodes.Ldloc, wbLock);
            wil.Emit(OpCodes.Callvirt, getBufferStrideCall);
            wil.Emit(OpCodes.Call, downloadCpu);
            wil.Emit(OpCodes.Ldloc, wbLock);
            wil.Emit(OpCodes.Callvirt, disposableDispose);
            wil.Append(wbDone);
            wil.Emit(OpCodes.Ret);
            writeback.Body.MaxStackSize = 32;
            ctx.Methods.Add(writeback);
        }
        // -- Upload: DeviceContext.FromBitmap funnels here. 32-bit sources
        // become CPU targets (copy-in + write-back registration); anything
        // else keeps the null today-shape instead of the PerThread NRE.
        var cpuFromBitmap = new MethodDefinition(
            "CpuFromBitmap",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            ideviceRef);
        cpuFromBitmap.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        cpuFromBitmap.Parameters.Add(new ParameterDefinition(ibitmapRef));
        {
            var fil = cpuFromBitmap.Body.GetILProcessor();
            var fbSize = new VariableDefinition(sizeIntRef);
            var fbPf = new VariableDefinition(pixelFormatRef);
            var fbW = new VariableDefinition(module.TypeSystem.Int32);
            var fbH = new VariableDefinition(module.TypeSystem.Int32);
            var fbBpp = new VariableDefinition(module.TypeSystem.Int32);
            var fbTarget = new VariableDefinition(module.TypeSystem.Int64);
            var fbSrc = new VariableDefinition(module.TypeSystem.IntPtr);
            var fbRect = new VariableDefinition(rectIntRef);
            var fbOptRect = new VariableDefinition(optRectType);
            var fbLock = new VariableDefinition(ibitmapLockRef);
            var fbCtx = new VariableDefinition(ctx);
            cpuFromBitmap.Body.Variables.Add(fbSize);
            cpuFromBitmap.Body.Variables.Add(fbPf);
            cpuFromBitmap.Body.Variables.Add(fbW);
            cpuFromBitmap.Body.Variables.Add(fbH);
            cpuFromBitmap.Body.Variables.Add(fbBpp);
            cpuFromBitmap.Body.Variables.Add(fbTarget);
            cpuFromBitmap.Body.Variables.Add(fbSrc);
            cpuFromBitmap.Body.Variables.Add(fbRect);
            cpuFromBitmap.Body.Variables.Add(fbOptRect);
            cpuFromBitmap.Body.Variables.Add(fbLock);
            cpuFromBitmap.Body.Variables.Add(fbCtx);
            cpuFromBitmap.Body.InitLocals = true;
            var fbNull = fil.Create(OpCodes.Nop);
            var fbHasCache = fil.Create(OpCodes.Nop);
            fil.Emit(OpCodes.Ldarg_1);
            fil.Emit(OpCodes.Callvirt, getSizeCall);
            fil.Emit(OpCodes.Stloc, fbSize);
            fil.Emit(OpCodes.Ldloca_S, fbSize);
            fil.Emit(OpCodes.Call, getWidthCall);
            fil.Emit(OpCodes.Stloc, fbW);
            fil.Emit(OpCodes.Ldloca_S, fbSize);
            fil.Emit(OpCodes.Call, getHeightCall);
            fil.Emit(OpCodes.Stloc, fbH);
            fil.Emit(OpCodes.Ldloc, fbW);
            fil.Emit(OpCodes.Ldc_I4_0);
            fil.Emit(OpCodes.Ble, fbNull);
            fil.Emit(OpCodes.Ldloc, fbH);
            fil.Emit(OpCodes.Ldc_I4_0);
            fil.Emit(OpCodes.Ble, fbNull);
            fil.Emit(OpCodes.Ldarg_1);
            fil.Emit(OpCodes.Callvirt, getPixelFormatCall);
            fil.Emit(OpCodes.Stloc, fbPf);
            fil.Emit(OpCodes.Ldloca_S, fbPf);
            fil.Emit(OpCodes.Call, getBppCall);
            fil.Emit(OpCodes.Stloc, fbBpp);
            fil.Emit(OpCodes.Ldloc, fbBpp);
            fil.Emit(OpCodes.Ldc_I4, 32);
            fil.Emit(OpCodes.Bne_Un, fbNull);
            fil.Emit(OpCodes.Ldloc, fbW);
            fil.Emit(OpCodes.Ldloc, fbH);
            fil.Emit(OpCodes.Call, createBitmapEmpty);
            fil.Emit(OpCodes.Stloc, fbTarget);
            fil.Emit(OpCodes.Ldloc, fbTarget);
            fil.Emit(OpCodes.Ldc_I8, 0L);
            fil.Emit(OpCodes.Beq, fbNull);
            fil.Emit(OpCodes.Ldloc, fbTarget);
            fil.Emit(OpCodes.Call, lockBitmap);
            fil.Emit(OpCodes.Stloc, fbSrc);
            fil.Emit(OpCodes.Ldc_I4_0);
            fil.Emit(OpCodes.Ldc_I4_0);
            fil.Emit(OpCodes.Ldloc, fbW);
            fil.Emit(OpCodes.Ldloc, fbH);
            fil.Emit(OpCodes.Newobj, rectCtor);
            fil.Emit(OpCodes.Stloc, fbRect);
            fil.Emit(OpCodes.Ldarg_1);
            fil.Emit(OpCodes.Ldloc, fbRect);
            fil.Emit(OpCodes.Ldc_I4_3);
            fil.Emit(OpCodes.Callvirt, lockCall);
            fil.Emit(OpCodes.Stloc, fbLock);
            fil.Emit(OpCodes.Ldarg_1);
            fil.Emit(OpCodes.Ldloc, fbSrc);
            fil.Emit(OpCodes.Ldloc, fbW);
            fil.Emit(OpCodes.Ldc_I4_4);
            fil.Emit(OpCodes.Mul);
            fil.Emit(OpCodes.Ldloc, fbW);
            fil.Emit(OpCodes.Ldloc, fbH);
            fil.Emit(OpCodes.Mul);
            fil.Emit(OpCodes.Ldc_I4_4);
            fil.Emit(OpCodes.Mul);
            fil.Emit(OpCodes.Ldloca_S, fbOptRect);
            fil.Emit(OpCodes.Callvirt, copyPixelsCall);
            fil.Emit(OpCodes.Ldloc, fbTarget);
            fil.Emit(OpCodes.Call, unlockBitmap);
            fil.Emit(OpCodes.Ldloc, fbLock);
            fil.Emit(OpCodes.Callvirt, disposableDispose);
            fil.Emit(OpCodes.Ldloc, fbTarget);
            fil.Emit(OpCodes.Newobj, ctxCtor);
            fil.Emit(OpCodes.Stloc, fbCtx);
            fil.Emit(OpCodes.Ldloc, fbCtx);
            fil.Emit(OpCodes.Ldfld, resCacheField);
            fil.Emit(OpCodes.Brtrue, fbHasCache);
            fil.Emit(OpCodes.Ldloc, fbCtx);
            fil.Emit(OpCodes.Newobj, cpuHtCtor);
            fil.Emit(OpCodes.Stfld, resCacheField);
            fil.Append(fbHasCache);
            fil.Emit(OpCodes.Ldloc, fbCtx);
            fil.Emit(OpCodes.Ldfld, resCacheField);
            fil.Emit(OpCodes.Ldloc, fbTarget);
            fil.Emit(OpCodes.Box, module.TypeSystem.Int64);
            fil.Emit(OpCodes.Ldarg_1);
            fil.Emit(OpCodes.Callvirt, cpuHtAdd);
            fil.Emit(OpCodes.Ldloc, fbCtx);
            fil.Emit(OpCodes.Ret);
            fil.Append(fbNull);
            fil.Emit(OpCodes.Ldnull);
            fil.Emit(OpCodes.Ret);
            cpuFromBitmap.Body.MaxStackSize = 32;
            ctx.Methods.Add(cpuFromBitmap);
        }
        // -- Holder core: answer the installed cache (null until the
        // DrawingContext .ctor installs one via TrySet below).
        MethodDefinition cTryGet = Contract("TryGetResourceCache", m => m.HasGenericParameters && m.Parameters.Count == 0);
        {
            var tryGet = new MethodDefinition(cTryGet.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                module.TypeSystem.Object);
            var gp = new GenericParameter("T", tryGet);
            gp.Attributes = cTryGet.GenericParameters[0].Attributes;
            // Rebuild each constraint against the target module: sharing the
            // contract's instances carries the SOURCE module's scope object,
            // which Cecil 0.11.6 emits against the wrong AssemblyRef at write
            // time (observed: IObjectRef re-scoped to System.Numerics.Vectors,
            // fatal TypeLoad at bind). Importing re-roots the reference.
            foreach (var gc in cTryGet.GenericParameters[0].Constraints)
                gp.Constraints.Add(new Mono.Cecil.GenericParameterConstraint(
                    module.ImportReference(gc.ConstraintType)));
            tryGet.GenericParameters.Add(gp);
            tryGet.ReturnType = gp;
            var til = tryGet.Body.GetILProcessor();
            til.Emit(OpCodes.Ldarg_0);
            til.Emit(OpCodes.Ldfld, holderCacheField);
            til.Emit(OpCodes.Isinst, gp);
            til.Emit(OpCodes.Ret);
            tryGet.Body.MaxStackSize = 32;
            ctx.Methods.Add(tryGet);
            implemented.Add(cTryGet);
        }
        // -- Holder core: install (last writer wins; headless is single-threaded).
        var trySet = Impl(Contract("TrySetResourceCache", m => m.Parameters.Count == 1));
        {
            var ilTrySet = trySet.Body.GetILProcessor();
            ilTrySet.Emit(OpCodes.Ldarg_0);
            ilTrySet.Emit(OpCodes.Ldarg_1);
            ilTrySet.Emit(OpCodes.Stfld, holderCacheField);
            ilTrySet.Emit(OpCodes.Ldc_I4_1);
            ilTrySet.Emit(OpCodes.Ret);
        }
        // -- GetDeviceContext: desktop-identical AddRef through the public
        // CreateRef<T> extension (the proxy machinery is PDN's own code).
        var getDC = Impl(Contract("GetDeviceContext", m => m.Parameters.Count == 0));
        {
            TypeDefinition extDef = ResolveCpuDef(XRef("PaintDotNet.ComponentModel", "PaintDotNet.ComponentModel.ObjectRefExtensions"));
            MethodDefinition openRef = extDef.Methods.First(m => m.Name == "CreateRef" && m.HasGenericParameters && m.Parameters.Count == 1);
            var gim = new GenericInstanceMethod(module.ImportReference(openRef));
            gim.GenericArguments.Add(ideviceRef);
            var ilGetDC = getDC.Body.GetILProcessor();
            ilGetDC.Emit(OpCodes.Ldarg_0);
            ilGetDC.Emit(OpCodes.Call, gim);
            ilGetDC.Emit(OpCodes.Ret);
        }
        // -- CreateCompatibleDeviceContext: parent-sized CPU target (desired
        // sizes need Nullable accessors the loader cannot bind; a parent-size
        // target is a compatible buffer by construction).
        var compat = Impl(Contract("CreateCompatibleDeviceContext", m => m.Parameters.Count == 4));
        {
            var ilCompat = compat.Body.GetILProcessor();
            var cW = new VariableDefinition(module.TypeSystem.Int32);
            var cH = new VariableDefinition(module.TypeSystem.Int32);
            var cT = new VariableDefinition(module.TypeSystem.Int64);
            compat.Body.Variables.Add(cW);
            compat.Body.Variables.Add(cH);
            compat.Body.Variables.Add(cT);
            compat.Body.InitLocals = true;
            var cOkW = ilCompat.Create(OpCodes.Nop);
            var cOkH = ilCompat.Create(OpCodes.Nop);
            ilCompat.Emit(OpCodes.Ldarg_0);
            ilCompat.Emit(OpCodes.Ldfld, targetField);
            ilCompat.Emit(OpCodes.Call, getTargetWidthCpu);
            ilCompat.Emit(OpCodes.Stloc, cW);
            ilCompat.Emit(OpCodes.Ldarg_0);
            ilCompat.Emit(OpCodes.Ldfld, targetField);
            ilCompat.Emit(OpCodes.Call, getTargetHeightCpu);
            ilCompat.Emit(OpCodes.Stloc, cH);
            ilCompat.Emit(OpCodes.Ldloc, cW);
            ilCompat.Emit(OpCodes.Ldc_I4_1);
            ilCompat.Emit(OpCodes.Bge, cOkW);
            ilCompat.Emit(OpCodes.Ldc_I4_1);
            ilCompat.Emit(OpCodes.Stloc, cW);
            ilCompat.Append(cOkW);
            ilCompat.Emit(OpCodes.Ldloc, cH);
            ilCompat.Emit(OpCodes.Ldc_I4_1);
            ilCompat.Emit(OpCodes.Bge, cOkH);
            ilCompat.Emit(OpCodes.Ldc_I4_1);
            ilCompat.Emit(OpCodes.Stloc, cH);
            ilCompat.Append(cOkH);
            ilCompat.Emit(OpCodes.Ldloc, cW);
            ilCompat.Emit(OpCodes.Ldloc, cH);
            ilCompat.Emit(OpCodes.Call, createBitmapEmpty);
            ilCompat.Emit(OpCodes.Stloc, cT);
            ilCompat.Emit(OpCodes.Ldloc, cT);
            ilCompat.Emit(OpCodes.Newobj, ctxCtor);
            ilCompat.Emit(OpCodes.Ret);
        }
        // -- ICompatibleDeviceContext.Bitmap: device-bitmap view over the target.
        var getBmp = Impl(Contract("get_Bitmap", m => m.Parameters.Count == 0));
        {
            var ilGetBmp = getBmp.Body.GetILProcessor();
            var bT = new VariableDefinition(module.TypeSystem.Int64);
            getBmp.Body.Variables.Add(bT);
            ilGetBmp.Emit(OpCodes.Ldarg_0);
            ilGetBmp.Emit(OpCodes.Ldfld, targetField);
            ilGetBmp.Emit(OpCodes.Stloc, bT);
            ilGetBmp.Emit(OpCodes.Ldloc, bT);
            ilGetBmp.Emit(OpCodes.Ldloc, bT);
            ilGetBmp.Emit(OpCodes.Call, getTargetWidthCpu);
            ilGetBmp.Emit(OpCodes.Ldloc, bT);
            ilGetBmp.Emit(OpCodes.Call, getTargetHeightCpu);
            ilGetBmp.Emit(OpCodes.Newobj, module.ImportReference(bmpCtor));
            ilGetBmp.Emit(OpCodes.Ret);
        }
        // -- EndDraw: flush painted pixels back to the FromBitmap source.
        // -- MaximumBitmapSize: the CPU has no texture limit; answer PDN's
        // own tile clamp (BitmapImage tiles by min(MaximumBitmapSize,8192)).
        // The tail-loop default (0) makes a zero tile size: divide-by-zero
        // in the tile math and empty clippers downstream (wheel upload).
        var maxBmpSize = Impl(Contract("get_MaximumBitmapSize", m => m.Parameters.Count == 0));
        {
            var ilMaxBmp = maxBmpSize.Body.GetILProcessor();
            ilMaxBmp.Emit(OpCodes.Ldc_I4, 8192);
            ilMaxBmp.Emit(OpCodes.Ret);
        }
        var endDraw = Impl(Contract("EndDraw", m => m.Parameters.Count == 0));
        {
            var ilEndDraw = endDraw.Body.GetILProcessor();
            ilEndDraw.Emit(OpCodes.Ldarg_0);
            ilEndDraw.Emit(OpCodes.Call, writeback);
            ilEndDraw.Emit(OpCodes.Ret);
        }
        // -- Dispose: release the primary ref through the base (the last ref
        // frees the target in Dispose(bool)); never free inline, outstanding
        // CreateRef proxies still draw until their own release.
        var disp = Impl(Contract("Dispose", m => m.Parameters.Count == 0));
        {
            MethodDefinition baseDispose = ResolveCpuDef(refTrackedRef).Methods
                .First(m => m.Name == "Dispose" && m.Parameters.Count == 0 && !m.IsStatic);
            var ilDisp = disp.Body.GetILProcessor();
            ilDisp.Emit(OpCodes.Ldarg_0);
            ilDisp.Emit(OpCodes.Call, module.ImportReference(baseDispose));
            ilDisp.Emit(OpCodes.Ret);
        }
        // -- Dispose(bool): last-ref teardown (write-back, then free).
        {
            MethodDefinition baseDisposing = FindBaseVirtual(
                ResolveCpuDef(refTrackedRef), "Dispose", m => m.Parameters.Count == 1);
            var dispB = new MethodDefinition(
                "Dispose",
                MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            dispB.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            dispB.Overrides.Add(module.ImportReference(baseDisposing));
            var ilDispB = dispB.Body.GetILProcessor();
            ilDispB.Emit(OpCodes.Ldarg_0);
            ilDispB.Emit(OpCodes.Call, writeback);
            ilDispB.Emit(OpCodes.Ldarg_0);
            ilDispB.Emit(OpCodes.Ldfld, targetField);
            ilDispB.Emit(OpCodes.Call, freeContext);
            ilDispB.Emit(OpCodes.Ret);
            dispB.Body.MaxStackSize = 32;
            ctx.Methods.Add(dispB);
        }
        }        // -- Remaining IDrawingContext members: device-neutral defaults.
        // Dispose releases the CPU target; reference-typed device objects
        // throw loudly (a null would explode far from the cause); value
        // types answer default; void members are headless no-ops.
        MethodReference notSupportedCtor = module.ImportReference(
            typeof(NotSupportedException).GetConstructor(new[] { typeof(string) })!);
        foreach (MethodDefinition contract in contracts)
        {
            if (!implemented.Add(contract))
                continue;
            var impl = new MethodDefinition(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                MapCpuType(module, contract.ReturnType));
            foreach (var px in contract.Parameters)
                impl.Parameters.Add(new ParameterDefinition(
                    px.Name, px.Attributes, MapCpuType(module, px.ParameterType)));
            var il = impl.Body.GetILProcessor();
            if (contract.Name == "Dispose" && contract.Parameters.Count == 0)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, targetField);
                il.Emit(OpCodes.Call, freeContext);
                il.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "CreateEffect" && contract.Parameters.Count == 1)
            {
                // Headless effect backend: auto-registration (TryCreateEffect)
                // answers a CPU effect, so custom effects construct real
                // Properties bags headless.
                il.Emit(OpCodes.Newobj, cpuEffectCtor);
                il.Emit(OpCodes.Ret);
            }
            else if (contract.Name == "GetTextRenderingParameters" && contract.Parameters.Count == 0)
            {
                // Every caller null-checks (mode/algorithm scopes take
                // the inert path, the algorithm query defaults): null
                // skips the D2D parameter cache that NREs headless.
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }
            else if (contract.ReturnType.FullName == "System.Void")
            {
                il.Emit(OpCodes.Ret);
            }
            else if (MapCpuType(module, contract.ReturnType).Resolve()?.IsValueType == true)
            {
                var loc = new VariableDefinition(MapCpuType(module, contract.ReturnType));
                impl.Body.Variables.Add(loc);
                impl.Body.InitLocals = true;
                il.Emit(OpCodes.Ldloca_S, loc);
                il.Emit(OpCodes.Initobj, MapCpuType(module, contract.ReturnType));
                il.Emit(OpCodes.Ldloc, loc);
                il.Emit(OpCodes.Ret);
            }
            else
            {
                il.Emit(OpCodes.Ldstr, "CpuDrawingContext." + contract.Name + " has no headless backend");
                il.Emit(OpCodes.Newobj, notSupportedCtor);
                il.Emit(OpCodes.Throw);
            }
            // Synthesized bodies push deep stacks (DrawBitmap: 14 slots);
            // fresh MethodBodies default too small for them.
            impl.Body.MaxStackSize = 32;
            ctx.Methods.Add(impl);
        }

        module.Types.Add(ctx);

        // -- FromBitmap/BeginDraw CPU bodies: the factory is null headless,
        // so these always took the PerThread NRE path. Route 32-bit sources
        // to the CPU upload instead; exotic formats keep the null today-shape.
        {
            TypeDefinition devCtxCls = module.GetType("PaintDotNet.Direct2D1.DeviceContext")
                ?? throw new InvalidOperationException("CpuPaint: DeviceContext missing");
            MethodDefinition fromBmp = devCtxCls.Methods
                .FirstOrDefault(m => m.Name == "FromBitmap" && m.Parameters.Count == 2 && m.IsStatic && !m.HasGenericParameters)
                ?? throw new InvalidOperationException("CpuPaint: DeviceContext.FromBitmap/2 missing");
            if (fromBmp.Parameters[0].ParameterType.FullName != "PaintDotNet.Direct2D1.IDirect2DFactory"
                || fromBmp.Parameters[1].ParameterType.FullName != "PaintDotNet.Imaging.IBitmap")
                throw new InvalidOperationException("CpuPaint: DeviceContext.FromBitmap/2 shape drifted");
            MethodDefinition cpuFromBitmapRef = ctx.Methods.First(m => m.Name == "CpuFromBitmap");
            fromBmp.Body.ExceptionHandlers.Clear();
            fromBmp.Body.Variables.Clear();
            fromBmp.Body.Instructions.Clear();
            var fbil = fromBmp.Body.GetILProcessor();
            fbil.Emit(OpCodes.Ldarg_0);
            fbil.Emit(OpCodes.Ldarg_1);
            fbil.Emit(OpCodes.Call, cpuFromBitmapRef);
            fbil.Emit(OpCodes.Ret);
            fromBmp.Body.MaxStackSize = 32;
            foreach (MethodDefinition bd in drawCtx.Methods.Where(m => m.Name == "BeginDraw" && m.HasBody))
            {
                string bdp0 = bd.Parameters.Count > 0 ? bd.Parameters[0].ParameterType.FullName : "";
                string bdp1 = bd.Parameters.Count > 1 ? bd.Parameters[1].ParameterType.FullName : "";
                bd.Body.ExceptionHandlers.Clear();
                bd.Body.Variables.Clear();
                bd.Body.Instructions.Clear();
                var bil = bd.Body.GetILProcessor();
                if (bd.Parameters.Count == 1 && bdp0 == "PaintDotNet.Direct2D1.IDeviceContext")
                {
                    // A CPU context already IS a drawing context (wrapping it
                    // in DrawingContext would throw: it implements
                    // IDrawingContext). Anything else keeps the null shape.
                    var bdOk = bil.Create(OpCodes.Nop);
                    bil.Emit(OpCodes.Ldarg_0);
                    bil.Emit(OpCodes.Isinst, ctx);
                    bil.Emit(OpCodes.Dup);
                    bil.Emit(OpCodes.Brtrue, bdOk);
                    bil.Emit(OpCodes.Pop);
                    bil.Emit(OpCodes.Ldnull);
                    bil.Emit(OpCodes.Ret);
                    bil.Append(bdOk);
                    bil.Emit(OpCodes.Ret);
                }
                else if (bd.Parameters.Count == 2
                    && bdp0 == "PaintDotNet.Direct2D1.IDirect2DFactory"
                    && bdp1 == "PaintDotNet.Imaging.IBitmap")
                {
                    bil.Emit(OpCodes.Ldarg_0);
                    bil.Emit(OpCodes.Ldarg_1);
                    bil.Emit(OpCodes.Call, cpuFromBitmapRef);
                    bil.Emit(OpCodes.Castclass, idrawingRef);
                    bil.Emit(OpCodes.Ret);
                }
                else if (bd.Parameters.Count == 1 && bdp0 == "PaintDotNet.Imaging.IBitmap")
                {
                    bil.Emit(OpCodes.Ldnull);
                    bil.Emit(OpCodes.Ldarg_0);
                    bil.Emit(OpCodes.Call, cpuFromBitmapRef);
                    bil.Emit(OpCodes.Castclass, idrawingRef);
                    bil.Emit(OpCodes.Ret);
                }
                // else: generic surface/region overloads + BeginDrawNull keep
                // their neutered shape (status quo ante).
                bd.Body.MaxStackSize = 32;
            }
        }
        // -- Static Draw(RegionPtr<TPixel>, Action<IDrawingContext>) [+ the
        // factory overload]: bind the mask bytes to a CPU context and run
        // the action under try/finally (the helper's retry loop only
        // handles device loss, which cannot happen headless).
        // RegionPtr<!!0> accessors: the proven !!0-form tokens when
        // they survive, else built per Draw method on its own !!0.
        (OpCode Op, MethodReference Ref)? regionPtr =
            HarvestCpuCall(module, "PaintDotNet.RegionPtr`1", "get_Ptr", true);
        (OpCode Op, MethodReference Ref)? regionWidth =
            HarvestCpuCall(module, "PaintDotNet.RegionPtr`1", "get_Width", true);
        (OpCode Op, MethodReference Ref)? regionHeight =
            HarvestCpuCall(module, "PaintDotNet.RegionPtr`1", "get_Height", true);
        (OpCode Op, MethodReference Ref)? regionStride =
            HarvestCpuCall(module, "PaintDotNet.RegionPtr`1", "get_Stride", true);
        TypeReference? regionElem = null;
        (OpCode Op, MethodReference Ref) RegionAccessor(MethodDefinition draw, string name)
        {
            (OpCode Op, MethodReference Ref)? hit = name switch
            {
                "get_Ptr" => regionPtr,
                "get_Width" => regionWidth,
                "get_Height" => regionHeight,
                _ => regionStride,
            };
            if (hit is not null)
                return hit.Value;
            regionElem ??= OwnedCpuElement(module, "PaintDotNet", "RegionPtr`1");
            var inst = new GenericInstanceType(regionElem);
            inst.GenericArguments.Add(draw.GenericParameters[0]);
            // get_Ptr returns the open T* (!0 of RegionPtr<T> itself):
            // pre-substituting the caller's !!0 into the return mismatches
            // the declaration and the call never resolves (same !0 rule as
            // the Nullable probes). Width/Height/Stride return plain ints.
            TypeReference ret = name == "get_Ptr"
                ? new PointerType(ResolveCpuDef(regionElem).GenericParameters[0])
                : module.TypeSystem.Int32;
            var mr = new MethodReference(name, ret, inst) { HasThis = true };
            return (OpCodes.Call, mr);
        }
        foreach (MethodDefinition draw in drawCtx.Methods
            .Where(m => m.Name == "Draw" && m.HasGenericParameters && m.GenericParameters.Count == 1))
        {
            int maskIndex = -1;
            int actionIndex = -1;
            for (int i = 0; i < draw.Parameters.Count; i++)
            {
                TypeReference pt = draw.Parameters[i].ParameterType;
                if (pt is GenericInstanceType gi && gi.ElementType.FullName == "PaintDotNet.RegionPtr`1")
                    maskIndex = i;
                if (pt.FullName == "System.Action`1<PaintDotNet.Direct2D1.IDrawingContext>")
                    actionIndex = i;
            }
            if (maskIndex < 0 || actionIndex < 0)
                continue;
            RewriteCpuDraw(
                module, draw, maskIndex, actionIndex, ctxCtor,
                RegionAccessor(draw, "get_Ptr"), RegionAccessor(draw, "get_Width"),
                RegionAccessor(draw, "get_Height"), RegionAccessor(draw, "get_Stride"),
                createTarget, freeContext);
        }
        // Draw(IBitmap, Action) [+ factory]: sprite dab rendering binds
        // the bitmap lock; anything outside the bitmap/factory/action
        // shape stays pristine.
        foreach (MethodDefinition draw in drawCtx.Methods
            .Where(m => m.Name == "Draw" && !m.HasGenericParameters))
        {
            int bmpIndex = -1;
            int actionIndex = -1;
            bool shapeOk = true;
            for (int i = 0; i < draw.Parameters.Count; i++)
            {
                TypeReference pt = draw.Parameters[i].ParameterType;
                if (pt.FullName == "PaintDotNet.Imaging.IBitmap")
                    bmpIndex = i;
                else if (pt.FullName == "System.Action`1<PaintDotNet.Direct2D1.IDrawingContext>")
                    actionIndex = i;
                else if (pt.FullName == "PaintDotNet.Direct2D1.IDirect2DFactory")
                    continue;
                else
                {
                    shapeOk = false;
                    break;
                }
            }
            if (!shapeOk || bmpIndex < 0 || actionIndex < 0)
                continue;
            RewriteCpuBitmapDraw(
                module, draw, bmpIndex, actionIndex, ctxCtor, createTarget, freeContext);
        }
        // Draw(void*, SizeInt32, stride, PixelFormat&, Action) [+ factory]:
        // raw upload buffers bind directly.
        foreach (MethodDefinition draw in drawCtx.Methods
            .Where(m => m.Name == "Draw" && !m.HasGenericParameters))
        {
            int ptrIndex = -1;
            int sizeIndex = -1;
            int strideIndex = -1;
            int formatIndex = -1;
            int actionIndex = -1;
            bool shapeOk = true;
            for (int i = 0; i < draw.Parameters.Count; i++)
            {
                TypeReference pt = draw.Parameters[i].ParameterType;
                if (pt is PointerType ptr && ptr.ElementType.FullName == "System.Void")
                    ptrIndex = i;
                else if (pt.FullName == "PaintDotNet.Rendering.SizeInt32")
                    sizeIndex = i;
                else if (pt.FullName == "System.Int32")
                    strideIndex = i;
                else if (UnwrapByref(pt) is ByReferenceType br
                    && br.ElementType.FullName == "PaintDotNet.Imaging.PixelFormat")
                    formatIndex = i;
                else if (pt.FullName == "System.Action`1<PaintDotNet.Direct2D1.IDrawingContext>")
                    actionIndex = i;
                else if (pt.FullName == "PaintDotNet.Direct2D1.IDirect2DFactory")
                    continue;
                else
                {
                    shapeOk = false;
                    break;
                }
            }
            if (!shapeOk || ptrIndex < 0 || sizeIndex < 0 || strideIndex < 0
                || formatIndex < 0 || actionIndex < 0)
                continue;
            RewriteCpuRawDraw(
                module, draw, ptrIndex, sizeIndex, strideIndex, formatIndex,
                actionIndex, ctxCtor, createTarget, freeContext);
        }

        return true;
    }

    // FillRectangle(RectFloat, brush) and FillEllipse(ellipse, brush)
    // over a CPU brush. The ellipse shape is decomposed through its own
    // definition (Center/RadiusX/RadiusY, or X/Y/Width/Height with the
    // center and radii derived) rather than a transcribed contract.
    private static void EmitCpuFill(
        ModuleDefinition module,
        MethodDefinition fill,
        FieldDefinition targetField,
        TypeDefinition cpuBrush,
        FieldDefinition opacityField,
        FieldDefinition rField,
        FieldDefinition gField,
        FieldDefinition bField,
        FieldDefinition aField,
        MethodReference cpuFill,
        bool isEllipse)
    {
        var il = fill.Body.GetILProcessor();
        var brushLoc = new VariableDefinition(cpuBrush);
        fill.Body.Variables.Add(brushLoc);
        fill.Body.InitLocals = true;
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, cpuBrush);
        il.Emit(OpCodes.Stloc, brushLoc);
        il.Emit(OpCodes.Ldloc, brushLoc);
        var notCpu = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Brfalse, notCpu);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        if (!isEllipse)
        {
            MethodReference getX = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_X");
            MethodReference getY = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Y");
            MethodReference getW = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Width");
            MethodReference getH = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Height");
            foreach (var g in new[] { getX, getY, getW, getH })
            {
                il.Emit(OpCodes.Ldarga_S, fill.Parameters[0]);
                il.Emit(OpCodes.Call, g);
            }
        }
        else
        {
            TypeReference shapeRef = MapCpuType(module, fill.Parameters[0].ParameterType);
            TypeDefinition shapeDef = ResolveCpuDef(shapeRef);
            MethodDefinition? center = shapeDef.Methods
                .FirstOrDefault(m => m.Name == "get_Center" && m.Parameters.Count == 0);
            MethodDefinition? radiusX = shapeDef.Methods
                .FirstOrDefault(m => m.Name == "get_RadiusX" && m.Parameters.Count == 0);
            MethodDefinition? radiusY = shapeDef.Methods
                .FirstOrDefault(m => m.Name == "get_RadiusY" && m.Parameters.Count == 0);
            if (center is not null && radiusX is not null && radiusY is not null)
            {
                var centerGet = new MethodReference(
                    center.Name, MapCpuType(module, center.ReturnType), shapeRef) { HasThis = true };
                var rxGet = new MethodReference(
                    radiusX.Name, module.TypeSystem.Single, shapeRef) { HasThis = true };
                var ryGet = new MethodReference(
                    radiusY.Name, module.TypeSystem.Single, shapeRef) { HasThis = true };
                TypeReference pointRef = MapCpuType(module, center.ReturnType);
                MethodReference pointX = StructCpuGetter(module, pointRef.FullName, "get_X");
                MethodReference pointY = StructCpuGetter(module, pointRef.FullName, "get_Y");
                var centerLoc = new VariableDefinition(pointRef);
                fill.Body.Variables.Add(centerLoc);
                il.Emit(OpCodes.Ldarga_S, fill.Parameters[0]);
                il.Emit(OpCodes.Call, centerGet);
                il.Emit(OpCodes.Stloc, centerLoc);
                il.Emit(OpCodes.Ldloca_S, centerLoc);
                il.Emit(OpCodes.Call, pointX);
                il.Emit(OpCodes.Ldloca_S, centerLoc);
                il.Emit(OpCodes.Call, pointY);
                il.Emit(OpCodes.Ldarga_S, fill.Parameters[0]);
                il.Emit(OpCodes.Call, rxGet);
                il.Emit(OpCodes.Ldarga_S, fill.Parameters[0]);
                il.Emit(OpCodes.Call, ryGet);
            }
            else
            {
                MethodDefinition? getX = shapeDef.Methods
                    .FirstOrDefault(m => m.Name == "get_X" && m.Parameters.Count == 0);
                MethodDefinition? getY = shapeDef.Methods
                    .FirstOrDefault(m => m.Name == "get_Y" && m.Parameters.Count == 0);
                MethodDefinition? getW = shapeDef.Methods
                    .FirstOrDefault(m => m.Name == "get_Width" && m.Parameters.Count == 0);
                MethodDefinition? getH = shapeDef.Methods
                    .FirstOrDefault(m => m.Name == "get_Height" && m.Parameters.Count == 0);
                if (getX is null || getY is null || getW is null || getH is null)
                    throw new InvalidOperationException(
                        $"CpuPaint: ellipse shape {shapeRef.FullName} has neither Center nor bounds");
                var xLoc = new VariableDefinition(module.TypeSystem.Single);
                var yLoc = new VariableDefinition(module.TypeSystem.Single);
                var wLoc = new VariableDefinition(module.TypeSystem.Single);
                var hLoc = new VariableDefinition(module.TypeSystem.Single);
                foreach (var v in new[] { xLoc, yLoc, wLoc, hLoc })
                    fill.Body.Variables.Add(v);
                (MethodDefinition Def, VariableDefinition Loc)[] parts =
                    [(getX, xLoc), (getY, yLoc), (getW, wLoc), (getH, hLoc)];
                foreach (var (def, loc) in parts)
                {
                    var getter = new MethodReference(
                        def.Name, module.TypeSystem.Single, shapeRef) { HasThis = true };
                    il.Emit(OpCodes.Ldarga_S, fill.Parameters[0]);
                    il.Emit(OpCodes.Call, getter);
                    il.Emit(OpCodes.Stloc, loc);
                }
                // cx = x + w/2; cy = y + h/2; rx = w/2; ry = h/2.
                il.Emit(OpCodes.Ldloc, xLoc);
                il.Emit(OpCodes.Ldloc, wLoc);
                il.Emit(OpCodes.Ldc_R4, 2f);
                il.Emit(OpCodes.Div);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldloc, yLoc);
                il.Emit(OpCodes.Ldloc, hLoc);
                il.Emit(OpCodes.Ldc_R4, 2f);
                il.Emit(OpCodes.Div);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldloc, wLoc);
                il.Emit(OpCodes.Ldc_R4, 2f);
                il.Emit(OpCodes.Div);
                il.Emit(OpCodes.Ldloc, hLoc);
                il.Emit(OpCodes.Ldc_R4, 2f);
                il.Emit(OpCodes.Div);
            }
        }

        il.Emit(OpCodes.Ldloc, brushLoc);
        il.Emit(OpCodes.Ldfld, opacityField);
        foreach (var f in new[] { rField, gField, bField, aField })
        {
            il.Emit(OpCodes.Ldloc, brushLoc);
            il.Emit(OpCodes.Ldfld, f);
        }
        il.Emit(OpCodes.Call, cpuFill);
        var doneLabel = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Br, doneLabel);
        il.Append(notCpu);
        if (!isEllipse)
        {
            // PDN's own SolidColorBrush (e.g. from SolidColorBrushCache,
            // which bypasses the dc resource cache, as the Colors-window
            // palette wells do) implements ISolidColorBrush but is not
            // the synthesized CPU type: read it through the interface
            // exactly like FillGeometry/FillEllipse instead of silently
            // skipping. Other brush kinds keep today's silent skip.
            TypeReference solidRef = OwnedCpuRef(module, "PaintDotNet.Direct2D1.ISolidColorBrush");
            MethodReference getColor = VirtualCpuCall(module, "PaintDotNet.Direct2D1.ISolidColorBrush", "get_Color", 0);
            MethodReference getOpacity = VirtualCpuCall(module, "PaintDotNet.Direct2D1.IDeviceBrush", "get_Opacity", 0);
            MethodReference fillR = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_R");
            MethodReference fillG = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_G");
            MethodReference fillB = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_B");
            MethodReference fillA = StructCpuGetter(module, "PaintDotNet.Imaging.ColorRgba128Float", "get_A");
            TypeReference colorRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.ColorRgba128Float");
            var solidLoc = new VariableDefinition(solidRef);
            var colorLoc = new VariableDefinition(colorRef);
            var opLoc = new VariableDefinition(module.TypeSystem.Single);
            var srLoc = new VariableDefinition(module.TypeSystem.Single);
            var sgLoc = new VariableDefinition(module.TypeSystem.Single);
            var sbLoc = new VariableDefinition(module.TypeSystem.Single);
            var saLoc = new VariableDefinition(module.TypeSystem.Single);
            foreach (var v in new[] { solidLoc, colorLoc, opLoc, srLoc, sgLoc, sbLoc, saLoc })
                fill.Body.Variables.Add(v);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, solidRef);
            il.Emit(OpCodes.Stloc, solidLoc);
            il.Emit(OpCodes.Ldloc, solidLoc);
            var tryUi = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Brfalse, tryUi);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            MethodReference getX = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_X");
            MethodReference getY = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Y");
            MethodReference getW = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Width");
            MethodReference getH = StructCpuGetter(module, "PaintDotNet.Rendering.RectFloat", "get_Height");
            foreach (var g in new[] { getX, getY, getW, getH })
            {
                il.Emit(OpCodes.Ldarga_S, fill.Parameters[0]);
                il.Emit(OpCodes.Call, g);
            }
            il.Emit(OpCodes.Ldloc, solidLoc);
            il.Emit(OpCodes.Callvirt, getOpacity);
            il.Emit(OpCodes.Stloc, opLoc);
            il.Emit(OpCodes.Ldloc, solidLoc);
            il.Emit(OpCodes.Callvirt, getColor);
            il.Emit(OpCodes.Stloc, colorLoc);
            il.Emit(OpCodes.Ldloca_S, colorLoc);
            il.Emit(OpCodes.Call, fillR);
            il.Emit(OpCodes.Stloc, srLoc);
            il.Emit(OpCodes.Ldloca_S, colorLoc);
            il.Emit(OpCodes.Call, fillG);
            il.Emit(OpCodes.Stloc, sgLoc);
            il.Emit(OpCodes.Ldloca_S, colorLoc);
            il.Emit(OpCodes.Call, fillB);
            il.Emit(OpCodes.Stloc, sbLoc);
            il.Emit(OpCodes.Ldloca_S, colorLoc);
            il.Emit(OpCodes.Call, fillA);
            il.Emit(OpCodes.Stloc, saLoc);
            il.Emit(OpCodes.Ldloc, opLoc);
            il.Emit(OpCodes.Ldloc, srLoc);
            il.Emit(OpCodes.Ldloc, sgLoc);
            il.Emit(OpCodes.Ldloc, sbLoc);
            il.Emit(OpCodes.Ldloc, saLoc);
            il.Emit(OpCodes.Call, cpuFill);
            il.Emit(OpCodes.Br, doneLabel);
            il.Append(tryUi);
            // PDN's own SolidColorBrush (UI.Media, e.g. from
            // SolidColorBrushCache, which bypasses the dc resource
            // cache) is neither the CPU type nor an ISolidColorBrush:
            // read Color/Opacity straight off it. Fixture/small-app
            // modules may lack the UI.Media universe entirely; skip the
            // branch then (silent skip, as before) instead of failing
            // the transform.
            TypeReference? uiBrushRef = null;
            MethodReference? getUiColor = null;
            MethodReference? getUiOpacity = null;
            try
            {
                uiBrushRef = OwnedCpuRef(module, "PaintDotNet.UI.Media.SolidColorBrush");
                getUiColor = VirtualCpuCall(module, "PaintDotNet.UI.Media.SolidColorBrush", "get_Color", 0);
                getUiOpacity = VirtualCpuCall(module, "PaintDotNet.UI.Media.Brush", "get_Opacity", 0);
            }
            catch (System.Exception) { }
            if (uiBrushRef is null || getUiColor is null || getUiOpacity is null)
            {
                il.Emit(OpCodes.Br, doneLabel);
            }
            else
            {
            var uiLoc = new VariableDefinition(uiBrushRef);
            fill.Body.Variables.Add(uiLoc);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, uiBrushRef);
            il.Emit(OpCodes.Stloc, uiLoc);
            il.Emit(OpCodes.Ldloc, uiLoc);
            il.Emit(OpCodes.Brfalse, doneLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            foreach (var g in new[] { getX, getY, getW, getH })
            {
                il.Emit(OpCodes.Ldarga_S, fill.Parameters[0]);
                il.Emit(OpCodes.Call, g);
            }
            il.Emit(OpCodes.Ldloc, uiLoc);
            il.Emit(OpCodes.Callvirt, getUiOpacity);
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Stloc, opLoc);
            il.Emit(OpCodes.Ldloc, uiLoc);
            il.Emit(OpCodes.Callvirt, getUiColor);
            il.Emit(OpCodes.Stloc, colorLoc);
            il.Emit(OpCodes.Ldloca_S, colorLoc);
            il.Emit(OpCodes.Call, fillR);
            il.Emit(OpCodes.Stloc, srLoc);
            il.Emit(OpCodes.Ldloca_S, colorLoc);
            il.Emit(OpCodes.Call, fillG);
            il.Emit(OpCodes.Stloc, sgLoc);
            il.Emit(OpCodes.Ldloca_S, colorLoc);
            il.Emit(OpCodes.Call, fillB);
            il.Emit(OpCodes.Stloc, sbLoc);
            il.Emit(OpCodes.Ldloca_S, colorLoc);
            il.Emit(OpCodes.Call, fillA);
            il.Emit(OpCodes.Stloc, saLoc);
            il.Emit(OpCodes.Ldloc, opLoc);
            il.Emit(OpCodes.Ldloc, srLoc);
            il.Emit(OpCodes.Ldloc, sgLoc);
            il.Emit(OpCodes.Ldloc, sbLoc);
            il.Emit(OpCodes.Ldloc, saLoc);
            il.Emit(OpCodes.Call, cpuFill);
            }
        }
        il.Append(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    // Static Draw<TPixel>(RegionPtr<TPixel> mask, ...) replacement: the
    // region bytes become the CPU target (1-byte coverage masks and
    // 4-byte sprite bitmaps; anything else stays inert, exactly like
    // today's neutered body) and the action runs under
    // try/catch/finally (TargetInvocationException unwrapped via
    // ExceptionDispatchInfo) so the handle is always released.
    private static TypeReference UnwrapByref(TypeReference t)
    {
        while (t is RequiredModifierType req) t = req.ElementType;
        while (t is OptionalModifierType opt) t = opt.ElementType;
        return t;
    }

    // Shared Draw tail: run the action through Delegate.DynamicInvoke
    // (closed Action.Invoke tokens do not resolve on the WASM loader)
    // under the Roslyn try/catch/finally shape, freeing the CPU target
    // in the finally. Every Draw driver emits this verbatim so the
    // pinned EH shape holds for all overloads.
    private static void EmitCpuDrawInvoke(
        ModuleDefinition module,
        MethodDefinition draw,
        ParameterDefinition actionParam,
        VariableDefinition handleLoc,
        VariableDefinition ctxLoc,
        MethodDefinition ctxCtor,
        MethodReference freeContext)
    {
        draw.Body.MaxStackSize = Math.Max(draw.Body.MaxStackSize, 32);
        var il = draw.Body.GetILProcessor();
        MethodReference dynamicInvoke = module.ImportReference(
            typeof(Delegate).GetMethod("DynamicInvoke", [typeof(object[])])!);
        TypeReference tieType = module.ImportReference(
            typeof(System.Reflection.TargetInvocationException));
        MethodReference getInner = module.ImportReference(
            typeof(Exception).GetMethod("get_InnerException")!);
        MethodReference ediCapture = module.ImportReference(
            typeof(System.Runtime.ExceptionServices.ExceptionDispatchInfo).GetMethod("Capture", [typeof(Exception)])!);
        MethodReference ediThrow = module.ImportReference(
            typeof(System.Runtime.ExceptionServices.ExceptionDispatchInfo).GetMethod("Throw", Type.EmptyTypes)!);
        var tieLoc = new VariableDefinition(tieType);
        var innerLoc = new VariableDefinition(module.ImportReference(typeof(Exception)));
        draw.Body.Variables.Add(tieLoc);
        draw.Body.Variables.Add(innerLoc);
        int invokeStart = il.Body.Instructions.Count;
        il.Emit(OpCodes.Ldarg, actionParam);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, module.TypeSystem.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, ctxLoc);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, dynamicInvoke);
        il.Emit(OpCodes.Pop);
        var endTarget = il.Create(OpCodes.Ret);
        var midTarget = il.Create(OpCodes.Leave, endTarget);
        il.Emit(OpCodes.Leave, midTarget);
        int catchStart = il.Body.Instructions.Count;
        il.Emit(OpCodes.Stloc, tieLoc);
        il.Emit(OpCodes.Ldloc, tieLoc);
        il.Emit(OpCodes.Callvirt, getInner);
        il.Emit(OpCodes.Stloc, innerLoc);
        il.Emit(OpCodes.Ldloc, innerLoc);
        var nullInner = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Brfalse, nullInner);
        il.Emit(OpCodes.Ldloc, innerLoc);
        il.Emit(OpCodes.Call, ediCapture);
        il.Emit(OpCodes.Callvirt, ediThrow);
        il.Emit(OpCodes.Leave, midTarget);
        il.Append(nullInner);
        il.Emit(OpCodes.Rethrow);
        il.Emit(OpCodes.Leave, midTarget);
        il.Append(midTarget);
        int finallyStart = il.Body.Instructions.Count;
        il.Emit(OpCodes.Ldloc, handleLoc);
        il.Emit(OpCodes.Call, freeContext);
        il.Emit(OpCodes.Endfinally);
        il.Append(endTarget);
        var invokeHandler = new ExceptionHandler(ExceptionHandlerType.Catch);
        invokeHandler.CatchType = tieType;
        invokeHandler.TryStart = il.Body.Instructions[invokeStart];
        invokeHandler.TryEnd = il.Body.Instructions[catchStart];
        invokeHandler.HandlerStart = il.Body.Instructions[catchStart];
        invokeHandler.HandlerEnd = midTarget;
        draw.Body.ExceptionHandlers.Add(invokeHandler);
        var finallyHandler = new ExceptionHandler(ExceptionHandlerType.Finally);
        finallyHandler.TryStart = il.Body.Instructions[invokeStart];
        finallyHandler.TryEnd = il.Body.Instructions[finallyStart];
        finallyHandler.HandlerStart = il.Body.Instructions[finallyStart];
        finallyHandler.HandlerEnd = endTarget;
        draw.Body.ExceptionHandlers.Add(finallyHandler);
    }

    // -- Static Draw(IBitmap, Action) [+ factory]: sprite dab rendering
    // binds the bitmap's lock buffer as a CPU target. Bytes per pixel
    // come from the pixel format; the transform stays identity like
    // FromBitmap. Non-lockable bitmaps fail loudly and name their type.
    private static void RewriteCpuBitmapDraw(
        ModuleDefinition module,
        MethodDefinition draw,
        int bitmapIndex,
        int actionIndex,
        MethodDefinition ctxCtor,
        MethodReference createTarget,
        MethodReference freeContext)
    {
        draw.Body.ExceptionHandlers.Clear();
        draw.Body.Variables.Clear();
        draw.Body.Instructions.Clear();
        TypeReference lockRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.IBitmapLock");
        MethodReference getBuffer = VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_Buffer", 0);
        MethodReference getBufferStride = VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_BufferStride", 0);
        MethodReference getPixelFormat = VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapLock", "get_PixelFormat", 0);
        MethodReference getSize = VirtualCpuCall(module, "PaintDotNet.Imaging.IBitmapSource", "get_Size", 0);
        MethodReference getW = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Width");
        MethodReference getH = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Height");
        MethodReference getBits = StructCpuGetter(module, "PaintDotNet.Imaging.PixelFormat", "GetBitsPerPixel");
        TypeReference sizeRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Rendering.SizeInt32");
        TypeReference formatRef = LocalOrOwnedCpuRef(module, "PaintDotNet.Imaging.PixelFormat");
        var lockLoc = new VariableDefinition(lockRef);
        var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
        var ctxLoc = new VariableDefinition(ctxCtor.DeclaringType);
        var bppLoc = new VariableDefinition(module.TypeSystem.Int32);
        var sizeLoc = new VariableDefinition(sizeRef);
        var wLoc = new VariableDefinition(module.TypeSystem.Int32);
        var hLoc = new VariableDefinition(module.TypeSystem.Int32);
        var formatLoc = new VariableDefinition(formatRef);
        foreach (var v in new[] { lockLoc, handleLoc, ctxLoc, bppLoc, sizeLoc, wLoc, hLoc, formatLoc })
            draw.Body.Variables.Add(v);
        draw.Body.InitLocals = true;
        var il = draw.Body.GetILProcessor();
        ParameterDefinition bitmapParam = draw.Parameters[bitmapIndex];
        ParameterDefinition actionParam = draw.Parameters[actionIndex];
        il.Emit(OpCodes.Ldarg, bitmapParam);
        il.Emit(OpCodes.Isinst, lockRef);
        il.Emit(OpCodes.Stloc, lockLoc);
        var hasLock = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldloc, lockLoc);
        il.Emit(OpCodes.Brtrue, hasLock);
        il.Emit(OpCodes.Ldstr, "CpuDrawingContext.Draw has no headless backend for ");
        il.Emit(OpCodes.Ldarg, bitmapParam);
        il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(object).GetMethod("GetType", Type.EmptyTypes)!));
        il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(Type).GetMethod("get_FullName", Type.EmptyTypes)!));
        il.Emit(OpCodes.Call, module.ImportReference(typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!));
        il.Emit(OpCodes.Newobj, module.ImportReference(typeof(NotSupportedException).GetConstructor([typeof(string)])!));
        il.Emit(OpCodes.Throw);
        il.Append(hasLock);
        il.Emit(OpCodes.Ldloc, lockLoc);
        il.Emit(OpCodes.Callvirt, getPixelFormat);
        il.Emit(OpCodes.Stloc, formatLoc);
        il.Emit(OpCodes.Ldloca_S, formatLoc);
        il.Emit(OpCodes.Call, getBits);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, bppLoc);
        var bppOk = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldloc, bppLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, bppOk);
        il.Emit(OpCodes.Ldloc, bppLoc);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Beq, bppOk);
        il.Emit(OpCodes.Newobj, module.ImportReference(typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!));
        il.Emit(OpCodes.Throw);
        il.Append(bppOk);
        il.Emit(OpCodes.Ldloc, lockLoc);
        il.Emit(OpCodes.Callvirt, getSize);
        il.Emit(OpCodes.Stloc, sizeLoc);
        il.Emit(OpCodes.Ldloca_S, sizeLoc);
        il.Emit(OpCodes.Call, getW);
        il.Emit(OpCodes.Stloc, wLoc);
        il.Emit(OpCodes.Ldloca_S, sizeLoc);
        il.Emit(OpCodes.Call, getH);
        il.Emit(OpCodes.Stloc, hLoc);
        il.Emit(OpCodes.Ldloc, lockLoc);
        il.Emit(OpCodes.Callvirt, getBuffer);
        il.Emit(OpCodes.Ldloc, wLoc);
        il.Emit(OpCodes.Ldloc, hLoc);
        il.Emit(OpCodes.Ldloc, lockLoc);
        il.Emit(OpCodes.Callvirt, getBufferStride);
        il.Emit(OpCodes.Ldloc, bppLoc);
        il.Emit(OpCodes.Call, createTarget);
        il.Emit(OpCodes.Stloc, handleLoc);
        il.Emit(OpCodes.Ldloc, handleLoc);
        il.Emit(OpCodes.Newobj, ctxCtor);
        il.Emit(OpCodes.Stloc, ctxLoc);
        EmitCpuDrawInvoke(module, draw, actionParam, handleLoc, ctxLoc, ctxCtor, freeContext);
    }

    // -- Static Draw(void*, SizeInt32, stride, PixelFormat&, Action)
    // [+ factory]: raw upload buffers bind directly; the format names
    // the pixel width. Same shared tail as every other driver.
    private static void RewriteCpuRawDraw(
        ModuleDefinition module,
        MethodDefinition draw,
        int ptrIndex,
        int sizeIndex,
        int strideIndex,
        int formatIndex,
        int actionIndex,
        MethodDefinition ctxCtor,
        MethodReference createTarget,
        MethodReference freeContext)
    {
        draw.Body.ExceptionHandlers.Clear();
        draw.Body.Variables.Clear();
        draw.Body.Instructions.Clear();
        MethodReference getW = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Width");
        MethodReference getH = StructCpuGetter(module, "PaintDotNet.Rendering.SizeInt32", "get_Height");
        MethodReference getBits = StructCpuGetter(module, "PaintDotNet.Imaging.PixelFormat", "GetBitsPerPixel");
        var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
        var ctxLoc = new VariableDefinition(ctxCtor.DeclaringType);
        var bppLoc = new VariableDefinition(module.TypeSystem.Int32);
        var wLoc = new VariableDefinition(module.TypeSystem.Int32);
        var hLoc = new VariableDefinition(module.TypeSystem.Int32);
        foreach (var v in new[] { handleLoc, ctxLoc, bppLoc, wLoc, hLoc })
            draw.Body.Variables.Add(v);
        draw.Body.InitLocals = true;
        var il = draw.Body.GetILProcessor();
        ParameterDefinition actionParam = draw.Parameters[actionIndex];
        il.Emit(OpCodes.Ldarg, draw.Parameters[formatIndex]);
        il.Emit(OpCodes.Call, getBits);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, bppLoc);
        var bppOk = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldloc, bppLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, bppOk);
        il.Emit(OpCodes.Ldloc, bppLoc);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Beq, bppOk);
        il.Emit(OpCodes.Newobj, module.ImportReference(typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!));
        il.Emit(OpCodes.Throw);
        il.Append(bppOk);
        il.Emit(OpCodes.Ldarga, draw.Parameters[sizeIndex]);
        il.Emit(OpCodes.Call, getW);
        il.Emit(OpCodes.Stloc, wLoc);
        il.Emit(OpCodes.Ldarga, draw.Parameters[sizeIndex]);
        il.Emit(OpCodes.Call, getH);
        il.Emit(OpCodes.Stloc, hLoc);
        il.Emit(OpCodes.Ldarg, draw.Parameters[ptrIndex]);
        il.Emit(OpCodes.Ldloc, wLoc);
        il.Emit(OpCodes.Ldloc, hLoc);
        il.Emit(OpCodes.Ldarg, draw.Parameters[strideIndex]);
        il.Emit(OpCodes.Ldloc, bppLoc);
        il.Emit(OpCodes.Call, createTarget);
        il.Emit(OpCodes.Stloc, handleLoc);
        il.Emit(OpCodes.Ldloc, handleLoc);
        il.Emit(OpCodes.Newobj, ctxCtor);
        il.Emit(OpCodes.Stloc, ctxLoc);
        EmitCpuDrawInvoke(module, draw, actionParam, handleLoc, ctxLoc, ctxCtor, freeContext);
    }
    private static void RewriteCpuDraw(
        ModuleDefinition module,
        MethodDefinition draw,
        int maskIndex,
        int actionIndex,
        MethodDefinition ctxCtor,
        (OpCode Op, MethodReference Ref) regionPtr,
        (OpCode Op, MethodReference Ref) regionWidth,
        (OpCode Op, MethodReference Ref) regionHeight,
        (OpCode Op, MethodReference Ref) regionStride,
        MethodReference createTarget,
        MethodReference freeContext)
    {
        draw.Body.ExceptionHandlers.Clear();
        draw.Body.Variables.Clear();
        draw.Body.Instructions.Clear();
        var handleLoc = new VariableDefinition(module.TypeSystem.Int64);
        var ctxLoc = new VariableDefinition(ctxCtor.DeclaringType);
        var bppLoc = new VariableDefinition(module.TypeSystem.Int32);
        draw.Body.Variables.Add(handleLoc);
        draw.Body.Variables.Add(ctxLoc);
        draw.Body.Variables.Add(bppLoc);
        draw.Body.InitLocals = true;
        var il = draw.Body.GetILProcessor();
        ParameterDefinition maskParam = draw.Parameters[maskIndex];
        ParameterDefinition actionParam = draw.Parameters[actionIndex];

        void EmitBind()
        {
            il.Emit(OpCodes.Ldarga, maskParam);
            il.Emit(regionPtr.Op, regionPtr.Ref);
            il.Emit(OpCodes.Ldarga, maskParam);
            il.Emit(regionWidth.Op, regionWidth.Ref);
            il.Emit(OpCodes.Ldarga, maskParam);
            il.Emit(regionHeight.Op, regionHeight.Ref);
            il.Emit(OpCodes.Ldarga, maskParam);
            il.Emit(regionStride.Op, regionStride.Ref);
            il.Emit(OpCodes.Ldloc, bppLoc);
            il.Emit(OpCodes.Call, createTarget);
            il.Emit(OpCodes.Stloc, handleLoc);
        }

        // Section A: bind the region bytes as the CPU target; bytes per
        // pixel come from sizeof(TPixel) over Draw's own !!0.
        il.Emit(OpCodes.Sizeof, draw.GenericParameters[0]);
        il.Emit(OpCodes.Stloc, bppLoc);
        EmitBind();
        il.Emit(OpCodes.Ldloc, handleLoc);
        il.Emit(OpCodes.Newobj, ctxCtor);
        il.Emit(OpCodes.Stloc, ctxLoc);

        // The action runs through Delegate.DynamicInvoke, not the
        // closed Action<IDrawingContext>.Invoke: closed delegate-Invoke
        // tokens do not resolve on the WASM loader in any provenance
        // (module-scoped reference, same-module definition alike), while
        // DynamicInvoke dispatches fine (live stacks prove it). One call
        // per Draw. The Dispatch wrapper is unwrapped so failures
        // propagate exactly as a direct call would.
        EmitCpuDrawInvoke(module, draw, actionParam, handleLoc, ctxLoc, ctxCtor, freeContext);
    }

    private static TypeDefinition? EnsureHeadlessGdiFontMap(ModuleDefinition module)
    {
        // Top-level, sealed, no generics: same load-safety reasoning as
        // the inline task driver. Implements IGdiFontMap (whose
        // TryGetFontProperties comes from IFontMap) by answering default
        // properties for every family: weight 400, stretch 5, style 0,
        // decorations 0 (the DWRITE normal/default values PDN itself
        // uses for its missing-font fallback).
        const string mapFullName = "PaintDotNet.DirectWrite.HeadlessGdiFontMap";
        TypeDefinition? existing = module.GetType(mapFullName);
        if (existing is not null)
            return existing;

        TypeDefinition? igdi = module.GetType("PaintDotNet.DirectWrite.IGdiFontMap");
        TypeDefinition? ifont = module.GetType("PaintDotNet.DirectWrite.IFontMap");
        TypeDefinition? props = module.GetType("PaintDotNet.DirectWrite.FontProperties");
        if (igdi is null || ifont is null || props is null)
            return null;
        MethodDefinition? tryGetDef = ifont.Methods
            .FirstOrDefault(m => m.Name == "TryGetFontProperties" && m.Parameters.Count == 1);
        MethodDefinition? hideDef = igdi.Methods
            .FirstOrDefault(m => m.Name == "get_HideInactiveFonts" && m.Parameters.Count == 0);
        MethodDefinition? propsCtor = props.Methods
            .FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 6);
        var objectCtor = module.ImportReference(typeof(object).GetConstructor([])!);
        if (tryGetDef is null || hideDef is null || propsCtor is null)
            return null;

        var map = new TypeDefinition(
            "PaintDotNet.DirectWrite", "HeadlessGdiFontMap",
            TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        map.Interfaces.Add(new InterfaceImplementation(igdi));

        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        var ctorIl = ctor.Body.GetILProcessor();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, objectCtor);
        ctorIl.Emit(OpCodes.Ret);
        map.Methods.Add(ctor);

        const MethodAttributes implAttrs =
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot | MethodAttributes.Final;
        var tryGet = new MethodDefinition("TryGetFontProperties", implAttrs, props);
        tryGet.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        tryGet.Overrides.Add(tryGetDef);
        var tg = tryGet.Body.GetILProcessor();
        tg.Emit(OpCodes.Ldarg_1);
        tg.Emit(OpCodes.Ldarg_1);
        tg.Emit(OpCodes.Ldc_I4, 400);
        tg.Emit(OpCodes.Ldc_I4_5);
        tg.Emit(OpCodes.Ldc_I4_0);
        tg.Emit(OpCodes.Ldc_I4_0);
        tg.Emit(OpCodes.Newobj, propsCtor);
        tg.Emit(OpCodes.Ret);
        map.Methods.Add(tryGet);

        var hide = new MethodDefinition("get_HideInactiveFonts", implAttrs, module.TypeSystem.Boolean);
        hide.Overrides.Add(hideDef);
        var hb = hide.Body.GetILProcessor();
        hb.Emit(OpCodes.Ldc_I4_0);
        hb.Emit(OpCodes.Ret);
        map.Methods.Add(hide);

        // IGdiFontMap's full transitive contract: IObjectRef (COM ref
        // sharing), IIsDisposed, IDisposable, ICollection<string> (the
        // map doubles as the family-name list), IEnumerable, and
        // IInternalImpl. The loader rejects the type while ANY slot is
        // unfilled (observed: TryCreateRef), so every member gets an
        // honest headless answer: no refs, undisposed, empty family
        // list, internal-only guard as a no-op.
        TypeReference? stringRef = module.TypeSystem.String;
        TypeReference? boolRef = module.TypeSystem.Boolean;
        TypeReference? intRef = module.TypeSystem.Int32;
        TypeReference? voidRef = module.TypeSystem.Void;
        TypeReference? objectRef = module.TypeSystem.Object;
        TypeReference? typeRef = TargetCorlibRef(module, "System", "Type");
        TypeReference? nullableRef = TargetCorlibRef(module, "System", "Nullable`1");
        TypeReference? ienumerableRef = TargetCorlibRef(module, "System.Collections", "IEnumerable");
        TypeReference? ienumeratorRef = TargetCorlibRef(module, "System.Collections", "IEnumerator");
        TypeReference? idisposableRef = TargetCorlibRef(module, "System", "IDisposable");
        TypeReference? genericEnumerableRef = TargetCorlibRef(module, "System.Collections.Generic", "IEnumerable`1");
        TypeReference? genericEnumeratorRef = TargetCorlibRef(module, "System.Collections.Generic", "IEnumerator`1");
        TypeReference? genericCollectionRef = TargetCorlibRef(module, "System.Collections.Generic", "ICollection`1");
        if (typeRef is null || nullableRef is null || ienumerableRef is null || ienumeratorRef is null
            || idisposableRef is null || genericEnumerableRef is null || genericEnumeratorRef is null
            || genericCollectionRef is null)
            return null;
        AssemblyNameReference? compScope = module.AssemblyReferences
            .FirstOrDefault(r => r.Name == "PaintDotNet.ComponentModel");
        if (compScope is null)
            return null;
        var iobjRef = new TypeReference("PaintDotNet.ComponentModel", "IObjectRef", module, compScope);
        var stringArray = new ArrayType(stringRef);
        var nullableBool = new GenericInstanceType(nullableRef);
        nullableBool.GenericArguments.Add(boolRef);
        var genericEnumeratorOfString = new GenericInstanceType(genericEnumeratorRef);
        genericEnumeratorOfString.GenericArguments.Add(stringRef);
        // The Nullable<bool> ctor MUST come from ImportReference (a
        // compiler-grade token): a hand-built MethodReference on a
        // hand-built GenericInstanceType resolves at write time but the
        // WASM interpreter rejects it ("Method not found:
        // Nullable`1..ctor(bool)", observed live in
        // FontListComboBoxHandler..ctor). Same bug class as the dropped
        // cross-assembly Overrides.
        MethodReference? nullableBoolCtor = null;
        try { nullableBoolCtor = module.ImportReference(typeof(bool?).GetConstructor([typeof(bool)])!); }
        catch { nullableBoolCtor = null; }
        if (nullableBoolCtor is null)
            return null;

        // Overrides entries are kept ONLY for same-module definitions
        // (scope-identical by construction). Cross-assembly and corlib
        // slots rely on implicit name/signature matching: hand-built
        // MemberRefs carry the source scope identity and the loader
        // rejects them ("Method not found", observed live for
        // ICollection<string>.Add).
        void AddImpl(string name, TypeReference ret, Action<ILProcessor>? emit, params TypeReference[] pars)
        {
            var md = new MethodDefinition(name, implAttrs, ret);
            foreach (TypeReference par in pars)
                md.Parameters.Add(new ParameterDefinition(par));
            emit?.Invoke(md.Body.GetILProcessor());
            map.Methods.Add(md);
        }

        var tryCreateRefOut = new ParameterDefinition(new ByReferenceType(iobjRef));
        var tryCreateRef = new MethodDefinition("TryCreateRef", implAttrs, nullableBool);
        tryCreateRef.Parameters.Add(new ParameterDefinition(typeRef));
        tryCreateRef.Parameters.Add(tryCreateRefOut);
        // Headless QueryInterface: the fake implements every interface it
        // can be asked for (font map, object ref, family-name list), so
        // answer true with a ref to self. Returning false (or throwing
        // from CreateRefError) kills FontListComboBoxHandler..ctor, which
        // needs a working fontMap for the text toolbar.
        var tcr = tryCreateRef.Body.GetILProcessor();
        tcr.Emit(OpCodes.Ldarg_2);
        tcr.Emit(OpCodes.Ldarg_0);
        tcr.Emit(OpCodes.Stind_Ref);
        tcr.Emit(OpCodes.Ldc_I4_1);
        tcr.Emit(OpCodes.Newobj, nullableBoolCtor);
        tcr.Emit(OpCodes.Ret);
        map.Methods.Add(tryCreateRef);

        // IFontMapInternal.CollectionChanged: no fonts ever change
        // headless, so subscribe/unsubscribe are no-ops. Overrides only
        // when the interface lives in this same module.
        TypeReference eventHandlerRef;
        try { eventHandlerRef = module.ImportReference(typeof(EventHandler)); }
        catch { return null; }
        TypeDefinition? ifontInternal = module.GetType("PaintDotNet.DirectWrite.IFontMapInternal");
        if (ifontInternal is not null)
            map.Interfaces.Add(new InterfaceImplementation(ifontInternal));
        void AddEventImpl(string name)
        {
            var md = new MethodDefinition(name, implAttrs, voidRef);
            md.Parameters.Add(new ParameterDefinition(eventHandlerRef));
            md.Body.GetILProcessor().Emit(OpCodes.Ret);
            map.Methods.Add(md);
            MethodDefinition? target = ifontInternal?.Methods
                .FirstOrDefault(m => m.Name == name && m.Parameters.Count == 1);
            if (target is not null)
                md.Overrides.Add(target);
        }

        AddEventImpl("add_CollectionChanged");
        AddEventImpl("remove_CollectionChanged");

        AddImpl("InternalImplementationOnly", voidRef, il => il.Emit(OpCodes.Ret));
        AddImpl("get_IsDisposed", boolRef, il => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); });
        AddImpl("Dispose", voidRef, il => il.Emit(OpCodes.Ret));
        AddImpl("get_Count", intRef, il => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); });
        AddImpl("get_IsReadOnly", boolRef, il => { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret); });
        AddImpl("Add", voidRef, il => il.Emit(OpCodes.Ret), stringRef);
        AddImpl("Clear", voidRef, il => il.Emit(OpCodes.Ret));
        AddImpl("Contains", boolRef, il => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); }, stringRef);
        AddImpl("CopyTo", voidRef, il => il.Emit(OpCodes.Ret), stringArray, intRef);
        AddImpl("Remove", boolRef, il => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); }, stringRef);

        // Top-level (never nested generic shapes): an empty family-name
        // enumerator. Both GetEnumerator overloads share the one .ctor;
        // overloads differ by return type only, which IL allows.
        var enumerator = new TypeDefinition(
            "PaintDotNet.DirectWrite", "HeadlessEmptyStringEnumerator",
            TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        enumerator.Interfaces.Add(new InterfaceImplementation(genericEnumeratorOfString));
        enumerator.Interfaces.Add(new InterfaceImplementation(ienumeratorRef));
        var enumCtor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            voidRef);
        var enumCtorIl = enumCtor.Body.GetILProcessor();
        enumCtorIl.Emit(OpCodes.Ldarg_0);
        enumCtorIl.Emit(OpCodes.Call, objectCtor);
        enumCtorIl.Emit(OpCodes.Ret);
        enumerator.Methods.Add(enumCtor);
        void AddEnumImpl(string name, TypeReference ret, Action<ILProcessor> emit)
        {
            var md = new MethodDefinition(name, implAttrs, ret);
            emit(md.Body.GetILProcessor());
            enumerator.Methods.Add(md);
        }

        AddEnumImpl("get_Current", stringRef, il => { il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret); });
        AddEnumImpl("get_Current", objectRef, il => { il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret); });
        AddEnumImpl("MoveNext", boolRef, il => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); });
        AddEnumImpl("Reset", voidRef, il => il.Emit(OpCodes.Ret));
        AddEnumImpl("Dispose", voidRef, il => il.Emit(OpCodes.Ret));
        module.Types.Add(enumerator);

        var enumeratorCtor = enumerator.Methods.First(m => m.Name == ".ctor");
        void AddMapEnumerator(TypeReference ret)
        {
            var md = new MethodDefinition("GetEnumerator", implAttrs, ret);
            var body = md.Body.GetILProcessor();
            body.Emit(OpCodes.Newobj, enumeratorCtor);
            body.Emit(OpCodes.Ret);
            map.Methods.Add(md);
        }

        AddMapEnumerator(genericEnumeratorOfString);
        AddMapEnumerator(ienumeratorRef);

        module.Types.Add(map);
        return map;
    }

    private static Instruction CloneStloc(ILProcessor il, MethodDefinition method, int slot)
    {
        return slot switch
        {
            0 => il.Create(OpCodes.Stloc_0),
            1 => il.Create(OpCodes.Stloc_1),
            2 => il.Create(OpCodes.Stloc_2),
            3 => il.Create(OpCodes.Stloc_3),
            _ => il.Create(OpCodes.Stloc_S, method.Body.Variables[slot]),
        };
    }

    // Factory members that must never run in WASM: every one acquires or
    // dereferences native DWrite COM pointers (calli). The static Create
    // stays: with a neutered .ctor it yields an inert instance whose leaf
    // methods answer null/false.
    private static bool IsWasmNeuteredFactoryMember(MethodDefinition method)
    {
        if (method.Name == "Create" && method.IsStatic)
            return false;
        return method.Name is ".ctor" or "Initialize"
            or "CreateFontFile" or "CreateFontFace"
            or "GetSystemFontCollection" or "GetFontCollection"
            or "CreateTextAnalyzer" or "IsLocalUri" or "get_DWriteFactory";
    }

    // One backward step over a throw-expression instruction: updates the
    // count of stack slots the expression still needs from earlier code.
    // Returns false for opcodes a throw-expression cannot contain (the
    // caller then leaves the throw alone).
    private static bool TryThrowExprStep(Instruction ins, ref int depth)
    {
        int pops;
        int pushes;
        var op = ins.OpCode;
        if (op == OpCodes.Newobj && ins.Operand is MethodReference ctor)
        {
            pops = ctor.Parameters.Count;
            pushes = 1;
        }
        else if (op == OpCodes.Call || op == OpCodes.Callvirt)
        {
            if (ins.Operand is not MethodReference callee)
                return false;
            pops = callee.Parameters.Count + (callee.HasThis ? 1 : 0);
            pushes = callee.ReturnType.FullName == "System.Void" ? 0 : 1;
        }
        else if (op == OpCodes.Newobj || op.FlowControl == FlowControl.Call)
        {
            return false;
        }
        else if (op.StackBehaviourPop == StackBehaviour.Pop1
            && op.StackBehaviourPush == StackBehaviour.Push1)
        {
            pops = 1;
            pushes = 1;
        }
        else if (op.StackBehaviourPop == StackBehaviour.Pop0
            && op.StackBehaviourPush == StackBehaviour.Push1)
        {
            pops = 0;
            pushes = 1;
        }
        else if (op.StackBehaviourPop == StackBehaviour.Pop1
            && op.StackBehaviourPush == StackBehaviour.Push0)
        {
            pops = 1;
            pushes = 0;
        }
        else if (op == OpCodes.Dup)
        {
            pops = 1;
            pushes = 2;
        }
        else if (op == OpCodes.Pop)
        {
            pops = 1;
            pushes = 0;
        }
        else if (op == OpCodes.Newarr)
        {
            pops = 1;
            pushes = 1;
        }
        else if (op.StackBehaviourPop == StackBehaviour.Pop1_pop1
            && op.StackBehaviourPush == StackBehaviour.Push1)
        {
            pops = 2;
            pushes = 1;
        }
        else if (op == OpCodes.Constrained || op == OpCodes.Readonly)
        {
            pops = 0;
            pushes = 0;
        }
        else
        {
            return false;
        }
        depth = depth - pushes + pops;
        return depth >= 0;
    }

    /// <summary>
    /// Stubs every remaining P/Invoke in an already-loaded module with the
    /// headless defaults (handles/ids/pointers stay nonzero so callers
    /// proceed; window-query predicates answer false). Shared by the
    /// file-based <see cref="StubPInvokes"/> pass and the full rewrite:
    /// transformed assemblies keep live natives otherwise, and the first
    /// one hit at runtime (Telerik IsZoomed during layout) kills boot.
    /// </summary>
    private static readonly HashSet<string> CasRedirectFullNames = new(StringComparer.Ordinal)
    {
        "System.Security.CodeAccessPermission",
        "System.Security.IPermission",
        "System.Security.PermissionSet",
        "System.Security.SecurityManager",
        "System.Security.AccessControl.AccessControlActions",
        "System.Security.AccessControl.RegistryRights",
        "System.Security.Permissions.CodeAccessSecurityAttribute",
        "System.Security.Permissions.EnvironmentPermission",
        "System.Security.Permissions.EnvironmentPermissionAccess",
        "System.Security.Permissions.EnvironmentPermissionAttribute",
        "System.Security.Permissions.FileIOPermission",
        "System.Security.Permissions.FileIOPermissionAccess",
        "System.Security.Permissions.FileIOPermissionAttribute",
        "System.Security.Permissions.HostProtectionAttribute",
        "System.Security.Permissions.HostProtectionResource",
        "System.Security.Permissions.IUnrestrictedPermission",
        "System.Security.Permissions.PermissionSetAttribute",
        "System.Security.Permissions.PermissionState",
        "System.Security.Permissions.PrincipalPermission",
        "System.Security.Permissions.PrincipalPermissionAttribute",
        "System.Security.Permissions.ReflectionPermission",
        "System.Security.Permissions.ReflectionPermissionAttribute",
        "System.Security.Permissions.ReflectionPermissionFlag",
        "System.Security.Permissions.RegistryPermission",
        "System.Security.Permissions.RegistryPermissionAccess",
        "System.Security.Permissions.RegistryPermissionAttribute",
        "System.Security.Permissions.SecurityAction",
        "System.Security.Permissions.SecurityAttribute",
        "System.Security.Permissions.SecurityPermission",
        "System.Security.Permissions.SecurityPermissionAttribute",
        "System.Security.Permissions.SecurityPermissionFlag",
        "System.Security.Permissions.UIPermission",
        "System.Security.Permissions.UIPermissionAttribute",
        "System.Security.Permissions.UIPermissionClipboard",
        "System.Security.Permissions.UIPermissionWindow",
    };

    // CAS permission types do not exist in the browser runtime; any
    // surviving reference is fatal at load (RadLabel's UIPermission field
    // broke every Telerik app before any form painted). Same
    // namespace+name, scope moved to the WinForms fork stubs
    // (CasPermissionStubs.cs). Runs after the caller typeMap so the stub
    // scope always wins for these names.
    public static void RedirectCasTypeReferences(Mono.Cecil.ModuleDefinition module)
    {
        Mono.Cecil.AssemblyNameReference? formsScope = null;
        foreach (Mono.Cecil.TypeReference typeRef in module.GetTypeReferences())
        {
            if (typeRef is Mono.Cecil.TypeSpecification || typeRef.Scope == module)
                continue;
            if (!CasRedirectFullNames.Contains(typeRef.FullName.Replace('/', '+')))
                continue;
            formsScope ??= module.AssemblyReferences.FirstOrDefault(r => r.Name == "WfWasm.WinForms");
            if (formsScope is null)
            {
                formsScope = new Mono.Cecil.AssemblyNameReference("WfWasm.WinForms", new Version(1, 0, 0, 0));
                module.AssemblyReferences.Add(formsScope);
            }
            typeRef.Scope = formsScope;
        }
    }

    // System.Configuration.* (settings + AppSettings) does not exist in
    // the browser runtime; the netfx scopes (System / System.Configuration
    // 4.0.0.0) never resolve. Retarget the whole namespace tree to the
    // System.Configuration.ConfigurationManager package, which ships in
    // _framework (CanvasHost package reference) and implements every
    // referenced member (verified against the closure: 15 types).
    // Regression: TypeLoadException ApplicationSettingsBase on a
    // ThreadPool thread <- BookShelf settings load (FailFast post-paint).
    public static void RedirectConfigurationTypeReferences(Mono.Cecil.ModuleDefinition module)
    {
        Mono.Cecil.AssemblyNameReference? configScope = null;
        foreach (Mono.Cecil.TypeReference typeRef in module.GetTypeReferences())
        {
            if (typeRef is Mono.Cecil.TypeSpecification || typeRef.Scope == module)
                continue;
            string? ns = typeRef.Namespace;
            if (ns != "System.Configuration" && (ns is null || !ns.StartsWith("System.Configuration.", StringComparison.Ordinal)))
                continue;
            configScope ??= module.AssemblyReferences.FirstOrDefault(r => r.Name == "System.Configuration.ConfigurationManager");
            if (configScope is null)
            {
                configScope = new Mono.Cecil.AssemblyNameReference("System.Configuration.ConfigurationManager", new Version(9, 0, 0, 0));
                configScope.PublicKeyToken = Array.Empty<byte>();
                module.AssemblyReferences.Add(configScope);
            }
            typeRef.Scope = configScope;
        }
    }

    public static void RedirectIsolatedStorageTypeReferences(Mono.Cecil.ModuleDefinition module)
    {
        Mono.Cecil.AssemblyNameReference? isoScope = null;
        foreach (Mono.Cecil.TypeReference typeRef in module.GetTypeReferences())
        {
            if (typeRef is Mono.Cecil.TypeSpecification || typeRef.Scope == module)
                continue;
            // The compatibility assembly implements only these two classes.
            // Keep enums/exceptions in the framework: redirecting IsolatedStorageScope
            // to a nonexistent stub type breaks constant emission in Syncfusion.Shared.Base.
            if (typeRef.Namespace != "System.IO.IsolatedStorage"
                || typeRef.Name is not ("IsolatedStorageFile" or "IsolatedStorageFileStream"))
                continue;
            isoScope ??= module.AssemblyReferences.FirstOrDefault(r => r.Name == "WfWasm.IsolatedStorage");
            if (isoScope is null)
            {
                isoScope = new Mono.Cecil.AssemblyNameReference("WfWasm.IsolatedStorage", new Version(1, 0, 0, 0));
                isoScope.PublicKeyToken = Array.Empty<byte>();
                module.AssemblyReferences.Add(isoScope);
            }
            typeRef.Scope = isoScope;
        }
    }

    public static void RetargetNativeLoaderCalls(Mono.Cecil.ModuleDefinition module)
    {
        // kernel32 loader queries are absent headless (no Win32 loader in
        // WASM): LoadLibraryEx/GetModuleHandle answer null-handle instead of
        // trapping inside an unresolvable PInvoke (RichTextBox.CreateParams
        // loads MsftEdit on first construction and OOB-trapped the session
        // with no managed frame). The null handle keeps documented failure
        // paths (Win32Exception). String overloads only: wider signatures
        // (PCWSTR/HANDLE) keep their imports and stay visible.
        Mono.Cecil.AssemblyNameReference? formsScope = null;
        Mono.Cecil.TypeReference? nativeLibType = null;
        Mono.Cecil.MethodReference? loadLibraryExNull = null;
        Mono.Cecil.MethodReference? getModuleHandleNull = null;
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions.ToList())
                {
                    if ((instruction.OpCode == Mono.Cecil.Cil.OpCodes.Call || instruction.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
                        && instruction.Operand is Mono.Cecil.MethodReference mr
                        && (mr.Name == "LoadLibraryEx" || mr.Name == "GetModuleHandle")
                        && mr.DeclaringType.FullName == "Windows.Win32.PInvoke")
                    {
                        bool wantLoad = mr.Name == "LoadLibraryEx" && mr.Parameters.Count == 2
                            && mr.Parameters[0].ParameterType.FullName == "System.String";
                        bool wantHandle = mr.Name == "GetModuleHandle" && mr.Parameters.Count == 1
                            && mr.Parameters[0].ParameterType.FullName == "System.String";
                        if (!wantLoad && !wantHandle) continue;
                        // Self-exclusion: the shim desktop passthrough calls
                        // the same API; retargeting it would recurse.
                        if (type.FullName == "System.Windows.Forms.WasmNativeLibrary"
                            && (method.Name == "LoadLibraryExNull" || method.Name == "GetModuleHandleNull"))
                            continue;
                        if (formsScope is null)
                        {
                            formsScope = module.AssemblyReferences.FirstOrDefault(r => r.Name == "WfWasm.WinForms");
                            if (formsScope is null)
                            {
                                formsScope = new Mono.Cecil.AssemblyNameReference("WfWasm.WinForms", new Version(1, 0, 0, 0));
                                module.AssemblyReferences.Add(formsScope);
                            }
                            nativeLibType = new Mono.Cecil.TypeReference("System.Windows.Forms", "WasmNativeLibrary", module, formsScope);
                            var hinstanceType = new Mono.Cecil.TypeReference("Windows.Win32.Foundation", "HINSTANCE", module, formsScope);
                            var loadFlagsType = new Mono.Cecil.TypeReference("Windows.Win32.System.LibraryLoader", "LOAD_LIBRARY_FLAGS", module, formsScope);
                            loadLibraryExNull = new Mono.Cecil.MethodReference("LoadLibraryExNull", hinstanceType, nativeLibType)
                            {
                                HasThis = false,
                            };
                            loadLibraryExNull.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.String));
                            loadLibraryExNull.Parameters.Add(new Mono.Cecil.ParameterDefinition(loadFlagsType));
                            getModuleHandleNull = new Mono.Cecil.MethodReference("GetModuleHandleNull", hinstanceType, nativeLibType)
                            {
                                HasThis = false,
                            };
                            getModuleHandleNull.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.String));
                        }
                        instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                        instruction.Operand = wantLoad ? loadLibraryExNull : getModuleHandleNull;
                    }
                }
            }
        }
    }
    public static void RedirectMediaTypeReferences(Mono.Cecil.ModuleDefinition module)
    {
        Mono.Cecil.AssemblyNameReference? mediaScope = null;
        foreach (Mono.Cecil.TypeReference typeRef in module.GetTypeReferences())
        {
            if (typeRef is Mono.Cecil.TypeSpecification || typeRef.Scope == module)
                continue;
            if (typeRef.Namespace != "System.Media")
                continue;
            mediaScope ??= module.AssemblyReferences.FirstOrDefault(r => r.Name == "System.Windows.Extensions");
            if (mediaScope is null)
            {
                mediaScope = new Mono.Cecil.AssemblyNameReference("System.Windows.Extensions", new Version(4, 0, 0, 0));
                mediaScope.PublicKeyToken = Array.Empty<byte>();
                module.AssemblyReferences.Add(mediaScope);
            }
            typeRef.Scope = mediaScope;
        }
    }

    public static void RedirectOleDbTypeReferences(Mono.Cecil.ModuleDefinition module)
    {
        Mono.Cecil.AssemblyNameReference? oleDbScope = null;
        foreach (Mono.Cecil.TypeReference typeRef in module.GetTypeReferences())
        {
            if (typeRef is Mono.Cecil.TypeSpecification || typeRef.Scope == module)
                continue;
            if (typeRef.Namespace != "System.Data.OleDb")
                continue;
            oleDbScope ??= module.AssemblyReferences.FirstOrDefault(r => r.Name == "System.Data.OleDb");
            if (oleDbScope is null)
            {
                oleDbScope = new Mono.Cecil.AssemblyNameReference("System.Data.OleDb", new Version(4, 0, 0, 0));
                oleDbScope.PublicKeyToken = Array.Empty<byte>();
                module.AssemblyReferences.Add(oleDbScope);
            }
            typeRef.Scope = oleDbScope;
        }
    }

    public static void RedirectSpinDrawBackground(Mono.Cecil.ModuleDefinition module)
    {
        Mono.Cecil.AssemblyNameReference? formsScope = module.AssemblyReferences
            .FirstOrDefault(r => r.Name == "WfWasm.WinForms" || r.Name == "System.Windows.Forms");
        if (formsScope is null)
            return;
        Mono.Cecil.AssemblyNameReference? drawingScope = module.AssemblyReferences
            .FirstOrDefault(r => r.Name == "WfWasm.Drawing" || r.Name == "System.Drawing" || r.Name == "System.Drawing.Common");
        if (drawingScope is null)
            return;
        var painterType = new Mono.Cecil.TypeReference("System.Windows.Forms.VisualStyles", "VisualStyleSpinPainter", module, formsScope);
        var rectType = new Mono.Cecil.TypeReference("System.Drawing", "Rectangle", module, drawingScope);
        Mono.Cecil.MethodReference spin2 = new Mono.Cecil.MethodReference("DrawSpinBackground", module.TypeSystem.Void, painterType)
        {
            HasThis = false,
        };
        spin2.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
        spin2.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
        spin2.Parameters.Add(new Mono.Cecil.ParameterDefinition(rectType));
        Mono.Cecil.MethodReference spin3 = new Mono.Cecil.MethodReference("DrawSpinBackground", module.TypeSystem.Void, painterType)
        {
            HasThis = false,
        };
        spin3.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
        spin3.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Object));
        spin3.Parameters.Add(new Mono.Cecil.ParameterDefinition(rectType));
        spin3.Parameters.Add(new Mono.Cecil.ParameterDefinition(rectType));
        foreach (Mono.Cecil.TypeDefinition type in module.GetTypes())
        {
            if (type.FullName == "System.Windows.Forms.VisualStyles.VisualStyleSpinPainter")
                continue;
            foreach (Mono.Cecil.MethodDefinition method in type.Methods)
            {
                if (!method.HasBody)
                    continue;
                foreach (Mono.Cecil.Cil.Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != Mono.Cecil.Cil.OpCodes.Callvirt
                        && instruction.OpCode != Mono.Cecil.Cil.OpCodes.Call)
                        continue;
                    if (instruction.Operand is not Mono.Cecil.MethodReference mr)
                        continue;
                    if (mr.Name != "DrawBackground"
                        || mr.DeclaringType is null
                        || mr.DeclaringType.FullName != "System.Windows.Forms.VisualStyles.VisualStyleRenderer")
                        continue;
                    if (mr.Parameters.Count == 2)
                    {
                        instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                        instruction.Operand = spin2;
                    }
                    else if (mr.Parameters.Count == 3)
                    {
                        instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                        instruction.Operand = spin3;
                    }
                }
            }
        }
    }

    public static void RedirectScrollButtonDraw(Mono.Cecil.ModuleDefinition module)
    {
        // ControlPaint.DrawScrollButton's classic branch ends in User32
        // DrawFrameControl, a success-stubbed P/Invoke headless, so every
        // classic scroll arrow paints nothing (PDN Colors-window numeric
        // chips render white with no arrows; RenderWithVisualStyles is
        // false headless so the themed path never runs). Retarget both
        // static overloads to the fork's GDI+ scroll painter. The helper
        // reference reuses the original call site's own parameter scopes,
        // so the emitted signature is expressed in exactly the type
        // identities the caller already resolves (same assemblies that
        // let the original call bind); only the declaring type moves to
        // the fork helper, which ships in that same forms assembly.
        foreach (Mono.Cecil.TypeDefinition type in module.GetTypes())
        {
            if (type.FullName == "System.Windows.Forms.ControlPaintScrollPainter")
                continue;
            foreach (Mono.Cecil.MethodDefinition method in type.Methods)
            {
                if (!method.HasBody)
                    continue;
                foreach (Mono.Cecil.Cil.Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != Mono.Cecil.Cil.OpCodes.Call
                        && instruction.OpCode != Mono.Cecil.Cil.OpCodes.Callvirt)
                        continue;
                    if (instruction.Operand is not Mono.Cecil.MethodReference mr)
                        continue;
                    if (mr.Name != "DrawScrollButton"
                        || mr.DeclaringType is null
                        || mr.DeclaringType.FullName != "System.Windows.Forms.ControlPaint")
                        continue;
                    int count = mr.Parameters.Count;
                    if (count != 4 && count != 7)
                        continue;
                    var painterType = new Mono.Cecil.TypeReference(
                        "System.Windows.Forms", "ControlPaintScrollPainter", module, mr.DeclaringType.Scope);
                    var helper = new Mono.Cecil.MethodReference("DrawScrollButton", module.TypeSystem.Void, painterType)
                    {
                        HasThis = false,
                    };
                    foreach (var p in mr.Parameters)
                        helper.Parameters.Add(new Mono.Cecil.ParameterDefinition(p.ParameterType));
                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                    instruction.Operand = helper;
                }
            }
        }
    }

    public static void FixCyotekColorGridBounds(Mono.Cecil.ModuleDefinition module)
    {
        // Cyotek ColorGrid GetColor/GetColorSource/SetColor guard
        // `index > count` instead of `index >= count`: a click on the
        // empty custom well (index == Colors.Count + CustomColors.Count)
        // reports Source=Custom (not None), flows through the ColorIndex
        // setter into GetColor(count) and throws IndexOutOfRangeException,
        // so the selection never commits (0px change, caught IOORE).
        // The vendored IL expresses the guard as
        //   ldarg.1, <count>, <custom>, add, ble.s REAL
        // falling through to the Empty/None/throw block; swapping ble to
        // blt (same size, short form preserved) corrects the boundary.
        // Only the guard ble is touched: it is identified by its
        // fall-through block shape (Empty load / None+ret / throw of
        // ArgumentOutOfRangeException), so the SetColor dispatch ble
        // (fall-through = custom set_Item) is left alone.
        if (module.GetType("Cyotek.Windows.Forms.ColorGrid") is null)
            return;
        foreach (Mono.Cecil.TypeDefinition type in module.GetTypes())
        {
            if (type.FullName != "Cyotek.Windows.Forms.ColorGrid")
                continue;
            foreach (Mono.Cecil.MethodDefinition method in type.Methods)
            {
                if (!method.HasBody)
                    continue;
                bool isGetColor = method.Name == "GetColor" && method.Parameters.Count == 1
                    && method.Parameters[0].ParameterType.MetadataType == Mono.Cecil.MetadataType.Int32;
                bool isGetSource = method.Name == "GetColorSource" && method.Parameters.Count == 1
                    && method.Parameters[0].ParameterType.MetadataType == Mono.Cecil.MetadataType.Int32;
                bool isSetColor = method.Name == "SetColor" && method.Parameters.Count == 2;
                if (!isGetColor && !isGetSource && !isSetColor)
                    continue;
                var ins = method.Body.Instructions;
                for (int k = 0; k < ins.Count; k++)
                {
                    var op = ins[k].OpCode;
                    bool isBle = op == Mono.Cecil.Cil.OpCodes.Ble || op == Mono.Cecil.Cil.OpCodes.Ble_S;
                    if (!isBle)
                        continue;
                    if (k + 1 >= ins.Count)
                        continue;
                    var next = ins[k + 1];
                    bool guardFail = false;
                    if (isGetColor && next.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld)
                        guardFail = next.Operand is Mono.Cecil.FieldReference fr
                            && fr.Name == "Empty"
                            && fr.DeclaringType.FullName == "System.Drawing.Color";
                    else if (isSetColor && next.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr)
                        guardFail = next.Operand is string s && s == "colorIndex";
                    else if (isGetSource && next.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0
                        && k + 3 < ins.Count
                        && ins[k + 2].OpCode == Mono.Cecil.Cil.OpCodes.Stloc_0
                        && (ins[k + 3].OpCode == Mono.Cecil.Cil.OpCodes.Br_S || ins[k + 3].OpCode == Mono.Cecil.Cil.OpCodes.Br))
                    {
                        if (ins[k + 3].Operand is Mono.Cecil.Cil.Instruction end
                            && ins[k].Operand is Mono.Cecil.Cil.Instruction target
                            && ins.IndexOf(end) > ins.IndexOf(target))
                            guardFail = true;
                    }
                    if (!guardFail)
                        continue;
                    ins[k].OpCode = op == Mono.Cecil.Cil.OpCodes.Ble_S
                        ? Mono.Cecil.Cil.OpCodes.Blt_S
                        : Mono.Cecil.Cil.OpCodes.Blt;
                }
            }
        }
    }

    public static void StubModulePInvokes(Mono.Cecil.ModuleDefinition module)
    {
            MethodReference? allocZeroedRef = null;
            MethodReference? freeRef = null;
            MethodReference? reallocRef = null;
            try
            {
                var allocM = typeof(System.Runtime.InteropServices.NativeMemory).GetMethod("AllocZeroed", [typeof(nuint)]);
                if (allocM != null) allocZeroedRef = module.ImportReference(allocM);
                var freeM = typeof(System.Runtime.InteropServices.NativeMemory).GetMethod("Free", [typeof(void*)]);
                if (freeM != null) freeRef = module.ImportReference(freeM);
                var reallocM = typeof(System.Runtime.InteropServices.NativeMemory).GetMethod("Realloc", [typeof(void*), typeof(nuint)]);
                if (reallocM != null) reallocRef = module.ImportReference(reallocM);
            }
            catch { }

            // Rev61: fork GDI text surface for app-private extent
            // imports (helper refs are built per method from that
            // method's own parameter types, so the Size reference is
            // the app's own and remaps with everything else). Null
            // when the module has no WinForms reference to anchor to.
            TypeReference? gdiTextType = null;
            try
            {
                var gdiFormsScope = module.AssemblyReferences.FirstOrDefault(r => r.Name == "System.Windows.Forms");
                if (gdiFormsScope != null)
                    gdiTextType = new TypeReference("System.Windows.Forms", "WasmGdiText", module, gdiFormsScope);
            }
            catch { }

            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods.ToList())
                {
                    if (!method.IsPInvokeImpl)
                        continue;

                    method.IsPInvokeImpl = false;
                    method.PInvokeInfo = null;
                    method.Attributes &= ~MethodAttributes.PInvokeImpl;
                    method.Body = new Mono.Cecil.Cil.MethodBody(method);
                    var il = method.Body.GetILProcessor();

                    if (method.Name == "HeapAlloc" && allocZeroedRef != null && method.Parameters.Count >= 3)
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
                        if (method.Parameters[2].ParameterType.FullName != "System.UIntPtr")
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, allocZeroedRef);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if ((method.Name == "VirtualAlloc" || method.Name == "VirtualAllocEx") && allocZeroedRef != null)
                    {
                        int sizeArgIdx = method.Name == "VirtualAllocEx" ? 2 : 1;
                        if (method.Parameters.Count > sizeArgIdx)
                        {
                            il.Emit(sizeArgIdx == 1 ? Mono.Cecil.Cil.OpCodes.Ldarg_1 : Mono.Cecil.Cil.OpCodes.Ldarg_2);
                            if (method.Parameters[sizeArgIdx].ParameterType.FullName != "System.UIntPtr")
                                il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Call, allocZeroedRef);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                        else
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x1000);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                    }
                    else if (method.Name == "CoTaskMemAlloc" && allocZeroedRef != null && method.Parameters.Count >= 1)
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        if (method.Parameters[0].ParameterType.FullName != "System.UIntPtr")
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, allocZeroedRef);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.Name == "GlobalAlloc" && allocZeroedRef != null && method.Parameters.Count >= 2)
                    {
                        var loc = new Mono.Cecil.Cil.VariableDefinition(method.ReturnType);
                        method.Body.Variables.Add(loc);
                        method.Body.InitLocals = true;
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Initobj, method.ReturnType);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
                        if (method.Parameters[1].ParameterType.FullName != "System.UIntPtr")
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, allocZeroedRef);
                        var valField = method.ReturnType.Resolve()?.Fields.FirstOrDefault(f => f.Name == "Value");
                        if (valField != null)
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(valField));
                        }
                        else
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
                        }
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.Name == "HeapReAlloc" && reallocRef != null && allocZeroedRef != null && method.Parameters.Count >= 4)
                    {
                        var branchAlloc = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2); // lpMem
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x10000);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ble_Un, branchAlloc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2); // lpMem
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_3); // dwBytes
                        if (method.Parameters[3].ParameterType.FullName != "System.UIntPtr")
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, reallocRef);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        il.Append(branchAlloc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_3); // dwBytes
                        if (method.Parameters[3].ParameterType.FullName != "System.UIntPtr")
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, allocZeroedRef);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.Name == "HeapFree" && freeRef != null && method.Parameters.Count >= 3)
                    {
                        var skipFree = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x10000);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ble_Un, skipFree);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, freeRef);
                        il.Append(skipFree);

                        var loc = new Mono.Cecil.Cil.VariableDefinition(method.ReturnType);
                        method.Body.Variables.Add(loc);
                        method.Body.InitLocals = true;
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Initobj, method.ReturnType);
                        var valField = method.ReturnType.Resolve()?.Fields.FirstOrDefault(f => f.Name == "Value");
                        if (valField != null)
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(valField));
                        }
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if ((method.Name == "VirtualFree" || method.Name == "VirtualFreeEx") && freeRef != null)
                    {
                        int addrArgIdx = method.Name == "VirtualFreeEx" ? 1 : 0;
                        var skipFree = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
                        il.Emit(addrArgIdx == 0 ? Mono.Cecil.Cil.OpCodes.Ldarg_0 : Mono.Cecil.Cil.OpCodes.Ldarg_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x10000);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ble_Un, skipFree);
                        il.Emit(addrArgIdx == 0 ? Mono.Cecil.Cil.OpCodes.Ldarg_0 : Mono.Cecil.Cil.OpCodes.Ldarg_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, freeRef);
                        il.Append(skipFree);

                        if (method.ReturnType.FullName == "System.Boolean")
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                        else
                        {
                            var loc = new Mono.Cecil.Cil.VariableDefinition(method.ReturnType);
                            method.Body.Variables.Add(loc);
                            method.Body.InitLocals = true;
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Initobj, method.ReturnType);
                            var valField = method.ReturnType.Resolve()?.Fields.FirstOrDefault(f => f.Name == "Value");
                            if (valField != null)
                            {
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(valField));
                            }
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, loc);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                    }
                    else if (method.Name == "CoTaskMemFree" && freeRef != null && method.Parameters.Count >= 1)
                    {
                        var skipFree = il.Create(Mono.Cecil.Cil.OpCodes.Ret);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x10000);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ble_Un, skipFree);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, freeRef);
                        il.Append(skipFree);
                    }
                    else if ((method.Name == "GlobalFree" || method.Name == "LocalFree") && freeRef != null && method.Parameters.Count >= 1)
                    {
                        var skipFree = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
                        var p0Field = method.Parameters[0].ParameterType.Resolve()?.Fields.FirstOrDefault(f => f.Name == "Value");
                        if (p0Field != null)
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarga_S, method.Parameters[0]);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldfld, module.ImportReference(p0Field));
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x10000);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ble_Un, skipFree);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarga_S, method.Parameters[0]);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldfld, module.ImportReference(p0Field));
                            il.Emit(Mono.Cecil.Cil.OpCodes.Call, freeRef);
                            il.Append(skipFree);
                        }
                        var loc = new Mono.Cecil.Cil.VariableDefinition(method.ReturnType);
                        method.Body.Variables.Add(loc);
                        method.Body.InitLocals = true;
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Initobj, method.ReturnType);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.Name == "VirtualProtect" && method.Parameters.Count >= 4)
                    {
                        var skipWrite = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_3);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Brfalse, skipWrite);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_3);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 4); // PAGE_READWRITE
                        il.Emit(Mono.Cecil.Cil.OpCodes.Stind_I4);
                        il.Append(skipWrite);

                        var loc = new Mono.Cecil.Cil.VariableDefinition(method.ReturnType);
                        method.Body.Variables.Add(loc);
                        method.Body.InitLocals = true;
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Initobj, method.ReturnType);
                        var valField = method.ReturnType.Resolve()?.Fields.FirstOrDefault(f => f.Name == "Value");
                        if (valField != null)
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(valField));
                        }
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.FullName == "System.Void")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.Name == "GetCurrentThreadId" || method.Name == "GetCurrentProcessId")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.Name == "GetWindowThreadProcessId")
                    {
                        // Thread-affinity coherence: the single browser UI
                        // thread owns every window, and GetCurrentThreadId
                        // stubs to 1 above. Returning the generic 0 here
                        // made dispatcher thread checks (Paint.NET's
                        // ControlDispatcher) conclude their controls were
                        // created on the wrong thread. Also report process
                        // id 1 through the out pointer when one is passed.
                        var pidParam = method.Parameters.FirstOrDefault(p =>
                            p.ParameterType is Mono.Cecil.PointerType or Mono.Cecil.ByReferenceType);
                        if (pidParam != null)
                        {
                            var skipWrite = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, pidParam);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Brfalse, skipWrite);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, pidParam);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Stind_I4);
                            il.Append(skipWrite);
                        }
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.Name == "GetDeviceCaps")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 96);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (gdiTextType != null
                        && (method.Name == "GetTextExtentPoint32" || method.Name == "GetTextExtentPoint32W" || method.Name == "GetTextExtentPoint32A")
                        && method.Parameters.Count == 4
                        && method.Parameters[0].ParameterType.FullName == "System.IntPtr"
                        && method.Parameters[1].ParameterType.FullName == "System.String"
                        && method.Parameters[3].ParameterType is Mono.Cecil.ByReferenceType
                        && (method.ReturnType.FullName == "System.Int32" || method.ReturnType.FullName == "System.UInt32" || method.ReturnType.FullName == "System.Boolean"))
                    {
                        // Rev61: app-private GDI text extents must WRITE the
                        // out Size (fork text bridge); the generic stub
                        // returned 0 with a zeroed struct, so every Telerik
                        // document span measured (0,0) and typed glyphs
                        // never rastered. Regression: ZZ invisible <-
                        // RadTextMeasurer <- NativeTextRenderer import.
                        var extentRef = new MethodReference("GetTextExtentPoint32", module.TypeSystem.Int32, gdiTextType) { HasThis = false };
                        extentRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
                        extentRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                        extentRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                        extentRef.Parameters.Add(new ParameterDefinition(method.Parameters[3].ParameterType));
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_3);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, extentRef);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (gdiTextType != null
                        && (method.Name == "GetTextExtentExPoint" || method.Name == "GetTextExtentExPointW" || method.Name == "GetTextExtentExPointA")
                        && method.Parameters.Count == 7
                        && method.Parameters[0].ParameterType.FullName == "System.IntPtr"
                        && method.Parameters[1].ParameterType.FullName == "System.String"
                        && method.Parameters[4].ParameterType is Mono.Cecil.ArrayType
                        && method.Parameters[5].ParameterType is Mono.Cecil.ArrayType
                        && method.Parameters[6].ParameterType is Mono.Cecil.ByReferenceType
                        && (method.ReturnType.FullName == "System.Boolean" || method.ReturnType.FullName == "System.Int32" || method.ReturnType.FullName == "System.UInt32"))
                    {
                        var extentExRef = new MethodReference("GetTextExtentExPoint", module.TypeSystem.Boolean, gdiTextType) { HasThis = false };
                        extentExRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
                        extentExRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                        extentExRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                        extentExRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                        extentExRef.Parameters.Add(new ParameterDefinition(method.Parameters[4].ParameterType));
                        extentExRef.Parameters.Add(new ParameterDefinition(method.Parameters[5].ParameterType));
                        extentExRef.Parameters.Add(new ParameterDefinition(method.Parameters[6].ParameterType));
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_3);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, method.Parameters[4]);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, method.Parameters[5]);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, method.Parameters[6]);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, extentExRef);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (gdiTextType != null
                        && method.Name == "SelectObject"
                        && type.Name == "NativeTextRenderer"
                        && method.Parameters.Count == 2
                        && method.Parameters[0].ParameterType.FullName == "System.IntPtr"
                        && method.Parameters[1].ParameterType.FullName == "System.IntPtr"
                        && method.ReturnType.FullName == "System.IntPtr")
                    {
                        // Registers the selected HFONT with the bridge so
                        // the extents above measure with the right font.
                        var selectRef = new MethodReference("SelectObject", module.TypeSystem.IntPtr, gdiTextType) { HasThis = false };
                        selectRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
                        selectRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, selectRef);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (gdiTextType != null
                        && (method.Name == "DrawText" || method.Name == "DrawTextW" || method.Name == "DrawTextA")
                        && method.Parameters.Count == 5
                        && method.Parameters[0].ParameterType.FullName == "System.IntPtr"
                        && method.Parameters[1].ParameterType.FullName == "System.String"
                        && method.Parameters[2].ParameterType.FullName == "System.Int32"
                        && method.Parameters[3].ParameterType is Mono.Cecil.ByReferenceType
                        && (method.ReturnType.FullName == "System.Int32" || method.ReturnType.FullName == "System.UInt32"))
                    {
                        // Rev81: app-private gdi32 DrawText imports (Syncfusion
                        // NativeMethods/NativePaint/ComboBoxEx/Win32API nested
                        // RECT&) trap the interpreter as native imports; the
                        // generic stub's success-shaped zero drops all node
                        // text (TreeViewAdv paints labels through this path on
                        // a zero fork HDC, resolved by WasmFillBridge during
                        // scoped paint). Retarget to WasmGdiText.DrawText: the
                        // rect decomposes through the signature's own struct
                        // (field-name casing varies: left vs Left), so take
                        // the first four Int32 fields in declaration order.
                        // Unresolvable shapes keep the stub (quiet zero, no
                        // worse than today). CALCRECT extents write back
                        // through the out pair. Regression: zero-width labels <-
                        // DrawText <- TreeViewAdv.OnPaint.
                        var dtRectDef = (method.Parameters[3].ParameterType as Mono.Cecil.ByReferenceType)?.ElementType.Resolve();
                        var dtFields = dtRectDef?.Fields
                            .Where(f => !f.IsStatic && f.FieldType.FullName == "System.Int32")
                            .Take(4).ToList();
                        var dtFifth = method.Parameters[4].ParameterType.FullName;
                        bool dtFifthOk = dtFifth == "System.Int32" || dtFifth == "System.UInt32";
                        if (!dtFifthOk)
                        {
                            try { dtFifthOk = method.Parameters[4].ParameterType.Resolve()?.IsEnum == true; }
                            catch { dtFifthOk = false; }
                        }
                        if (dtFields is not null && dtFields.Count == 4 && dtFifthOk)
                        {
                            var dtRectElem = ((Mono.Cecil.ByReferenceType)method.Parameters[3].ParameterType).ElementType;
                            var drawRef = new MethodReference("DrawText", module.TypeSystem.Int32, gdiTextType) { HasThis = false };
                            drawRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
                            drawRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                            for (int dti = 0; dti < 6; dti++)
                                drawRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                            var dtIntByRef = new Mono.Cecil.ByReferenceType(module.TypeSystem.Int32);
                            drawRef.Parameters.Add(new ParameterDefinition(dtIntByRef));
                            drawRef.Parameters.Add(new ParameterDefinition(dtIntByRef));
                            var dtRightVar = new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Int32);
                            var dtBottomVar = new Mono.Cecil.Cil.VariableDefinition(module.TypeSystem.Int32);
                            var dtRectVar = new Mono.Cecil.Cil.VariableDefinition(module.ImportReference(dtRectElem));
                            method.Body.Variables.Add(dtRectVar);
                            method.Body.Variables.Add(dtRightVar);
                            method.Body.Variables.Add(dtBottomVar);
                            method.Body.InitLocals = true;
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_3);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldobj, module.ImportReference(dtRectElem));
                            il.Emit(Mono.Cecil.Cil.OpCodes.Stloc, dtRectVar);
                            foreach (var dtField in dtFields)
                            {
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, dtRectVar);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldfld, module.ImportReference(dtField));
                            }
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, method.Parameters[4]);
                            if (dtFifth != "System.Int32")
                                il.Emit(Mono.Cecil.Cil.OpCodes.Conv_I4);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, dtRightVar);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, dtBottomVar);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Call, drawRef);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, method.Parameters[3]);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, dtRightVar);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(dtFields[2]));
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, method.Parameters[3]);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, dtBottomVar);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(dtFields[3]));
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                            method.Body.MaxStackSize = System.Math.Max(method.Body.MaxStackSize, 12);
                        }
                        else
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                    }
                    else if (method.Name == "GetDpiForMonitor")
                    {
                        // Monitor-DPI queries report S_OK with 96/96 through
                        // the out params. The generic IntPtr sentinel (0x1000)
                        // reads as a failed HRESULT here (Telerik declares the
                        // HRESULT as IntPtr), and the generic int default (0)
                        // leaves the outs zeroed, which scales fonts to zero.
                        // Any dpiType spelling answers 96: headless has one
                        // scale. Regression: COMException E_FAIL <-
                        // GetMonitorDpi <- GetScaledFont <- RadForm..ctor <-
                        // PhotoAlbum boot (silent death).
                        foreach (var p in method.Parameters)
                        {
                            if (p.ParameterType is not Mono.Cecil.ByReferenceType
                                and not Mono.Cecil.PointerType)
                                continue;
                            var skipDpiWrite = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, p);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Brfalse, skipDpiWrite);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, p);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 96);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Stind_I4);
                            il.Append(skipDpiWrite);
                        }
                        if (method.ReturnType.FullName == "System.Void")
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                        else if (method.ReturnType.FullName == "System.IntPtr" || method.ReturnType.FullName == "System.UIntPtr")
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_I);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                        else if (method.ReturnType.FullName == "System.Int32" || method.ReturnType.FullName == "System.UInt32")
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                        else if (method.ReturnType.IsValueType)
                        {
                            var dpiLoc = new Mono.Cecil.Cil.VariableDefinition(method.ReturnType);
                            method.Body.Variables.Add(dpiLoc);
                            method.Body.InitLocals = true;
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, dpiLoc);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Initobj, method.ReturnType);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, dpiLoc);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                        else
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                    }
                    else if (method.Name == "GetDpiForWindow" || method.Name == "GetDpiForSystem")
                    {
                        // Value-returning DPI queries (UINT, not HRESULT):
                        // one headless scale, 96.
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 96);
                        if (method.ReturnType.FullName == "System.IntPtr" || method.ReturnType.FullName == "System.UIntPtr")
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_I);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.Name == "GetScrollBarInfo" && method.Parameters.Count == 3)
                    {
                        // Native scroll-state reads leave managed int[] fields virgin
                        // headless (the desktop marshaller materializes them when native
                        // fills the struct): Syncfusion
                        // ScrollersFrame.UpdateScrollerSettings ldelem's
                        // SCROLLBARINFO.rgstate unconditionally, so a virgin struct NREs
                        // during TreeViewAdv parenting once any fake handle exists
                        // (the IsHandleCreated bail the vendor relies on pre-Show no
                        // longer holds). Materialize the 6-DWORD state buffer (native
                        // CCHILDREN_SCROLLBAR layout) when null; zeros mean "no state
                        // flags", matching the vendor defaults the pre-handle bail takes
                        // on desktop. Still returns FALSE (no native state). Pointer-
                        // shaped params keep the old behavior (no safe deref).
                        // Regression: NRE <- UpdateScrollerSettings <-
                        // SynchronizeScrollbars <- TreeViewAdvDragDrop.Form1..ctor
                        // (treeview window failure).
                        var sbiParam = method.Parameters[2];
                        if (sbiParam.ParameterType is Mono.Cecil.ByReferenceType sbiRef
                            && sbiRef.ElementType.Resolve() is { } sbiDef)
                        {
                            var rgField = sbiDef.Fields.FirstOrDefault(f => f.Name == "rgstate"
                                && f.FieldType.FullName == "System.Int32[]");
                            if (rgField != null)
                            {
                                var rgRef = module.ImportReference(rgField);
                                var haveRg = il.Create(Mono.Cecil.Cil.OpCodes.Nop);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, sbiParam);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldfld, rgRef);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Brtrue, haveRg);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, sbiParam);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_6);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Newarr, module.TypeSystem.Int32);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, rgRef);
                                il.Append(haveRg);
                            }
                        }
                        method.Body.MaxStackSize = System.Math.Max(method.Body.MaxStackSize, 1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                        if (method.ReturnType.FullName == "System.IntPtr" || method.ReturnType.FullName == "System.UIntPtr")
                            il.Emit(Mono.Cecil.Cil.OpCodes.Conv_I);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.Name == "ReleaseDC" || method.Name == "DeleteDC" || method.Name == "DeleteObject")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.Name == "GetSystemMetrics")
                    {
                        var l1 = il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, 1920);
                        var l2 = il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, 1080);
                        var l3 = il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, 10000);
                        var lDef = il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4, 32);
                        var ret = il.Create(Mono.Cecil.Cil.OpCodes.Ret);

                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Beq, l1);

                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Beq, l2);

                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 59);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Beq, l3);

                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 60);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Beq, l3);

                        il.Append(lDef);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Br, ret);
                        il.Append(l1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Br, ret);
                        il.Append(l2);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Br, ret);
                        il.Append(l3);
                        il.Append(ret);
                    }
                    else if (method.Name == "GetDC" || method.Name == "CreateCompatibleDC")
                    {
                        var loc = new Mono.Cecil.Cil.VariableDefinition(method.ReturnType);
                        method.Body.Variables.Add(loc);
                        method.Body.InitLocals = true;
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Initobj, method.ReturnType);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x1000);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        var valField = method.ReturnType.Resolve()?.Fields.FirstOrDefault(f => f.Name == "Value");
                        if (valField != null)
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(valField));
                        }
                        else
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
                        }
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (IsNativeAddressQuery(method.Name)
                        && (method.ReturnType.IsPointer
                            || method.ReturnType.FullName == "System.IntPtr"
                            || method.ReturnType.FullName == "System.UIntPtr"))
                    {
                        // Module/proc-address queries fail closed: no native
                        // module can load headless, so LoadLibrary /
                        // GetModuleHandle / GetProcAddress answer NULL (the
                        // desktop failure shape). Callers null-check and
                        // degrade; the 0x1000 sentinel instead reads as a
                        // live address and the runtime traps generating the
                        // call stub. Regression: func[7893] OOB <-
                        // GetDelegateForFunctionPointer <- DllWrapper <-
                        // ShapedForm..ctor <- ERP boot (silent death).
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.IsPointer || method.ReturnType.IsByReference)
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x1000);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.FullName == "System.IntPtr" || method.ReturnType.FullName == "System.UIntPtr")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x1000);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_I);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.FullName == "System.UInt16" || method.ReturnType.FullName == "System.Int16")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if ((method.Name == "GetDynamicTimeZoneInformation" || method.Name == "GetTimeZoneInformation")
                        && (method.ReturnType.FullName == "System.UInt32" || method.ReturnType.FullName == "System.Int32"))
                    {
                        // Win32 time-zone queries fail closed with
                        // TIME_ZONE_ID_INVALID (-1): the documented failure
                        // code, which callers must handle (branch to a
                        // custom/UTC zone). Answering 0 (UNKNOWN) instead
                        // sends Telerik down the registry-read path, which
                        // has no headless answers. Regression: MME/NRE <-
                        // TimeZoneInfo.GetLocalTimeZone <- Scheduler <-
                        // hotelapp boot (silent-ish death).
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_M1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.FullName == "System.UInt32" || method.ReturnType.FullName == "System.Int32")
                    {
                        if (method.Name.StartsWith("Register") || method.Name == "RegisterWindowMessage")
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0xC001);
                        else
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if ((method.Name == "GetTextMetrics" || method.Name == "GetTextMetricsA" || method.Name == "GetTextMetricsW")
                        && method.Parameters.Any(p => p.ParameterType is Mono.Cecil.ByReferenceType))
                    {
                        // GDI text metrics drive baseline ratios (ascent/height)
                        // in rich-text/grid stacks; the generic stub answered
                        // success with a zeroed struct, so 0/0.0 poisoned layout
                        // with NaN. Fill the well-known int/char fields with
                        // coherent 13px-UI-font metrics (ascent+descent==height,
                        // same scale as the fork PInvoke stub) and report success.
                        // Absolute scale stays a guess until real font metrics
                        // arrive; zeros are never valid for height/ascent.
                        // Regression: InvalidOperationException MeasureOverride-NaN
                        // <- ParagraphLayoutBox <- RichTextEditor boot.
                        var fills = new (string Name, int Value)[]
                        {
                            ("tmHeight", 13), ("tmAscent", 11), ("tmDescent", 2),
                            ("tmInternalLeading", 1), ("tmAveCharWidth", 7),
                            ("tmMaxCharWidth", 13), ("tmDigitizedAspectX", 96),
                            ("tmDigitizedAspectY", 96), ("tmFirstChar", 32),
                            ("tmLastChar", 255), ("tmDefaultChar", 63),
                            ("tmBreakChar", 32),
                        };
                        var tmParam = method.Parameters.First(p => p.ParameterType is Mono.Cecil.ByReferenceType);
                        var tmElem = ((Mono.Cecil.ByReferenceType)tmParam.ParameterType).ElementType;
                        var tmDef = tmElem.Resolve();
                        if (tmDef != null)
                        {
                            foreach (var (name, value) in fills)
                            {
                                var field = tmDef.Fields.FirstOrDefault(f => f.Name == name
                                    && (f.FieldType.FullName == "System.Int32" || f.FieldType.FullName == "System.Char"));
                                if (field == null)
                                    continue;
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, tmParam);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, value);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(field));
                            }
                        }
                        if (method.ReturnType.FullName == "System.Void")
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                        else if (method.ReturnType.FullName == "System.Boolean"
                            || method.ReturnType.FullName == "System.Int32"
                            || method.ReturnType.FullName == "System.UInt32")
                        {
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                        else
                        {
                            var loc = new Mono.Cecil.Cil.VariableDefinition(method.ReturnType);
                            method.Body.Variables.Add(loc);
                            method.Body.InitLocals = true;
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Initobj, method.ReturnType);
                            var valField = method.ReturnType.Resolve()?.Fields.FirstOrDefault(f => f.Name == "Value");
                            if (valField != null)
                            {
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(valField));
                            }
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, loc);
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                        }
                    }
                    else if (method.ReturnType.FullName == "System.Boolean"
                        && method.Name.Length > 2
                        && method.Name.StartsWith("Is", System.StringComparison.Ordinal)
                        && char.IsUpper(method.Name[2]))
                    {
                        // Window-query predicates (IsZoomed, IsIconic,
                        // IsWindowVisible, ...): headless has no native
                        // windows, so every query answers false. Success
                        // codes (ReleaseDC et al.) keep the true default
                        // below: their names never take Is+Upper form.
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.FullName == "System.Boolean")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.IsPrimitive)
                    {
                        switch (method.ReturnType.FullName)
                        {
                            case "System.Int64":
                            case "System.UInt64":
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I8, 0L);
                                break;
                            case "System.Single":
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_R4, 0.0f);
                                break;
                            case "System.Double":
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_R8, 0.0);
                                break;
                            default:
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                                break;
                        }
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.IsValueType)
                    {
                        var loc = new Mono.Cecil.Cil.VariableDefinition(method.ReturnType);
                        method.Body.Variables.Add(loc);
                        method.Body.InitLocals = true;
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Initobj, method.ReturnType);
                        var valField = method.ReturnType.Resolve()?.Fields.FirstOrDefault(f => f.Name == "Value");
                        if (valField != null)
                        {
                            if (method.ReturnType.Name == "BOOL")
                            {
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(valField));
                            }
                            else if (method.ReturnType.Name != "HRESULT" && !IsNativeAddressQuery(method.Name))
                            {
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldloca_S, loc);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 0x1000);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                                il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, module.ImportReference(valField));
                            }
                        }
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, loc);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                }
            }
    }

    // Scopes the writer still emits typerefs against: reachability from
    // live metadata (signatures, bodies, attributes, forwarders), not
    // TypeRef-table presence (strip passes leave dead rows the writer
    // prunes). Assembly-ref trimming consults this; dropping a live
    // scope orphans its typerefs and Cecil re-emits them against the
    // wrong table (observed: System.Private.CoreLib, fatal at bind).
    private static HashSet<Mono.Cecil.IMetadataScope> LiveScopes(Mono.Cecil.ModuleDefinition module)
    {
        var live = new HashSet<Mono.Cecil.IMetadataScope>();
        foreach (Mono.Cecil.CustomAttribute attribute in module.Assembly.CustomAttributes)
            CollectScopes(attribute, live);
        foreach (Mono.Cecil.CustomAttribute attribute in module.CustomAttributes)
            CollectScopes(attribute, live);
        CollectSecurityScopes(module.Assembly, live);
        foreach (Mono.Cecil.ExportedType forwarded in module.ExportedTypes)
            if (forwarded.Scope is not null)
                live.Add(forwarded.Scope);
        foreach (Mono.Cecil.TypeDefinition type in module.GetTypes())
        {
            CollectScopes(type.BaseType, live);
            foreach (Mono.Cecil.InterfaceImplementation face in type.Interfaces)
                CollectScopes(face.InterfaceType, live);
            foreach (Mono.Cecil.GenericParameter generic in type.GenericParameters)
                foreach (Mono.Cecil.GenericParameterConstraint constraint in generic.Constraints)
                    CollectScopes(constraint.ConstraintType, live);
            foreach (Mono.Cecil.FieldDefinition field in type.Fields)
            {
                CollectScopes(field.FieldType, live);
                foreach (Mono.Cecil.CustomAttribute attribute in field.CustomAttributes)
                    CollectScopes(attribute, live);
            }
            foreach (Mono.Cecil.PropertyDefinition property in type.Properties)
            {
                CollectScopes(property.PropertyType, live);
                foreach (Mono.Cecil.CustomAttribute attribute in property.CustomAttributes)
                    CollectScopes(attribute, live);
            }
            foreach (Mono.Cecil.EventDefinition @event in type.Events)
            {
                CollectScopes(@event.EventType, live);
                foreach (Mono.Cecil.CustomAttribute attribute in @event.CustomAttributes)
                    CollectScopes(attribute, live);
            }
            foreach (Mono.Cecil.MethodDefinition method in type.Methods)
            {
                CollectScopes(method.ReturnType, live);
                foreach (Mono.Cecil.ParameterDefinition parameter in method.Parameters)
                    CollectScopes(parameter.ParameterType, live);
                foreach (Mono.Cecil.GenericParameter generic in method.GenericParameters)
                    foreach (Mono.Cecil.GenericParameterConstraint constraint in generic.Constraints)
                        CollectScopes(constraint.ConstraintType, live);
                if (method.HasBody)
                {
                    foreach (Mono.Cecil.Cil.VariableDefinition variable in method.Body.Variables)
                        CollectScopes(variable.VariableType, live);
                    foreach (Mono.Cecil.Cil.Instruction instruction in method.Body.Instructions)
                        CollectScopes(instruction.Operand, live);
                    foreach (Mono.Cecil.Cil.ExceptionHandler handler in method.Body.ExceptionHandlers)
                        CollectScopes(handler.CatchType, live);
                }
                foreach (Mono.Cecil.CustomAttribute attribute in method.CustomAttributes)
                    CollectScopes(attribute, live);
                CollectSecurityScopes(method, live);
            }
            foreach (Mono.Cecil.CustomAttribute attribute in type.CustomAttributes)
                CollectScopes(attribute, live);
            CollectSecurityScopes(type, live);
        }
        return live;
    }

    private static void CollectSecurityScopes(Mono.Cecil.ICustomAttributeProvider owner, HashSet<Mono.Cecil.IMetadataScope> live)
    {
        if (owner is Mono.Cecil.AssemblyDefinition assembly && assembly.HasSecurityDeclarations)
            foreach (Mono.Cecil.SecurityDeclaration declaration in assembly.SecurityDeclarations)
                CollectScopes(declaration, live);
        if (owner is Mono.Cecil.TypeDefinition type && type.HasSecurityDeclarations)
            foreach (Mono.Cecil.SecurityDeclaration declaration in type.SecurityDeclarations)
                CollectScopes(declaration, live);
        if (owner is Mono.Cecil.MethodDefinition method && method.HasSecurityDeclarations)
            foreach (Mono.Cecil.SecurityDeclaration declaration in method.SecurityDeclarations)
                CollectScopes(declaration, live);
    }

    private static void CollectScopes(object? node, HashSet<Mono.Cecil.IMetadataScope> live)
    {
        CollectScopes(node, live, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static void CollectScopes(object? node, HashSet<Mono.Cecil.IMetadataScope> live, HashSet<object> seen)
    {
        if (node is null || !seen.Add(node))
            return;
        switch (node)
        {
            case null:
                return;
            case Mono.Cecil.TypeReference type:
                if (type.Scope is not null)
                    live.Add(type.Scope);
                switch (type)
                {
                    case Mono.Cecil.GenericInstanceType generic:
                        foreach (Mono.Cecil.TypeReference argument in generic.GenericArguments)
                            CollectScopes(argument, live, seen);
                        CollectScopes(generic.ElementType, live, seen);
                        break;
                    case Mono.Cecil.TypeSpecification specification:
                        CollectScopes(specification.ElementType, live, seen);
                        break;
                    case Mono.Cecil.GenericParameter parameter:
                        foreach (Mono.Cecil.GenericParameterConstraint constraint in parameter.Constraints)
                            CollectScopes(constraint.ConstraintType, live, seen);
                        break;
                }
                // FunctionPointerType stores its signature off-band (ElementType is null).
                if (type is Mono.Cecil.FunctionPointerType fnptr)
                {
                    CollectScopes(fnptr.ReturnType, live, seen);
                    foreach (Mono.Cecil.ParameterDefinition parameter in fnptr.Parameters)
                        CollectScopes(parameter.ParameterType, live, seen);
                }
                return;
            case Mono.Cecil.MethodReference method:
                CollectScopes(method.DeclaringType, live, seen);
                CollectScopes(method.ReturnType, live, seen);
                foreach (Mono.Cecil.ParameterDefinition parameter in method.Parameters)
                    CollectScopes(parameter.ParameterType, live, seen);
                if (method is Mono.Cecil.GenericInstanceMethod genericMethod)
                    foreach (Mono.Cecil.TypeReference argument in genericMethod.GenericArguments)
                        CollectScopes(argument, live, seen);
                return;
            case Mono.Cecil.FieldReference field:
                CollectScopes(field.DeclaringType, live, seen);
                CollectScopes(field.FieldType, live, seen);
                return;
            case Mono.Cecil.ParameterDefinition parameter:
                CollectScopes(parameter.ParameterType, live, seen);
                return;
            case Mono.Cecil.Cil.VariableDefinition variable:
                CollectScopes(variable.VariableType, live, seen);
                return;
            case Mono.Cecil.CustomAttribute attribute:
                CollectScopes(attribute.AttributeType, live, seen);
                foreach (Mono.Cecil.CustomAttributeArgument argument in attribute.ConstructorArguments)
                    CollectScopes(argument.Type, live, seen);
                foreach (Mono.Cecil.CustomAttributeNamedArgument argument in attribute.Properties)
                    CollectScopes(argument.Argument.Type, live, seen);
                foreach (Mono.Cecil.CustomAttributeNamedArgument argument in attribute.Fields)
                    CollectScopes(argument.Argument.Type, live, seen);
                return;
            case Mono.Cecil.SecurityDeclaration declaration:
                foreach (Mono.Cecil.SecurityAttribute attribute in declaration.SecurityAttributes)
                    CollectScopes(attribute, live, seen);
                return;
            default:
                return;
        }
    }

    // Native module/proc-address queries: LoadLibrary / GetModuleHandle /
    // GetProcAddress in any entry-point spelling (A/W suffixed or bare).
    // These fail closed (NULL) in the stubber; every other shape keeps
    // the sentinel/success defaults.
    private static bool IsNativeAddressQuery(string name)
    {
        string bare = name;
        if (bare.Length > 1 && (bare.EndsWith('A') || bare.EndsWith('W')))
        {
            string trimmed = bare.Substring(0, bare.Length - 1);
            if (trimmed is "LoadLibrary" or "LoadLibraryEx" or "GetModuleHandle" or "GetModuleHandleEx" or "GetProcAddress")
                return true;
        }

        return bare is "LoadLibrary" or "LoadLibraryEx" or "GetModuleHandle" or "GetModuleHandleEx" or "GetProcAddress";
    }

    public static void StubPInvokes(string assemblyPath, string outputPath)
    {
        string? fileDir = Path.GetDirectoryName(assemblyPath);
        using var resolver = new FrameworkResolver(fileDir);
        var bytes = File.ReadAllBytes(assemblyPath);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var stream = new MemoryStream(bytes);
        using var assembly = AssemblyDefinition.ReadAssembly(stream, readerParams);
        var module = assembly.MainModule;
        module.Attributes |= ModuleAttributes.ILOnly;
        module.Attributes &= ~ModuleAttributes.Required32Bit;
        module.Attributes &= ~ModuleAttributes.Preferred32Bit;

        StubModulePInvokes(module);
        var outDir = Path.GetDirectoryName(outputPath) ?? ".";
        var tempFile = Path.Combine(outDir, Guid.NewGuid() + ".tmp");
        assembly.Write(tempFile);
        assembly.Dispose();
        File.Move(tempFile, outputPath, true);
    }

    /// <summary>
    /// Diagnostic pass: inserts <c>Console.WriteLine("[probe] ENTER/EXIT
    /// Type::Method")</c> breadcrumbs into the listed methods (matched as
    /// <c>TypeFullName::MethodName</c>). Used to locate instant engine
    /// deaths (null-function calli traps) that leave no managed trace:
    /// the last ENTER without EXIT is the killer's container. Never part
    /// of the shipping rewrite; applied manually on top of a regen and
    /// wiped by the next regen.
    /// </summary>
    // Diagnostic only (--calls): enumerate every method/PInvoke/member
    // reference used by the named method bodies. Read-only.
    public static int DumpMethodCalls(string assemblyPath, IEnumerable<string> methodKeys)
    {
        var keys = new HashSet<string>(methodKeys, StringComparer.Ordinal);
        string? dir = Path.GetDirectoryName(assemblyPath);
        using var resolver = new FrameworkResolver(dir, [AppContext.BaseDirectory]);
        var bytes = File.ReadAllBytes(assemblyPath);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var stream = new MemoryStream(bytes);
        using var assembly = AssemblyDefinition.ReadAssembly(stream, readerParams);
        int dumped = 0;
        foreach (var type in assembly.MainModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                if (!keys.Contains(type.FullName + "::" + method.Name)) continue;
                dumped++;
                Console.WriteLine("METHOD " + type.FullName + "::" + method.ToString());
                foreach (var ins in method.Body.Instructions)
                {
                    try
                    {
                        if (ins.Operand is MethodReference mr)
                        {
                            string pinvoke = "";
                            try
                            {
                                var def = mr.Resolve();
                                if (def is not null && def.IsPInvokeImpl && def.PInvokeInfo is not null)
                                    pinvoke = " [PInvoke " + def.PInvokeInfo.Module.Name + "!" + def.PInvokeInfo.EntryPoint + "]";
                            }
                            catch { }
                            Console.WriteLine("  " + ins.OpCode.ToString() + " " + mr.DeclaringType.FullName + "::" + mr.Name + pinvoke);
                        }
                        else if (ins.Operand is FieldReference fr)
                            Console.WriteLine("  " + ins.OpCode.ToString() + " field " + fr.DeclaringType.FullName + "::" + fr.Name);
                    }
                    catch { }
                }
            }
        }
        return dumped;
    }

    // Diagnostic only: --ildump <dll> <Type::Method>... prints full
    // IL (offsets, opcodes, operands) plus exception handlers, for
    // triage where the call list hides control flow. Read-only.
    public static int DumpIL(string assemblyPath, IEnumerable<string> methodKeys)
    {
        var keys = new HashSet<string>(methodKeys, StringComparer.Ordinal);
        string? dir = Path.GetDirectoryName(assemblyPath);
        using var resolver = new FrameworkResolver(dir, [AppContext.BaseDirectory]);
        var bytes = File.ReadAllBytes(assemblyPath);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var stream = new MemoryStream(bytes);
        using var assembly = AssemblyDefinition.ReadAssembly(stream, readerParams);
        int dumped = 0;
        foreach (var type in assembly.MainModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                if (!keys.Contains(type.FullName + "::" + method.Name)) continue;
                dumped++;
                Console.WriteLine("METHOD " + type.FullName + "::" + method.ToString());
                foreach (var h in method.Body.ExceptionHandlers)
                    Console.WriteLine($"  handler {h.HandlerType} try {h.TryStart?.Offset:X4}-{h.TryEnd?.Offset:X4} handler {h.HandlerStart?.Offset:X4}-{h.HandlerEnd?.Offset:X4} catch={h.CatchType?.FullName}");
                foreach (var ins in method.Body.Instructions)
                {
                    string op;
                    try
                    {
                        op = ins.Operand switch
                        {
                            MethodReference mr => mr.DeclaringType.FullName + "::" + mr.Name,
                            FieldReference fr => "field " + fr.DeclaringType.FullName + "::" + fr.Name,
                            TypeReference tr => "type " + tr.FullName,
                            string s => "\"" + (s.Length > 60 ? s[..60] + "..." : s) + "\"",
                            null => "",
                            _ => ins.Operand.ToString() ?? "",
                        };
                    }
                    catch { op = "?"; }
                    Console.WriteLine($"  {ins.Offset:X4}: {ins.OpCode} {op}");
                }
            }
        }
        return dumped;
    }
    public static int InstrumentProbes(string assemblyPath, string outputPath, IEnumerable<string> methodKeys)
    {
        var keys = new HashSet<string>(methodKeys, StringComparer.Ordinal);
        // (Shared framework dirs come from FrameworkResolver discovery, not the
        // stale hardcoded 9.0.20 paths that used to sit here.)
        // Rewritten app assemblies reference the migrated fork, which
        // lives in the scratch staging dirs (see rewrite-*.ps1), not next
        // to the app DLLs: metadata resolution needs them on the path.
        string? probeDir = Path.GetDirectoryName(assemblyPath);
        using var resolver = new FrameworkResolver(probeDir, [AppContext.BaseDirectory]);
        var bytes = File.ReadAllBytes(assemblyPath);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var stream = new MemoryStream(bytes);
        using var assembly = AssemblyDefinition.ReadAssembly(stream, readerParams);
        var module = assembly.MainModule;
        var writeLine = module.ImportReference(
            typeof(Console).GetMethod("WriteLine", [typeof(string)])!);
        int probed = 0;
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;
                if (!keys.Contains(type.FullName + "::" + method.Name))
                    continue;
                var il = method.Body.GetILProcessor();
                string tag = "[probe] " + type.FullName + "::" + method.Name;
                method.Body.Instructions.Insert(0, il.Create(OpCodes.Call, writeLine));
                method.Body.Instructions.Insert(0, il.Create(OpCodes.Ldstr, "ENTER " + tag));
                foreach (var ret in method.Body.Instructions
                    .Where(i => i.OpCode == OpCodes.Ret).ToList())
                {
                    il.InsertBefore(ret, il.Create(OpCodes.Ldstr, "EXIT " + tag));
                    il.InsertBefore(ret, il.Create(OpCodes.Call, writeLine));
                }

                // The inserts add one net stack slot (ldstr ahead of its call):
                // keep the stored max honest or the method goes InvalidProgram
                // (a self-defeating probe: it would die at transform with no ENTER).
                method.Body.MaxStackSize++;
                // The EXIT pair before each ret grows the distance between
                // existing short branches and their targets (and loop
                // back-edges to method start grow from the ENTER pair). A
                // branch sitting at the +/-127 edge then writes a bad
                // target (Cecil never expands short branches): expand
                // every short form up front, the same long-form rule the
                // emitters follow. This retires the historic victims
                // (SetVisualStyle, GetFormatString, FocusManager.Draw,
                // RenderRulers, DrawDocumentBackground, set_Unit, EndUpdate).
                foreach (var ins in method.Body.Instructions)
                {
                    if (ins.OpCode == OpCodes.Br_S) ins.OpCode = OpCodes.Br;
                    else if (ins.OpCode == OpCodes.Brfalse_S) ins.OpCode = OpCodes.Brfalse;
                    else if (ins.OpCode == OpCodes.Brtrue_S) ins.OpCode = OpCodes.Brtrue;
                    else if (ins.OpCode == OpCodes.Beq_S) ins.OpCode = OpCodes.Beq;
                    else if (ins.OpCode == OpCodes.Bge_S) ins.OpCode = OpCodes.Bge;
                    else if (ins.OpCode == OpCodes.Bgt_S) ins.OpCode = OpCodes.Bgt;
                    else if (ins.OpCode == OpCodes.Ble_S) ins.OpCode = OpCodes.Ble;
                    else if (ins.OpCode == OpCodes.Blt_S) ins.OpCode = OpCodes.Blt;
                    else if (ins.OpCode == OpCodes.Bne_Un_S) ins.OpCode = OpCodes.Bne_Un;
                    else if (ins.OpCode == OpCodes.Bge_Un_S) ins.OpCode = OpCodes.Bge_Un;
                    else if (ins.OpCode == OpCodes.Bgt_Un_S) ins.OpCode = OpCodes.Bgt_Un;
                    else if (ins.OpCode == OpCodes.Ble_Un_S) ins.OpCode = OpCodes.Ble_Un;
                    else if (ins.OpCode == OpCodes.Blt_Un_S) ins.OpCode = OpCodes.Blt_Un;
                    else if (ins.OpCode == OpCodes.Leave_S) ins.OpCode = OpCodes.Leave;
                }
                probed++;
            }
        }

        var outDir = Path.GetDirectoryName(outputPath) ?? ".";
        var tempFile = Path.Combine(outDir, Guid.NewGuid() + ".tmp");
        assembly.Write(tempFile);
        assembly.Dispose();
        File.Move(tempFile, outputPath, true);
        return probed;
    }

    public static void PatchRuntimeProbes(string assemblyPath, string outputPath)
    {
        // Managed environment probes that throw in the browser: WPF tracing
        // reads the Windows registry during DependencyObject..cctor, which
        // breaks AppWorkspace creation. Answer "disabled" instead.
        string? fileDir = Path.GetDirectoryName(assemblyPath);
        using var resolver = new FrameworkResolver(fileDir);
        var bytes = File.ReadAllBytes(assemblyPath);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var stream = new MemoryStream(bytes);
        using var assembly = AssemblyDefinition.ReadAssembly(stream, readerParams);
        var module = assembly.MainModule;

        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods.ToList())
            {
                if (!method.HasBody)
                    continue;
                if (type.FullName == "MS.Internal.AvTrace"
                    && method.Name == "ShouldCreateTraceSources"
                    && method.ReturnType.FullName == "System.Boolean")
                {
                    method.Body.ExceptionHandlers.Clear();
                    method.Body.Variables.Clear();
                    method.Body.Instructions.Clear();
                    var il = method.Body.GetILProcessor();
                    il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                    il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                }
                if (type.FullName == "MS.Internal.Text.TextInterface.Factory"
                    && IsWasmNeuteredFactoryMember(method))
                {
                    // The factory acquires native DWrite COM pointers and
                    // calls through them (calli); with no native backend the
                    // pointers are null and calli traps the WASM engine
                    // ("null function", uncatchable). Neuter to safe
                    // managed defaults (null/false) instead.
                    method.Body.ExceptionHandlers.Clear();
                    method.Body.Variables.Clear();
                    method.Body.Instructions.Clear();
                    var il = method.Body.GetILProcessor();
                    if (method.Name == ".ctor")
                    {
                        // Constructors must chain to object..ctor to verify.
                        var objectCtor = module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes));
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, objectCtor);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.FullName == "System.Void")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.FullName == "System.Boolean")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.IsPointer)
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                }
                if ((type.FullName == "System.Windows.Media.FontFamily"
                        && method.Name == "PreCreateDefaultFamilyCollection")
                    || (type.FullName == "MS.Internal.FontCache.FamilyCollection"
                        && method.Name == "FromWindowsFonts"))
                {
                    // No font files ship in the browser: enumerating system
                    // fonts throws. Answer "no collection" instead.
                    method.Body.ExceptionHandlers.Clear();
                    method.Body.Variables.Clear();
                    method.Body.Instructions.Clear();
                    var il = method.Body.GetILProcessor();
                    il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
                    il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                }
                if (type.FullName == "System.Windows.Input.Cursor"
                    && method.Name is "LoadCursorHelper" or "LoadFromFile" or "LoadFromStream" or "LegacyLoadFromStream")
                {
                    // Cursor files/handles do not exist in WASM: record the
                    // requested type (Arrow for file/stream loads) and skip
                    // all native loading.
                    var cursorTypeField = type.Fields.FirstOrDefault(f => f.Name == "_cursorType");
                    if (cursorTypeField != null)
                    {
                        method.Body.ExceptionHandlers.Clear();
                        method.Body.Variables.Clear();
                        method.Body.Instructions.Clear();
                        var il = method.Body.GetILProcessor();
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        if (method.Name == "LoadCursorHelper")
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
                        else
                            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_2);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Stfld, cursorTypeField);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                }
                if (type.FullName == "MS.Internal.Text.TextInterface.DWriteLoader"
                    && method.Name != ".cctor")
                {
                    // Native library loader for dwrite.dll: absent in the
                    // browser; skip so no null function pointer is produced
                    // for the calli sites.
                    method.Body.ExceptionHandlers.Clear();
                    method.Body.Variables.Clear();
                    method.Body.Instructions.Clear();
                    var il = method.Body.GetILProcessor();
                    if (method.Name == ".ctor")
                    {
                        var objectCtor = module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes));
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Call, objectCtor);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.FullName == "System.Void")
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else if (method.ReturnType.IsPointer)
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Conv_U);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                    else
                    {
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
                        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                    }
                }
                if (type.FullName == "ModuleInitializer"
                    && method.Name == "Initialize"
                    && method.IsStatic
                    && method.ReturnType.FullName == "System.Void"
                    && method.Parameters.Count == 0)
                {
                    // PresentationCore's module init eagerly loads the
                    // native DirectWrite stack (LoadDWrite), which cannot
                    // exist in the browser; the DPI check and exit hook it
                    // also performs are meaningless in WASM.
                    method.Body.ExceptionHandlers.Clear();
                    method.Body.Variables.Clear();
                    method.Body.Instructions.Clear();
                    var il = method.Body.GetILProcessor();
                    il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                }
            }
        }

        var outDir = Path.GetDirectoryName(outputPath) ?? ".";
        var tempFile = Path.Combine(outDir, Guid.NewGuid() + ".tmp");
        assembly.Write(tempFile);
        assembly.Dispose();
        File.Move(tempFile, outputPath, true);
    }

    // Verbatim-plus micro-pass (rev58): WPF AssemblyHelper.OnLoaded
    // guards on Assembly.ReflectionOnly, whose Mono-WASM builder
    // override throws NIE; escaping an AssemblyResolve handler that
    // kills the runtime (paint MONO cascade). Answer false: exact
    // .NET Core behavior (no reflection-only context exists), so the
    // GAC-record loop below stays intact. Stack-neutral rewrite:
    // the instance call pops the assembly and pushes a bool, so the
    // call becomes pop + ldc.i4.0. Returns neutralized site count.
    public static int NeutralizeModuleReflectionOnlyGuards(ModuleDefinition module)
    {
        int sites = 0;
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods.ToList())
            {
                if (!method.HasBody)
                    continue;
                var il = method.Body.GetILProcessor();
                foreach (var ins in method.Body.Instructions.ToList())
                {
                    if ((ins.OpCode != Mono.Cecil.Cil.OpCodes.Callvirt && ins.OpCode != Mono.Cecil.Cil.OpCodes.Call)
                        || ins.Operand is not MethodReference mr
                        || mr.Name != "get_ReflectionOnly"
                        || mr.DeclaringType.FullName != "System.Reflection.Assembly"
                        || mr.Parameters.Count != 0)
                        continue;
                    var pop = il.Create(Mono.Cecil.Cil.OpCodes.Pop);
                    il.Replace(ins, pop);
                    il.InsertAfter(pop, il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
                    sites++;
                }
            }
        }
        return sites;
    }

    public static int NeutralizeReflectionOnlyGuards(string assemblyPath, string outputPath)
    {
        string? fileDir = Path.GetDirectoryName(assemblyPath);
        using var resolver = new FrameworkResolver(fileDir);
        var bytes = File.ReadAllBytes(assemblyPath);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var stream = new MemoryStream(bytes);
        using var assembly = AssemblyDefinition.ReadAssembly(stream, readerParams);
        int sites = NeutralizeModuleReflectionOnlyGuards(assembly.MainModule);
        // Clean files stay byte-identical: the host-bundle pass (Fix 3)
        // runs over _framework DLLs it must not churn, and the served
        // path has no reason to round-trip an untouched file either.
        if (sites == 0)
            return 0;
        var outDir = Path.GetDirectoryName(outputPath) ?? ".";
        var tempFile = Path.Combine(outDir, Guid.NewGuid() + ".tmp");
        assembly.Write(tempFile);
        assembly.Dispose();
        File.Move(tempFile, outputPath, true);
        return sites;
    }

    // Rev59: Telerik's ContextLayoutManager posts its measure/arrange
    // pass via RadControl.InvokeLayoutCallback, gated on IsHandleCreated
    // (always false headless): the post is silently dropped and TPF
    // layout queues starve after boot, so edited content paints stale
    // layout indefinitely. Neutralize the handle check in that one
    // method (pop this + ldc.i4.1): the same-thread BeginInvoke then
    // runs the layout callback inline, exactly like the desktop
    // Dispatcher.BeginInvoke UI-thread fast path. The Disposing/
    // IsDisposed guards after it stay. Returns neutralized site count.
    // Rev62: document span glyphs paint through TextBlock.DrawGdi
    // (GDI DrawTransparentText: DIB section + TextOut + AlphaBlend,
    // none of which has a headless backend, so every run paints
    // nothing). The sibling DrawGdiPlus path paints the same runs
    // through managed Graphics.DrawString (fork-supported); the
    // signatures match, so retarget the single PaintElement call.
    // Regression: typed/deleted-region glyphs invisible (X/ZZ) while
    // selection/deletion reflow painted fine.
    public static int RetargetTextBlockGdiPaint(ModuleDefinition module)
    {
        int sites = 0;
        foreach (var type in module.GetTypes())
        {
            if (type.FullName != "Telerik.WinControls.RichTextEditor.UI.TextBlock")
                continue;
            var target = type.Methods.FirstOrDefault(m => m.Name == "DrawGdiPlus" && m.Parameters.Count == 3);
            if (target is null)
                continue;
            foreach (var method in type.Methods.ToList())
            {
                if (method.Name != "PaintElement" || !method.HasBody)
                    continue;
                var il = method.Body.GetILProcessor();
                foreach (var ins in method.Body.Instructions.ToList())
                {
                    if ((ins.OpCode != Mono.Cecil.Cil.OpCodes.Callvirt && ins.OpCode != Mono.Cecil.Cil.OpCodes.Call)
                        || ins.Operand is not MethodReference mr
                        || mr.Name != "DrawGdi"
                        || mr.DeclaringType.FullName != "Telerik.WinControls.RichTextEditor.UI.TextBlock"
                        || mr.Parameters.Count != 3)
                        continue;
                    il.Replace(ins, il.Create(Mono.Cecil.Cil.OpCodes.Callvirt, module.ImportReference(target)));
                    sites++;
                }
            }
        }
        return sites;
    }
    public static int NeutralizeModuleLayoutHandleGuard(ModuleDefinition module)
    {
        // The retarget needs the fork scope; without it, leave the
        // guard alone (ungating into a throwing BeginInvoke is worse
        // than the silent drop).
        var formsScope = module.AssemblyReferences.FirstOrDefault(r => r.Name == "System.Windows.Forms");
        if (formsScope is null)
            return 0;
        var queueType = new TypeReference("System.Windows.Forms", "WasmLayoutQueue", module, formsScope);
        var controlType = new TypeReference("System.Windows.Forms", "Control", module, formsScope);
        var post = new MethodReference("Post", module.ImportReference(typeof(System.IAsyncResult)), queueType)
        {
            HasThis = false,
        };
        post.Parameters.Add(new ParameterDefinition(controlType));
        post.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(System.Delegate))));
        post.Parameters.Add(new ParameterDefinition(new ArrayType(module.TypeSystem.Object)));
        int sites = 0;
        foreach (var type in module.GetTypes())
        {
            if (type.FullName != "Telerik.WinControls.RadControl")
                continue;
            foreach (var method in type.Methods.ToList())
            {
                if (method.Name != "InvokeLayoutCallback" || !method.HasBody)
                    continue;
                var il = method.Body.GetILProcessor();
                foreach (var ins in method.Body.Instructions.ToList())
                {
                    if ((ins.OpCode != Mono.Cecil.Cil.OpCodes.Callvirt && ins.OpCode != Mono.Cecil.Cil.OpCodes.Call)
                        || ins.Operand is not MethodReference mr
                        || mr.Name != "get_IsHandleCreated"
                        || mr.DeclaringType.FullName != "System.Windows.Forms.Control"
                        || mr.Parameters.Count != 0)
                        continue;
                    var pop = il.Create(Mono.Cecil.Cil.OpCodes.Pop);
                    il.Replace(ins, pop);
                    il.InsertAfter(pop, il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_1));
                    sites++;
                }
                foreach (var ins in method.Body.Instructions.ToList())
                {
                    // BeginInvoke needs a window handle (throws without
                    // one); the fork layout queue is drained by the host
                    // once per frame, the headless message-loop turn.
                    if (ins.Operand is MethodReference begin && begin.Name == "BeginInvoke"
                        && begin.DeclaringType.FullName == "System.Windows.Forms.Control"
                        && begin.Parameters.Count == 2)
                    {
                        ins.OpCode = Mono.Cecil.Cil.OpCodes.Call;
                        ins.Operand = post;
                    }
                }
            }
        }
        return sites;
    }

    public static int NeutralizeLayoutHandleGuard(string assemblyPath, string outputPath)
    {
        string? fileDir = Path.GetDirectoryName(assemblyPath);
        using var resolver = new FrameworkResolver(fileDir);
        var bytes = File.ReadAllBytes(assemblyPath);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var stream = new MemoryStream(bytes);
        using var assembly = AssemblyDefinition.ReadAssembly(stream, readerParams);
        int sites = NeutralizeModuleLayoutHandleGuard(assembly.MainModule);
        // No RadControl (every other module): stay byte-identical.
        if (sites == 0)
            return 0;
        var outDir = Path.GetDirectoryName(outputPath) ?? ".";
        var tempFile = Path.Combine(outDir, Guid.NewGuid() + ".tmp");
        assembly.Write(tempFile);
        assembly.Dispose();
        File.Move(tempFile, outputPath, true);
        return sites;
    }
}
