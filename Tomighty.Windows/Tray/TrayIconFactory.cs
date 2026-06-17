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
        private const int MenuIconSize = 16;
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

        public static Bitmap CreateMenuImage()
        {
            return File.Exists(DefaultIconPath)
                ? CreateScaledAssetBitmap(MenuIconSize)
                : CreateFallbackDefaultBitmap(MenuIconSize);
        }

        public static Icon CreateRemainingTimeIcon(Duration remainingTime)
        {
            using (var bitmap = new Bitmap(NotifyIconSize, NotifyIconSize, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var pen = new Pen(MonochromeIconColor, 2f))
            using (var brush = new SolidBrush(MonochromeIconColor))
            using (var format = new StringFormat(StringFormatFlags.NoWrap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                using (var path = CreateRoundedRectangle(new RectangleF(4.5f, 5.5f, 23f, 21f), 3f))
                {
                    graphics.DrawPath(pen, path);
                }

                var text = FormatRemainingTime(remainingTime);
                using (var font = CreateCountdownFont(graphics, text))
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.None;
                    graphics.DrawString(text, font, brush, new RectangleF(4, 4, 24, 24), format);
                }

                return CreateIcon(bitmap);
            }
        }

        private static Bitmap CreateScaledAssetBitmap(int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var source = Image.FromFile(DefaultIconPath))
                {
                    graphics.DrawImage(source, new Rectangle(0, 0, size, size));
                }
            }

            return bitmap;
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

        private static Font CreateCountdownFont(Graphics graphics, string text)
        {
            var fontSize = text.Length > 1 ? 14f : 16f;
            while (fontSize >= 9f)
            {
                var font = new Font("Segoe UI Semibold", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
                var size = graphics.MeasureString(text, font);
                if (size.Width <= 21f && size.Height <= 18f)
                    return font;

                font.Dispose();
                fontSize -= 1f;
            }

            return new Font("Segoe UI Semibold", 9f, FontStyle.Regular, GraphicsUnit.Pixel);
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
