using Xunit;

// UI-global static state (WasmDialogHost registry, hover/selection
// singletons) is shared across test classes by design, mirroring the
// single-threaded browser host: serialize the suite instead of racing it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
