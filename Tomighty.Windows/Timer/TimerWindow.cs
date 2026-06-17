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
using System.Runtime.InteropServices;
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

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 0x0002;
        private const int CompactWidth = 280;
        private const int CompactHeight = 152;
        private const int CompactPadding = 8;
        private const int TitleHeight = 26;
        private const int DropDownSize = 30;
        private const int TimeTop = 40;
        private const int TimeHeight = 66;
        private const int ActionButtonWidth = 200;
        private const int ActionButtonHeight = 34;
        private const int ActionButtonCornerRadius = 4;

        private Action currentTimerAction;
        private bool pinned;
        private bool closeButtonPressed;
        private string titleText = "Title";
        private string timeText = "--:--";
        private float layoutScale = 1f;

        private readonly Action<string> UpdateTimeDisplayDelegate;
        private readonly Action<string> UpdateTitleDelegate;
        private readonly Action<string, Action> UpdateTimerButtonTextDelegate;

        public TimerWindow()
        {
            InitializeComponent();

            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            ModernTheme.ApplyTimerWindow(this);
            pinButton.Visible = false;
            pinButton.Enabled = false;
            ModernTheme.StyleIconButton(closeButton);
            ModernTheme.RegisterThemeChange(this, ApplyTheme);
            ApplyTheme();

            Icon = Properties.Resources.icon_tomato_red;

            ApplyCompactFonts();
            ApplyCompactWindowSize();
            UpdateCompactBounds();

            titleLabel.Visible = false;
            timeLabel.Visible = false;
            closeButton.Visible = false;

            UpdateTimeDisplayDelegate = (text) =>
            {
                timeText = text;
                timeLabel.Text = text;
                Invalidate(timeLabel.Bounds);
            };
            UpdateTitleDelegate = (text) =>
            {
                titleText = text;
                titleLabel.Text = text;
                Invalidate(titleLabel.Bounds);
            };
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
                BeginInvoke(UpdateTitleDelegate, text);
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
                BeginInvoke(UpdateTimeDisplayDelegate, text);
            else
                UpdateTimeDisplayDelegate(text);
        }

        public void UpdateColorScheme(Color color)
        {
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            layoutScale = GetCurrentDpiScale();
            ApplyCompactFonts();
            ApplyCompactWindowSize();
            UpdateCompactBounds();
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);

            if (IsHandleCreated)
                UpdateCompactBounds();
        }

        private void ApplyCompactWindowSize()
        {
            var compactSize = new Size(ScaleLayout(CompactWidth), ScaleLayout(CompactHeight));
            if (ClientSize != compactSize)
                ClientSize = compactSize;
        }

        private void ApplyCompactFonts()
        {
            titleLabel.Font = ModernTheme.SemiBoldTextFont(11f);
            timeLabel.Font = ModernTheme.DisplayMediumFont(42f);
            timerButton.Font = ModernTheme.SemiBoldTextFont(9.5f);
        }

        private void UpdateCompactBounds()
        {
            titleLabel.Bounds = GetTitleBounds();
            timeLabel.Bounds = GetTimeBounds();
            closeButton.Bounds = GetDropDownBounds();
            timerButton.Bounds = GetActionButtonBounds();
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (GetDropDownBounds().Contains(e.Location))
                {
                    closeButtonPressed = true;
                    Invalidate(GetDropDownBounds());
                    return;
                }

                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
                return;
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && closeButtonPressed)
            {
                var bounds = GetDropDownBounds();
                closeButtonPressed = false;
                Invalidate(bounds);

                if (bounds.Contains(e.Location))
                    ((DropDownButton)closeButton).RaiseClick();

                return;
            }

            base.OnMouseUp(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

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

            using (var pen = new Pen(ModernTheme.InnerHighlightColor))
            {
                e.Graphics.DrawLine(pen, 1, 1, Width - 2, 1);
                e.Graphics.DrawLine(pen, 1, 1, 1, Height - 2);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            DrawAlignedText(e.Graphics, titleText, titleLabel.Font, GetTitleBounds(), StringAlignment.Center, StringAlignment.Center);
            DrawAlignedText(e.Graphics, timeText, timeLabel.Font, GetTimeBounds(), StringAlignment.Center, StringAlignment.Center);
            DrawDropDownArrow(e.Graphics, GetDropDownBounds());
        }

        private void DrawAlignedText(Graphics graphics, string text, Font font, Rectangle bounds, StringAlignment alignment, StringAlignment lineAlignment)
        {
            using (var brush = new SolidBrush(ModernTheme.TimerTextColor))
            using (var format = new StringFormat(StringFormatFlags.NoWrap))
            {
                format.Alignment = alignment;
                format.LineAlignment = lineAlignment;
                format.Trimming = StringTrimming.EllipsisCharacter;
                graphics.DrawString(text, font, brush, bounds, format);
            }
        }

        private Rectangle GetDropDownBounds()
        {
            var padding = ScaleLayout(CompactPadding);
            return new Rectangle(ClientSize.Width - padding - ScaleLayout(DropDownSize), padding, ScaleLayout(DropDownSize), ScaleLayout(TitleHeight));
        }

        private Rectangle GetTitleBounds()
        {
            var padding = ScaleLayout(CompactPadding);
            return new Rectangle(padding, padding, ClientSize.Width - padding * 2, ScaleLayout(TitleHeight));
        }

        private Rectangle GetTimeBounds()
        {
            var padding = ScaleLayout(CompactPadding);
            return new Rectangle(padding, ScaleLayout(TimeTop), ClientSize.Width - padding * 2, ScaleLayout(TimeHeight));
        }

        private Rectangle GetActionButtonBounds()
        {
            var padding = ScaleLayout(CompactPadding);
            var width = Math.Min(ScaleLayout(ActionButtonWidth), ClientSize.Width - padding * 2);
            var height = ScaleLayout(ActionButtonHeight);
            return new Rectangle((ClientSize.Width - width) / 2, ClientSize.Height - padding - height, width, height);
        }

        private int ScaleLayout(int value)
        {
            return (int)Math.Round(value * layoutScale);
        }

        private float GetCurrentDpiScale()
        {
            if (IsHandleCreated)
            {
                try
                {
                    return Math.Max(1f, GetDpiForWindow(Handle) / 96f);
                }
                catch
                {
                }
            }

            using (var graphics = CreateGraphics())
            {
                return Math.Max(1f, graphics.DpiX / 96f);
            }
        }

        private void DrawDropDownArrow(Graphics graphics, Rectangle bounds)
        {
            var scale = graphics.DpiX / 96f;
            var width = 8f * scale;
            var height = 4f * scale;
            var centerX = bounds.Left + bounds.Width / 2f;
            var centerY = bounds.Top + bounds.Height / 2f + 1f * scale;

            using (var pen = new Pen(ModernTheme.TimerTextColor, Math.Max(1.4f, 1.6f * scale)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawLine(pen, centerX - width / 2f, centerY - height / 2f, centerX, centerY + height / 2f);
                graphics.DrawLine(pen, centerX, centerY + height / 2f, centerX + width / 2f, centerY - height / 2f);
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
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, CreateTextFormat(TextAlign));
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

            private static TextFormatFlags CreateTextFormat(System.Drawing.ContentAlignment alignment)
            {
                var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;

                if (alignment == System.Drawing.ContentAlignment.TopLeft || alignment == System.Drawing.ContentAlignment.MiddleLeft || alignment == System.Drawing.ContentAlignment.BottomLeft)
                    flags |= TextFormatFlags.Left;
                else if (alignment == System.Drawing.ContentAlignment.TopRight || alignment == System.Drawing.ContentAlignment.MiddleRight || alignment == System.Drawing.ContentAlignment.BottomRight)
                    flags |= TextFormatFlags.Right;
                else
                    flags |= TextFormatFlags.HorizontalCenter;

                if (alignment == System.Drawing.ContentAlignment.TopLeft || alignment == System.Drawing.ContentAlignment.TopCenter || alignment == System.Drawing.ContentAlignment.TopRight)
                    flags |= TextFormatFlags.Top;
                else if (alignment == System.Drawing.ContentAlignment.BottomLeft || alignment == System.Drawing.ContentAlignment.BottomCenter || alignment == System.Drawing.ContentAlignment.BottomRight)
                    flags |= TextFormatFlags.Bottom;
                else
                    flags |= TextFormatFlags.VerticalCenter;

                return flags;
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
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
            }
        }

        private void OnTimerButtonClick(object sender, EventArgs e)
        {
            currentTimerAction?.Invoke();
        }

        private void ApplyTheme()
        {
            Opacity = 1.0;
            ForeColor = ModernTheme.TimerTextColor;
            BackColor = ModernTheme.TimerWindowBackColor;
            titleLabel.ForeColor = ModernTheme.TimerTextColor;
            timeLabel.ForeColor = ModernTheme.TimerTextColor;
            closeButton.Invalidate();
            timerButton.Invalidate();
            Invalidate();
        }

        private class DropDownButton : Button
        {
            public void RaiseClick()
            {
                OnClick(EventArgs.Empty);
            }

            public DropDownButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                PaintParentBackground(e);

                var scale = e.Graphics.DpiX / 96f;
                var width = 8f * scale;
                var height = 4f * scale;
                var centerX = ClientRectangle.Left + ClientRectangle.Width / 2f;
                var centerY = ClientRectangle.Top + ClientRectangle.Height / 2f + 1f * scale;

                using (var pen = new Pen(ModernTheme.TimerTextColor, Math.Max(1.4f, 1.6f * scale)))
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
                e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                var bounds = new Rectangle(0, 0, Width, Height);
                var state = GetState();

                DrawButtonSurface(e.Graphics, bounds, state);

                var textColor = Enabled ? ModernTheme.ActionButtonTextColor : ModernTheme.SecondaryTextColor;
                using (var brush = new SolidBrush(textColor))
                using (var format = new StringFormat(StringFormatFlags.NoWrap))
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    e.Graphics.DrawString(Text, Font, brush, bounds, format);
                }

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

                var radius = Math.Max(1f, ActionButtonCornerRadius * graphics.DpiX / 96f);
                var pathBounds = new RectangleF(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

                using (var path = CreateRoundedRectangle(pathBounds, radius))
                using (var brush = new SolidBrush(fill))
                using (var border = new Pen(ModernTheme.ActionButtonBorderColor))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(border, path);
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
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);
    }
}
