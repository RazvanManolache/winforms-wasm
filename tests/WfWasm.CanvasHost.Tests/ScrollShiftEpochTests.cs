using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// Post-shift verify epoch (WF-P-066): a carried shift tags the control with
// the shifted-surface hash; a later whole-control repaint with stable scroll
// generation attempts verification, and the hash gate suppresses the upload
// only on pixel identity. Any difference uploads as today.
public sealed class ScrollShiftEpochTests
{
    private static WinFormsAppHost.ShiftEpochTag Tag(long gen = 7) =>
        new(Dx: 0, Dy: -240, ShiftHash: 0x9E3779B97F4A7C15uL, WatchGeneration: gen);

    private static WinFormsAppHost.PostShiftDrainFacts Facts(
        bool whole = true, bool layout = true, bool stable = true, bool noInput = true) =>
        new(DirtyIsWhole: whole, LayoutMarksOnly: layout,
            GenerationStable: stable, NoInputMarks: noInput);

    [Fact]
    public void TagPlusWholeLayoutMarkStableGeneration_RepaintSetEmpty()
    {
        // (1) tag + whole-control layout mark, generation stable, no input
        // marks => attempt verification (skip the redundant repaint/upload
        // when the hash gate then confirms identity).
        Assert.True(WinFormsAppHost.ShouldAttemptPostShiftVerify(Tag(), Facts()));
    }

    [Fact]
    public void ValueChange_FullRepaintPreserved()
    {
        // (2) same but the watcher generation moved (scroll/zoom/doc/size
        // change rewrites the baseline) => today's full path, no attempt.
        Assert.False(WinFormsAppHost.ShouldAttemptPostShiftVerify(Tag(gen: 7), Facts(stable: false)));
        Assert.False(WinFormsAppHost.ShouldAttemptPostShiftVerify(null, Facts()));
    }

    [Fact]
    public void InputMark_FullRepaintPreserved()
    {
        // (3) same but an input/pointer mark (or a non-layout mark) dirtied
        // the control since the tag => today's full path, no attempt.
        Assert.False(WinFormsAppHost.ShouldAttemptPostShiftVerify(Tag(), Facts(noInput: false)));
        Assert.False(WinFormsAppHost.ShouldAttemptPostShiftVerify(Tag(), Facts(layout: false)));
        Assert.False(WinFormsAppHost.ShouldAttemptPostShiftVerify(Tag(), Facts(whole: false)));
    }

    [Fact]
    public void HashGate_EqualSuppressesDifferUploads()
    {
        // (4) safety gate: identical follow-up raster suppresses the upload;
        // any pixel difference uploads as today.
        Assert.True(WinFormsAppHost.ShouldSuppressUpload(
            0x9E3779B97F4A7C15uL, 0x9E3779B97F4A7C15uL));
        Assert.False(WinFormsAppHost.ShouldSuppressUpload(
            0x9E3779B97F4A7C15uL, 0x1B873593E39AEEF5uL));
    }
}
