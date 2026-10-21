namespace WfWasm.CanvasHost;

// Per-signature first-chance console gate: the old global cap (80) let one
// noisy site (BinaryFormatter image reads: dozens per boot) exhaust the
// budget and mask the distinct error behind a ThreadExceptionDialog (RTE).
// Each type+message signature gets its own small budget; a high global
// cap still bounds pathological sessions.
public static class FirstChanceLogGate
{
    public const int PerSignatureCap = 8;
    public const int GlobalCap = 500;

    private static readonly object s_gate = new();
    private static readonly Dictionary<string, int> s_counts = new(StringComparer.Ordinal);
    private static int s_total;

    public static bool ShouldLog(string key)
    {
        lock (s_gate)
        {
            if (s_total >= GlobalCap) return false;
            s_counts.TryGetValue(key, out int n);
            if (n >= PerSignatureCap) return false;
            s_counts[key] = n + 1;
            s_total++;
            return true;
        }
    }

    internal static void Reset()
    {
        lock (s_gate)
        {
            s_counts.Clear();
            s_total = 0;
        }
    }
}
