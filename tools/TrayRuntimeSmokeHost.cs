using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Tomighty.Events;
using Tomighty.Windows.Timer;

namespace Tomighty.Tools
{
    internal static class TrayRuntimeSmokeHost
    {
        private const int RuntimePomodoroSeconds = 3;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: TrayRuntimeSmokeHost.exe <output-directory>");
                return 2;
            }

            SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var outputDirectory = args[0];
            System.IO.Directory.CreateDirectory(outputDirectory);

            var eventHub = new SynchronousEventHub();
            var timer = new Tomighty.Timer(eventHub);
            var preferences = new SmokeUserPreferences();
            var engine = new PomodoroEngine(timer, preferences, eventHub);
            using (var container = new Container())
            using (var notifyIcon = new NotifyIcon(container))
            {
                notifyIcon.Text = "Tomighty runtime smoke";

                var presenter = CreateTimerWindowPresenter(engine, timer, eventHub);
                var trayIconController = CreateTrayIconController(notifyIcon, presenter, preferences, eventHub);
                try
                {
                    notifyIcon.Visible = true;
                    PumpUi(500);
                    SaveNotifyIconEvidence(notifyIcon, outputDirectory, "01-default");

                    engine.StartTimer(IntervalType.Pomodoro);
                    PumpUi(1500);
                    SaveNotifyIconEvidence(notifyIcon, outputDirectory, "02-countdown");

                    PumpUi((RuntimePomodoroSeconds + 1) * 1000);
                    AssertCompletionWindowBehavior(presenter);
                }
                finally
                {
                    notifyIcon.Visible = false;
                    (trayIconController as IDisposable)?.Dispose();
                }
            }

            return 0;
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

        private static object CreateTrayIconController(NotifyIcon notifyIcon, object presenter, IUserPreferences preferences, IEventHub eventHub)
        {
            var controllerType = typeof(TimerWindow).Assembly.GetType("Tomighty.Windows.Tray.TrayIconController", true);
            return Activator.CreateInstance(
                controllerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { notifyIcon, presenter, preferences, eventHub },
                null);
        }

        private static void SaveNotifyIconEvidence(NotifyIcon notifyIcon, string outputDirectory, string name)
        {
            using (var bitmap = notifyIcon.Icon.ToBitmap())
            {
                bitmap.Save(System.IO.Path.Combine(outputDirectory, name + "-bitmap.png"), ImageFormat.Png);
            }

            Rect rect;
            if (!TryGetNotifyIconRect(notifyIcon, out rect))
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory, name + "-tray-rect.txt"), "Shell_NotifyIconGetRect failed.");
                return;
            }

            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory, name + "-tray-rect.txt"), bounds.ToString());

            var captureBounds = Rectangle.Inflate(bounds, 8, 8);
            using (var bitmap = new Bitmap(captureBounds.Width, captureBounds.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(captureBounds.Left, captureBounds.Top, 0, 0, captureBounds.Size);
                bitmap.Save(System.IO.Path.Combine(outputDirectory, name + "-tray.png"), ImageFormat.Png);
            }
        }

        private static bool TryGetNotifyIconRect(NotifyIcon notifyIcon, out Rect rect)
        {
            rect = default(Rect);

            try
            {
                var type = typeof(NotifyIcon);
                var idField = type.GetField("id", BindingFlags.Instance | BindingFlags.NonPublic);
                var windowField = type.GetField("window", BindingFlags.Instance | BindingFlags.NonPublic);

                if (idField == null || windowField == null)
                    return false;

                var nativeWindow = windowField.GetValue(notifyIcon) as NativeWindow;
                if (nativeWindow == null || nativeWindow.Handle == IntPtr.Zero)
                    return false;

                var identifier = new NotifyIconIdentifier
                {
                    Size = Marshal.SizeOf(typeof(NotifyIconIdentifier)),
                    WindowHandle = nativeWindow.Handle,
                    Id = (uint)(int)idField.GetValue(notifyIcon),
                    Guid = Guid.Empty
                };

                return Shell_NotifyIconGetRect(ref identifier, out rect) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void AssertCompletionWindowBehavior(object presenter)
        {
            var window = GetPresenterWindow(presenter);
            if (!window.Visible)
                throw new InvalidOperationException("Completion window was not forced visible.");

            if (!window.TopMost)
                throw new InvalidOperationException("Completion window TopMost property is false.");

            if (!IsTopmostWindow(window.Handle))
                throw new InvalidOperationException("Completion window is missing WS_EX_TOPMOST.");

            FocusOutsideWindow(window);

            if (!window.Visible)
                throw new InvalidOperationException("Completion window hid after focus moved outside.");

            ClickPrimaryActionButton(window);
            PumpUi(250);
            if (!window.Visible)
                throw new InvalidOperationException("Completion window hid before the outside click after CTA acknowledgement.");

            FocusOutsideWindow(window);
            if (window.Visible)
                throw new InvalidOperationException("Completion window stayed visible after CTA acknowledgement and outside focus.");
        }

        private static TimerWindow GetPresenterWindow(object presenter)
        {
            var field = presenter.GetType().GetField("window", BindingFlags.Instance | BindingFlags.NonPublic);
            var window = field.GetValue(presenter) as TimerWindow;
            if (window == null)
                throw new InvalidOperationException("TimerWindowPresenter did not create a timer window.");

            return window;
        }

        private static void FocusOutsideWindow(TimerWindow window)
        {
            using (var probe = new Form())
            {
                probe.Text = "Tomighty tray runtime smoke probe";
                probe.ShowInTaskbar = false;
                probe.StartPosition = FormStartPosition.Manual;
                probe.Bounds = new Rectangle(window.Right + 20, window.Top, 120, 80);
                probe.Show();
                probe.Activate();
                SetForegroundWindow(probe.Handle);
                PumpUi(500);
            }
        }

        private static void ClickDropDownButton(TimerWindow window)
        {
            var bounds = (Rectangle)typeof(TimerWindow)
                .GetMethod("GetDropDownBounds", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[0]);
            var x = bounds.Left + bounds.Width / 2;
            var y = bounds.Top + bounds.Height / 2;
            var args = new MouseEventArgs(MouseButtons.Left, 1, x, y, 0);

            typeof(TimerWindow)
                .GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { args });
            PumpUi(80);
            typeof(TimerWindow)
                .GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { args });
        }

        private static void ClickPrimaryActionButton(TimerWindow window)
        {
            var bounds = (Rectangle)typeof(TimerWindow)
                .GetMethod("GetPrimaryActionButtonBounds", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[0]);
            var x = bounds.Left + bounds.Width / 2;
            var y = bounds.Top + bounds.Height / 2;
            var args = new MouseEventArgs(MouseButtons.Left, 1, x, y, 0);

            typeof(TimerWindow)
                .GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { args });
            PumpUi(80);
            typeof(TimerWindow)
                .GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { args });
        }

        private static bool IsTopmostWindow(IntPtr handle)
        {
            const int gwlExStyle = -20;
            const int wsExTopmost = 0x00000008;
            return (GetWindowLong(handle, gwlExStyle) & wsExTopmost) == wsExTopmost;
        }

        private static void PumpUi(int milliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(40);
            }
        }

        private sealed class SmokeUserPreferences : IUserPreferences
        {
            public Duration GetIntervalDuration(IntervalType intervalType)
            {
                if (intervalType == IntervalType.Pomodoro) return new Duration(RuntimePomodoroSeconds);
                if (intervalType == IntervalType.ShortBreak) return new Duration(10);
                if (intervalType == IntervalType.LongBreak) return new Duration(10);
                throw new ArgumentException($"Unsupported interval type: {intervalType}");
            }

            public int MaxPomodoroCount => 4;
            public bool ShowToastNotifications => false;
            public bool PlaySoundNotifications => false;
            public bool ShowRemainingTimeOnTray => true;
            public bool AutoUpdate => false;

            public void Update(Action<IMutableUserPreferences> action)
            {
                throw new NotSupportedException();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NotifyIconIdentifier
        {
            public int Size;
            public IntPtr WindowHandle;
            public uint Id;
            public Guid Guid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("shell32.dll", SetLastError = false)]
        private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect iconLocation);
    }
}
