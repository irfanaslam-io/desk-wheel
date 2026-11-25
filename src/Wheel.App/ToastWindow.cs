using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Wheel.App;

/// <summary>
/// A small "Done" card that fades in at the screen corner (top-right on Mac, bottom-right on Windows)
/// and closes itself after 5 seconds. Click it to reveal the output file.
/// </summary>
internal sealed class ToastWindow : Window
{
    private const double CardWidth = 360, Gap = 16;

    public ToastWindow(string title, string text, bool error, Action? onClick)
    {
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = CardWidth;
        SizeToContent = SizeToContent.Height;
        Title = "Desk Wheel";

        // Error → red warning; finished work → green tick; anything else → neutral info.
        var success = !error && (title.StartsWith("Saved") || title.StartsWith("Copied") || title.StartsWith("Done") || title.StartsWith("Renamed")
                                 || title.StartsWith("Opened") || title.StartsWith("Recording saved") || title.StartsWith("Link copied") || title.StartsWith("QR code"));
        var (icon, tone) = error ? (IconKind.Warning, Ui.DangerColor) : success ? (IconKind.Check, Ui.SuccessColor) : (IconKind.Info, Ui.InfoColor);
        var badge = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0x2E, tone.R, tone.G, tone.B)),
            Child = new ContentControl { Content = Ui.Icon(icon, 16, new SolidColorBrush(tone)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var close = Ui.IconButton(IconKind.Close, "Dismiss", Close, 12);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Padding = new Thickness(4);

        var body = new StackPanel
        {
            Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0),
            Children =
            {
                new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Ui.Text, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = text, FontSize = 12, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3 },
            },
        };
        var row = new DockPanel();
        DockPanel.SetDock(badge, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(badge);
        row.Children.Add(close);
        row.Children.Add(body);

        Content = new Border
        {
            CornerRadius = Ui.CardRadius,
            Background = Ui.Floating,
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 12, 8, 12),
            Cursor = onClick is null ? null : new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = row,
        };

        PointerPressed += (_, _) =>
        {
            onClick?.Invoke();
            Close();
        };
        DispatcherTimer.RunOnce(() => { if (IsVisible) Close(); }, TimeSpan.FromSeconds(error ? 8 : 5));
    }

    public void ShowInCorner()
    {
        Show(); // the height is known after the first layout
        var screen = Screens.Primary;
        if (screen is null) return;
        var s = screen.Scaling;
        var area = screen.WorkingArea;
        int w = (int)(CardWidth * s), h = (int)(Math.Max(60, Bounds.Height) * s), m = (int)(Gap * s);
        var top = OperatingSystem.IsMacOS(); // macOS notifications live top-right, Windows bottom-right
        Position = new PixelPoint(area.Right - w - m, top ? area.Y + m : area.Bottom - h - m);
    }
}
