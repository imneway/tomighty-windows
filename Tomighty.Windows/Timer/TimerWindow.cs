//
//  Tomighty - http://www.tomighty.org
//
//  This software is licensed under the Apache License Version 2.0:
//  http://www.apache.org/licenses/LICENSE-2.0.txt
//

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Tomighty.Windows.UI;

namespace Tomighty.Windows.Timer
{
    public partial class TimerWindow : Form
    {
        public static readonly Color DarkGray = Color.FromArgb(41, 41, 41);
        public static readonly Color Red = Color.FromArgb(168, 32, 41);
        public static readonly Color Green = Color.FromArgb(26, 102, 41);
        public static readonly Color Blue = Color.FromArgb(22, 57, 101);

        private Action currentTimerAction;
        private bool pinned;

        private readonly Action<string> UpdateTimeDisplayDelegate;
        private readonly Action<string> UpdateTitleDelegate;
        private readonly Action<string, Action> UpdateTimerButtonTextDelegate;

        public TimerWindow()
        {
            InitializeComponent();

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            ModernTheme.ApplyTimerWindow(this);
            pinButton.Visible = false;
            pinButton.Enabled = false;
            ModernTheme.StyleIconButton(closeButton);
            ModernTheme.RegisterThemeChange(this, ApplyTheme);
            ApplyTheme();

            Icon = Properties.Resources.icon_tomato_red;

            titleLabel.Font = ModernTheme.TextFont(10f);
            timeLabel.Font = ModernTheme.DisplayFont(50f);
            timerButton.Font = ModernTheme.TextFont(9.5f);

            UpdateTimeDisplayDelegate = (text) => timeLabel.Text = text;
            UpdateTitleDelegate = (text) => titleLabel.Text = text;
            UpdateTimerButtonTextDelegate = (text, action) =>
            {
                timerButton.Text = text;
                timerButton.AccessibleName = text;
            };
        }

        public Button PinButton => pinButton;
        public Button CloseButton => closeButton;

        public void UpdateTitle(string text)
        {
            if (IsHandleCreated)
                titleLabel.BeginInvoke(UpdateTitleDelegate, text);
            else
                UpdateTitleDelegate(text);
        }

        public void SetTimerAction(string text, Action action)
        {
            currentTimerAction = action;

            if (IsHandleCreated)
                timerButton.BeginInvoke(UpdateTimerButtonTextDelegate, text, action);
            else
                UpdateTimerButtonTextDelegate(text, action);
        }

        public void UpdateTimeDisplay(string text)
        {
            if (IsHandleCreated)
                timeLabel.BeginInvoke(UpdateTimeDisplayDelegate, text);
            else
                UpdateTimeDisplayDelegate(text);
        }

        public void UpdateColorScheme(Color color)
        {
            Invalidate();
        }

        public void UpdatePinButtonState(bool pinned)
        {
            this.pinned = pinned;
        }

        public void Show(Point location)
        {
            Location = location;
            Show();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Windows11Backdrop.IsSupported)
            {
                using (var brush = new SolidBrush(ModernTheme.TimerWindowTintColor))
                {
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                }
            }
            else
            {
                e.Graphics.Clear(ModernTheme.TimerWindowBackColor);
            }

            using (var pen = new Pen(ModernTheme.BorderColor))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        private class TransparentLabel : Label
        {
            private const int WM_NCHITTEST = 0x0084;
            private const int HTTRANSPARENT = (-1);

            public TransparentLabel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                PaintParentBackground(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                using (var brush = new SolidBrush(ForeColor))
                using (var format = CreateStringFormat(TextAlign))
                {
                    e.Graphics.DrawString(Text, Font, brush, ClientRectangle, format);
                }
            }

            protected void PaintParentBackground(PaintEventArgs e)
            {
                if (Parent == null)
                    return;

                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform(-Left, -Top);

                try
                {
                    var parentClip = new Rectangle(Left, Top, Width, Height);
                    using (var parentArgs = new PaintEventArgs(e.Graphics, parentClip))
                    {
                        InvokePaintBackground(Parent, parentArgs);
                        InvokePaint(Parent, parentArgs);
                    }
                }
                finally
                {
                    e.Graphics.Restore(state);
                }
            }

            private static StringFormat CreateStringFormat(System.Drawing.ContentAlignment alignment)
            {
                var format = new StringFormat(StringFormatFlags.NoWrap)
                {
                    Trimming = StringTrimming.EllipsisCharacter
                };

                if (alignment == System.Drawing.ContentAlignment.TopLeft || alignment == System.Drawing.ContentAlignment.MiddleLeft || alignment == System.Drawing.ContentAlignment.BottomLeft)
                    format.Alignment = StringAlignment.Near;
                else if (alignment == System.Drawing.ContentAlignment.TopRight || alignment == System.Drawing.ContentAlignment.MiddleRight || alignment == System.Drawing.ContentAlignment.BottomRight)
                    format.Alignment = StringAlignment.Far;
                else
                    format.Alignment = StringAlignment.Center;

                if (alignment == System.Drawing.ContentAlignment.TopLeft || alignment == System.Drawing.ContentAlignment.TopCenter || alignment == System.Drawing.ContentAlignment.TopRight)
                    format.LineAlignment = StringAlignment.Near;
                else if (alignment == System.Drawing.ContentAlignment.BottomLeft || alignment == System.Drawing.ContentAlignment.BottomCenter || alignment == System.Drawing.ContentAlignment.BottomRight)
                    format.LineAlignment = StringAlignment.Far;
                else
                    format.LineAlignment = StringAlignment.Center;

                return format;
            }

            protected override void WndProc(ref Message m)
            {
                // Bubble up mouse events to the parent component so as to
                // allow the timer window to be dragged around even when
                // the user clicks on and moves the mouse over this label
                if (m.Msg == WM_NCHITTEST)
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                }
                else
                {
                    base.WndProc(ref m);
                }
            }
        }

        private class TimerDisplayLabel : TransparentLabel
        {
            public TimerDisplayLabel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                PaintParentBackground(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

                using (var brush = new SolidBrush(ForeColor))
                using (var format = new StringFormat(StringFormatFlags.NoWrap))
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.None;
                    e.Graphics.DrawString(Text, Font, brush, ClientRectangle, format);
                }
            }
        }

        private void OnTimerButtonClick(object sender, EventArgs e)
        {
            currentTimerAction?.Invoke();
        }

        private void ApplyTheme()
        {
            Opacity = 1.0;
            ForeColor = ModernTheme.TextColor;
            BackColor = ModernTheme.TimerWindowBackColor;
            titleLabel.ForeColor = ModernTheme.TextColor;
            timeLabel.ForeColor = ModernTheme.TextColor;
            closeButton.Invalidate();
            timerButton.Invalidate();
            Invalidate();
        }

        private class DropDownButton : Button
        {
            private bool hovered;
            private bool pressed;

            public DropDownButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                hovered = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                hovered = false;
                pressed = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                pressed = true;
                Invalidate();
                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                pressed = false;
                Invalidate();
                base.OnMouseUp(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                PaintParentBackground(e);

                if (hovered || pressed)
                {
                    using (var brush = new SolidBrush(pressed ? ModernTheme.ControlPressedBackColor : ModernTheme.ControlHoverBackColor))
                    {
                        e.Graphics.FillRectangle(brush, ClientRectangle);
                    }
                }

                var scale = e.Graphics.DpiX / 96f;
                var width = 8f * scale;
                var height = 4f * scale;
                var centerX = ClientRectangle.Left + ClientRectangle.Width / 2f;
                var centerY = ClientRectangle.Top + ClientRectangle.Height / 2f + 1f * scale;

                using (var pen = new Pen(ModernTheme.TextColor, Math.Max(1.4f, 1.6f * scale)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(pen, centerX - width / 2f, centerY - height / 2f, centerX, centerY + height / 2f);
                    e.Graphics.DrawLine(pen, centerX, centerY + height / 2f, centerX + width / 2f, centerY - height / 2f);
                }
            }

            private void PaintParentBackground(PaintEventArgs e)
            {
                if (Parent == null)
                    return;

                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform(-Left, -Top);

                try
                {
                    var parentClip = new Rectangle(Left, Top, Width, Height);
                    using (var parentArgs = new PaintEventArgs(e.Graphics, parentClip))
                    {
                        InvokePaintBackground(Parent, parentArgs);
                        InvokePaint(Parent, parentArgs);
                    }
                }
                finally
                {
                    e.Graphics.Restore(state);
                }
            }
        }

        private class NativeActionButton : Button
        {
            private bool hovered;
            private bool pressed;

            public NativeActionButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                hovered = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                hovered = false;
                pressed = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                pressed = true;
                Invalidate();
                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                pressed = false;
                Invalidate();
                base.OnMouseUp(e);
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                Invalidate();
                base.OnEnabledChanged(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

                var bounds = new Rectangle(0, 0, Width, Height);
                var state = GetState();

                DrawButtonSurface(e.Graphics, bounds, state);

                var textColor = Enabled ? ModernTheme.ActionButtonTextColor : ModernTheme.SecondaryTextColor;
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
                TextRenderer.DrawText(e.Graphics, Text, Font, bounds, textColor, flags);

                if (Focused && ShowFocusCues)
                {
                    var focusBounds = Rectangle.Inflate(bounds, -4, -4);
                    ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, textColor, ModernTheme.ActionButtonBackColor);
                }
            }

            private PushButtonState GetState()
            {
                if (!Enabled)
                    return PushButtonState.Disabled;

                if (pressed)
                    return PushButtonState.Pressed;

                if (hovered)
                    return PushButtonState.Hot;

                return PushButtonState.Normal;
            }

            private static void DrawButtonSurface(Graphics graphics, Rectangle bounds, PushButtonState state)
            {
                var fill = state == PushButtonState.Pressed
                    ? ModernTheme.ActionButtonPressedBackColor
                    : state == PushButtonState.Hot
                        ? ModernTheme.ActionButtonHoverBackColor
                        : ModernTheme.ActionButtonBackColor;

                using (var brush = new SolidBrush(fill))
                using (var border = new Pen(ModernTheme.ActionButtonBorderColor))
                {
                    graphics.FillRectangle(brush, bounds);
                    graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
                }
            }
        }
    }
}
