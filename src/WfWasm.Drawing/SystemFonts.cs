// System fonts: Segoe UI 9pt defaults (modern .NET on Windows 10+), with the
// MS Shell Dlg stand-in (Microsoft Sans Serif 8.25pt) for dialog units.
namespace System.Drawing
{
    /// <summary>System fonts (cf. System.Drawing.SystemFonts).</summary>
    public static class SystemFonts
    {
        public static Font CaptionFont { get; } = new("Segoe UI", 9f);
        public static Font SmallCaptionFont { get; } = new("Segoe UI", 9f);
        public static Font MenuFont { get; } = new("Segoe UI", 9f);
        public static Font MessageBoxFont { get; } = new("Segoe UI", 9f);
        public static Font StatusFont { get; } = new("Segoe UI", 9f);
        public static Font DefaultFont { get; } = new("Segoe UI", 9f);
        public static Font DialogFont { get; } = new("Microsoft Sans Serif", 8.25f);
        public static Font IconTitleFont { get; } = new("Segoe UI", 9f);

        public static Font GetFontByName(string systemFontName) => new(systemFontName, 9f);
    }
}
