using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SteamInputBridge.Microphone;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace SteamInputBridge.App.Tray;

internal sealed class StatusOverlayWindow : Window
{
    private const double DotSize = 9;
    private const double DotGap = 7;
    private const double GlowPadding = 18;
    private const double GlowBlurRadius = 14;
    private const double GlowOpacity = 0.9;
    private const double GlowShadowDepth = 0;

    private const double EdgeMargin = DotSize;
    private const double WindowWidth = (DotSize * 2) + DotGap + EdgeMargin + GlowPadding;
    private const double WindowHeight = DotSize + EdgeMargin + GlowPadding;

    private const string MutedColor = "#FF9F0A";
    private const string ActiveColor = "#32D74B";

    private readonly Ellipse _actionDot;
    private readonly Ellipse _microphoneDot;

    // MARK: Lifecycle
    // ========================================================================

    public StatusOverlayWindow()
    {
        Width = WindowWidth;
        Height = WindowHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        IsHitTestVisible = false;

        Canvas canvas = new()
        {
            Width = WindowWidth,
            Height = WindowHeight,
            IsHitTestVisible = false,
        };

        _actionDot = CreateDot();
        _microphoneDot = CreateDot();
        Canvas.SetTop(_actionDot, EdgeMargin);
        Canvas.SetTop(_microphoneDot, EdgeMargin);

        _ = canvas.Children.Add(_actionDot);
        _ = canvas.Children.Add(_microphoneDot);

        Content = canvas;
        SourceInitialized += OnSourceInitialized;
        Hide();
    }

    // MARK: Updates
    // ========================================================================

    public void SetMicrophoneStatus(MicrophoneStatus status)
    {
        SetDot(_microphoneDot, MicrophoneColor(status));
        Refresh();
    }

    public void SetActionColor(string? color)
    {
        SetDot(_actionDot, color);
        Refresh();
    }

    // MARK: Implementation
    // ========================================================================

    private void Refresh()
    {
        LayoutDots();
        bool visible = _actionDot.Visibility == Visibility.Visible || _microphoneDot.Visibility == Visibility.Visible;
        if (!visible)
        {
            Hide();
            return;
        }

        Left = SystemParameters.PrimaryScreenWidth - Width;
        Top = 0;
        if (!IsVisible)
        {
            Show();
        }
    }

    private void LayoutDots()
    {
        double rightDotLeft = WindowWidth - EdgeMargin - DotSize;
        if (_actionDot.Visibility == Visibility.Visible && _microphoneDot.Visibility == Visibility.Visible)
        {
            Canvas.SetLeft(_actionDot, rightDotLeft - DotGap - DotSize);
            Canvas.SetLeft(_microphoneDot, rightDotLeft);
            return;
        }

        Canvas.SetLeft(_actionDot, rightDotLeft);
        Canvas.SetLeft(_microphoneDot, rightDotLeft);
    }

    private static Ellipse CreateDot()
    {
        return new Ellipse
        {
            Width = DotSize,
            Height = DotSize,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
    }

    private static string? MicrophoneColor(MicrophoneStatus status)
    {
        return !status.Available
            ? null
            : status.Muted
            ? MutedColor
            : status.IsActive
            ? ActiveColor
            : null;
    }

    private static void SetDot(Ellipse dot, string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText) || ColorConverter.ConvertFromString(colorText) is not Color color)
        {
            dot.Visibility = Visibility.Collapsed;
            dot.Fill = null;
            dot.Effect = null;
            return;
        }

        dot.Visibility = Visibility.Visible;
        dot.Fill = new SolidColorBrush(color);
        dot.Effect = new DropShadowEffect
        {
            BlurRadius = GlowBlurRadius,
            ShadowDepth = GlowShadowDepth,
            Opacity = GlowOpacity,
            Color = color,
        };
    }

    // MARK: Window Styles
    // ========================================================================

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        HWND handle = new(new WindowInteropHelper(this).Handle);
        WindowStylesEx style = (WindowStylesEx)GetWindowLong(handle, WindowLongFlags.GWL_EXSTYLE);
        _ = SetWindowLong(
            handle,
            WindowLongFlags.GWL_EXSTYLE,
            (int)(style |
                WindowStylesEx.WS_EX_TOOLWINDOW |
                WindowStylesEx.WS_EX_TRANSPARENT |
                WindowStylesEx.WS_EX_NOACTIVATE));
    }
}
