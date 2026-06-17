//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tomighty.Windows.UI
{
    internal enum BackdropMaterial
    {
        Mica = 2,
        Acrylic = 3,
        MicaAlt = 4
    }

    internal static class Windows11Backdrop
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        private const int DWMWCP_ROUND = 2;
        private static readonly System.Version Windows11Build = new System.Version(10, 0, 22000);

        public static bool IsSupported => Environment.OSVersion.Version.CompareTo(Windows11Build) >= 0;

        public static void ApplyMica(Form form)
        {
            Apply(form, BackdropMaterial.Mica);
        }

        public static void ApplyMicaAlt(Form form)
        {
            Apply(form, BackdropMaterial.MicaAlt);
        }

        public static void ApplyAcrylic(Form form)
        {
            Apply(form, BackdropMaterial.Acrylic, true);
        }

        public static void ApplyAcrylic(ToolStripDropDown dropDown)
        {
            if (dropDown == null)
                return;

            dropDown.HandleCreated += (sender, args) => Apply(dropDown.Handle, BackdropMaterial.Acrylic, true);
            dropDown.Opened += (sender, args) => Apply(dropDown.Handle, BackdropMaterial.Acrylic, true);

            if (dropDown.IsHandleCreated)
                Apply(dropDown.Handle, BackdropMaterial.Acrylic, true);
        }

        private static void Apply(Form form, BackdropMaterial material)
        {
            Apply(form, material, false);
        }

        private static void Apply(Form form, BackdropMaterial material, bool useAcrylicAccent)
        {
            if (form == null)
                return;

            form.HandleCreated += (sender, args) => Apply(form.Handle, material, useAcrylicAccent);

            if (form.IsHandleCreated)
                Apply(form.Handle, material, useAcrylicAccent);
        }

        public static void Refresh(Form form, BackdropMaterial material)
        {
            if (form == null || !form.IsHandleCreated)
                return;

            Apply(form.Handle, material, material == BackdropMaterial.Acrylic);
        }

        private static void Apply(IntPtr handle, BackdropMaterial material, bool useAcrylicAccent)
        {
            if (handle == IntPtr.Zero)
                return;

            var isDarkMode = IsSystemDarkTheme();
            var darkMode = isDarkMode ? 1 : 0;
            TrySetAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, darkMode);
            TrySetAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, darkMode);

            var cornerPreference = DWMWCP_ROUND;
            TrySetAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, cornerPreference);

            var backdrop = (int)material;
            TrySetAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, backdrop);

            if (!useAcrylicAccent)
            {
                var margins = Margins.ExtendIntoClientArea;
                DwmExtendFrameIntoClientArea(handle, ref margins);
            }

            if (useAcrylicAccent)
                TrySetAcrylicAccent(handle, isDarkMode);
        }

        private static void TrySetAttribute(IntPtr handle, int attribute, int value)
        {
            DwmSetWindowAttribute(handle, attribute, ref value, Marshal.SizeOf(typeof(int)));
        }

        private static bool IsSystemDarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key?.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void TrySetAcrylicAccent(IntPtr handle, bool darkMode)
        {
            try
            {
                var tint = darkMode
                    ? ToAbgr(32, 32, 32, 142)
                    : ToAbgr(243, 243, 243, 112);

                var accent = new AccentPolicy
                {
                    AccentState = AccentEnableAcrylicBlurBehind,
                    AccentFlags = 2,
                    GradientColor = tint,
                    AnimationId = 0
                };

                var accentSize = Marshal.SizeOf(typeof(AccentPolicy));
                var accentPtr = Marshal.AllocHGlobal(accentSize);

                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WcaAccentPolicy,
                        SizeOfData = accentSize,
                        Data = accentPtr
                    };

                    SetWindowCompositionAttribute(handle, ref data);
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
            catch
            {
                // DWM system backdrop still provides the fallback material.
            }
        }

        private static int ToAbgr(byte red, byte green, byte blue, byte alpha)
        {
            return (alpha << 24) | (blue << 16) | (green << 8) | red;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        private const int WcaAccentPolicy = 19;
        private const int AccentEnableAcrylicBlurBehind = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;

            public static Margins ExtendIntoClientArea => new Margins
            {
                Left = -1,
                Right = -1,
                Top = -1,
                Bottom = -1
            };
        }
    }
}
