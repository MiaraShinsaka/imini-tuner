using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace ImimiCC
{
    internal static class Autostart
    {
        public const string TaskName = "imini Control Center";

        private static string Run(string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", arguments);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.Default;
            psi.StandardErrorEncoding = Encoding.Default;
            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                if (p.ExitCode != 0)
                {
                    Log.Write("schtasks " + arguments + " -> " + p.ExitCode + " " + output + error);
                }
                return output + error;
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/Query /TN \"" + TaskName + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(8000);
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool Enable(out string error)
        {
            error = "";
            try
            {
                string user = WindowsIdentity.GetCurrent().Name;
                string exe = Application.ExecutablePath;
                string xml = BuildXml(user, exe);
                string temp = Path.Combine(Path.GetTempPath(), "imini_cc_task_" + Guid.NewGuid().ToString("N") + ".xml");
                File.WriteAllText(temp, xml, new UnicodeEncoding(false, true));
                string output = Run("/Create /TN \"" + TaskName + "\" /XML \"" + temp + "\" /F");
                try { File.Delete(temp); } catch { }
                if (!IsEnabled())
                {
                    error = string.IsNullOrEmpty(output.Trim()) ? "创建计划任务失败" : output.Trim();
                    return false;
                }
                Log.Write("Autostart enabled: " + exe);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Write("Autostart enable failed: " + ex);
                return false;
            }
        }

        public static bool Disable(out string error)
        {
            error = "";
            try
            {
                // A missing task is already the desired state; avoid a noisy
                // "system cannot find the file" from schtasks in the log.
                if (IsEnabled())
                {
                    Run("/Delete /TN \"" + TaskName + "\" /F");
                    if (IsEnabled())
                    {
                        error = "删除计划任务失败";
                        return false;
                    }
                }
                Log.Write("Autostart disabled");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string BuildXml(string user, string exe)
        {
            string u = SecurityElement.Escape(user);
            string e = SecurityElement.Escape(exe);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            sb.AppendLine("  <RegistrationInfo>");
            sb.AppendLine("    <Description>imini Control Center 开机自动启动（管理员权限，托盘运行）</Description>");
            sb.AppendLine("  </RegistrationInfo>");
            sb.AppendLine("  <Triggers>");
            sb.AppendLine("    <LogonTrigger>");
            sb.AppendLine("      <Enabled>true</Enabled>");
            sb.AppendLine("      <UserId>" + u + "</UserId>");
            sb.AppendLine("    </LogonTrigger>");
            sb.AppendLine("  </Triggers>");
            sb.AppendLine("  <Principals>");
            sb.AppendLine("    <Principal id=\"Author\">");
            sb.AppendLine("      <UserId>" + u + "</UserId>");
            sb.AppendLine("      <LogonType>InteractiveToken</LogonType>");
            sb.AppendLine("      <RunLevel>HighestAvailable</RunLevel>");
            sb.AppendLine("    </Principal>");
            sb.AppendLine("  </Principals>");
            sb.AppendLine("  <Settings>");
            sb.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            sb.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            sb.AppendLine("    <StartWhenAvailable>false</StartWhenAvailable>");
            sb.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            sb.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            sb.AppendLine("    <Enabled>true</Enabled>");
            sb.AppendLine("    <Hidden>false</Hidden>");
            sb.AppendLine("    <RunOnlyIfIdle>false</RunOnlyIfIdle>");
            sb.AppendLine("    <WakeToRun>false</WakeToRun>");
            sb.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            sb.AppendLine("    <Priority>7</Priority>");
            sb.AppendLine("  </Settings>");
            sb.AppendLine("  <Actions Context=\"Author\">");
            sb.AppendLine("    <Exec>");
            sb.AppendLine("      <Command>" + e + "</Command>");
            sb.AppendLine("      <Arguments>--minimized</Arguments>");
            sb.AppendLine("    </Exec>");
            sb.AppendLine("  </Actions>");
            sb.AppendLine("</Task>");
            return sb.ToString();
        }
    }
}
