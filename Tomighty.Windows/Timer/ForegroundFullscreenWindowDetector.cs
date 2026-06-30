//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Tomighty.Windows.Timer
{
    internal sealed class ForegroundFullscreenWindowDetector : IFullscreenWindowDetector
    {
        private const int FullscreenTolerancePixels = 4;
        private const int DwmExtendedFrameBounds = 9;

        public bool IsFullscreenWindowActiveOn(Screen screen, IntPtr ignoredWindowHandle)
        {
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero || foregroundWindow == ignoredWindowHandle)
                return false;

            if (ignoredWindowHandle != IntPtr.Zero && IsChild(ignoredWindowHandle, foregroundWindow))
                return false;

            if (!IsWindowVisible(foregroundWindow) || IsShellWindow(foregroundWindow))
                return false;

            Rectangle foregroundBounds;
            if (!TryGetWindowBounds(foregroundWindow, out foregroundBounds))
                return false;

            var foregroundScreen = Screen.FromHandle(foregroundWindow);
            if (!Equals(foregroundScreen.DeviceName, screen.DeviceName))
                return false;

            return CoversScreen(foregroundBounds, screen.Bounds);
        }

        private static bool CoversScreen(Rectangle windowBounds, Rectangle screenBounds)
        {
            return windowBounds.Left <= screenBounds.Left + FullscreenTolerancePixels
                && windowBounds.Top <= screenBounds.Top + FullscreenTolerancePixels
                && windowBounds.Right >= screenBounds.Right - FullscreenTolerancePixels
                && windowBounds.Bottom >= screenBounds.Bottom - FullscreenTolerancePixels;
        }

        private static bool TryGetWindowBounds(IntPtr handle, out Rectangle bounds)
        {
            Rect rect;
            if (DwmGetWindowAttribute(handle, DwmExtendedFrameBounds, out rect, Marshal.SizeOf(typeof(Rect))) == 0)
            {
                bounds = rect.ToRectangle();
                return !bounds.IsEmpty;
            }

            if (GetWindowRect(handle, out rect))
            {
                bounds = rect.ToRectangle();
                return !bounds.IsEmpty;
            }

            bounds = Rectangle.Empty;
            return false;
        }

        private static bool IsShellWindow(IntPtr handle)
        {
            var className = GetClassName(handle);
            return className == "Progman"
                || className == "WorkerW"
                || className == "Shell_TrayWnd"
                || className == "Shell_SecondaryTrayWnd"
                || className == "NotifyIconOverflowWindow";
        }

        private static string GetClassName(IntPtr handle)
        {
            var className = new StringBuilder(256);
            GetClassName(handle, className, className.Capacity);
            return className.ToString();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out Rect pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public Rectangle ToRectangle()
            {
                return Rectangle.FromLTRB(Left, Top, Right, Bottom);
            }
        }
    }
}
