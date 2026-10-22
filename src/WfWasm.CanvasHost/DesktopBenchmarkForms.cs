using System.Reflection;

namespace WfWasm.CanvasHost;

/// <summary>Exact real-form construction, without arbitrary-form or smoke-form fallbacks.</summary>
internal static class DesktopBenchmarkForms
{
    public static System.Windows.Forms.Form Create(DesktopBenchmarks.Benchmark benchmark)
    {
        if (benchmark.RequiresManualInput)
            throw new NotSupportedException($"{benchmark.Label}: application files and verified startup information are not prepared. {benchmark.Notes}");
        Type? type = Type.GetType(benchmark.FormType + ", " + benchmark.FormAssembly);
        type ??= AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, benchmark.FormAssembly, StringComparison.OrdinalIgnoreCase))
            ?.GetType(benchmark.FormType);
        if (type is null)
            throw new TypeLoadException($"{benchmark.Label}: real form {benchmark.FormType} was not found in {benchmark.FormAssembly}.");
        if (!typeof(System.Windows.Forms.Form).IsAssignableFrom(type))
            throw new TypeLoadException($"{benchmark.Label}: declared startup type is not a compatible WinForms Form.");
        var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (constructor is null)
            throw new NotSupportedException($"{benchmark.Label}: its real startup form requires application services. A startup adapter is needed. {benchmark.Notes}");
        return (System.Windows.Forms.Form)constructor.Invoke(null);
    }
}
