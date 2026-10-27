using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ImimiCC
{
    internal static class Program
    {
        public static int ShowMessage;
        public static string MenuShotPath;

        [STAThread]
        private static int Main(string[] args)
        {
            string forcedTheme = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--theme", StringComparison.OrdinalIgnoreCase)) forcedTheme = args[i + 1];
            }
            if (string.Equals(forcedTheme, "dark", StringComparison.OrdinalIgnoreCase)) Theme.Apply(true);
            else if (string.Equals(forcedTheme, "light", StringComparison.OrdinalIgnoreCase)) Theme.Apply(false);
            else Theme.Initialize();
            Theme.S = DetectScale();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool status = false;
            bool diagnostics = false;
            bool minimized = false;
            bool selftest = false;
            string selftestDir = null;
            string aboutShot = null;
            string settingsShot = null;
            string toastShot = null;
            int mode = -1;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--status" || a == "-s") status = true;
                else if (a == "--diagnostics" || a == "--diag") diagnostics = true;
                else if (a == "--minimized" || a == "-m") minimized = true;
                else if (a == "--selftest")
                {
                    selftest = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) selftestDir = args[++i];
                }
                else if (a == "--menushot")
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) MenuShotPath = args[++i];
                }
                else if (a == "--aboutshot")
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) aboutShot = args[++i];
                }
                else if (a == "--settingshot")
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) settingsShot = args[++i];
                }
                else if (a == "--toastshot")
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) toastShot = args[++i];
                }
                else if (a == "--silent") mode = 2;
                else if (a == "--balanced") mode = 0;
                else if (a == "--performance" || a == "--turbo") mode = 1;
                else if (a == "--mode" && i + 1 < args.Length)
                {
                    mode = ParseMode(args[++i]);
                }
                else if (a.StartsWith("--mode="))
                {
                    mode = ParseMode(a.Substring(7));
                }
            }

            bool cli = status || diagnostics || mode >= 0 || selftest || MenuShotPath != null;
            if (aboutShot != null) { CaptureAbout(aboutShot); return 0; }
            if (settingsShot != null) { CaptureSettings(settingsShot); return 0; }
            if (toastShot != null) { CaptureToast(toastShot); return 0; }
            if (cli && !IsAdmin())
            {
                Relaunch(args);
                return 0;
            }
            if (selftest)
            {
                return RunSelfTest(selftestDir ?? Path.Combine(Log.DirectoryPath, "selftest"));
            }
            if (cli)
            {
                return RunCli(status, diagnostics, mode);
            }

            if (!IsAdmin())
            {
                Relaunch(args);
                return 1;
            }

            Log.Write("==== imini Control Center start, pid=" + Process.GetCurrentProcess().Id + " ====");
            ShowMessage = Native.RegisterWindowMessage("iminiCC_ShowWindow");

            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\iminiCC.ControlCenter", out created))
            {
                if (!created)
                {
                    if (ShowMessage != 0)
                    {
                        Native.PostMessage(Native.HwndBroadcast, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    }
                    return 0;
                }
                Application.Run(new MainForm(minimized));
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        private static int ParseMode(string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            string v = value.Trim().ToLowerInvariant();
            if (v == "0" || v == "balanced" || v == "normal" || v == "均衡") return 0;
            if (v == "1" || v == "performance" || v == "turbo" || v == "狂暴") return 1;
            if (v == "2" || v == "silent" || v == "quiet" || v == "静音") return 2;
            return -1;
        }

        private static float DetectScale()
        {
            try
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / 96f;
                }
            }
            catch
            {
                return 1f;
            }
        }

        private static bool IsAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void Relaunch(string[] args)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(' ');
                    if (args[i].IndexOf(' ') >= 0) sb.Append('"').Append(args[i]).Append('"');
                    else sb.Append(args[i]);
                }
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, sb.ToString());
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223)
                {
                    MessageBox.Show("需要管理员权限才能控制 CPU 模式。\r\n\r\n" + ex.Message,
                        "imini 控制中心", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法以管理员权限启动：\r\n" + ex.Message,
                    "imini 控制中心", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static int RunCli(bool status, bool diagnostics, int mode)
        {
            string dir = Log.DirectoryPath;
            try
            {
                Directory.CreateDirectory(dir);
                if (!string.IsNullOrEmpty(MenuShotPath))
                {
                    PowerStatus preview = PowerSwitch.GetStatus();
                    return CaptureForm(preview, MenuShotPath) ? 0 : 1;
                }
                if (diagnostics)
                {
                    string text = PowerSwitch.Diagnostics();
                    File.WriteAllText(Path.Combine(dir, "diagnostics.txt"), text, new UTF8Encoding(false));
                    Log.Write("CLI diagnostics written");
                    return 0;
                }
                if (mode >= 0)
                {
                    int result = PowerSwitch.SetMode(mode);
                    Thread.Sleep(150);
                    PowerStatus st = PowerSwitch.GetStatus();
                    WriteResult("mode", result, st);
                    Log.Write("CLI set mode " + mode + " result=" + result);
                    return st.Connected && st.CurrentMode == mode ? 0 : 2;
                }
                PowerStatus s = PowerSwitch.GetStatus();
                WriteResult("status", 0, s);
                return s.Connected ? 0 : 3;
            }
            catch (Exception ex)
            {
                Log.Write("CLI failed: " + ex);
                WriteError(ex.Message);
                return 1;
            }
        }

        private static int RunSelfTest(string dir)
        {
            Directory.CreateDirectory(dir);
            StringBuilder report = new StringBuilder();
            int failures = 0;
            PowerStatus before = null;
#if !LITE
            bool autoWasEnabled = Autostart.IsEnabled();
#endif
            report.AppendLine("imini Control Center self-test");
            report.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("admin=" + IsAdmin());
            try
            {
                before = PowerSwitch.GetStatus();
                report.AppendLine("initial.connected=" + before.Connected);
                report.AppendLine("initial.mode=" + before.CurrentMode + " (" + before.ModeName + ")");
                report.AppendLine("initial.temp=" + before.Temperature);
                report.AppendLine("initial.fan1=" + before.FanRpm + " fan2=" + before.Fan2Rpm);
                report.AppendLine("initial.cpu=" + before.CpuName);
                report.AppendLine("initial.error=" + before.Error);
                if (!before.Connected)
                {
                    CaptureForm(before, Path.Combine(dir, "shot-error.png"));
                    report.AppendLine("FATAL: interface not connected");
                    failures++;
                }
                else
                {
                    int[] modes = new int[] { 2, 0, 1 };
                    for (int i = 0; i < modes.Length; i++)
                    {
                        int m = modes[i];
                        int rc;
                        try { rc = PowerSwitch.SetMode(m); }
                        catch (Exception ex)
                        {
                            rc = -1;
                            report.AppendLine("mode " + m + " set EXCEPTION " + ex.Message);
                        }
                        Thread.Sleep(1200);
                        PowerStatus st = PowerSwitch.GetStatus();
                        bool ok = rc == 0 && st.Connected && st.CurrentMode == m;
                        if (!ok) failures++;
                        string shot = Path.Combine(dir, "shot-" + m + ".png");
                        bool captured = CaptureForm(st, shot);
                        report.AppendLine("mode " + m + ": setResult=" + rc + " read=" + st.CurrentMode +
                            " temp=" + st.Temperature + " fan=" + st.FanRpm + " captured=" + captured + " ok=" + ok);
                    }

                    int restore = PowerSwitch.SetMode(before.CurrentMode);
                    Thread.Sleep(800);
                    PowerStatus after = PowerSwitch.GetStatus();
                    bool restored = after.Connected && after.CurrentMode == before.CurrentMode;
                    if (!restored) failures++;
                    report.AppendLine("restore: setResult=" + restore + " read=" + after.CurrentMode + " ok=" + restored);

#if !LITE
                    string err;
                    bool en = Autostart.Enable(out err);
                    bool enRead = Autostart.IsEnabled();
                    bool dis = Autostart.Disable(out err);
                    bool disRead = Autostart.IsEnabled();
                    bool autoOk = en && enRead && dis && !disRead;
                    if (!autoOk) failures++;
                    report.AppendLine("autostart: was=" + autoWasEnabled + " enable=" + en + " enabledRead=" + enRead +
                        " disable=" + dis + " disabledRead=" + disRead + " ok=" + autoOk + " error=" + err);
#endif
                }
            }
            catch (Exception ex)
            {
                failures++;
                report.AppendLine("EXCEPTION: " + ex);
            }
            finally
            {
                if (before != null && before.Connected)
                {
                    try { PowerSwitch.SetMode(before.CurrentMode); } catch { }
                }
#if !LITE
                if (!autoWasEnabled)
                {
                    try
                    {
                        string ignored;
                        Autostart.Disable(out ignored);
                    }
                    catch { }
                }
#endif
            }
            report.AppendLine("failures=" + failures);
            File.WriteAllText(Path.Combine(dir, "selftest.txt"), report.ToString(), new UTF8Encoding(false));
            Log.Write("selftest failures=" + failures);
            return failures == 0 ? 0 : 2;
        }

        private static bool CaptureAbout(string path)
        {
            try
            {
                PowerStatus st = new PowerStatus();
                st.Connected = true;
                st.CurrentMode = 2;
                st.Temperature = 55;
                st.FanRpm = 1339;
                using (AboutForm f = new AboutForm(st))
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(-4000, -4000);
                    f.ShowInTaskbar = false;
                    f.Show();
                    Application.DoEvents();
                    Thread.Sleep(150);
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(path, ImageFormat.Png);
                    }
                    f.Close();
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("CaptureAbout failed: " + ex);
                return false;
            }
        }

        private static bool CaptureSettings(string path)
        {
            try
            {
                AppSettings s = AppSettings.Load();
                s.HotkeyEnabled = true;
                using (SettingsForm f = new SettingsForm(s))
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(-4000, -4000);
                    f.ShowInTaskbar = false;
                    f.Show();
                    Application.DoEvents();
                    Thread.Sleep(150);
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(path, ImageFormat.Png);
                    }
                    f.Close();
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("CaptureSettings failed: " + ex);
                return false;
            }
        }

        private static bool CaptureToast(string path)
        {
            try
            {
                using (ToastForm f = ToastForm.CreateForCapture("已切换到“静音模式”"))
                {
                    f.ShowInTaskbar = false;
                    f.Show();
                    Application.DoEvents();
                    Thread.Sleep(120);
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(path, ImageFormat.Png);
                    }
                    f.Close();
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("CaptureToast failed: " + ex);
                return false;
            }
        }

        private static bool CaptureForm(PowerStatus st, string path)
        {
            try
            {
                using (MainForm f = new MainForm(false))
                {
                    f.SetPreviewStatus(st);
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(-4000, -4000);
                    f.ShowInTaskbar = false;
                    f.Show();
                    Application.DoEvents();
                    Thread.Sleep(150);
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(path, ImageFormat.Png);
                    }
                    string menuPath = MenuShotPath;
                    if (!string.IsNullOrEmpty(menuPath)) f.CaptureMenu(menuPath);
                    f.ExitNow();
                }
                return File.Exists(path);
            }
            catch (Exception ex)
            {
                Log.Write("CaptureForm failed: " + ex);
                return false;
            }
        }

        private static void WriteResult(string action, int result, PowerStatus s)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"ok\": ").Append(s != null && s.Connected ? "true" : "false").Append(",\r\n");
            sb.Append("  \"action\": \"").Append(action).Append("\",\r\n");
            sb.Append("  \"result\": ").Append(result.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
            if (s != null)
            {
                sb.Append("  \"connected\": ").Append(s.Connected ? "true" : "false").Append(",\r\n");
                sb.Append("  \"mode\": ").Append(s.CurrentMode.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"modeName\": \"").Append(Escape(s.ModeName)).Append("\",\r\n");
                sb.Append("  \"modeCount\": ").Append(s.ModeCount.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"temperature\": ").Append(s.Temperature.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"fanRpm\": ").Append(s.FanRpm.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"cpuLoad\": ").Append(s.CpuLoad.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"cpuClockMhz\": ").Append(s.CpuClockMhz.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
                sb.Append("  \"cpuName\": \"").Append(Escape(s.CpuName)).Append("\",\r\n");
                sb.Append("  \"error\": \"").Append(Escape(s.Error)).Append("\",\r\n");
            }
            sb.Append("  \"time\": \"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\"\r\n");
            sb.Append("}\r\n");
            File.WriteAllText(Path.Combine(Log.DirectoryPath, "cli-result.json"), sb.ToString(), new UTF8Encoding(false));
        }

        private static void WriteError(string message)
        {
            string json = "{\r\n  \"ok\": false,\r\n  \"error\": \"" + Escape(message) + "\"\r\n}\r\n";
            try
            {
                Directory.CreateDirectory(Log.DirectoryPath);
                File.WriteAllText(Path.Combine(Log.DirectoryPath, "cli-result.json"), json, new UTF8Encoding(false));
            }
            catch { }
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
