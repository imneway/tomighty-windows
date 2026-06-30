//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tomighty.Windows.Preferences
{
    internal static class StartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "Tomighty";

        public static bool IsEnabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    var value = key?.GetValue(RunValueName) as string;
                    return string.Equals(value, CurrentCommandLine, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ??
                Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled)
                    key.SetValue(RunValueName, CurrentCommandLine, RegistryValueKind.String);
                else
                    key.DeleteValue(RunValueName, false);
            }
        }

        private static string CurrentCommandLine => "\"" + Application.ExecutablePath + "\"";
    }
}
