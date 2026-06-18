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
        private const int TrayPopupGap = 8;
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
        private DateTime lastDeactivateHideTime = DateTime.MinValue;
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
                ShowPendingCompletionAlertIfAllowed();
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
                    window.Hide();
            }
            else
            {
                if (!window.IsHandleCreated && window.Handle == IntPtr.Zero)
                    return;

                Point location = GetLocationAboveTrayIcon(approximateLocation);
                window.Show(location);

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
                    EnterState(GetTimerInterruptedStateFor(@event.IntervalType), @event.RemainingTime);
                });
            }
        }

        public void RememberTrayIconCenter(Point location)
        {
            lastTrayIconCenter = location;
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

        private Point GetLocationAboveTrayIcon(Point approximateLocation)
        {
            var screen = Screen.FromPoint(approximateLocation);
            var workingArea = screen.WorkingArea;
            var trayIconSize = Math.Max(16, SystemInformation.SmallIconSize.Height);

            var x = approximateLocation.X - window.Width / 2;
            var y = approximateLocation.Y - trayIconSize / 2 - TrayPopupGap - window.Height;

            x = Math.Max(workingArea.Left + TrayPopupGap, Math.Min(x, workingArea.Right - window.Width - TrayPopupGap));

            if (y < workingArea.Top + TrayPopupGap)
                y = approximateLocation.Y + trayIconSize / 2 + TrayPopupGap;

            y = Math.Max(workingArea.Top + TrayPopupGap, Math.Min(y, workingArea.Bottom - window.Height - TrayPopupGap));

            return new Point(x, y);
        }

        private void OnPinButtonClick(object sender, System.EventArgs e)
        {
            IsPinned = !IsPinned;
        }

        private void OnCloseButtonClick(object sender, System.EventArgs e)
        {
            requiresManualClose = false;
            window.Hide();
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
                window.Hide();
        }

        private void OnWindowDeactivate(object sender, System.EventArgs e)
        {
            if (ShouldAutoHideWindow())
            {
                lastDeactivateHideTime = DateTime.UtcNow;
                window.Hide();
            }
        }

        private void HideIfFocusMovedOutside()
        {
            if (!ShouldAutoHideWindow())
                return;

            lastDeactivateHideTime = DateTime.UtcNow;
            window.Hide();
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

        private void ShowPendingCompletionAlertIfAllowed()
        {
            if (!completionAlertPending || window == null || window.Visible || ShouldSuppressCompletionAlert())
                return;

            completionAlertPending = false;
            ShowCompletionAlertNow();
        }

        private void ShowCompletionAlertNow()
        {
            var location = window.Visible
                ? window.Location
                : GetCompletionAlertLocation();

            window.Show(location);
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
            if (hasLastTrayIconCenter)
                return GetLocationAboveTrayIcon(lastTrayIconCenter);

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
