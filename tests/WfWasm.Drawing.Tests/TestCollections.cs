using Xunit;

// The BrowserText.MeasureOverride seam is process-global mutable state:
// serialize the suite so a fixed-raster injection can never leak into
// a concurrent measurement (suite runs in milliseconds; see
// BrowserTextTests.MeasureString_Huge_Layout_Width_Stays_Unconstrained).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
