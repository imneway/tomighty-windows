using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace Tomighty.Tools
{
    internal static class TrayAppSmokeHost
    {
        private static int processId;
        private static string outputDirectory;

        [STAThread]
        private static int Main(string[] args)
        {
            if ((args.Length != 2 && args.Length != 3) || !int.TryParse(args[0], out processId))
            {
                Console.Error.WriteLine("Usage: TrayAppSmokeHost.exe <tomighty-process-id> <output-directory> [start-only]");
                return 2;
            }

            var startOnly = args.Length == 3 && args[2] == "start-only";
            outputDirectory = args[1];
            System.IO.Directory.CreateDirectory(outputDirectory);

            var trayButton = WaitForTrayButton();
            var pomodoroItem = OpenMenuAndWaitForItem(trayButton, "Pomodoro");
            CaptureContainingMenu(pomodoroItem, "actual-context-menu.png");
            InvokeOrClick(pomodoroItem);

            Thread.Sleep(1600);
            trayButton = WaitForTrayButton();
            CaptureElement(trayButton, "actual-countdown-tray.png");

            if (startOnly)
                return 0;

            Thread.Sleep(5000);
            var timerWindow = WaitForTomightyWindow(4000);
            AssertTopmost(timerWindow);
            AssertStaysVisibleAfterOutsideFocus(timerWindow);
            ClickPrimaryCompletionAction(timerWindow);
            Thread.Sleep(500);
            if (!IsWindowVisible(timerWindow))
                throw new InvalidOperationException("Timer window hid before outside focus after CTA acknowledgement.");

            AssertHidesAfterOutsideFocus(timerWindow);

            return 0;
        }

        private static AutomationElement WaitForTrayButton()
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                var element = FindTrayButton();
                if (element != null)
                    return element;

                TryOpenOverflow();
                Thread.Sleep(200);
            }

            throw new InvalidOperationException("Could not find visible Tomighty tray button.");
        }

        private static AutomationElement OpenMenuAndWaitForItem(AutomationElement trayButton, string menuItemName)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                RightClick(trayButton);
                try
                {
                    return WaitForMenuItem(menuItemName, 2000);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    SendKeys.SendWait("{ESC}");
                    Thread.Sleep(250);
                    trayButton = WaitForTrayButton();
                }
            }

            throw new InvalidOperationException($"Could not find '{menuItemName}' menu item after retrying.", lastError);
        }

        private static AutomationElement FindTrayButton()
        {
            AutomationElement result;
            if (TryFindTrayButtonInWindow("Shell_TrayWnd", out result))
                return result;

            if (TryFindTrayButtonInWindow("NotifyIconOverflowWindow", out result))
                return result;

            return null;
        }

        private static bool TryFindTrayButtonInWindow(string className, out AutomationElement result)
        {
            result = null;
            var handle = FindWindow(className, null);
            if (handle == IntPtr.Zero)
                return false;

            var root = AutomationElement.FromHandle(handle);
            if (root == null)
                return false;

            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            LogButtons(className, buttons);

            for (var index = 0; index < buttons.Count; index++)
            {
                var button = buttons[index];
                var name = button.Current.Name ?? "";
                if (IsTomightyTrayButtonName(name))
                {
                    result = button;
                    return true;
                }
            }

            return false;
        }

        private static void TryOpenOverflow()
        {
            var handle = FindWindow("Shell_TrayWnd", null);
            if (handle == IntPtr.Zero)
                return;

            var root = AutomationElement.FromHandle(handle);
            if (root == null)
                return;

            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            for (var index = 0; index < buttons.Count; index++)
            {
                var button = buttons[index];
                var name = button.Current.Name ?? "";
                if (name.IndexOf("Show hidden icons", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    InvokeOrClick(button);
                    Thread.Sleep(250);
                    return;
                }
            }
        }

        private static void LogButtons(string className, AutomationElementCollection buttons)
        {
            var path = System.IO.Path.Combine(outputDirectory, "tray-buttons.txt");
            using (var writer = new System.IO.StreamWriter(path, true))
            {
                writer.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + className);
                for (var index = 0; index < buttons.Count; index++)
                {
                    var button = buttons[index];
                    writer.WriteLine("  " + (button.Current.Name ?? "") + " " + button.Current.BoundingRectangle);
                }
            }
        }

        private static bool IsTomightyTrayButtonName(string name)
        {
            return string.Equals(name.Trim(), "Tomighty", StringComparison.OrdinalIgnoreCase);
        }

        private static AutomationElement WaitForMenuItem(string name, int timeoutMilliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                var condition = new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                    new PropertyCondition(AutomationElement.NameProperty, name));
                var element = FindInTomightyTopLevelWindows(condition);
                if (element != null)
                    return element;

                Thread.Sleep(100);
            }

            throw new InvalidOperationException($"Could not find '{name}' menu item.");
        }

        private static AutomationElement FindInTomightyTopLevelWindows(Condition condition)
        {
            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
            LogTopLevelWindows(windows);

            for (var index = 0; index < windows.Count; index++)
            {
                var window = windows[index];
                if (window.Current.ProcessId != processId)
                    continue;

                var element = window.FindFirst(TreeScope.Descendants, condition);
                if (element != null)
                    return element;
            }

            return null;
        }

        private static void LogTopLevelWindows(AutomationElementCollection windows)
        {
            var path = System.IO.Path.Combine(outputDirectory, "top-level-windows.txt");
            using (var writer = new System.IO.StreamWriter(path, true))
            {
                writer.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "]");
                for (var index = 0; index < windows.Count; index++)
                {
                    var window = windows[index];
                    writer.WriteLine(
                        $"  pid={window.Current.ProcessId} name='{window.Current.Name}' class='{window.Current.ClassName}' type='{window.Current.ControlType.ProgrammaticName}' rect={window.Current.BoundingRectangle}");
                }
            }
        }

        private static IntPtr WaitForTomightyWindow(int timeoutMilliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                var window = FindTomightyWindow();
                if (window != IntPtr.Zero)
                    return window;

                Thread.Sleep(100);
            }

            throw new InvalidOperationException("Timer completion window did not appear.");
        }

        private static IntPtr FindTomightyWindow()
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((handle, parameter) =>
            {
                uint windowProcessId;
                GetWindowThreadProcessId(handle, out windowProcessId);
                if (windowProcessId == processId && IsWindowVisible(handle))
                {
                    result = handle;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return result;
        }

        private static void AssertTopmost(IntPtr window)
        {
            const int gwlExStyle = -20;
            const int wsExTopmost = 0x00000008;
            if ((GetWindowLong(window, gwlExStyle) & wsExTopmost) != wsExTopmost)
                throw new InvalidOperationException("Completion window is missing WS_EX_TOPMOST.");
        }

        private static void AssertStaysVisibleAfterOutsideFocus(IntPtr window)
        {
            FocusOutsideWindow();

            if (!IsWindowVisible(window))
                throw new InvalidOperationException("Completion window hid after focus moved outside.");
        }

        private static void AssertHidesAfterOutsideFocus(IntPtr window)
        {
            FocusOutsideWindow();

            if (IsWindowVisible(window))
                throw new InvalidOperationException("Timer window stayed visible after CTA acknowledgement and outside focus.");
        }

        private static void FocusOutsideWindow()
        {
            using (var probe = new Form())
            {
                probe.Text = "Tomighty app smoke probe";
                probe.ShowInTaskbar = false;
                probe.StartPosition = FormStartPosition.Manual;
                probe.Bounds = new Rectangle(40, 40, 120, 80);
                probe.Show();
                probe.Activate();
                SetForegroundWindow(probe.Handle);
                Thread.Sleep(500);
            }
        }

        private static void ClickPrimaryCompletionAction(IntPtr window)
        {
            Rect rect;
            GetWindowRect(window, out rect);
            LeftClick(rect.Left + (rect.Right - rect.Left) / 3, rect.Bottom - 24);
            Thread.Sleep(500);
        }

        private static void RightClick(AutomationElement element)
        {
            var point = GetElementCenter(element);
            SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y));
            MouseEvent(MouseEventRightDown);
            MouseEvent(MouseEventRightUp);
        }

        private static void InvokeOrClick(AutomationElement element)
        {
            object pattern;
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
            {
                ((InvokePattern)pattern).Invoke();
                return;
            }

            var point = GetElementCenter(element);
            LeftClick((int)Math.Round(point.X), (int)Math.Round(point.Y));
        }

        private static void LeftClick(int x, int y)
        {
            SetCursorPos(x, y);
            MouseEvent(MouseEventLeftDown);
            MouseEvent(MouseEventLeftUp);
        }

        private static PointF GetElementCenter(AutomationElement element)
        {
            var rect = element.Current.BoundingRectangle;
            return new PointF(
                (float)(rect.Left + rect.Width / 2),
                (float)(rect.Top + rect.Height / 2));
        }

        private static void CaptureElement(AutomationElement element, string fileName)
        {
            var rect = element.Current.BoundingRectangle;
            var bounds = Rectangle.Inflate(
                new Rectangle(
                    (int)Math.Floor(rect.Left),
                    (int)Math.Floor(rect.Top),
                    (int)Math.Ceiling(rect.Width),
                    (int)Math.Ceiling(rect.Height)),
                8,
                8);

            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                bitmap.Save(System.IO.Path.Combine(outputDirectory, fileName), ImageFormat.Png);
            }
        }

        private static void CaptureContainingMenu(AutomationElement element, string fileName)
        {
            Rect menuRect;
            if (TryGetVisiblePopupMenuBounds(out menuRect))
            {
                CaptureBounds(menuRect, fileName, 8);
                return;
            }

            var best = element;
            var parent = TreeWalker.RawViewWalker.GetParent(element);
            while (parent != null)
            {
                if (parent.Current.ProcessId == processId && IsLargerThan(parent, best))
                    best = parent;

                if (parent.Current.ControlType == ControlType.Menu)
                {
                    best = parent;
                    break;
                }

                parent = TreeWalker.RawViewWalker.GetParent(parent);
            }

            CaptureElement(best, fileName);
        }

        private static bool IsLargerThan(AutomationElement candidate, AutomationElement current)
        {
            var candidateRect = candidate.Current.BoundingRectangle;
            var currentRect = current.Current.BoundingRectangle;
            return candidateRect.Width >= currentRect.Width &&
                candidateRect.Height >= currentRect.Height &&
                candidateRect.Width > 0 &&
                candidateRect.Height > 0;
        }

        private static void CaptureBounds(Rect rect, string fileName, int inflate)
        {
            var bounds = Rectangle.Inflate(
                Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom),
                inflate,
                inflate);

            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                bitmap.Save(System.IO.Path.Combine(outputDirectory, fileName), ImageFormat.Png);
            }
        }

        private static bool TryGetVisiblePopupMenuBounds(out Rect popupBounds)
        {
            var candidateBounds = new Rect();
            var found = false;
            EnumWindows((handle, parameter) =>
            {
                if (!IsWindowVisible(handle))
                    return true;

                var className = new char[256];
                var length = GetClassName(handle, className, className.Length);
                if (length <= 0 || new string(className, 0, length) != "#32768")
                    return true;

                uint windowProcessId;
                GetWindowThreadProcessId(handle, out windowProcessId);
                if (windowProcessId != processId)
                    return true;

                Rect rect;
                if (GetWindowRect(handle, out rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
                {
                    candidateBounds = rect;
                    found = true;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            popupBounds = candidateBounds;
            return found;
        }

        private static void MouseEvent(uint flags)
        {
            mouse_event(flags, 0, 0, 0, UIntPtr.Zero);
        }

        private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;
        private const uint MouseEventRightDown = 0x0008;
        private const uint MouseEventRightUp = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
