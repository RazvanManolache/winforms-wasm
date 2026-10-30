using System;
using System.IO;
using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// TEMP-DIAG pin (rev266 VTable hunt): the one-shot loader matrix must run
// exactly once per process and never throw (it runs on the thumbnail queue
// path in vivo; a throw there would mask the real failure).
public sealed class ProbeAdapterLoadsTests
{
    [Fact]
    public void Runs_Once_Without_Throwing()
    {
        var sw = new StringWriter();
        var old = Console.Out;
        Console.SetOut(sw);
        try
        {
            CpuPaint.ProbeAdapterLoads();
            CpuPaint.ProbeAdapterLoads();
        }
        finally
        {
            Console.SetOut(old);
        }
        string[] lines = sw.ToString().Split(
            new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        int probes = 0;
        foreach (string line in lines)
            if (line.Contains("[PROBE-T]")) probes++;
        // Exactly one pass over the thirteen candidate types.
        Assert.Equal(13, probes);
    }
}
