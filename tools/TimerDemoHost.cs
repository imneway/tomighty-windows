using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Tomighty.Windows.Timer;

namespace Tomighty.Tools
{
    internal static class TimerDemoHost
    {
        [STAThread]
        private static int Main(string[] args)
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var screen = SelectScreen(args.Length > 0 ? args[0] : "primary");
            var probePath = args.Length > 1 ? args[1] : string.Empty;
            SetThemeOverride(args.Length > 2 ? args[2] : "system");

            using (var form = new TimerWindow())
            {
                form.Text = "Tomighty UI Demo";
                form.UpdateTitle("Pomodoro");
                form.UpdateTimeDisplay("25:00");
                form.UpdateColorScheme(Color.FromArgb(197, 47, 61));
                form.SetTimerAction("Interrupt", () => { });
                form.StartPosition = FormStartPosition.Manual;
                if (!form.IsHandleCreated && form.Handle == IntPtr.Zero)
                    return 1;

                form.Location = CenterOnScreen(form, screen);
                form.Shown += (sender, eventArgs) =>
                {
                    WriteProbe(probePath, form, screen);
                    form.Activate();
                };

                Application.Run(form);
            }

            return 0;
        }

        private static Screen SelectScreen(string target)
        {
            var screens = Screen.AllScreens;
            if (string.Equals(target, "secondary", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(target, "small", StringComparison.OrdinalIgnoreCase))
            {
                return screens.Where(screen => !screen.Primary)
                    .OrderBy(screen => screen.WorkingArea.Width * screen.WorkingArea.Height)
                    .FirstOrDefault() ?? Screen.PrimaryScreen;
            }

            return Screen.PrimaryScreen;
        }

        private static void SetThemeOverride(string theme)
        {
            bool? lightTheme = null;
            if (string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase))
                lightTheme = true;
            else if (string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase))
                lightTheme = false;

            var themeType = typeof(TimerWindow).Assembly.GetType("Tomighty.Windows.UI.ModernTheme", true);
            var method = themeType.GetMethod("SetThemeOverrideForTooling", BindingFlags.Static | BindingFlags.NonPublic);
            method.Invoke(null, new object[] { lightTheme });
        }

        private static Point CenterOnScreen(Form form, Screen screen)
        {
            var bounds = screen.WorkingArea;
            return new Point(
                bounds.Left + (bounds.Width - form.Width) / 2,
                bounds.Top + (bounds.Height - form.Height) / 2);
        }

        private static void WriteProbe(string path, Form form, Screen screen)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                $"dpi={GetDpiForWindow(form.Handle)}{Environment.NewLine}" +
                $"scale={GetDpiForWindow(form.Handle) / 96f:0.##}{Environment.NewLine}" +
                $"screen={screen.DeviceName}{Environment.NewLine}" +
                $"screen_bounds={screen.Bounds}{Environment.NewLine}" +
                $"bounds={form.Bounds}{Environment.NewLine}" +
                $"client_size={form.ClientSize}{Environment.NewLine}");
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);
    }
}
