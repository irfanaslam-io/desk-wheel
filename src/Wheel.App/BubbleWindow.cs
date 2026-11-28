using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Wheel.App;

/// <summary>
/// The floating Wheel button: always on top at the screen edge.
/// Drag a file onto it → the wheel opens around it. Click it → wheel for the selected files.
/// Drag the button itself to move it; the position is remembered.
/// </summary>
internal sealed class BubbleWindow : Window
{
    public const double Size = 56;
    private const double MoveThreshold = 4;

    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0xE8, 0x62, 0x2C));
    private readonly Ellipse _ring;
    private readonly Ellipse _badge = new()
    {
        Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43)), Stroke = Brushes.White, StrokeThickness = 2,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0),
        IsHitTestVisible = false, IsVisible = false,
    };
    private const string Tip = "Click for screenshots · drop a file to convert it";
    private Point? _pressAt;

    /// <summary>A drag carrying files reached the button. Argument: the button's centre in screen pixels.</summary>
    public event Action<PixelPoint>? FilesDraggedIn;

    /// <summary>The button was clicked (not moved).</summary>
    public event Action<PixelPoint>? Clicked;

    /// <summary>Right-click: take a screenshot of an area.</summary>
    public event Action? RightClicked;

    /// <summary>The button was moved; save the new position.</summary>
    public event Action<PixelPoint>? Moved;

    public BubbleWindow()
    {
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = Size;
        Height = Size;
        Title = "Desk Wheel";
        ToolTip.SetTip(this, Tip);

        _ring = new Ellipse
        {
            Width = Size - 6, Height = Size - 6,
            Fill = Ui.Bubble,
            Stroke = Accent, StrokeThickness = 3,
        };
        var dot = new Ellipse { Width = 14, Height = 14, Fill = Accent, IsHitTestVisible = false };
        var inner = new Ellipse
        {
            Width = 30, Height = 30, Stroke = Accent, StrokeThickness = 5, Opacity = 0.85, IsHitTestVisible = false,
        };
        Content = new Grid
        {
            Children =
            {
                _ring,
                new Panel { Children = { inner }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false },
                new Panel { Children = { dot }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false },
                _badge,
            },
        };
        Opacity = 0.9;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = DragDropEffects.Copy; e.Handled = true; });

        PointerEntered += (_, _) => Opacity = 1;
        PointerExited += (_, _) => Opacity = 0.9;
        PointerPressed += OnPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnReleased;
    }

    /// <summary>A green dot on the button while an update waits; clicking the button shows "Update" first on the wheel.</summary>
    public void SetUpdateBadge(string? version)
    {
        _badge.IsVisible = version is not null;
        ToolTip.SetTip(this, version is null ? Tip : $"Desk Wheel {version} is ready: click, then Update · drop a file to convert it");
    }

    /// <summary>Places the button at a saved position, or on the right edge of the primary screen, vertically centred.</summary>
    public void ShowAt(PixelPoint? saved)
    {
        var screen = (saved is { } p ? Screens.ScreenFromPoint(p) : null) ?? Screens.Primary;
        if (screen is not null)
        {
            var size = (int)(Size * screen.Scaling);
            var area = screen.WorkingArea;
            var pos = saved ?? new PixelPoint(area.Right - size - (int)(8 * screen.Scaling), area.Y + area.Height / 2 - size / 2);
            Position = new PixelPoint(
                Math.Max(area.X, Math.Min(pos.X, area.Right - size)),
                Math.Max(area.Y, Math.Min(pos.Y, area.Bottom - size)));
        }
        Show();
    }

    /// <summary>Centre of the button in screen pixels (where the wheel opens).</summary>
    public PixelPoint CentrePixel()
    {
        var scale = (Screens.ScreenFromPoint(Position) ?? Screens.Primary)?.Scaling ?? 1;
        var half = (int)(Size * scale / 2);
        return new PixelPoint(Position.X + half, Position.Y + half);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = DragDropEffects.Copy;
        if (e.Data.Contains(DataFormats.Files))
            FilesDraggedIn?.Invoke(CentrePixel());
    }

    private bool _moving;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressAt = e.GetPosition(this);
            _moving = false;
        }
    }

    /// <summary>Moves the window with the pointer once it has travelled a few pixels (otherwise it's a click).</summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressAt is not { } start)
            return;
        var now = e.GetPosition(this);
        double dx = now.X - start.X, dy = now.Y - start.Y;
        if (!_moving && Math.Abs(dx) <= MoveThreshold && Math.Abs(dy) <= MoveThreshold)
            return;
        _moving = true;
        var scale = (Screens.ScreenFromPoint(Position) ?? Screens.Primary)?.Scaling ?? 1;
        Position = new PixelPoint(Position.X + (int)(dx * scale), Position.Y + (int)(dy * scale));
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right)
            RightClicked?.Invoke();
        else if (_pressAt is not null && !_moving && e.InitialPressMouseButton == MouseButton.Left)
            Clicked?.Invoke(CentrePixel());
        if (_moving)
            Moved?.Invoke(Position);
        _pressAt = null;
        _moving = false;
    }
}
