using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Tomighty.Windows.Timer;

namespace Tomighty.Tools
{
    internal static class TimerSmokeHost
    {
        private static readonly Color ActionButtonBackColor = Color.FromArgb(197, 47, 61);
        private static readonly Color BreakActionButtonBackColor = Color.FromArgb(0, 155, 59);
        private static readonly Color NeutralActionButtonBackColor = Color.FromArgb(133, 137, 145);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: TimerSmokeHost.exe <output.png>");
                return 2;
            }

            SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var backdrop = new ProbeBackdropForm())
            {
                backdrop.Show();
                Application.DoEvents();
                Thread.Sleep(250);
                Application.DoEvents();

                backdrop.Advance();
                Application.DoEvents();
                Thread.Sleep(450);
                Application.DoEvents();

                CaptureStates(args[0], new Point(80, 80), backdrop);
                backdrop.Close();
            }

            return 0;
        }

        private static void CaptureStates(string primaryOutput, Point location, IWin32Window owner)
        {
            var outputDirectory = Path.Combine(Path.GetDirectoryName(primaryOutput), "timer-window-states");
            Directory.CreateDirectory(outputDirectory);
            foreach (var stalePng in Directory.GetFiles(outputDirectory, "*.png"))
                File.Delete(stalePng);

            var shots = new[]
            {
                TimerStateShot.Timed("01-pomodoro", "Pomodoro", "25:00", "Interrupt", ActionButtonBackColor),
                TimerStateShot.Timed("02-short-break", "Short Break", "05:00", "Interrupt", BreakActionButtonBackColor),
                TimerStateShot.Timed("03-long-break", "Long Break", "15:00", "Interrupt", BreakActionButtonBackColor),
                TimerStateShot.Message("04-idle", "Idle", "Start Pomodoro", NeutralActionButtonBackColor),
                TimerStateShot.Message("05-pomodoro-completed-short", "Pomodoro Completed", "Start Short break", BreakActionButtonBackColor, "Skip Break", NeutralActionButtonBackColor),
                TimerStateShot.Message("06-pomodoro-completed-long", "Pomodoro Completed", "Start Long break", BreakActionButtonBackColor, "Skip Break", NeutralActionButtonBackColor),
                TimerStateShot.Message("07-break-finished", "Break Finished", "Start Pomodoro", NeutralActionButtonBackColor),
                TimerStateShot.Message("08-pomodoro-interrupted", "Pomodoro Interrupted", "Restart", NeutralActionButtonBackColor),
                TimerStateShot.Message("09-short-break-interrupted", "Short Break Interrupted", "Restart", NeutralActionButtonBackColor),
                TimerStateShot.Message("10-long-break-interrupted", "Long Break Interrupted", "Restart", NeutralActionButtonBackColor)
            };

            var images = new Bitmap[shots.Length];
            try
            {
                for (var index = 0; index < shots.Length; index++)
                {
                    using (var form = CreateTimerWindow(shots[index], location, owner))
                    {
                        Application.DoEvents();
                        Thread.Sleep(250);
                        Application.DoEvents();

                        var shotPath = Path.Combine(outputDirectory, shots[index].FileName + ".png");
                        CaptureWindow(form.Handle, shotPath);
                        images[index] = new Bitmap(shotPath);

                        if (index == 0)
                            images[index].Save(primaryOutput, ImageFormat.Png);

                        form.Close();
                    }
                }

                SaveContactSheet(images, Path.Combine(outputDirectory, "all-states.png"));
            }
            finally
            {
                for (var index = 0; index < images.Length; index++)
                {
                    if (images[index] != null)
                        images[index].Dispose();
                }
            }
        }

        private static TimerWindow CreateTimerWindow(TimerStateShot shot, Point location, IWin32Window owner)
        {
            var form = new TimerWindow();
            form.UpdateTitle(shot.Title);
            if (shot.ShowTime)
                form.UpdateTimeDisplay(shot.Time);
            else
                form.HideTimeDisplay();

            form.UpdateColorScheme(shot.AccentColor);
            if (shot.HasSecondaryAction)
                form.SetTimerActions(shot.PrimaryActionText, () => { }, shot.PrimaryActionColor, shot.SecondaryActionText, () => { }, shot.SecondaryActionColor);
            else
                form.SetTimerAction(shot.PrimaryActionText, () => { }, shot.PrimaryActionColor);

            form.StartPosition = FormStartPosition.Manual;

            if (!form.IsHandleCreated && form.Handle == IntPtr.Zero)
                throw new InvalidOperationException("TimerWindow handle was not created.");

            form.Location = location;
            form.Show(owner);
            form.Activate();
            form.BringToFront();
            SetForegroundWindow(form.Handle);
            form.Invalidate(true);
            form.Update();
            return form;
        }

        private static void SaveContactSheet(Bitmap[] images, string output)
        {
            if (images.Length == 0 || images[0] == null)
                return;

            const int columns = 2;
            const int gap = 24;
            var rows = (int)Math.Ceiling(images.Length / (double)columns);
            var cellWidth = 0;
            var cellHeight = 0;
            for (var index = 0; index < images.Length; index++)
            {
                if (images[index] == null)
                    continue;

                cellWidth = Math.Max(cellWidth, images[index].Width);
                cellHeight = Math.Max(cellHeight, images[index].Height);
            }

            var width = columns * cellWidth + (columns + 1) * gap;
            var height = rows * cellHeight + (rows + 1) * gap;

            using (var sheet = new Bitmap(width, height))
            using (var graphics = Graphics.FromImage(sheet))
            {
                graphics.Clear(Color.White);

                for (var index = 0; index < images.Length; index++)
                {
                    var column = index % columns;
                    var row = index / columns;
                    var x = gap + column * (cellWidth + gap);
                    var y = gap + row * (cellHeight + gap);
                    graphics.DrawImageUnscaled(images[index], x + (cellWidth - images[index].Width) / 2, y);
                }

                sheet.Save(output, ImageFormat.Png);
            }
        }

        private sealed class TimerStateShot
        {
            public readonly string FileName;
            public readonly string Title;
            public readonly string Time;
            public readonly bool ShowTime;
            public readonly Color AccentColor;
            public readonly string PrimaryActionText;
            public readonly Color PrimaryActionColor;
            public readonly string SecondaryActionText;
            public readonly Color SecondaryActionColor;

            private TimerStateShot(string fileName, string title, string time, bool showTime, Color accentColor, string primaryActionText, Color primaryActionColor, string secondaryActionText, Color secondaryActionColor)
            {
                FileName = fileName;
                Title = title;
                Time = time;
                ShowTime = showTime;
                AccentColor = accentColor;
                PrimaryActionText = primaryActionText;
                PrimaryActionColor = primaryActionColor;
                SecondaryActionText = secondaryActionText;
                SecondaryActionColor = secondaryActionColor;
            }

            public bool HasSecondaryAction => !string.IsNullOrEmpty(SecondaryActionText);

            public static TimerStateShot Timed(string fileName, string title, string time, string actionText, Color actionColor)
            {
                return new TimerStateShot(fileName, title, time, true, actionColor, actionText, actionColor, "", NeutralActionButtonBackColor);
            }

            public static TimerStateShot Message(string fileName, string title, string primaryActionText, Color primaryActionColor)
            {
                return Message(fileName, title, primaryActionText, primaryActionColor, "", NeutralActionButtonBackColor);
            }

            public static TimerStateShot Message(string fileName, string title, string primaryActionText, Color primaryActionColor, string secondaryActionText, Color secondaryActionColor)
            {
                return new TimerStateShot(fileName, title, "", false, primaryActionColor, primaryActionText, primaryActionColor, secondaryActionText, secondaryActionColor);
            }
        }

        private sealed class ProbeBackdropForm : Form
        {
            private bool alternate;

            public ProbeBackdropForm()
            {
                StartPosition = FormStartPosition.Manual;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                Bounds = new Rectangle(40, 40, 520, 360);
            }

            public void Advance()
            {
                alternate = true;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                var startColor = alternate ? Color.FromArgb(248, 248, 248) : Color.FromArgb(32, 126, 235);
                var endColor = alternate ? Color.FromArgb(210, 226, 244) : Color.FromArgb(236, 242, 248);

                using (var brush = new LinearGradientBrush(ClientRectangle, startColor, endColor, 0f))
                {
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                }

                using (var brush = new SolidBrush(alternate ? Color.FromArgb(128, 30, 30, 30) : Color.FromArgb(120, 100, 90, 220)))
                {
                    e.Graphics.FillEllipse(brush, alternate ? 28 : 260, alternate ? 32 : -70, 260, 210);
                }

                using (var brush = new SolidBrush(alternate ? Color.FromArgb(112, 255, 255, 255) : Color.FromArgb(130, 197, 47, 61)))
                {
                    e.Graphics.FillRectangle(brush, 0, 240, 520, 70);
                }
            }
        }

        private static void CaptureWindow(IntPtr handle, string output)
        {
            RECT rect;
            GetWindowRect(handle, out rect);

            var left = rect.Left;
            var top = rect.Top;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            var dpi = GetDpiForWindow(handle);

            if (dpi > 96 && width < 200)
            {
                var scale = dpi / 96f;
                left = (int)Math.Round(left * scale);
                top = (int)Math.Round(top * scale);
                width = (int)Math.Round(width * scale);
                height = (int)Math.Round(height * scale);
            }

            using (var bitmap = new Bitmap(width, height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height));
                bitmap.Save(output, ImageFormat.Png);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
