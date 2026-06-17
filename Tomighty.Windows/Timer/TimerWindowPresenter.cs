//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Drawing;
using System.Windows.Forms;
using Tomighty.Events;
using Tomighty.Windows.UI;

namespace Tomighty.Windows.Timer
{
    internal class TimerWindowPresenter
    {
        private const int TrayPopupGap = 8;

        private readonly ICountdownClock countdownClock;
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
        private bool _isPinned;
        private bool requiresManualClose;
        private bool hasLastTrayIconCenter;
        private Point lastTrayIconCenter;
        private DateTime lastDeactivateHideTime = DateTime.MinValue;

        public TimerWindowPresenter(IPomodoroEngine pomodoroEngine, ICountdownClock countdownClock, IEventHub eventHub)
        {
            this.countdownClock = countdownClock;

            idleState = new IdleState(pomodoroEngine);
            pomodoroState = new PomodoroState(pomodoroEngine);
            shortBreakState = new ShortBreakState(pomodoroEngine);
            longBreakState = new LongBreakState(pomodoroEngine);
            pomodoroCompletedState = new PomodoroCompletedState(pomodoroEngine);
            breakFinishedState = new BreakFinishedState(pomodoroEngine);
            pomodoroInterruptedState = new TimerInterruptedState("Pomodoro interrupted", IntervalType.Pomodoro, pomodoroEngine);
            shortBreakInterruptedState = new TimerInterruptedState("Short break interrupted", IntervalType.ShortBreak, pomodoroEngine);
            longBreakInterruptedState = new TimerInterruptedState("Long break interrupted", IntervalType.LongBreak, pomodoroEngine);

            currentState = idleState;
            window = CreateTimerWindow();
            currentState.Apply(window, countdownClock.RemainingTime);
            EnsureWindowHandle();

            eventHub.Subscribe<TimerStarted>(OnTimerStarted);
            eventHub.Subscribe<TimeElapsed>(OnTimeElapsed);
            eventHub.Subscribe<TimerStopped>(OnTimerStopped);
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
            if (CanAutoHideWindow && !window.ContainsFocus)
                window.Hide();
        }

        private void OnWindowDeactivate(object sender, System.EventArgs e)
        {
            if (CanAutoHideWindow)
            {
                lastDeactivateHideTime = DateTime.UtcNow;
                window.Hide();
            }
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
            var location = window.Visible
                ? window.Location
                : GetCompletionAlertLocation();

            window.Show(location);
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

            public PomodoroCompletedState(IPomodoroEngine pomodoroEngine)
            {
                this.pomodoroEngine = pomodoroEngine;
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
                pomodoroEngine.StartTimer(IntervalType.ShortBreak);
            }

            private void StartLongBreak()
            {
                pomodoroEngine.StartTimer(IntervalType.LongBreak);
            }
        }

        private class BreakFinishedState : IWindowState
        {
            private readonly IPomodoroEngine pomodoroEngine;

            public BreakFinishedState(IPomodoroEngine pomodoroEngine)
            {
                this.pomodoroEngine = pomodoroEngine;
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
