using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ImimiCC
{
    internal sealed class AppSettings
    {
        public bool CloseToTray = true;
        public bool ShowTray = true;
        public bool HotkeyEnabled = true;
        public int HotkeyModifiers = 0x0003; // MOD_CONTROL | MOD_ALT
        public int HotkeyKey = (int)Keys.M;

        public static string FilePath
        {
            get
            {
#if PORTABLE
                return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "imini-settings.ini");
#else
                return Path.Combine(Log.DirectoryPath, "settings.ini");
#endif
            }
        }

        public string HotkeyText
        {
            get
            {
                if (!HotkeyEnabled) return "未启用";
                return FormatHotkey(HotkeyModifiers, HotkeyKey);
            }
        }

        public AppSettings Clone()
        {
            AppSettings s = new AppSettings();
            s.CloseToTray = CloseToTray;
            s.ShowTray = ShowTray;
            s.HotkeyEnabled = HotkeyEnabled;
            s.HotkeyModifiers = HotkeyModifiers;
            s.HotkeyKey = HotkeyKey;
            return s;
        }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (string.Equals(key, "closeToTray", StringComparison.OrdinalIgnoreCase)) s.CloseToTray = ParseBool(value, true);
                    else if (string.Equals(key, "showTray", StringComparison.OrdinalIgnoreCase)) s.ShowTray = ParseBool(value, true);
                    else if (string.Equals(key, "hotkeyEnabled", StringComparison.OrdinalIgnoreCase)) s.HotkeyEnabled = ParseBool(value, true);
                    else if (string.Equals(key, "hotkeyModifiers", StringComparison.OrdinalIgnoreCase)) s.HotkeyModifiers = ParseInt(value, 0x0003);
                    else if (string.Equals(key, "hotkeyKey", StringComparison.OrdinalIgnoreCase)) s.HotkeyKey = ParseInt(value, (int)Keys.M);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Settings load failed: " + ex.Message);
            }
            s.Validate();
            return s;
        }

        public bool Save()
        {
            Validate();
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# imini Control Center settings");
                sb.AppendLine("closeToTray=" + (CloseToTray ? "1" : "0"));
                sb.AppendLine("showTray=" + (ShowTray ? "1" : "0"));
                sb.AppendLine("hotkeyEnabled=" + (HotkeyEnabled ? "1" : "0"));
                sb.AppendLine("hotkeyModifiers=" + HotkeyModifiers.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("hotkeyKey=" + HotkeyKey.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Settings save failed: " + ex.Message);
                return false;
            }
        }

        public void Validate()
        {
            if (!ShowTray) CloseToTray = false;
            if (HotkeyModifiers < 0) HotkeyModifiers = 0;
            HotkeyModifiers &= 0x000F;
            if ((HotkeyModifiers & 0x0003) == 0)
            {
                HotkeyModifiers = 0x0003;
                if (HotkeyKey == 0) HotkeyKey = (int)Keys.M;
            }
            if (HotkeyKey <= 0 || HotkeyKey > 0xFE) HotkeyKey = (int)Keys.M;
        }

        public static string FormatHotkey(int modifiers, int key)
        {
            StringBuilder sb = new StringBuilder();
            if ((modifiers & 0x0002) != 0) sb.Append("Ctrl + ");
            if ((modifiers & 0x0001) != 0) sb.Append("Alt + ");
            if ((modifiers & 0x0004) != 0) sb.Append("Shift + ");
            if ((modifiers & 0x0008) != 0) sb.Append("Win + ");
            sb.Append(KeyName(key));
            return sb.ToString();
        }

        public static string KeyName(int key)
        {
            Keys k = (Keys)key;
            if (key >= 0x30 && key <= 0x39) return ((char)key).ToString();
            if (key >= 0x41 && key <= 0x5A) return ((char)key).ToString();
            if (key >= 0x70 && key <= 0x7B) return "F" + (key - 0x6F).ToString(CultureInfo.InvariantCulture);
            switch (k)
            {
                case Keys.Space: return "Space";
                case Keys.Tab: return "Tab";
                case Keys.Enter: return "Enter";
                case Keys.Escape: return "Esc";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Insert: return "Insert";
                case Keys.Delete: return "Delete";
                default: return k.ToString();
            }
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)) return false;
            bool b;
            if (bool.TryParse(value, out b)) return b;
            return fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            int n;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return fallback;
        }
    }
}
