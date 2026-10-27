using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryTray
{
    internal static class Probe
    {
        public static void Run()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("2.4G 电量显示器 · 诊断输出");
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("系统：" + Environment.OSVersion.VersionString);
            sb.AppendLine();

            sb.AppendLine("== HID 接口 ==");
            List<HidInterface> interfaces = HidEnum.Enumerate();
            for (int i = 0; i < interfaces.Count; i++)
            {
                sb.AppendLine(interfaces[i].ToString());
                sb.AppendLine("  " + interfaces[i].Path);
            }
            sb.AppendLine();

            sb.AppendLine("== 电量读取 ==");
            for (int i = 0; i < interfaces.Count; i++)
            {
                HidInterface iface = interfaces[i];
                if (iface.VendorId != 0x046D || iface.UsagePage != 0xFF00 || iface.Usage != 0x0002)
                {
                    continue;
                }
                sb.AppendLine("罗技接口：" + iface.ToString());
                LogitechHidpp link = LogitechHidpp.Open(iface);
                if (link == null)
                {
                    sb.AppendLine("  打开失败，Win32 错误码：" + Marshal.GetLastWin32Error());
                    continue;
                }
                try
                {
                    List<byte> indexes = link.DetectDevices(1000);
                    sb.AppendLine("  检测到设备索引数：" + indexes.Count);
                    for (int k = 0; k < indexes.Count; k++)
                    {
                        byte index = indexes[k];
                        int millivolts;
                        byte flags;
                        string raw;
                        string error;
                        bool ok = link.TryReadBattery(index, out millivolts, out flags, out raw, out error);
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "  index={0} ok={1} mv={2} flags=0x{3:X2} raw={4} err={5}",
                            index, ok, millivolts, flags, raw, error));
                    }
                }
                finally
                {
                    link.Dispose();
                }
            }
            sb.AppendLine();

            sb.AppendLine("== 索尼 DS5 手柄 ==");
            for (int i = 0; i < interfaces.Count; i++)
            {
                HidInterface iface = interfaces[i];
                if (!DualSenseHid.IsDualSense(iface))
                {
                    continue;
                }
                sb.AppendLine("DS5 接口：" + iface.ToString());
                sb.AppendLine("  " + iface.Path);
                DualSenseHid link = DualSenseHid.Open(iface);
                if (link == null)
                {
                    sb.AppendLine("  打开失败，Win32 错误码：" + Marshal.GetLastWin32Error());
                    continue;
                }
                try
                {
                    int percent;
                    bool charging;
                    byte batteryByte;
                    string raw;
                    string error;
                    bool ok = link.TryReadBattery(out percent, out charging, out batteryByte, out raw, out error);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  连接={0} ok={1} 电量={2}% 充电={3} 电量字节=0x{4:X2}",
                        link.Wired ? "有线" : "蓝牙", ok, percent, charging, batteryByte));
                    sb.AppendLine("  原始：" + raw);
                    if (!ok)
                    {
                        sb.AppendLine("  错误：" + error);
                    }
                }
                finally
                {
                    link.Dispose();
                }
            }
            sb.AppendLine();

            sb.AppendLine("== 汇总 ==");
            List<DeviceReading> readings = DeviceMonitor.ScanOnce();
            if (readings.Count == 0)
            {
                sb.AppendLine("未检测到可读取电量的设备。");
            }
            for (int i = 0; i < readings.Count; i++)
            {
                DeviceReading reading = readings[i];
                sb.AppendLine(reading.Name + "  [" + reading.Key + "]");
                sb.AppendLine("  百分比：" + (reading.Percent.HasValue ? reading.Percent.Value + "%" : "-"));
                sb.AppendLine("  电压：" + (reading.Millivolts > 0 ? reading.Millivolts + " mV" : "-"));
                sb.AppendLine("  状态：" + reading.State.ToString());
                sb.AppendLine("  在线：" + (reading.Online ? "是" : "否"));
                sb.AppendLine("  原始：" + (string.IsNullOrEmpty(reading.Raw) ? "-" : reading.Raw));
                if (!string.IsNullOrEmpty(reading.Error))
                {
                    sb.AppendLine("  错误：" + reading.Error);
                }
                sb.AppendLine();
            }

            string text = sb.ToString();
            WriteConsole(text);
            WriteFile(text);
        }

        public static void SelfTest()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("2.4G 电量显示器 · 稳定性自测");
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine();
            int runs = 8;
            int logitechOk = 0;
            int rkOk = 0;
            for (int i = 0; i < runs; i++)
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                List<DeviceReading> list = DeviceMonitor.ScanOnce();
                long ms = watch.ElapsedMilliseconds;
                bool logitech = false;
                bool rk = false;
                string detail = string.Empty;
                for (int k = 0; k < list.Count; k++)
                {
                    DeviceReading reading = list[k];
                    string percent = reading.Percent.HasValue
                        ? reading.Percent.Value.ToString(CultureInfo.InvariantCulture) + "%"
                        : "-";
                    if (reading.Kind == DeviceKind.Logitech)
                    {
                        logitech = true;
                        detail += " 罗技=" + percent;
                    }
                    else if (reading.Kind == DeviceKind.Rk)
                    {
                        rk = true;
                        detail += " RK=" + percent;
                    }
                }
                if (logitech)
                {
                    logitechOk++;
                }
                if (rk)
                {
                    rkOk++;
                }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "#{0} 用时={1} ms 罗技={2} RK={3}{4}", i + 1, ms, logitech, rk, detail));
                System.Threading.Thread.Sleep(1200);
            }
            sb.AppendLine();
            sb.AppendLine("罗技成功 " + logitechOk + "/" + runs + "，RK 成功 " + rkOk + "/" + runs);
            WriteConsole(sb.ToString());
            WriteFile(sb.ToString());
        }

        public static void DumpIcons()
        {
            string dir = Path.Combine(Path.GetTempPath(), "2.4G-battery-icons");
            Directory.CreateDirectory(dir);
            int[] sizes = new int[] { 16, 20, 24, 32, 48 };
            int[] percents = new int[] { 5, 30, 66, 78, 100 };
            for (int i = 0; i < percents.Length; i++)
            {
                DeviceReading reading = new DeviceReading();
                reading.Kind = DeviceKind.Logitech;
                reading.Name = "test";
                reading.Online = true;
                reading.State = BatteryState.Discharging;
                reading.Percent = percents[i];
                for (int k = 0; k < sizes.Length; k++)
                {
                    using (Bitmap bmp = BatteryIcon.CreateBitmap(reading, sizes[k], IconLook.Number))
                    {
                        string file = Path.Combine(dir, "p" + percents[i] + "_" + sizes[k] + ".png");
                        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    using (Bitmap plain = BatteryIcon.CreateBitmap(reading, sizes[k], IconLook.Plain))
                    {
                        string file = Path.Combine(dir, "plain_" + percents[i] + "_" + sizes[k] + ".png");
                        plain.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
            }
            DeviceReading charging = new DeviceReading();
            charging.Online = true;
            charging.State = BatteryState.Charging;
            charging.Percent = null;
            for (int k = 0; k < sizes.Length; k++)
            {
                using (Bitmap bmp = BatteryIcon.CreateBitmap(charging, sizes[k], IconLook.Number))
                {
                    bmp.Save(Path.Combine(dir, "charging_" + sizes[k] + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                using (Bitmap plain = BatteryIcon.CreateBitmap(charging, sizes[k], IconLook.Plain))
                {
                    plain.Save(Path.Combine(dir, "plain_charging_" + sizes[k] + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            WriteConsole("icons written to " + dir + "\r\n");
        }

        private static void WriteConsole(string text)
        {            try
            {
                if (!Native.AttachConsole(-1))
                {
                    Native.AllocConsole();
                }
                using (StreamWriter writer = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8))
                {
                    writer.AutoFlush = true;
                    writer.Write(text);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void WriteFile(string text)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "2.4G电量-probe.txt");
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }
    }
}
