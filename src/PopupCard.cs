using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BatteryTray
{
    internal sealed class PopupCard : Form
    {
        private readonly float _scale;
        private bool _dark;
        private Color _background;
        private Color _foreground;
        private Color _secondary;
        private Color _track;
        private Color _border;
        private Color _accent;
        private readonly Font _titleFont;
        private readonly Font _nameFont;
        private readonly Font _valueFont;
        private readonly Font _smallFont;

        private List<DeviceReading> _readings = new List<DeviceReading>();
        private DateTime _lastRefresh;
        private int _intervalMinutes = Settings.DefaultIntervalMinutes;
        private Rectangle _refreshRect;
        private bool _hoverRefresh;
        private readonly Timer _hideGuard;

        public DateTime LastHidden { get; private set; }
        public event EventHandler RefreshRequested;

        public PopupCard()
        {
            _scale = DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
            ApplyPalette(false);

            _titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _nameFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            _valueFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            _smallFont = new Font("Segoe UI", 8f, FontStyle.Regular);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = _background;
            Padding = new Padding(0);
            KeyPreview = true;
            Opacity = 0.99;

            _hideGuard = new Timer();
            _hideGuard.Interval = 300;
            _hideGuard.Tick += HideGuardTick;
        }

        public void SetDarkMode(bool dark)
        {
            if (_dark == dark)
            {
                return;
            }
            ApplyPalette(dark);
            BackColor = _background;
            Invalidate();
        }

        private void ApplyPalette(bool dark)
        {
            _dark = dark;
            if (dark)
            {
                _background = Color.FromArgb(18, 18, 20);
                _foreground = Color.FromArgb(245, 245, 247);
                _secondary = Color.FromArgb(168, 170, 176);
                _track = Color.FromArgb(56, 255, 255, 255);
                _border = Color.FromArgb(48, 255, 255, 255);
                _accent = Color.FromArgb(110, 180, 255);
            }
            else
            {
                _background = Color.FromArgb(250, 250, 252);
                _foreground = Color.FromArgb(32, 32, 34);
                _secondary = Color.FromArgb(110, 112, 118);
                _track = Color.FromArgb(30, 0, 0, 0);
                _border = Color.FromArgb(24, 0, 0, 0);
                _accent = Color.FromArgb(0, 95, 184);
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        public void UpdateData(IList<DeviceReading> readings, DateTime lastRefresh, int intervalMinutes)
        {
            _readings = new List<DeviceReading>(readings.Count);
            for (int i = 0; i < readings.Count; i++)
            {
                _readings.Add(readings[i].Clone());
            }
            _lastRefresh = lastRefresh;
            _intervalMinutes = Settings.NormalizeInterval(intervalMinutes);
            ResizeToContent();
            Invalidate();
        }

        private void ResizeToContent()
        {
            int rows = _readings.Count;
            if (rows < 1)
            {
                rows = 1;
            }
            int width = S(300);
            int header = S(40);
            int rowHeight = S(54);
            int footer = S(34);
            int height = header + rows * rowHeight + footer;
            if (ClientSize.Width != width || ClientSize.Height != height)
            {
                ClientSize = new Size(width, height);
                ApplyRoundRegion();
            }
        }

        private void ApplyRoundRegion()
        {
            int radius = S(12);
            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width, Height), radius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null)
                {
                    old.Dispose();
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundRegion();
        }

        public void ShowCard(Point anchor)
        {
            ResizeToContent();
            Screen screen = Screen.FromPoint(anchor);
            Rectangle area = screen.WorkingArea;
            int x = anchor.X - Width / 2;
            int y = area.Bottom - Height - S(10);
            if (x < area.Left + S(8))
            {
                x = area.Left + S(8);
            }
            if (x + Width > area.Right - S(8))
            {
                x = area.Right - S(8) - Width;
            }
            if (y < area.Top + S(8))
            {
                y = area.Top + S(8);
            }
            Location = new Point(x, y);
            Show();
            Activate();
        }

        private void HideGuardTick(object sender, EventArgs e)
        {
            _hideGuard.Stop();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Hide();
            LastHidden = DateTime.Now;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedRect(bounds, S(12)))
            using (SolidBrush brush = new SolidBrush(_background))
            using (Pen pen = new Pen(_border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            int pad = S(16);
            int headerTop = S(12);
            TextRenderer.DrawText(g, "2.4G 设备电量", _titleFont,
                new Rectangle(pad, headerTop, Width - pad * 2, S(20)), _foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            string refreshText = "立即刷新";
            Size refreshSize = TextRenderer.MeasureText(g, refreshText, _smallFont);
            _refreshRect = new Rectangle(Width - pad - refreshSize.Width - S(4), headerTop + S(2), refreshSize.Width + S(4), S(18));
            TextRenderer.DrawText(g, refreshText, _smallFont, _refreshRect,
                _hoverRefresh ? _accent : _secondary,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            g.DrawLine(new Pen(_border), pad, S(40), Width - pad, S(40));

            if (_readings.Count == 0)
            {
                TextRenderer.DrawText(g, "未检测到 2.4G 设备", _nameFont,
                    new Rectangle(pad, S(56), Width - pad * 2, S(24)), _secondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            else
            {
                int rowTop = S(44);
                for (int i = 0; i < _readings.Count; i++)
                {
                    DrawRow(g, _readings[i], rowTop + i * S(54), pad);
                }
            }

            string footer = string.Format(CultureInfo.InvariantCulture, "每 {0} 分钟刷新 · {1:HH:mm:ss} 更新",
                _intervalMinutes, _lastRefresh == default(DateTime) ? DateTime.Now : _lastRefresh);
            TextRenderer.DrawText(g, footer, _smallFont,
                new Rectangle(pad, Height - S(24), Width - pad * 2, S(16)), _secondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void DrawRow(Graphics g, DeviceReading reading, int top, int pad)
        {
            bool failed = !reading.Online || reading.State == BatteryState.Error;
            Color levelColor = BatteryIcon.ColorFor(reading);
            int right = Width - pad;

            TextRenderer.DrawText(g, reading.Name, _nameFont,
                new Rectangle(pad, top, Width / 2, S(18)), _foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            string valueText;
            Color valueColor;
            if (failed)
            {
                valueText = "读取失败";
                valueColor = BatteryIcon.MutedColor;
            }
            else if (reading.State == BatteryState.Charging)
            {
                valueText = "充电中";
                valueColor = BatteryIcon.ChargingColor;
            }
            else if (reading.Percent.HasValue)
            {
                valueText = reading.Percent.Value.ToString(CultureInfo.InvariantCulture) + "%";
                valueColor = levelColor;
            }
            else
            {
                valueText = "--";
                valueColor = BatteryIcon.MutedColor;
            }
            Size valueSize = TextRenderer.MeasureText(g, valueText, _valueFont);
            TextRenderer.DrawText(g, valueText, _valueFont,
                new Rectangle(right - valueSize.Width - S(2), top - S(2), valueSize.Width + S(2), S(20)), valueColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            int barTop = top + S(20);
            int barHeight = S(5);
            Rectangle bar = new Rectangle(pad, barTop, right - pad, barHeight);
            using (GraphicsPath track = RoundedRect(bar, barHeight / 2f))
            using (SolidBrush trackBrush = new SolidBrush(_track))
            {
                g.FillPath(trackBrush, track);
            }
            int fillPercent = failed ? 0 : (reading.State == BatteryState.Charging ? 100 : (reading.Percent.HasValue ? BatteryCurve.Clamp(reading.Percent.Value) : 0));
            if (fillPercent > 0)
            {
                int fillWidth = Math.Max(barHeight, (int)Math.Round(bar.Width * fillPercent / 100.0));
                Rectangle fill = new Rectangle(bar.X, bar.Y, Math.Min(fillWidth, bar.Width), bar.Height);
                using (GraphicsPath fillPath = RoundedRect(fill, barHeight / 2f))
                using (SolidBrush fillBrush = new SolidBrush(levelColor))
                {
                    g.FillPath(fillBrush, fillPath);
                }
            }

            string status = BuildStatus(reading, failed);
            TextRenderer.DrawText(g, status, _smallFont,
                new Rectangle(pad, top + S(28), Width - pad * 2, S(16)), _secondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private string BuildStatus(DeviceReading reading, bool failed)
        {
            if (failed)
            {
                return string.IsNullOrEmpty(reading.Error) ? "设备未响应" : reading.Error;
            }
            List<string> parts = new List<string>();
            if (reading.Millivolts > 0)
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.00} V", reading.Millivolts / 1000.0));
            }
            if (reading.State == BatteryState.Charging)
            {
                parts.Add("充电中");
            }
            else if (reading.State == BatteryState.Full)
            {
                parts.Add("已充满");
            }
            else if (reading.State == BatteryState.Discharging)
            {
                parts.Add("放电中");
            }
            if (reading.Kind == DeviceKind.Logitech && reading.Percent.HasValue && reading.State == BatteryState.Discharging)
            {
                parts.Add("由电压估算");
            }
            if (reading.Updated != default(DateTime))
            {
                parts.Add(reading.Updated.ToString("HH:mm", CultureInfo.InvariantCulture) + " 更新");
            }
            return string.Join(" · ", parts.ToArray());
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = _refreshRect.Contains(e.Location);
            if (hover != _hoverRefresh)
            {
                _hoverRefresh = hover;
                Cursor = hover ? Cursors.Hand : Cursors.Default;
                Invalidate(_refreshRect);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverRefresh)
            {
                _hoverRefresh = false;
                Cursor = Cursors.Default;
                Invalidate(_refreshRect);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && _refreshRect.Contains(e.Location))
            {
                EventHandler handler = RefreshRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hideGuard.Dispose();
                _titleFont.Dispose();
                _nameFont.Dispose();
                _valueFont.Dispose();
                _smallFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private int S(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
