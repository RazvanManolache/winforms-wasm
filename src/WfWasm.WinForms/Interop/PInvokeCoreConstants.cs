// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

using Gfx = System.Drawing.Graphics;
namespace Windows.Win32
{
    internal static partial class PInvokeCore
    {
        public const int DISPID_BORDERSTYLE = -504;
        public const int DISPID_TABSTOP = -516;
        public const int DISPID_FORECOLOR = -513;
        public const int DISPID_BACKCOLOR = -501;
        public const int DISPID_HWND = -515;
        public const int DISPID_ENABLED = -514;
        public const int DISPID_READYSTATE = -525;
        public const int DISPID_FONT = -512;
        public const int DISPID_TEXT = -517;

        public const string INK_SERIALIZED_FORMAT = "Ink Serialized Format";
        public const string CFSTR_FILENAMEA = "FileName";
        public const string CFSTR_FILENAME = "FileNameW";

        public const uint WM_USER = 0x0400;
        public const uint WM_APP = 0x8000;
        public const uint LF_FACESIZE = 32;


        public static unsafe Graphics.GdiPlus.Status GdiplusStartup(nuint* token, Graphics.GdiPlus.GdiplusStartupInput* input, void* output) => Graphics.GdiPlus.Status.Ok;
        public static unsafe void GdiplusShutdown(nuint token) { }
    }

    internal static partial class PInvoke
    {
        public const uint MAX_TAB_STOPS = 32;
        public const int LB_ERR = -1;

        public const int ARW_BOTTOMLEFT = 0x0000;
        public const int ARW_BOTTOMRIGHT = 0x0001;
        public const int ARW_TOPLEFT = 0x0002;
        public const int ARW_TOPRIGHT = 0x0003;
        public const int ARW_STARTLEFT = 0x0000;
        public const int ARW_STARTUP = 0x0004;
        public const int ARW_LEFT = 0x0000;
        public const int ARW_RIGHT = 0x0000;
        public const int ARW_UP = 0x0004;
        public const int ARW_DOWN = 0x0004;
        public const int ARW_HIDE = 0x0008;

        public const int HTNOWHERE = 0;
        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTSYSMENU = 3;
        public const int HTGROWBOX = 4;
        public const int HTMENU = 5;
        public const int HTHSCROLL = 6;
        public const int HTVSCROLL = 7;
        public const int HTMINBUTTON = 8;
        public const int HTMAXBUTTON = 9;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;
        public const int HTBORDER = 18;
        public const int HTCLOSE = 20;
        public const int HTHELP = 21;

        public const uint LV_VIEW_ICON = 0x0000;
        public const uint LV_VIEW_DETAILS = 0x0001;
        public const uint LV_VIEW_SMALLICON = 0x0002;
        public const uint LV_VIEW_LIST = 0x0003;
        public const uint LV_VIEW_TILE = 0x0004;

        public const uint LVA_DEFAULT = 0x0000;
        public const uint LVA_ALIGNLEFT = 0x0001;
        public const uint LVA_ALIGNTOP = 0x0002;
        public const uint LVA_SNAPTOGRID = 0x0005;

        public const uint LVIR_BOUNDS = 0;
        public const uint LVIR_ICON = 1;
        public const uint LVIR_LABEL = 2;
        public const uint LVIR_SELECTBOUNDS = 3;

        public const uint DTS_UPDOWN = 0x0001;
        public const uint DTS_TIMEFORMAT = 0x0009;

        public static BOOL DrawFrameControl(HDC hdc, ref RECT lprc, uint uType, uint uState)
            => PaintFrameControl(hdc, lprc, uType, uState);
        public static unsafe BOOL DrawFrameControl(HDC hdc, RECT* lprc, uint uType, uint uState)
            => lprc is null ? (BOOL)false : PaintFrameControl(hdc, *lprc, uType, uState);

        // Headless DrawFrameControl: classic (non-theme) button frames painted
        // with Graphics primitives through the HDC target registry (same seam
        // as WasmFillBridge). Only DFC_BUTTON check/radio marks are emulated;
        // every other frame type keeps today's silent no-op success so
        // callers (ControlPaint, renderers) never fail headless.
        private static BOOL PaintFrameControl(HDC hdc, RECT lprc, uint uType, uint uState)
        {
            const uint DFC_BUTTON = 4;
            if (uType != DFC_BUTTON)
                return (BOOL)true;
            uint kind = uState & 0x1Fu;
            bool isRadio = kind == 0x4u;
            bool isMixed = kind == 0x8u;
            if (!isRadio && !isMixed && kind != 0x0u)
                return (BOOL)true;
            if (!PInvoke.WasmFillBridge.TryGetTarget(hdc, out Gfx? graphics) || graphics is null)
                return (BOOL)true;
            try
            {
                Rectangle rect = lprc;
                if (rect.Width <= 0 || rect.Height <= 0)
                    return (BOOL)true;
                bool check = (uState & 0x400u) != 0;
                bool pushed = (uState & 0x200u) != 0;
                bool inactive = (uState & 0x100u) != 0;
                bool flat = (uState & 0x4000u) != 0;
                bool mono = (uState & 0x8000u) != 0;
                Color edge = (mono || flat) ? Color.Black : SystemColors.ControlDark;
                Color face = pushed ? SystemColors.Control : Color.White;
                Color mark = inactive ? SystemColors.GrayText : Color.Black;
                using (var back = new SolidBrush(face))
                using (var edgePen = new Pen(edge))
                using (var markPen = new Pen(mark, 2))
                {
                    if (isRadio)
                    {
                        graphics.FillEllipse(back, rect);
                        graphics.DrawEllipse(edgePen, rect);
                        if (check)
                        {
                            int dx = rect.Width / 4, dy = rect.Height / 4;
                            using var dot = new SolidBrush(mark);
                            graphics.FillEllipse(dot, rect.X + dx, rect.Y + dy,
                                rect.Width - 2 * dx, rect.Height - 2 * dy);
                        }
                    }
                    else
                    {
                        graphics.FillRectangle(back, rect);
                        graphics.DrawRectangle(edgePen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                        if (check)
                        {
                            if (isMixed)
                            {
                                int dx = rect.Width / 4, dy = rect.Height / 4;
                                using var fill = new SolidBrush(mark);
                                graphics.FillRectangle(fill, rect.X + dx, rect.Y + dy,
                                    rect.Width - 2 * dx, rect.Height - 2 * dy);
                            }
                            else
                            {
                                graphics.DrawLine(markPen,
                                    rect.X + 3, rect.Y + 6, rect.X + 5, rect.Y + 9);
                                graphics.DrawLine(markPen,
                                    rect.X + 5, rect.Y + 9, rect.X + 10, rect.Y + 3);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return (BOOL)true;
        }
        public static BOOL GetComboBoxInfo(HWND hwndCombo, ref UI.Controls.COMBOBOXINFO pcbi) => (BOOL)false;
        public static BOOL GetComboBoxInfo(ref UI.Controls.COMBOBOXINFO pcbi) => (BOOL)false;
        public static void SetThemeAppProperties(UI.Controls.SET_THEME_APP_PROPERTIES_FLAGS flags) { }
    }
}
