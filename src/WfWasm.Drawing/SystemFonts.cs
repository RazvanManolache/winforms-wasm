// System fonts: Tahoma-based approximations until OS font queries exist.
namespace System.Drawing
{
    /// <summary>System fonts (cf. System.Drawing.SystemFonts).</summary>
    public static class SystemFonts
    {
        public static Font CaptionFont { get; } = new("Tahoma", 8f);
        public static Font SmallCaptionFont { get; } = new("Tahoma", 8f);
        public static Font MenuFont { get; } = new("Tahoma", 8f);
        public static Font MessageBoxFont { get; } = new("Tahoma", 8f);
        public static Font StatusFont { get; } = new("Tahoma", 8f);
        public static Font DefaultFont { get; } = new("Tahoma", 8f);
        public static Font DialogFont { get; } = new("Tahoma", 8f);
        public static Font IconTitleFont { get; } = new("Tahoma", 8f);

        public static Font GetFontByName(string systemFontName) => new(systemFontName, 8f);
    }
}
