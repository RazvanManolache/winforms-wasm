// UIA client probe: UIAutomationCore.dll does not exist in the browser,
// so the DllImport behind AutomationInteropProvider.ClientsAreListening
// throws DllNotFoundException out of every caller (Telerik list adds,
// Invoke-marshaled UI updates). The rewriter retargets that static getter
// here: no assistive client can listen headless, so false is exact.
// Regression: DllNotFoundException UIAutomationCore <- RadListDataItem.Add
// <- MainForm.CategoryListLoaded <- DeserializeData, leaving BookShelf
// shelves empty (categories loaded, UI update aborted).
namespace System.Windows.Forms;

public static class WasmAutomation
{
    public static bool NoClientsAreListening() => false;
}
