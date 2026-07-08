//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Timers;
using Tomighty.Events;
using SystemTimer = System.Timers.Timer;

namespace Tomighty
{
    public class Timer : ITimer
    {
        private const int IntervalInSeconds = 1;

        private readonly SystemTimer systemTimer = new SystemTimer();
        private readonly IEventHub eventHub;
        private readonly object stateLock = new object();

        // Mutable fields, guarded by stateLock
        private Duration remainingTime = Duration.Zero;
        private IntervalType intervalType;
        private Duration duration;
        private bool isRunning;

        public Timer(IEventHub eventHub)
        {
            this.eventHub = eventHub;

            systemTimer.Interval = IntervalInSeconds * 1000;
            systemTimer.AutoReset = true;
            systemTimer.Elapsed += SystemTimerOnElapsed;
        }

        public Duration RemainingTime => remainingTime;

        public void Start(Duration duration, IntervalType intervalType)
        {
            lock (stateLock)
            {
                this.duration = duration;
                this.intervalType = intervalType;

                remainingTime = duration;
                isRunning = true;

                systemTimer.Start();
            }

            eventHub.Publish(new TimerStarted(intervalType, duration, duration));
        }

        public void Stop()
        {
            IntervalType stoppedIntervalType;
            Duration stoppedDuration;
            Duration stoppedRemainingTime;

            lock (stateLock)
            {
                if (!isRunning)
                    return;

                isRunning = false;
                systemTimer.Stop();

                stoppedIntervalType = intervalType;
                stoppedDuration = duration;
                stoppedRemainingTime = remainingTime;
                remainingTime = Duration.Zero;
            }

            eventHub.Publish(new TimerStopped(stoppedIntervalType, stoppedDuration, stoppedRemainingTime));
        }

        private void SystemTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            IntervalType elapsedIntervalType;
            Duration elapsedDuration;
            Duration elapsedRemainingTime;
            bool isIntervalCompleted;

            // System.Timers.Timer can deliver one more Elapsed callback after
            // Stop, and callbacks can race with Stop/Start from other threads,
            // so every state transition happens atomically under the lock.
            lock (stateLock)
            {
                if (!isRunning)
                    return;

                remainingTime = Duration.InSeconds(Math.Max(0, remainingTime.Seconds - IntervalInSeconds));

                elapsedIntervalType = intervalType;
                elapsedDuration = duration;
                elapsedRemainingTime = remainingTime;
                isIntervalCompleted = remainingTime.Seconds == 0;

                if (isIntervalCompleted)
                {
                    isRunning = false;
                    systemTimer.Stop();
                }
            }

            eventHub.Publish(new TimeElapsed(elapsedIntervalType, elapsedDuration, elapsedRemainingTime));

            if (isIntervalCompleted)
                eventHub.Publish(new TimerStopped(elapsedIntervalType, elapsedDuration, elapsedRemainingTime));
        }
    }
}
