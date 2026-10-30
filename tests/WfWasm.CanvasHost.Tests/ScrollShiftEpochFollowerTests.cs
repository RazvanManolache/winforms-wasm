using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// WF-P-073: why the epoch suppress never fires on scroll traffic.
// E2E (isolated instrumented build, :5213) proved the decision chain works
// up to the hash gate — tag placed, taken by the immediate follower full,
// whole + generation-stable + layers all true (frames 10/26/42, tagGen
// 2/4/6) — but hashEq is always false: the re-rendered follower raster
// differs systematically from the shifted surface (diffBytes
// 127344/130558/185178 of 2515760, ~5-7%, bottom-heavy rows, deterministic
// across runs).
public sealed class ScrollShiftEpochFollowerTests
{
    [Fact]
    public void E2E_FollowerRepaintDiffers_SuppressionCorrectlyDeclines()
    {
        // WF-P-073 failing-first (trx wf-p073-failing-first.trx) proved the
        // desired suppression does NOT fire for the measured E2E pair, so
        // this test locks the correct decline instead: differing pixels must
        // never suppress (the browser needs those bytes). The defect is
        // upstream — the follow-up repaint diverges ~5-7% from the shifted
        // surface — and is fixed separately (vendor-true blit offset or
        // WF-P-062(b) follow-up narrowing). Frames 10/26/42, tagGen 2/4/6,
        // diffBytes 127344/130558/185178 of 2515760.
        var tag = new WinFormsAppHost.ShiftEpochTag(
            Dx: 0, Dy: -240, ShiftHash: 0x8CC12B9F5E6DC401uL, WatchGeneration: 2);
        var facts = new WinFormsAppHost.PostShiftDrainFacts(
            DirtyIsWhole: true, LayoutMarksOnly: true,
            GenerationStable: true, NoInputMarks: true);
        Assert.True(WinFormsAppHost.ShouldAttemptPostShiftVerify(tag, facts));
        Assert.False(WinFormsAppHost.ShouldSuppressUpload(
            0x8CC12B9F5E6DC401uL, 0x8101C091A78B28C1uL));
    }

    [Fact]
    public void PostShiftSkip_EnabledUnlessExplicitlyDisabled()
    {
        // WF-P-FIX (2b): the follow-up skip is the default path; the
        // reference full repaint stays reachable via the opt-out switch.
        Assert.False(AppContext.TryGetSwitch(
            "WfWasm.DisablePostShiftSkip", out bool disabled) && disabled);
    }
}
