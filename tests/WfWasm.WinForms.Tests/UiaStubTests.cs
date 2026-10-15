extern alias Fork;
namespace WfWasm.WinForms.Tests;

// UIA stub contract: no assistive client can listen in the browser, so
// the probe the rewriter retargets AutomationInteropProvider to answers
// false without touching UIAutomationCore.dll.
// Regression: DllNotFoundException UIAutomationCore <- RadListDataItem.Add
// <- MainForm.CategoryListLoaded <- DeserializeData (BookShelf shelves
// stayed empty: categories loaded, UI update aborted).
public sealed class UiaStubTests
{
    [Fact]
    public void NoClientsAreListening_Answers_False()
    {
        Assert.False(Fork::System.Windows.Forms.WasmAutomation.NoClientsAreListening());
    }
}
