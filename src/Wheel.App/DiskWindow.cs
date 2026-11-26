using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Wheel.App.Platform;
using Wheel.Core.Converters;
using Wheel.Core.Disk;

namespace Wheel.App;

/// <summary>
/// What's taking up space: a folder's contents as a list (biggest first) beside a ring chart. Select items to
/// open, show, rename, ZIP, move to the Trash or run any Desk Wheel tool on them; collect items from different
/// folders and move them to the Trash together.
/// </summary>
internal sealed class DiskWindow : Window
{
    private const int MaxRows = 300;

    private readonly IPlatform _platform;
    private readonly Action<IReadOnlyList<string>> _openWheel;
    private readonly SunburstView _chart = new();
    private readonly StackPanel _rows = new();
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = Ui.Caption("", wrap: false);
    private readonly TextBlock _selectionText = new() { Foreground = Ui.Text, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.Medium };
    private readonly StackPanel _selectionActions = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly TextBlock _trayText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted };
    private readonly Border _tray;
    private readonly Button _trayReview;
    private readonly Button _trayTrash;
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3, MinHeight = 3, IsVisible = false };
    private readonly Button _up;
    private readonly Button _cancel;

    private readonly List<DiskNode> _selection = [];
    private readonly List<DiskNode> _collected = [];
    private readonly Dictionary<DiskNode, Border> _rowFor = [];
    private DiskNode? _root;
    private DiskNode? _current;
    private DiskNode? _anchor; // for Shift-click ranges
    private long _freeSpace;
    private CancellationTokenSource? _scan;
    private DateTime _trashArmed;
    private bool _largest;
    private IReadOnlyList<DiskNode> _visible = [];
    private readonly ToggleButton _largestToggle = new() { Content = "Largest files", Padding = new Thickness(10, 4) };

    public DiskWindow(IPlatform platform, string folder, Action<IReadOnlyList<string>> openWheel)
    {
        _platform = platform;
        _openWheel = openWheel;
        Ui.Window(this, "Disk Space");
        Width = 1180;
        Height = 760;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // ---- top: where we are, and what to scan
        _up = Ui.IconButton(IconKind.Back, "Up a folder (Backspace)", GoUp);
        var rescan = Ui.IconButton(IconKind.Refresh, "Scan again", () => { if (_root is not null) StartScan(_current?.FullPath ?? _root.FullPath); });
        _cancel = Ui.Ghost("Stop", () => _scan?.Cancel(), IconKind.Close, "Stop scanning");
        _cancel.IsVisible = false;
        var choose = Ui.Secondary("Scan…", () => { }, IconKind.Folder, "Scan a drive or folder");
        choose.Flyout = PlacesMenu();
        var top = new DockPanel { Margin = new Thickness(16, 12, 16, 6), LastChildFill = true };
        ToolTip.SetTip(_largestToggle, "The biggest files anywhere in this folder, in one list");
        _largestToggle.IsCheckedChanged += (_, _) => { _largest = _largestToggle.IsChecked == true; _selection.Clear(); FillRows(); UpdateSelection(); };
        top.Children.Add(Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _cancel, _largestToggle, rescan, choose } }, Dock.Right));
        top.Children.Add(Docked(_up, Dock.Left));
        top.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(8, 0), Content = _crumbs,
        });
        _status.Margin = new Thickness(20, 0, 20, 8);

        // ---- left: the list
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,100,90,110"), Margin = new Thickness(14, 4, 14, 4) };
        AddCell(header, Ui.Label("NAME"), 1);
        AddCell(header, Right(Ui.Label("SIZE")), 2);
        AddCell(header, Ui.Label("KIND"), 4, new Thickness(12, 0, 0, 0));
        var list = new DockPanel();
        list.Children.Add(Docked(header, Dock.Top));
        list.Children.Add(new ScrollViewer { Content = _rows, Padding = new Thickness(0, 0, 0, 8) });

        // ---- right: the chart
        _chart.Margin = new Thickness(8);
        _chart.Open += node => Navigate(node);
        _chart.Up += GoUp;
        _chart.Pick += (node, add) => Select(node, add ? SelectMode.Toggle : SelectMode.Only, reveal: true);
        _chart.Menu += (node, e) =>
        {
            if (!_selection.Contains(node)) Select(node, SelectMode.Only, reveal: true);
            ShowMenu(_chart, e);
        };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,6*") };
        body.Children.Add(new Border { Child = list, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0) });
        Grid.SetColumn(_chart, 1);
        body.Children.Add(_chart);

        // ---- bottom: what's selected, and the collect tray
        var selectionBar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { _selectionText, _selectionActions },
        };
        _trayReview = Ui.Ghost("Review", () => { }, IconKind.Eye, "See and change what's collected");
        _trayReview.Flyout = new Flyout { Placement = PlacementMode.TopEdgeAlignedRight };
        _trayReview.Click += (_, _) => FillReview();
        _trayTrash = Ui.Secondary($"Move to {_platform.TrashName}", () => _ = TrashCollectedAsync(), IconKind.Trash);
        _tray = new Border
        {
            CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1.5), BorderBrush = Ui.LineStrong,
            Padding = new Thickness(14, 4, 6, 4), Background = Ui.Surface,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { Ui.Icon(IconKind.Trash, 15, Ui.Muted), _trayText, _trayReview, _trayTrash },
            },
        };
        DragDrop.SetAllowDrop(_tray, true);
        _tray.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
            _tray.BorderBrush = Ui.Accent;
        });
        _tray.AddHandler(DragDrop.DragLeaveEvent, (_, _) => _tray.BorderBrush = Ui.LineStrong);
        _tray.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            _tray.BorderBrush = Ui.LineStrong;
            if (e.Data.Contains(DragFormat)) Collect(_selection.ToList());
        });
        ToolTip.SetTip(_tray, "Drag items here (or press C) to collect them from any folder, then move them all to the "
                              + _platform.TrashName + " at once");

        var bottom = new DockPanel { Margin = new Thickness(14, 8, 14, 10) };
        bottom.Children.Add(Docked(_tray, Dock.Right));
        bottom.Children.Add(selectionBar);

        var root = new DockPanel();
        root.Children.Add(Docked(new StackPanel { Children = { top, _status, _progress } }, Dock.Top));
        root.Children.Add(Docked(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = bottom }, Dock.Bottom));
        root.Children.Add(new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = body });
        Content = root;

        KeyDown += OnKey;
        Closed += (_, _) => _scan?.Cancel();
        UpdateSelection();
        UpdateTray();
        StartScan(folder);
    }

    private const string DragFormat = "deskwheel/disk-items";

    // ---------------------------------------------------------------- scanning

    private void StartScan(string folder)
    {
        _scan?.Cancel();
        var cts = new CancellationTokenSource();
        _scan = cts;
        _progress.IsVisible = _cancel.IsVisible = true;
        _status.Text = $"Scanning {folder}…";
        var progress = new Progress<ScanProgress>(p =>
        {
            if (cts.IsCancellationRequested) return;
            _status.Text = $"Scanning… {p.Files:N0} files · {DiskScanner.FormatSize(p.Bytes)} · {Shorten(p.Current)}";
        });
        _ = Run();

        async Task Run()
        {
            try
            {
                var started = DateTime.UtcNow;
                var tree = await DiskScanner.ScanAsync(folder, progress, cts.Token);
                if (cts.IsCancellationRequested) return;
                Log.Trace($"Disk scan of {folder}: {tree.FileCount:N0} files, {DiskScanner.FormatSize(tree.Size)} in {(DateTime.UtcNow - started).TotalSeconds:0.0} s");
                _root = tree;
                _freeSpace = DiskScanner.DriveSpace(folder)?.Free ?? 0;
                _selection.Clear();
                _collected.Clear();
                UpdateTray();
                Navigate(tree);
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(_scan, cts)) _status.Text = _root is null ? "Stopped." : StatusLine();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _status.Text = ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_scan, cts)) _progress.IsVisible = _cancel.IsVisible = false;
            }
        }
    }

    private static string Shorten(string path) => path.Length <= 70 ? path : "…" + path[^68..];

    private string StatusLine()
    {
        if (_current is null) return "";
        var line = $"{DiskScanner.FormatSize(_current.Size)} · {_current.FileCount:N0} files";
        if (ReferenceEquals(_current, _root) && _freeSpace > 0) line += $" · {DiskScanner.FormatSize(_freeSpace)} free";
        if (_current.Incomplete)
            line += OperatingSystem.IsMacOS()
                ? " · some folders couldn't be read (allow Desk Wheel Full Disk Access in System Settings → Privacy & Security for full sizes)"
                : " · some folders couldn't be read (they need administrator rights)";
        return line;
    }

    private Flyout PlacesMenu()
    {
        var panel = new StackPanel { Spacing = 2, MinWidth = 260 };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedRight };
        void Add(string title, string detail, IconKind icon, Action run)
        {
            var b = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent, Padding = new Thickness(10, 6),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10,
                    Children =
                    {
                        Ui.Icon(icon, 16, Ui.Muted),
                        new StackPanel { Children = { new TextBlock { Text = title, Foreground = Ui.Text }, Ui.Caption(detail, wrap: false) } },
                    },
                },
            };
            b.Click += (_, _) => { flyout.Hide(); run(); };
            panel.Children.Add(b);
        }
        flyout.Opening += (_, _) =>
        {
            panel.Children.Clear();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Add("Home folder", home, IconKind.Folder, () => StartScan(home));
            foreach (var drive in Drives())
                Add(drive.Name, drive.Detail, IconKind.File, () => StartScan(drive.Path));
            Add("Choose a folder…", "Any folder or disk", IconKind.Plus, () => _ = PickFolderAsync());
        };
        return flyout;
    }

    private static IEnumerable<(string Name, string Detail, string Path)> Drives()
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            (string, string, string)? item = null;
            try
            {
                if (!d.IsReady || d.DriveType is DriveType.Ram or DriveType.NoRootDirectory or DriveType.Unknown) continue;
                var path = d.RootDirectory.FullName;
                // On a Mac only the startup disk and disks under /Volumes mean anything to people.
                if (OperatingSystem.IsMacOS() && path != "/" && !path.StartsWith("/Volumes/", StringComparison.Ordinal)) continue;
                if (OperatingSystem.IsLinux() && d.DriveType != DriveType.Fixed) continue;
                var name = OperatingSystem.IsMacOS()
                    ? path == "/" ? "Macintosh HD" : Path.GetFileName(path.TrimEnd('/'))
                    : $"{(string.IsNullOrEmpty(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel)} ({path.TrimEnd('\\')})";
                item = (name, $"{DiskScanner.FormatSize(d.AvailableFreeSpace)} free of {DiskScanner.FormatSize(d.TotalSize)}", path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (item is { } i) yield return i;
        }
    }

    private async Task PickFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder to scan", AllowMultiple = false });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) StartScan(path);
    }

    // ---------------------------------------------------------------- moving around

    private void Navigate(DiskNode folder)
    {
        _current = folder;
        _selection.RemoveAll(n => !n.IsInside(folder) || ReferenceEquals(n, folder));
        _anchor = null;
        _up.IsEnabled = folder.Parent is not null;
        _chart.Show(folder, ReferenceEquals(folder, _root) ? _freeSpace : 0);
        FillCrumbs();
        FillRows();
        UpdateSelection();
        _status.Text = StatusLine();
    }

    private void GoUp()
    {
        if (_current?.Parent is { } parent)
        {
            var from = _current;
            Navigate(parent);
            Select(from, SelectMode.Only, reveal: true);
        }
    }

    private void FillCrumbs()
    {
        _crumbs.Children.Clear();
        var chain = new List<DiskNode>();
        for (var n = _current; n is not null; n = n.Parent) chain.Insert(0, n);
        for (var i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            if (i > 0) _crumbs.Children.Add(new TextBlock { Text = "›", Foreground = Ui.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0) });
            var label = i == 0 ? node.FullPath : node.Name;
            var crumb = new Button
            {
                Content = label, Background = Brushes.Transparent, Padding = new Thickness(6, 3),
                FontWeight = i == chain.Count - 1 ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = i == chain.Count - 1 ? Ui.Text : Ui.Muted,
            };
            crumb.Click += (_, _) => Navigate(node);
            _crumbs.Children.Add(crumb);
        }
    }

    // ---------------------------------------------------------------- the list

    private void FillRows()
    {
        _rows.Children.Clear();
        _rowFor.Clear();
        _visible = [];
        if (_current is null) return;
        var children = _largest ? Largest(_current, MaxRows) : _current.Children;
        _visible = children.Take(MaxRows).ToList();
        var biggest = Math.Max(1, children.Count > 0 ? children[0].Size : 1);
        foreach (var child in children.Take(MaxRows))
            _rows.Children.Add(Row(child, biggest));
        if (children.Count > MaxRows)
        {
            var rest = children.Skip(MaxRows).ToList();
            var more = Ui.Caption($"and {rest.Count:N0} smaller items · {DiskScanner.FormatSize(rest.Sum(r => r.Size))}", wrap: false);
            more.Margin = new Thickness(56, 8);
            _rows.Children.Add(more);
        }
        if (children.Count == 0)
        {
            var empty = Ui.Caption(_current.Incomplete ? "This folder couldn't be read." : "This folder is empty.", wrap: false);
            empty.Margin = new Thickness(20, 20);
            _rows.Children.Add(empty);
        }
    }

    /// <summary>The biggest files anywhere inside a folder, biggest first.</summary>
    private static List<DiskNode> Largest(DiskNode folder, int count)
    {
        var top = new PriorityQueue<DiskNode, long>();
        var stack = new Stack<DiskNode>();
        stack.Push(folder);
        while (stack.Count > 0)
        {
            foreach (var child in stack.Pop().Children)
            {
                if (child.IsFolder)
                {
                    // Smaller than the smallest kept file: nothing inside can make the list.
                    if (top.Count < count || child.Size > top.Peek().Size) stack.Push(child);
                    continue;
                }
                if (top.Count < count) top.Enqueue(child, child.Size);
                else if (child.Size > top.Peek().Size) top.EnqueueDequeue(child, child.Size);
            }
        }
        var list = new List<DiskNode>(top.Count);
        while (top.Count > 0) list.Add(top.Dequeue());
        list.Reverse();
        return list;
    }

    private Border Row(DiskNode node, long biggest)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,100,90,110") };
        AddCell(grid, Ui.Icon(node.IsFolder ? IconKind.Folder : IconKind.File, 16, node.IsFolder ? Ui.Info : Ui.Muted), 0);
        AddCell(grid, new TextBlock { Text = node.Name, Foreground = Ui.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, 1);
        AddCell(grid, Right(new TextBlock { Text = DiskScanner.FormatSize(node.Size), Foreground = Ui.Text }), 2);
        var bar = new Border
        {
            Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Line, Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Border
            {
                Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Info, HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(3, 78.0 * node.Size / biggest),
            },
        };
        AddCell(grid, bar, 3);
        AddCell(grid, new TextBlock
        {
            Text = _largest && node.Parent is { } where ? "in " + where.Name
                : node.IsFolder ? $"{node.FileCount:N0} file{(node.FileCount == 1 ? "" : "s")}" : node.Kind,
            Foreground = Ui.Muted, FontSize = Ui.CaptionSize,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        }, 4, new Thickness(12, 0, 0, 0));

        var row = new Border
        {
            Child = grid, Padding = new Thickness(8, 5), Margin = new Thickness(6, 0), CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
        };
        ToolTip.SetTip(row, node.FullPath);
        _rowFor[node] = row;
        row.PointerEntered += (_, _) => { if (!_selection.Contains(node)) row.Background = Ui.RowHover; _chart.Highlight = node; };
        row.PointerExited += (_, _) => { Paint(row, node); if (ReferenceEquals(_chart.Highlight, node)) _chart.Highlight = null; };
        row.DoubleTapped += (_, _) => { if (node.IsFolder) Navigate(node); else _platform.OpenFile(node.FullPath); };

        Point? pressAt = null;
        row.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(row);
            var add = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (point.Properties.IsRightButtonPressed)
            {
                if (!_selection.Contains(node)) Select(node, SelectMode.Only);
                ShowMenu(row, e);
                e.Handled = true;
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Select(node, SelectMode.Range);
            else if (add) Select(node, SelectMode.Toggle);
            else if (!_selection.Contains(node)) Select(node, SelectMode.Only);
            pressAt = e.GetPosition(row);
            Focus();
        };
        row.PointerReleased += (_, e) =>
        {
            // A plain click on an already-selected item (not the start of a drag) selects just that one.
            if (pressAt is not null && e.KeyModifiers == KeyModifiers.None && _selection.Count > 1) Select(node, SelectMode.Only);
            pressAt = null;
        };
        row.PointerMoved += async (_, e) =>
        {
            // Drag to the collect tray (inside the window).
            if (pressAt is not { } start) return;
            var now = e.GetPosition(row);
            if (Math.Abs(now.X - start.X) < 6 && Math.Abs(now.Y - start.Y) < 6) return;
            pressAt = null;
            var data = new DataObject();
            data.Set(DragFormat, "items");
            try { await DragDrop.DoDragDrop(e, data, DragDropEffects.Move); }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { Log.Write(ex); }
        };
        Paint(row, node);
        return row;
    }

    private void Paint(Border row, DiskNode node) =>
        row.Background = _selection.Contains(node) ? Ui.AccentSoft : Brushes.Transparent;

    private enum SelectMode { Only, Toggle, Range }

    private void Select(DiskNode node, SelectMode mode, bool reveal = false)
    {
        // Something deeper in the chart: select its row in this folder only if it's directly here.
        switch (mode)
        {
            case SelectMode.Only:
                _selection.Clear();
                _selection.Add(node);
                _anchor = node;
                break;
            case SelectMode.Toggle:
                if (!_selection.Remove(node)) _selection.Add(node);
                _anchor = node;
                break;
            case SelectMode.Range when _current is not null && _anchor is not null:
                var items = Visible();
                int a = IndexOf(items, _anchor), b = IndexOf(items, node);
                if (a < 0 || b < 0) goto case SelectMode.Only;
                _selection.Clear();
                for (var i = Math.Min(a, b); i <= Math.Max(a, b); i++) _selection.Add(items[i]);
                break;
            default:
                goto case SelectMode.Only;
        }
        UpdateSelection();
        if (reveal && _rowFor.TryGetValue(node, out var row)) row.BringIntoView();
    }

    /// <summary>The items in the list, in order.</summary>
    private IReadOnlyList<DiskNode> Visible() => _visible;

    private static int IndexOf(IReadOnlyList<DiskNode> items, DiskNode node)
    {
        for (var i = 0; i < items.Count; i++) if (ReferenceEquals(items[i], node)) return i;
        return -1;
    }

    private void UpdateSelection()
    {
        foreach (var (node, row) in _rowFor) Paint(row, node);
        _chart.SetSelection(_selection);
        _selectionActions.Children.Clear();
        if (_selection.Count == 0)
        {
            _selectionText.Text = "Select items to open, rename, ZIP or remove them";
            _selectionText.Foreground = Ui.Muted;
            _selectionText.FontWeight = FontWeight.Normal;
            return;
        }
        _selectionText.Foreground = Ui.Text;
        _selectionText.FontWeight = FontWeight.Medium;
        var size = DiskScanner.FormatSize(_selection.Sum(n => n.Size));
        _selectionText.Text = _selection.Count == 1 ? $"{_selection[0].Name} · {size}" : $"{_selection.Count} items · {size}";
        if (_selectionText.Text.Length > 48) _selectionText.Text = _selectionText.Text[..30] + "… · " + size;

        var reveal = OperatingSystem.IsMacOS() ? "Show in Finder" : "Show in File Explorer";
        _selectionActions.Children.Add(Ui.Ghost("Open", OpenSelection, IconKind.Eye, "Open (Enter)"));
        _selectionActions.Children.Add(Ui.Ghost("Show", () => _platform.Reveal(_selection[0].FullPath), IconKind.Folder, reveal));
        if (_selection.Count == 1) _selectionActions.Children.Add(Ui.Ghost("Rename", () => _ = RenameAsync(_selection[0]), IconKind.Edit, "Rename… (F2)"));
        _selectionActions.Children.Add(Ui.Ghost("ZIP", () => _ = ZipAsync(), IconKind.Box, "Compress into a ZIP beside it"));
        _selectionActions.Children.Add(Ui.Ghost("Tools", () => _openWheel(Paths(_selection)), IconKind.Wheel, "The Desk Wheel: convert, compress, share…"));
        _selectionActions.Children.Add(Ui.IconButton(IconKind.Copy, "Copy path", () => _platform.CopyText(string.Join(Environment.NewLine, Paths(_selection)))));
        _selectionActions.Children.Add(Ui.Ghost("Collect", () => Collect(_selection.ToList()), IconKind.Plus, "Add to the tray (C), then remove everything collected at once"));
        var trash = Ui.Ghost(OperatingSystem.IsMacOS() ? "Trash" : "Delete", () => _ = TrashSelectionAsync(), IconKind.Trash,
            $"Move to {_platform.TrashName} ({(OperatingSystem.IsMacOS() ? "⌘⌫" : "Delete")}): you can put it back from there");
        trash.Foreground = Ui.Danger;
        _selectionActions.Children.Add(trash);
    }

    private static List<string> Paths(IEnumerable<DiskNode> nodes) => nodes.Select(n => n.FullPath).ToList();

    private void ShowMenu(Control target, PointerEventArgs e)
    {
        var items = new List<MenuItem>();
        MenuItem Item(string header, Action run, bool enabled = true)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled };
            m.Click += (_, _) => run();
            items.Add(m);
            return m;
        }
        var one = _selection.Count == 1 ? _selection[0] : null;
        if (one is { IsFolder: true } folder) Item("Show what's inside", () => Navigate(folder));
        Item("Open", OpenSelection);
        Item(OperatingSystem.IsMacOS() ? "Show in Finder" : "Show in File Explorer", () => _platform.Reveal(_selection[0].FullPath));
        Item("Rename…", () => _ = RenameAsync(one!), one is not null);
        Item("Compress to ZIP", () => _ = ZipAsync());
        Item("Desk Wheel tools…", () => _openWheel(Paths(_selection)));
        Item("Copy path", () => _platform.CopyText(string.Join(Environment.NewLine, Paths(_selection))));
        Item("Collect", () => Collect(_selection.ToList()));
        Item($"Move to {_platform.TrashName}", () => _ = TrashSelectionAsync());
        var menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
        menu.Open(target);
    }

    // ---------------------------------------------------------------- actions

    private void OpenSelection()
    {
        foreach (var node in _selection.Take(10)) _platform.OpenFile(node.FullPath);
    }

    private async Task RenameAsync(DiskNode node)
    {
        var folder = Path.GetDirectoryName(node.FullPath)!;
        var name = await PromptWindow.AskAsync("Rename", $"New name for {node.Name}", node.Name, check: text =>
        {
            var t = text.Trim();
            if (t.Length == 0) return "Type a name.";
            if (t.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || t is "." or "..") return "A name can't contain / \\ : * ? \" < > |";
            if (!string.Equals(t, node.Name, StringComparison.OrdinalIgnoreCase) && (File.Exists(Path.Combine(folder, t)) || Directory.Exists(Path.Combine(folder, t))))
                return "Something in this folder already has that name.";
            return null;
        });
        if (name is null || (name = name.Trim()) == node.Name) return;
        try
        {
            var from = node.FullPath;
            var to = Path.Combine(folder, name);
            if (node.IsFolder) Directory.Move(from, to); else File.Move(from, to);
            node.Rename(name);
            Refresh();
            _status.Text = $"Renamed to {name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Couldn't rename {node.Name}: {ex.Message}";
        }
    }

    private async Task ZipAsync()
    {
        var nodes = _selection.ToList();
        if (nodes.Count == 0) return;
        _status.Text = $"Compressing {nodes.Count} item{(nodes.Count == 1 ? "" : "s")}…";
        _progress.IsVisible = true;
        var result = await Task.Run(() => ArchiveConverter.ZipSelection(Paths(nodes)));
        _progress.IsVisible = _scan is { IsCancellationRequested: false } && _cancel.IsVisible;
        if (!result.Success || result.Outputs.Count == 0)
        {
            _status.Text = "Couldn't make the ZIP: " + result.Error;
            return;
        }
        var zip = result.Outputs[0];
        var info = new FileInfo(zip);
        // The ZIP sits next to the items: add it to the tree if that's the folder on screen.
        if (nodes[0].Parent is { } parent && string.Equals(Path.GetDirectoryName(zip), parent.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            var added = parent.AddFile(info.Name, info.Length, info.LastWriteTime);
            Refresh();
            Select(added, SelectMode.Only, reveal: true);
        }
        var before = nodes.Sum(n => n.Size);
        _status.Text = $"Saved {info.Name} · {DiskScanner.FormatSize(info.Length)} (from {DiskScanner.FormatSize(before)}). Move the originals to the {_platform.TrashName} to free the space.";
    }

    private void Collect(IReadOnlyList<DiskNode> nodes)
    {
        foreach (var n in nodes)
        {
            // Collecting a folder covers what's inside it; collecting something inside a collected folder adds nothing.
            if (_collected.Any(c => n.IsInside(c))) continue;
            _collected.RemoveAll(c => c.IsInside(n));
            _collected.Add(n);
        }
        UpdateTray();
    }

    private void UpdateTray()
    {
        var any = _collected.Count > 0;
        _trayReview.IsVisible = _trayTrash.IsVisible = any;
        _trayText.Text = any
            ? $"{_collected.Count} collected · {DiskScanner.FormatSize(_collected.Sum(c => c.Size))}"
            : "Drop items to collect";
        _trayText.Foreground = any ? Ui.Text : Ui.Muted;
        _trayTrash.Content = $"Move to {_platform.TrashName}";
        _tray.Padding = any ? new Thickness(14, 4, 6, 4) : new Thickness(14, 8);
    }

    private void FillReview()
    {
        var list = new StackPanel { Spacing = 2, MinWidth = 340, MaxHeight = 360 };
        foreach (var node in _collected.OrderByDescending(n => n.Size))
        {
            var remove = Ui.IconButton(IconKind.Close, "Don't remove this", () => { _collected.Remove(node); UpdateTray(); FillReview(); });
            var row = new DockPanel { Margin = new Thickness(4, 2) };
            row.Children.Add(Docked(remove, Dock.Right));
            row.Children.Add(Docked(new TextBlock { Text = DiskScanner.FormatSize(node.Size), Foreground = Ui.Muted, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center }, Dock.Right));
            var name = new TextBlock { Text = node.Name, Foreground = Ui.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(name, node.FullPath);
            row.Children.Add(name);
            list.Children.Add(row);
        }
        if (_collected.Count == 0) list.Children.Add(Ui.Caption("Nothing collected.", wrap: false));
        ((Flyout)_trayReview.Flyout!).Content = new ScrollViewer { Content = list, MaxHeight = 380 };
    }

    private Task TrashSelectionAsync() => TrashAsync(_selection.ToList(), fromTray: false);

    private Task TrashCollectedAsync() => TrashAsync(_collected.ToList(), fromTray: true);

    /// <summary>
    /// Moves to the Trash / Recycle Bin (never deletes for good). The first click asks; a second click within
    /// a few seconds goes ahead.
    /// </summary>
    private async Task TrashAsync(IReadOnlyList<DiskNode> nodes, bool fromTray)
    {
        if (nodes.Count == 0) return;
        var size = DiskScanner.FormatSize(nodes.Sum(n => n.Size));
        var what = nodes.Count == 1 ? nodes[0].Name : $"{nodes.Count} items";
        if (DateTime.UtcNow - _trashArmed > TimeSpan.FromSeconds(5))
        {
            _trashArmed = DateTime.UtcNow;
            _status.Text = $"Move {what} ({size}) to the {_platform.TrashName}? Click again (or press the key again) to confirm.";
            if (fromTray) _trayTrash.Content = "Click to confirm";
            _ = Task.Delay(5000).ContinueWith(_ => Dispatcher.UIThread.Post(UpdateTray));
            return;
        }
        _trashArmed = default;
        _status.Text = $"Moving {what} to the {_platform.TrashName}…";
        var (moved, problem) = await _platform.MoveToTrashAsync(Paths(nodes));
        var gone = nodes.Where(n => moved.Contains(n.FullPath)).ToList();
        foreach (var node in gone)
        {
            node.Remove();
            _selection.Remove(node);
            _collected.RemoveAll(c => ReferenceEquals(c, node) || c.IsInside(node));
        }
        if (_current is not null && _root is not null && !_current.IsInside(_root)) _current = _root;
        UpdateTray();
        Refresh();
        var freed = DiskScanner.FormatSize(gone.Sum(n => n.Size));
        _status.Text = problem is null
            ? $"Moved {what} to the {_platform.TrashName} · {freed}. Empty the {_platform.TrashName} to free the space."
            : $"Moved {gone.Count} of {nodes.Count} · {problem}";
    }

    private void Refresh()
    {
        if (_current is null) return;
        Navigate(_current);
    }

    // ---------------------------------------------------------------- keys

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Back when command && OperatingSystem.IsMacOS():
            case Key.Delete:
                _ = TrashSelectionAsync();
                break;
            case Key.Back:
            case Key.Left when command:
            case Key.Up when command:
                GoUp();
                break;
            case Key.Enter:
            case Key.Down when command:
                if (_selection is [{ IsFolder: true } folder]) Navigate(folder); else OpenSelection();
                break;
            case Key.F2 when _selection.Count == 1:
                _ = RenameAsync(_selection[0]);
                break;
            case Key.C when !command && _selection.Count > 0:
                Collect(_selection.ToList());
                break;
            case Key.A when command && _current is not null:
                _selection.Clear();
                _selection.AddRange(Visible());
                UpdateSelection();
                break;
            case Key.Down or Key.Up when Visible() is { Count: > 0 } items:
                var at = _selection.Count == 0 ? -1 : IndexOf(items, _selection[^1]);
                var next = Math.Clamp(at + (e.Key == Key.Down ? 1 : -1), 0, Math.Min(items.Count, MaxRows) - 1);
                Select(items[next], e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? SelectMode.Range : SelectMode.Only, reveal: true);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- layout helpers

    private static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private static void AddCell(Grid grid, Control c, int column, Thickness? margin = null)
    {
        Grid.SetColumn(c, column);
        if (margin is { } m) c.Margin = m;
        if (c is TextBlock t) t.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(c);
    }

    private static Control Right(Control c)
    {
        c.HorizontalAlignment = HorizontalAlignment.Right;
        return c;
    }
}
