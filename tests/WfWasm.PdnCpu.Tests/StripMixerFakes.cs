// Test fakes for CpuPaint.DrawCompositeImage's strip-mixer walk
// (FindStripMixer). Matching is by type-name substring ("CpuDeviceBitmap"
// for the per-row thumbnail upload, "CheckerboardEffect" for the
// checkerboard input), so these stand-ins carry those names; the overlay
// additionally carries the _width/_height/_handle fields the production
// CpuDeviceBitmap exposes.
namespace WfWasm.PdnCpu.Tests.Fakes;

public sealed class CpuDeviceBitmap
{
    public int _width;
    public int _height;
    public long _handle;
}

public sealed class CheckerboardEffect
{
}
