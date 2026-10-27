using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace BatteryTray
{
    internal sealed class TrayController : ApplicationContext
    {
        private readonly Settings _settings;
        private readonly DeviceMonitor _monitor;
        private readonly PopupCard _card;
        private readonly ContextMenuStrip _menu;
        private readonly NotifyIcon _placeholder;
        private readonly NotifyIcon _single;
        private readonly Dictionary<string, NotifyIcon> _icons = new Dictionary<string, NotifyIcon>();
        private readonly Control _invoker;
        private readonly DeviceWatcher _watcher;
        private readonly Timer _debounce;
        private readonly ToolStripMenuItem _interval1;
        private readonly ToolStripMenuItem _interval5;
        private readonly ToolStripMenuItem _interval10;
        private readonly ToolStripMenuItem _autostartItem;
        private readonly ToolStripMenuItem _styleSplit;
        private readonly ToolStripMenuItem _styleSingle;
        private readonly ToolStripMenuItem _lookNumber;
        private readonly ToolStripMenuItem _lookPlain;
        private readonly ToolStripMenuItem _darkItem;
        private bool _disposed;

        public TrayController()
        {
            _settings = Settings.Load();
            _monitor = new DeviceMonitor();
            _card = new PopupCard();
            _invoker = new Control();
            IntPtr forceHandle = _invoker.Handle;
            if (forceHandle == IntPtr.Zero)
            {
                _invoker.CreateControl();
            }

            _menu = new ContextMenuStrip();
            ToolStripMenuItem refreshItem = new ToolStripMenuItem("立即刷新");
            refreshItem.Click += RefreshItemClick;
            _menu.Items.Add(refreshItem);

            ToolStripMenuItem rescanItem = new ToolStripMenuItem("重新扫描设备");
            rescanItem.Click += RefreshItemClick;
            _menu.Items.Add(rescanItem);

            ToolStripMenuItem styleItem = new ToolStripMenuItem("图标更换");
            _styleSplit = new ToolStripMenuItem("分开显示（每个设备一个）");
            _styleSingle = new ToolStripMenuItem("集合体（只显示一个图标）");
            _styleSplit.Click += StyleClick;
            _styleSingle.Click += StyleClick;
            styleItem.DropDownItems.Add(_styleSplit);
            styleItem.DropDownItems.Add(_styleSingle);
            _menu.Items.Add(styleItem);

            ToolStripMenuItem lookItem = new ToolStripMenuItem("图标样式");
            _lookNumber = new ToolStripMenuItem("数字 + 横电池");
            _lookPlain = new ToolStripMenuItem("纯电池图标（竖）");
            _lookNumber.Click += LookClick;
            _lookPlain.Click += LookClick;
            lookItem.DropDownItems.Add(_lookNumber);
            lookItem.DropDownItems.Add(_lookPlain);
            _menu.Items.Add(lookItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem intervalItem = new ToolStripMenuItem("刷新频率");
            _interval1 = new ToolStripMenuItem("1 分钟");
            _interval5 = new ToolStripMenuItem("5 分钟");
            _interval10 = new ToolStripMenuItem("10 分钟");
            _interval1.Click += IntervalClick;
            _interval5.Click += IntervalClick;
            _interval10.Click += IntervalClick;
            intervalItem.DropDownItems.Add(_interval1);
            intervalItem.DropDownItems.Add(_interval5);
            intervalItem.DropDownItems.Add(_interval10);
            _menu.Items.Add(intervalItem);

            _autostartItem = new ToolStripMenuItem("开机自启");
            _autostartItem.CheckOnClick = true;
            _autostartItem.Click += AutostartClick;
            _menu.Items.Add(_autostartItem);

            _darkItem = new ToolStripMenuItem("深色模式");
            _darkItem.CheckOnClick = true;
            _darkItem.Click += DarkClick;
            _menu.Items.Add(_darkItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem diagnosticsItem = new ToolStripMenuItem("诊断信息");
            diagnosticsItem.Click += DiagnosticsClick;
            _menu.Items.Add(diagnosticsItem);

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += ExitClick;
            _menu.Items.Add(exitItem);

            _placeholder = new NotifyIcon();
            _placeholder.ContextMenuStrip = _menu;
            _placeholder.MouseClick += TrayMouseClick;
            _placeholder.Icon = BatteryIcon.CreateEmpty(BatteryIcon.SmallIconSize);
            _placeholder.Text = "2.4G 电量 · 未检测到设备";
            _placeholder.Visible = false;

            _single = new NotifyIcon();
            _single.ContextMenuStrip = _menu;
            _single.MouseClick += TrayMouseClick;
            _single.Icon = BatteryIcon.CreateEmpty(BatteryIcon.SmallIconSize);
            _single.Text = "2.4G 电量";
            _single.Visible = false;

            _debounce = new Timer();
            _debounce.Interval = 900;
            _debounce.Tick += DebounceTick;

            _watcher = new DeviceWatcher();
            _watcher.Changed += WatcherChanged;

            _card.RefreshRequested += CardRefreshRequested;

            if (_settings.Autostart && !Autostart.IsEnabled())
            {
                Autostart.SetEnabled(true);
            }
            _autostartItem.Checked = Autostart.IsEnabled();
            _settings.Autostart = _autostartItem.Checked;
            UpdateIntervalChecks();
            UpdateStyleChecks();
            UpdateLookChecks();
            _darkItem.Checked = _settings.DarkMode;
            _card.SetDarkMode(_settings.DarkMode);

            _monitor.Updated += MonitorUpdated;
            _monitor.SetInterval(_settings.IntervalMinutes);
            _monitor.Start();
        }

        private void MonitorUpdated(object sender, ReadingsEventArgs e)
        {
            if (_disposed)
            {
                return;
            }
            IList<DeviceReading> readings = e.Readings;
            try
            {
                if (_invoker.IsHandleCreated)
                {
                    _invoker.BeginInvoke(new MethodInvoker(delegate { ApplyReadings(readings); }));
                }
            }
            catch (Exception)
            {
            }
        }

        private void ApplyReadings(IList<DeviceReading> readings)
        {
            if (_disposed)
            {
                return;
            }
            HashSet<string> seen = new HashSet<string>();
            bool singleMode = _settings.Style == IconStyle.Single;
            int iconSize = BatteryIcon.SmallIconSize;
            for (int i = 0; i < readings.Count; i++)
            {
                DeviceReading reading = readings[i];
                seen.Add(reading.Key);
                NotifyIcon icon;
                if (!_icons.TryGetValue(reading.Key, out icon))
                {
                    icon = new NotifyIcon();
                    icon.ContextMenuStrip = _menu;
                    icon.MouseClick += TrayMouseClick;
                    icon.Visible = false;
                    _icons[reading.Key] = icon;
                }
                Icon previous = icon.Icon;
                icon.Icon = BatteryIcon.Create(reading, iconSize, _settings.Look);
                if (previous != null)
                {
                    previous.Dispose();
                }
                icon.Text = ClampTooltip(reading.ShortText());
                icon.Visible = !singleMode;
            }

            List<string> stale = new List<string>();
            foreach (string key in _icons.Keys)
            {
                if (!seen.Contains(key))
                {
                    stale.Add(key);
                }
            }
            for (int i = 0; i < stale.Count; i++)
            {
                NotifyIcon icon = _icons[stale[i]];
                _icons.Remove(stale[i]);
                icon.Visible = false;
                icon.Dispose();
            }

            if (singleMode && readings.Count > 0)
            {
                DeviceReading aggregate = PickAggregate(readings);
                Icon previous = _single.Icon;
                _single.Icon = BatteryIcon.Create(aggregate, iconSize, _settings.Look);
                if (previous != null)
                {
                    previous.Dispose();
                }
                _single.Text = ClampTooltip(BuildAggregateTooltip(readings));
                _single.Visible = true;
            }
            else
            {
                _single.Visible = false;
            }

            _placeholder.Visible = readings.Count == 0;

            if (_settings.FirstRun && readings.Count > 0)
            {
                _settings.FirstRun = false;
                _settings.Save();
                NotifyIcon balloon = _single.Visible ? _single : null;
                if (balloon == null)
                {
                    foreach (KeyValuePair<string, NotifyIcon> pair in _icons)
                    {
                        if (pair.Value.Visible)
                        {
                            balloon = pair.Value;
                            break;
                        }
                    }
                }
                if (balloon != null)
                {
                    balloon.BalloonTipTitle = "2.4G 电量已启动";
                    balloon.BalloonTipText = "如果任务栏没有看到电池图标：点右下角的 ^，把电池图标拖到任务栏上即可（只需拖一次）。";
                    balloon.BalloonTipIcon = ToolTipIcon.Info;
                    balloon.ShowBalloonTip(8000);
                }
            }

            if (_card.Visible)
            {
                _card.UpdateData(readings, _monitor.LastRefresh, _settings.IntervalMinutes);
            }
        }

        private static DeviceReading PickAggregate(IList<DeviceReading> readings)
        {
            DeviceReading best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < readings.Count; i++)
            {
                DeviceReading reading = readings[i];
                if (!reading.Online || reading.State == BatteryState.Error)
                {
                    continue;
                }
                int score;
                if (reading.State == BatteryState.Charging)
                {
                    score = 1000;
                }
                else if (reading.Percent.HasValue)
                {
                    score = reading.Percent.Value;
                }
                else
                {
                    score = 999;
                }
                if (best == null || score < bestScore)
                {
                    best = reading;
                    bestScore = score;
                }
            }
            if (best == null)
            {
                for (int i = 0; i < readings.Count; i++)
                {
                    if (readings[i].Online)
                    {
                        best = readings[i];
                        break;
                    }
                }
            }
            if (best != null && best.State != BatteryState.Charging && best.Percent.HasValue && best.Percent.Value >= 60)
            {
                for (int i = 0; i < readings.Count; i++)
                {
                    if (readings[i].Online && readings[i].State == BatteryState.Charging)
                    {
                        return readings[i];
                    }
                }
            }
            return best ?? readings[0];
        }

        private static string BuildAggregateTooltip(IList<DeviceReading> readings)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("2.4G · ");
            for (int i = 0; i < readings.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(" / ");
                }
                sb.Append(readings[i].ShortText());
            }
            return sb.ToString();
        }

        private static string ClampTooltip(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "2.4G 电量";
            }
            if (text.Length > 63)
            {
                return text.Substring(0, 62) + "…";
            }
            return text;
        }

        private void TrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            ToggleCard();
        }

        private void ToggleCard()
        {
            if (_card.Visible)
            {
                _card.Hide();
                return;
            }
            if ((DateTime.Now - _card.LastHidden).TotalMilliseconds < 350)
            {
                return;
            }
            _card.UpdateData(_monitor.Snapshot(), _monitor.LastRefresh, _settings.IntervalMinutes);
            _card.ShowCard(Cursor.Position);
        }

        private void CardRefreshRequested(object sender, EventArgs e)
        {
            _monitor.RefreshNow();
        }

        private void RefreshItemClick(object sender, EventArgs e)
        {
            _monitor.RefreshNow();
        }

        private void IntervalClick(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null)
            {
                return;
            }
            int minutes = Settings.DefaultIntervalMinutes;
            if (item == _interval1)
            {
                minutes = 1;
            }
            else if (item == _interval5)
            {
                minutes = 5;
            }
            else if (item == _interval10)
            {
                minutes = 10;
            }
            _settings.IntervalMinutes = Settings.NormalizeInterval(minutes);
            _settings.Save();
            UpdateIntervalChecks();
            _monitor.SetInterval(_settings.IntervalMinutes);
            if (_card.Visible)
            {
                _card.UpdateData(_monitor.Snapshot(), _monitor.LastRefresh, _settings.IntervalMinutes);
            }
        }

        private void UpdateIntervalChecks()
        {
            _interval1.Checked = _settings.IntervalMinutes == 1;
            _interval5.Checked = _settings.IntervalMinutes == 5;
            _interval10.Checked = _settings.IntervalMinutes == 10;
        }

        private void UpdateStyleChecks()
        {
            _styleSplit.Checked = _settings.Style == IconStyle.Split;
            _styleSingle.Checked = _settings.Style == IconStyle.Single;
        }

        private void StyleClick(object sender, EventArgs e)
        {
            IconStyle style = ReferenceEquals(sender, _styleSingle) ? IconStyle.Single : IconStyle.Split;
            if (_settings.Style == style)
            {
                UpdateStyleChecks();
                return;
            }
            _settings.Style = style;
            _settings.Save();
            UpdateStyleChecks();
            ApplyReadings(_monitor.Snapshot());
        }

        private void UpdateLookChecks()
        {
            _lookNumber.Checked = _settings.Look == IconLook.Number;
            _lookPlain.Checked = _settings.Look == IconLook.Plain;
        }

        private void LookClick(object sender, EventArgs e)
        {
            IconLook look = ReferenceEquals(sender, _lookPlain) ? IconLook.Plain : IconLook.Number;
            if (_settings.Look == look)
            {
                UpdateLookChecks();
                return;
            }
            _settings.Look = look;
            _settings.Save();
            UpdateLookChecks();
            ApplyReadings(_monitor.Snapshot());
        }

        private void DarkClick(object sender, EventArgs e)
        {
            _settings.DarkMode = _darkItem.Checked;
            _settings.Save();
            _card.SetDarkMode(_settings.DarkMode);
            if (_card.Visible)
            {
                _card.UpdateData(_monitor.Snapshot(), _monitor.LastRefresh, _settings.IntervalMinutes);
            }
        }

        private void AutostartClick(object sender, EventArgs e)
        {
            bool wanted = _autostartItem.Checked;
            if (!Autostart.SetEnabled(wanted))
            {
                _autostartItem.Checked = !wanted;
                MessageBox.Show("无法修改开机自启设置，请检查注册表权限。", "2.4G 电量",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _settings.Autostart = wanted;
            _settings.Save();
        }

        private void DiagnosticsClick(object sender, EventArgs e)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("程序路径：" + Autostart.ExecutablePath);
            sb.AppendLine("刷新频率：" + _settings.IntervalMinutes.ToString(CultureInfo.InvariantCulture) + " 分钟");
            sb.AppendLine("图标样式：" + (_settings.Style == IconStyle.Single ? "集合体（一个图标）" : "分开显示（每个设备一个）"));
            sb.AppendLine("图标款式：" + (_settings.Look == IconLook.Plain ? "纯电池（竖）" : "数字 + 横电池"));
            sb.AppendLine("深色模式：" + (_settings.DarkMode ? "开" : "关"));
            sb.AppendLine("开机自启：" + (Autostart.IsEnabled() ? "已开启" : "已关闭"));
            sb.AppendLine("上次刷新：" + (_monitor.LastRefresh == default(DateTime) ? "-" : _monitor.LastRefresh.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            sb.AppendLine();
            IList<DeviceReading> readings = _monitor.Snapshot();
            if (readings.Count == 0)
            {
                sb.AppendLine("未检测到设备。");
            }
            for (int i = 0; i < readings.Count; i++)
            {
                DeviceReading reading = readings[i];
                sb.AppendLine(reading.Name + "：" + reading.ShortText());
                sb.AppendLine("  状态：" + reading.StateText());
                sb.AppendLine("  原始数据：" + (string.IsNullOrEmpty(reading.Raw) ? "-" : reading.Raw));
                if (!string.IsNullOrEmpty(reading.Error))
                {
                    sb.AppendLine("  错误：" + reading.Error);
                }
            }
            MessageBox.Show(sb.ToString(), "2.4G 电量 · 诊断信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExitClick(object sender, EventArgs e)
        {
            ExitThread();
        }

        private void WatcherChanged(object sender, EventArgs e)
        {
            DeviceMonitor.InvalidateIndexCache();
            _debounce.Stop();
            _debounce.Start();
        }

        private void DebounceTick(object sender, EventArgs e)
        {
            _debounce.Stop();
            _monitor.RefreshNow();
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (disposing)
            {
                _monitor.Dispose();
                _watcher.Dispose();
                _debounce.Dispose();
                _card.Dispose();
                foreach (KeyValuePair<string, NotifyIcon> pair in _icons)
                {
                    pair.Value.Visible = false;
                    pair.Value.Dispose();
                }
                _icons.Clear();
                _single.Visible = false;
                _single.Dispose();
                _placeholder.Visible = false;
                _placeholder.Dispose();
                _menu.Dispose();
                _invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
