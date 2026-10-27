using System;

namespace BatteryTray
{
    internal enum DeviceKind
    {
        Logitech,
        DualSense
    }

    internal enum BatteryState
    {
        Unknown,
        Discharging,
        Charging,
        Full,
        Error
    }

    internal enum IconStyle
    {
        Split,
        Single
    }

    internal enum IconLook
    {
        Number,
        Plain
    }

    internal sealed class DeviceReading
    {
        public string Key = string.Empty;
        public DeviceKind Kind;
        public string Name = string.Empty;
        public int? Percent;
        public int Millivolts;
        public BatteryState State = BatteryState.Unknown;
        public string Raw = string.Empty;
        public string Error = string.Empty;
        public bool Online = true;
        public DateTime Updated;

        public DeviceReading Clone()
        {
            DeviceReading copy = new DeviceReading();
            copy.Key = Key;
            copy.Kind = Kind;
            copy.Name = Name;
            copy.Percent = Percent;
            copy.Millivolts = Millivolts;
            copy.State = State;
            copy.Raw = Raw;
            copy.Error = Error;
            copy.Online = Online;
            copy.Updated = Updated;
            return copy;
        }

        public string StateText()
        {
            if (!Online || State == BatteryState.Error)
            {
                return "读取失败";
            }
            switch (State)
            {
                case BatteryState.Charging:
                    return "充电中";
                case BatteryState.Full:
                    return "已充满";
                case BatteryState.Discharging:
                    return Millivolts > 0 ? string.Format("{0:0.00} V · 放电中", Millivolts / 1000.0) : "放电中";
                default:
                    return Millivolts > 0 ? string.Format("{0:0.00} V", Millivolts / 1000.0) : string.Empty;
            }
        }

        public string ShortText()
        {
            if (!Online || State == BatteryState.Error)
            {
                return Name + " 读取失败";
            }
            if (State == BatteryState.Charging)
            {
                return Name + " 充电中";
            }
            if (Percent.HasValue)
            {
                return string.Format("{0} {1}%", Name, Percent.Value);
            }
            return Name;
        }
    }
}
