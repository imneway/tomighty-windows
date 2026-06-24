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
                PlaySound(() => Properties.Resources.audio_windup);
            }
        }

        private void OnTimerStopped(TimerStopped @event)
        {
            if (@event.IsIntervalCompleted && userPreferences.PlaySoundNotifications)
            {
                PlaySound(() => Properties.Resources.audio_deskbell);
            }
        }

        private static void PlaySound(Func<Stream> openStream)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var stream = openStream())
                    using (var player = new SoundPlayer(stream))
                    {
                        player.Load();
                        player.PlaySync();
                    }
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(exception);
                }
            });
        }
    }
}
