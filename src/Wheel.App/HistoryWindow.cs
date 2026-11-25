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
using Wheel.Core;
using Wheel.Core.History;

namespace Wheel.App;

/// <summary>Where Wheel keeps its history: beside the settings.</summary>
internal static class AppHistory
{
    public static readonly HistoryStore Store = new(System.IO.Path.Combine(
        OperatingSystem.IsMacOS()
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "DeskWheel")
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskWheel"),
        "history.jsonl"));

    /// <summary>Records without ever failing the job that produced the file.</summary>
    public static void Record(string kind, string path, string? source = null, string? action = null)
    {
        try
        {
            Store.Add(new HistoryEntry(DateTimeOffset.Now, kind, path, source, action));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(ex);
        }
    }
}

/// <summary>
/// Recent screenshots and converted files, newest first, with thumbnails and a search box.
/// Click a row to show it in Finder / Explorer, double-click to open it, or drag it into any app.
/// </summary>
internal sealed class HistoryWindow : Window
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".ico" };
    private const int ThumbWidth = 96;

    private readonly IPlatform _platform;
    private readonly string _screenshotsFolder;
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly TextBox _search = Ui.SearchBox("Search by name, folder or action");
    private TextBlock _count = new();
    private readonly TextBlock _empty = new()
    {
        Text = "Nothing yet. Screenshots and converted files appear here.",
        Foreground = Ui.Muted, Margin = new Thickness(20, 30), IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly Dictionary<string, Bitmap?> _thumbs = new();
    private bool _refreshQueued;

    public HistoryWindow(IPlatform platform, string screenshotsFolder)
    {
        _platform = platform;
        _screenshotsFolder = screenshotsFolder;
        Ui.Window(this, "Desk Wheel History");
        Width = 680;
        Height = 640;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var openFolder = Ui.Secondary("Screenshots folder", () =>
        {
            Directory.CreateDirectory(_screenshotsFolder);
            _platform.OpenFolder(_screenshotsFolder);
        }, IconKind.Folder);
        var clear = Ui.Ghost("Clear", () => AppHistory.Store.Clear(), IconKind.Trash, "Forget the list. Your files stay where they are.");
        _count = Ui.Caption("", wrap: false);

        var header = new StackPanel
        {
            Children =
            {
                new DockPanel
                {
                    Margin = new Thickness(20, 16, 20, 4),
                    Children =
                    {
                        Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { openFolder, clear } }, Dock.Right),
                        new StackPanel { Spacing = 2, Children = { Ui.Title("History"), _count } },
                    },
                },
                _search,
            },
        };
        _search.Margin = new Thickness(20, 10, 20, 12);
        _search.TextChanged += (_, _) => Refresh();

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(0, 6, 0, 12), Children = { _empty, _list } } },
        });
        Content = root;

        AppHistory.Store.Changed += QueueRefresh;
        Closed += (_, _) =>
        {
            AppHistory.Store.Changed -= QueueRefresh;
            foreach (var bitmap in _thumbs.Values) bitmap?.Dispose();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Opened += (_, _) => _search.Focus();
        Refresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() => { _refreshQueued = false; Refresh(); });
    }

    private void Refresh()
    {
        _list.Children.Clear();
        var entries = HistoryStore.Filter(AppHistory.Store.Load(), _search.Text)
            .Where(e => File.Exists(e.Path) || Directory.Exists(e.Path)) // deleted or moved files drop out
            .Take(200)
            .ToList();
        _empty.IsVisible = entries.Count == 0;
        _count.Text = entries.Count == 0 ? "Recent screenshots, conversions and edits" : $"{entries.Count} recent item{(entries.Count == 1 ? "" : "s")}";
        _empty.Text = string.IsNullOrWhiteSpace(_search.Text)
            ? "Nothing yet. Screenshots and converted files appear here."
            : "No matches.";

        string? lastDay = null;
        foreach (var entry in entries)
        {
            var day = DayLabel(entry.At);
            if (day != lastDay)
            {
                var label = Ui.Label(day);
                label.Margin = new Thickness(20, lastDay is null ? 8 : 18, 20, 6);
                _list.Children.Add(label);
                lastDay = day;
            }
            _list.Children.Add(Row(entry));
        }
    }

    private static string DayLabel(DateTimeOffset at)
    {
        var day = at.LocalDateTime.Date;
        if (day == DateTime.Today) return "Today";
        if (day == DateTime.Today.AddDays(-1)) return "Yesterday";
        return day.ToString("dddd d MMMM yyyy");
    }

    private Control Row(HistoryEntry entry)
    {
        var thumb = new Border
        {
            Width = ThumbWidth, Height = 60, CornerRadius = new CornerRadius(6), ClipToBounds = true,
            Background = Ui.Raised, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            Child = Thumbnail(entry.Path) is { } bitmap
                ? new Image { Source = bitmap, Stretch = Stretch.UniformToFill }
                : new TextBlock
                {
                    Text = Badge(entry.Path), FontWeight = FontWeight.SemiBold, FontSize = 12, Foreground = Ui.Muted,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
        };

        var detail = entry.Kind switch
        {
            "Screenshot" => $"Screenshot · {entry.At.LocalDateTime:t}",
            _ when entry.Source is not null => $"{entry.Action ?? entry.Kind} · from {System.IO.Path.GetFileName(entry.Source)} · {entry.At.LocalDateTime:t}",
            _ => $"{entry.Action ?? entry.Kind} · {entry.At.LocalDateTime:t}",
        };
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Spacing = 2,
            Children =
            {
                new TextBlock { Text = entry.Name, FontWeight = FontWeight.Medium, Foreground = Ui.Text, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = detail, Foreground = Ui.Muted, FontSize = Ui.CaptionSize, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };

        var copy = Ui.IconButton(IconKind.Copy, "Copy (the picture, or the file's path)", () => _ = CopyAsync(entry));
        var show = Ui.IconButton(IconKind.Folder, "Show in its folder", () => _platform.Reveal(entry.Path));
        var open = Ui.IconButton(IconKind.Eye, "Open", () => _platform.OpenFile(entry.Path));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { open, copy, show } };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ThumbWidth},*,Auto") };
        text.Margin = new Thickness(12, 0);
        grid.Children.Add(thumb);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        var row = new Border
        {
            Child = grid, Padding = new Thickness(10, 6), Margin = new Thickness(10, 1), CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(row, entry.Path);
        row.PointerEntered += (_, _) => row.Background = Ui.Surface;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.DoubleTapped += (_, _) => _platform.OpenFile(entry.Path);
        Point? pressAt = null;
        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) pressAt = e.GetPosition(row);
        };
        row.PointerReleased += (_, _) => pressAt = null;
        row.PointerMoved += async (_, e) =>
        {
            // Drag the row out into any app: the receiver gets a copy of the file.
            if (pressAt is not { } start) return;
            var now = e.GetPosition(row);
            if (Math.Abs(now.X - start.X) < 6 && Math.Abs(now.Y - start.Y) < 6) return;
            pressAt = null;
            if (await StorageItemFor(entry.Path) is not { } item) return;
            var data = new DataObject();
            data.Set(DataFormats.Files, new[] { item });
            try
            {
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                Log.Write(ex);
            }
        };
        return row;
    }

    private async Task<Avalonia.Platform.Storage.IStorageItem?> StorageItemFor(string path) =>
        StorageProvider is { } storage
            ? Directory.Exists(path)
                ? (Avalonia.Platform.Storage.IStorageItem?)await storage.TryGetFolderFromPathAsync(new Uri(path))
                : await storage.TryGetFileFromPathAsync(new Uri(path))
            : null;

    private static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private static Button SmallButton(string label, string tip, Action run)
    {
        var b = new Button { Content = label, Padding = new Thickness(8, 3), FontSize = 12 };
        ToolTip.SetTip(b, tip);
        b.Click += (_, e) => { run(); e.Handled = true; };
        return b;
    }

    private async Task CopyAsync(HistoryEntry entry)
    {
        try
        {
            if (ImageExtensions.Contains(System.IO.Path.GetExtension(entry.Path)))
                await _platform.CopyImageAsync(entry.Path);
            else
                _platform.CopyText(entry.Path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Write(ex);
        }
    }

    private static string Badge(string path)
    {
        if (Directory.Exists(path)) return "FOLDER";
        var ext = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return ext.Length is > 0 and <= 5 ? ext : "FILE";
    }

    private Bitmap? Thumbnail(string path)
    {
        if (_thumbs.TryGetValue(path, out var cached)) return cached;
        Bitmap? bitmap = null;
        if (ImageExtensions.Contains(System.IO.Path.GetExtension(path)) && File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                bitmap = Bitmap.DecodeToWidth(stream, ThumbWidth * 2);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                bitmap = null; // unreadable or a format Avalonia can't decode: show the badge instead
            }
        }
        _thumbs[path] = bitmap;
        return bitmap;
    }
}
