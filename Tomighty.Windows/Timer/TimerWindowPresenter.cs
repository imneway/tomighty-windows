//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Tomighty.Events;
using Tomighty.Windows.UI;

namespace Tomighty.Windows.Timer
{
    internal class TimerWindowPresenter
    {
        private const int TrayPopupGap = 10;
        private const int OutsideClickDismissGraceMilliseconds = 250;
        private static readonly TimeSpan CompletionAcknowledgementAutoHideDelay = TimeSpan.FromMilliseconds(400);

        private readonly ICountdownClock countdownClock;
        private readonly IUserPreferences userPreferences;
        private readonly IFullscreenWindowDetector fullscreenWindowDetector;
        private readonly IWindowState idleState;
        private readonly IWindowState pomodoroState;
        private readonly IWindowState shortBreakState;
        private readonly IWindowState longBreakState;
        private readonly IWindowState pomodoroCompletedState;
        private readonly IWindowState breakFinishedState;
        private readonly IWindowState pomodoroInterruptedState;
        private readonly IWindowState shortBreakInterruptedState;
        private readonly IWindowState longBreakInterruptedState;

        private IWindowState currentState;
        private TimerWindow window;
        private readonly System.Windows.Forms.Timer autoHideTimer;
        private bool _isPinned;
        private bool requiresManualClose;
        private bool hasLastTrayIconCenter;
        private Point lastTrayIconCenter;
        private bool hasLastTrayIconBounds;
        private Rectangle lastTrayIconBounds;
        private DateTime lastDeactivateHideTime = DateTime.MinValue;
        private DateTime windowShownTime = DateTime.MinValue;
        private readonly GlobalLeftMouseDownMonitor outsideClickMonitor;
        private DateTime completionAcknowledgedTime = DateTime.MinValue;
        private bool completionAlertPending;

        public TimerWindowPresenter(IPomodoroEngine pomodoroEngine, ICountdownClock countdownClock, IEventHub eventHub, IUserPreferences userPreferences)
            : this(pomodoroEngine, countdownClock, eventHub, userPreferences, new ForegroundFullscreenWindowDetector())
        {
        }

        internal TimerWindowPresenter(
            IPomodoroEngine pomodoroEngine,
            ICountdownClock countdownClock,
            IEventHub eventHub,
            IUserPreferences userPreferences,
            IFullscreenWindowDetector fullscreenWindowDetector)
        {
            this.countdownClock = countdownClock;
            this.userPreferences = userPreferences;
            this.fullscreenWindowDetector = fullscreenWindowDetector;

            idleState = new IdleState(pomodoroEngine);
            pomodoroState = new PomodoroState(pomodoroEngine);
            shortBreakState = new ShortBreakState(pomodoroEngine);
            longBreakState = new LongBreakState(pomodoroEngine);
            pomodoroCompletedState = new PomodoroCompletedState(pomodoroEngine, AcknowledgeCompletion);
            breakFinishedState = new BreakFinishedState(pomodoroEngine, AcknowledgeCompletion);
            pomodoroInterruptedState = new TimerInterruptedState("Pomodoro interrupted", IntervalType.Pomodoro, pomodoroEngine);
            shortBreakInterruptedState = new BreakInterruptedState("Short break interrupted", pomodoroEngine);
            longBreakInterruptedState = new BreakInterruptedState("Long break interrupted", pomodoroEngine);

            currentState = idleState;
            outsideClickMonitor = new GlobalLeftMouseDownMonitor(OnGlobalLeftMouseDown);
            window = CreateTimerWindow();
            currentState.Apply(window, countdownClock.RemainingTime);
            EnsureWindowHandle();

            eventHub.Subscribe<TimerStarted>(OnTimerStarted);
            eventHub.Subscribe<TimeElapsed>(OnTimeElapsed);
            eventHub.Subscribe<TimerStopped>(OnTimerStopped);

            autoHideTimer = new System.Windows.Forms.Timer { Interval = 150 };
            autoHideTimer.Tick += (sender, args) =>
            {
                HideIfFocusMovedOutside();
                UpdateCompletionAlertFullscreenState();
            };
            autoHideTimer.Start();
        }

        private TimerWindow CreateTimerWindow()
        {
            var window = new TimerWindow();

            foreach (var control in window.Controls)
                ((Control)control).LostFocus += OnLostFocus;

            window.LostFocus += OnLostFocus;
            window.Deactivate += OnWindowDeactivate;
            window.VisibleChanged += OnWindowVisibleChanged;
            window.PinButton.Click += OnPinButtonClick;
            window.CloseButton.Click += OnCloseButtonClick;

            new DragAroundController(window, () => IsPinned);

            return window;
        }
        
        public void Toggle(Point approximateLocation)
        {
            RememberTrayIconCenter(approximateLocation);
            ToggleAtTrayAnchor();
        }

        public void Toggle(Rectangle trayIconBounds)
        {
            RememberTrayIconBounds(trayIconBounds);
            ToggleAtTrayAnchor();
        }

        private void ToggleAtTrayAnchor()
        {
            var shouldCreateWindow = window == null;

            if (shouldCreateWindow)
            {
                window = CreateTimerWindow();
            }

            if (!window.Visible && WasJustHiddenByDeactivation())
                return;

            if (window.Visible)
            {
                if (CanAutoHideWindow)
                    window.HideAnimated();
            }
            else
            {
                if (!window.IsHandleCreated && window.Handle == IntPtr.Zero)
                    return;

                window.Show(GetLocationAboveTrayIcon);
                ResetOutsideClickTracking();

                if (shouldCreateWindow)
                {
                    currentState.Apply(window, countdownClock.RemainingTime);
                }
            }
        }

        private void OnTimerStarted(TimerStarted @event)
        {
            RunOnWindowThread(() =>
            {
                requiresManualClose = false;
                completionAlertPending = false;
                EnterState(GetRunningTimerStateFor(@event.IntervalType), @event.RemainingTime);
            });
        }

        private void OnTimerStopped(TimerStopped @event)
        {
            if (@event.IsIntervalCompleted)
            {
                RunOnWindowThread(() =>
                {
                    EnsureWindow();
                    requiresManualClose = true;
                    EnterState(GetCompletedIntervalStateFor(@event.IntervalType), @event.RemainingTime);
                    ShowCompletionAlert();
                });
            }
            else
            {
                RunOnWindowThread(() =>
                {
                    requiresManualClose = false;
                    completionAlertPending = false;
                    EnterState(GetTimerInterruptedStateFor(@event.IntervalType), @event.RemainingTime);
                });
            }
        }

        public void RememberTrayIconCenter(Point location)
        {
            lastTrayIconCenter = location;
            hasLastTrayIconCenter = true;
            hasLastTrayIconBounds = false;
        }

        public void RememberTrayIconBounds(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                RememberTrayIconCenter(bounds.Location);
                return;
            }

            lastTrayIconBounds = bounds;
            lastTrayIconCenter = GetBoundsCenter(bounds);
            hasLastTrayIconBounds = true;
            hasLastTrayIconCenter = true;
        }

        private void OnTimeElapsed(TimeElapsed @event)
        {
            if (window != null)
            {
                window.UpdateTimeDisplay(@event.RemainingTime.ToTimeString());
            }
        }
        
        private void EnterState(IWindowState newState, Duration remainingTime)
        {
            if (newState != currentState)
            {
                currentState = newState;
                currentState.Apply(window, remainingTime);
                RepositionVisibleWindowAboveTrayIcon();
            }
        }

        private IWindowState GetRunningTimerStateFor(IntervalType intervalType)
        {
            if (intervalType == IntervalType.Pomodoro) return pomodoroState;
            if (intervalType == IntervalType.ShortBreak) return shortBreakState;
            if (intervalType == IntervalType.LongBreak) return longBreakState;
            throw new ArgumentException($"Unknown interval type: {intervalType}");
        }

        private IWindowState GetCompletedIntervalStateFor(IntervalType intervalType)
        {
            if (intervalType == IntervalType.Pomodoro) return pomodoroCompletedState;
            if (intervalType == IntervalType.ShortBreak || intervalType == IntervalType.LongBreak) return breakFinishedState;
            throw new ArgumentException($"Unknown interval type: {intervalType}");
        }

        private IWindowState GetTimerInterruptedStateFor(IntervalType intervalType)
        {
            if (intervalType == IntervalType.Pomodoro) return pomodoroInterruptedState;
            if (intervalType == IntervalType.ShortBreak) return shortBreakInterruptedState;
            if (intervalType == IntervalType.LongBreak) return longBreakInterruptedState;
            throw new ArgumentException($"Unknown interval type: {intervalType}");
        }

        private Point GetLocationAboveTrayIcon()
        {
            Rectangle trayIconBounds;
            Point trayIconCenter;

            if (hasLastTrayIconBounds)
            {
                trayIconBounds = lastTrayIconBounds;
                trayIconCenter = GetBoundsCenter(trayIconBounds);
            }
            else
            {
                trayIconCenter = hasLastTrayIconCenter ? lastTrayIconCenter : Cursor.Position;
                var trayIconSize = Math.Max(16, SystemInformation.SmallIconSize.Height);
                trayIconBounds = new Rectangle(
                    trayIconCenter.X - trayIconSize / 2,
                    trayIconCenter.Y - trayIconSize / 2,
                    trayIconSize,
                    trayIconSize);
            }

            var screen = Screen.FromPoint(trayIconCenter);
            var screenBounds = screen.Bounds;
            var gap = GetScaledTrayPopupGap();

            var x = trayIconCenter.X - window.Width / 2;
            var y = trayIconBounds.Top - gap - window.Height;

            x = Math.Max(screenBounds.Left + gap, Math.Min(x, screenBounds.Right - window.Width - gap));

            if (y < screenBounds.Top + gap)
                y = trayIconBounds.Bottom + gap;

            y = Math.Max(screenBounds.Top + gap, Math.Min(y, screenBounds.Bottom - window.Height - gap));

            return new Point(x, y);
        }

        private void RepositionVisibleWindowAboveTrayIcon()
        {
            if (window == null || !window.Visible || IsPinned || (!hasLastTrayIconCenter && !hasLastTrayIconBounds))
                return;

            window.Location = GetLocationAboveTrayIcon();
        }

        private int GetScaledTrayPopupGap()
        {
            var dpi = 96;

            try
            {
                if (window != null && window.IsHandleCreated)
                    dpi = Math.Max(96, GetDpiForWindow(window.Handle));
            }
            catch
            {
            }

            return Math.Max(1, (int)Math.Round(TrayPopupGap * dpi / 96.0));
        }

        private static Point GetBoundsCenter(Rectangle bounds)
        {
            return new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        }

        private void OnPinButtonClick(object sender, System.EventArgs e)
        {
            IsPinned = !IsPinned;
        }

        private void OnCloseButtonClick(object sender, System.EventArgs e)
        {
            requiresManualClose = false;
            completionAlertPending = false;
            window.HideAnimated();
            IsPinned = false;
        }

        private void OnWindowVisibleChanged(object sender, System.EventArgs e)
        {
            if (window.Visible)
            {
                window.Focus();
                outsideClickMonitor.Start();
            }
            else
            {
                outsideClickMonitor.Stop();
            }
        }

        private void OnLostFocus(object sender, System.EventArgs e)
        {
            if (ShouldAutoHideWindow())
                window.HideAnimated();
        }

        private void OnWindowDeactivate(object sender, System.EventArgs e)
        {
            if (ShouldAutoHideWindow())
            {
                lastDeactivateHideTime = DateTime.UtcNow;
                window.HideAnimated();
            }
        }

        private void OnGlobalLeftMouseDown(Point screenPoint)
        {
            // Called on the hook thread; only marshal to the window thread here.
            var target = window;
            if (target == null || !target.IsHandleCreated)
                return;

            try
            {
                target.BeginInvoke((Action)(() => DismissOnOutsideClick(screenPoint)));
            }
            catch (InvalidOperationException)
            {
                // The window handle was torn down between the check and the call.
            }
        }

        private void DismissOnOutsideClick(Point screenPoint)
        {
            if (window == null || !window.Visible || IsPinned)
                return;

            // A finished-timer alert is sticky: only an explicit in-window close
            // dismisses it, so a stray click on the desktop cannot make the user
            // miss that the timer ended. A manually opened popup still closes on
            // an outside click.
            if (requiresManualClose)
                return;

            if ((DateTime.UtcNow - windowShownTime).TotalMilliseconds < OutsideClickDismissGraceMilliseconds)
                return;

            if (window.Bounds.Contains(screenPoint))
                return;

            completionAlertPending = false;
            lastDeactivateHideTime = DateTime.UtcNow;
            window.HideAnimated();
        }

        private void HideIfFocusMovedOutside()
        {
            if (!ShouldAutoHideWindow())
                return;

            lastDeactivateHideTime = DateTime.UtcNow;
            window.HideAnimated();
        }

        private bool WasJustHiddenByDeactivation()
        {
            var elapsed = DateTime.UtcNow - lastDeactivateHideTime;

            if (elapsed.TotalMilliseconds > 250)
                return false;

            lastDeactivateHideTime = DateTime.MinValue;
            return true;
        }

        private bool CanAutoHideWindow => !IsPinned && !requiresManualClose;

        private bool ShouldAutoHideWindow()
        {
            if (window == null || !window.Visible || !CanAutoHideWindow || WasCompletionJustAcknowledged())
                return false;

            return !WindowHasForegroundFocus();
        }

        private void ResetOutsideClickTracking()
        {
            windowShownTime = DateTime.UtcNow;
        }

        private bool WasCompletionJustAcknowledged()
        {
            if (completionAcknowledgedTime == DateTime.MinValue)
                return false;

            return DateTime.UtcNow - completionAcknowledgedTime < CompletionAcknowledgementAutoHideDelay;
        }

        private bool WindowHasForegroundFocus()
        {
            if (window.ContainsFocus)
                return true;

            if (!window.IsHandleCreated)
                return false;

            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
                return false;

            return foregroundWindow == window.Handle || IsChild(window.Handle, foregroundWindow);
        }

        private void AcknowledgeCompletion()
        {
            requiresManualClose = false;
            completionAlertPending = false;
            lastDeactivateHideTime = DateTime.MinValue;
            completionAcknowledgedTime = DateTime.UtcNow;

            if (window != null && window.Visible)
            {
                window.TopMost = false;
                window.Activate();
                window.Focus();
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hWnd);

        private void EnsureWindow()
        {
            if (window == null)
            {
                window = CreateTimerWindow();
                EnsureWindowHandle();
            }
        }

        private void EnsureWindowHandle()
        {
            GC.KeepAlive(window.Handle);
        }

        private void RunOnWindowThread(Action action)
        {
            EnsureWindow();

            if (window.InvokeRequired)
                window.BeginInvoke(action);
            else
                action();
        }

        private void ShowCompletionAlert()
        {
            if (ShouldSuppressCompletionAlert())
            {
                completionAlertPending = true;
                window.Hide();
                return;
            }

            completionAlertPending = false;
            ShowCompletionAlertNow();
        }

        private void UpdateCompletionAlertFullscreenState()
        {
            if (window == null || !requiresManualClose)
                return;

            var shouldSuppress = ShouldSuppressCompletionAlert();

            if (window.Visible && shouldSuppress)
            {
                completionAlertPending = true;
                window.TopMost = false;
                window.Hide();
                return;
            }

            if (!completionAlertPending || window.Visible || shouldSuppress)
                return;

            completionAlertPending = false;
            ShowCompletionAlertNow();
        }

        private void ShowCompletionAlertNow()
        {
            if (window.Visible && IsPinned)
                window.Show(() => window.Location);
            else
                window.Show(GetCompletionAlertLocation);

            ResetOutsideClickTracking();
        }

        private bool ShouldSuppressCompletionAlert()
        {
            if (!userPreferences.SuppressPopupInFullscreenApps)
                return false;

            var screen = Screen.FromPoint(GetCompletionAlertScreenPoint());
            var ignoredWindowHandle = window != null && window.IsHandleCreated ? window.Handle : IntPtr.Zero;
            return fullscreenWindowDetector.IsFullscreenWindowActiveOn(screen, ignoredWindowHandle);
        }

        private Point GetCompletionAlertScreenPoint()
        {
            if (window != null && window.Visible)
                return new Point(window.Left + window.Width / 2, window.Top + window.Height / 2);

            if (hasLastTrayIconCenter)
                return lastTrayIconCenter;

            return Cursor.Position;
        }

        private Point GetCompletionAlertLocation()
        {
            if (hasLastTrayIconCenter || hasLastTrayIconBounds)
                return GetLocationAboveTrayIcon();

            var screen = Screen.FromPoint(Cursor.Position);
            var workingArea = screen.WorkingArea;
            return new Point(
                workingArea.Right - window.Width - TrayPopupGap,
                workingArea.Bottom - window.Height - TrayPopupGap);
        }

        private bool IsPinned
        {
            get { return _isPinned; }
            set
            {
                if (value != _isPinned)
                {
                    _isPinned = value;

                    if (window != null)
                    {
                        window.UpdatePinButtonState(_isPinned);
                    }
                }
            }
        }

        private interface IWindowState
        {
            void Apply(TimerWindow window, Duration remainingTime);
        }

        private class IdleState : IWindowState
        {
            private readonly IPomodoroEngine pomodoroEngine;

            public IdleState(IPomodoroEngine pomodoroEngine)
            {
                this.pomodoroEngine = pomodoroEngine;
            }

            public void Apply(TimerWindow window, Duration remainingTime)
            {
                if (window == null)
                    return;

                window.UpdateTitle("Idle");
                window.UpdateColorScheme(TimerWindow.DarkGray);
                window.HideTimeDisplay();
                window.SetTimerAction("Start Pomodoro", StartTimer, ModernTheme.ActionButtonBackColor);
            }

            private void StartTimer()
            {
                pomodoroEngine.StartTimer(IntervalType.Pomodoro);
            }
        }

        private class PomodoroState : IWindowState
        {
            private readonly IPomodoroEngine pomodoroEngine;

            public PomodoroState(IPomodoroEngine pomodoroEngine)
            {
                this.pomodoroEngine = pomodoroEngine;
            }

            public void Apply(TimerWindow window, Duration remainingTime)
            {
                if (window == null)
                    return;

                window.UpdateTitle("Pomodoro");
                window.UpdateColorScheme(TimerWindow.Red);
                window.UpdateTimeDisplay(remainingTime.ToTimeString());
                window.SetTimerAction("Interrupt", pomodoroEngine.StopTimer, ModernTheme.ActionButtonBackColor);
            }
        }

        private class ShortBreakState : IWindowState
        {
            private readonly IPomodoroEngine pomodoroEngine;

            public ShortBreakState(IPomodoroEngine pomodoroEngine)
            {
                this.pomodoroEngine = pomodoroEngine;
            }

            public void Apply(TimerWindow window, Duration remainingTime)
            {
                if (window == null)
                    return;

                window.UpdateTitle("Short Break");
                window.UpdateColorScheme(TimerWindow.Green);
                window.UpdateTimeDisplay(remainingTime.ToTimeString());
                window.SetTimerAction("Interrupt", pomodoroEngine.StopTimer, ModernTheme.BreakActionButtonBackColor);
            }
        }

        private class LongBreakState : IWindowState
        {
            private readonly IPomodoroEngine pomodoroEngine;

            public LongBreakState(IPomodoroEngine pomodoroEngine)
            {
                this.pomodoroEngine = pomodoroEngine;
            }

            public void Apply(TimerWindow window, Duration remainingTime)
            {
                if (window == null)
                    return;

                window.UpdateTitle("Long Break");
                window.UpdateColorScheme(TimerWindow.Blue);
                window.UpdateTimeDisplay(remainingTime.ToTimeString());
                window.SetTimerAction("Interrupt", pomodoroEngine.StopTimer, ModernTheme.BreakActionButtonBackColor);
            }
        }

        private class PomodoroCompletedState : IWindowState
        {
            private readonly IPomodoroEngine pomodoroEngine;
            private readonly Action acknowledgeCompletion;

            public PomodoroCompletedState(IPomodoroEngine pomodoroEngine, Action acknowledgeCompletion)
            {
                this.pomodoroEngine = pomodoroEngine;
                this.acknowledgeCompletion = acknowledgeCompletion;
            }

            public void Apply(TimerWindow window, Duration remainingTime)
            {
                if (window == null)
                    return;

                window.UpdateTitle("Pomodoro finished");
                window.UpdateColorScheme(TimerWindow.DarkGray);
                window.HideTimeDisplay();
                window.SetTimerMessageActions(
                    "Take a break",
                    "Short",
                    StartShortBreak,
                    ModernTheme.BreakActionButtonBackColor,
                    "Long",
                    StartLongBreak,
                    ModernTheme.NeutralActionButtonBackColor);
            }

            private void StartShortBreak()
            {
                acknowledgeCompletion();
                pomodoroEngine.StartTimer(IntervalType.ShortBreak);
            }

            private void StartLongBreak()
            {
                acknowledgeCompletion();
                pomodoroEngine.StartTimer(IntervalType.LongBreak);
            }
        }

        private class BreakFinishedState : IWindowState
        {
            private readonly IPomodoroEngine pomodoroEngine;
            private readonly Action acknowledgeCompletion;

            public BreakFinishedState(IPomodoroEngine pomodoroEngine, Action acknowledgeCompletion)
            {
                this.pomodoroEngine = pomodoroEngine;
                this.acknowledgeCompletion = acknowledgeCompletion;
            }

            public void Apply(TimerWindow window, Duration remainingTime)
            {
                if (window == null)
                    return;

                window.UpdateTitle("Break Finished");
                window.UpdateColorScheme(TimerWindow.DarkGray);
                window.HideTimeDisplay();
                window.SetTimerAction("Start Pomodoro", StartTimer, ModernTheme.ActionButtonBackColor);
            }

            private void StartTimer()
            {
                acknowledgeCompletion();
                pomodoroEngine.StartTimer(IntervalType.Pomodoro);
            }
        }

        private class TimerInterruptedState : IWindowState
        {
            private readonly string title;
            private readonly IntervalType restartType;
            private readonly IPomodoroEngine pomodoroEngine;

            public TimerInterruptedState(string title, IntervalType restartType, IPomodoroEngine pomodoroEngine)
            {
                this.title = title;
                this.restartType = restartType;
                this.pomodoroEngine = pomodoroEngine;
            }

            public void Apply(TimerWindow window, Duration remainingTime)
            {
                if (window == null)
                    return;

                window.UpdateTitle(title);
                window.UpdateColorScheme(TimerWindow.DarkGray);
                window.HideTimeDisplay();
                window.SetTimerAction("Restart", StartTimer, ModernTheme.NeutralActionButtonBackColor);
            }

            private void StartTimer()
            {
                pomodoroEngine.StartTimer(restartType);
            }
        }

        private class BreakInterruptedState : IWindowState
        {
            private readonly string title;
            private readonly IPomodoroEngine pomodoroEngine;

            public BreakInterruptedState(string title, IPomodoroEngine pomodoroEngine)
            {
                this.title = title;
                this.pomodoroEngine = pomodoroEngine;
            }

            public void Apply(TimerWindow window, Duration remainingTime)
            {
                if (window == null)
                    return;

                window.UpdateTitle(title);
                window.UpdateColorScheme(TimerWindow.DarkGray);
                window.HideTimeDisplay();
                window.SetTimerAction("Start Pomodoro", StartPomodoro, ModernTheme.ActionButtonBackColor);
            }

            private void StartPomodoro()
            {
                pomodoroEngine.StartTimer(IntervalType.Pomodoro);
            }
        }

        private sealed class GlobalLeftMouseDownMonitor
        {
            private const int WH_MOUSE_LL = 14;
            private const int WM_LBUTTONDOWN = 0x0201;
            private const int WM_QUIT = 0x0012;
            private const uint PM_NOREMOVE = 0x0000;

            private readonly Action<Point> onLeftMouseDown;
            private readonly LowLevelMouseProc hookProc;
            private readonly object threadLock = new object();
            private HookSession session;

            public GlobalLeftMouseDownMonitor(Action<Point> onLeftMouseDown)
            {
                this.onLeftMouseDown = onLeftMouseDown;

                // Keep the delegate in a field so the GC cannot collect it
                // while the native hook still references it.
                hookProc = HookProc;
            }

            // The hook runs on a dedicated thread with its own message pump.
            // Hosting it on the UI thread stalls the system-wide mouse input
            // pipeline whenever that thread stops pumping (e.g. during the
            // modal window-drag loop), which Windows punishes by silently
            // dropping the hook.
            public void Start()
            {
                lock (threadLock)
                {
                    if (session != null)
                        return;

                    session = new HookSession();
                    var thread = new System.Threading.Thread(RunHookLoop)
                    {
                        IsBackground = true,
                        Name = "TimerPopupMouseHook"
                    };
                    thread.Start(session);
                }
            }

            public void Stop()
            {
                HookSession stoppingSession;

                lock (threadLock)
                {
                    if (session == null)
                        return;

                    stoppingSession = session;
                    session = null;
                }

                stoppingSession.Ready.Wait(1000);
                PostThreadMessage(stoppingSession.ThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }

            private void RunHookLoop(object state)
            {
                var currentSession = (HookSession)state;
                currentSession.ThreadId = GetCurrentThreadId();

                // Force the creation of this thread's message queue so that
                // PostThreadMessage from Stop cannot be lost.
                NativeMessage probe;
                PeekMessage(out probe, IntPtr.Zero, 0, 0, PM_NOREMOVE);

                var hookHandle = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(null), 0);
                currentSession.Ready.Set();

                try
                {
                    NativeMessage message;
                    while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                    {
                        TranslateMessage(ref message);
                        DispatchMessage(ref message);
                    }
                }
                finally
                {
                    if (hookHandle != IntPtr.Zero)
                        UnhookWindowsHookEx(hookHandle);
                }
            }

            private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
            {
                if (code >= 0 && (int)wParam == WM_LBUTTONDOWN)
                {
                    var data = (MouseLowLevelHookData)Marshal.PtrToStructure(lParam, typeof(MouseLowLevelHookData));

                    try
                    {
                        onLeftMouseDown(new Point(data.Point.X, data.Point.Y));
                    }
                    catch
                    {
                        // A low-level hook callback must never let an exception
                        // escape into the global input chain.
                    }
                }

                // The hook-handle argument is ignored for low-level hooks.
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            }

            private sealed class HookSession
            {
                public readonly System.Threading.ManualResetEventSlim Ready = new System.Threading.ManualResetEventSlim(false);
                public uint ThreadId;
            }

            private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

            [StructLayout(LayoutKind.Sequential)]
            private struct MouseLowLevelHookData
            {
                public NativePoint Point;
                public uint MouseData;
                public uint Flags;
                public uint Time;
                public IntPtr ExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct NativePoint
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct NativeMessage
            {
                public IntPtr WindowHandle;
                public uint Message;
                public IntPtr WParam;
                public IntPtr LParam;
                public uint Time;
                public NativePoint Point;
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr SetWindowsHookEx(int hookType, LowLevelMouseProc callback, IntPtr module, uint threadId);

            [DllImport("user32.dll")]
            private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

            [DllImport("user32.dll")]
            private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            private static extern int GetMessage(out NativeMessage message, IntPtr windowHandle, uint filterMin, uint filterMax);

            [DllImport("user32.dll")]
            private static extern bool PeekMessage(out NativeMessage message, IntPtr windowHandle, uint filterMin, uint filterMax, uint removeFlags);

            [DllImport("user32.dll")]
            private static extern bool TranslateMessage(ref NativeMessage message);

            [DllImport("user32.dll")]
            private static extern IntPtr DispatchMessage(ref NativeMessage message);

            [DllImport("user32.dll")]
            private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll")]
            private static extern uint GetCurrentThreadId();

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr GetModuleHandle(string moduleName);
        }

        private class DragAroundController
        {
            private readonly Form form;
            private readonly Func<bool> canStartDraggingAround;
            private bool dragAround;
            private int offsetX, offsetY;

            public DragAroundController(Form form, Func<bool> canStartDraggingAround)
            {
                this.form = form;
                this.canStartDraggingAround = canStartDraggingAround;

                form.MouseDown += OnMouseDown;
                form.MouseUp += OnMouseUp;
                form.MouseMove += OnMouseMove;
            }

            private void OnMouseDown(object sender, MouseEventArgs e)
            {
                if (canStartDraggingAround())
                {
                    dragAround = true;
                    offsetX = e.Location.X;
                    offsetY = e.Location.Y;
                }
            }

            private void OnMouseUp(object sender, MouseEventArgs e)
            {
                dragAround = false;
            }

            private void OnMouseMove(object sender, MouseEventArgs e)
            {
                if (dragAround)
                {
                    var relativeX = e.Location.X;
                    var relativeY = e.Location.Y;

                    var deltaX = relativeX - offsetX;
                    var deltaY = relativeY - offsetY;

                    form.Location = new Point(
                        form.Location.X + deltaX,
                        form.Location.Y + deltaY
                    );
                }
            }
        }
    }
}
