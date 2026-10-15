// System colors: exact .NET member shapes; web-compatible CPU values.
namespace System.Drawing
{
    /// <summary>System colors (cf. System.Drawing.SystemColors).</summary>
    public static class SystemColors
    {
        public static Color ActiveBorder => Color.FromKnownColor(KnownColor.ActiveBorder);
        public static Color ActiveCaption => Color.FromKnownColor(KnownColor.ActiveCaption);
        public static Color ActiveCaptionText => Color.FromKnownColor(KnownColor.ActiveCaptionText);
        public static Color AppWorkspace => Color.FromKnownColor(KnownColor.AppWorkspace);
        public static Color ButtonFace => Color.FromKnownColor(KnownColor.ButtonFace);
        public static Color ButtonHighlight => Color.FromKnownColor(KnownColor.ButtonHighlight);
        public static Color ButtonShadow => Color.FromKnownColor(KnownColor.ButtonShadow);
        public static Color Control => Color.FromKnownColor(KnownColor.Control);
        public static Color ControlDark => Color.FromKnownColor(KnownColor.ControlDark);
        public static Color ControlDarkDark => Color.FromKnownColor(KnownColor.ControlDarkDark);
        public static Color ControlLight => Color.FromKnownColor(KnownColor.ControlLight);
        public static Color ControlLightLight => Color.FromKnownColor(KnownColor.ControlLightLight);
        public static Color ControlText => Color.FromKnownColor(KnownColor.ControlText);
        public static Color Desktop => Color.FromKnownColor(KnownColor.Desktop);
        public static Color GradientActiveCaption => Color.FromKnownColor(KnownColor.GradientActiveCaption);
        public static Color GradientInactiveCaption => Color.FromKnownColor(KnownColor.GradientInactiveCaption);
        public static Color GrayText => Color.FromKnownColor(KnownColor.GrayText);
        public static Color Highlight => Color.FromKnownColor(KnownColor.Highlight);
        public static Color HighlightText => Color.FromKnownColor(KnownColor.HighlightText);
        public static Color HotTrack => Color.FromKnownColor(KnownColor.HotTrack);
        public static Color InactiveBorder => Color.FromKnownColor(KnownColor.InactiveBorder);
        public static Color InactiveCaption => Color.FromKnownColor(KnownColor.InactiveCaption);
        public static Color InactiveCaptionText => Color.FromKnownColor(KnownColor.InactiveCaptionText);
        public static Color Info => Color.FromKnownColor(KnownColor.Info);
        public static Color InfoText => Color.FromKnownColor(KnownColor.InfoText);
        public static Color Menu => Color.FromKnownColor(KnownColor.Menu);
        public static Color MenuBar => Color.FromKnownColor(KnownColor.MenuBar);
        public static Color MenuHighlight => Color.FromKnownColor(KnownColor.MenuHighlight);
        public static Color MenuText => Color.FromKnownColor(KnownColor.MenuText);
        public static Color ScrollBar => Color.FromKnownColor(KnownColor.ScrollBar);
        public static Color Window => Color.FromKnownColor(KnownColor.Window);
        public static Color WindowFrame => Color.FromKnownColor(KnownColor.WindowFrame);
        public static Color WindowText => Color.FromKnownColor(KnownColor.WindowText);

        public static bool UseAlternativeColorSet { get; set; }
    }

    /// <summary>System brushes (cf. System.Drawing.SystemBrushes).</summary>
    public static class SystemBrushes
    {
        public static Brush ActiveBorder => new SolidBrush(SystemColors.ActiveBorder);
        public static Brush ActiveCaption => new SolidBrush(SystemColors.ActiveCaption);
        public static Brush ActiveCaptionText => new SolidBrush(SystemColors.ActiveCaptionText);
        public static Brush AppWorkspace => new SolidBrush(SystemColors.AppWorkspace);
        public static Brush ButtonFace => new SolidBrush(SystemColors.ButtonFace);
        public static Brush ButtonHighlight => new SolidBrush(SystemColors.ButtonHighlight);
        public static Brush ButtonShadow => new SolidBrush(SystemColors.ButtonShadow);
        public static Brush Control => new SolidBrush(SystemColors.Control);
        public static Brush ControlDark => new SolidBrush(SystemColors.ControlDark);
        public static Brush ControlDarkDark => new SolidBrush(SystemColors.ControlDarkDark);
        public static Brush ControlLight => new SolidBrush(SystemColors.ControlLight);
        public static Brush ControlLightLight => new SolidBrush(SystemColors.ControlLightLight);
        public static Brush ControlText => new SolidBrush(SystemColors.ControlText);
        public static Brush Desktop => new SolidBrush(SystemColors.Desktop);
        public static Brush GradientActiveCaption => new SolidBrush(SystemColors.GradientActiveCaption);
        public static Brush GradientInactiveCaption => new SolidBrush(SystemColors.GradientInactiveCaption);
        public static Brush GrayText => new SolidBrush(SystemColors.GrayText);
        public static Brush Highlight => new SolidBrush(SystemColors.Highlight);
        public static Brush HighlightText => new SolidBrush(SystemColors.HighlightText);
        public static Brush HotTrack => new SolidBrush(SystemColors.HotTrack);
        public static Brush InactiveBorder => new SolidBrush(SystemColors.InactiveBorder);
        public static Brush InactiveCaption => new SolidBrush(SystemColors.InactiveCaption);
        public static Brush InactiveCaptionText => new SolidBrush(SystemColors.InactiveCaptionText);
        public static Brush Info => new SolidBrush(SystemColors.Info);
        public static Brush InfoText => new SolidBrush(SystemColors.InfoText);
        public static Brush Menu => new SolidBrush(SystemColors.Menu);
        public static Brush MenuBar => new SolidBrush(SystemColors.MenuBar);
        public static Brush MenuHighlight => new SolidBrush(SystemColors.MenuHighlight);
        public static Brush MenuText => new SolidBrush(SystemColors.MenuText);
        public static Brush ScrollBar => new SolidBrush(SystemColors.ScrollBar);
        public static Brush Window => new SolidBrush(SystemColors.Window);
        public static Brush WindowFrame => new SolidBrush(SystemColors.WindowFrame);
        public static Brush WindowText => new SolidBrush(SystemColors.WindowText);

        public static Brush FromSystemColor(Color c) => new SolidBrush(c);
    }

    /// <summary>System pens (cf. System.Drawing.SystemPens).</summary>
    public static class SystemPens
    {
        public static Pen ActiveBorder => new Pen(SystemColors.ActiveBorder);
        public static Pen ActiveCaption => new Pen(SystemColors.ActiveCaption);
        public static Pen ActiveCaptionText => new Pen(SystemColors.ActiveCaptionText);
        public static Pen AppWorkspace => new Pen(SystemColors.AppWorkspace);
        public static Pen ButtonFace => new Pen(SystemColors.ButtonFace);
        public static Pen ButtonHighlight => new Pen(SystemColors.ButtonHighlight);
        public static Pen ButtonShadow => new Pen(SystemColors.ButtonShadow);
        public static Pen Control => new Pen(SystemColors.Control);
        public static Pen ControlDark => new Pen(SystemColors.ControlDark);
        public static Pen ControlDarkDark => new Pen(SystemColors.ControlDarkDark);
        public static Pen ControlLight => new Pen(SystemColors.ControlLight);
        public static Pen ControlLightLight => new Pen(SystemColors.ControlLightLight);
        public static Pen ControlText => new Pen(SystemColors.ControlText);
        public static Pen Desktop => new Pen(SystemColors.Desktop);
        public static Pen GradientActiveCaption => new Pen(SystemColors.GradientActiveCaption);
        public static Pen GradientInactiveCaption => new Pen(SystemColors.GradientInactiveCaption);
        public static Pen GrayText => new Pen(SystemColors.GrayText);
        public static Pen Highlight => new Pen(SystemColors.Highlight);
        public static Pen HighlightText => new Pen(SystemColors.HighlightText);
        public static Pen HotTrack => new Pen(SystemColors.HotTrack);
        public static Pen InactiveBorder => new Pen(SystemColors.InactiveBorder);
        public static Pen InactiveCaption => new Pen(SystemColors.InactiveCaption);
        public static Pen InactiveCaptionText => new Pen(SystemColors.InactiveCaptionText);
        public static Pen Info => new Pen(SystemColors.Info);
        public static Pen InfoText => new Pen(SystemColors.InfoText);
        public static Pen Menu => new Pen(SystemColors.Menu);
        public static Pen MenuBar => new Pen(SystemColors.MenuBar);
        public static Pen MenuHighlight => new Pen(SystemColors.MenuHighlight);
        public static Pen MenuText => new Pen(SystemColors.MenuText);
        public static Pen ScrollBar => new Pen(SystemColors.ScrollBar);
        public static Pen Window => new Pen(SystemColors.Window);
        public static Pen WindowFrame => new Pen(SystemColors.WindowFrame);
        public static Pen WindowText => new Pen(SystemColors.WindowText);

        public static Pen FromSystemColor(Color c) => new Pen(c);
    }

    /// <summary>Stock icons (cf. System.Drawing.SystemIcons).</summary>
    public static class SystemIcons
    {
        public static Icon Application => Glyph(g =>
        {
            g.FillRectangle(Brushes.SteelBlue, 4, 2, 24, 28);
            g.FillRectangle(Brushes.White, 8, 6, 16, 4);
            g.FillRectangle(Brushes.White, 8, 13, 16, 4);
            g.FillRectangle(Brushes.White, 8, 20, 10, 4);
        });

        public static Icon Asterisk => InfoGlyph();
        public static Icon Information => InfoGlyph();

        public static Icon Error => Glyph(g =>
        {
            g.FillEllipse(Brushes.Crimson, 2, 2, 28, 28);
            using var pen = new Pen(Color.White, 5f);
            g.DrawLine(pen, 10, 10, 22, 22);
            g.DrawLine(pen, 22, 10, 10, 22);
        });

        public static Icon Hand => Error;

        public static Icon Exclamation => WarnGlyph();
        public static Icon Warning => WarnGlyph();

        public static Icon Question => Glyph(g =>
        {
            g.FillEllipse(Brushes.RoyalBlue, 2, 2, 28, 28);
            g.FillEllipse(Brushes.White, 12, 21, 8, 8);
            g.FillRectangle(Brushes.White, 12, 7, 8, 10);
        });

        public static Icon WinLogo => Glyph(g =>
        {
            g.FillRectangle(Brushes.Crimson, 3, 5, 12, 10);
            g.FillRectangle(Brushes.ForestGreen, 17, 5, 12, 10);
            g.FillRectangle(Brushes.RoyalBlue, 3, 17, 12, 10);
            g.FillRectangle(Brushes.Gold, 17, 17, 12, 10);
        });

        public static Icon Shield => Glyph(g =>
        {
            g.FillPolygon(Brushes.RoyalBlue, [new Point(16, 2), new Point(28, 8), new Point(26, 20), new Point(16, 30), new Point(6, 20), new Point(4, 8)]);
            using var pen = new Pen(Color.White, 3f);
            g.DrawLine(pen, 10, 16, 15, 21);
            g.DrawLine(pen, 15, 21, 23, 11);
        });

        public static Icon GetStockIcon(StockIconId id, StockIconOptions options = default) =>
            id switch
            {
                StockIconId.Error => Error,
                StockIconId.Warning => Warning,
                StockIconId.Info => Information,
                StockIconId.Shield => Shield,
                StockIconId.Application => Application,
                _ => Application,
            };

        public static Icon GetStockIcon(StockIconId id, int version) =>
            GetStockIcon(id, (StockIconOptions)version);

        private static Icon InfoGlyph() => Glyph(g =>
        {
            g.FillEllipse(Brushes.RoyalBlue, 2, 2, 28, 28);
            g.FillRectangle(Brushes.White, 13, 7, 6, 12);
            g.FillEllipse(Brushes.White, 12, 21, 8, 8);
        });

        private static Icon WarnGlyph() => Glyph(g =>
        {
            g.FillPolygon(Brushes.Gold, [new Point(16, 2), new Point(30, 28), new Point(2, 28)]);
            g.FillPolygon(Brushes.Black, [new Point(16, 6), new Point(27, 26), new Point(5, 26)]);
            g.FillPolygon(Brushes.Gold, [new Point(16, 9), new Point(25, 24), new Point(7, 24)]);
            g.FillRectangle(Brushes.Black, 14, 12, 4, 7);
            g.FillEllipse(Brushes.Black, 13, 21, 6, 5);
        });

        private static Icon Glyph(Action<Graphics> paint)
        {
            var bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
                paint(g);
            return new Icon(bmp);
        }
    }

}
