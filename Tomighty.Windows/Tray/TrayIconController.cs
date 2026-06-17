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
using Tomighty.Windows.Timer;

namespace Tomighty.Windows.Tray
{
    internal class TrayIconController : IDisposable
    {
        private readonly NotifyIcon notifyIcon;
        private readonly TimerWindowPresenter timerWindowPresenter;
        private readonly IUserPreferences userPreferences;
        private Icon currentIcon;

        public TrayIconController(NotifyIcon notifyIcon, TimerWindowPresenter timerWindowPresenter, IUserPreferences userPreferences, IEventHub eventHub)
        {
            this.notifyIcon = notifyIcon;
            this.timerWindowPresenter = timerWindowPresenter;
            this.userPreferences = userPreferences;

            notifyIcon.Click += OnNotifyIconClick;
            SetIcon(TrayIconFactory.CreateDefaultIcon());

            eventHub.Subscribe<TimerStarted>(OnTimerStarted);
            eventHub.Subscribe<TimerStopped>(OnTimerStopped);
            eventHub.Subscribe<TimeElapsed>(OnTimeElapsed);
        }
        
        public void Dispose()
        {
            DisposeCurrentIcon();
            notifyIcon.Container.Dispose();
        }

        private void OnNotifyIconClick(object sender, EventArgs @event)
        {
            if (((MouseEventArgs)@event).Button == MouseButtons.Left)
            {
                timerWindowPresenter.Toggle(Cursor.Position);
            }
        }

        private void OnTimerStarted(TimerStarted @event)
        {
            UpdateTimerIcon(@event.RemainingTime);
        }

        private void OnTimerStopped(TimerStopped @event)
        {
            SetIcon(TrayIconFactory.CreateDefaultIcon());
        }

        private void OnTimeElapsed(TimeElapsed @event)
        {
            UpdateTimerIcon(@event.RemainingTime);
        }

        private void UpdateTimerIcon(Duration remainingTime)
        {
            SetIcon(userPreferences.ShowRemainingTimeOnTray
                ? TrayIconFactory.CreateRemainingTimeIcon(remainingTime)
                : TrayIconFactory.CreateDefaultIcon());
        }

        private void SetIcon(Icon icon)
        {
            var previousIcon = currentIcon;
            currentIcon = icon;
            notifyIcon.Icon = currentIcon;
            previousIcon?.Dispose();
        }

        private void DisposeCurrentIcon()
        {
            currentIcon?.Dispose();
            currentIcon = null;
        }
    }
}
