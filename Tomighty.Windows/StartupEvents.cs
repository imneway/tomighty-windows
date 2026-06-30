using System;
using System.Threading.Tasks;
using Tomighty.Windows.Events;

namespace Tomighty.Windows
{
    public class StartupEvents : StartupEventFlags
    {
        public StartupEvents(IEventHub eventHub)
        {
            Task.Run(() =>
            {
                if (Flags.IsOn(FirstRunFlag, true))
                {
                    Flags.TurnOff(FirstRunFlag);
                    eventHub.Publish(new FirstRun());
                }
            });
        }
    }
}
