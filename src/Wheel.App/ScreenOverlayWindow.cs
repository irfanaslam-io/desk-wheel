using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Wheel.Core.Screen;

namespace Wheel.App;

/// <summary>
/// A still picture of one screen, shown full-size over that screen, for picking a colour (with a magnifier)
/// or dragging out an area (to record). Working on a still picture means nothing moves while you aim,
/// and it needs no extra permission beyond taking a screenshot.
/// </summary>
internal sealed class ScreenOverlayWindow : Window
{
    public enum Purpose { PickColor, SelectArea }

    private const int LoupeCells = 11, LoupeCell = 12; // 11×11 pixels, each shown 12×12

    private readonly Purpose _purpose;
    private readonly SKBitmap _pixels;
    private readonly Bitmap _picture;
    private readonly PixelRect _screenBounds; // the screen, in desktop pixels
    private readonly Canvas _canvas = new();
    private readonly TaskCompletionSource<object?> _result = new();
    private readonly Border _loupe;
    private readonly Image _loupeImage = new() { Width = LoupeCells * LoupeCell, Height = LoupeCells * LoupeCell, Stretch = Stretch.Fill };
    private readonly TextBlock _loupeText = new() { FontWeight = FontWeight.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _loupeSwatch = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
    private readonly Rectangle _selection = new() { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = [5, 3], IsVisible = false };
    private readonly Border _sizeLabel = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xD0, 0, 0, 0)), CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 2), IsVisible = false,
    };
    private readonly Avalonia.Controls.Shapes.Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)) };
    private Point? _dragFrom;

    private ScreenOverlayWindow(Purpose purpose, SKBitmap pixels, Bitmap picture, PixelRect screenBounds, double scaling)
    {
        _purpose = purpose;
        _pixels = pixels;
        _picture = picture;
        _screenBounds = screenBounds;

        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = Brushes.Black;
        Position = screenBounds.Position;
        Width = screenBounds.Width / scaling;
        Height = screenBounds.Height / scaling;
        Cursor = new Cursor(StandardCursorType.Cross);

        var image = new Image { Source = picture, Stretch = Stretch.Fill, Width = Width, Height = Height };
        _canvas.Children.Add(image);
        RenderOptions.SetBitmapInterpolationMode(_loupeImage, BitmapInterpolationMode.None);

        if (purpose == Purpose.SelectArea)
        {
            _dim.Data = new RectangleGeometry(new Rect(0, 0, Width, Height));
            _canvas.Children.Add(_dim);
            _canvas.Children.Add(_selection);
            _sizeLabel.Child = new TextBlock { Foreground = Brushes.White, FontSize = 12 };
            _canvas.Children.Add(_sizeLabel);
            _canvas.Children.Add(Hint("Drag over the area to record · Esc to cancel"));
        }
        else
        {
            _canvas.Children.Add(Hint("Click to copy a colour · Esc to cancel"));
        }

        _loupe = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1D, 0x21)), CornerRadius = new CornerRadius(10),
            BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), Padding = new Thickness(6),
            IsVisible = purpose == Purpose.PickColor, IsHitTestVisible = false,
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new Grid
                    {
                        Children =
                        {
                            _loupeImage,
                            new Rectangle // the pixel that will be picked
                            {
                                Width = LoupeCell + 2, Height = LoupeCell + 2, Stroke = Brushes.White, StrokeThickness = 2,
                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                            },
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { _loupeSwatch, _loupeText },
                    },
                },
            },
        };
        _canvas.Children.Add(_loupe);
        Content = _canvas;

        PointerMoved += OnMoved;
        PointerPressed += OnPressed;
        PointerReleased += OnReleased;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };
        Deactivated += (_, _) => Finish(null);
        Closed += (_, _) =>
        {
            _result.TrySetResult(null);
            _picture.Dispose();
            _pixels.Dispose();
        };
    }

    private Border Hint(string text)
    {
        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x1C, 0x1D, 0x21)), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6), IsHitTestVisible = false,
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 13 },
        };
        Canvas.SetLeft(hint, Width / 2 - 170);
        Canvas.SetTop(hint, 40);
        return hint;
    }

    /// <summary>Picks a colour on the screen under the pointer. Null if cancelled.</summary>
    public static async Task<SKColor?> PickColorAsync(string screenshot, PixelRect screenBounds, double scaling) =>
        await ShowAsync(Purpose.PickColor, screenshot, screenBounds, scaling) as SKColor?;

    /// <summary>An area of the screen under the pointer, in that screen's picture pixels. Null if cancelled.</summary>
    public static async Task<ScreenRect?> SelectAreaAsync(string screenshot, PixelRect screenBounds, double scaling) =>
        await ShowAsync(Purpose.SelectArea, screenshot, screenBounds, scaling) as ScreenRect?;

    private static async Task<object?> ShowAsync(Purpose purpose, string screenshot, PixelRect screenBounds, double scaling)
    {
        var pixels = SKBitmap.Decode(screenshot) ?? throw new IOException("Couldn't read the screen picture.");
        Bitmap picture;
        using (var stream = File.OpenRead(screenshot))
            picture = new Bitmap(stream);
        var window = new ScreenOverlayWindow(purpose, pixels, picture, screenBounds, scaling);
        window.Show();
        window.Activate();
        return await window._result.Task;
    }

    private void Finish(object? result)
    {
        if (_result.Task.IsCompleted) return;
        _result.TrySetResult(result);
        Close();
    }

    /// <summary>View point → pixel in the screen picture.</summary>
    private (int X, int Y) ToPixel(Point p) =>
        (Math.Clamp((int)(p.X / Width * _pixels.Width), 0, _pixels.Width - 1),
         Math.Clamp((int)(p.Y / Height * _pixels.Height), 0, _pixels.Height - 1));

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        if (_purpose == Purpose.PickColor)
        {
            UpdateLoupe(p);
            return;
        }
        if (_dragFrom is { } from)
            ShowSelection(new Rect(from, p).Normalize());
    }

    private void UpdateLoupe(Point p)
    {
        var (x, y) = ToPixel(p);
        var half = LoupeCells / 2;
        var crop = new PixelRect(
            Math.Clamp(x - half, 0, Math.Max(0, _pixels.Width - LoupeCells)),
            Math.Clamp(y - half, 0, Math.Max(0, _pixels.Height - LoupeCells)),
            Math.Min(LoupeCells, _pixels.Width), Math.Min(LoupeCells, _pixels.Height));
        _loupeImage.Source = new CroppedBitmap(_picture, crop);

        var c = _pixels.GetPixel(x, y);
        _loupeSwatch.Background = new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue));
        _loupeText.Text = ScreenColor.Hex(c.Red, c.Green, c.Blue);
        _loupeText.Foreground = Brushes.White;

        // Beside the pointer, flipped near the edges so it stays on screen.
        const double size = LoupeCells * LoupeCell + 12;
        var left = p.X + 24 + size > Width ? p.X - 24 - size : p.X + 24;
        var top = p.Y + 24 + size + 30 > Height ? p.Y - 24 - size - 30 : p.Y + 24;
        Canvas.SetLeft(_loupe, left);
        Canvas.SetTop(_loupe, top);
    }

    private void ShowSelection(Rect r)
    {
        _selection.IsVisible = _sizeLabel.IsVisible = true;
        _selection.Width = r.Width;
        _selection.Height = r.Height;
        Canvas.SetLeft(_selection, r.X);
        Canvas.SetTop(_selection, r.Y);
        // Darken everything except the selection.
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(r));
        var (x1, y1) = ToPixel(r.TopLeft);
        var (x2, y2) = ToPixel(r.BottomRight);
        ((TextBlock)_sizeLabel.Child!).Text = $"{x2 - x1} × {y2 - y1}";
        Canvas.SetLeft(_sizeLabel, r.X);
        Canvas.SetTop(_sizeLabel, r.Y > 28 ? r.Y - 26 : r.Bottom + 6);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Finish(null);
            return;
        }
        var p = e.GetPosition(_canvas);
        if (_purpose == Purpose.PickColor)
        {
            var (x, y) = ToPixel(p);
            Finish(_pixels.GetPixel(x, y));
            return;
        }
        _dragFrom = p;
        e.Pointer.Capture(this);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_purpose != Purpose.SelectArea || _dragFrom is not { } from) return;
        _dragFrom = null;
        e.Pointer.Capture(null);
        var r = new Rect(from, e.GetPosition(_canvas)).Normalize();
        var (x1, y1) = ToPixel(r.TopLeft);
        var (x2, y2) = ToPixel(r.BottomRight);
        if (x2 - x1 < 16 || y2 - y1 < 16)
        {
            _selection.IsVisible = _sizeLabel.IsVisible = false; // too small: probably a click; try again
            _dim.Data = new RectangleGeometry(new Rect(0, 0, Width, Height));
            return;
        }
        Finish(new ScreenRect(x1, y1, x2 - x1, y2 - y1));
    }
}
