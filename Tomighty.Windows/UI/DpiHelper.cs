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
        private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

        public static void EnablePerMonitorV2()
        {
            if (Environment.OSVersion.Version.Major >= 10 && SetProcessDpiAwarenessContext(PerMonitorAwareV2))
                return;

            SetProcessDPIAware();
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiAwarenessContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}
