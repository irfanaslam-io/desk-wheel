using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Wheel.App;

/// <summary>
/// The card that appears after a screenshot (bottom-left): a thumbnail plus Copy, Edit, Pin, Wheel, Show and close.
/// Drag the thumbnail into any app to drop the file there. Closes itself after 10 s unless the pointer is on it.
/// </summary>
internal sealed class PreviewWindow : Window
{
    private const double CardWidth = 380, Gap = 16, ThumbMaxHeight = 200;

    private readonly string _path;
    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(10) };
    private IStorageFile? _file;   // looked up in advance: a drag must start synchronously
    private Point? _pressAt;

    public event Action? CopyRequested;
    public event Action? PinRequested;
    public event Action? WheelRequested;
    public event Action? ShowRequested;
    public event Action? EditRequested;
    public event Action? TextRequested;

    /// <summary>Shows the "Text" button (copy the text in the screenshot). Set before the card is shown.</summary>
    public bool ShowsTextButton
    {
        get => _textButton.IsVisible;
        set => _textButton.IsVisible = value;
    }

    private readonly Button _textButton;

    public PreviewWindow(string path)
    {
        _path = path;
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
        Title = "Desk Wheel screenshot";

        var thumb = new Image
        {
            Source = new Bitmap(path),
            Stretch = Stretch.Uniform,
            MaxHeight = ThumbMaxHeight,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(thumb, "Drag into any app");
        thumb.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(thumb).Properties.IsLeftButtonPressed) _pressAt = e.GetPosition(this);
        };
        thumb.PointerReleased += (_, _) => _pressAt = null;
        thumb.PointerMoved += OnThumbMoved;

        var name = System.IO.Path.GetFileName(path);
        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 0) };
        var close = Ui.IconButton(IconKind.Close, "Close", Close, 12);
        close.Padding = new Thickness(5);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new StackPanel
        {
            Spacing = 1, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = name.StartsWith("QR code") ? "QR code ready" : "Screenshot saved", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Ui.Text },
                new TextBlock { Text = name, FontSize = 11.5, Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        });

        var frame = new Border
        {
            CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = Ui.Canvas,
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = thumb,
        };

        _textButton = Action(IconKind.Text, "Text", "Copy the text in it", () => TextRequested?.Invoke());
        _textButton.IsVisible = false;
        var actions = new Avalonia.Controls.Primitives.UniformGrid { Rows = 1 };
        actions.Children.Add(Action(IconKind.Copy, "Copy", "Copy the picture", () => CopyRequested?.Invoke()));
        actions.Children.Add(Action(IconKind.Edit, "Edit", "Mark up, blur, crop, beautify", () => { EditRequested?.Invoke(); Close(); }));
        actions.Children.Add(_textButton);
        actions.Children.Add(Action(IconKind.Pin, "Pin", "Keep it floating on top", () => { PinRequested?.Invoke(); Close(); }));
        actions.Children.Add(Action(IconKind.Wheel, "Convert", "Open the wheel for it", () => { WheelRequested?.Invoke(); Close(); }));
        actions.Children.Add(Action(IconKind.Folder, "Show", "Show in the folder", () => { ShowRequested?.Invoke(); Close(); }));

        Content = new Border
        {
            CornerRadius = Ui.CardRadius,
            Background = Ui.Floating,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Child = new StackPanel { Spacing = 10, Children = { header, frame, actions } },
        };

        _autoClose.Tick += (_, _) => Close();
        PointerEntered += (_, _) => _autoClose.Stop();
        PointerExited += (_, _) => _autoClose.Start();
        Closed += (_, _) => _autoClose.Stop();
        Opened += async (_, _) =>
        {
            try { _file = await StorageProvider.TryGetFileFromPathAsync(new Uri(_path)); }
            catch (Exception ex) when (ex is ArgumentException or UriFormatException) { Log.Write(ex); }
        };
    }

    public void ShowInCorner()
    {
        Show(); // size is known only after the first layout
        var screen = Screens.Primary;
        if (screen is not null)
        {
            var s = screen.Scaling;
            var area = screen.WorkingArea;
            var h = (int)(Bounds.Height * s);
            var m = (int)(Gap * s);
            Position = new PixelPoint(area.X + m, area.Bottom - h - m);
        }
        _autoClose.Start();
    }

    private async void OnThumbMoved(object? sender, PointerEventArgs e)
    {
        if (_pressAt is not { } start || _file is null) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < 6 && Math.Abs(now.Y - start.Y) < 6) return;
        _pressAt = null;

        var data = new DataObject();
        data.Set(DataFormats.Files, new[] { _file });
        try
        {
            var result = await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            if (result != DragDropEffects.None) Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            Log.Write(ex);
        }
    }

    /// <summary>An icon over a short label: the card's action buttons.</summary>
    private static Button Action(IconKind icon, string label, string tip, Action onClick)
    {
        var b = new Button
        {
            Padding = new Thickness(2, 6), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new StackPanel
            {
                Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { Ui.Icon(icon, 17), new TextBlock { Text = label, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center } },
            },
        };
        b.Classes.Add("ghost");
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }
}
