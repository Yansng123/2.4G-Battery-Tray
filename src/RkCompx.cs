using System;
using System.Runtime.InteropServices;

namespace BatteryTray
{
    internal sealed class RkCompx : IDisposable
    {
        private const byte BatteryReportId = 0x06;

        private IntPtr _handle = IntPtr.Zero;
        private int _featureLength;

        public string Path { get; private set; }

        private RkCompx()
        {
        }

        public static RkCompx Open(HidInterface iface)
        {
            if (iface == null || iface.FeatureLength <= 0)
            {
                return null;
            }
            RkCompx link = new RkCompx();
            link.Path = iface.Path;
            link._featureLength = iface.FeatureLength;
            link._handle = Native.CreateFile(iface.Path, 0,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (link._handle == (IntPtr)(-1) || link._handle == IntPtr.Zero)
            {
                link._handle = IntPtr.Zero;
                return null;
            }
            return link;
        }

        public bool TryReadBattery(out int percent, out string raw, out string error)
        {
            percent = 0;
            raw = string.Empty;
            error = string.Empty;
            if (_handle == IntPtr.Zero || _featureLength <= 0)
            {
                error = "设备未打开";
                return false;
            }

            byte[] buffer = new byte[_featureLength];
            buffer[0] = BatteryReportId;
            if (!Native.HidD_GetFeature(_handle, buffer, (uint)buffer.Length))
            {
                error = "读取特性报告失败";
                return false;
            }

            raw = HidEnum.Hex(buffer);
            byte value = buffer[buffer.Length - 1];
            int level = value;
            if (level < 0)
            {
                level = 0;
            }
            if (level > 100)
            {
                level = 100;
            }
            percent = level;
            return true;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                Native.CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
