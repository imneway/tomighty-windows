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
        private const int WM_DPICHANGED = 0x02E0;
        private const int HTCAPTION = 0x0002;
        private const int CompactPadding = 8;
        private const int ActionButtonWidth = 160;
        private const int ActionButtonHeight = 32;
        private const int ActionButtonGap = 4;
        private const int TitleHeight = 20;
        private const int DropDownSize = 30;
        private const int TimeVerticalMargin = 14;
        private const int TimeTop = CompactPadding + TitleHeight + TimeVerticalMargin;
        private const int TimeHeight = 48;
        private const int MessageTitleHeight = 72;
        private const int TitleOnlyContentHeight = 64;
        private const int CompactWidth = ActionButtonWidth + CompactPadding * 2;
        private const int CompactHeight = CompactPadding + TitleHeight + TimeVerticalMargin + TimeHeight + TimeVerticalMargin + ActionButtonHeight + CompactPadding;
        private const int CompactMessageHeight = CompactPadding + MessageTitleHeight + ActionButtonHeight + CompactPadding;
        private const int CompactTitleOnlyHeight = CompactPadding + TitleOnlyContentHeight + ActionButtonHeight + CompactPadding;
        private const int WindowCornerRadius = 20;
        private const int ActionButtonCornerRadius = 8;
        private const int ActionButtonHoverColorOffset = 12;
        private const int ActionButtonPressedColorOffset = -12;
        private const int PopupEnterOffset = 18;
        private const int PopupEnterOvershoot = 4;
        private const int PopupExitOffset = 10;
        private const int PopupEnterDuration = 260;
        private const int PopupExitDuration = 150;
        private const uint SPI_GETUIEFFECTS = 0x103E;
        private const float TitleOnlyFontSize = 13f;

        private enum ActionButtonTone
        {
            Action,
            Break,
            Neutral,
            Custom
        }

        private Action primaryTimerAction;
        private Action secondaryTimerAction;
        private bool pinned;
        private bool closeButtonPressed;
        private bool primaryActionButtonHovered;
        private bool primaryActionButtonPressed;
        private bool secondaryActionButtonHovered;
        private bool secondaryActionButtonPressed;
        private bool timeDisplayVisible = true;
        private string titleText = "Title";
        private string timeText = "--:--";
        private string messageText = "";
        private string primaryActionText = "Timer Action";
        private string secondaryActionText = "";
        private Color primaryActionButtonColor = ModernTheme.ActionButtonBackColor;
        private Color secondaryActionButtonColor = ModernTheme.NeutralActionButtonBackColor;
        private ActionButtonTone primaryActionButtonTone = ActionButtonTone.Action;
        private ActionButtonTone secondaryActionButtonTone = ActionButtonTone.Neutral;
        private float layoutScale = 1f;
        private System.Windows.Forms.Timer popupAnimationTimer;
        private PopupAnimation popupAnimation;

        private readonly Action<string> UpdateTimeDisplayDelegate;
        private readonly Action<string> UpdateTitleDelegate;

        public TimerWindow()
        {
            InitializeComponent();

            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            ModernTheme.ApplyTimerWindow(this);
            Windows11Backdrop.SuppressBorder(this);
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
            timerButton.Visible = false;

            UpdateTimeDisplayDelegate = (text) =>
            {
                timeDisplayVisible = true;
                timeText = text;
                timeLabel.Text = text;
                ApplyCompactWindowSize();
                UpdateCompactBounds();
                Invalidate();
            };
            UpdateTitleDelegate = (text) =>
            {
                titleText = text;
                titleLabel.Text = text;
                Invalidate();
            };
        }

        public Button PinButton => pinButton;
        public Button CloseButton => closeButton;
        public event EventHandler UserMouseInteracted;

        public void UpdateTitle(string text)
        {
            ExecuteOnWindowThread(() => UpdateTitleDelegate(text));
        }

        public void SetTimerAction(string text, Action action)
        {
            SetTimerAction(text, action, ModernTheme.ActionButtonBackColor);
        }

        public void SetTimerAction(string text, Action action, Color color)
        {
            ExecuteOnWindowThread(() => ApplyTimerActions(text, action, color, "", null, ModernTheme.NeutralActionButtonBackColor, ""));
        }

        public void SetTimerActions(string primaryText, Action primaryAction, Color primaryColor, string secondaryText, Action secondaryAction, Color secondaryColor)
        {
            ExecuteOnWindowThread(() => ApplyTimerActions(primaryText, primaryAction, primaryColor, secondaryText, secondaryAction, secondaryColor, ""));
        }

        public void SetTimerMessageActions(string message, string primaryText, Action primaryAction, Color primaryColor, string secondaryText, Action secondaryAction, Color secondaryColor)
        {
            ExecuteOnWindowThread(() => ApplyTimerActions(primaryText, primaryAction, primaryColor, secondaryText, secondaryAction, secondaryColor, message));
        }

        public void UpdateTimeDisplay(string text)
        {
            ExecuteOnWindowThread(() => UpdateTimeDisplayDelegate(text));
        }

        public void HideTimeDisplay()
        {
            ExecuteOnWindowThread(ApplyHiddenTimeDisplay);
        }

        private void ExecuteOnWindowThread(Action action)
        {
            if (IsHandleCreated && InvokeRequired)
                BeginInvoke(action);
            else
                action();
        }

        public void UpdateColorScheme(Color color)
        {
            Invalidate();
        }

        private void ApplyTimerActions(string primaryText, Action primaryAction, Color primaryColor, string secondaryText, Action secondaryAction, Color secondaryColor, string message)
        {
            messageText = message ?? "";
            primaryActionText = primaryText;
            primaryTimerAction = primaryAction;
            secondaryActionText = secondaryText ?? "";
            secondaryTimerAction = secondaryAction;
            primaryActionButtonTone = GetActionButtonTone(primaryColor);
            secondaryActionButtonTone = GetActionButtonTone(secondaryColor);
            primaryActionButtonColor = ResolveActionButtonColor(primaryActionButtonTone, primaryColor);
            secondaryActionButtonColor = ResolveActionButtonColor(secondaryActionButtonTone, secondaryColor);
            primaryActionButtonHovered = false;
            primaryActionButtonPressed = false;
            secondaryActionButtonHovered = false;
            secondaryActionButtonPressed = false;

            timerButton.Text = primaryText;
            timerButton.AccessibleName = string.IsNullOrEmpty(secondaryActionText)
                ? primaryText
                : primaryText + ", " + secondaryActionText;

            ApplyCompactWindowSize();
            UpdateCompactBounds();
            Invalidate();
        }

        private void ApplyHiddenTimeDisplay()
        {
            timeDisplayVisible = false;
            timeLabel.Text = "";
            ApplyCompactWindowSize();
            UpdateCompactBounds();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshDpiLayout();
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            UpdateWindowRegion();

            if (IsHandleCreated)
                UpdateCompactBounds();
        }

        private void ApplyCompactWindowSize()
        {
            var bottom = Bottom;
            var wasVisible = Visible;
            var compactClientSize = new Size(ScaleLayout(CompactWidth), ScaleLayout(GetCompactHeight()));
            if (ClientSize != compactClientSize)
            {
                ClientSize = compactClientSize;

                if (wasVisible)
                    Top = bottom - Height;
            }

            UpdateWindowRegion();
        }

        private void ApplyCompactFonts()
        {
            titleLabel.Font = ModernTheme.TextFont(9.5f);
            timeLabel.Font = ModernTheme.DisplayMediumFont(36f);
            timerButton.Font = ModernTheme.MediumTextFont(9.5f);
        }

        private void UpdateCompactBounds()
        {
            titleLabel.Bounds = GetTitleBounds();
            timeLabel.Bounds = GetTimeBounds();
            closeButton.Bounds = GetDropDownBounds();
            timerButton.Bounds = GetPrimaryActionButtonBounds();
        }

        public void UpdatePinButtonState(bool pinned)
        {
            this.pinned = pinned;
        }

        public void Show(Point location)
        {
            TopMost = true;
            StopPopupAnimation();
            var wasVisible = Visible;

            if (!wasVisible && ShouldAnimatePopup())
            {
                var offset = ScaleLayout(PopupEnterOffset);
                Location = new Point(location.X, location.Y + offset);
                Opacity = 0.0;
            }
            else
            {
                Location = location;
                Opacity = 1.0;
            }

            if (!wasVisible)
                base.Show();

            BringToFront();
            Activate();

            if (IsHandleCreated)
                SetForegroundWindow(Handle);

            if (!wasVisible && ShouldAnimatePopup())
                StartPopupEnterAnimation(location);
        }

        public void HideAnimated()
        {
            if (!Visible)
                return;

            if (!ShouldAnimatePopup())
            {
                StopPopupAnimation();
                Opacity = 1.0;
                base.Hide();
                return;
            }

            StartPopupExitAnimation();
        }

        private void StartPopupEnterAnimation(Point targetLocation)
        {
            popupAnimation = PopupAnimation.CreateEnter(
                Location,
                targetLocation,
                ScaleLayout(PopupEnterOvershoot),
                PopupEnterDuration);
            StartPopupAnimationTimer();
        }

        private void StartPopupExitAnimation()
        {
            if (popupAnimation != null && popupAnimation.HideWhenComplete)
                return;

            popupAnimation = PopupAnimation.CreateExit(
                Location,
                new Point(Location.X, Location.Y + ScaleLayout(PopupExitOffset)),
                Opacity,
                PopupExitDuration);
            StartPopupAnimationTimer();
        }

        private void StartPopupAnimationTimer()
        {
            if (popupAnimationTimer == null)
            {
                popupAnimationTimer = new System.Windows.Forms.Timer { Interval = 15 };
                popupAnimationTimer.Tick += OnPopupAnimationTick;
            }

            popupAnimationTimer.Stop();
            popupAnimationTimer.Start();
        }

        private void OnPopupAnimationTick(object sender, EventArgs e)
        {
            if (popupAnimation == null)
                return;

            var progress = Clamp01((DateTime.UtcNow - popupAnimation.StartedAt).TotalMilliseconds / popupAnimation.Duration);

            if (popupAnimation.HideWhenComplete)
            {
                var eased = EaseInCubic(progress);
                Location = new Point(
                    popupAnimation.StartLocation.X,
                    Lerp(popupAnimation.StartLocation.Y, popupAnimation.TargetLocation.Y, eased));
                Opacity = Math.Max(0.0, popupAnimation.StartOpacity * (1.0 - eased));
            }
            else
            {
                Location = new Point(popupAnimation.TargetLocation.X, GetEnterAnimationTop(popupAnimation, progress));
                Opacity = EaseOutCubic(Clamp01(progress / 0.55));
            }

            if (progress < 1.0)
                return;

            CompletePopupAnimation();
        }

        private int GetEnterAnimationTop(PopupAnimation animation, double progress)
        {
            var overshootTop = animation.TargetLocation.Y - animation.Overshoot;

            if (progress < 0.72)
            {
                var eased = EaseOutCubic(progress / 0.72);
                return Lerp(animation.StartLocation.Y, overshootTop, eased);
            }

            return Lerp(overshootTop, animation.TargetLocation.Y, EaseOutCubic((progress - 0.72) / 0.28));
        }

        private void CompletePopupAnimation()
        {
            var animation = popupAnimation;
            StopPopupAnimation();

            if (animation.HideWhenComplete)
            {
                Location = animation.StartLocation;
                Opacity = 1.0;
                base.Hide();
            }
            else
            {
                Location = animation.TargetLocation;
                Opacity = 1.0;
            }
        }

        private void StopPopupAnimation()
        {
            if (popupAnimationTimer != null)
                popupAnimationTimer.Stop();

            popupAnimation = null;
        }

        private static bool ShouldAnimatePopup()
        {
            var enabled = true;
            return !SystemParametersInfo(SPI_GETUIEFFECTS, 0, ref enabled, 0) || enabled;
        }

        private static int Lerp(int start, int end, double progress)
        {
            return (int)Math.Round(start + (end - start) * progress);
        }

        private static double EaseOutCubic(double progress)
        {
            progress = Clamp01(progress);
            var inverse = 1.0 - progress;
            return 1.0 - inverse * inverse * inverse;
        }

        private static double EaseInCubic(double progress)
        {
            progress = Clamp01(progress);
            return progress * progress * progress;
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0)
                return 0.0;

            if (value > 1.0)
                return 1.0;

            return value;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopPopupAnimation();

            if (popupAnimationTimer != null)
            {
                popupAnimationTimer.Dispose();
                popupAnimationTimer = null;
            }

            base.OnFormClosed(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                OnUserMouseInteracted();

                if (GetDropDownBounds().Contains(e.Location))
                {
                    closeButtonPressed = true;
                    Invalidate(GetDropDownBounds());
                    return;
                }

                if (GetPrimaryActionButtonBounds().Contains(e.Location))
                {
                    primaryActionButtonPressed = true;
                    Invalidate(GetPrimaryActionButtonBounds());
                    return;
                }

                if (HasSecondaryAction && GetSecondaryActionButtonBounds().Contains(e.Location))
                {
                    secondaryActionButtonPressed = true;
                    Invalidate(GetSecondaryActionButtonBounds());
                    return;
                }

                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
                return;
            }

            base.OnMouseDown(e);
        }

        private void OnUserMouseInteracted()
        {
            UserMouseInteracted?.Invoke(this, EventArgs.Empty);
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

            if (e.Button == MouseButtons.Left && primaryActionButtonPressed)
            {
                var bounds = GetPrimaryActionButtonBounds();
                primaryActionButtonPressed = false;
                Invalidate(bounds);

                if (bounds.Contains(e.Location))
                    primaryTimerAction?.Invoke();

                return;
            }

            if (e.Button == MouseButtons.Left && secondaryActionButtonPressed)
            {
                var bounds = GetSecondaryActionButtonBounds();
                secondaryActionButtonPressed = false;
                Invalidate(bounds);

                if (bounds.Contains(e.Location))
                    secondaryTimerAction?.Invoke();

                return;
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var primaryBounds = GetPrimaryActionButtonBounds();
            var primaryHovered = primaryBounds.Contains(e.Location);
            var secondaryBounds = GetSecondaryActionButtonBounds();
            var secondaryHovered = HasSecondaryAction && secondaryBounds.Contains(e.Location);

            if (primaryHovered == primaryActionButtonHovered && secondaryHovered == secondaryActionButtonHovered)
                return;

            primaryActionButtonHovered = primaryHovered;
            secondaryActionButtonHovered = secondaryHovered;
            Invalidate(primaryBounds);
            if (HasSecondaryAction)
                Invalidate(secondaryBounds);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            var primaryBounds = GetPrimaryActionButtonBounds();
            var secondaryBounds = GetSecondaryActionButtonBounds();
            primaryActionButtonHovered = false;
            primaryActionButtonPressed = false;
            secondaryActionButtonHovered = false;
            secondaryActionButtonPressed = false;
            Invalidate(primaryBounds);
            if (HasSecondaryAction)
                Invalidate(secondaryBounds);

            base.OnMouseLeave(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

            if (Windows11Backdrop.IsSupported)
                return;

            e.Graphics.Clear(ModernTheme.TimerWindowBackColor);
            var radius = ScaleLayout(WindowCornerRadius);
            var borderBounds = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);

            using (var path = CreateRoundedRectangle(borderBounds, Math.Max(1f, radius - 0.5f)))
            using (var pen = new Pen(ModernTheme.BorderColor))
            {
                e.Graphics.DrawPath(pen, path);
            }

            var highlightBounds = RectangleF.Inflate(borderBounds, -1f, -1f);
            using (var pen = new Pen(ModernTheme.InnerHighlightColor))
            using (var path = CreateRoundedRectangle(highlightBounds, Math.Max(1f, radius - 1.5f)))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (timeDisplayVisible)
            {
                DrawAlignedText(e.Graphics, titleText, titleLabel.Font, GetTitleBounds(), StringAlignment.Center, StringAlignment.Center);
                DrawAlignedText(e.Graphics, timeText, timeLabel.Font, GetTimeBounds(), StringAlignment.Center, StringAlignment.Center);
            }
            else if (HasMessage)
            {
                DrawAlignedText(e.Graphics, titleText, titleLabel.Font, GetTitleBounds(), StringAlignment.Center, StringAlignment.Center);

                using (var messageFont = ModernTheme.TextFont(18f))
                {
                    DrawAlignedText(e.Graphics, messageText, messageFont, GetMessageBounds(), StringAlignment.Center, StringAlignment.Center, true);
                }
            }
            else
            {
                DrawTitleOnlyText(e.Graphics);
            }

            DrawDropDownArrow(e.Graphics, GetDropDownBounds());
            DrawActionButtons(e.Graphics);
        }

        private void DrawAlignedText(Graphics graphics, string text, Font font, Rectangle bounds, StringAlignment alignment, StringAlignment lineAlignment)
        {
            DrawAlignedText(graphics, text, font, bounds, alignment, lineAlignment, true);
        }

        private void DrawAlignedText(Graphics graphics, string text, Font font, Rectangle bounds, StringAlignment alignment, StringAlignment lineAlignment, bool noWrap)
        {
            using (var brush = new SolidBrush(ModernTheme.TimerTextColor))
            using (var format = noWrap ? new StringFormat(StringFormatFlags.NoWrap) : new StringFormat())
            {
                format.Alignment = alignment;
                format.LineAlignment = lineAlignment;
                format.Trimming = StringTrimming.EllipsisCharacter;
                graphics.DrawString(text, font, brush, bounds, format);
            }
        }

        private void DrawTitleOnlyText(Graphics graphics)
        {
            var bounds = GetTitleOnlyTitleBounds();

            using (var font = ModernTheme.TextFont(TitleOnlyFontSize))
            {
                DrawAlignedText(graphics, titleText, font, bounds, StringAlignment.Center, StringAlignment.Center, false);
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

        private Rectangle GetTitleOnlyTitleBounds()
        {
            return GetTitleOnlyTitleBounds(false);
        }

        private Rectangle GetTitleOnlyTitleBounds(bool reserveDropDown)
        {
            var padding = ScaleLayout(CompactPadding);
            return new Rectangle(padding, padding, ClientSize.Width - padding * 2, ScaleLayout(TitleOnlyContentHeight));
        }

        private Rectangle GetMessageTitleBounds()
        {
            var padding = ScaleLayout(CompactPadding);
            var actionTop = GetFirstActionButtonTop();
            return new Rectangle(padding, padding, ClientSize.Width - padding * 2, Math.Max(ScaleLayout(TitleHeight), actionTop - padding * 2));
        }

        private Rectangle GetMessageBounds()
        {
            var padding = ScaleLayout(CompactPadding);
            var titleBottom = padding + ScaleLayout(TitleHeight);
            var actionTop = GetFirstActionButtonTop();
            return new Rectangle(padding, titleBottom, ClientSize.Width - padding * 2, Math.Max(ScaleLayout(TitleHeight), actionTop - titleBottom - padding));
        }

        private Rectangle GetTimeBounds()
        {
            var padding = ScaleLayout(CompactPadding);
            return new Rectangle(padding, ScaleLayout(TimeTop), ClientSize.Width - padding * 2, ScaleLayout(TimeHeight));
        }

        private Rectangle GetPrimaryActionButtonBounds()
        {
            var padding = ScaleLayout(CompactPadding);
            var fullWidth = Math.Min(ScaleLayout(ActionButtonWidth), ClientSize.Width - padding * 2);
            var width = HasSecondaryAction ? (fullWidth - ScaleLayout(ActionButtonGap)) / 2 : fullWidth;
            var height = ScaleLayout(ActionButtonHeight);
            var groupLeft = (ClientSize.Width - fullWidth) / 2;
            return new Rectangle(groupLeft, GetFirstActionButtonTop(), width, height);
        }

        private Rectangle GetSecondaryActionButtonBounds()
        {
            var padding = ScaleLayout(CompactPadding);
            var fullWidth = Math.Min(ScaleLayout(ActionButtonWidth), ClientSize.Width - padding * 2);
            var width = (fullWidth - ScaleLayout(ActionButtonGap)) / 2;
            var height = ScaleLayout(ActionButtonHeight);
            var primaryBounds = GetPrimaryActionButtonBounds();
            return new Rectangle(primaryBounds.Right + ScaleLayout(ActionButtonGap), primaryBounds.Top, width, height);
        }

        private int GetFirstActionButtonTop()
        {
            var padding = ScaleLayout(CompactPadding);
            var height = ScaleLayout(ActionButtonHeight);

            return ClientSize.Height - padding - height;
        }

        private int GetCompactHeight()
        {
            if (timeDisplayVisible)
                return CompactHeight;

            return HasMessage ? CompactMessageHeight : CompactTitleOnlyHeight;
        }

        private int ScaleLayout(int value)
        {
            return (int)Math.Round(value * layoutScale);
        }

        private bool HasSecondaryAction => !string.IsNullOrWhiteSpace(secondaryActionText) && secondaryTimerAction != null;
        private bool HasMessage => !string.IsNullOrWhiteSpace(messageText);

        private void UpdateWindowRegion()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            using (var path = CreateRoundedRectangle(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), ScaleLayout(WindowCornerRadius)))
            {
                var oldRegion = Region;
                Region = new Region(path);
                oldRegion?.Dispose();
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

        private void DrawActionButtons(Graphics graphics)
        {
            DrawActionButton(
                graphics,
                GetPrimaryActionButtonBounds(),
                primaryActionText,
                primaryActionButtonColor,
                primaryActionButtonHovered,
                primaryActionButtonPressed);

            if (HasSecondaryAction)
            {
                DrawActionButton(
                    graphics,
                    GetSecondaryActionButtonBounds(),
                    secondaryActionText,
                    secondaryActionButtonColor,
                    secondaryActionButtonHovered,
                    secondaryActionButtonPressed);
            }
        }

        private void DrawActionButton(Graphics graphics, Rectangle bounds, string text, Color color, bool hovered, bool pressed)
        {
            var state = pressed
                ? PushButtonState.Pressed
                : hovered
                    ? PushButtonState.Hot
                    : PushButtonState.Normal;

            DrawActionButtonSurface(graphics, bounds, state, color);

            using (var brush = new SolidBrush(ModernTheme.ActionButtonTextColor))
            using (var format = new StringFormat(StringFormatFlags.NoWrap))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                graphics.DrawString(text, timerButton.Font, brush, bounds, format);
            }
        }

        private static void DrawActionButtonSurface(Graphics graphics, Rectangle bounds, PushButtonState state, Color normalColor)
        {
            var fill = ResolveActionButtonFillColor(state, normalColor);

            var scale = graphics.DpiX / 96f;
            var radius = Math.Max(1f, ActionButtonCornerRadius * scale);
            var pathBounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var borderBounds = new RectangleF(
                bounds.X + 0.5f * scale,
                bounds.Y + 0.5f * scale,
                bounds.Width - scale,
                bounds.Height - scale);

            using (var path = CreateRoundedRectangle(pathBounds, radius))
            using (var borderPath = CreateRoundedRectangle(borderBounds, Math.Max(1f, radius - scale / 2f)))
            using (var brush = new SolidBrush(fill))
            using (var border = new Pen(ModernTheme.ActionButtonBorderColor, Math.Max(1f, scale)))
            {
                border.LineJoin = LineJoin.Round;
                graphics.FillPath(brush, path);
                graphics.DrawPath(border, borderPath);
            }
        }

        private static Color ResolveActionButtonFillColor(PushButtonState state, Color normalColor)
        {
            if (state == PushButtonState.Pressed)
                return AdjustColor(normalColor, ActionButtonPressedColorOffset);

            if (state == PushButtonState.Hot)
                return AdjustColor(normalColor, ActionButtonHoverColorOffset);

            return normalColor;
        }

        private static Color AdjustColor(Color color, int offset)
        {
            return Color.FromArgb(
                color.A,
                ClampColor(color.R + offset),
                ClampColor(color.G + offset),
                ClampColor(color.B + offset));
        }

        private static int ClampColor(int value)
        {
            return Math.Max(0, Math.Min(255, value));
        }

        private static ActionButtonTone GetActionButtonTone(Color color)
        {
            if (color.ToArgb() == ModernTheme.ActionButtonBackColor.ToArgb())
                return ActionButtonTone.Action;

            if (color.ToArgb() == ModernTheme.BreakActionButtonBackColor.ToArgb())
                return ActionButtonTone.Break;

            if (color.ToArgb() == ModernTheme.NeutralActionButtonBackColor.ToArgb())
                return ActionButtonTone.Neutral;

            return ActionButtonTone.Custom;
        }

        private static Color ResolveActionButtonColor(ActionButtonTone tone, Color customColor)
        {
            switch (tone)
            {
                case ActionButtonTone.Action:
                    return ModernTheme.ActionButtonBackColor;
                case ActionButtonTone.Break:
                    return ModernTheme.BreakActionButtonBackColor;
                case ActionButtonTone.Neutral:
                    return ModernTheme.NeutralActionButtonBackColor;
                default:
                    return customColor;
            }
        }

        private void RefreshThemedActionButtonColors()
        {
            primaryActionButtonColor = ResolveActionButtonColor(primaryActionButtonTone, primaryActionButtonColor);
            secondaryActionButtonColor = ResolveActionButtonColor(secondaryActionButtonTone, secondaryActionButtonColor);
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

        private void RefreshDpiLayout()
        {
            ApplyDpiLayoutScale(GetCurrentDpiScale());
        }

        private void ApplyDpiLayoutScale(float dpiScale)
        {
            layoutScale = Math.Max(1f, dpiScale);
            ApplyCompactFonts();
            ApplyCompactWindowSize();
            UpdateWindowRegion();
            UpdateCompactBounds();
            closeButton.Invalidate();
            timerButton.Invalidate();
            Invalidate();
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

        private class PopupAnimation
        {
            private PopupAnimation()
            {
            }

            public Point StartLocation { get; private set; }
            public Point TargetLocation { get; private set; }
            public DateTime StartedAt { get; private set; }
            public double StartOpacity { get; private set; }
            public double Duration { get; private set; }
            public int Overshoot { get; private set; }
            public bool HideWhenComplete { get; private set; }

            public static PopupAnimation CreateEnter(Point startLocation, Point targetLocation, int overshoot, int duration)
            {
                return new PopupAnimation
                {
                    StartLocation = startLocation,
                    TargetLocation = targetLocation,
                    StartedAt = DateTime.UtcNow,
                    StartOpacity = 0.0,
                    Duration = duration,
                    Overshoot = overshoot,
                    HideWhenComplete = false
                };
            }

            public static PopupAnimation CreateExit(Point startLocation, Point targetLocation, double startOpacity, int duration)
            {
                return new PopupAnimation
                {
                    StartLocation = startLocation,
                    TargetLocation = targetLocation,
                    StartedAt = DateTime.UtcNow,
                    StartOpacity = startOpacity,
                    Duration = duration,
                    Overshoot = 0,
                    HideWhenComplete = true
                };
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
            primaryTimerAction?.Invoke();
        }

        private void ApplyTheme()
        {
            Opacity = 1.0;
            ForeColor = ModernTheme.TimerTextColor;
            BackColor = ModernTheme.TimerWindowBackColor;
            titleLabel.ForeColor = ModernTheme.TimerTextColor;
            timeLabel.ForeColor = ModernTheme.TimerTextColor;
            closeButton.ForeColor = ModernTheme.TimerTextColor;
            timerButton.ForeColor = ModernTheme.ActionButtonTextColor;
            ApplyCompactFonts();
            RefreshThemedActionButtonColors();
            ApplyCompactWindowSize();
            UpdateCompactBounds();
            closeButton.Invalidate();
            timerButton.Invalidate();
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED)
            {
                base.WndProc(ref m);
                ApplyDpiLayoutScale(GetDpiScaleFromWParam(m.WParam));
                return;
            }

            base.WndProc(ref m);
        }

        private static float GetDpiScaleFromWParam(IntPtr wParam)
        {
            return Math.Max(1f, ((int)wParam & 0xFFFF) / 96f);
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

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                UpdateButtonRegion();
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                UpdateButtonRegion();
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

            private void UpdateButtonRegion()
            {
                if (Width <= 0 || Height <= 0)
                    return;

                using (var graphics = CreateGraphics())
                using (var path = CreateRoundedRectangle(new RectangleF(0, 0, Width, Height), GetCornerRadius(graphics)))
                {
                    var oldRegion = Region;
                    Region = new Region(path);
                    oldRegion?.Dispose();
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
                var fill = ResolveActionButtonFillColor(state, ModernTheme.ActionButtonBackColor);

                var scale = graphics.DpiX / 96f;
                var radius = GetCornerRadius(graphics);
                var pathBounds = new RectangleF(0, 0, bounds.Width, bounds.Height);
                var borderBounds = new RectangleF(0.5f * scale, 0.5f * scale, bounds.Width - scale, bounds.Height - scale);

                using (var path = CreateRoundedRectangle(pathBounds, radius))
                using (var borderPath = CreateRoundedRectangle(borderBounds, Math.Max(1f, radius - scale / 2f)))
                using (var brush = new SolidBrush(fill))
                using (var border = new Pen(ModernTheme.ActionButtonBorderColor, Math.Max(1f, scale)))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(border, borderPath);
                }
            }

            private static float GetCornerRadius(Graphics graphics)
            {
                return Math.Max(1f, ActionButtonCornerRadius * graphics.DpiX / 96f);
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

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);
    }
}
