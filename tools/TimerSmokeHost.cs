using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Tomighty.Windows.Timer;

namespace Tomighty.Tools
{
    internal static class TimerSmokeHost
    {
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
            using (var form = new TimerWindow())
            {
                backdrop.Show();
                Application.DoEvents();
                Thread.Sleep(250);
                Application.DoEvents();

                form.UpdateTitle("Pomodoro");
                form.UpdateTimeDisplay("25:00");
                form.UpdateColorScheme(Color.FromArgb(197, 47, 61));
                form.SetTimerAction("Interrupt", () => { });
                form.StartPosition = FormStartPosition.Manual;
                if (!form.IsHandleCreated && form.Handle == IntPtr.Zero)
                    return 1;

                form.Location = new Point(80, 80);
                form.Show();

                Application.DoEvents();
                Thread.Sleep(250);
                Application.DoEvents();

                backdrop.Advance();
                Application.DoEvents();
                Thread.Sleep(450);
                Application.DoEvents();

                CaptureWindow(form.Handle, args[0]);
                form.Close();
                backdrop.Close();
            }

            return 0;
        }

        private sealed class ProbeBackdropForm : Form
        {
            private bool alternate;

            public ProbeBackdropForm()
            {
                StartPosition = FormStartPosition.Manual;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
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

        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
