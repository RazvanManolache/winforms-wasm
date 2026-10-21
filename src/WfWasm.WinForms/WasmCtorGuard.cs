namespace System.Windows.Forms;

// Ctor-guard stash for the rewriter's InitializeComponent rule: an
// uncaught throw inside a reflection-invoked ctor cannot unwind across
// the WASM runtime_invoke boundary (mono unwinder aborts the session
// with zero managed frames), so guarded designer methods stash the
// throw here and return degraded; the host (above the boundary) takes
// it and reports + falls back. Type/message only, never TargetSite or
// StackTrace (frame resolution aborts the browser runtime).
public static class WasmCtorGuard
{
    private static Exception? s_stashed;

    public static void Stash(Exception? ex)
    {
        try
        {
            if (ex is null)
                return;
            System.Threading.Interlocked.CompareExchange(ref s_stashed, ex, null);
            try { System.Console.WriteLine("[CTOR-GUARD] " + ex.GetType().Name + ": " + ((ex.Message ?? "").Split('\n')[0])); } catch { }
        }
        catch { }
    }

    public static Exception? TakeStashed()
    {
        try
        {
            return System.Threading.Interlocked.Exchange(ref s_stashed, null);
        }
        catch
        {
            return null;
        }
    }
}
