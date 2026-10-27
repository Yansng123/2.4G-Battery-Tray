using System;
using System.Runtime.InteropServices;

namespace BatteryTray
{
    /// <summary>
    /// 索尼 DualSense（DS5）手柄电量读取。
    ///
    /// 蓝牙和有线两种连接方式的报文不一样，取数方式也不一样（本机实测）：
    ///   蓝牙：完整报文是 0x31 号输入报告（78 字节），只能用 HidD_GetInputReport 主动索取；
    ///         流式读到的 0x01 号报告只有摇杆数据，是简化报文，里面没有电量。
    ///         电量在字节 54。
    ///   有线：插上 USB 线后仍报 PID 0CE6，但走的是 USB 接口，报文变 64 字节的 0x01 号报告；
    ///         此时 HidD_GetInputReport 会失败，改为读一帧流式报文。电量在字节 53。
    ///
    /// 电量字节含义（用真机报文对表确认）：
    ///   低四位 = 档位 0~10，乘 10 得百分比（实测未插线 0x09 → 90%，插线 0x29 → 档位仍为 9）
    ///   高四位 = 供电状态，0 = 放电中，非 0 = 接着电源（实测插线为 2）
    /// </summary>
    internal sealed class DualSenseHid : IDisposable
    {
        private const ushort SonyVendorId = 0x054C;
        private const ushort DualSenseProductId = 0x0CE6;

        private const byte BluetoothReportId = 0x31;
        private const byte UsbReportId = 0x01;

        private const int BluetoothReportLength = 78;
        private const int BluetoothBatteryOffset = 54;
        private const int UsbBatteryOffset = 53;
        private const int UsbReadTimeoutMs = 1500;

        private IntPtr _readHandle = IntPtr.Zero;
        private IntPtr _readEvent = IntPtr.Zero;
        private IntPtr _readBuffer = IntPtr.Zero;
        private int _inputLength;

        public string Path { get; private set; }

        /// <summary>true = 插着 USB 线；false = 蓝牙连接。</summary>
        public bool Wired { get; private set; }

        private DualSenseHid()
        {
        }

        public static bool IsDualSense(HidInterface iface)
        {
            if (iface == null)
            {
                return false;
            }
            if (iface.VendorId != SonyVendorId || iface.ProductId != DualSenseProductId)
            {
                return false;
            }
            // 只认游戏手柄那个集合（用法页 0x0001 / 用法 0x0005）
            if (iface.UsagePage != 0x0001 || iface.Usage != 0x0005)
            {
                return false;
            }
            return iface.InputLength >= UsbBatteryOffset + 1;
        }

        private static bool LooksBluetooth(HidInterface iface)
        {
            if (iface.InputLength >= BluetoothReportLength)
            {
                return true;
            }
            // 蓝牙 HID 的设备路径里带这个服务 GUID
            return !string.IsNullOrEmpty(iface.Path)
                && iface.Path.IndexOf("00001124", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static DualSenseHid Open(HidInterface iface)
        {
            if (iface == null)
            {
                return null;
            }
            DualSenseHid link = new DualSenseHid();
            link.Path = iface.Path;
            link.Wired = !LooksBluetooth(iface);
            link._inputLength = iface.InputLength;
            if (link._inputLength < BluetoothReportLength)
            {
                link._inputLength = BluetoothReportLength;
            }

            // 全程只读：不申请写权限，避免和 Steam / 游戏 / 其它手柄工具抢设备
            link._readHandle = Native.CreateFile(iface.Path,
                Native.GENERIC_READ,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (link._readHandle == (IntPtr)(-1) || link._readHandle == IntPtr.Zero)
            {
                link._readHandle = IntPtr.Zero;
                return null;
            }

            if (!link.Wired)
            {
                return link;
            }

            link._readEvent = Native.CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            if (link._readEvent == IntPtr.Zero)
            {
                link.Dispose();
                return null;
            }
            link._readBuffer = Marshal.AllocHGlobal(link._inputLength);
            return link;
        }

        public bool TryReadBattery(out int percent, out bool charging, out byte batteryByte, out string raw, out string error)
        {
            percent = 0;
            charging = false;
            batteryByte = 0;
            raw = string.Empty;
            error = string.Empty;
            if (_readHandle == IntPtr.Zero)
            {
                error = "设备未打开";
                return false;
            }

            byte[] data;
            int offset;
            if (Wired)
            {
                offset = UsbBatteryOffset;
                data = ReadOne(UsbReadTimeoutMs);
                if (data != null && data[0] != UsbReportId)
                {
                    // 偶尔会夹带一帧别的报告，再取一帧
                    data = ReadOne(600);
                }
                if (data == null)
                {
                    error = "未收到手柄报文（可能已关机）";
                    return false;
                }
                if (data[0] != UsbReportId)
                {
                    error = "报文不是状态报告 0x01";
                    return false;
                }
            }
            else
            {
                offset = BluetoothBatteryOffset;
                byte[] buffer = new byte[_inputLength];
                buffer[0] = BluetoothReportId;
                if (!Native.HidD_GetInputReport(_readHandle, buffer, (uint)buffer.Length))
                {
                    error = "蓝牙报告索取失败（手柄可能已休眠）";
                    return false;
                }
                if (buffer[0] != BluetoothReportId)
                {
                    error = "只拿到简化报文（不含电量）";
                    return false;
                }
                data = buffer;
            }

            if (data.Length <= offset)
            {
                error = "报文长度不足（" + data.Length + " 字节）";
                return false;
            }

            batteryByte = data[offset];
            int level = batteryByte & 0x0F;      // 档位 0~10
            int power = (batteryByte >> 4) & 0x0F; // 供电状态：0 放电，非 0 接着电源
            if (level > 10)
            {
                level = 10;
            }
            percent = level * 10;
            charging = power != 0;

            byte[] head = new byte[Math.Min(16, data.Length)];
            Array.Copy(data, head, head.Length);
            raw = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "模式={0} 长度={1} 电量字节[{2}]=0x{3:X2} 档位={4} 供电状态={5} 头部={6}",
                Wired ? "有线" : "蓝牙", data.Length, offset, batteryByte, level, power, HidEnum.Hex(head));
            return true;
        }

        /// <summary>读一帧流式输入报告（有线模式用），超时返回 null。</summary>
        private byte[] ReadOne(int waitMs)
        {
            if (_readEvent == IntPtr.Zero || _readBuffer == IntPtr.Zero)
            {
                return null;
            }
            Native.OVERLAPPED ov = new Native.OVERLAPPED();
            ov.hEvent = _readEvent;
            Native.ResetEvent(_readEvent);
            uint read;
            bool ok = Native.ReadFile(_readHandle, _readBuffer, (uint)_inputLength, out read, ref ov);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 997)
                {
                    return null;
                }
                uint wait = Native.WaitForSingleObject(_readEvent, (uint)waitMs);
                if (wait != 0)
                {
                    Native.CancelIoEx(_readHandle, ref ov);
                    Native.WaitForSingleObject(_readEvent, 50);
                    uint dummy;
                    Native.GetOverlappedResult(_readHandle, ref ov, out dummy, false);
                    return null;
                }
                if (!Native.GetOverlappedResult(_readHandle, ref ov, out read, false))
                {
                    return null;
                }
            }
            if (read == 0 || read > (uint)_inputLength)
            {
                return null;
            }
            byte[] data = new byte[read];
            Marshal.Copy(_readBuffer, data, 0, (int)read);
            return data;
        }

        public void Dispose()
        {
            if (_readHandle != IntPtr.Zero)
            {
                Native.CancelIoEx(_readHandle, IntPtr.Zero);
            }
            if (_readEvent != IntPtr.Zero)
            {
                Native.CloseHandle(_readEvent);
                _readEvent = IntPtr.Zero;
            }
            if (_readHandle != IntPtr.Zero)
            {
                Native.CloseHandle(_readHandle);
                _readHandle = IntPtr.Zero;
            }
            if (_readBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_readBuffer);
                _readBuffer = IntPtr.Zero;
            }
        }
    }
}
