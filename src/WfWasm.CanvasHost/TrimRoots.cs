using System.Diagnostics.CodeAnalysis;

// Publish trim roots for the resource-cache providers the CPU
// drawing context reads through corlib reflection (Type.GetMethod /
// Type.GetField by name). The publish trimmer cannot see
// string-based member references, so without these roots those
// members risk being trimmed from release bundles while unit tests
// (untrimmed desktop assemblies) stay green. Keep() is called from
// the Paint.NET bootstrap path so the method itself is never
// trimmed away with its attributes.
internal static class TrimRoots
{
    [DynamicDependency("get_SystemColor", "PaintDotNet.UI.Media.SystemBrush", "PaintDotNet.UI")]
    [DynamicDependency("get_BitmapSource", "PaintDotNet.UI.Media.DeviceBitmap", "PaintDotNet.UI")]
    [DynamicDependency("get_Color", "PaintDotNet.UI.Media.SolidColorBrush", "PaintDotNet.UI")]
    [DynamicDependency("get_Text", "PaintDotNet.UI.Media.TextLayout", "PaintDotNet.UI")]
    [DynamicDependency("get_FontSize", "PaintDotNet.UI.Media.TextLayout", "PaintDotNet.UI")]
    [DynamicDependency("get_MaxWidth", "PaintDotNet.UI.Media.TextLayout", "PaintDotNet.UI")]
    [DynamicDependency("get_MaxHeight", "PaintDotNet.UI.Media.TextLayout", "PaintDotNet.UI")]
    [DynamicDependency("srgbImageProvider", "PaintDotNet.Resources.ImageResourceUtil+AutoColorSpaceImageProvider", "PaintDotNet.Framework")]
    internal static void Keep()
    {
    }
}
