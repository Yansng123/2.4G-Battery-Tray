using System;

namespace BatteryTray
{
    internal static class BatteryCurve
    {
        private static readonly int[] Millivolts = { 3500, 3650, 3700, 3750, 3800, 3850, 3900, 3950, 4000, 4050, 4100, 4150, 4186 };
        private static readonly int[] Percents = { 0, 10, 20, 30, 40, 50, 62, 72, 80, 85, 90, 95, 100 };

        public static int FromMillivolts(int millivolts)
        {
            if (millivolts <= Millivolts[0])
            {
                return 0;
            }
            if (millivolts >= Millivolts[Millivolts.Length - 1])
            {
                return 100;
            }
            for (int i = 1; i < Millivolts.Length; i++)
            {
                if (millivolts <= Millivolts[i])
                {
                    double span = Millivolts[i] - Millivolts[i - 1];
                    double t = span <= 0 ? 0 : (millivolts - Millivolts[i - 1]) / span;
                    double value = Percents[i - 1] + t * (Percents[i] - Percents[i - 1]);
                    int rounded = (int)Math.Round(value);
                    if (rounded < 0)
                    {
                        rounded = 0;
                    }
                    if (rounded > 100)
                    {
                        rounded = 100;
                    }
                    return rounded;
                }
            }
            return 100;
        }

        public static int Clamp(int value)
        {
            if (value < 0)
            {
                return 0;
            }
            if (value > 100)
            {
                return 100;
            }
            return value;
        }
    }
}
