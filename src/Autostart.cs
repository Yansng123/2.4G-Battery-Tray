using System;
using Microsoft.Win32;

namespace BatteryTray
{
    internal static class Autostart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "2.4G设备电量显示";

        public static string ExecutablePath
        {
            get
            {
                try
                {
                    return System.Reflection.Assembly.GetEntryAssembly().Location;
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    string value = key.GetValue(ValueName) as string;
                    if (string.IsNullOrEmpty(value))
                    {
                        return false;
                    }
                    string expected = BuildCommand();
                    return string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    if (enabled)
                    {
                        key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string BuildCommand()
        {
            return "\"" + ExecutablePath + "\" --autostart";
        }
    }
}
