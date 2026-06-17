//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Tomighty.Windows.Tray
{
    internal static class TrayIconFactory
    {
        private const int NotifyIconSize = 32;
        private const float CountdownCornerRadius = 4f;
        private const float CountdownFontSize = 21f;
        private const float CountdownTextVerticalOffset = -1f;
        private static readonly Color MonochromeIconColor = Color.White;
        private static readonly Color CountdownBackColor = Color.White;
        private static readonly Color CountdownTextColor = Color.FromArgb(230, 0, 0, 0);

        public static Icon CreateDefaultIcon()
        {
            if (File.Exists(DefaultIconPath))
            {
                using (var bitmap = new Bitmap(DefaultIconPath))
                {
                    return CreateIcon(bitmap);
                }
            }

            using (var bitmap = CreateFallbackDefaultBitmap(NotifyIconSize))
            {
                return CreateIcon(bitmap);
            }
        }

        public static Icon CreateRemainingTimeIcon(Duration remainingTime)
        {
            using (var bitmap = new Bitmap(NotifyIconSize, NotifyIconSize, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var background = new SolidBrush(CountdownBackColor))
            using (var foreground = new SolidBrush(CountdownTextColor))
            using (var format = CreateCountdownFormat())
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                var bounds = new RectangleF(0f, 2f, 32f, 28f);
                using (var path = CreateRoundedRectangle(bounds, CountdownCornerRadius))
                {
                    graphics.FillPath(background, path);
                }

                var text = FormatRemainingTime(remainingTime);
                using (var font = CreateCountdownFont())
                {
                    DrawCenteredCountdownText(graphics, foreground, font, format, bounds, text);
                }

                return CreateIcon(bitmap);
            }
        }

        private static Bitmap CreateFallbackDefaultBitmap(int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                DrawFallbackDefaultIcon(graphics, size);
            }

            return bitmap;
        }

        private static string DefaultIconPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "tomighty.png"); }
        }

        private static void DrawFallbackDefaultIcon(Graphics graphics, int size)
        {
            var scale = size / 32f;
            using (var pen = new Pen(MonochromeIconColor, Math.Max(1f, 2f * scale)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawEllipse(pen, 4f * scale, 4f * scale, 24f * scale, 24f * scale);
                graphics.DrawLine(pen, 11f * scale, 16f * scale, 15f * scale, 20f * scale);
                graphics.DrawLine(pen, 15f * scale, 20f * scale, 23f * scale, 12f * scale);
            }
        }

        private static string FormatRemainingTime(Duration remainingTime)
        {
            if (remainingTime.Seconds < 60)
                return Math.Max(0, remainingTime.Seconds).ToString();

            var minutes = remainingTime.Seconds / 60;
            return Math.Min(99, minutes).ToString();
        }

        private static Font CreateCountdownFont()
        {
            return new Font("Segoe UI", CountdownFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        private static StringFormat CreateCountdownFormat()
        {
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.Trimming = StringTrimming.None;
            format.FormatFlags |= StringFormatFlags.NoWrap;
            return format;
        }

        private static void DrawCenteredCountdownText(Graphics graphics, Brush foreground, Font font, StringFormat format, RectangleF bounds, string text)
        {
            using (var path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, format);

                var textBounds = path.GetBounds();
                var x = bounds.Left + (bounds.Width - textBounds.Width) / 2f - textBounds.Left;
                var y = bounds.Top + (bounds.Height - textBounds.Height) / 2f - textBounds.Top + CountdownTextVerticalOffset;

                using (var transform = new Matrix())
                {
                    transform.Translate(x, y);
                    path.Transform(transform);
                }

                graphics.FillPath(foreground, path);
            }
        }

        private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
        {
            var diameter = radius * 2f;
            var path = new GraphicsPath();

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }

        private static Icon CreateIcon(Bitmap bitmap)
        {
            var handle = bitmap.GetHicon();
            try
            {
                using (var icon = Icon.FromHandle(handle))
                {
                    return (Icon)icon.Clone();
                }
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
