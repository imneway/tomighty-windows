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

            using (var backdrop = new AcrylicProbeBackdrop())
            using (var form = new TimerWindow())
            {
                backdrop.StartPosition = FormStartPosition.Manual;
                backdrop.Location = new Point(40, 40);
                backdrop.Show();

                form.UpdateTitle("Pomodoro");
                form.UpdateTimeDisplay("25:00");
                form.UpdateColorScheme(Color.FromArgb(197, 47, 61));
                form.SetTimerAction("Interrupt", () => { });
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(120, 120);
                form.Show();

                Application.DoEvents();
                Thread.Sleep(700);
                Application.DoEvents();

                CaptureWindow(form.Handle, args[0]);
                form.Close();
                backdrop.Close();
            }

            return 0;
        }

        private class AcrylicProbeBackdrop : Form
        {
            public AcrylicProbeBackdrop()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                Size = new Size(640, 420);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(38, 135, 255), Color.FromArgb(255, 214, 104), LinearGradientMode.ForwardDiagonal))
                {
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                }

                using (var brush = new SolidBrush(Color.FromArgb(190, 93, 64, 255)))
                {
                    e.Graphics.FillEllipse(brush, 220, 42, 280, 220);
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
