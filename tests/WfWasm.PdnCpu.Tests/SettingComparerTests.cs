using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// CpuPaint comparer helpers must preserve EqualityComparer<T>.Default
// semantics for value types: the rewriter replaces direct
// get_Default/Equals/GetHashCode on EqualityComparer<TValue>
// instantiations (which throws InvalidProgramException in the browser
// interpreter for value-type TValue) with these helpers. Includes the
// NaN-equals-NaN contract for doubles and the null-comparer fallback.
public sealed class SettingComparerTests
{
    [Fact]
    public void Default_Int_Compares()
    {
        object c = CpuPaint.SettingEqualityComparer(typeof(int));
        Assert.NotNull(c);
        Assert.True(CpuPaint.SettingComparerEquals(c, 1, 1));
        Assert.False(CpuPaint.SettingComparerEquals(c, 1, 2));
    }

    [Fact]
    public void Default_Double_NaN_Equals_NaN()
    {
        object c = CpuPaint.SettingEqualityComparer(typeof(double));
        Assert.True(CpuPaint.SettingComparerEquals(c, double.NaN, double.NaN));
        Assert.False(CpuPaint.SettingComparerEquals(c, double.NaN, 1.0));
    }

    [Fact]
    public void Default_Enum_Compares()
    {
        object c = CpuPaint.SettingEqualityComparer(typeof(StringComparison));
        Assert.True(CpuPaint.SettingComparerEquals(c,
            StringComparison.Ordinal, StringComparison.Ordinal));
        Assert.False(CpuPaint.SettingComparerEquals(c,
            StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hash_Matches_Value_Hash()
    {
        object c = CpuPaint.SettingEqualityComparer(typeof(int));
        Assert.Equal(7.GetHashCode(), CpuPaint.SettingComparerHash(c, 7));
    }

    [Fact]
    public void Null_Comparer_Falls_Back_To_Object_Semantics()
    {
        Assert.True(CpuPaint.SettingComparerEquals(null, 3, 3));
        Assert.False(CpuPaint.SettingComparerEquals(null, 3, 4));
        Assert.Equal(3.GetHashCode(), CpuPaint.SettingComparerHash(null, 3));
    }

    [Fact]
    public void Null_Type_Fail_Safe()
    {
        Assert.True(CpuPaint.SettingComparerEquals(
            CpuPaint.SettingEqualityComparer(null), 5, 5));
    }

    private class FakeSetting<T>
    {
        protected virtual System.Collections.Generic.EqualityComparer<T> OnGetStaticEqualityComparer() =>
            System.Collections.Generic.EqualityComparer<T>.Default;
    }

    [Fact]
    public void GetComparer_Dispatches_Through_Helper()
    {
        object c = CpuPaint.SettingGetComparer(new FakeSetting<int>());
        Assert.NotNull(c);
        Assert.True(CpuPaint.SettingComparerEquals(c, 1, 1));
        Assert.False(CpuPaint.SettingComparerEquals(c, 1, 2));
    }

    [Fact]
    public void GetComparer_Null_Fail_Safe()
    {
        Assert.Null(CpuPaint.SettingGetComparer(null));
    }

    private class FakeConverterBase
    {
        public virtual bool OnValidateValue(Type t, object v) => true;
    }

    private sealed class FakeDoubleConverter : FakeConverterBase
    {
        public override bool OnValidateValue(Type t, object v) =>
            v is double d && d >= 0.0 && d <= 100.0;
    }

    [Fact]
    public void ConverterValidate_Dispatches_To_Override()
    {
        object c = new FakeDoubleConverter();
        Assert.True(CpuPaint.SettingConverterValidateValue(c, typeof(double), 50.0));
        Assert.False(CpuPaint.SettingConverterValidateValue(c, typeof(double), 150.0));
    }

    [Fact]
    public void ConverterValidate_Null_Fail_Safe()
    {
        Assert.True(CpuPaint.SettingConverterValidateValue(null, typeof(double), 1.0));
    }

    private sealed class InvalidProgramConverter : FakeConverterBase
    {
        public int Calls;
        public override bool OnValidateValue(Type t, object v)
        {
            Calls++;
            throw new InvalidProgramException("interpreter poison");
        }
    }

    [Fact]
    public void ConverterValidate_InvalidProgram_Skips_Reinvoke()
    {
        object c = new InvalidProgramConverter();
        Assert.True(CpuPaint.SettingConverterValidateValue(c, typeof(double), 1.0));
        Assert.True(CpuPaint.SettingConverterValidateValue(c, typeof(double), 2.0));
        Assert.Equal(1, ((InvalidProgramConverter)c).Calls);
    }
}
