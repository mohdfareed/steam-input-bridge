using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SteamInputBridge.Hosting.Server.Orchestration;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace SteamInputBridge.App.Tray.Core;

internal sealed class StatusOverlayWindow : Window
{
    private const double DotSize = 9;
    private const double DotGap = 7;
    private const double GlowPadding = 18;
    private const double EdgeMargin = DotSize;
    private const double WindowWidth = (DotSize * 2) + DotGap + EdgeMargin + GlowPadding;
    private const double WindowHeight = DotSize + EdgeMargin + GlowPadding;

    private readonly Ellipse _actionDot;
    private readonly Ellipse _micDot;

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
        _micDot = CreateDot();
        Canvas.SetTop(_actionDot, EdgeMargin);
        Canvas.SetTop(_micDot, EdgeMargin);
        _ = canvas.Children.Add(_actionDot);
        _ = canvas.Children.Add(_micDot);
        Content = canvas;

        SourceInitialized += OnSourceInitialized;
        Hide();
    }

    public void Update(OverlayStatus status)
    {
        SetDot(_actionDot, status.ActionColor);
        SetDot(_micDot, GetMicrophoneColor(status.Microphone));
        bool visible = _actionDot.Visibility == Visibility.Visible ||
            _micDot.Visibility == Visibility.Visible;
        LayoutDots();

        if (!visible)
        {
            Hide();
            return;
        }

        PositionAtPrimaryTopRight();
        Topmost = true;
        if (!IsVisible)
        {
            Show();
        }
    }

    private void LayoutDots()
    {
        double rightDotLeft = WindowWidth - EdgeMargin - DotSize;
        if (_actionDot.Visibility == Visibility.Visible && _micDot.Visibility == Visibility.Visible)
        {
            Canvas.SetLeft(_actionDot, rightDotLeft - DotGap - DotSize);
            Canvas.SetLeft(_micDot, rightDotLeft);
            return;
        }

        Canvas.SetLeft(_actionDot, rightDotLeft);
        Canvas.SetLeft(_micDot, rightDotLeft);
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

    private static string? GetMicrophoneColor(MicrophoneOverlayStatus status)
    {
        return !status.Available
            ? null
            : status.Muted
            ? "#FF9F0A"
            : status is { ActivityReliable: true, InputActive: true }
            ? "#32D74B"
            : null;
    }

    private static void SetDot(Ellipse dot, string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText) ||
            ColorConverter.ConvertFromString(colorText) is not Color color)
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
            BlurRadius = 14,
            ShadowDepth = 0,
            Opacity = 0.9,
            Color = color,
        };
    }

    private void PositionAtPrimaryTopRight()
    {
        // Keep the window onscreen and put the dot edge one dot diameter from
        // the top-right corner. Extra transparent space lives left/below the
        // cluster so the glow is not clipped by an artificial overlay box.
        Left = SystemParameters.PrimaryScreenWidth - Width;
        Top = 0;
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
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
