using WfWasm.Rewrite;
using Xunit;

public sealed class TelemetryDesignModeTests
{
    [Fact]
    public void Telemetry_DesignMode_Neutralized_To_Empty()
    {
        // Syncfusion.Telemetry.Telemetry.DesignMode answers
        // Process.GetCurrentProcess().ProcessName; headless that throws PNSE
        // (caught by the vendor, telemetry inert). The rule replaces the
        // getter body with ldstr "" + ret: no designer token matches, outcome
        // identical to today's caught path, no first-chance.
        string input = @"E:\tmp\wfwasm-syncfusion-apps\syncfusion-chart\Syncfusion.Telemetry.dll";
        if (!File.Exists(input)) return;
        using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(input);
        AssemblyRewriter.NeutralizeTelemetryDesignMode(asm.MainModule);
        var method = asm.MainModule.GetType("Syncfusion.Telemetry.Telemetry").Methods
            .First(m => m.Name == "get_DesignMode");
        var ops = method.Body.Instructions.Select(i => i.OpCode).ToArray();
        Assert.Equal(2, ops.Length);
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Ldstr, ops[0]);
        Assert.Equal(Mono.Cecil.Cil.OpCodes.Ret, ops[1]);
        Assert.Equal("", (string)method.Body.Instructions[0].Operand);
    }
}
