//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tomighty.Windows.UI
{
    internal static class ModernTheme
    {
        private const string TextFontName = "Segoe UI";
        private const string TextMediumFontName = "Segoe UI Semibold";
        private const string TextSemiBoldFontName = "Segoe UI Semibold";
        private const string TimerDisplayFontName = "OPPO Sans 4.0 EN";
        private const string TimerDisplayMediumFontName = "OPPO Sans 4.0 EN Medium_opsz40";
        private static readonly Color CommandPaletteDarkBaseColor = Color.FromArgb(32, 32, 32);
        private static readonly Color CommandPaletteLightBaseColor = Color.FromArgb(243, 243, 243);
        private static readonly Color TomatoRed = Color.FromArgb(197, 47, 61);
        private static readonly Lazy<bool> TextMediumFontAvailable = new Lazy<bool>(() => IsFontInstalled(TextMediumFontName));
        private static readonly Lazy<bool> TimerDisplayFontAvailable = new Lazy<bool>(() => IsFontInstalled(TimerDisplayFontName));
        private static readonly Lazy<bool> TimerDisplayMediumFontAvailable = new Lazy<bool>(() => IsFontInstalled(TimerDisplayMediumFontName));

        public static bool IsLightTheme => ReadAppsUseLightTheme();
        public static bool IsDarkTheme => !IsLightTheme;

        public static Color WindowBackColor => IsLightTheme ? CommandPaletteLightBaseColor : CommandPaletteDarkBaseColor;
        public static Color TimerWindowBackColor => WindowBackColor;
        public static Color MaterialLayerBackColor => IsLightTheme ? Color.FromArgb(160, 255, 255, 255) : Color.FromArgb(80, 32, 32, 32);
        public static Color MaterialLayerSecondaryBackColor => IsLightTheme ? Color.FromArgb(102, 255, 255, 255) : Color.FromArgb(36, 32, 32, 32);
        public static Color MaterialLayerHoverBackColor => IsLightTheme ? Color.FromArgb(190, 255, 255, 255) : Color.FromArgb(104, 48, 48, 48);
        public static Color ControlBackColor => IsLightTheme ? SystemColors.Control : Color.FromArgb(45, 45, 45);
        public static Color ControlHoverBackColor => IsLightTheme ? Color.FromArgb(229, 241, 251) : Color.FromArgb(62, 62, 62);
        public static Color ControlPressedBackColor => IsLightTheme ? Color.FromArgb(204, 228, 247) : Color.FromArgb(73, 73, 73);
        public static Color TextColor => IsLightTheme ? SystemColors.ControlText : Color.FromArgb(255, 255, 255);
        public static Color TimerTextColor => Color.FromArgb(28, 28, 28);
        public static Color SecondaryTextColor => IsLightTheme ? SystemColors.GrayText : Color.FromArgb(200, 200, 200);
        public static Color BorderColor => IsLightTheme ? Color.FromArgb(138, 0, 0, 0) : Color.FromArgb(118, 255, 255, 255);
        public static Color InnerHighlightColor => IsLightTheme ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(42, 255, 255, 255);
        public static Color ActionButtonBackColor => TomatoRed;
        public static Color ActionButtonHoverBackColor => Color.FromArgb(214, 62, 76);
        public static Color ActionButtonPressedBackColor => Color.FromArgb(160, 36, 49);
        public static Color BreakActionButtonBackColor => Color.FromArgb(0, 155, 59);
        public static Color NeutralActionButtonBackColor => Color.FromArgb(133, 137, 145);
        public static Color ActionButtonBorderColor => Color.FromArgb(31, 0, 0, 0);
        public static Color ActionButtonTextColor => Color.White;
        public static Color InputBackColor => IsLightTheme ? SystemColors.Window : Color.FromArgb(32, 32, 32);
        public static Color MenuBackColor => IsLightTheme ? SystemColors.Menu : Color.FromArgb(32, 32, 32);
        public static Color MenuTextColor => IsLightTheme ? SystemColors.MenuText : TextColor;
        public static bool IsTimerDisplayFontAvailable => TimerDisplayFontAvailable.Value;
        public static bool IsTimerDisplayMediumFontAvailable => TimerDisplayMediumFontAvailable.Value;

        public static Font TextFont(float size = 9f, FontStyle style = FontStyle.Regular)
        {
            return new Font(TextFontName, size, style, GraphicsUnit.Point);
        }

        public static Font SemiBoldTextFont(float size = 9f)
        {
            return new Font(TextSemiBoldFontName, size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font MediumTextFont(float size = 9f)
        {
            var fontName = TextMediumFontAvailable.Value ? TextMediumFontName : TextSemiBoldFontName;
            return new Font(fontName, size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font DisplayFont(float size, FontStyle style = FontStyle.Regular)
        {
            var fontName = IsTimerDisplayFontAvailable ? TimerDisplayFontName : TextFontName;
            return new Font(fontName, size, style, GraphicsUnit.Point);
        }

        public static Font DisplayMediumFont(float size)
        {
            if (IsTimerDisplayMediumFontAvailable)
                return new Font(TimerDisplayMediumFontName, size, FontStyle.Regular, GraphicsUnit.Point);

            return DisplayFont(size);
        }

        public static void ApplyMicaDialog(Form form)
        {
            Windows11Backdrop.ApplyMica(form);
            ApplyNativeForm(form, BackdropMaterial.Mica, () => WindowBackColor);
        }

        public static void ApplyTimerWindow(Form form)
        {
            Windows11Backdrop.ApplyAcrylic(form);
            ApplyNativeForm(form, BackdropMaterial.Acrylic, () => TimerWindowBackColor);
            form.BackColor = TimerWindowBackColor;
            form.Opacity = 1.0;
        }

        public static void ApplyContextMenu(ContextMenuStrip menu)
        {
            ApplyContextMenuColors(menu);
            Windows11Backdrop.ApplyAcrylic(menu);
            RegisterThemeChange(menu, () => ApplyContextMenuColors(menu));
        }

        public static void StyleDefaultButton(Button button)
        {
            if (IsLightTheme)
            {
                button.FlatStyle = FlatStyle.Standard;
                button.UseVisualStyleBackColor = true;
                button.BackColor = SystemColors.Control;
                button.ForeColor = SystemColors.ControlText;
                return;
            }

            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = ControlBackColor;
            button.ForeColor = TextColor;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.MouseOverBackColor = ControlHoverBackColor;
            button.FlatAppearance.MouseDownBackColor = ControlPressedBackColor;
        }

        public static void StylePrimaryButton(Button button, Color accent)
        {
            StyleDefaultButton(button);
        }

        public static void StyleIconButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.Transparent;
            button.ForeColor = TextColor;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlHoverBackColor;
            button.FlatAppearance.MouseDownBackColor = ControlPressedBackColor;
            button.UseVisualStyleBackColor = false;
        }

        public static Image CreateThemedImage(Image source)
        {
            var image = new Bitmap(source.Width, source.Height);
            var color = TextColor;

            using (var sourceBitmap = new Bitmap(source))
            {
                for (var x = 0; x < sourceBitmap.Width; x++)
                {
                    for (var y = 0; y < sourceBitmap.Height; y++)
                    {
                        var pixel = sourceBitmap.GetPixel(x, y);
                        image.SetPixel(x, y, Color.FromArgb(pixel.A, color));
                    }
                }
            }

            return image;
        }

        private static void ApplyNativeForm(Form form, BackdropMaterial material, Func<Color> backColorProvider)
        {
            form.BackColor = backColorProvider();
            form.ForeColor = TextColor;
            form.Font = SystemFonts.MessageBoxFont;
            form.Icon = Properties.Resources.icon_tomato_red;

            ApplyControls(form.Controls);
            RegisterThemeChange(form, () =>
            {
                form.BackColor = backColorProvider();
                form.ForeColor = TextColor;
                ApplyControls(form.Controls);
                Windows11Backdrop.Refresh(form, material);
                form.Invalidate(true);
            });
        }

        private static void ApplyControls(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                ApplyControl(control);
                ApplyControls(control.Controls);
            }
        }

        private static void ApplyControl(Control control)
        {
            control.Font = SystemFonts.MessageBoxFont;
            control.ForeColor = TextColor;

            if (control is Label)
            {
                control.BackColor = Color.Transparent;
            }
            else if (control is Button)
            {
                StyleDefaultButton((Button)control);
            }
            else if (control is TextBoxBase)
            {
                var textBox = (TextBoxBase)control;
                textBox.BackColor = InputBackColor;
                textBox.ForeColor = TextColor;
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is NumericUpDown)
            {
                control.BackColor = InputBackColor;
                control.ForeColor = TextColor;
            }
            else if (control is CheckBox)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = TextColor;
            }
            else if (control is GroupBox)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = SecondaryTextColor;
            }
            else if (control is TabControl)
            {
                control.BackColor = WindowBackColor;
            }
            else if (control is TabPage)
            {
                control.BackColor = WindowBackColor;
            }
        }

        private static void ApplyContextMenuColors(ContextMenuStrip menu)
        {
            menu.BackColor = MenuBackColor;
            menu.ForeColor = MenuTextColor;
            menu.Font = SystemFonts.MessageBoxFont;
            menu.Renderer = IsLightTheme ? (ToolStripRenderer)new ToolStripSystemRenderer() : new NativeToolStripRenderer();
            menu.ShowImageMargin = true;

            foreach (ToolStripItem item in menu.Items)
            {
                item.BackColor = MenuBackColor;
                item.ForeColor = item.Enabled ? MenuTextColor : SecondaryTextColor;
                item.Font = item.Font == null ? SystemFonts.MessageBoxFont : new Font(SystemFonts.MessageBoxFont, item.Font.Style);
            }
        }

        public static void RegisterThemeChange(Form form, Action applyTheme)
        {
            UserPreferenceChangedEventHandler handler = (sender, args) => BeginApply(form, applyTheme);
            SystemEvents.UserPreferenceChanged += handler;
            form.Disposed += (sender, args) => SystemEvents.UserPreferenceChanged -= handler;
        }

        private static void RegisterThemeChange(ToolStripDropDown menu, Action applyTheme)
        {
            UserPreferenceChangedEventHandler handler = (sender, args) => BeginApply(menu, applyTheme);
            SystemEvents.UserPreferenceChanged += handler;
            menu.Disposed += (sender, args) => SystemEvents.UserPreferenceChanged -= handler;
        }

        private static void BeginApply(Control control, Action applyTheme)
        {
            if (control.IsDisposed)
                return;

            if (control.InvokeRequired)
                control.BeginInvoke(applyTheme);
            else
                applyTheme();
        }

        private static bool ReadAppsUseLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key?.GetValue("AppsUseLightTheme");
                    return !(value is int) || (int)value != 0;
                }
            }
            catch
            {
                return true;
            }
        }

        private static bool IsFontInstalled(string fontName)
        {
            try
            {
                using (var fonts = new InstalledFontCollection())
                {
                    foreach (var family in fonts.Families)
                    {
                        if (string.Equals(family.Name, fontName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private class NativeToolStripRenderer : ToolStripProfessionalRenderer
        {
            public NativeToolStripRenderer()
                : base(new NativeColorTable())
            {
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var bounds = new Rectangle(Point.Empty, e.Item.Size);
                var color = e.Item.Selected ? ControlHoverBackColor : MenuBackColor;

                using (var brush = new SolidBrush(color))
                {
                    e.Graphics.FillRectangle(brush, bounds);
                }
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                var y = e.Item.Height / 2;
                using (var pen = new Pen(BorderColor))
                {
                    e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
                }
            }
        }

        private class NativeColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => MenuBackColor;
            public override Color ImageMarginGradientBegin => MenuBackColor;
            public override Color ImageMarginGradientMiddle => MenuBackColor;
            public override Color ImageMarginGradientEnd => MenuBackColor;
            public override Color MenuBorder => BorderColor;
            public override Color MenuItemBorder => ControlHoverBackColor;
            public override Color MenuItemSelected => ControlHoverBackColor;
            public override Color MenuItemSelectedGradientBegin => ControlHoverBackColor;
            public override Color MenuItemSelectedGradientEnd => ControlHoverBackColor;
        }
    }
}
