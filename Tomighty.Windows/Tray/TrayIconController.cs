//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
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

            notifyIcon.MouseClick += OnNotifyIconMouseClick;
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

        private void OnNotifyIconMouseClick(object sender, MouseEventArgs @event)
        {
            if (@event.Button == MouseButtons.Left)
            {
                var iconBounds = GetNotifyIconBounds();
                timerWindowPresenter.RememberTrayIconBounds(iconBounds);
                timerWindowPresenter.Toggle(iconBounds);
            }
        }

        private Rectangle GetNotifyIconBounds()
        {
            Rect rect;

            if (!TryGetNotifyIconRect(out rect))
            {
                var center = Cursor.Position;
                var size = Math.Max(16, SystemInformation.SmallIconSize.Height);
                return new Rectangle(center.X - size / 2, center.Y - size / 2, size, size);
            }

            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        private bool TryGetNotifyIconRect(out Rect rect)
        {
            rect = default(Rect);

            try
            {
                var type = typeof(NotifyIcon);
                var idField = type.GetField("id", BindingFlags.Instance | BindingFlags.NonPublic);
                var windowField = type.GetField("window", BindingFlags.Instance | BindingFlags.NonPublic);

                if (idField == null || windowField == null)
                    return false;

                var nativeWindow = windowField.GetValue(notifyIcon) as NativeWindow;
                if (nativeWindow == null || nativeWindow.Handle == IntPtr.Zero)
                    return false;

                var identifier = new NotifyIconIdentifier
                {
                    Size = Marshal.SizeOf(typeof(NotifyIconIdentifier)),
                    WindowHandle = nativeWindow.Handle,
                    Id = (uint)(int)idField.GetValue(notifyIcon),
                    Guid = Guid.Empty
                };

                return Shell_NotifyIconGetRect(ref identifier, out rect) == 0;
            }
            catch
            {
                return false;
            }
        }

        private void OnTimerStarted(TimerStarted @event)
        {
            timerWindowPresenter.RememberTrayIconBounds(GetNotifyIconBounds());
            UpdateTimerIcon(@event.RemainingTime);
        }

        private void OnTimerStopped(TimerStopped @event)
        {
            SetIcon(TrayIconFactory.CreateDefaultIcon());
        }

        private void OnTimeElapsed(TimeElapsed @event)
        {
            timerWindowPresenter.RememberTrayIconBounds(GetNotifyIconBounds());
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

        [StructLayout(LayoutKind.Sequential)]
        private struct NotifyIconIdentifier
        {
            public int Size;
            public IntPtr WindowHandle;
            public uint Id;
            public Guid Guid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("shell32.dll", SetLastError = false)]
        private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect iconLocation);
    }
}
