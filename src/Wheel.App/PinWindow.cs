using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Wheel.App;

/// <summary>
/// A screenshot pinned above all windows: handy for copying values or comparing while you work.
/// Drag to move; the ✕ (shown on hover), double-click or Esc closes it.
/// </summary>
internal sealed class PinWindow : Window
{
    private Point? _pressAt;
    private bool _moving;

    public PinWindow(string path)
    {
        Usage.Screen("pin");
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Pinned screenshot";
        SizeToContent = SizeToContent.WidthAndHeight;

        var bitmap = new Bitmap(path);
        // Show at half size on Retina-style screens (screenshots are taken at full pixel density), capped to a sensible size.
        var width = Math.Min(bitmap.Size.Width / 2, 900);
        var image = new Image { Source = bitmap, Width = Math.Max(120, width), Stretch = Stretch.Uniform };

        var close = new Button
        {
            Content = "✕", Padding = new Thickness(8, 2), FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6), IsVisible = false,
        };
        close.Click += (_, _) => Close();

        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x62, 0x2C)),
            BorderThickness = new Thickness(1),
            Child = new Grid { Children = { image, close } },
        };

        PointerEntered += (_, _) => close.IsVisible = true;
        PointerExited += (_, _) => close.IsVisible = false;
        DoubleTapped += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _pressAt = e.GetPosition(this);
                _moving = false;
            }
        };
        PointerMoved += (_, e) =>
        {
            if (_pressAt is not { } start) return;
            var now = e.GetPosition(this);
            double dx = now.X - start.X, dy = now.Y - start.Y;
            if (!_moving && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
            _moving = true;
            var scale = (Screens.ScreenFromPoint(Position) ?? Screens.Primary)?.Scaling ?? 1;
            Position = new PixelPoint(Position.X + (int)(dx * scale), Position.Y + (int)(dy * scale));
        };
        PointerReleased += (_, _) => _pressAt = null;
    }

    /// <summary>Opens near the centre of the primary screen.</summary>
    public void ShowNearCentre()
    {
        Show();
        var screen = Screens.Primary;
        if (screen is null) return;
        var s = screen.Scaling;
        var area = screen.WorkingArea;
        Position = new PixelPoint(
            area.X + (area.Width - (int)(Bounds.Width * s)) / 2,
            area.Y + (area.Height - (int)(Bounds.Height * s)) / 3);
    }
}
