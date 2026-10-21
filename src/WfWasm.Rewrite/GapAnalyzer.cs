using Mono.Cecil;

namespace WfWasm.Rewrite;

/// <summary>One app member reference with no counterpart in the target assemblies.</summary>
public sealed record Gap(string Scope, string DeclaringType, string Member, string Kind, string Detail = "");

/// <summary>
/// Measures the rewrite gap: for every type/member reference the app makes
/// into a remapped framework assembly, checks the replacement assembly
/// actually exposes it. The missing list is the shim work queue, per app.
/// </summary>
public static class GapAnalyzer
{
    /// <param name="assemblyMap">
    /// Framework assembly name → replacement assembly file to check against.
    /// </param>
    public static List<Gap> Analyze(string appAssemblyPath, IReadOnlyDictionary<string, string> assemblyMap)
        => Analyze(appAssemblyPath, assemblyMap, new Dictionary<string, (string Assembly, string Type)>());

    /// <param name="typeMap">
    /// Source type full name → (replacement assembly name, replacement type
    /// full name), same shape as the rewriter's map. Mapped references are
    /// checked against the replacement identity, so the queue reads as work
    /// items on our surface ("WfWasm.Shim.Form lacks X") instead of raw
    /// framework names.
    /// </param>
    public static List<Gap> Analyze(
        string appAssemblyPath,
        IReadOnlyDictionary<string, string> assemblyMap,
        IReadOnlyDictionary<string, (string Assembly, string Type)> typeMap)
    {
        using var app = AssemblyDefinition.ReadAssembly(appAssemblyPath);
        var targets = new Dictionary<string, Dictionary<string, TypeDefinition>>(StringComparer.Ordinal);
        foreach ((string from, string file) in assemblyMap)
        {
            var resolver = new FrameworkResolver(Path.GetDirectoryName(Path.GetFullPath(file)));
            var parameters = new ReaderParameters { AssemblyResolver = resolver };
            var target = AssemblyDefinition.ReadAssembly(file, parameters);
            var index = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
            foreach (TypeDefinition type in target.MainModule.Types)
                IndexType(type, index);
            // Facade assemblies (e.g. System.Drawing.dll) hold no types:
            // follow their forwarders into the real implementation.
            foreach (ExportedType exported in target.MainModule.ExportedTypes)
            {
                try
                {
                    TypeDefinition? resolved = exported.Resolve();
                    if (resolved is not null)
                        IndexType(resolved, index);
                }
                catch (AssemblyResolutionException)
                {
                }
            }

            targets[from] = index;
            targets[target.Name.Name] = index;
        }

        string RemapScope(string scope, string full, out string mappedFull)
        {
            mappedFull = full;
            if (!typeMap.TryGetValue(full, out (string Assembly, string Type) mapped))
                return scope;
            mappedFull = mapped.Type;
            return mapped.Assembly;
        }

        // Replacement assembly name per mapped framework name (for signature
        // identities below), plus the set of mapped target identities so
        // our own definitions render exactly like mapped references.
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string from, string file) in assemblyMap)
        {
            using var probe = AssemblyDefinition.ReadAssembly(file);
            replacements[from] = probe.Name.Name;
        }
        var mappedTargets = new HashSet<(string Assembly, string Type)>(typeMap.Values);

        // Post-rewrite identity of a referenced type: mapped names resolve to
        // the replacement identity, everything else keeps scope + full name.
        // Both app references and our definitions normalize through this, so
        // a name-and-arity match with forked parameter types (e.g. our own
        // ThreadExceptionEventHandler vs the framework one) reports as a
        // signature gap instead of silently passing the queue.
        // Generic instantiation along the member walk (filled by Mismatch):
        // Collection<Control> members mention T, but calls pass Control.
        var subst = new Dictionary<string, string>(StringComparer.Ordinal);

        string Identity(TypeReference type)
        {
            if (type is GenericParameter genericParam)
            {
                if (subst.TryGetValue(genericParam.FullName, out string? arg))
                    return arg;
                return "param:" + genericParam.FullName;
            }
            while (type is TypeSpecification spec)
            {
                if (type is GenericInstanceType generic)
                {
                    string args = string.Join(",", generic.GenericArguments.Select(Identity));
                    return Identity(generic.ElementType) + "<" + args + ">";
                }
                if (type is ArrayType array)
                    return Identity(array.ElementType) + "[]";
                if (type is ByReferenceType byRef)
                    return Identity(byRef.ElementType) + "&";
                if (type is PointerType pointer)
                    return Identity(pointer.ElementType) + "*";
                if (type is RequiredModifierType mod)
                    return Identity(mod.ElementType);
                if (type is FunctionPointerType fnptr)
                    return "fnptr:" + Identity(fnptr.ReturnType) + "(" + string.Join(",", fnptr.Parameters.Select(p => Identity(p.ParameterType))) + ")";
                if (spec.ElementType is null)
                    return "<exotic:" + spec.GetType().Name + ">";
                type = spec.ElementType;
            }
            string full = type.FullName.Replace('/', '+');
            if (typeMap.TryGetValue(full, out (string Assembly, string Type) mapped))
                return mapped.Assembly + ":" + mapped.Type;
            // Our own definitions must render exactly like the mapped
            // app-side identity above: resolve through the mapped targets.
            TypeReference root = type;
            while (root.DeclaringType is not null)
                root = root.DeclaringType;
            string owner = (root.Scope as AssemblyNameReference)?.Name
                ?? root.Module?.Assembly.Name.Name ?? "";
            if (mappedTargets.Contains((owner, full)))
                return owner + ":" + full;
            // Unmapped types pass through the rewriter untouched, so they
            // keep framework identity: compare by full name only. Facade
            // scopes (mscorlib vs System.Private.CoreLib, the many
            // System.ComponentModel.* facades) all forward to one runtime
            // type, so scope-sensitive comparison would fork them.
            // "ours:" marks helper types with no source counterpart, which
            // can never satisfy a framework reference.
            if (replacements.Values.Contains(owner))
                return "ours:" + full;
            return full;
        }

        var gaps = new List<Gap>();
        foreach (TypeReference typeRef in app.MainModule.GetTypeReferences())
        {
            if (typeRef.Scope is not AssemblyNameReference scope || !targets.ContainsKey(scope.Name))
                continue;
            string full = typeRef.FullName.Replace('/', '+');
            string checkScope = RemapScope(scope.Name, full, out string checkFull);
            if (!targets[checkScope].ContainsKey(checkFull))
                gaps.Add(new Gap(checkScope, checkFull, "", "type"));
        }

        foreach (MemberReference memberRef in app.MainModule.GetMemberReferences())
        {
            string? scopeName = ScopeOf(memberRef);
            if (scopeName is null || !targets.ContainsKey(scopeName))
                continue;
            string declaring = memberRef.DeclaringType.FullName.Replace('/', '+');
            string checkScope = RemapScope(scopeName, declaring, out string checkDeclaring);
            var index = targets[checkScope];
            if (!index.TryGetValue(checkDeclaring, out TypeDefinition? target))
            {
                gaps.Add(new Gap(checkScope, checkDeclaring, memberRef.Name, KindOf(memberRef)));
                continue;
            }

            (string? Kind, string Detail) outcome = Mismatch(target, memberRef, Identity, subst);
            if (outcome.Kind is not null)
                gaps.Add(new Gap(checkScope, checkDeclaring, memberRef.Name, outcome.Kind, outcome.Detail));
        }

        return gaps
            .Distinct()
            .OrderBy(g => g.Scope)
            .ThenBy(g => g.DeclaringType)
            .ThenBy(g => g.Member)
            .ToList();
    }

    /// <summary>
    /// Drafts a type map automatically: every app type reference in a mapped
    /// framework assembly whose simple name exists in the replacement
    /// assembly maps to it (nested names included). The rewriter's hand map
    /// stays authoritative; this is the queue-measurer's starting point.
    /// </summary>
    public static Dictionary<string, (string Assembly, string Type)> AutoMap(
        string appAssemblyPath,
        IReadOnlyDictionary<string, string> assemblyMap)
    {
        using var app = AssemblyDefinition.ReadAssembly(appAssemblyPath);
        var names = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach ((string from, string file) in assemblyMap)
        {
            using var target = AssemblyDefinition.ReadAssembly(file);
            var byName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TypeDefinition type in target.MainModule.GetTypes())
                byName[type.FullName.Replace('/', '+')] = target.Name.Name;
            names[from] = byName;
            names[target.Name.Name] = byName;
        }

        var map = new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal);
        foreach (TypeReference typeRef in app.MainModule.GetTypeReferences())
        {
            if (typeRef.Scope is not AssemblyNameReference scope
                || !names.TryGetValue(scope.Name, out var byName))
                continue;
            string full = typeRef.FullName.Replace('/', '+');
            if (map.ContainsKey(full))
                continue;
            if (byName.TryGetValue(full, out string? exactAssembly))
            {
                map[full] = (exactAssembly, full);
                continue;
            }
            // Same-name rule: System.Windows.Forms.Form → WfWasm.Shim.Form,
            // System.Drawing.Size → System.Drawing.Size (other assembly);
            // nested paths compare without their namespaces.
            string simple = SimpleName(full);
            foreach (var candidate in byName.Keys)
            {
                if (SimpleName(candidate) == simple)
                {
                    map[full] = (byName[candidate], candidate);
                    break;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Last resort for an unresolvable system base (System.Object and kin):
    /// the runtime always provides the object protocol, so those names are
    /// present; anything else is genuinely missing from our surface.
    /// </summary>
    private static bool IsFrameworkProtocol(MemberReference memberRef, TypeReference baseRef)
    {
        string? scope = (baseRef.Scope as AssemblyNameReference)?.Name;
        if (scope is null || !scope.StartsWith("System.", StringComparison.Ordinal))
            return false;
        return memberRef.Name is "ToString" or "GetHashCode" or "Equals"
            or "GetType" or "Finalize" or "MemberwiseClone";
    }

    private static string SimpleName(string full)
    {
        int plus = full.IndexOf('+');
        int nsEnd = plus < 0 ? full.LastIndexOf('.') : full.LastIndexOf('.', plus);
        return nsEnd < 0 ? full : full[(nsEnd + 1)..];
    }

    private static void IndexType(TypeDefinition type, Dictionary<string, TypeDefinition> index)
    {
        index[type.FullName.Replace('/', '+')] = type;
        foreach (TypeDefinition nested in type.NestedTypes)
            IndexType(nested, index);
    }

    private static string? ScopeOf(MemberReference member)
    {
        TypeReference declaring = member.DeclaringType;
        while (declaring is TypeSpecification spec)
            declaring = spec.ElementType;
        return (declaring.Scope as AssemblyNameReference)?.Name;
    }

    private static string KindOf(MemberReference member) => member switch
    {
        MethodReference => "method",
        FieldReference => "field",
        _ => "member",
    };

    /// <summary>
    /// Checks a member reference against a target type, walking the base
    /// chain like the runtime does. Returns null when a compatible slot
    /// exists, the member kind when nothing matches by name ("method",
    /// "field"), or "signature" when the name exists with a matching arity
    /// but every overload's parameter or return types differ after remap
    /// (a forked-identity hazard the runtime rejects with MissingMethod).
    /// </summary>
    private static (string? Kind, string Detail) Mismatch(
        TypeDefinition target,
        MemberReference memberRef,
        Func<TypeReference, string> identity,
        Dictionary<string, string> subst)
    {
        subst.Clear();
        string detail = "";
        bool sawName = false;
        for (TypeDefinition? current = target; current is not null;)
        {
            if (memberRef is FieldReference fieldRef)
            {
                foreach (FieldDefinition field in current.Fields.Where(f => f.Name == fieldRef.Name))
                {
                    sawName = true;
                    if (identity(field.FieldType) == identity(fieldRef.FieldType))
                        return (null, "");
                    detail = $"ref:{identity(fieldRef.FieldType)} def:{identity(field.FieldType)}";
                }
            }
            else if (memberRef is MethodReference methodRef)
            {
                if (detail.Length == 0)
                    detail = "ref:" + Sig(methodRef, identity);
                foreach (MethodDefinition method in current.Methods.Where(m =>
                    m.Name == methodRef.Name && m.Parameters.Count == methodRef.Parameters.Count))
                {
                    sawName = true;
                    bool same = method.Parameters
                        .Select((p, i) => identity(p.ParameterType) == identity(methodRef.Parameters[i].ParameterType))
                        .All(b => b)
                        && (identity(method.ReturnType) == identity(methodRef.ReturnType)
                            || ReturnsCompatible(method.ReturnType, methodRef.ReturnType, identity));
                    if (same)
                        return (null, "");
                    detail += " def:" + Sig(method, identity);
                }
            }
            else
            {
                return (null, "");
            }

            TypeReference? baseRef = current.BaseType;
            current = null;
            if (baseRef is null)
                return sawName ? ("signature", detail) : (KindOf(memberRef), "");
            if (baseRef is GenericInstanceType instance)
                BindArguments(instance, identity, subst);
            baseRef = Unwrap(baseRef);
            try
            {
                current = baseRef.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return IsFrameworkProtocol(memberRef, baseRef) ? (null, "") : (KindOf(memberRef), "");
            }
            catch (FileNotFoundException)
            {
                return IsFrameworkProtocol(memberRef, baseRef) ? (null, "") : (KindOf(memberRef), "");
            }
        }

        return sawName ? ("signature", detail) : (KindOf(memberRef), "");
    }

    private static TypeReference Unwrap(TypeReference type)
    {
        while (type is TypeSpecification spec)
            type = spec.ElementType;
        return type;
    }

    private static void BindArguments(
        GenericInstanceType instance,
        Func<TypeReference, string> identity,
        Dictionary<string, string> subst)
    {
        TypeDefinition? genericDef = null;
        try
        {
            genericDef = instance.ElementType.Resolve();
        }
        catch (AssemblyResolutionException)
        {
        }
        catch (FileNotFoundException)
        {
        }
        if (genericDef is null)
            return;
        var parameters = genericDef.GenericParameters;
        for (int i = 0; i < parameters.Count && i < instance.GenericArguments.Count; i++)
            subst[parameters[i].FullName] = identity(instance.GenericArguments[i]);
    }

    /// <summary>
    /// Covariant returns bind: the runtime resolves by name + parameters and
    /// verifies assignability, so a definition returning IEnumerator&lt;T&gt;
    /// satisfies a call expecting IEnumerator. Anything the definition's own
    /// base/interface chain does not cover stays a gap.
    /// </summary>
    private static bool ReturnsCompatible(
        TypeReference defined,
        TypeReference requested,
        Func<TypeReference, string> identity)
    {
        string want = identity(requested);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var agenda = new Queue<TypeReference>();
        agenda.Enqueue(defined);
        for (int steps = 0; steps < 64 && agenda.Count != 0; steps++)
        {
            TypeReference next = Unwrap(agenda.Dequeue());
            if (!seen.Add(next.FullName))
                continue;
            if (identity(next) == want)
                return true;
            TypeDefinition? resolved = null;
            try
            {
                resolved = next.Resolve();
            }
            catch (AssemblyResolutionException)
            {
            }
            catch (FileNotFoundException)
            {
            }
            if (resolved is null)
                continue;
            if (resolved.BaseType is not null)
                agenda.Enqueue(resolved.BaseType);
            foreach (InterfaceImplementation face in resolved.Interfaces)
                agenda.Enqueue(face.InterfaceType);
        }
        return false;
    }

    private static string Sig(MethodReference method, Func<TypeReference, string> identity)
    {
        string ps = string.Join(",", method.Parameters.Select(p => identity(p.ParameterType)));
        return $"({ps})->{identity(method.ReturnType)}";
    }
}
