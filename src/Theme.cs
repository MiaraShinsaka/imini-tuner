using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ImimiCC
{
    internal static class Theme
    {
        public static float S = 1f;

        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();

        public static bool IsDark { get; private set; }

        public static Color Bg { get; private set; }
        public static Color Bg2 { get; private set; }
        public static Color Panel { get; private set; }
        public static Color PanelHover { get; private set; }
        public static Color Border { get; private set; }
        public static Color BorderSoft { get; private set; }
        public static Color Text { get; private set; }
        public static Color Muted { get; private set; }
        public static Color Faint { get; private set; }
        public static Color Green { get; private set; }
        public static Color Blue { get; private set; }
        public static Color Orange { get; private set; }
        public static Color Red { get; private set; }
        public static Color Yellow { get; private set; }
        public static Color AccentSilent { get; private set; }
        public static Color AccentBalanced { get; private set; }
        public static Color AccentPerformance { get; private set; }

        static Theme()
        {
            Apply(false);
        }

        public static void Initialize()
        {
            string forced = Environment.GetEnvironmentVariable("IMINI_THEME");
            if (string.Equals(forced, "dark", StringComparison.OrdinalIgnoreCase))
            {
                Apply(true);
                return;
            }
            if (string.Equals(forced, "light", StringComparison.OrdinalIgnoreCase))
            {
                Apply(false);
                return;
            }
            Apply(ReadSystemDark());
        }

        public static bool ReadSystemDark()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                if (value == null) return false;
                return Convert.ToInt32(value) == 0;
            }
            catch
            {
                return false;
            }
        }

        public static void ApplySystem()
        {
            bool dark = ReadSystemDark();
            if (dark != IsDark) Apply(dark);
        }

        public static void Apply(bool dark)
        {
            IsDark = dark;
            if (dark)
            {
                Bg = Color.FromArgb(9, 13, 19);
                Bg2 = Color.FromArgb(12, 18, 26);
                Panel = Color.FromArgb(16, 23, 32);
                PanelHover = Color.FromArgb(21, 30, 41);
                Border = Color.FromArgb(30, 43, 56);
                BorderSoft = Color.FromArgb(23, 34, 45);
                Text = Color.FromArgb(233, 240, 247);
                Muted = Color.FromArgb(130, 148, 166);
                Faint = Color.FromArgb(86, 105, 123);
                Green = Color.FromArgb(61, 220, 151);
                Blue = Color.FromArgb(77, 155, 255);
                Orange = Color.FromArgb(255, 122, 69);
                Red = Color.FromArgb(255, 90, 90);
                Yellow = Color.FromArgb(242, 190, 80);
                AccentSilent = Color.FromArgb(55, 214, 168);
                AccentBalanced = Color.FromArgb(77, 155, 255);
                AccentPerformance = Color.FromArgb(255, 107, 69);
            }
            else
            {
                Bg = Color.FromArgb(244, 246, 249);
                Bg2 = Color.FromArgb(234, 238, 244);
                Panel = Color.FromArgb(255, 255, 255);
                PanelHover = Color.FromArgb(242, 246, 251);
                Border = Color.FromArgb(214, 222, 231);
                BorderSoft = Color.FromArgb(229, 234, 241);
                Text = Color.FromArgb(24, 34, 46);
                Muted = Color.FromArgb(92, 106, 122);
                Faint = Color.FromArgb(139, 151, 165);
                Green = Color.FromArgb(16, 150, 112);
                Blue = Color.FromArgb(37, 99, 235);
                Orange = Color.FromArgb(219, 84, 42);
                Red = Color.FromArgb(209, 67, 67);
                Yellow = Color.FromArgb(176, 116, 26);
                AccentSilent = Color.FromArgb(12, 138, 108);
                AccentBalanced = Color.FromArgb(37, 99, 235);
                AccentPerformance = Color.FromArgb(209, 74, 32);
            }
        }

        public static void ApplyWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int dark = IsDark ? 1 : 0;
                Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                int corner = 2;
                Native.DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
                int border = ColorTranslator.ToWin32(IsDark ? Bg : Border);
                Native.DwmSetWindowAttribute(hwnd, 34, ref border, 4);
            }
            catch
            {
            }
        }

        public static int P(float v)
        {
            return (int)Math.Round(v * S);
        }

        public static float SW(float v)
        {
            return Math.Max(1f, v * S);
        }

        public static Color Blend(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0)
            {
                p.AddRectangle(r);
                return p;
            }
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Font Ui(float pt, bool bold)
        {
            string key = "u" + pt.ToString("0.##") + (bold ? "b" : "r");
            Font f;
            if (!Fonts.TryGetValue(key, out f))
            {
                f = new Font("Microsoft YaHei UI", pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
                Fonts[key] = f;
            }
            return f;
        }

        public static Font Num(float pt, bool bold)
        {
            string key = "n" + pt.ToString("0.##") + (bold ? "b" : "r");
            Font f;
            if (!Fonts.TryGetValue(key, out f))
            {
                f = new Font("Segoe UI", pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
                Fonts[key] = f;
            }
            return f;
        }

        public static void DrawText(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPadding);
        }

        public static readonly TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        public static readonly TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        public static readonly TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        public static readonly TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
    }
}
