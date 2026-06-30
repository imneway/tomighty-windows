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
        private const int LeftMouseButton = 0x01;
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
        private bool outsideClickDismissArmed;
        private bool leftMouseButtonWasDown;
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
            window.UserMouseInteracted += OnWindowUserMouseInteracted;
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

                Point location = GetLocationAboveTrayIcon();
                window.Show(location);
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
                window.Focus();
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

        private void OnWindowUserMouseInteracted(object sender, EventArgs e)
        {
            outsideClickDismissArmed = true;
            leftMouseButtonWasDown = IsLeftMouseButtonDown();
        }

        private void HideIfFocusMovedOutside()
        {
            if (!ShouldAutoHideWindow() && !ShouldHideForOutsideMouseClick())
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

        private bool ShouldHideForOutsideMouseClick()
        {
            if (window == null || !window.Visible || !CanAutoHideWindow || WasCompletionJustAcknowledged())
                return false;

            if ((DateTime.UtcNow - windowShownTime).TotalMilliseconds < 250)
            {
                UpdateLeftMouseButtonTracking();
                return false;
            }

            Point clickPoint;
            if (!TryConsumeCompletedLeftMouseClick(out clickPoint))
                return false;

            if (window.Bounds.Contains(clickPoint))
            {
                outsideClickDismissArmed = true;
                return false;
            }

            return outsideClickDismissArmed || !WindowHasForegroundFocus();
        }

        private void ResetOutsideClickTracking()
        {
            windowShownTime = DateTime.UtcNow;
            outsideClickDismissArmed = false;
            leftMouseButtonWasDown = IsLeftMouseButtonDown();
        }

        private bool TryConsumeCompletedLeftMouseClick(out Point clickPoint)
        {
            var isDown = IsLeftMouseButtonDown();
            var wasDown = leftMouseButtonWasDown;

            leftMouseButtonWasDown = isDown;

            if (wasDown && !isDown)
            {
                clickPoint = Cursor.Position;
                return true;
            }

            clickPoint = Point.Empty;
            return false;
        }

        private void UpdateLeftMouseButtonTracking()
        {
            leftMouseButtonWasDown = IsLeftMouseButtonDown();
        }

        private static bool IsLeftMouseButtonDown()
        {
            return (GetAsyncKeyState(LeftMouseButton) & unchecked((short)0x8000)) != 0;
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

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

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
            var location = window.Visible && IsPinned
                ? window.Location
                : GetCompletionAlertLocation();

            window.Show(location);
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
