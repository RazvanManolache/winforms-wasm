using System.Diagnostics;
using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

/// <summary>
/// WF-R-NRE-0730 lane A: the PDN first-chance formatter must never throw,
/// whatever the exception or frame shape. A throwing formatter files its own
/// first-chance noise (and can mask the real report); these pin the guards.
/// </summary>
public sealed class FirstChanceFormatTests
{
    private sealed class NullMessageException : Exception
    {
        // Hostile override: the base declares non-nullable, but exotic
        // exceptions can still yield null here at runtime.
        public override string Message => null!;
    }

    [Fact]
    public void FormatFirstChance_Null_Message_Does_Not_Throw()
    {
        var (lines, topFrame) = WinFormsAppHost.FormatFirstChance(new NullMessageException(), frames: null);
        Assert.NotNull(lines);
        Assert.NotEmpty(lines);
        string joined = string.Join("\n", lines);
        Assert.Contains("NullMessageException", joined);
        Assert.Null(topFrame);
    }

    [Fact]
    public void FormatFirstChance_Null_Frames_Does_Not_Throw()
    {
        var ex = new InvalidOperationException("frameless-marker");
        var (lines, topFrame) = WinFormsAppHost.FormatFirstChance(ex, frames: null);
        Assert.Null(topFrame);
        Assert.Contains(lines, l => l.Contains("InvalidOperationException") && l.Contains("frameless-marker"));
    }

    [Fact]
    public void FormatFirstChance_Empty_Frames_TopFrame_Null()
    {
        var ex = new InvalidOperationException("empty-frames-marker");
        var (lines, topFrame) = WinFormsAppHost.FormatFirstChance(ex, frames: []);
        Assert.Null(topFrame);
        Assert.Contains(lines, l => l.Contains("empty-frames-marker"));
    }

    [Fact]
    public void FormatFirstChance_Real_Stack_Includes_Caller_Chain()
    {
        Exception ex;
        try
        {
            ThrowForChain();
            throw new InvalidOperationException("unreachable");
        }
        catch (Exception caught)
        {
            ex = caught;
        }

        StackFrame[]? frames = new StackTrace(ex, fNeedFileInfo: false).GetFrames();
        var (lines, topFrame) = WinFormsAppHost.FormatFirstChance(ex, frames);
        Assert.NotNull(topFrame);
        Assert.NotEmpty(topFrame);
        string joined = string.Join("\n", lines);
        Assert.Contains(nameof(ThrowForChain), joined);
    }

    private static void ThrowForChain() => throw new InvalidOperationException("chained-marker");
}
