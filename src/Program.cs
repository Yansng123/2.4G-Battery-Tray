using System;
using System.Threading;
using System.Windows.Forms;

namespace BatteryTray
{
    internal static class Program
    {
        private static Mutex _singleInstance;

        [STAThread]
        private static void Main(string[] args)
        {
            bool probe = false;
            bool selfTest = false;
            bool dumpIcons = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--probe", StringComparison.OrdinalIgnoreCase))
                {
                    probe = true;
                }
                else if (string.Equals(args[i], "--selftest", StringComparison.OrdinalIgnoreCase))
                {
                    selfTest = true;
                }
                else if (string.Equals(args[i], "--icons", StringComparison.OrdinalIgnoreCase))
                {
                    dumpIcons = true;
                }
            }

            if (probe)
            {
                Probe.Run();
                return;
            }

            if (selfTest)
            {
                Probe.SelfTest();
                return;
            }

            if (dumpIcons)
            {
                Probe.DumpIcons();
                return;
            }

            bool created;
            _singleInstance = new Mutex(true, "Local\\2.4G_Battery_Tray_Mutex", out created);
            if (!created)
            {
                return;
            }

            EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += ThreadExceptionHandler;

            try
            {
                using (TrayController controller = new TrayController())
                {
                    Application.Run(controller);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序遇到错误并退出：\r\n\r\n" + ex.Message, "2.4G 电量",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                GC.KeepAlive(_singleInstance);
                if (_singleInstance != null)
                {
                    _singleInstance.Dispose();
                    _singleInstance = null;
                }
            }
        }

        private static void ThreadExceptionHandler(object sender, ThreadExceptionEventArgs e)
        {
            string message = e.Exception == null ? "未知错误" : e.Exception.Message;
            MessageBox.Show("程序遇到错误：\r\n\r\n" + message, "2.4G 电量",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void EnableDpiAwareness()
        {
            try
            {
                if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4)))
                {
                    Native.SetProcessDPIAware();
                }
            }
            catch (Exception)
            {
                try
                {
                    Native.SetProcessDPIAware();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
