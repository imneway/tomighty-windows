//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Windows.Forms;
using Tomighty.Events;
using Tomighty.Windows.Events;
using Windows.UI.Notifications;

namespace Tomighty.Windows.Notifications
{
    internal class NotificationsPresenter
    {
        private static readonly Logger Logger = new Logger("notifications");
        private static readonly object LogLock = new object();

        private readonly IPomodoroEngine pomodoroEngine;
        private readonly IUserPreferences userPreferences;
        private readonly ToastNotifier toastNotifier = ToastNotificationManager.CreateToastNotifier("Tomighty");
        private readonly Control invoker = new Control();

        public NotificationsPresenter(IPomodoroEngine pomodoroEngine, IUserPreferences userPreferences, IEventHub eventHub)
        {
            this.pomodoroEngine = pomodoroEngine;
            this.userPreferences = userPreferences;
            GC.KeepAlive(invoker.Handle);
            eventHub.Subscribe<TimerStopped>(OnTimerStopped);
            eventHub.Subscribe<FirstRun>(OnFirstRun);
        }

        private void OnFirstRun(FirstRun @event)
        {
            ShowToast("first-run", Toasts.FirstRun);
        }

        private void OnTimerStopped(TimerStopped @event)
        {
            if (@event.IsIntervalCompleted && userPreferences.ShowToastNotifications)
            {
                var intervalType = @event.IntervalType;
                var suggestedBreakType = pomodoroEngine.SuggestedBreakType;
                ShowToast($"interval-completed:{intervalType}", () =>
                {
                    var toast = Toasts.IntervalCompleted(intervalType, suggestedBreakType);
                    toast.Activated += OnToastActivated;
                    return toast;
                });
            }
        }

        private void ShowToast(string name, Func<ToastNotification> createToast)
        {
            if (invoker.InvokeRequired)
            {
                try
                {
                    invoker.BeginInvoke(new Action(() => ShowToastOnUiThread(name, createToast)));
                }
                catch (Exception exception)
                {
                    LogError(exception);
                }

                return;
            }

            ShowToastOnUiThread(name, createToast);
        }

        private void ShowToastOnUiThread(string name, Func<ToastNotification> createToast)
        {
            try
            {
                var toast = createToast();
                toastNotifier.Show(toast);
                LogInfo($"Toast shown: {name}");
            }
            catch (Exception exception)
            {
                LogError(exception);
            }
        }

        private static void LogInfo(string message)
        {
            lock (LogLock)
                Logger.Info(message);
        }

        private static void LogError(Exception exception)
        {
            lock (LogLock)
                Logger.Error(exception);
        }

        private void OnToastActivated(ToastNotification sender, object args)
        {
            if (args is ToastActivatedEventArgs)
            {
                var activation = args as ToastActivatedEventArgs;

                if (Toasts.TimerAction.WithArgs.ContainsKey(activation.Arguments))
                {
                    var timerAction = Toasts.TimerAction.WithArgs[activation.Arguments];

                    pomodoroEngine.StartTimer(timerAction.IntervalType);
                }
            }
        }
    }
}
