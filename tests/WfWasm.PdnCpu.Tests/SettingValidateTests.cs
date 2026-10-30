using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// CpuPaint.SettingValidateValueT must preserve virtual dispatch: the
// rewriter replaces direct callvirt on Setting<TValue>
// instantiations (which throws InvalidProgramException in the browser
// interpreter for value-type TValue) with this reflection helper.
// A range-checking override that rejects must read false through the
// helper; the permissive base must read true; unknown shapes fail
// open (accept) so validation can never kill boot.
public sealed class SettingValidateTests
{
    private class FakeSetting<T>
    {
        protected virtual bool OnValidateValueT(T value) => true;

        public bool Check(object boxed) =>
            CpuPaint.SettingValidateValueT(this, boxed);
    }

    private sealed class RangedDoubleSetting : FakeSetting<double>
    {
        protected override bool OnValidateValueT(double value) =>
            value >= 0.0 && value <= 100.0;
    }

    [Fact]
    public void Base_Accepts_Through_Helper()
    {
        Assert.True(CpuPaint.SettingValidateValueT(new FakeSetting<int>(), 42));
    }

    [Fact]
    public void Override_Rejects_Out_Of_Range_Through_Helper()
    {
        var s = new RangedDoubleSetting();
        Assert.True(CpuPaint.SettingValidateValueT(s, 50.0));
        Assert.False(CpuPaint.SettingValidateValueT(s, 150.0));
    }

    [Fact]
    public void Null_Setting_Fail_Open()
    {
        Assert.True(CpuPaint.SettingValidateValueT(null, 1));
    }

    [Fact]
    public void Missing_Method_Fail_Open()
    {
        Assert.True(CpuPaint.SettingValidateValueT(new object(), 1));
    }

    private sealed class InvalidProgramSetting : FakeSetting<int>
    {
        public int Calls;
        protected override bool OnValidateValueT(int value)
        {
            Calls++;
            throw new InvalidProgramException("interpreter poison");
        }
    }

    [Fact]
    public void InvalidProgram_Skips_Reinvoke_After_First_Throw()
    {
        var s = new InvalidProgramSetting();
        Assert.True(CpuPaint.SettingValidateValueT(s, 1));
        Assert.True(CpuPaint.SettingValidateValueT(s, 2));
        Assert.Equal(1, s.Calls);
    }
}
