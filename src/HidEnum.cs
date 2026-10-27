using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryTray
{
    internal sealed class HidInterface
    {
        public string Path;
        public ushort VendorId;
        public ushort ProductId;
        public ushort Version;
        public ushort UsagePage;
        public ushort Usage;
        public ushort InputLength;
        public ushort OutputLength;
        public ushort FeatureLength;

        public override string ToString()
        {
            return string.Format("vid={0:X4} pid={1:X4} up={2:X4} usage={3:X4} in={4} out={5} feat={6}",
                VendorId, ProductId, UsagePage, Usage, InputLength, OutputLength, FeatureLength);
        }
    }

    internal static class HidEnum
    {
        public static List<HidInterface> Enumerate()
        {
            List<HidInterface> list = new List<HidInterface>();
            Guid guid = Native.HidInterfaceGuid;
            IntPtr set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (set == (IntPtr)(-1) || set == IntPtr.Zero)
            {
                return list;
            }
            try
            {
                Native.SP_DEVICE_INTERFACE_DATA did = new Native.SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(typeof(Native.SP_DEVICE_INTERFACE_DATA));
                for (uint i = 0; ; i++)
                {
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did))
                    {
                        break;
                    }
                    uint needed = 0;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out needed, IntPtr.Zero);
                    if (needed == 0)
                    {
                        continue;
                    }
                    IntPtr detail = Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref did, detail, needed, out needed, IntPtr.Zero))
                        {
                            continue;
                        }
                        string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                        if (string.IsNullOrEmpty(path))
                        {
                            continue;
                        }
                        HidInterface info = Describe(path);
                        if (info != null)
                        {
                            list.Add(info);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(set);
            }
            return list;
        }

        private static HidInterface Describe(string path)
        {
            IntPtr h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == (IntPtr)(-1) || h == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                Native.HIDD_ATTRIBUTES attrs = new Native.HIDD_ATTRIBUTES();
                attrs.Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
                if (!Native.HidD_GetAttributes(h, ref attrs))
                {
                    return null;
                }
                Native.HIDP_CAPS caps = new Native.HIDP_CAPS();
                IntPtr preparsed;
                if (!Native.HidD_GetPreparsedData(h, out preparsed))
                {
                    return null;
                }
                try
                {
                    if (Native.HidP_GetCaps(preparsed, ref caps) != Native.HIDP_STATUS_SUCCESS)
                    {
                        return null;
                    }
                }
                finally
                {
                    Native.HidD_FreePreparsedData(preparsed);
                }

                HidInterface info = new HidInterface();
                info.Path = path;
                info.VendorId = attrs.VendorID;
                info.ProductId = attrs.ProductID;
                info.Version = attrs.VersionNumber;
                info.UsagePage = caps.UsagePage;
                info.Usage = caps.Usage;
                info.InputLength = caps.InputReportByteLength;
                info.OutputLength = caps.OutputReportByteLength;
                info.FeatureLength = caps.FeatureReportByteLength;
                return info;
            }
            finally
            {
                Native.CloseHandle(h);
            }
        }

        public static string ReadProductString(string path)
        {
            IntPtr h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == (IntPtr)(-1) || h == IntPtr.Zero)
            {
                return string.Empty;
            }
            try
            {
                StringBuilder sb = new StringBuilder(256);
                if (Native.HidD_GetProductString(h, sb, (uint)(sb.Capacity * 2)))
                {
                    return sb.ToString();
                }
                return string.Empty;
            }
            finally
            {
                Native.CloseHandle(h);
            }
        }

        public static string Hex(byte[] bytes)
        {
            if (bytes == null)
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(bytes[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
