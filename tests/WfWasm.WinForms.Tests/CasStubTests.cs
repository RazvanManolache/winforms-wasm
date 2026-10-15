extern alias Fork;
namespace WfWasm.WinForms.Tests;

// CAS stub contract: every demand/assert is a no-op grant, factories
// answer usable objects, enum values mirror netfx exactly (rewritten
// framework IL compares/passes them; derived framework types such as
// WindowsBase MediaPermission bind overrides by signature).
// Regression: TypeLoadException UIPermission <- BookShelf.MainForm create.
public sealed class CasStubTests
{
    [Fact]
    public void Demands_Are_NoOp_Grants()
    {
        var ex = Record.Exception(() =>
        {
            Fork::System.Security.CodeAccessPermission p =
                new Fork::System.Security.Permissions.UIPermission(
                    Fork::System.Security.Permissions.UIPermissionWindow.AllWindows);
            p.Demand();
            p.Assert();
            p.Deny();
            p.PermitOnly();
            Fork::System.Security.CodeAccessPermission.RevertAssert();
            Fork::System.Security.CodeAccessPermission.RevertAll();
            Assert.Same(p, p.Copy());
            Assert.True(p.IsSubsetOf(null));
            var set = new Fork::System.Security.PermissionSet(
                Fork::System.Security.Permissions.PermissionState.Unrestricted);
            set.Demand();
            set.Assert();
            Fork::System.Security.PermissionSet.RevertAssert();
            Assert.Same(p, set.AddPermission(p));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void SecurityManager_Grants_Everything()
    {
        Assert.True(Fork::System.Security.SecurityManager.IsGranted(null));
        Assert.True(Fork::System.Security.SecurityManager.IsGranted(
            new Fork::System.Security.Permissions.FileIOPermission(
                Fork::System.Security.Permissions.PermissionState.None)));
    }

    [Fact]
    public void Permissions_Construct_And_Answer()
    {
        var file = new Fork::System.Security.Permissions.FileIOPermission(
            Fork::System.Security.Permissions.FileIOPermissionAccess.Read, "C:\\x");
        file.AddPathList(Fork::System.Security.Permissions.FileIOPermissionAccess.Write, "C:\\y");
        file.AllFiles = Fork::System.Security.Permissions.FileIOPermissionAccess.AllAccess;
        Assert.Empty(file.GetPathList(Fork::System.Security.Permissions.FileIOPermissionAccess.Read));
        Assert.True(((Fork::System.Security.Permissions.IUnrestrictedPermission)file).IsUnrestricted());

        var reg = new Fork::System.Security.Permissions.RegistryPermission(
            Fork::System.Security.Permissions.RegistryPermissionAccess.Read,
            Fork::System.Security.AccessControl.AccessControlActions.View, "HKCU\\x");
        reg.AddPathList(Fork::System.Security.Permissions.RegistryPermissionAccess.Read, "HKCU\\y");
        Assert.Equal(string.Empty, reg.GetPathList(
            Fork::System.Security.Permissions.RegistryPermissionAccess.Read));

        var refl = new Fork::System.Security.Permissions.ReflectionPermission(
            Fork::System.Security.Permissions.ReflectionPermissionFlag.MemberAccess);
        Assert.Equal(
            Fork::System.Security.Permissions.ReflectionPermissionFlag.MemberAccess, refl.Flags);
        var sec = new Fork::System.Security.Permissions.SecurityPermission(
            Fork::System.Security.Permissions.SecurityPermissionFlag.UnmanagedCode);
        Assert.Equal(
            Fork::System.Security.Permissions.SecurityPermissionFlag.UnmanagedCode, sec.Flags);
        var env = new Fork::System.Security.Permissions.EnvironmentPermission(
            Fork::System.Security.Permissions.PermissionState.None);
        env.SetPathList(Fork::System.Security.Permissions.EnvironmentPermissionAccess.Read, "PATH");
        var principal = new Fork::System.Security.Permissions.PrincipalPermission("u", "r", true);
        principal.Demand();
    }

    [Fact]
    public void Attributes_Construct_And_Expose_Unrestricted()
    {
        Fork::System.Security.Permissions.SecurityAttribute attr =
            new Fork::System.Security.Permissions.UIPermissionAttribute(
                Fork::System.Security.Permissions.SecurityAction.Demand)
            {
                Window = Fork::System.Security.Permissions.UIPermissionWindow.AllWindows,
                Clipboard = Fork::System.Security.Permissions.UIPermissionClipboard.OwnClipboard,
            };
        Assert.Equal(Fork::System.Security.Permissions.SecurityAction.Demand, attr.Action);
        attr.Unrestricted = true;
        Assert.True(attr.Unrestricted);
        Assert.Null(attr.CreatePermission());
        var host = new Fork::System.Security.Permissions.HostProtectionAttribute(
            Fork::System.Security.Permissions.SecurityAction.LinkDemand)
        {
            Resources = Fork::System.Security.Permissions.HostProtectionResource.UI,
            UI = true,
        };
        Assert.True(host.UI);
    }

    [Fact]
    public void Enum_Values_Match_Netfx()
    {
        Assert.Equal(3, (int)Fork::System.Security.Permissions.UIPermissionWindow.AllWindows);
        Assert.Equal(2, (int)Fork::System.Security.Permissions.SecurityAction.Demand);
        Assert.Equal(15, (int)Fork::System.Security.Permissions.FileIOPermissionAccess.AllAccess);
        Assert.Equal(0x3FFF, (int)Fork::System.Security.Permissions.SecurityPermissionFlag.AllFlags);
        Assert.Equal(7, (int)Fork::System.Security.Permissions.RegistryPermissionAccess.AllAccess);
        Assert.Equal(0xF003F, (int)Fork::System.Security.AccessControl.RegistryRights.FullControl);
        Assert.Equal(1, (int)Fork::System.Security.AccessControl.AccessControlActions.View);
        Assert.Equal(0x1FF, (int)Fork::System.Security.Permissions.HostProtectionResource.All);
    }
}
