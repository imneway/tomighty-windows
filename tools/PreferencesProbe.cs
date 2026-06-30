using System;
using Tomighty;
using Tomighty.Windows.Preferences;

namespace Tomighty.Tools
{
    internal static class PreferencesProbe
    {
        private static int Main(string[] args)
        {
            var preferences = new UserPreferences();
            if (args.Length == 1 && args[0] == "write-smoke")
            {
                preferences.Update(values =>
                {
                    values.SetIntervalDuration(IntervalType.Pomodoro, new Duration(5));
                    values.SetIntervalDuration(IntervalType.ShortBreak, new Duration(2));
                    values.SetIntervalDuration(IntervalType.LongBreak, new Duration(2));
                    values.MaxPomodoroCount = 4;
                    values.ShowToastNotifications = false;
                    values.PlaySoundNotifications = false;
                    values.ShowRemainingTimeOnTray = true;
                    values.SuppressPopupInFullscreenApps = true;
                });
            }

            Console.WriteLine(preferences.GetIntervalDuration(IntervalType.Pomodoro).Seconds);
            Console.WriteLine(preferences.ShowToastNotifications);
            Console.WriteLine(preferences.PlaySoundNotifications);
            Console.WriteLine(preferences.ShowRemainingTimeOnTray);
            Console.WriteLine(preferences.SuppressPopupInFullscreenApps);
            return 0;
        }
    }
}
