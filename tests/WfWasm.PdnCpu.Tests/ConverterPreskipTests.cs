using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// Regression: assembly-wide converter pre-skip (fail-open). Executing
// a SettingConverter<TValue> override on a value-type instantiation
// can kill the browser session with an uncatchable frameless NRE, so
// no converter body may execute headless: the first validation call
// sweeps loaded assemblies (plus AssemblyLoad for late ones) and
// remembers every converter type as skip-and-accept. These tests pin
// the plumbing: foreign assemblies are inert, repeat sweeps are
// no-ops, and validation stays fail-open accept.
public sealed class ConverterPreskipTests
{
    private sealed class DummyConverter
    {
    }

    [Fact]
    public void PreSkipConvertersIn_NullAssembly_NoThrow()
    {
        CpuPaint.PreSkipConvertersIn(null);
    }

    [Fact]
    public void PreSkipConvertersIn_ForeignAssembly_NoThrowAndIdempotent()
    {
        var asm = typeof(object).Assembly;
        CpuPaint.PreSkipConvertersIn(asm);
        CpuPaint.PreSkipConvertersIn(asm);
    }

    [Fact]
    public void SettingConverterValidateValue_NullConverter_Accepts()
    {
        Assert.True(CpuPaint.SettingConverterValidateValue(null!, typeof(object), null!));
    }

    [Fact]
    public void SettingConverterValidateValue_UnknownConverter_AcceptsWithoutThrow()
    {
        // No OnValidateValue(Type, object) shape: fail-open accept, and
        // the first call also runs the loaded-assembly sweep (must not
        // throw on framework/test assemblies).
        Assert.True(CpuPaint.SettingConverterValidateValue(new DummyConverter(), typeof(object), null!));
    }

    [Fact]
    public void FirstValidation_SweepsLoadedAssemblies()
    {
        // The first validation call installs the AssemblyLoad hook and
        // sweeps already-loaded assemblies; the sweep set must then
        // contain this test assembly (idempotent, no-throw).
        Assert.True(CpuPaint.SettingConverterValidateValue(new DummyConverter(), typeof(object), null!));
        var field = typeof(CpuPaint).GetField("s_converterScannedAssemblies",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var set = (System.Collections.Generic.HashSet<string>?)field.GetValue(null);
        Assert.NotNull(set);
        Assert.Contains(typeof(ConverterPreskipTests).Assembly.FullName!, set);
    }
}
