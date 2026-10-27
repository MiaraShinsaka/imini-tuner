// End-to-end check for the shortcut recorder.  Must run elevated (the app
// manifest requires administrator): it starts the real iMini Tuner, opens
// Settings through UI Automation, injects a physical-equivalent Win+Alt+Space
// through SendInput, and verifies both that the combo was recorded and that
// the Start menu never opened.
//
// usage: hotkey-e2e.exe <path to iMini Tuner.exe> <result.json>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

static class Program
{
    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const ushort VK_LWIN = 0x5B;
    const ushort VK_LMENU = 0x12;
    const ushort VK_SPACE = 0x20;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
        [FieldOffset(8)] public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

    [DllImport("user32.dll")]
    static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);

    [DllImport("user32.dll")]
    static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern IntPtr SetFocus(IntPtr hWnd);

    static readonly List<string> Checks = new List<string>();
    static readonly StringBuilder Diagnostics = new StringBuilder();

    static void Check(string name, bool ok, string detail)
    {
        Checks.Add("{\"name\":\"" + Esc(name) + "\",\"ok\":" + (ok ? "true" : "false") +
                   ",\"detail\":\"" + Esc(detail) + "\"}");
        if (!ok) Diagnostics.AppendLine(name + ": " + detail);
    }

    static string Esc(string value)
    {
        if (value == null) return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
    }

    static string LogPath
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "iminiControlCenter", "app.log");
        }
    }

    static string ReadLogFrom(long offset)
    {
        try
        {
            using (FileStream stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (offset > stream.Length) offset = 0;
                stream.Seek(offset, SeekOrigin.Begin);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
        catch (Exception error)
        {
            return "<log unreadable: " + error.Message + ">";
        }
    }

    static bool WaitFor(Func<bool> condition, int milliseconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            try
            {
                if (condition()) return true;
            }
            catch (Exception)
            {
                // UIA throws while the page is still building; retry.
            }
            Thread.Sleep(100);
        }
        return false;
    }

    static void Key(ushort vk, bool up)
    {
        INPUT input = new INPUT();
        input.type = INPUT_KEYBOARD;
        input.ki.wVk = vk;
        input.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        INPUT[] batch = new INPUT[] { input };
        uint sent = SendInput(1, batch, Marshal.SizeOf(typeof(INPUT)));
        if (sent != 1)
        {
            throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error());
        }
        Thread.Sleep(35);
    }

    static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(80);
        INPUT down = new INPUT();
        down.type = 0;
        down.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
        INPUT up = new INPUT();
        up.type = 0;
        up.mi.dwFlags = MOUSEEVENTF_LEFTUP;
        SendInput(1, new INPUT[] { down }, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(60);
        SendInput(1, new INPUT[] { up }, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(120);
    }

    static AutomationElement WaitForId(AutomationElement root, string id, int milliseconds)
    {
        AutomationElement found = null;
        WaitFor(delegate
        {
            Condition condition = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            found = root.FindFirst(TreeScope.Descendants, condition);
            return found != null;
        }, milliseconds);
        return found;
    }

    static void Invoke(AutomationElement element)
    {
        object pattern;
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
        {
            ((InvokePattern)pattern).Invoke();
            return;
        }
        System.Windows.Rect bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty) throw new InvalidOperationException("element has no clickable bounds");
        ClickAt((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));
    }

    static void Marker(string text)
    {
        try
        {
            long seconds = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            File.AppendAllText(LogPath, "t=" + seconds +
                                        " E2E marker: " + text + Environment.NewLine);
        }
        catch (Exception) { }
    }

    static void DumpButtons(AutomationElement root)
    {
        try
        {
            Condition condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            AutomationElementCollection all = root.FindAll(TreeScope.Descendants, condition);
            for (int i = 0; i < all.Count; i++)
            {
                Diagnostics.AppendLine("button[" + i + "]: name=" + all[i].Current.Name +
                                       " id=" + all[i].Current.AutomationId);
            }
        }
        catch (Exception error)
        {
            Diagnostics.AppendLine("button dump failed: " + error.Message);
        }
    }

    static void Shot(IntPtr hwnd, string path)
    {
        try
        {
            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) return;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return;
            using (Bitmap bitmap = new Bitmap(width, height))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
                }
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch (Exception error)
        {
            Diagnostics.AppendLine("screenshot failed: " + error.Message);
        }
    }

    static string ForegroundClass()
    {        StringBuilder name = new StringBuilder(256);
        GetClassName(GetForegroundWindow(), name, name.Capacity);
        return name.ToString();
    }

    // SetForegroundWindow is unreliable for a process that never received
    // input; attach to the foreground thread and, as a last resort, click the
    // window's own title strip (real input grants the foreground right).
    static bool ForceForeground(IntPtr hwnd)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            ShowWindow(hwnd, 9); // SW_RESTORE
            SwitchToThisWindow(hwnd, true);
            uint ignored;
            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
            uint targetThread = GetWindowThreadProcessId(hwnd, out ignored);
            bool attached = false;
            try
            {
                if (foregroundThread != 0 && targetThread != 0 && foregroundThread != targetThread)
                {
                    attached = AttachThreadInput(foregroundThread, targetThread, true);
                }
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                SetFocus(hwnd);
            }
            finally
            {
                if (attached) AttachThreadInput(foregroundThread, targetThread, false);
            }
            Thread.Sleep(250);
            if (GetForegroundWindow() == hwnd) return true;

            RECT rect;
            if (GetWindowRect(hwnd, out rect))
            {
                ClickAt(rect.Left + (rect.Right - rect.Left) / 2, rect.Top + 18);
                Thread.Sleep(300);
                if (GetForegroundWindow() == hwnd) return true;
            }
        }
        return false;
    }

    static string ReadFieldText(AutomationElement field)
    {
        object pattern;
        if (field.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
        {
            return ((ValuePattern)pattern).Current.Value ?? "";
        }
        return field.Current.Name ?? "";
    }

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: hotkey-e2e.exe <app.exe> <result.json>");
            return 2;
        }
        string exe = Path.GetFullPath(args[0]);
        string resultPath = Path.GetFullPath(args[1]);
        bool ok = false;
        try
        {
            ok = Run(exe);
        }
        catch (Exception error)
        {
            Check("unhandled", false, error.GetType().Name + ": " + error.Message);
        }

        StringBuilder json = new StringBuilder();
        json.Append("{\"ok\":").Append(ok ? "true" : "false").Append(",\"checks\":[");
        json.Append(string.Join(",", Checks.ToArray()));
        json.Append("],\"diagnostics\":\"").Append(Esc(Diagnostics.ToString())).Append("\"}");
        File.WriteAllText(resultPath, json.ToString(), new UTF8Encoding(false));
        return ok ? 0 : 1;
    }

    static bool Run(string exe)
    {
        foreach (Process stale in Process.GetProcessesByName("iMini Tuner"))
        {
            try { stale.Kill(); stale.WaitForExit(3000); } catch (Exception) { }
        }
        Thread.Sleep(400);

        Check("app_exists", File.Exists(exe), exe);
        ProcessStartInfo start = new ProcessStartInfo();
        start.FileName = exe;
        start.WorkingDirectory = Path.GetDirectoryName(exe);
        start.UseShellExecute = false;
        Process app = Process.Start(start);
        Thread.Sleep(1500);
        app.Refresh();
        IntPtr hwnd = app.MainWindowHandle;
        if (hwnd == IntPtr.Zero)
        {
            WaitFor(delegate
            {
                app.Refresh();
                hwnd = app.MainWindowHandle;
                return hwnd != IntPtr.Zero;
            }, 20000);
        }
        Check("main_window", hwnd != IntPtr.Zero, "hwnd=" + hwnd.ToInt64());
        if (hwnd == IntPtr.Zero) return false;

        long logStart = 0;
        try { logStart = new FileInfo(LogPath).Length; } catch (Exception) { }
        Thread.Sleep(1200);

        // Focus the app: the test is only meaningful with its own window in
        // front, otherwise a swallowed key would go nowhere.
        bool foregroundOk = ForceForeground(hwnd);
        Check("app_foreground", foregroundOk,
              "foreground=" + GetForegroundWindow().ToInt64() + " hwnd=" + hwnd.ToInt64() +
              " class=" + ForegroundClass());

        AutomationElement root = AutomationElement.FromHandle(hwnd);
        AutomationElement settingsButton = WaitForId(root, "btn-settings", 15000);
        Check("settings_button", settingsButton != null, "btn-settings");
        if (settingsButton == null) return false;

        // Which field is focused depends on the configured hotkey mode.
        string mode = "cycle";
        try
        {
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                               "iminiControlCenter", "settings.json");
            Match match = Regex.Match(File.ReadAllText(settingsPath), "\"hotkeyMode\"\\s*:\\s*\"([^\"]+)\"");
            if (match.Success) mode = match.Groups[1].Value;
        }
        catch (Exception) { }
        string fieldId = mode == "custom" ? "hotkey-silent" : "hotkey-cycle";
        string savedKey = mode == "custom" ? "hotkeySilent" : "hotkey";

        // The overlay is rendered by the WebView; opening it can take a moment
        // and a single UIA invoke is occasionally swallowed while the page is
        // still hydrating, so retry with a fresh root.
        AutomationElement field = null;
        for (int attempt = 0; attempt < 4 && field == null; attempt++)
        {
            Invoke(settingsButton);
            Thread.Sleep(700);
            try { root = AutomationElement.FromHandle(hwnd); } catch (Exception) { }
            field = WaitForId(root, fieldId, 4000);
        }
        Check("hotkey_field", field != null, fieldId);
        if (field == null)
        {
            Shot(hwnd, Path.Combine(Path.GetDirectoryName(exe), "e2e-nofield.png"));
            DumpButtons(root);
            return false;
        }

        // Getting a DOM focus via UIA and clicking the same field can race:
        // the focus handler starts capture, while a blur stops it again.  Loop
        // until the log ends on "started" with no later "stop requested", then
        // inject immediately.
        bool armed = false;
        for (int attempt = 0; attempt < 10 && !armed; attempt++)
        {
            Marker("focus attempt " + attempt);
            try { field.SetFocus(); } catch (Exception) { }
            Thread.Sleep(300);
            string log = ReadLogFrom(logStart);
            int started = log.LastIndexOf("hotkey capture started", StringComparison.Ordinal);
            int stopped = log.LastIndexOf("hotkey capture stop requested", StringComparison.Ordinal);
            armed = started >= 0 && started > stopped;
        }
        Check("capture_started", armed, "armed=" + armed + " after focus attempts");
        Marker("armed=" + armed);
        Shot(hwnd, Path.Combine(Path.GetDirectoryName(exe), "e2e-settings.png"));
        if (!armed)
        {
            Shot(hwnd, Path.Combine(Path.GetDirectoryName(exe), "e2e-nocapture.png"));
            return false;
        }
        Thread.Sleep(100);

        // The actual chord, delivered through the system input queue exactly
        // like a keyboard: Win down, Alt down, Space, then release.
        Key(VK_LWIN, false);
        Key(VK_LMENU, false);
        Key(VK_SPACE, false);
        Key(VK_SPACE, true);
        Key(VK_LMENU, true);
        Key(VK_LWIN, true);

        bool recorded = WaitFor(delegate
        {
            return ReadLogFrom(logStart).Contains("hotkey capture done: Win+Alt+Space");
        }, 8000);
        string afterKeys = ReadLogFrom(logStart);
        string afterTail = afterKeys.Length > 800 ? afterKeys.Substring(afterKeys.Length - 800) : afterKeys;
        Check("recorded_combo", recorded, "done missing; tail=" + afterTail);

        Thread.Sleep(400);
        IntPtr foreground = GetForegroundWindow();
        string foregroundClass = ForegroundClass();
        bool menuOpen = foreground != hwnd &&
                        (foregroundClass.IndexOf("CoreWindow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         foregroundClass.IndexOf("XamlExplorer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         foregroundClass.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0);
        Check("no_start_menu", !menuOpen, "foreground=" + foreground.ToInt64() + " class=" + foregroundClass);
        Check("app_still_foreground", foreground == hwnd, "foreground=" + foreground.ToInt64() + " hwnd=" + hwnd.ToInt64());

        if (recorded)
        {
            string text = ReadFieldText(field);
            int win = text.IndexOf("Win", StringComparison.OrdinalIgnoreCase);
            int alt = text.IndexOf("Alt", StringComparison.OrdinalIgnoreCase);
            int space = text.IndexOf("Space", StringComparison.OrdinalIgnoreCase);
            bool shown = win >= 0 && alt > win && space > alt;
            Check("field_shows_combo", shown, "field text=" + text);
        }

        // Save and confirm the value persisted as a registrable shortcut.
        AutomationElement save = WaitForId(root, "settings-save", 8000);
        if (save == null)
        {
            DumpButtons(root);
            Condition byName = new PropertyCondition(AutomationElement.NameProperty, "保存设置");
            save = root.FindFirst(TreeScope.Descendants, byName);
        }
        Check("save_button", save != null, "settings-save");
        if (save == null) Shot(hwnd, Path.Combine(Path.GetDirectoryName(exe), "e2e-nosave.png"));
        if (save != null)
        {
            Invoke(save);
            Thread.Sleep(1200);
            try
            {
                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "iminiControlCenter", "settings.json");
                string json = File.ReadAllText(settingsPath);
                bool saved = Regex.IsMatch(json, "\"" + savedKey + "\"\\s*:\\s*\"Super\\+Alt\\+Space\"");
                Check("saved_setting", saved, savedKey + " in settings.json");
            }
            catch (Exception error)
            {
                Check("saved_setting", false, error.Message);
            }
        }

        // The helper must be resident while the app runs...
        bool helperRunning = Process.GetProcessesByName("imini-hotkey-220").Length > 0;
        Check("helper_resident", helperRunning, "imini-hotkey-helper running");

        // ...and must disappear when the app quits (stdin EOF).
        ProcessStartInfo quit = new ProcessStartInfo();
        quit.FileName = exe;
        quit.Arguments = "--quit";
        quit.WorkingDirectory = Path.GetDirectoryName(exe);
        quit.UseShellExecute = false;
        try { Process.Start(quit); } catch (Exception) { }
        bool appGone = WaitFor(delegate { return app.HasExited; }, 8000);
        Thread.Sleep(800);
        bool helperGone = Process.GetProcessesByName("imini-hotkey-220").Length == 0;
        Check("helper_exits_with_app", helperGone, "imini-hotkey-helper gone after quit");
        try { if (!app.HasExited) app.Kill(); } catch (Exception) { }

        bool all = true;
        foreach (string check in Checks)
        {
            if (check.IndexOf("\"ok\":false", StringComparison.Ordinal) >= 0) all = false;
        }
        Diagnostics.AppendLine("appGone=" + appGone);
        return all;
    }
}
