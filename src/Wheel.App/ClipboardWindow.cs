using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Wheel.App.Platform;
using Wheel.Core.Clipboard;

namespace Wheel.App;

/// <summary>
/// Watches the clipboard and records what's copied (text, images, files) into <see cref="ClipboardStore"/>.
/// Polls a cheap change counter twice a second; only reads the clipboard when it changed. Content marked
/// private by the app that copied it (password managers) is never read into the history.
/// </summary>
internal sealed class ClipboardMonitor
{
    private readonly IPlatform _platform;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private long _lastCount = -1;

    public ClipboardStore Store { get; }

    public ClipboardMonitor(IPlatform platform, string dataFolder)
    {
        _platform = platform;
        Store = new ClipboardStore(Path.Combine(dataFolder, "Clipboard"));
        _timer.Tick += (_, _) => Poll();
    }

    public bool Running => _timer.IsEnabled;

    public void Start()
    {
        _lastCount = SafeCount();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private long SafeCount()
    {
        try { return _platform.ClipboardChangeCount(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log.Write(ex); return -1; }
    }

    private void Poll()
    {
        var count = SafeCount();
        if (count == _lastCount || count < 0) return;
        _lastCount = count;
        try
        {
            if (_platform.ReadClipboard() is { } content)
                Store.Add(content, DateTimeOffset.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Log.Write(ex);
        }
    }
}

/// <summary>
/// The clipboard-history picker: opens near the pointer, type to search, ↑↓ to move, Enter (or a click)
/// copies the item back to the clipboard. ⌘/Ctrl+1…9 picks one of the first nine. Esc or clicking away closes.
/// </summary>
internal sealed class ClipboardWindow : Window
{
    private const int MaxShown = 100;
    private readonly IPlatform _platform;
    private readonly ClipboardMonitor _monitor;
    private readonly Action<string, string> _notify;
    private readonly Action _openSettings;
    private readonly TextBox _search = Ui.SearchBox("Search what you copied");
    private readonly StackPanel _list = new() { Spacing = 1 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty = new() { Foreground = Ui.Muted, Margin = new Thickness(18, 24), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _status = new() { Foreground = Ui.Faint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, Bitmap?> _thumbs = new();
    private List<ClipEntry> _shown = new();
    private int _selected;
    private bool _refreshQueued, _closing;

    public ClipboardWindow(IPlatform platform, ClipboardMonitor monitor, Action<string, string> notify, Action openSettings)
    {
        _platform = platform;
        _monitor = monitor;
        _notify = notify;
        _openSettings = openSettings;
        Title = "Clipboard";
        Width = 460;
        Height = 560;
        CanResize = true;
        Topmost = true;
        ShowInTaskbar = false;
        SystemDecorations = SystemDecorations.BorderOnly;
        Background = Ui.Surface;
        Foreground = Ui.Text;

        var clear = Ui.Ghost("Clear", () => _monitor.Store.ClearUnpinned(), IconKind.Trash, "Forget everything except pinned items");
        var settings = Ui.IconButton(IconKind.Settings, "Clipboard settings", () => { CloseSoon(); _openSettings(); });
        var footer = new DockPanel { Margin = new Thickness(14, 8, 10, 10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { clear, settings } };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_status);

        var title = new DockPanel
        {
            Margin = new Thickness(16, 14, 14, 0),
            Children =
            {
                Docked(new TextBlock { Text = $"{(OperatingSystem.IsMacOS() ? "⌥⇧V" : "Alt+Shift+V")}", FontSize = 11.5, Foreground = Ui.Faint, VerticalAlignment = VerticalAlignment.Center }, Avalonia.Controls.Dock.Right),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Icon(IconKind.Clipboard, 16, Ui.Muted), Ui.Heading("Clipboard") } },
            },
        };
        _search.Margin = new Thickness(12, 10, 12, 8);
        _scroll = new ScrollViewer { Content = new StackPanel { Children = { _empty, _list } } };
        var top = new StackPanel { Children = { title, _search } };
        var root = new DockPanel();
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer, [DockPanel.DockProperty] = Avalonia.Controls.Dock.Bottom });
        root.Children.Add(_scroll);
        Content = new Border { Child = root, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = Ui.CardRadius };

        _search.TextChanged += (_, _) => { _selected = 0; Refresh(); };
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Deactivated += (_, _) => CloseSoon();
        _monitor.Store.Changed += QueueRefresh;
        Closed += (_, _) =>
        {
            _monitor.Store.Changed -= QueueRefresh;
            foreach (var b in _thumbs.Values) b?.Dispose();
        };
        Opened += (_, _) => _search.Focus();
        Refresh();
    }

    private static Control Docked(Control c, Avalonia.Controls.Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    /// <summary>Opens centred on the pointer, kept inside the screen it's on.</summary>
    public void ShowNear(PixelPoint pointer)
    {
        var screen = Screens.ScreenFromPoint(pointer) ?? Screens.Primary;
        var scale = screen?.Scaling ?? 1;
        var w = (int)(Width * scale);
        var h = (int)(Height * scale);
        var x = pointer.X - w / 2;
        var y = pointer.Y - h / 3;
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            x = Math.Clamp(x, area.X + 8, Math.Max(area.X + 8, area.Right - w - 8));
            y = Math.Clamp(y, area.Y + 8, Math.Max(area.Y + 8, area.Bottom - h - 8));
        }
        Position = new PixelPoint(x, y);
        Show();
        Activate();
    }

    private void CloseSoon()
    {
        if (_closing) return;
        _closing = true;
        Dispatcher.UIThread.Post(Close);
    }

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() => { _refreshQueued = false; Refresh(); });
    }

    private void Refresh()
    {
        _shown = ClipboardStore.Filter(_monitor.Store.Entries, _search.Text).Take(MaxShown).ToList();
        _selected = Math.Clamp(_selected, 0, Math.Max(0, _shown.Count - 1));
        _list.Children.Clear();
        for (var i = 0; i < _shown.Count; i++)
            _list.Children.Add(Row(_shown[i], i));

        _empty.IsVisible = _shown.Count == 0;
        _empty.Text = !string.IsNullOrWhiteSpace(_search.Text)
            ? "No matches."
            : _monitor.Running
                ? "Nothing yet. Text, images and files you copy will appear here."
                : "Clipboard history is off. Turn it on in Settings.";
        var count = _monitor.Store.Entries.Count;
        _status.Text = _monitor.Running
            ? $"{count} item{(count == 1 ? "" : "s")} · kept on this computer only"
            : "Paused";
    }

    private Control Row(ClipEntry entry, int index)
    {
        Control preview = entry.Kind == ClipKind.Image && Thumbnail(entry) is { } bitmap
            ? new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxHeight = 64, MaxWidth = 120, HorizontalAlignment = HorizontalAlignment.Left }
            : new TextBlock
            {
                Text = entry.Kind == ClipKind.Text ? (entry.Text ?? "").Trim() : entry.Title,
                TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis,
                FlowDirection = Wheel.Core.Ocr.OcrText.IsMostlyArabic(entry.Text ?? "") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            };

        var detail = entry.Kind switch
        {
            ClipKind.Image => entry.Title,
            ClipKind.Files => string.Join(", ", (entry.Files ?? Array.Empty<string>()).Select(f => System.IO.Path.GetFileName(f.TrimEnd('/', '\\'))).Take(4)),
            _ => $"{(entry.Text ?? "").Length:N0} characters",
        };
        var meta = new TextBlock
        {
            Text = $"{(index < 9 ? $"{Modifier}{index + 1}  ·  " : "")}{Ago(entry.At)}  ·  {detail}",
            Foreground = Ui.Faint, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var pin = Ui.IconButton(IconKind.Star, entry.Pinned ? "Unpin" : "Pin: keep it at the top", () => _monitor.Store.SetPinned(entry, !entry.Pinned), 14);
        if (entry.Pinned)
            pin.Content = Ui.Icon(IconKind.Star, 14, new SolidColorBrush(Color.FromRgb(0xF6, 0xC9, 0x0E)));
        var remove = Ui.IconButton(IconKind.Close, "Remove", () => _monitor.Store.Remove(entry), 12);
        pin.Padding = remove.Padding = new Thickness(5);

        var body = new StackPanel { Spacing = 3, Children = { preview, meta } };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Children = { pin, remove } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(body);
        Grid.SetColumn(tools, 1);
        grid.Children.Add(tools);

        var selected = index == _selected;
        var row = new Border
        {
            Child = grid, Padding = new Thickness(12, 8, 6, 8), Margin = new Thickness(8, 1), CornerRadius = new CornerRadius(7),
            Background = selected ? Ui.Raised : Brushes.Transparent,
            BorderBrush = selected ? Ui.Accent : Brushes.Transparent,
            BorderThickness = new Thickness(2, 0, 0, 0), Cursor = new Cursor(StandardCursorType.Hand),
        };
        row.PointerEntered += (_, _) => { if (index != _selected) row.Background = Ui.RowHover; };
        row.PointerExited += (_, _) => { if (index != _selected) row.Background = Brushes.Transparent; };
        row.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) _ = UseAsync(entry);
        };
        return row;
    }

    private static string Modifier => OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";

    private static string Ago(DateTimeOffset at)
    {
        var span = DateTimeOffset.Now - at;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
        return at.LocalDateTime.ToString("d MMM");
    }

    private Bitmap? Thumbnail(ClipEntry entry)
    {
        if (_thumbs.TryGetValue(entry.Id, out var cached)) return cached;
        Bitmap? bitmap = null;
        try
        {
            using var stream = File.OpenRead(_monitor.Store.ImagePath(entry));
            bitmap = Bitmap.DecodeToWidth(stream, 240);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            bitmap = null;
        }
        _thumbs[entry.Id] = bitmap;
        return bitmap;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Escape:
                CloseSoon();
                break;
            case Key.Down:
                Move(1);
                break;
            case Key.Up:
                Move(-1);
                break;
            case Key.Enter when _shown.Count > 0:
                _ = UseAsync(_shown[_selected]);
                break;
            case >= Key.D1 and <= Key.D9 when command:
                var i = e.Key - Key.D1;
                if (i < _shown.Count) _ = UseAsync(_shown[i]);
                break;
            case Key.Back or Key.Delete when command && _shown.Count > 0:
                _monitor.Store.Remove(_shown[_selected]);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Move(int by)
    {
        if (_shown.Count == 0) return;
        _selected = Math.Clamp(_selected + by, 0, _shown.Count - 1);
        Refresh();
        if (_list.Children.Count > _selected)
            _list.Children[_selected].BringIntoView();
    }

    /// <summary>Puts the item back on the clipboard, ready to paste, and closes.</summary>
    private async Task UseAsync(ClipEntry entry)
    {
        try
        {
            switch (entry.Kind)
            {
                case ClipKind.Image:
                    await _platform.CopyImageAsync(_monitor.Store.ImagePath(entry));
                    break;
                case ClipKind.Files:
                    var existing = (entry.Files ?? Array.Empty<string>()).Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
                    if (existing.Count == 0)
                    {
                        _notify("Those files are gone", "They were moved or deleted since you copied them.");
                        return;
                    }
                    _platform.CopyFiles(existing);
                    break;
                default:
                    _platform.CopyText(entry.Text ?? "");
                    break;
            }
            _monitor.Store.Touch(entry, DateTimeOffset.Now);
            CloseSoon();
            _notify("Copied", $"Paste with {(OperatingSystem.IsMacOS() ? "⌘V" : "Ctrl+V")}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Write(ex);
            _notify("Couldn't copy it", ex.Message);
        }
    }
}
