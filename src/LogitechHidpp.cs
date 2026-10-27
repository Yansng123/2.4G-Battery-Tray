using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BatteryTray
{
    /// <summary>
    /// Logitech HID++ 2.0 over the long (20 byte) receiver interface.
    /// Only feature 0x1001 (battery voltage) is guaranteed on the G502 LIGHTSPEED.
    /// </summary>
    internal sealed class LogitechHidpp : IDisposable
    {
        private const byte ReportId = 0x11;
        private const ushort FeatureBatteryVoltage = 0x1001;

        private readonly object _gate = new object();
        private readonly Dictionary<byte, byte> _featureIndex = new Dictionary<byte, byte>();
        private IntPtr _writeHandle = IntPtr.Zero;
        private IntPtr _readHandle = IntPtr.Zero;
        private IntPtr _readEvent = IntPtr.Zero;
        private IntPtr _readBuffer = IntPtr.Zero;
        private int _inputLength;
        private int _outputLength;
        private readonly byte[] _writeBuffer = new byte[64];

        public string Path { get; private set; }

        private LogitechHidpp()
        {
        }

        public static LogitechHidpp Open(HidInterface iface)
        {
            if (iface == null || iface.InputLength <= 0 || iface.OutputLength <= 0 || iface.OutputLength > 64)
            {
                return null;
            }
            LogitechHidpp link = new LogitechHidpp();
            link.Path = iface.Path;
            link._inputLength = iface.InputLength;
            link._outputLength = iface.OutputLength;

            link._writeHandle = Native.CreateFile(iface.Path,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (link._writeHandle == (IntPtr)(-1))
            {
                link._writeHandle = IntPtr.Zero;
                return null;
            }

            link._readHandle = Native.CreateFile(iface.Path,
                Native.GENERIC_READ,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (link._readHandle == (IntPtr)(-1))
            {
                link._readHandle = IntPtr.Zero;
                link.Dispose();
                return null;
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

        /// <summary>Probes receiver slots 1..6 and caches the 0x1001 feature index of each device found.</summary>
        public List<byte> DetectDevices(int timeoutPerIndexMs)
        {
            List<byte> found = new List<byte>();
            lock (_gate)
            {
                for (byte index = 1; index <= 6; index++)
                {
                    byte[] response;
                    if (index == 1)
                    {
                        response = QueryFeature(index, timeoutPerIndexMs, 2);
                    }
                    else
                    {
                        // Secondary receiver slots: one quick probe, no retries.
                        response = null;
                        if (Request(index, 0x00, 0x00, FeatureQueryParameters(), 300, out response)
                            && (response == null || response.Length < 5 || response[4] == 0))
                        {
                            response = null;
                        }
                    }
                    if (response != null)
                    {
                        _featureIndex[index] = response[4];
                        found.Add(index);
                    }
                }
            }
            return found;
        }

        public bool TryReadBattery(byte deviceIndex, out int millivolts, out byte flags, out string raw, out string error)
        {
            millivolts = 0;
            flags = 0;
            raw = string.Empty;
            error = string.Empty;
            lock (_gate)
            {
                byte featureIndex;
                if (!_featureIndex.TryGetValue(deviceIndex, out featureIndex))
                {
                    byte[] query = QueryFeature(deviceIndex, 900, 2);
                    if (query == null)
                    {
                        error = "设备未响应（0x1001 查询失败）";
                        return false;
                    }
                    featureIndex = query[4];
                    _featureIndex[deviceIndex] = featureIndex;
                }

                byte[] response;
                if (!TryRequest(deviceIndex, featureIndex, 0x00, null, 900, out response)
                    || response == null || response.Length < 7)
                {
                    error = "电量查询超时";
                    return false;
                }
                millivolts = (response[4] << 8) | response[5];
                flags = response[6];
                raw = HidEnum.Hex(response);
                if (millivolts <= 0)
                {
                    error = "电压数据无效";
                    return false;
                }
                return true;
            }
        }

        private static byte[] FeatureQueryParameters()
        {
            return new byte[] { (byte)(FeatureBatteryVoltage >> 8), (byte)(FeatureBatteryVoltage & 0xFF) };
        }

        private byte[] QueryFeature(byte deviceIndex, int timeoutMs, int attempts)
        {
            byte[] parameters = FeatureQueryParameters();
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                byte[] response;
                if (TryRequest(deviceIndex, 0x00, 0x00, parameters, timeoutMs, out response)
                    && response != null && response.Length >= 5 && response[4] != 0)
                {
                    return response;
                }
                if (attempt + 1 < attempts)
                {
                    System.Threading.Thread.Sleep(40);
                }
            }
            return null;
        }

        private bool TryRequest(byte deviceIndex, byte featureIndex, byte function, byte[] parameters, int timeoutMs, out byte[] response)
        {
            response = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (Request(deviceIndex, featureIndex, function, parameters, timeoutMs, out response))
                {
                    return true;
                }
                response = null;
                if (attempt == 0)
                {
                    DrainInput();
                    System.Threading.Thread.Sleep(30);
                }
            }
            return false;
        }

        private void DrainInput()
        {
            for (int i = 0; i < 4; i++)
            {
                if (ReadOne(5) == null)
                {
                    break;
                }
            }
        }

        private bool Request(byte deviceIndex, byte featureIndex, byte function, byte[] parameters, int timeoutMs, out byte[] response)
        {
            response = null;
            if (_writeHandle == IntPtr.Zero || _readHandle == IntPtr.Zero)
            {
                return false;
            }

            Array.Clear(_writeBuffer, 0, _writeBuffer.Length);
            _writeBuffer[0] = ReportId;
            _writeBuffer[1] = deviceIndex;
            _writeBuffer[2] = featureIndex;
            _writeBuffer[3] = function;
            if (parameters != null)
            {
                Array.Copy(parameters, 0, _writeBuffer, 4, Math.Min(parameters.Length, _writeBuffer.Length - 4));
            }

            uint written;
            if (!Native.WriteFile(_writeHandle, _writeBuffer, (uint)_outputLength, out written, IntPtr.Zero))
            {
                return false;
            }

            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                int remaining = timeoutMs - (int)watch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    break;
                }
                byte[] data = ReadOne(remaining);
                if (data == null || data.Length < 4)
                {
                    continue;
                }
                if (data[0] != ReportId || data[1] != deviceIndex)
                {
                    continue;
                }
                if (data[2] != featureIndex || data[3] != function)
                {
                    continue;
                }
                response = data;
                return true;
            }
            return false;
        }

        private byte[] ReadOne(int waitMs)
        {
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
                    // Reap the cancelled operation before the OVERLAPPED goes out of scope.
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
            lock (_gate)
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
                if (_writeHandle != IntPtr.Zero)
                {
                    Native.CloseHandle(_writeHandle);
                    _writeHandle = IntPtr.Zero;
                }
                if (_readBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_readBuffer);
                    _readBuffer = IntPtr.Zero;
                }
            }
        }
    }
}
