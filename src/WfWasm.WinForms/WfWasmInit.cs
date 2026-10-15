namespace System.Windows.Forms;

internal static class WfWasmInit
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init()
    {
        AppContext.SetSwitch("System.Windows.Forms.ServicePointManagerCheckCrl", false);
    }
}
