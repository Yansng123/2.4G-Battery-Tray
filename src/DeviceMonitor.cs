using System;
using System.Collections.Generic;
using System.Threading;

namespace BatteryTray
{
    internal sealed class ReadingsEventArgs : EventArgs
    {
        public IList<DeviceReading> Readings;
    }

    internal sealed class DeviceMonitor : IDisposable
    {
        private static readonly object IndexCacheLock = new object();
        private static readonly Dictionary<string, List<byte>> IndexCache = new Dictionary<string, List<byte>>();

        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly object _gate = new object();
        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _force;
        private volatile int _intervalMinutes = Settings.DefaultIntervalMinutes;
        private List<DeviceReading> _last = new List<DeviceReading>();

        // 充电状态轮询间隔：与用户设置的“刷新频率”无关，只用来尽早发现插拔充电线。
        private const int ChargePollMs = 15000;

        public event EventHandler<ReadingsEventArgs> Updated;
        public DateTime LastRefresh { get; private set; }

        public void Start()
        {
            if (_thread != null)
            {
                return;
            }
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "BatteryMonitor";
            _thread.Start();
        }

        public void RefreshNow()
        {
            _force = true;
            _wake.Set();
        }

        public void SetInterval(int minutes)
        {
            _intervalMinutes = Settings.NormalizeInterval(minutes);
            _force = true;
            _wake.Set();
        }

        public IList<DeviceReading> Snapshot()
        {
            lock (_gate)
            {
                List<DeviceReading> copy = new List<DeviceReading>(_last.Count);
                for (int i = 0; i < _last.Count; i++)
                {
                    copy.Add(_last[i].Clone());
                }
                return copy;
            }
        }

        private void Loop()
        {
            DateTime nextFull = DateTime.MinValue;
            string lastCharge = null;
            while (!_stop)
            {
                bool fullDue = DateTime.Now >= nextFull;
                bool force = _force;
                _force = false;
                List<DeviceReading> readings = null;
                try
                {
                    readings = ScanOnce();
                    string charge = ChargeSignature(readings);
                    bool changed = lastCharge != null
                        && charge.Length > 0
                        && lastCharge.Length > 0
                        && charge != lastCharge;
                    if (fullDue || force || changed)
                    {
                        lock (_gate)
                        {
                            _last = readings;
                        }
                        LastRefresh = DateTime.Now;
                        EventHandler<ReadingsEventArgs> handler = Updated;
                        if (handler != null)
                        {
                            ReadingsEventArgs args = new ReadingsEventArgs();
                            args.Readings = readings;
                            handler(this, args);
                        }
                        if (fullDue || force)
                        {
                            nextFull = DateTime.Now.AddMinutes(_intervalMinutes);
                        }
                    }
                    if (charge.Length > 0)
                    {
                        lastCharge = charge;
                    }
                }
                catch (Exception)
                {
                }

                if (_stop)
                {
                    break;
                }
                int waitMs = ChargePollMs;
                int remaining = (int)(nextFull - DateTime.Now).TotalMilliseconds;
                if (remaining > 0 && remaining < waitMs)
                {
                    waitMs = remaining;
                }
                if (waitMs < 1000)
                {
                    waitMs = 1000;
                }
                _wake.WaitOne(waitMs);
            }
        }

        // 只关心“有没有插电”，不关心具体电量；读不到状态的设备（RK、读取失败）不参与比较。
        private static string ChargeSignature(IList<DeviceReading> readings)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < readings.Count; i++)
            {
                DeviceReading reading = readings[i];
                string power;
                if (reading.State == BatteryState.Charging || reading.State == BatteryState.Full)
                {
                    power = "1";
                }
                else if (reading.State == BatteryState.Discharging)
                {
                    power = "0";
                }
                else
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(';');
                }
                sb.Append(reading.Key);
                sb.Append('=');
                sb.Append(power);
            }
            return sb.ToString();
        }

        public static void InvalidateIndexCache()
        {
            lock (IndexCacheLock)
            {
                IndexCache.Clear();
            }
        }

        public static List<DeviceReading> ScanOnce()
        {
            List<DeviceReading> result = new List<DeviceReading>();
            List<HidInterface> interfaces;
            try
            {
                interfaces = HidEnum.Enumerate();
            }
            catch (Exception)
            {
                interfaces = new List<HidInterface>();
            }

            int logitechCount = 0;
            for (int i = 0; i < interfaces.Count; i++)
            {
                HidInterface iface = interfaces[i];
                if (!IsLogitechHidpp(iface))
                {
                    continue;
                }
                LogitechHidpp link = null;
                try
                {
                    link = LogitechHidpp.Open(iface);
                    if (link == null)
                    {
                        continue;
                    }
                    List<byte> indexes;
                    lock (IndexCacheLock)
                    {
                        if (!IndexCache.TryGetValue(iface.Path, out indexes))
                        {
                            indexes = null;
                        }
                    }
                    if (indexes == null || indexes.Count == 0)
                    {
                        indexes = link.DetectDevices(900);
                        if (indexes.Count > 0)
                        {
                            lock (IndexCacheLock)
                            {
                                IndexCache[iface.Path] = new List<byte>(indexes);
                            }
                        }
                    }
                    for (int k = 0; k < indexes.Count; k++)
                    {
                        byte index = indexes[k];
                        int millivolts;
                        byte flags;
                        string raw;
                        string error;
                        DeviceReading reading = new DeviceReading();
                        reading.Key = "logitech:" + iface.Path + ":" + index.ToString();
                        reading.Kind = DeviceKind.Logitech;
                        reading.Name = "罗技鼠标";
                        reading.Millivolts = 0;
                        reading.Online = true;
                        reading.Updated = DateTime.Now;
                        if (link.TryReadBattery(index, out millivolts, out flags, out raw, out error))
                        {
                            reading.Millivolts = millivolts;
                            reading.Raw = raw;
                            reading.State = InterpretLogitechState(flags);
                            if (reading.State == BatteryState.Charging)
                            {
                                reading.Percent = null;
                            }
                            else if (reading.State == BatteryState.Full)
                            {
                                reading.Percent = 100;
                            }
                            else
                            {
                                reading.Percent = BatteryCurve.FromMillivolts(millivolts);
                            }
                        }
                        else
                        {
                            reading.Online = false;
                            reading.Error = error;
                            reading.State = BatteryState.Error;
                        }
                        result.Add(reading);
                        logitechCount++;
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    if (link != null)
                    {
                        link.Dispose();
                    }
                }
            }

            for (int i = 0; i < interfaces.Count; i++)
            {
                HidInterface iface = interfaces[i];
                if (!IsRkFeature(iface))
                {
                    continue;
                }
                RkCompx link = null;
                try
                {
                    link = RkCompx.Open(iface);
                    if (link == null)
                    {
                        continue;
                    }
                    int percent;
                    string raw;
                    string error;
                    DeviceReading reading = new DeviceReading();
                    reading.Key = "rk:" + iface.Path;
                    reading.Kind = DeviceKind.Rk;
                    reading.Name = "RK 键盘";
                    reading.Online = true;
                    reading.Updated = DateTime.Now;
                    if (link.TryReadBattery(out percent, out raw, out error))
                    {
                        reading.Percent = BatteryCurve.Clamp(percent);
                        reading.Raw = raw;
                        reading.State = BatteryState.Unknown;
                    }
                    else
                    {
                        reading.Online = false;
                        reading.Error = error;
                        reading.State = BatteryState.Error;
                    }
                    result.Add(reading);
                }
                catch (Exception)
                {
                }
                finally
                {
                    if (link != null)
                    {
                        link.Dispose();
                    }
                }
            }

            for (int i = 0; i < interfaces.Count; i++)
            {
                HidInterface iface = interfaces[i];
                if (!DualSenseHid.IsDualSense(iface))
                {
                    continue;
                }
                DualSenseHid link = null;
                try
                {
                    link = DualSenseHid.Open(iface);
                    if (link == null)
                    {
                        continue;
                    }
                    int percent;
                    bool charging;
                    byte batteryByte;
                    string raw;
                    string error;
                    // 读不到（手柄关机/休眠）就不进列表：图标自动消失，开机后自动回来
                    if (!link.TryReadBattery(out percent, out charging, out batteryByte, out raw, out error))
                    {
                        continue;
                    }
                    DeviceReading reading = new DeviceReading();
                    reading.Key = "ds5:" + iface.Path;
                    reading.Kind = DeviceKind.DualSense;
                    reading.Name = link.Wired ? "索尼 DS5（有线）" : "索尼 DS5 手柄";
                    reading.Percent = BatteryCurve.Clamp(percent);
                    reading.Raw = raw;
                    reading.State = charging
                        ? (percent >= 100 ? BatteryState.Full : BatteryState.Charging)
                        : BatteryState.Discharging;
                    reading.Online = true;
                    reading.Updated = DateTime.Now;
                    result.Add(reading);
                }
                catch (Exception)
                {
                }
                finally
                {
                    if (link != null)
                    {
                        link.Dispose();
                    }
                }
            }

            if (logitechCount > 1)
            {
                int ordinal = 0;
                for (int i = 0; i < result.Count; i++)
                {
                    if (result[i].Kind == DeviceKind.Logitech)
                    {
                        ordinal++;
                        result[i].Name = "罗技设备 " + ordinal.ToString();
                    }
                }
            }
            return result;
        }

        private static bool IsLogitechHidpp(HidInterface iface)
        {
            return iface.VendorId == 0x046D
                && iface.UsagePage == 0xFF00
                && iface.Usage == 0x0002
                && iface.OutputLength >= 7
                && iface.InputLength >= 7;
        }

        private static bool IsRkFeature(HidInterface iface)
        {
            return iface.VendorId == 0x25A7
                && iface.UsagePage == 0xFF02
                && iface.Usage == 0x0002
                && iface.FeatureLength > 0;
        }

        private static BatteryState InterpretLogitechState(byte flags)
        {
            bool externalPower = (flags & 0x80) != 0;
            int chargeState = flags & 0x07;
            if (!externalPower)
            {
                return BatteryState.Discharging;
            }
            if (chargeState == 0)
            {
                return BatteryState.Charging;
            }
            if (chargeState == 1)
            {
                return BatteryState.Full;
            }
            if (chargeState == 2)
            {
                return BatteryState.Discharging;
            }
            return BatteryState.Unknown;
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            Thread thread = _thread;
            if (thread != null)
            {
                thread.Join(2000);
                _thread = null;
            }
            _wake.Close();
        }
    }
}
