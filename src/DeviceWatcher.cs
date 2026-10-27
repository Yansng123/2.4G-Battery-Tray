using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BatteryTray
{
    internal sealed class DeviceWatcher : NativeWindow, IDisposable
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int WM_POWERBROADCAST = 0x0218;
        private const int DBT_DEVTYP_DEVICEINTERFACE = 5;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVNODES_CHANGED = 0x0007;
        private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        private const int PBT_APMRESUMESUSPEND = 0x0007;
        private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

        private IntPtr _notificationHandle = IntPtr.Zero;

        public event EventHandler Changed;

        public DeviceWatcher()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = "BatteryTrayDeviceWatcher";
            cp.Parent = (IntPtr)(-3); // HWND_MESSAGE
            CreateHandle(cp);
            Register();
        }

        private void Register()
        {
            if (Handle == IntPtr.Zero)
            {
                return;
            }
            int size = 12 + 16 + 2 * 512;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                byte[] zero = new byte[size];
                Marshal.Copy(zero, 0, buffer, size);
                Marshal.WriteInt32(buffer, 0, size);
                Marshal.WriteInt32(buffer, 4, DBT_DEVTYP_DEVICEINTERFACE);
                byte[] guid = Native.HidInterfaceGuid.ToByteArray();
                Marshal.Copy(guid, 0, new IntPtr(buffer.ToInt64() + 12), guid.Length);
                _notificationHandle = Native.RegisterDeviceNotification(Handle, buffer, DEVICE_NOTIFY_WINDOW_HANDLE);
            }
            catch (Exception)
            {
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE)
            {
                int evt = m.WParam.ToInt32();
                if (evt == DBT_DEVICEARRIVAL || evt == DBT_DEVICEREMOVECOMPLETE || evt == DBT_DEVNODES_CHANGED)
                {
                    RaiseChanged();
                }
            }
            else if (m.Msg == WM_POWERBROADCAST)
            {
                int evt = m.WParam.ToInt32();
                if (evt == PBT_APMRESUMEAUTOMATIC || evt == PBT_APMRESUMESUSPEND)
                {
                    RaiseChanged();
                }
            }
            base.WndProc(ref m);
        }

        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_notificationHandle != IntPtr.Zero)
            {
                Native.UnregisterDeviceNotification(_notificationHandle);
                _notificationHandle = IntPtr.Zero;
            }
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }
}
