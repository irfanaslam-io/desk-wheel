using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Wheel.App.Platform;
using Wheel.Core;
using Wheel.Core.Converters;
using Wheel.Core.Disk;

namespace Wheel.App;

/// <summary>
/// What's inside a ZIP, RAR, 7z, ISO or TAR, like WinRAR: walk through its folders, open any file inside,
/// extract everything or only what's selected (here, or anywhere), or drag files straight out.
/// </summary>
internal sealed class ArchiveWindow : Window
{
    private const long MaxDragOut = 500L * 1000 * 1000;

    private readonly IPlatform _platform;
    private readonly string _archive;
    private readonly StackPanel _rows = new();
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = Ui.Caption("", wrap: false);
    private readonly TextBlock _status = Ui.Caption("", wrap: false);
    private readonly Button _extractSelected;
    private readonly List<Entry> _selection = [];
    private readonly Dictionary<Entry, Border> _rowFor = [];
    private List<ArchiveItem> _items = [];
    private string _folder = "";       // the folder being shown, "" for the top
    private string? _password;
    private string? _tempFolder;

    /// <summary>A row: a file, or a folder (made up from the files' paths).</summary>
    private sealed record Entry(string Path, string Name, bool IsFolder, long Size, DateTime? Modified, int Files);

    public ArchiveWindow(IPlatform platform, string archive)
    {
        _platform = platform;
        _archive = archive;
        Ui.Window(this, Path.GetFileName(archive));
        Width = 860;
        Height = 620;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var up = Ui.IconButton(IconKind.Back, "Up a folder (Backspace)", GoUp);
        var extractAll = Ui.Primary("Extract all", () => _ = ExtractAllAsync(), IconKind.Save, "Into a new folder next to the archive");
        _extractSelected = Ui.Secondary("Extract selected", () => _ = ExtractSelectedAsync(null), IconKind.Save, "Into a folder next to the archive, keeping their folders");
        var extractTo = Ui.Ghost("Extract to…", () => _ = PickAndExtractAsync(), IconKind.Folder, "Choose where");
        var show = Ui.IconButton(IconKind.Eye, OperatingSystem.IsMacOS() ? "Show in Finder" : "Show in File Explorer", () => _platform.Reveal(_archive));

        var top = new DockPanel { Margin = new Thickness(16, 12, 16, 4) };
        top.Children.Add(Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { extractTo, _extractSelected, extractAll, show } }, Dock.Right));
        top.Children.Add(Docked(up, Dock.Left));
        top.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(8, 0), Content = _crumbs,
        });
        _summary.Margin = new Thickness(20, 0, 20, 8);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,100,150"), Margin = new Thickness(14, 4) };
        Cell(header, Ui.Label("NAME"), 1);
        Cell(header, Right(Ui.Label("SIZE")), 2);
        Cell(header, Ui.Label("MODIFIED"), 3, new Thickness(16, 0, 0, 0));

        var bottom = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 8), Child = _status };
        var root = new DockPanel();
        root.Children.Add(Docked(new StackPanel { Children = { top, _summary } }, Dock.Top));
        root.Children.Add(Docked(bottom, Dock.Bottom));
        root.Children.Add(new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new DockPanel { Children = { Docked(header, Dock.Top), new ScrollViewer { Content = _rows } } },
        });
        Content = root;

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += (_, _) => _ = LoadAsync();
        Closed += (_, _) =>
        {
            if (_tempFolder is not null) try { Directory.Delete(_tempFolder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        };
    }

    // ---------------------------------------------------------------- reading

    private async Task LoadAsync()
    {
        _status.Text = "Reading…";
        try
        {
            _items = await Task.Run(() => ArchiveConverter.List(_archive, _password));
        }
        catch (ArchivePasswordException ex)
        {
            if (await AskPasswordAsync(ex.Wrong ? "That password isn't right. Try again." : ex.Message)) await LoadAsync();
            else Close();
            return;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SharpCompress.Common.SharpCompressException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Write(ex);
            _status.Text = $"Couldn't read {Path.GetFileName(_archive)}: {ex.Message}";
            return;
        }
        var total = _items.Where(i => i.Size > 0).Sum(i => i.Size);
        _summary.Text = $"{_items.Count:N0} file{(_items.Count == 1 ? "" : "s")}" + (total > 0 ? $" · {DiskScanner.FormatSize(total)} when extracted" : "")
                        + $" · {DiskScanner.FormatSize(new FileInfo(_archive).Length)} archive"
                        + (_items.Any(i => i.Encrypted) ? " · password protected" : "");
        _status.Text = "Double-click to open · drag files out to copy them anywhere";
        Show("");
    }

    private async Task<bool> AskPasswordAsync(string message)
    {
        var typed = await PromptWindow.AskAsync("Password", message, password: true);
        if (string.IsNullOrEmpty(typed)) return false;
        _password = typed;
        return true;
    }

    /// <summary>The rows for one folder: its sub-folders first (with their sizes), then its files.</summary>
    private void Show(string folder)
    {
        _folder = folder;
        _selection.Clear();
        _rows.Children.Clear();
        _rowFor.Clear();
        var prefix = folder.Length == 0 ? "" : folder + "/";
        var entries = new List<Entry>();
        foreach (var group in _items.Where(i => i.Path.StartsWith(prefix, StringComparison.Ordinal))
                                    .GroupBy(i => i.Path[prefix.Length..].Split('/')[0]))
        {
            var name = group.Key;
            var isFolder = group.Any(i => i.Path.Length > prefix.Length + name.Length);
            if (isFolder)
                entries.Add(new Entry(prefix + name, name, true, group.Where(i => i.Size > 0).Sum(i => i.Size), group.Max(i => i.Modified), group.Count()));
            else
            {
                var item = group.First();
                entries.Add(new Entry(item.Path, name, false, item.Size, item.Modified, 1));
            }
        }
        foreach (var e in entries.OrderByDescending(e => e.IsFolder).ThenBy(e => e.Name, Wheel.Core.Files.NaturalComparer.Instance))
            _rows.Children.Add(Row(e));
        if (entries.Count == 0)
        {
            var empty = Ui.Caption("Nothing here.", wrap: false);
            empty.Margin = new Thickness(20);
            _rows.Children.Add(empty);
        }
        FillCrumbs();
        UpdateSelection();
    }

    private void FillCrumbs()
    {
        _crumbs.Children.Clear();
        var parts = _folder.Length == 0 ? [] : _folder.Split('/');
        Crumb(Path.GetFileName(_archive), "", parts.Length == 0);
        for (var i = 0; i < parts.Length; i++)
        {
            _crumbs.Children.Add(new TextBlock { Text = "›", Foreground = Ui.Faint, VerticalAlignment = VerticalAlignment.Center });
            Crumb(parts[i], string.Join('/', parts[..(i + 1)]), i == parts.Length - 1);
        }
        void Crumb(string label, string path, bool last)
        {
            var b = new Button { Content = label, Background = Brushes.Transparent, Padding = new Thickness(6, 3), FontWeight = last ? FontWeight.SemiBold : FontWeight.Normal, Foreground = last ? Ui.Text : Ui.Muted };
            b.Click += (_, _) => Show(path);
            _crumbs.Children.Add(b);
        }
    }

    private void GoUp()
    {
        if (_folder.Length == 0) return;
        var at = _folder.LastIndexOf('/');
        Show(at < 0 ? "" : _folder[..at]);
    }

    private Border Row(Entry e)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,100,150") };
        Cell(grid, Ui.Icon(e.IsFolder ? IconKind.Folder : IconKind.File, 16, e.IsFolder ? Ui.Info : Ui.Muted), 0);
        Cell(grid, new TextBlock { Text = e.Name, Foreground = Ui.Text, TextTrimming = TextTrimming.CharacterEllipsis }, 1);
        Cell(grid, Right(new TextBlock { Text = e.Size < 0 ? "" : DiskScanner.FormatSize(e.Size), Foreground = Ui.Text }), 2);
        Cell(grid, new TextBlock { Text = e.IsFolder ? $"{e.Files:N0} file{(e.Files == 1 ? "" : "s")}" : e.Modified?.ToString("d MMM yyyy, HH:mm") ?? "", Foreground = Ui.Muted, FontSize = Ui.CaptionSize }, 3, new Thickness(16, 0, 0, 0));
        var row = new Border { Child = grid, Padding = new Thickness(8, 5), Margin = new Thickness(6, 0), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
        ToolTip.SetTip(row, e.Path);
        _rowFor[e] = row;
        row.PointerEntered += (_, _) => { if (!_selection.Contains(e)) row.Background = Ui.RowHover; };
        row.PointerExited += (_, _) => Paint(row, e);
        row.DoubleTapped += (_, _) => Open(e);

        Point? pressAt = null;
        row.PointerPressed += (_, ev) =>
        {
            var point = ev.GetCurrentPoint(row);
            var add = ev.KeyModifiers.HasFlag(KeyModifiers.Control) || ev.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (point.Properties.IsRightButtonPressed)
            {
                if (!_selection.Contains(e)) { _selection.Clear(); _selection.Add(e); UpdateSelection(); }
                ShowMenu(row);
                ev.Handled = true;
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) return;
            if (add) { if (!_selection.Remove(e)) _selection.Add(e); }
            else if (!_selection.Contains(e)) { _selection.Clear(); _selection.Add(e); }
            UpdateSelection();
            pressAt = ev.GetPosition(row);
            Focus();
        };
        row.PointerReleased += (_, _) => pressAt = null;
        row.PointerMoved += async (_, ev) =>
        {
            if (pressAt is not { } start) return;
            var now = ev.GetPosition(row);
            if (Math.Abs(now.X - start.X) < 6 && Math.Abs(now.Y - start.Y) < 6) return;
            pressAt = null;
            await DragOutAsync(ev);
        };
        Paint(row, e);
        return row;
    }

    private void Paint(Border row, Entry e) => row.Background = _selection.Contains(e) ? Ui.AccentSoft : Brushes.Transparent;

    private void UpdateSelection()
    {
        foreach (var (e, row) in _rowFor) Paint(row, e);
        _extractSelected.IsEnabled = _selection.Count > 0;
        _extractSelected.Content = _selection.Count > 1 ? $"Extract {_selection.Count} selected" : "Extract selected";
    }

    private void ShowMenu(Control target)
    {
        var items = new List<MenuItem>();
        void Item(string header, Action run) { var m = new MenuItem { Header = header }; m.Click += (_, _) => run(); items.Add(m); }
        if (_selection is [{ } one]) Item(one.IsFolder ? "Open folder" : "Open", () => Open(one));
        Item("Extract here (next to the archive)", () => _ = ExtractSelectedAsync(null));
        Item("Extract to…", () => _ = PickAndExtractAsync());
        new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer }.Open(target);
    }

    // ---------------------------------------------------------------- actions

    /// <summary>A folder opens here; a file is extracted to a temporary folder and opened in its app.</summary>
    private void Open(Entry e)
    {
        if (e.IsFolder) { Show(e.Path); return; }
        _ = OpenFileAsync(e);
    }

    private async Task OpenFileAsync(Entry e)
    {
        var files = await ExtractToTempAsync([e]);
        if (files is [{ } file]) _platform.OpenFile(file);
    }

    private async Task<IReadOnlyList<string>?> ExtractToTempAsync(IReadOnlyList<Entry> entries)
    {
        _tempFolder ??= Path.Combine(Path.GetTempPath(), "wheel-archive-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(_tempFolder, Guid.NewGuid().ToString("N")[..8]);
        var written = await RunAsync(entries, folder, "Opening…");
        if (written is null) return null;
        // The items as chosen (a folder as one item), for opening or dragging: each lands as folder/its name.
        return entries.Select(e => Path.Combine(folder, e.Name)).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
    }

    private async Task ExtractAllAsync()
    {
        _status.Text = "Extracting…";
        try
        {
            var folder = await Task.Run(() => ArchiveConverter.Extract(_archive, _password));
            AppHistory.Record("Converted", folder, _archive, "Extracted");
            _status.Text = $"Extracted to {Path.GetFileName(folder)}";
            _platform.Reveal(folder);
        }
        catch (ArchivePasswordException ex)
        {
            if (await AskPasswordAsync(ex.Wrong ? "That password isn't right. Try again." : ex.Message)) await ExtractAllAsync();
            else _status.Text = "Not extracted: it needs the password.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SharpCompress.Common.SharpCompressException or UnauthorizedAccessException)
        {
            Log.Write(ex);
            _status.Text = "Couldn't extract: " + ex.Message;
        }
    }

    /// <summary>Into <paramref name="target"/>, or a new folder next to the archive (named after it).</summary>
    private async Task ExtractSelectedAsync(string? target)
    {
        if (_selection.Count == 0) return;
        target ??= OutputPaths.FolderBeside(_archive);
        var written = await RunAsync(_selection.ToList(), target, "Extracting…");
        if (written is null) return;
        _status.Text = $"Extracted {written.Count:N0} file{(written.Count == 1 ? "" : "s")} to {target}";
        _platform.Reveal(written.FirstOrDefault() ?? target);
    }

    private async Task PickAndExtractAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Extract to", AllowMultiple = false });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } folder) return;
        if (_selection.Count == 0) _selection.AddRange(_rowFor.Keys); // nothing chosen: everything shown
        await ExtractSelectedAsync(folder);
    }

    private async Task<IReadOnlyList<string>?> RunAsync(IReadOnlyList<Entry> entries, string folder, string doing)
    {
        _status.Text = doing;
        try
        {
            var paths = entries.Select(e => e.Path).ToList();
            // Folders keep their place: extracting "a/b" from inside folder "a" makes "b" in the target.
            var temp = _folder.Length == 0 ? folder : Path.Combine(Path.GetTempPath(), "wheel-x-" + Guid.NewGuid().ToString("N"));
            var written = await Task.Run(() => ArchiveConverter.ExtractSome(_archive, paths, temp, _password));
            if (_folder.Length > 0)
            {
                var inner = Path.Combine(temp, _folder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(folder);
                var moved = new List<string>();
                foreach (var file in written)
                {
                    var target = Path.Combine(folder, Path.GetRelativePath(inner, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (File.Exists(target)) target = OutputPaths.Unique(target);
                    File.Move(file, target);
                    moved.Add(target);
                }
                try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
                written = moved;
            }
            _status.Text = "";
            return written;
        }
        catch (ArchivePasswordException ex)
        {
            if (await AskPasswordAsync(ex.Wrong ? "That password isn't right. Try again." : ex.Message)) return await RunAsync(entries, folder, doing);
            _status.Text = "It needs the password.";
            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SharpCompress.Common.SharpCompressException or UnauthorizedAccessException)
        {
            Log.Write(ex);
            _status.Text = "Couldn't extract: " + ex.Message;
            return null;
        }
    }

    /// <summary>Dragging rows out copies those files (extracted first) into wherever they're dropped.</summary>
    private async Task DragOutAsync(PointerEventArgs e)
    {
        var size = _selection.Sum(s => Math.Max(0, s.Size));
        if (size > MaxDragOut)
        {
            _status.Text = $"Too big to drag ({DiskScanner.FormatSize(size)}): use Extract to… instead.";
            return;
        }
        var files = await ExtractToTempAsync(_selection.ToList());
        if (files is null || StorageProvider is not { } storage) return;
        var items = new List<IStorageItem>();
        foreach (var f in files)
        {
            IStorageItem? item = Directory.Exists(f) ? await storage.TryGetFolderFromPathAsync(new Uri(f)) : await storage.TryGetFileFromPathAsync(new Uri(f));
            if (item is not null) items.Add(item);
        }
        if (items.Count == 0) return;
        var data = new DataObject();
        data.Set(DataFormats.Files, items);
        try { await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { Log.Write(ex); }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Back: GoUp(); break;
            case Key.Enter when _selection is [{ } one]: Open(one); break;
            case Key.A when command: _selection.Clear(); _selection.AddRange(_rowFor.Keys); UpdateSelection(); break;
            default: return;
        }
        e.Handled = true;
    }

    private static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private static void Cell(Grid grid, Control c, int column, Thickness? margin = null)
    {
        Grid.SetColumn(c, column);
        if (margin is { } m) c.Margin = m;
        c.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(c);
    }

    private static Control Right(Control c)
    {
        c.HorizontalAlignment = HorizontalAlignment.Right;
        return c;
    }
}
