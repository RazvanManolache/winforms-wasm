using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using WfWasm.Platform.Server;

namespace WfWasm.Platform.Tests;

public sealed class IdentityTests
{
    [Fact] public void Account_Mode_Does_Not_Fall_Back_To_Anonymous_Users()
    {
        var sessions = new PlatformSessions(new() { DataDirectory = "unused", RequireAuthenticatedUsers = true }, new EphemeralDataProtectionProvider());
        var context = new DefaultHttpContext(); context.Request.Headers["X-User"] = "alice";
        Assert.Throws<UnauthorizedAccessException>(() => sessions.Create(context, "demo"));
    }
    [Fact] public void Session_Follows_Authenticated_Subject_And_Rejects_Identity_Changes()
    {
        var sessions = new PlatformSessions(new() { DataDirectory = "unused", RequireAuthenticatedUsers = true }, new EphemeralDataProtectionProvider());
        var context = new DefaultHttpContext();
        context.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice")], "test"));
        string token = JsonSerializer.SerializeToElement(sessions.Create(context, "demo")).GetProperty("token").GetString()!;
        context.Request.Headers["X-Wf-Session"] = token; context.Request.Headers["X-User"] = "mallory";
        Assert.Equal(new PlatformScope("auth:alice", "demo"), sessions.Resolve(context));
        context.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "bob")], "test"));
        Assert.Throws<UnauthorizedAccessException>(() => sessions.Resolve(context));
    }
}
