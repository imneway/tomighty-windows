using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Tomighty;
using Tomighty.Windows;
using Tomighty.Windows.About;
using Tomighty.Windows.Preferences;

namespace Tomighty.Tools
{
    internal static class DialogSmokeHost
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: DialogSmokeHost.exe <output-directory>");
                return 2;
            }

            SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Directory.CreateDirectory(args[0]);

            Capture("preferences.png", args[0], () => new UserPreferencesForm(new SmokePreferences()));
            Capture("preferences-miscellaneous.png", args[0], () =>
            {
                var form = new UserPreferencesForm(new SmokePreferences());
                var tabs = FindControl<TabControl>(form);
                if (tabs != null)
                    tabs.SelectedIndex = 2;
                return form;
            });
            Capture("about.png", args[0], () => new AboutWindow());
            Capture("error.png", args[0], () => new ErrorReportWindow(new Exception("UI smoke error")));

            return 0;
        }

        private static void Capture(string fileName, string outputDirectory, Func<Form> formFactory)
        {
            using (var form = formFactory())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(80, 80);
                form.Show();
                Application.DoEvents();
                Thread.Sleep(250);
                Application.DoEvents();

                CaptureForm(form, Path.Combine(outputDirectory, fileName));
                form.Close();
            }
        }

        private static T FindControl<T>(Control parent) where T : Control
        {
            foreach (Control control in parent.Controls)
            {
                if (control is T)
                    return (T)control;

                var child = FindControl<T>(control);
                if (child != null)
                    return child;
            }

            return null;
        }

        private static void CaptureForm(Form form, string output)
        {
            var bounds = new Rectangle(0, 0, form.Width, form.Height);
            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                form.DrawToBitmap(bitmap, bounds);
                bitmap.Save(output, ImageFormat.Png);
            }
        }

        private sealed class SmokePreferences : IMutableUserPreferences
        {
            private Duration pomodoro = Duration.InMinutes(25);
            private Duration shortBreak = Duration.InMinutes(5);
            private Duration longBreak = Duration.InMinutes(15);

            public int MaxPomodoroCount { get; set; } = 4;
            public bool ShowToastNotifications { get; set; } = true;
            public bool PlaySoundNotifications { get; set; } = true;
            public bool ShowRemainingTimeOnTray { get; set; } = true;
            public bool SuppressPopupInFullscreenApps { get; set; } = true;

            public Duration GetIntervalDuration(IntervalType intervalType)
            {
                if (intervalType == IntervalType.Pomodoro)
                    return pomodoro;

                if (intervalType == IntervalType.ShortBreak)
                    return shortBreak;

                return longBreak;
            }

            public void SetIntervalDuration(IntervalType intervalType, Duration duration)
            {
                if (intervalType == IntervalType.Pomodoro)
                    pomodoro = duration;
                else if (intervalType == IntervalType.ShortBreak)
                    shortBreak = duration;
                else
                    longBreak = duration;
            }

            public void Update(Action<IMutableUserPreferences> action)
            {
                action(this);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    }
}
