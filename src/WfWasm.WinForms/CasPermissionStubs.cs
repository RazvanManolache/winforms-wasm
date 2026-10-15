// Code Access Security (CAS) permission stubs: netfx assemblies reference
// System.Security.Permissions.* / CodeAccessPermission / PermissionSet,
// none of which exist in the browser runtime (.NET 9 has no CAS). Any
// surviving reference is fatal at load: RadLabel's UIPermission field
// broke every Telerik app with TypeLoadException before any form painted.
// The rewriter retargets those references here (same namespace+name, new
// scope). All demands are no-ops: the sandbox grants everything, so the
// stubs grant everything. Enum values and member signatures mirror netfx
// exactly so implicit overrides in derived framework types (WindowsBase
// MediaPermission/WebBrowserPermission) keep binding.
// Regression: TypeLoadException UIPermission <- BookShelf.MainForm create.
using System.Security.Permissions;

namespace System.Security
{

public interface IPermission
{
    IPermission Copy();
    void Demand();
}

public class CodeAccessPermission : IPermission
{
    public virtual void Demand() { }
    public virtual void Assert() { }
    public virtual void Deny() { }
    public virtual void PermitOnly() { }
    public static void RevertAssert() { }
    public static void RevertDeny() { }
    public static void RevertPermitOnly() { }
    public static void RevertAll() { }
    public virtual IPermission Copy() => this;
    public virtual IPermission? Intersect(IPermission? target) => this;
    public virtual bool IsSubsetOf(IPermission? target) => true;
    public virtual IPermission? Union(IPermission? target) => this;
    public virtual void FromXml(SecurityElement? e) { }
    public virtual SecurityElement? ToXml() => null;
}

public sealed class PermissionSet
{
    public PermissionSet() { }
    public PermissionSet(PermissionState state) { }
    public void Demand() { }
    public void Assert() { }
    public static void RevertAssert() { }
    public IPermission? AddPermission(IPermission? perm) => perm;
    public PermissionSet Copy() => this;
}

public static class SecurityManager
{
    public static bool IsGranted(IPermission? perm) => true;
}

}

namespace System.Security.Permissions
{

public enum PermissionState
{
    Unrestricted = 0,
    None = 1,
}

public enum SecurityAction
{
    Demand = 2,
    Assert = 3,
    Deny = 4,
    PermitOnly = 6,
    LinkDemand = 7,
    InheritanceDemand = 8,
    RequestMinimum = 9,
    RequestOptional = 10,
    RequestRefuse = 11,
}

[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
public abstract class SecurityAttribute : Attribute
{
    protected SecurityAttribute(SecurityAction action) { Action = action; }
    public SecurityAction Action { get; set; }
    public virtual bool Unrestricted { get; set; }
    public virtual IPermission? CreatePermission() => null;
}

public abstract class CodeAccessSecurityAttribute : SecurityAttribute
{
    protected CodeAccessSecurityAttribute(SecurityAction action) : base(action) { }
}

public interface IUnrestrictedPermission
{
    bool IsUnrestricted();
}

public enum UIPermissionWindow
{
    NoWindows = 0,
    SafeTopLevelWindows = 1,
    SafeSubWindows = 2,
    AllWindows = 3,
}

public enum UIPermissionClipboard
{
    NoClipboard = 0,
    OwnClipboard = 1,
    AllClipboard = 2,
}

public sealed class UIPermission : CodeAccessPermission, IUnrestrictedPermission
{
    public UIPermission(PermissionState state) { }
    public UIPermission(UIPermissionWindow window) { Window = window; }
    public UIPermission(UIPermissionWindow window, UIPermissionClipboard clipboard) { Window = window; Clipboard = clipboard; }
    public UIPermissionWindow Window { get; set; }
    public UIPermissionClipboard Clipboard { get; set; }
    public bool IsUnrestricted() => true;
}

public sealed class UIPermissionAttribute : CodeAccessSecurityAttribute
{
    public UIPermissionAttribute(SecurityAction action) : base(action) { }
    public UIPermissionWindow Window { get; set; }
    public UIPermissionClipboard Clipboard { get; set; }
}

[Flags]
public enum FileIOPermissionAccess
{
    NoAccess = 0,
    Read = 1,
    Write = 2,
    Append = 4,
    PathDiscovery = 8,
    AllAccess = 15,
}

public sealed class FileIOPermission : CodeAccessPermission, IUnrestrictedPermission
{
    public FileIOPermission(PermissionState state) { }
    public FileIOPermission(FileIOPermissionAccess access, string path) { }
    public FileIOPermission(FileIOPermissionAccess access, string[] pathList) { }
    public FileIOPermissionAccess AllFiles { get; set; }
    public FileIOPermissionAccess AllLocalIntranet { get; set; }
    public void AddPathList(FileIOPermissionAccess access, string path) { }
    public void AddPathList(FileIOPermissionAccess access, string[] pathList) { }
    public string[] GetPathList(FileIOPermissionAccess access) => [];
    public bool IsUnrestricted() => true;
}

public sealed class FileIOPermissionAttribute : CodeAccessSecurityAttribute
{
    public FileIOPermissionAttribute(SecurityAction action) : base(action) { }
    public string All { get; set; } = string.Empty;
    public string Append { get; set; } = string.Empty;
    public string ChangeAccessControl { get; set; } = string.Empty;
    public string Create { get; set; } = string.Empty;
    public string PathDiscovery { get; set; } = string.Empty;
    public string Read { get; set; } = string.Empty;
    public string ViewAccessControl { get; set; } = string.Empty;
    public string ViewAndModify { get; set; } = string.Empty;
    public string Write { get; set; } = string.Empty;
}

public sealed class ReflectionPermission : CodeAccessPermission, IUnrestrictedPermission
{
    public ReflectionPermission(PermissionState state) { }
    public ReflectionPermission(ReflectionPermissionFlag flag) { Flags = flag; }
    public ReflectionPermissionFlag Flags { get; set; }
    public bool IsUnrestricted() => true;
}

[Flags]
public enum ReflectionPermissionFlag
{
    NoFlags = 0,
    TypeInformation = 1,
    MemberAccess = 2,
    ReflectionEmit = 4,
    AllFlags = 7,
}

public sealed class ReflectionPermissionAttribute : CodeAccessSecurityAttribute
{
    public ReflectionPermissionAttribute(SecurityAction action) : base(action) { }
    public ReflectionPermissionFlag Flags { get; set; }
    public bool MemberAccess { get; set; }
    public bool ReflectionEmit { get; set; }
    public bool TypeInformation { get; set; }
}

[Flags]
public enum RegistryPermissionAccess
{
    NoAccess = 0,
    Read = 1,
    Write = 2,
    Create = 4,
    AllAccess = 7,
}

public sealed class RegistryPermission : CodeAccessPermission, IUnrestrictedPermission
{
    public RegistryPermission(PermissionState state) { }
    public RegistryPermission(RegistryPermissionAccess access, string pathList) { }
    public RegistryPermission(RegistryPermissionAccess access, AccessControl.AccessControlActions actions, string pathList) { }
    public void AddPathList(RegistryPermissionAccess access, string pathList) { }
    public void AddPathList(RegistryPermissionAccess access, AccessControl.AccessControlActions actions, string pathList) { }
    public string GetPathList(RegistryPermissionAccess access) => string.Empty;
    public bool IsUnrestricted() => true;
}

public sealed class RegistryPermissionAttribute : CodeAccessSecurityAttribute
{
    public RegistryPermissionAttribute(SecurityAction action) : base(action) { }
    public string All { get; set; } = string.Empty;
    public string ChangeAccessControl { get; set; } = string.Empty;
    public string Create { get; set; } = string.Empty;
    public string Read { get; set; } = string.Empty;
    public string ViewAccessControl { get; set; } = string.Empty;
    public string ViewAndModify { get; set; } = string.Empty;
    public string Write { get; set; } = string.Empty;
}

[Flags]
public enum SecurityPermissionFlag
{
    NoFlags = 0,
    Assertion = 1,
    UnmanagedCode = 2,
    SkipVerification = 4,
    Execution = 8,
    ControlThread = 0x10,
    ControlEvidence = 0x20,
    ControlPolicy = 0x40,
    SerializationFormatter = 0x80,
    ControlDomainPolicy = 0x100,
    ControlPrincipal = 0x200,
    ControlAppDomain = 0x400,
    RemotingConfiguration = 0x800,
    Infrastructure = 0x1000,
    BindingRedirects = 0x2000,
    AllFlags = 0x3FFF,
}

public sealed class SecurityPermission : CodeAccessPermission, IUnrestrictedPermission
{
    public SecurityPermission(PermissionState state) { }
    public SecurityPermission(SecurityPermissionFlag flag) { Flags = flag; }
    public SecurityPermissionFlag Flags { get; set; }
    public bool IsUnrestricted() => true;
}

public sealed class SecurityPermissionAttribute : CodeAccessSecurityAttribute
{
    public SecurityPermissionAttribute(SecurityAction action) : base(action) { }
    public SecurityPermissionFlag Flags { get; set; }
    public bool Assertion { get; set; }
    public bool BindingRedirects { get; set; }
    public bool ControlAppDomain { get; set; }
    public bool ControlDomainPolicy { get; set; }
    public bool ControlEvidence { get; set; }
    public bool ControlPolicy { get; set; }
    public bool ControlPrincipal { get; set; }
    public bool ControlThread { get; set; }
    public bool Execution { get; set; }
    public bool Infrastructure { get; set; }
    public bool RemotingConfiguration { get; set; }
    public bool SerializationFormatter { get; set; }
    public bool SkipVerification { get; set; }
    public bool UnmanagedCode { get; set; }
}

public sealed class EnvironmentPermissionAttribute : CodeAccessSecurityAttribute
{
    public EnvironmentPermissionAttribute(SecurityAction action) : base(action) { }
    public string All { get; set; } = string.Empty;
    public string Read { get; set; } = string.Empty;
    public string Write { get; set; } = string.Empty;
}

public sealed class EnvironmentPermission : CodeAccessPermission, IUnrestrictedPermission
{
    public EnvironmentPermission(PermissionState state) { }
    public string GetPathList(EnvironmentPermissionAccess access) => string.Empty;
    public void SetPathList(EnvironmentPermissionAccess access, string pathList) { }
    public bool IsUnrestricted() => true;
}

[Flags]
public enum EnvironmentPermissionAccess
{
    NoAccess = 0,
    Read = 1,
    Write = 2,
    AllAccess = 3,
}

[Flags]
public enum HostProtectionResource
{
    None = 0,
    Synchronization = 1,
    SharedState = 2,
    ExternalProcessMgmt = 4,
    SelfAffectingProcessMgmt = 8,
    ExternalThreading = 0x10,
    SelfAffectingThreading = 0x20,
    SecurityInfrastructure = 0x40,
    UI = 0x80,
    MayLeakOnAbort = 0x100,
    All = 0x1FF,
}

public sealed class HostProtectionAttribute : CodeAccessSecurityAttribute
{
    public HostProtectionAttribute(SecurityAction action) : base(action) { }
    public HostProtectionResource Resources { get; set; }
    public bool Synchronization { get; set; }
    public bool SharedState { get; set; }
    public bool ExternalProcessMgmt { get; set; }
    public bool SelfAffectingProcessMgmt { get; set; }
    public bool ExternalThreading { get; set; }
    public bool SelfAffectingThreading { get; set; }
    public bool SecurityInfrastructure { get; set; }
    public bool UI { get; set; }
    public bool MayLeakOnAbort { get; set; }
}

public sealed class PrincipalPermission : CodeAccessPermission, IUnrestrictedPermission
{
    public PrincipalPermission(PermissionState state) { }
    public PrincipalPermission(string name, string role) { }
    public PrincipalPermission(string name, string role, bool isAuthenticated) { }
    public bool IsUnrestricted() => true;
}

public sealed class PrincipalPermissionAttribute : CodeAccessSecurityAttribute
{
    public PrincipalPermissionAttribute(SecurityAction action) : base(action) { }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool Authenticated { get; set; }
}

public sealed class PermissionSetAttribute : CodeAccessSecurityAttribute
{
    public PermissionSetAttribute(SecurityAction action) : base(action) { }
    public string File { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string XML { get; set; } = string.Empty;
}

}

namespace System.Security.AccessControl
{

public enum AccessControlActions
{
    None = 0,
    View = 1,
    Change = 2,
}

[Flags]
public enum RegistryRights
{
    QueryValues = 1,
    SetValue = 2,
    CreateSubKey = 4,
    EnumerateSubKeys = 8,
    Notify = 0x10,
    CreateLink = 0x20,
    Delete = 0x10000,
    ReadPermissions = 0x20000,
    WriteKey = 0x20006,
    ReadKey = 0x20019,
    ChangePermissions = 0x40000,
    TakeOwnership = 0x80000,
    FullControl = 0xF003F,
}
}
