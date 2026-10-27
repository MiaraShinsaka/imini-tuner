using System;
using System.Globalization;
using System.Text;

namespace ImimiCC
{
    /// <summary>
    /// Tiny JSON bridge used by the Tauri UI.  All firmware access stays in the
    /// same, already-probed C# WMI code (src/PowerSwitch.cs).
    /// </summary>
    internal static class HelperProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "--status")
                {
                    return PrintStatus(PowerSwitch.GetStatus(), true);
                }
                if (args.Length == 2 && args[0] == "--set")
                {
                    int mode;
                    if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out mode) ||
                        mode < 0 || mode > 2)
                    {
                        return PrintError("invalid mode");
                    }
                    int result = PowerSwitch.SetMode(mode);
                    if (result != 0)
                    {
                        return PrintError("SetPowerMode returned " + result);
                    }
                    return PrintStatus(PowerSwitch.GetStatus(), true);
                }
                if (args.Length == 2 && args[0] == "--fan")
                {
                    int duty;
                    if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out duty) ||
                        duty < 0 || duty > 101)
                    {
                        return PrintError("invalid fan duty");
                    }
                    int result = PowerSwitch.SetFan(duty, 1);
                    if (result != 0)
                    {
                        return PrintError("SetFanControl returned " + result);
                    }
                    return PrintStatus(PowerSwitch.GetStatus(false), true);
                }
                if (args.Length == 1 && args[0] == "--serve")
                {
                    return Serve();
                }
                if (args.Length == 1 && args[0] == "--diagnostics")
                {
                    Console.OutputEncoding = new UTF8Encoding(false);
                    Console.Write(PowerSwitch.Diagnostics());
                    return 0;
                }
                return PrintError("usage: --status | --set 0|1|2 | --fan 0..101 | --serve | --diagnostics");
            }
            catch (Exception ex)
            {
                return PrintError(ex.Message);
            }
        }

        /// <summary>
        /// Line-oriented long-lived bridge.  Keeping the WMI connection warm
        /// avoids a process launch and WMI scope connect for every fan tick.
        /// </summary>
        private static int Serve()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                line = line.Trim();
                try
                {
                    if (line == "status" || line == "temp")
                    {
                        PrintStatus(PowerSwitch.GetStatus(false), true);
                    }
                    else if (line == "cpu")
                    {
                        PrintStatus(PowerSwitch.GetStatus(true), true);
                    }
                    else if (line == "quit")
                    {
                        return 0;
                    }
                    else if (line.StartsWith("mode ", StringComparison.Ordinal))
                    {
                        int mode;
                        if (!int.TryParse(line.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out mode) ||
                            mode < 0 || mode > 2)
                        {
                            PrintError("invalid mode");
                            continue;
                        }
                        int result = PowerSwitch.SetMode(mode);
                        if (result != 0)
                        {
                            PrintError("SetPowerMode returned " + result);
                            continue;
                        }
                        PrintStatus(PowerSwitch.GetStatus(false), true);
                    }
                    else if (line.StartsWith("fan ", StringComparison.Ordinal))
                    {
                        int duty;
                        if (!int.TryParse(line.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out duty) ||
                            duty < 0 || duty > 101)
                        {
                            PrintError("invalid fan duty");
                            continue;
                        }
                        int result = PowerSwitch.SetFan(duty, 1);
                        if (result != 0)
                        {
                            PrintError("SetFanControl returned " + result);
                            continue;
                        }
                        PrintStatus(PowerSwitch.GetStatus(false), true);
                    }
                    else
                    {
                        PrintError("unknown command");
                    }
                }
                catch (Exception ex)
                {
                    PrintError(ex.Message);
                }
            }
            return 0;
        }

        private static int PrintStatus(PowerStatus s, bool ok)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"ok\":").Append(ok ? "true" : "false").Append(',');
            sb.Append("\"connected\":").Append(s.Connected ? "true" : "false").Append(',');
            sb.Append("\"error\":\"").Append(Escape(s.Error)).Append("\",");
            sb.Append("\"mode\":").Append(s.CurrentMode).Append(',');
            sb.Append("\"modeName\":\"").Append(Escape(s.ModeName)).Append("\",");
            sb.Append("\"temperature\":").Append(s.Temperature).Append(',');
            sb.Append("\"fanRpm\":").Append(s.FanRpm).Append(',');
            sb.Append("\"cpuName\":\"").Append(Escape(s.CpuName)).Append("\",");
            sb.Append("\"cpuLoad\":").Append(s.CpuLoad).Append(',');
            sb.Append("\"clockMhz\":").Append(s.CpuClockMhz).Append(',');
            sb.Append("\"modeCount\":").Append(s.ModeCount).Append(',');
            sb.Append("\"fanCount\":").Append(s.FanCount).Append(',');
            sb.Append("\"feature1\":").Append(s.Feature1).Append(',');
            sb.Append("\"systemManufacturer\":\"").Append(Escape(s.SystemManufacturer)).Append("\",");
            sb.Append("\"systemModel\":\"").Append(Escape(s.SystemModel)).Append("\",");
            sb.Append("\"boardProduct\":\"").Append(Escape(s.BoardProduct)).Append("\"");
            sb.Append('}');
            Write(sb.ToString());
            return s.Connected ? 0 : 2;
        }

        private static int PrintError(string message)
        {
            Write("{\"ok\":false,\"connected\":false,\"error\":\"" + Escape(message) + "\"}");
            return 1;
        }

        private static void Write(string json)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            using (System.IO.Stream stdout = Console.OpenStandardOutput())
            {
                stdout.Write(bytes, 0, bytes.Length);
                stdout.WriteByte(10);
                stdout.Flush();
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            StringBuilder sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
