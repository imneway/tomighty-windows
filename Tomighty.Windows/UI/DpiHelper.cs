//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Runtime.InteropServices;

namespace Tomighty.Windows.UI
{
    internal static class DpiHelper
    {
        private const int ErrorAccessDenied = 5;
        private const int ProcessPerMonitorDpiAware = 2;
        private const int SOk = 0;
        private const int EAccessDenied = unchecked((int)0x80070005);

        private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

        public static void EnablePerMonitorV2()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10 && SetProcessDpiAwarenessContext(PerMonitorAwareV2))
                    return;

                if (Marshal.GetLastWin32Error() == ErrorAccessDenied)
                    return;
            }
            catch (EntryPointNotFoundException)
            {
            }

            try
            {
                var result = SetProcessDpiAwareness(ProcessPerMonitorDpiAware);
                if (result == SOk || result == EAccessDenied)
                    return;
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            SetProcessDPIAware();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiAwarenessContext);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();
    }
}
