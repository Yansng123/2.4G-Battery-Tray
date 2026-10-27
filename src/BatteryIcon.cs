using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BatteryTray
{
    internal static class BatteryIcon
    {
        public static readonly Color ChargingColor = Color.FromArgb(0, 145, 255);
        public static readonly Color GoodColor = Color.FromArgb(52, 199, 89);
        public static readonly Color WarnColor = Color.FromArgb(255, 159, 10);
        public static readonly Color LowColor = Color.FromArgb(255, 59, 48);
        public static readonly Color MutedColor = Color.FromArgb(150, 150, 150);

        private static readonly string[][] Glyphs = new string[][]
        {
            new string[] { "111", "101", "101", "101", "111" }, // 0
            new string[] { "010", "110", "010", "010", "111" }, // 1
            new string[] { "111", "001", "111", "100", "111" }, // 2
            new string[] { "111", "001", "111", "001", "111" }, // 3
            new string[] { "101", "101", "111", "001", "001" }, // 4
            new string[] { "111", "100", "111", "001", "111" }, // 5
            new string[] { "111", "100", "111", "101", "111" }, // 6
            new string[] { "111", "001", "010", "010", "010" }, // 7
            new string[] { "111", "101", "111", "101", "111" }, // 8
            new string[] { "111", "101", "111", "001", "111" }, // 9
            new string[] { "000", "000", "111", "000", "000" }, // -
            new string[] { "111", "001", "010", "000", "010" }  // ?
        };

        private static readonly string[] BoltMask = new string[]
        {
            "00110",
            "01100",
            "01100",
            "11111",
            "00110",
            "01100",
            "11000",
            "10000"
        };

        private static bool LightTaskbar
        {
            get
            {
                try
                {
                    object value = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                        "SystemUsesLightTheme", 0);
                    return Convert.ToInt32(value) == 1;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private static Color TextColor
        {
            get { return LightTaskbar ? Color.FromArgb(255, 32, 32, 34) : Color.FromArgb(255, 255, 255, 255); }
        }

        private static Color TextShadow
        {
            get { return LightTaskbar ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 38, 38, 40); }
        }

        public static int SmallIconSize
        {
            get
            {
                int size = Native.GetSystemMetrics(49);
                return size > 8 && size <= 64 ? size : 16;
            }
        }

        public static Color ColorFor(DeviceReading reading)
        {
            if (reading == null || !reading.Online || reading.State == BatteryState.Error)
            {
                return MutedColor;
            }
            if (reading.State == BatteryState.Charging)
            {
                return ChargingColor;
            }
            if (!reading.Percent.HasValue)
            {
                return MutedColor;
            }
            int percent = reading.Percent.Value;
            if (percent >= 60)
            {
                return GoodColor;
            }
            if (percent >= 30)
            {
                return WarnColor;
            }
            return LowColor;
        }

        public static Icon Create(DeviceReading reading, int size)
        {
            return Create(reading, size, IconLook.Number);
        }

        public static Icon Create(DeviceReading reading, int size, IconLook look)
        {
            using (Bitmap bitmap = CreateBitmap(reading, size, look))
            {
                return ToIcon(bitmap);
            }
        }

        public static Bitmap CreateBitmap(DeviceReading reading, int size)
        {
            return CreateBitmap(reading, size, IconLook.Number);
        }

        public static Bitmap CreateBitmap(DeviceReading reading, int size, IconLook look)
        {
            if (size < 12)
            {
                size = 12;
            }
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            float s = size / 16f;
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                if (look == IconLook.Plain)
                {
                    DrawPlainBody(g, reading, s);
                }
                else
                {
                    DrawNumberBody(g, reading, s);
                }
            }
            bool charging = reading != null && reading.State == BatteryState.Charging;
            bool hasData = reading != null && reading.Online && reading.State != BatteryState.Error;
            if (look == IconLook.Plain)
            {
                if (charging)
                {
                    PaintBolt(bitmap, new RectangleF(5.8f * s, 5.6f * s, 4.4f * s, 7.6f * s), true);
                }
                else if (!hasData)
                {
                    PaintText(bitmap, "?", s, new RectangleF(5.1f * s, 8.0f * s, 5.8f * s, 5.0f * s));
                }
            }
            else
            {
                if (charging)
                {
                    PaintBolt(bitmap, new RectangleF(5.2f * s, 0.4f * s, 5.6f * s, 8.4f * s), false);
                }
                else
                {
                    string text = BuildText(reading);
                    if (text.Length > 0)
                    {
                        PaintText(bitmap, text, s, new RectangleF(0f, 0.2f * s, 16f * s, 8.4f * s));
                    }
                }
            }
            return bitmap;
        }

        public static Icon CreateEmpty(int size)
        {
            return Create(null, size, IconLook.Number);
        }

        /// <summary>数字在上、横电池在下；电池按电量填充并变色。</summary>
        private static void DrawNumberBody(Graphics g, DeviceReading reading, float s)
        {
            bool hasData = reading != null && reading.Online && reading.State != BatteryState.Error;
            bool charging = reading != null && reading.State == BatteryState.Charging;
            Color levelColor = ColorFor(reading);

            RectangleF body = new RectangleF(1.0f * s, 9.2f * s, 12.8f * s, 5.0f * s);
            RectangleF cap = new RectangleF(13.6f * s, 10.5f * s, 1.5f * s, 2.4f * s);
            float radius = body.Height * 0.30f;
            float stroke = Math.Max(1f, s);

            using (GraphicsPath bodyPath = RoundedRect(body, radius))
            using (Pen outline = new Pen(Color.FromArgb(255, 135, 137, 143), stroke))
            {
                g.DrawPath(outline, bodyPath);
            }

            int percent = -1;
            if (hasData)
            {
                if (charging)
                {
                    percent = 100;
                }
                else if (reading.Percent.HasValue)
                {
                    percent = BatteryCurve.Clamp(reading.Percent.Value);
                }
            }
            if (percent > 0)
            {
                float innerW = body.Width - 2f * s;
                float fillW = Math.Max(0.8f * s, innerW * percent / 100f);
                RectangleF fill = new RectangleF(body.X + s, body.Y + s, fillW, body.Height - 2f * s);
                GraphicsState state = g.Save();
                using (GraphicsPath clipPath = RoundedRect(body, radius))
                {
                    g.SetClip(clipPath, CombineMode.Intersect);
                }
                using (GraphicsPath fillPath = RoundedRect(fill, radius * 0.5f))
                using (SolidBrush fillBrush = new SolidBrush(levelColor))
                {
                    g.FillPath(fillBrush, fillPath);
                }
                g.Restore(state);
            }

            using (GraphicsPath capPath = RoundedRect(cap, 0.5f * s))
            using (SolidBrush capBrush = new SolidBrush(hasData ? Color.FromArgb(255, 160, 162, 168) : Color.FromArgb(255, 120, 120, 124)))
            {
                g.FillPath(capBrush, capPath);
            }
        }

        /// <summary>纯图标：竖放的彩色电池，不带数字。</summary>
        private static void DrawPlainBody(Graphics g, DeviceReading reading, float s)
        {
            bool hasData = reading != null && reading.Online && reading.State != BatteryState.Error;
            Color fillColor = hasData ? ColorFor(reading) : Color.FromArgb(255, 160, 162, 168);

            RectangleF body = new RectangleF(5.1f * s, 4.2f * s, 5.8f * s, 10.2f * s);
            RectangleF cap = new RectangleF(6.7f * s, 1.5f * s, 2.6f * s, 2.5f * s);
            float stroke = Math.Max(1f, s * 0.9f);

            using (GraphicsPath bodyPath = RoundedRect(body, 1.3f * s))
            using (SolidBrush brush = new SolidBrush(fillColor))
            using (Pen edge = new Pen(Color.FromArgb(110, 0, 0, 0), stroke))
            {
                g.FillPath(brush, bodyPath);
                g.DrawPath(edge, bodyPath);
            }
            using (GraphicsPath capPath = RoundedRect(cap, 0.7f * s))
            using (SolidBrush capBrush = new SolidBrush(fillColor))
            using (Pen edge = new Pen(Color.FromArgb(110, 0, 0, 0), stroke))
            {
                g.FillPath(capBrush, capPath);
                g.DrawPath(edge, capPath);
            }
        }

        private static void PaintText(Bitmap bitmap, string text, float s, RectangleF area)
        {
            int scale = Math.Max(1, (int)Math.Round(s));
            int charW = 3 * scale;
            int gap = scale;
            int textW = text.Length * charW + (text.Length - 1) * gap;
            int textH = 5 * scale;
            int x = (int)Math.Round(area.X + (area.Width - textW) / 2f);
            int y = (int)Math.Round(area.Y + (area.Height - textH) / 2f);

            List<Point> points = new List<Point>();
            int cursor = x;
            for (int i = 0; i < text.Length; i++)
            {
                int glyph = GlyphIndex(text[i]);
                if (glyph >= 0)
                {
                    string[] rows = Glyphs[glyph];
                    for (int row = 0; row < 5; row++)
                    {
                        string line = rows[row];
                        for (int col = 0; col < 3; col++)
                        {
                            if (line[col] == '1')
                            {
                                points.Add(new Point(cursor + col * scale, y + row * scale));
                            }
                        }
                    }
                }
                cursor += 4 * scale;
            }
            PaintPoints(bitmap, points, scale, TextColor, TextShadow);
        }

        private static void PaintBolt(Bitmap bitmap, RectangleF area, bool onBattery)
        {
            int maskW = BoltMask[0].Length;
            int maskH = BoltMask.Length;
            int scale = Math.Max(1, (int)Math.Floor(Math.Min(area.Width / maskW, area.Height / maskH)));
            int width = maskW * scale;
            int height = maskH * scale;
            int x = (int)Math.Round(area.X + (area.Width - width) / 2f);
            int y = (int)Math.Round(area.Y + (area.Height - height) / 2f);
            List<Point> points = new List<Point>();
            for (int row = 0; row < maskH; row++)
            {
                string line = BoltMask[row];
                for (int col = 0; col < maskW; col++)
                {
                    if (line[col] == '1')
                    {
                        points.Add(new Point(x + col * scale, y + row * scale));
                    }
                }
            }
            PaintPoints(bitmap, points, scale,
                onBattery ? Color.FromArgb(255, 255, 255, 255) : TextColor,
                onBattery ? Color.FromArgb(0, 0, 0, 0) : TextShadow);
        }

        private static void PaintPoints(Bitmap bitmap, List<Point> points, int block, Color color, Color shadow)
        {
            if (points.Count == 0)
            {
                return;
            }
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buffer = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

                if (shadow.A > 0)
                {
                    for (int i = 0; i < points.Count; i++)
                    {
                        BlitBlock(buffer, stride, bitmap.Width, bitmap.Height, points[i].X + block, points[i].Y + block, block, shadow);
                    }
                }
                for (int i = 0; i < points.Count; i++)
                {
                    BlitBlock(buffer, stride, bitmap.Width, bitmap.Height, points[i].X, points[i].Y, block, color);
                }
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static void BlitBlock(byte[] buffer, int stride, int width, int height, int x, int y, int block, Color color)
        {
            for (int dy = 0; dy < block; dy++)
            {
                int py = y + dy;
                if (py < 0 || py >= height)
                {
                    continue;
                }
                for (int dx = 0; dx < block; dx++)
                {
                    int px = x + dx;
                    if (px < 0 || px >= width)
                    {
                        continue;
                    }
                    int offset = py * stride + px * 4;
                    buffer[offset] = color.B;
                    buffer[offset + 1] = color.G;
                    buffer[offset + 2] = color.R;
                    buffer[offset + 3] = color.A;
                }
            }
        }

        private static int GlyphIndex(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }
            if (c == '-')
            {
                return 10;
            }
            if (c == '?')
            {
                return 11;
            }
            return -1;
        }

        private static string BuildText(DeviceReading reading)
        {
            if (reading == null)
            {
                return "?";
            }
            if (!reading.Online || reading.State == BatteryState.Error)
            {
                return "?";
            }
            if (reading.State == BatteryState.Charging && !reading.Percent.HasValue)
            {
                return string.Empty;
            }
            if (!reading.Percent.HasValue)
            {
                return "--";
            }
            return reading.Percent.Value.ToString();
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d <= 0.5f)
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

        private static Icon ToIcon(Bitmap bitmap)
        {
            IntPtr handle = bitmap.GetHicon();
            try
            {
                using (Icon temporary = Icon.FromHandle(handle))
                {
                    return (Icon)temporary.Clone();
                }
            }
            finally
            {
                Native.DestroyIcon(handle);
            }
        }

        public static Icon AppIcon()
        {
            return Create(null, 32);
        }
    }
}
