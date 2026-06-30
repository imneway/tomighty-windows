using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Threading;
using Tomighty.Events;

namespace Tomighty.Windows.Notifications
{
    public class SoundNotificationPlayer
    {
        private static readonly Logger Logger = new Logger("sound");
        private static readonly object LogLock = new object();

        private readonly IUserPreferences userPreferences;

        public SoundNotificationPlayer(IUserPreferences userPreferences, IEventHub eventHub)
        {
            this.userPreferences = userPreferences;
            eventHub.Subscribe<TimerStarted>(OnTimerStarted);
            eventHub.Subscribe<TimerStopped>(OnTimerStopped);
        }

        private void OnTimerStarted(TimerStarted @event)
        {
            if (userPreferences.PlaySoundNotifications)
            {
                PlaySound("timer-started", () => Properties.Resources.audio_windup);
            }
        }

        private void OnTimerStopped(TimerStopped @event)
        {
            if (@event.IsIntervalCompleted && userPreferences.PlaySoundNotifications)
            {
                PlaySound($"interval-completed:{@event.IntervalType}", () => Properties.Resources.audio_deskbell);
            }
        }

        private static void PlaySound(string name, Func<Stream> openStream)
        {
            LogInfo($"Queue sound: {name}");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var stream = openStream())
                    using (var player = new SoundPlayer(stream))
                    {
                        LogInfo($"Play sound start: {name}, length={stream.Length}");
                        player.Load();
                        player.PlaySync();
                        LogInfo($"Play sound finished: {name}");
                    }
                }
                catch (Exception exception)
                {
                    LogError(exception);
                    Debug.WriteLine(exception);
                }
            });
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
    }
}
