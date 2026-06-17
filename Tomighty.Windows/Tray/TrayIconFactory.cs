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
        private static readonly Color MonochromeIconColor = Color.White;

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
            using (var brush = new SolidBrush(MonochromeIconColor))
            using (var format = CreateCountdownFormat())
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                var text = FormatRemainingTime(remainingTime);
                using (var font = CreateCountdownFont(text))
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.None;
                    graphics.DrawString(text, font, brush, new RectangleF(0, 0, NotifyIconSize, NotifyIconSize), format);
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

            var minutes = (remainingTime.Seconds + 59) / 60;
            return Math.Min(99, minutes).ToString();
        }

        private static Font CreateCountdownFont(string text)
        {
            var fontSize = text.Length > 1 ? 32f : 44f;
            return new Font("Segoe UI Semibold", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private static StringFormat CreateCountdownFormat()
        {
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            format.Trimming = StringTrimming.None;
            format.FormatFlags |= StringFormatFlags.NoWrap;
            return format;
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
