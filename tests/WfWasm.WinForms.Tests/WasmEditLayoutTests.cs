// Tests for WasmEditLayout: the headless EDIT-control geometry used by the
// EM_* message cases in PInvokeCore (PositionFromChar/CharFromPos/LineIndex/...).
// Spell-checkers (incl. Telerik's) query these to position squiggles queried
// against text painted via Graphics.FromHwnd; the renderer and these helpers
// must agree (single text run at x=5, vertically centered line).
extern alias Fork;
extern alias WasmDraw;

using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfColor = WasmDraw::System.Drawing.Color;
using WfFont = WasmDraw::System.Drawing.Font;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using WfPoint = WasmDraw::System.Drawing.Point;
using WfSize = WasmDraw::System.Drawing.Size;
using ForkForms = Fork::System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

public sealed class WasmEditLayoutTests : IDisposable
{
    private readonly ForkForms::TextBox _box;

    public WasmEditLayoutTests()
    {
        _box = new ForkForms::TextBox
        {
            Text = "Hello world",
            Font = new WfFont("Segoe UI", 9f),
            Width = 200,
            Height = 23,
        };
        // Force handle creation so ClientSize is meaningful.
        _ = _box.Handle;
    }

    public void Dispose() => _box.Dispose();

    [Fact]
    public void PositionFromChar_FirstChar_SitsAtLeftInset()
    {
        var pt = ForkForms::WasmEditLayout.PositionFromChar(_box, 0);
        Assert.True(pt.X >= 4 && pt.X <= 6, $"x={pt.X} should sit at the 5px run inset");
        Assert.True(pt.Y >= 0 && pt.Y < _box.ClientSize.Height, $"y={pt.Y} inside client height");
    }

    [Fact]
    public void PositionFromChar_AdvancesMonotonically_WithCharacterIndex()
    {
        int prev = ForkForms::WasmEditLayout.PositionFromChar(_box, 0).X;
        for (int i = 1; i <= _box.Text.Length; i++)
        {
            int cur = ForkForms::WasmEditLayout.PositionFromChar(_box, i).X;
            Assert.True(cur >= prev, $"x must not move left at index {i} ({prev} -> {cur})");
            prev = cur;
        }
        Assert.True(prev > 5, "end-of-text x must advance past the inset");
    }

    [Fact]
    public void CharFromPos_RoundTrips_PositionFromChar()
    {
        for (int i = 0; i < _box.Text.Length; i++)
        {
            var pt = ForkForms::WasmEditLayout.PositionFromChar(_box, i);
            int back = ForkForms::WasmEditLayout.CharFromPos(_box, pt.X + 1, pt.Y + 1);
            Assert.True(Math.Abs(back - i) <= 1, $"index {i} round-tripped to {back}");
        }
    }

    [Fact]
    public void LineHelpers_SingleLineText_Agree()
    {
        Assert.Equal(1, ForkForms::WasmEditLayout.LineCount(_box));
        Assert.Equal(0, ForkForms::WasmEditLayout.LineFromChar(_box, 0));
        Assert.Equal(0, ForkForms::WasmEditLayout.LineFromChar(_box, _box.Text.Length));
        Assert.Equal(0, ForkForms::WasmEditLayout.LineIndex(_box, 0));
        Assert.Equal(-1, ForkForms::WasmEditLayout.LineIndex(_box, 1));
    }

    [Fact]
    public void WasmNotifyPaint_Delivers_WmPaint_To_Subclass()
    {
        var spy = new PaintSpy();
        spy.AssignHandle(_box.Handle);
        try
        {
            ForkForms::NativeWindow.WasmNotifyPaint(_box.Handle);
            Assert.Contains(15, spy.Seen);
        }
        finally
        {
            spy.ReleaseHandle();
        }
    }

    [Fact]
    public void WasmNotifyPaint_NoWindowAttached_Is_NoOp()
    {
        // Must not throw for zero or unknown handles (plain controls
        // without a subclass take this path every dirty frame).
        ForkForms::NativeWindow.WasmNotifyPaint(IntPtr.Zero);
        ForkForms::NativeWindow.WasmNotifyPaint(new IntPtr(0x1234_5678));
    }

    private sealed class PaintSpy : ForkForms::NativeWindow
    {
        public readonly List<int> Seen = new();

        protected override void WndProc(ref ForkForms::Message m)
        {
            Seen.Add(m.Msg);
            base.WndProc(ref m);
        }
    }

    [Fact]
    public void OverlayBridge_Identical_Recommit_Skips_Invalidate()
    {
        // The repaint-loop guard: a pixel-identical re-commit must not
        // invalidate, or every synthetic WM_PAINT re-marks its control
        // dirty and the pump never idles (spellcheck saturated the
        // browser main thread at ~1fps with no crash and no error).
        static WfBitmap Solid(int w, int h, uint argb)
        {
            var bmp = new WfBitmap(w, h);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.FromArgb((int)argb));
            return bmp;
        }

        // NOTE: committed bitmaps are bridge-owned (stored or disposed
        // by CommitOverlay); the test must not dispose them.
        var hwnd = _box.Handle;
        ForkForms::WasmRepaintGate.DrainDirty(out _);
        try
        {
            ForkForms::WasmOverlayBridge.CommitOverlay(hwnd, Solid(16, 8, 0xFFFF0000));
            ForkForms::WasmRepaintGate.DrainDirty(out var dirty1);
            Assert.True(dirty1.ContainsKey(_box), "first commit must invalidate");

            ForkForms::WasmOverlayBridge.CommitOverlay(hwnd, Solid(16, 8, 0xFFFF0000));
            ForkForms::WasmRepaintGate.DrainDirty(out var dirty2);
            Assert.False(dirty2.ContainsKey(_box), "identical re-commit must not invalidate");

            ForkForms::WasmOverlayBridge.CommitOverlay(hwnd, Solid(16, 8, 0xFF0000FF));
            ForkForms::WasmRepaintGate.DrainDirty(out var dirty3);
            Assert.True(dirty3.ContainsKey(_box), "changed re-commit must invalidate");
        }
        finally
        {
            ForkForms::WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void OverlayBridge_Commit_TryGet_RoundTrips()
    {
        var hwnd = _box.Handle;
        var bmp = new WfBitmap(50, 12); // bridge-owned after commit
        ForkForms::WasmOverlayBridge.CommitOverlay(hwnd, bmp);
        Assert.True(ForkForms::WasmOverlayBridge.TryGetOverlay(hwnd, new WfSize(50, 12), out var got));
        Assert.Same(bmp, got);
    }

    [Fact]
    public void OverlayBridge_SizeMismatch_Drops_On_Read()
    {
        var hwnd = _box.Handle;
        var stale = new WfBitmap(10, 10);
        ForkForms::WasmOverlayBridge.CommitOverlay(hwnd, stale);
        Assert.False(ForkForms::WasmOverlayBridge.TryGetOverlay(hwnd, new WfSize(50, 12), out _));
        // Dropped: a later matching read finds nothing left.
        Assert.False(ForkForms::WasmOverlayBridge.TryGetOverlay(hwnd, new WfSize(10, 10), out _));
    }

    [Fact]
    public void OverlayBridge_Latest_Commit_Wins()
    {
        static WfBitmap Solid(byte r, byte b)
        {
            var bmp = new WfBitmap(50, 12);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.FromArgb(255, r, 0, b));
            return bmp;
        }

        // Pixel-different commits (a pixel-identical re-commit keeps
        // the stored bitmap by design; see Identical_Recommit).
        var hwnd = _box.Handle;
        var stale = Solid(255, 0);
        var keep = Solid(0, 255);
        ForkForms::WasmOverlayBridge.CommitOverlay(hwnd, stale);
        ForkForms::WasmOverlayBridge.CommitOverlay(hwnd, keep);
        Assert.True(ForkForms::WasmOverlayBridge.TryGetOverlay(hwnd, new WfSize(50, 12), out var got));
        Assert.Same(keep, got);
    }
}
