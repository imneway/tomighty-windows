using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Tomighty.Events;
using Tomighty.Windows.Timer;

namespace Tomighty.Tools
{
    internal static class TimerSmokeHost
    {
        private static readonly Color ActionButtonBackColor = Color.FromArgb(197, 47, 61);
        private static readonly Color BreakActionButtonBackColor = Color.FromArgb(0, 155, 59);
        private static readonly Color NeutralActionButtonBackColor = Color.FromArgb(133, 137, 145);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: TimerSmokeHost.exe <output.png>");
                return 2;
            }

            SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var backdrop = new ProbeBackdropForm())
            {
                backdrop.Show();
                Application.DoEvents();
                Thread.Sleep(250);
                Application.DoEvents();

                backdrop.Advance();
                Application.DoEvents();
                Thread.Sleep(450);
                Application.DoEvents();

                CaptureTheme(args[0], true, "timer-window-states", "timer-window-driven-states", "timer-window-driven.png", backdrop);
                CaptureTheme(
                    Path.Combine(Path.GetDirectoryName(args[0]), "timer-window-smoke-dark.png"),
                    false,
                    "timer-window-states-dark",
                    "timer-window-driven-states-dark",
                    "timer-window-driven-dark.png",
                    backdrop);
                SetThemeOverride(null);
                backdrop.Close();
            }

            return 0;
        }

        private static void CaptureTheme(string primaryOutput, bool lightTheme, string statesDirectoryName, string drivenDirectoryName, string drivenFileName, IWin32Window owner)
        {
            SetThemeOverride(lightTheme);

            CaptureStates(primaryOutput, statesDirectoryName, new Point(80, 80), owner);
            CapturePresenterDrivenStates(
                Path.Combine(Path.GetDirectoryName(primaryOutput), drivenFileName),
                drivenDirectoryName,
                new Point(80, 80),
                owner);
        }

        private static void SetThemeOverride(bool? lightTheme)
        {
            var themeType = typeof(TimerWindow).Assembly.GetType("Tomighty.Windows.UI.ModernTheme", true);
            var method = themeType.GetMethod("SetThemeOverrideForTooling", BindingFlags.Static | BindingFlags.NonPublic);
            method.Invoke(null, new object[] { lightTheme });
        }

        private static void CaptureStates(string primaryOutput, string outputDirectoryName, Point location, IWin32Window owner)
        {
            var outputDirectory = Path.Combine(Path.GetDirectoryName(primaryOutput), outputDirectoryName);
            Directory.CreateDirectory(outputDirectory);
            foreach (var stalePng in Directory.GetFiles(outputDirectory, "*.png"))
                File.Delete(stalePng);

            var shots = new[]
            {
                TimerStateShot.Timed("01-pomodoro", "Pomodoro", "25:00", "Interrupt", ActionButtonBackColor),
                TimerStateShot.Timed("02-short-break", "Short Break", "05:00", "Interrupt", BreakActionButtonBackColor),
                TimerStateShot.Timed("03-long-break", "Long Break", "15:00", "Interrupt", BreakActionButtonBackColor),
                TimerStateShot.Message("04-idle", "Idle", "Start Pomodoro", ActionButtonBackColor),
                TimerStateShot.MessageActions("05-pomodoro-finished", "Pomodoro finished", "Take a break", "Short", BreakActionButtonBackColor, "Long", BreakActionButtonBackColor),
                TimerStateShot.Message("06-break-finished", "Break Finished", "Start Pomodoro", ActionButtonBackColor),
                TimerStateShot.Message("07-pomodoro-interrupted", "Pomodoro interrupted", "Restart", NeutralActionButtonBackColor),
                TimerStateShot.Message("08-short-break-interrupted", "Short break interrupted", "Restart", NeutralActionButtonBackColor),
                TimerStateShot.Message("09-long-break-interrupted", "Long break interrupted", "Restart", NeutralActionButtonBackColor)
            };

            var images = new Bitmap[shots.Length];
            try
            {
                for (var index = 0; index < shots.Length; index++)
                {
                    using (var form = CreateTimerWindow(shots[index], location, owner))
                    {
                        Application.DoEvents();
                        Thread.Sleep(250);
                        Application.DoEvents();

                        var shotPath = Path.Combine(outputDirectory, shots[index].FileName + ".png");
                        CaptureWindow(form.Handle, shotPath);
                        images[index] = new Bitmap(shotPath);

                        if (index == 0)
                            images[index].Save(primaryOutput, ImageFormat.Png);

                        form.Close();
                    }
                }

                SaveContactSheet(images, Path.Combine(outputDirectory, "all-states.png"));
            }
            finally
            {
                for (var index = 0; index < images.Length; index++)
                {
                    if (images[index] != null)
                        images[index].Dispose();
                }
            }
        }

        private static void CapturePresenterDrivenStates(string primaryOutput, string outputDirectoryName, Point location, IWin32Window owner)
        {
            var outputDirectory = Path.Combine(Path.GetDirectoryName(primaryOutput), outputDirectoryName);
            Directory.CreateDirectory(outputDirectory);
            foreach (var stalePng in Directory.GetFiles(outputDirectory, "*.png"))
                File.Delete(stalePng);

            var eventHub = new SynchronousEventHub();
            var timer = new Tomighty.Timer(eventHub);
            var preferences = new SmokeUserPreferences();
            var engine = new PomodoroEngine(timer, preferences, eventHub);
            var presenter = CreateTimerWindowPresenter(engine, timer, eventHub);
            var images = new List<Bitmap>();

            try
            {
                InvokePresenterToggle(presenter, location);
                var window = GetPresenterWindow(presenter);
                window.TopMost = true;
                window.Location = location;
                window.Activate();
                window.BringToFront();
                PumpUi();

                CaptureAndAssertDrivenState(outputDirectory, images, window, "01-idle", "Idle", false, "Start Pomodoro", ActionButtonBackColor);

                engine.StartTimer(IntervalType.Pomodoro);
                PumpUi();
                CaptureAndAssertDrivenState(outputDirectory, images, window, "02-pomodoro-running", "Pomodoro", true, "Interrupt", ActionButtonBackColor);

                ClickPrimaryAction(window);
                PumpUi();
                CaptureAndAssertDrivenState(outputDirectory, images, window, "03-pomodoro-interrupted", "Pomodoro interrupted", false, "Restart", NeutralActionButtonBackColor);

                engine.StartTimer(IntervalType.ShortBreak);
                PumpUi();
                CaptureAndAssertDrivenState(outputDirectory, images, window, "04-short-break-running", "Short Break", true, "Interrupt", BreakActionButtonBackColor);

                ClickPrimaryAction(window);
                PumpUi();
                CaptureAndAssertDrivenState(outputDirectory, images, window, "05-short-break-interrupted", "Short break interrupted", false, "Restart", NeutralActionButtonBackColor);

                engine.StartTimer(IntervalType.LongBreak);
                PumpUi();
                CaptureAndAssertDrivenState(outputDirectory, images, window, "06-long-break-running", "Long Break", true, "Interrupt", BreakActionButtonBackColor);

                ClickPrimaryAction(window);
                PumpUi();
                CaptureAndAssertDrivenState(outputDirectory, images, window, "07-long-break-interrupted", "Long break interrupted", false, "Restart", NeutralActionButtonBackColor);

                eventHub.Publish(new TimerStopped(IntervalType.Pomodoro, preferences.GetIntervalDuration(IntervalType.Pomodoro), Duration.Zero));
                PumpUi();
                CaptureAndAssertDrivenState(
                    outputDirectory,
                    images,
                    window,
                    "08-pomodoro-finished",
                    "Pomodoro finished",
                    false,
                    "Short",
                    BreakActionButtonBackColor,
                    "Long",
                    BreakActionButtonBackColor,
                    "Take a break");

                eventHub.Publish(new TimerStopped(IntervalType.ShortBreak, preferences.GetIntervalDuration(IntervalType.ShortBreak), Duration.Zero));
                PumpUi();
                CaptureAndAssertDrivenState(outputDirectory, images, window, "09-break-finished", "Break Finished", false, "Start Pomodoro", ActionButtonBackColor);

                SaveContactSheet(images.ToArray(), Path.Combine(outputDirectory, "all-driven-states.png"));
                File.Copy(Path.Combine(outputDirectory, "all-driven-states.png"), primaryOutput, true);
                window.Close();
            }
            finally
            {
                foreach (var image in images)
                    image.Dispose();
            }
        }

        private static object CreateTimerWindowPresenter(IPomodoroEngine engine, ICountdownClock countdownClock, IEventHub eventHub)
        {
            var presenterType = typeof(TimerWindow).Assembly.GetType("Tomighty.Windows.Timer.TimerWindowPresenter", true);
            return Activator.CreateInstance(
                presenterType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { engine, countdownClock, eventHub },
                null);
        }

        private static void InvokePresenterToggle(object presenter, Point location)
        {
            var method = presenter.GetType().GetMethod("Toggle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method.Invoke(presenter, new object[] { location });
        }

        private static TimerWindow GetPresenterWindow(object presenter)
        {
            var field = presenter.GetType().GetField("window", BindingFlags.Instance | BindingFlags.NonPublic);
            var window = field.GetValue(presenter) as TimerWindow;
            if (window == null)
                throw new InvalidOperationException("TimerWindowPresenter did not create a timer window.");

            return window;
        }

        private static void ClickPrimaryAction(TimerWindow window)
        {
            var bounds = (Rectangle)typeof(TimerWindow)
                .GetMethod("GetPrimaryActionButtonBounds", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[0]);
            var x = bounds.Left + bounds.Width / 2;
            var y = bounds.Top + bounds.Height / 2;
            var args = new MouseEventArgs(MouseButtons.Left, 1, x, y, 0);

            typeof(TimerWindow).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { args });
            typeof(TimerWindow).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { args });
        }

        private static void CaptureAndAssertDrivenState(
            string outputDirectory,
            List<Bitmap> images,
            TimerWindow window,
            string fileName,
            string title,
            bool showTime,
            string primaryActionText,
            Color primaryActionColor,
            string secondaryActionText = "",
            Color? secondaryActionColor = null,
            string messageText = "")
        {
            AssertTimerWindowState(window, title, showTime, primaryActionText, primaryActionColor, secondaryActionText, secondaryActionColor, messageText);

            var path = Path.Combine(outputDirectory, fileName + ".png");
            CaptureWindow(window.Handle, path);
            images.Add(new Bitmap(path));
        }

        private static void AssertTimerWindowState(
            TimerWindow window,
            string title,
            bool showTime,
            string primaryActionText,
            Color primaryActionColor,
            string secondaryActionText,
            Color? secondaryActionColor,
            string messageText)
        {
            AssertEqual("title", title, GetPrivateField<string>(window, "titleText"));
            AssertEqual("timeDisplayVisible", showTime, GetPrivateField<bool>(window, "timeDisplayVisible"));
            AssertEqual("messageText", messageText, GetPrivateField<string>(window, "messageText"));
            AssertEqual("timerWindowForeColor", GetModernThemeColor("TimerTextColor").ToArgb(), window.ForeColor.ToArgb());
            AssertEqual("timerWindowBackColor", GetModernThemeColor("TimerWindowBackColor").ToArgb(), window.BackColor.ToArgb());
            AssertEqual("primaryActionText", primaryActionText, GetPrivateField<string>(window, "primaryActionText"));
            AssertEqual("primaryActionButtonColor", primaryActionColor.ToArgb(), GetPrivateField<Color>(window, "primaryActionButtonColor").ToArgb());
            AssertEqual("secondaryActionText", secondaryActionText, GetPrivateField<string>(window, "secondaryActionText"));

            if (!string.IsNullOrEmpty(secondaryActionText))
            {
                AssertEqual("secondaryActionButtonColor", secondaryActionColor.GetValueOrDefault().ToArgb(), GetPrivateField<Color>(window, "secondaryActionButtonColor").ToArgb());
                AssertHorizontalActionButtons(window);
            }
        }

        private static void AssertHorizontalActionButtons(TimerWindow window)
        {
            var primaryBounds = (Rectangle)typeof(TimerWindow)
                .GetMethod("GetPrimaryActionButtonBounds", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[0]);
            var secondaryBounds = (Rectangle)typeof(TimerWindow)
                .GetMethod("GetSecondaryActionButtonBounds", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[0]);
            var layoutScale = GetPrivateField<float>(window, "layoutScale");
            var expectedGap = (int)Math.Round(4f * layoutScale);

            AssertEqual("secondaryActionY", primaryBounds.Top, secondaryBounds.Top);
            AssertEqual("actionButtonGap", expectedGap, secondaryBounds.Left - primaryBounds.Right);
        }

        private static T GetPrivateField<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }

        private static Color GetModernThemeColor(string propertyName)
        {
            var themeType = typeof(TimerWindow).Assembly.GetType("Tomighty.Windows.UI.ModernTheme", true);
            return (Color)themeType.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
        }

        private static void PumpUi()
        {
            for (var index = 0; index < 5; index++)
            {
                Application.DoEvents();
                Thread.Sleep(80);
            }
        }

        private static TimerWindow CreateTimerWindow(TimerStateShot shot, Point location, IWin32Window owner)
        {
            var form = new TimerWindow();
            form.UpdateTitle(shot.Title);
            if (shot.ShowTime)
                form.UpdateTimeDisplay(shot.Time);
            else
                form.HideTimeDisplay();

            form.UpdateColorScheme(shot.AccentColor);
            if (shot.HasMessage && shot.HasSecondaryAction)
                form.SetTimerMessageActions(shot.MessageText, shot.PrimaryActionText, () => { }, shot.PrimaryActionColor, shot.SecondaryActionText, () => { }, shot.SecondaryActionColor);
            else if (shot.HasSecondaryAction)
                form.SetTimerActions(shot.PrimaryActionText, () => { }, shot.PrimaryActionColor, shot.SecondaryActionText, () => { }, shot.SecondaryActionColor);
            else
                form.SetTimerAction(shot.PrimaryActionText, () => { }, shot.PrimaryActionColor);

            form.StartPosition = FormStartPosition.Manual;

            if (!form.IsHandleCreated && form.Handle == IntPtr.Zero)
                throw new InvalidOperationException("TimerWindow handle was not created.");

            form.Location = location;
            form.Show(owner);
            form.Activate();
            form.BringToFront();
            SetForegroundWindow(form.Handle);
            form.Invalidate(true);
            form.Update();
            return form;
        }

        private static void SaveContactSheet(Bitmap[] images, string output)
        {
            if (images.Length == 0 || images[0] == null)
                return;

            const int columns = 2;
            const int gap = 24;
            var rows = (int)Math.Ceiling(images.Length / (double)columns);
            var cellWidth = 0;
            var cellHeight = 0;
            for (var index = 0; index < images.Length; index++)
            {
                if (images[index] == null)
                    continue;

                cellWidth = Math.Max(cellWidth, images[index].Width);
                cellHeight = Math.Max(cellHeight, images[index].Height);
            }

            var width = columns * cellWidth + (columns + 1) * gap;
            var height = rows * cellHeight + (rows + 1) * gap;

            using (var sheet = new Bitmap(width, height))
            using (var graphics = Graphics.FromImage(sheet))
            {
                graphics.Clear(Color.White);

                for (var index = 0; index < images.Length; index++)
                {
                    var column = index % columns;
                    var row = index / columns;
                    var x = gap + column * (cellWidth + gap);
                    var y = gap + row * (cellHeight + gap);
                    graphics.DrawImageUnscaled(images[index], x + (cellWidth - images[index].Width) / 2, y);
                }

                sheet.Save(output, ImageFormat.Png);
            }
        }

        private sealed class TimerStateShot
        {
            public readonly string FileName;
            public readonly string Title;
            public readonly string Time;
            public readonly string MessageText;
            public readonly bool ShowTime;
            public readonly Color AccentColor;
            public readonly string PrimaryActionText;
            public readonly Color PrimaryActionColor;
            public readonly string SecondaryActionText;
            public readonly Color SecondaryActionColor;

            private TimerStateShot(string fileName, string title, string time, string messageText, bool showTime, Color accentColor, string primaryActionText, Color primaryActionColor, string secondaryActionText, Color secondaryActionColor)
            {
                FileName = fileName;
                Title = title;
                Time = time;
                MessageText = messageText;
                ShowTime = showTime;
                AccentColor = accentColor;
                PrimaryActionText = primaryActionText;
                PrimaryActionColor = primaryActionColor;
                SecondaryActionText = secondaryActionText;
                SecondaryActionColor = secondaryActionColor;
            }

            public bool HasSecondaryAction => !string.IsNullOrEmpty(SecondaryActionText);
            public bool HasMessage => !string.IsNullOrEmpty(MessageText);

            public static TimerStateShot Timed(string fileName, string title, string time, string actionText, Color actionColor)
            {
                return new TimerStateShot(fileName, title, time, "", true, actionColor, actionText, actionColor, "", NeutralActionButtonBackColor);
            }

            public static TimerStateShot Message(string fileName, string title, string primaryActionText, Color primaryActionColor)
            {
                return Message(fileName, title, primaryActionText, primaryActionColor, "", NeutralActionButtonBackColor);
            }

            public static TimerStateShot Message(string fileName, string title, string primaryActionText, Color primaryActionColor, string secondaryActionText, Color secondaryActionColor)
            {
                return new TimerStateShot(fileName, title, "", "", false, primaryActionColor, primaryActionText, primaryActionColor, secondaryActionText, secondaryActionColor);
            }

            public static TimerStateShot MessageActions(string fileName, string title, string messageText, string primaryActionText, Color primaryActionColor, string secondaryActionText, Color secondaryActionColor)
            {
                return new TimerStateShot(fileName, title, "", messageText, false, primaryActionColor, primaryActionText, primaryActionColor, secondaryActionText, secondaryActionColor);
            }
        }

        private sealed class SmokeUserPreferences : IUserPreferences
        {
            public Duration GetIntervalDuration(IntervalType intervalType)
            {
                if (intervalType == IntervalType.Pomodoro) return Duration.InMinutes(25);
                if (intervalType == IntervalType.ShortBreak) return Duration.InMinutes(5);
                if (intervalType == IntervalType.LongBreak) return Duration.InMinutes(15);
                throw new ArgumentException($"Unsupported interval type: {intervalType}");
            }

            public int MaxPomodoroCount => 4;
            public bool ShowToastNotifications => false;
            public bool PlaySoundNotifications => false;
            public bool AutoUpdate => false;

            public void Update(Action<IMutableUserPreferences> action)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class ProbeBackdropForm : Form
        {
            private bool alternate;

            public ProbeBackdropForm()
            {
                StartPosition = FormStartPosition.Manual;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                Bounds = new Rectangle(40, 40, 520, 360);
            }

            public void Advance()
            {
                alternate = true;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                var startColor = alternate ? Color.FromArgb(248, 248, 248) : Color.FromArgb(32, 126, 235);
                var endColor = alternate ? Color.FromArgb(210, 226, 244) : Color.FromArgb(236, 242, 248);

                using (var brush = new LinearGradientBrush(ClientRectangle, startColor, endColor, 0f))
                {
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                }

                using (var brush = new SolidBrush(alternate ? Color.FromArgb(128, 30, 30, 30) : Color.FromArgb(120, 100, 90, 220)))
                {
                    e.Graphics.FillEllipse(brush, alternate ? 28 : 260, alternate ? 32 : -70, 260, 210);
                }

                using (var brush = new SolidBrush(alternate ? Color.FromArgb(112, 255, 255, 255) : Color.FromArgb(130, 197, 47, 61)))
                {
                    e.Graphics.FillRectangle(brush, 0, 240, 520, 70);
                }
            }
        }

        private static void CaptureWindow(IntPtr handle, string output)
        {
            RECT rect;
            GetWindowRect(handle, out rect);

            var left = rect.Left;
            var top = rect.Top;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            var dpi = GetDpiForWindow(handle);

            if (dpi > 96 && width < 200)
            {
                var scale = dpi / 96f;
                left = (int)Math.Round(left * scale);
                top = (int)Math.Round(top * scale);
                width = (int)Math.Round(width * scale);
                height = (int)Math.Round(height * scale);
            }

            using (var bitmap = new Bitmap(width, height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height));
                bitmap.Save(output, ImageFormat.Png);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
