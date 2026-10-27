using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BatteryTray
{
    internal sealed class Settings
    {
        public const int DefaultIntervalMinutes = 5;
        private static readonly int[] AllowedIntervals = { 1, 5, 10 };

        public int IntervalMinutes = DefaultIntervalMinutes;
        public bool Autostart = true;
        public bool FirstRun = true;
        public IconStyle Style = IconStyle.Split;
        public IconLook Look = IconLook.Number;
        public bool DarkMode = false;
        // RK 键盘的"满电基准"：接收器报的这个数值当作 100%。0 = 不换算，原样显示。
        public int RkFullValue = 0;

        private static string DirectoryPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "2.4G电量");
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(DirectoryPath, "settings.ini"); }
        }

        public static Settings Load()
        {
            Settings settings = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                {
                    return settings;
                }
                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (string.Equals(key, "interval", StringComparison.OrdinalIgnoreCase))
                    {
                        int minutes;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
                        {
                            settings.IntervalMinutes = NormalizeInterval(minutes);
                        }
                    }
                    else if (string.Equals(key, "autostart", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.Autostart = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(key, "firstrun", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.FirstRun = value != "0" && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(key, "style", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.Style = string.Equals(value, "single", StringComparison.OrdinalIgnoreCase)
                            ? IconStyle.Single
                            : IconStyle.Split;
                    }
                    else if (string.Equals(key, "dark", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.DarkMode = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(key, "look", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.Look = string.Equals(value, "plain", StringComparison.OrdinalIgnoreCase)
                            ? IconLook.Plain
                            : IconLook.Number;
                    }
                    else if (string.Equals(key, "rkfull", StringComparison.OrdinalIgnoreCase))
                    {
                        int full;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out full))
                        {
                            settings.RkFullValue = full > 0 ? full : 0;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 配置损坏时回退到默认值
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 2.4G 设备电量显示器配置");
                sb.AppendLine("interval=" + IntervalMinutes.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("autostart=" + (Autostart ? "1" : "0"));
                sb.AppendLine("firstrun=" + (FirstRun ? "1" : "0"));
                sb.AppendLine("style=" + (Style == IconStyle.Single ? "single" : "split"));
                sb.AppendLine("look=" + (Look == IconLook.Plain ? "plain" : "number"));
                sb.AppendLine("dark=" + (DarkMode ? "1" : "0"));
                sb.AppendLine("# RK 键盘：把接收器报的这个数值当作 100%（0 = 不换算，原样显示）");
                sb.AppendLine("rkfull=" + RkFullValue.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }

        public static int NormalizeInterval(int minutes)
        {
            for (int i = 0; i < AllowedIntervals.Length; i++)
            {
                if (AllowedIntervals[i] == minutes)
                {
                    return minutes;
                }
            }
            return DefaultIntervalMinutes;
        }

        public static IList<int> Intervals
        {
            get { return Array.AsReadOnly(AllowedIntervals); }
        }
    }
}
