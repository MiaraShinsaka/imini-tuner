using System;
using System.Globalization;
using System.Management;

namespace ImimiCC
{
    internal enum PowerModeKind
    {
        Balanced = 0,
        Performance = 1,
        Silent = 2
    }

    internal sealed class PowerStatus
    {
        public bool Connected;
        public string Error = "";
        public int ModeCount = 3;
        public int CurrentMode = -1;
        public int Temperature = -1;
        public int FanRpm = -1;
        public int Fan2Rpm = -1;
        public int CpuLoad = -1;
        public int CpuClockMhz = -1;
        public string CpuName = "";
        public string SystemManufacturer = "";
        public string SystemModel = "";
        public string BoardProduct = "";
        public int Feature1 = -1;
        public int FanCount = 1;

        public string ModeName
        {
            get
            {
                switch (CurrentMode)
                {
                    case 0: return "均衡模式";
                    case 1: return "狂暴模式";
                    case 2: return "静音模式";
                    default: return "未知模式";
                }
            }
        }
    }

    internal static class PowerSwitch
    {
        private const string WmiNamespace = @"\\.\root\WMI";
        private const string InstancePath = "PowerSwitchInterface.InstanceName=\"ACPI\\\\PNP0C14\\\\IP3POWERSWITCH_0\"";

        private static ManagementScope _scope;
        private static string _cpuName;
        private static string _systemManufacturer;
        private static string _systemModel;
        private static string _boardProduct;
        private static bool _systemInfoLoaded;
        private static readonly object _sync = new object();

        private static ManagementScope Scope()
        {
            if (_scope == null)
            {
                ManagementScope s = new ManagementScope(WmiNamespace);
                s.Connect();
                _scope = s;
            }
            return _scope;
        }

        private static void ResetScope()
        {
            try { _scope = null; } catch { }
        }

        private static ManagementObject OpenInterface()
        {
            ManagementScope scope = Scope();
            ManagementObject o = new ManagementObject(scope, new ManagementPath(InstancePath), null);
            o.Get();
            return o;
        }

        private static ManagementBaseObject Invoke(ManagementObject o, string method, params object[] pairs)
        {
            ManagementBaseObject inParams = o.GetMethodParameters(method);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                inParams[(string)pairs[i]] = pairs[i + 1];
            }
            return o.InvokeMethod(method, inParams, null);
        }

        private static int Int(object value)
        {
            if (value == null) return -1;
            try { return Convert.ToInt32(value); }
            catch { return -1; }
        }

        private static int Feature(ManagementObject o, byte id)
        {
            ManagementBaseObject outParams = Invoke(o, "GetFeatureValue",
                "Reserved1", id, "Reserved2", (byte)0, "Reserved3", (byte)0, "Reserved4", (byte)0);
            return Int(outParams["ResultStatus"]);
        }

        public static PowerStatus GetStatus()
        {
            return GetStatus(true);
        }

        public static PowerStatus GetStatus(bool includeCpu)
        {
            PowerStatus s = new PowerStatus();
            lock (_sync)
            {
                try
                {
                    using (ManagementObject o = OpenInterface())
                    {
                        s.Connected = true;
                        s.ModeCount = Feature(o, 2);
                        if (s.ModeCount < 1 || s.ModeCount > 3) s.ModeCount = 3;
                        s.Feature1 = Feature(o, 1);
                        s.FanCount = Feature(o, 3);
                        if (s.FanCount < 1 || s.FanCount > 2) s.FanCount = 1;

                        ManagementBaseObject pm = Invoke(o, "GetPowerMode");
                        s.CurrentMode = Int(pm["CurrentPowerMode"]);

                        try
                        {
                            ManagementBaseObject temp = Invoke(o, "GetHwTemp", "HwTempType", (byte)1);
                            int t = Int(temp["Temp"]);
                            if (t > 0 && t < 130) s.Temperature = t;
                        }
                        catch { }

                        try
                        {
                            ManagementBaseObject fan = Invoke(o, "GetFanControl", "FanNumber", (byte)1);
                            long duty = Convert.ToInt64(fan["FanDuty"]);
                            int rpm = (int)(duty & 0xFFFF);
                            if (rpm > 0 && rpm < 10000) s.FanRpm = rpm;
                            int rpm2 = (int)((duty >> 16) & 0xFFFF);
                            if (rpm2 > 0 && rpm2 < 10000) s.Fan2Rpm = rpm2;
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    s.Connected = false;
                    s.Error = Friendly(ex);
                    ResetScope();
                }
            }
            FillSystemInfo(s);
            if (includeCpu) RefreshCpu(s);
            return s;
        }

        private static void FillSystemInfo(PowerStatus s)
        {
            if (!_systemInfoLoaded)
            {
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                        @"\\.\root\cimv2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                    {
                        foreach (ManagementObject item in searcher.Get())
                        {
                            using (item)
                            {
                                _systemManufacturer = Convert.ToString(item["Manufacturer"], CultureInfo.InvariantCulture);
                                _systemModel = Convert.ToString(item["Model"], CultureInfo.InvariantCulture);
                                break;
                            }
                        }
                    }
                }
                catch { }
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                        @"\\.\root\cimv2", "SELECT Product FROM Win32_BaseBoard"))
                    {
                        foreach (ManagementObject item in searcher.Get())
                        {
                            using (item)
                            {
                                _boardProduct = Convert.ToString(item["Product"], CultureInfo.InvariantCulture);
                                break;
                            }
                        }
                    }
                }
                catch { }
                _systemInfoLoaded = !string.IsNullOrEmpty(_systemModel) || !string.IsNullOrEmpty(_boardProduct);
            }
            s.SystemManufacturer = _systemManufacturer ?? "";
            s.SystemModel = _systemModel ?? "";
            s.BoardProduct = _boardProduct ?? "";
        }

        public static int SetFan(int duty, int fanNumber)
        {
            if (duty < 0 || duty > 101) throw new ArgumentOutOfRangeException("duty");
            if (duty != 101 && duty < 20) throw new ArgumentOutOfRangeException("duty", "风扇转速低于 20% 可能停转");
            if (fanNumber < 1 || fanNumber > 2) throw new ArgumentOutOfRangeException("fanNumber");
            lock (_sync)
            {
                try
                {
                    using (ManagementObject o = OpenInterface())
                    {
                        ManagementBaseObject result = Invoke(o, "SetFanControl",
                            "FanDuty", (byte)duty, "FanNumber", (byte)fanNumber);
                        int status = Int(result["ResultStatus"]);
                        Log.Write("SetFanControl(" + fanNumber + ", " + duty + ") -> " + status);
                        return status;
                    }
                }
                catch (Exception ex)
                {
                    ResetScope();
                    Log.Write("SetFanControl(" + fanNumber + ", " + duty + ") failed: " + ex.Message);
                    throw new PowerSwitchException(Friendly(ex), ex);
                }
            }
        }

        public static int GetTemperature()
        {
            lock (_sync)
            {
                try
                {
                    using (ManagementObject o = OpenInterface())
                    {
                        ManagementBaseObject temp = Invoke(o, "GetHwTemp", "HwTempType", (byte)1);
                        int t = Int(temp["Temp"]);
                        return t > 0 && t < 130 ? t : -1;
                    }
                }
                catch (Exception ex)
                {
                    ResetScope();
                    throw new PowerSwitchException(Friendly(ex), ex);
                }
            }
        }

        public static int SetMode(int mode)
        {
            if (mode < 0 || mode > 2) throw new ArgumentOutOfRangeException("mode");
            lock (_sync)
            {
                try
                {
                    using (ManagementObject o = OpenInterface())
                    {
                        ManagementBaseObject result = Invoke(o, "SetPowerMode", "PowerMode", (byte)mode);
                        int status = Int(result["ResultStatus"]);
                        Log.Write("SetPowerMode(" + mode + ") -> " + status);
                        return status;
                    }
                }
                catch (Exception ex)
                {
                    ResetScope();
                    Log.Write("SetPowerMode(" + mode + ") failed: " + ex.Message);
                    throw new PowerSwitchException(Friendly(ex), ex);
                }
            }
        }

        public static string Diagnostics()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            try
            {
                using (ManagementObject o = OpenInterface())
                {
                    sb.AppendLine("Instance: " + o.Path.Path);
                    for (byte i = 1; i <= 5; i++)
                    {
                        try { sb.AppendLine("Feature(" + i + "): " + Feature(o, i)); }
                        catch (Exception ex) { sb.AppendLine("Feature(" + i + "): " + ex.Message); }
                    }
                    for (byte i = 1; i <= 4; i++)
                    {
                        try
                        {
                            ManagementBaseObject t = Invoke(o, "GetHwTemp", "HwTempType", i);
                            sb.AppendLine("HwTemp(" + i + "): " + Int(t["Temp"]));
                        }
                        catch (Exception ex) { sb.AppendLine("HwTemp(" + i + "): " + ex.Message); }
                    }
                    try
                    {
                        ManagementBaseObject fan = Invoke(o, "GetFanControl", "FanNumber", (byte)1);
                        sb.AppendLine("FanDuty(1): " + Convert.ToInt64(fan["FanDuty"]));
                    }
                    catch (Exception ex) { sb.AppendLine("Fan(1): " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR: " + ex.Message);
            }
            return sb.ToString();
        }

        private static void RefreshCpu(PowerStatus s)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"root\CIMV2", "SELECT Name, LoadPercentage, CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        using (mo)
                        {
                            if (_cpuName == null)
                            {
                                object n = mo["Name"];
                                _cpuName = n == null ? "" : n.ToString().Trim();
                            }
                            int load = Int(mo["LoadPercentage"]);
                            if (load >= 0) s.CpuLoad = load;
                            int clk = Int(mo["CurrentClockSpeed"]);
                            if (clk > 0) s.CpuClockMhz = clk;
                            break;
                        }
                    }
                }
            }
            catch { }
            s.CpuName = _cpuName == null ? "" : _cpuName;
        }

        private static string Friendly(Exception ex)
        {
            string msg = ex.Message == null ? ex.GetType().Name : ex.Message;
            if (ex is UnauthorizedAccessException || msg.IndexOf("拒绝访问", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("0x80070005", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "需要管理员权限：请以管理员身份运行本程序";
            }
            if (msg.IndexOf("Invalid class", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("无效类", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "此机型未提供 PowerSwitchInterface 接口";
            }
            if (msg.IndexOf("Invalid method", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("无效方法", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "固件接口不支持此操作（可能是 BIOS 版本过旧）";
            }
            return msg;
        }
    }

    internal sealed class PowerSwitchException : Exception
    {
        public PowerSwitchException(string message, Exception inner) : base(message, inner) { }
    }
}
