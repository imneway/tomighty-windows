using System.Media;
using Tomighty.Events;

namespace Tomighty.Windows.Notifications
{
    public class SoundNotificationPlayer
    {
        private readonly IUserPreferences userPreferences;
        private readonly SoundPlayer intervalCompletedNotification = new SoundPlayer(Properties.Resources.audio_deskbell);
        private readonly SoundPlayer timerStartedNotification = new SoundPlayer(Properties.Resources.audio_windup);

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
                timerStartedNotification.Play();
            }
        }

        private void OnTimerStopped(TimerStopped @event)
        {
            if (@event.IsIntervalCompleted && userPreferences.PlaySoundNotifications)
            {
                intervalCompletedNotification.Play();
            }
        }
    }
}
