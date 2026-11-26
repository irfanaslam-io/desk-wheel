using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Wheel.App.Platform;
using Wheel.Core.Pdf;

namespace Wheel.App;

/// <summary>
/// The PDF viewer: pages one under another, drawn sharp at any zoom only when they come into view, thumbnails,
/// Find (every match highlighted), page jumps, and Desk Wheel's PDF tools one click away.
/// </summary>
internal sealed class PdfViewerWindow : Window
{
    private const double Gap = 14;

    private readonly IPlatform _platform;
    private readonly Action<IReadOnlyList<string>> _openWheel;
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly StackPanel _pages = new() { Spacing = Gap, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ListBox _thumbs = new() { Width = 150, Background = Brushes.Transparent };
    private readonly Border _thumbPanel;
    private readonly TextBox _pageBox = new() { Width = 46, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pageCount = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted };
    private readonly TextBlock _zoomText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted, Width = 44, TextAlignment = TextAlignment.Center };
    private readonly TextBox _find = Ui.SearchBox("Find");
    private readonly TextBlock _findCount = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted, FontSize = Ui.CaptionSize, MinWidth = 60 };
    private readonly List<PageView> _views = [];
    private readonly DispatcherTimer _renderSoon = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private string _path = "";
    private PdfView? _pdf;
    private double _zoom = 1;   // 1 = fit width
    private bool _fitWidth = true;
    private List<PdfMatch> _matches = [];
    private int _match = -1;
    private CancellationTokenSource? _findCts;

    public PdfViewerWindow(IPlatform platform, Action<IReadOnlyList<string>> openWheel)
    {
        _platform = platform;
        _openWheel = openWheel;
        Ui.Window(this, "PDF");
        Width = 980;
        Height = 820;
        MinWidth = 560;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DragDrop.SetAllowDrop(this, true);

        // ---- toolbar
        var open = Ui.IconButton(IconKind.Folder, "Open a PDF… (" + Cmd("O") + ")", () => _ = PickAsync());
        var thumbs = Ui.IconButton(IconKind.List, "Page thumbnails", () => _thumbPanel!.IsVisible = !_thumbPanel.IsVisible);
        var prev = Ui.IconButton(IconKind.Back, "Previous page (←)", () => GoToPage(CurrentPage() - 1));
        var next = Ui.IconButton(IconKind.Forward, "Next page (→)", () => GoToPage(CurrentPage() + 1));
        var zoomOut = Ui.IconButton(IconKind.ZoomOut, "Zoom out (" + Cmd("−") + ")", () => Zoom(1 / 1.2));
        var zoomIn = Ui.IconButton(IconKind.ZoomIn, "Zoom in (" + Cmd("+") + ")", () => Zoom(1.2));
        var fit = Ui.IconButton(IconKind.Expand, "Fit the width (" + Cmd("0") + ")", FitWidth);
        var tools = Ui.Ghost("Tools", () => { if (_path.Length > 0) _openWheel([_path]); }, IconKind.Wheel, "Desk Wheel's PDF tools: merge, pages, compress, password, searchable…");
        var print = Ui.Ghost("Print", Print, IconKind.Save, OperatingSystem.IsMacOS() ? "Opens it in Preview to print" : "Opens it in Microsoft Edge to print");
        var show = Ui.IconButton(IconKind.Eye, OperatingSystem.IsMacOS() ? "Show in Finder" : "Show in File Explorer", () => { if (_path.Length > 0) _platform.Reveal(_path); });
        _find.Width = 200;
        _find.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { NextMatch(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); e.Handled = true; }
            if (e.Key == Key.Escape) { _find.Text = ""; Focus(); e.Handled = true; }
        };
        _find.TextChanged += (_, _) => _ = FindAsync(_find.Text ?? "");
        _pageBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (int.TryParse(_pageBox.Text, out var n)) GoToPage(n - 1);
            Focus();
            e.Handled = true;
        };

        var bar = new DockPanel { Margin = new Thickness(12, 8), LastChildFill = false };
        bar.Children.Add(Docked(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children = { open, thumbs, Separator(), prev, _pageBox, _pageCount, next, Separator(), zoomOut, _zoomText, zoomIn, fit },
        }, Dock.Left));
        bar.Children.Add(Docked(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { _find, _findCount, tools, print, show },
        }, Dock.Right));

        // ---- thumbnails and pages
        _thumbs.SelectionChanged += (_, _) => { if (_thumbs.SelectedIndex >= 0 && _thumbs.IsKeyboardFocusWithin | _thumbs.IsPointerOver) GoToPage(_thumbs.SelectedIndex); };
        _thumbPanel = new Border
        {
            Child = new ScrollViewer { Content = _thumbs }, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), IsVisible = false,
            Background = Ui.Surface,
        };
        _scroll.Content = _pages;
        _scroll.Background = Ui.IsLight ? new SolidColorBrush(Color.FromRgb(0xE9, 0xEA, 0xEE)) : new SolidColorBrush(Color.FromRgb(0x2A, 0x2B, 0x30));
        _scroll.ScrollChanged += (_, _) => { _renderSoon.Stop(); _renderSoon.Start(); UpdatePageBox(); };
        _scroll.SizeChanged += (_, _) => { if (_fitWidth) Relayout(); };
        _renderSoon.Tick += (_, _) => { _renderSoon.Stop(); RenderVisible(); };

        var body = new DockPanel();
        body.Children.Add(Docked(_thumbPanel, Dock.Left));
        body.Children.Add(_scroll);
        var root = new DockPanel();
        root.Children.Add(Docked(new Border { Child = bar, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) }, Dock.Top));
        root.Children.Add(body);
        Content = root;

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().FirstOrDefault(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) is { } pdf)
                _ = OpenAsync(pdf);
        });
        AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return;
            Zoom(e.Delta.Y > 0 ? 1.1 : 1 / 1.1);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            _findCts?.Cancel();
            foreach (var v in _views) v.Clear();
            _pdf?.Dispose();
        };
    }

    private static string Cmd(string key) => (OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+") + key;

    // ---------------------------------------------------------------- opening

    public async Task OpenAsync(string path, string? password = null)
    {
        PdfView pdf;
        try
        {
            pdf = await Task.Run(() => PdfView.Open(path, password));
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            var typed = await PromptWindow.AskAsync("Password", $"{Path.GetFileName(path)} is protected. Type its password to open it.", password: true);
            if (typed is not null) await OpenAsync(path, typed);
            else if (_pdf is null) Close();
            return;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Write(ex);
            _pageCount.Text = "";
            _pages.Children.Clear();
            _pages.Children.Add(Ui.Body($"Couldn't open {Path.GetFileName(path)}: {ex.Message}"));
            return;
        }
        foreach (var v in _views) v.Clear();
        _pdf?.Dispose();
        _pdf = pdf;
        _path = path;
        Title = Path.GetFileName(path) + " · Desk Wheel";
        _views.Clear();
        _pages.Children.Clear();
        for (var i = 0; i < pdf.PageCount; i++)
        {
            var view = new PageView(i, pdf.PageSize(i));
            _views.Add(view);
            _pages.Children.Add(view.Root);
        }
        _pageCount.Text = $"of {pdf.PageCount}";
        _matches = [];
        _match = -1;
        _findCount.Text = "";
        FillThumbs();
        _fitWidth = true;
        Relayout();
        _scroll.Offset = default;
        if (!string.IsNullOrWhiteSpace(_find.Text)) _ = FindAsync(_find.Text);
    }

    private async Task PickAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a PDF", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await OpenAsync(path);
    }

    // ---------------------------------------------------------------- layout and drawing

    /// <summary>Sizes every page for the zoom (fit width: the widest page fills the window).</summary>
    private void Relayout()
    {
        if (_pdf is null || _views.Count == 0) return;
        var widest = _views.Max(v => v.Size.Width);
        var available = Math.Max(200, _scroll.Bounds.Width - 48 - 20);
        if (_fitWidth) _zoom = available / widest;
        _zoom = Math.Clamp(_zoom, 0.1, 8);
        foreach (var v in _views) v.SetScale(_zoom);
        _zoomText.Text = $"{Math.Round(_zoom * 100 * 72 / 96)}%";
        _renderSoon.Stop();
        _renderSoon.Start();
    }

    private void Zoom(double factor)
    {
        if (_pdf is null) return;
        var page = CurrentPage();
        _fitWidth = false;
        _zoom *= factor;
        Relayout();
        Dispatcher.UIThread.Post(() => GoToPage(page), DispatcherPriority.Background);
    }

    private void FitWidth()
    {
        var page = CurrentPage();
        _fitWidth = true;
        Relayout();
        Dispatcher.UIThread.Post(() => GoToPage(page), DispatcherPriority.Background);
    }

    /// <summary>Draws the pages in view (and one either side); forgets the far ones to save memory.</summary>
    private void RenderVisible()
    {
        if (_pdf is not { } pdf) return;
        var top = _scroll.Offset.Y;
        var bottom = top + _scroll.Viewport.Height;
        var scaling = RenderScaling;
        var y = 24.0;
        for (var i = 0; i < _views.Count; i++)
        {
            var v = _views[i];
            var h = v.Root.Height;
            var near = y + h >= top - h && y <= bottom + h;
            if (near) v.Render(pdf, scaling);
            else if (y + h < top - 4 * h || y > bottom + 4 * h) v.Clear();
            y += h + Gap;
        }
        ShowMatches();
    }

    private int CurrentPage()
    {
        var middle = _scroll.Offset.Y + _scroll.Viewport.Height / 3;
        var y = 24.0;
        for (var i = 0; i < _views.Count; i++)
        {
            y += _views[i].Root.Height + Gap;
            if (y > middle) return i;
        }
        return Math.Max(0, _views.Count - 1);
    }

    private void GoToPage(int page)
    {
        if (_views.Count == 0) return;
        page = Math.Clamp(page, 0, _views.Count - 1);
        var y = 24.0;
        for (var i = 0; i < page; i++) y += _views[i].Root.Height + Gap;
        _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, y - 12));
        UpdatePageBox();
    }

    private void UpdatePageBox()
    {
        if (_pageBox.IsFocused) return;
        var page = CurrentPage();
        _pageBox.Text = (page + 1).ToString();
        if (_thumbs.SelectedIndex != page && !_thumbs.IsPointerOver) _thumbs.SelectedIndex = page;
    }

    private void FillThumbs()
    {
        var items = new List<Control>();
        if (_pdf is not { } pdf) return;
        for (var i = 0; i < pdf.PageCount; i++)
        {
            var (w, h) = pdf.PageSize(i);
            var image = new Image { Width = 110, Height = 110 * h / Math.Max(1, w), Stretch = Stretch.Uniform };
            var index = i;
            items.Add(new StackPanel
            {
                Spacing = 3, Margin = new Thickness(0, 4),
                Children =
                {
                    new Border { Child = image, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, BoxShadow = BoxShadows.Parse("0 1 3 0 #40000000") },
                    new TextBlock { Text = (i + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center, FontSize = Ui.CaptionSize, Foreground = Ui.Muted },
                },
            });
            // Thumbnails are drawn in the background, a few at a time.
            _ = Task.Run(() =>
            {
                try
                {
                    var tw = 110 * 2;
                    var th = (int)Math.Max(1, tw * h / Math.Max(1, w));
                    var pixels = pdf.Render(index, tw, th);
                    Dispatcher.UIThread.Post(() => image.Source = PageView.ToBitmap(pixels, tw, th));
                }
                catch (Exception ex) when (ex is InvalidDataException or ObjectDisposedException) { }
            });
            if (i >= 400) break; // very long documents: thumbnails for the first pages only
        }
        _thumbs.ItemsSource = items;
    }

    // ---------------------------------------------------------------- find

    private async Task FindAsync(string query)
    {
        _findCts?.Cancel();
        var cts = new CancellationTokenSource();
        _findCts = cts;
        if (_pdf is not { } pdf || query.Trim().Length < 2)
        {
            _matches = [];
            _match = -1;
            _findCount.Text = "";
            ShowMatches();
            return;
        }
        await Task.Delay(200, CancellationToken.None); // wait for typing to pause
        if (cts.IsCancellationRequested) return;
        var found = await Task.Run(() => { try { return pdf.Find(query.Trim()); } catch (ObjectDisposedException) { return []; } });
        if (cts.IsCancellationRequested) return;
        _matches = found;
        _match = -1;
        if (found.Count == 0)
        {
            var scanned = Enumerable.Range(0, Math.Min(pdf.PageCount, 3)).All(p => pdf.PageText(p).Trim().Length == 0);
            _findCount.Text = scanned ? "No text (scanned)" : "Not found";
            ToolTip.SetTip(_findCount, scanned ? "This PDF's pages are pictures. Tools → Searchable makes its text findable." : null);
            ShowMatches();
            return;
        }
        NextMatch(1);
    }

    private void NextMatch(int step)
    {
        if (_matches.Count == 0) return;
        _match = ((_match + step) % _matches.Count + _matches.Count) % _matches.Count;
        _findCount.Text = $"{_match + 1} of {_matches.Count}";
        var m = _matches[_match];
        // Scroll so the match sits a third of the way down.
        var y = 24.0;
        for (var i = 0; i < m.Page; i++) y += _views[i].Root.Height + Gap;
        var v = _views[m.Page];
        var within = m.Boxes.Count > 0 ? m.Boxes[0].Y * v.Root.Height : 0;
        _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, y + within - _scroll.Viewport.Height / 3));
        ShowMatches();
    }

    private void ShowMatches()
    {
        for (var i = 0; i < _views.Count; i++)
            _views[i].Highlight(_matches.Where(m => m.Page == i).ToList(), _match >= 0 && _match < _matches.Count ? _matches[_match] : null);
    }

    // ---------------------------------------------------------------- other

    private void Print()
    {
        if (_path.Length == 0) return;
        try
        {
            if (OperatingSystem.IsMacOS()) Process.Start("/usr/bin/open", ["-a", "Preview", _path])?.Dispose();
            else if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("msedge", $"\"{_path}\"") { UseShellExecute = true })?.Dispose();
            else _platform.OpenFile(_path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Write(ex);
            _platform.OpenFile(_path);
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (command && e.Key == Key.F) { _find.Focus(); _find.SelectAll(); e.Handled = true; return; }
        if (command && e.Key == Key.O) { _ = PickAsync(); e.Handled = true; return; }
        if (command && e.Key is Key.OemPlus or Key.Add) { Zoom(1.2); e.Handled = true; return; }
        if (command && e.Key is Key.OemMinus or Key.Subtract) { Zoom(1 / 1.2); e.Handled = true; return; }
        if (command && e.Key is Key.D0 or Key.NumPad0) { FitWidth(); e.Handled = true; return; }
        if (_find.IsFocused || _pageBox.IsFocused) return;
        switch (e.Key)
        {
            case Key.Left or Key.PageUp when !command: GoToPage(CurrentPage() - 1); break;
            case Key.Right or Key.PageDown when !command: GoToPage(CurrentPage() + 1); break;
            case Key.Home: GoToPage(0); break;
            case Key.End: GoToPage(_views.Count - 1); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    private static Control Separator() => new Border { Width = 1, Height = 20, Background = Ui.Line, Margin = new Thickness(6, 0) };

    private static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    /// <summary>One page: white paper with a shadow, its picture once drawn, and a layer for Find highlights.</summary>
    private sealed class PageView
    {
        private readonly Image _image = new() { Stretch = Stretch.Fill };
        private readonly Canvas _marks = new() { IsHitTestVisible = false };
        private int _drawnWidth;
        private bool _drawing;

        public PageView(int index, (double Width, double Height) size)
        {
            Index = index;
            Size = size;
            Root = new Border
            {
                Background = Brushes.White, BoxShadow = BoxShadows.Parse("0 2 8 0 #40000000"),
                Child = new Grid { Children = { _image, _marks } },
            };
        }

        public int Index { get; }
        public (double Width, double Height) Size { get; }
        public Border Root { get; }

        public void SetScale(double zoom)
        {
            // Points → screen units: 72 points per inch, Avalonia's units are 96 per inch.
            Root.Width = Size.Width * zoom;
            Root.Height = Size.Height * zoom;
        }

        /// <summary>Draws the page sharp for its size on screen (again only if the size changed a lot).</summary>
        public void Render(PdfView pdf, double scaling)
        {
            var width = (int)Math.Min(5000, Math.Round(Root.Width * scaling));
            var height = (int)Math.Min(7000, Math.Round(Root.Height * scaling));
            if (width < 8 || height < 8 || _drawing || Math.Abs(width - _drawnWidth) < 2) return;
            _drawing = true;
            Task.Run(() =>
            {
                try
                {
                    var pixels = pdf.Render(Index, width, height);
                    Dispatcher.UIThread.Post(() =>
                    {
                        var old = _image.Source as IDisposable;
                        _image.Source = ToBitmap(pixels, width, height);
                        old?.Dispose();
                        _drawnWidth = width;
                        _drawing = false;
                    });
                }
                catch (Exception ex) when (ex is InvalidDataException or ObjectDisposedException)
                {
                    Dispatcher.UIThread.Post(() => _drawing = false);
                }
            });
        }

        public void Clear()
        {
            if (_image.Source is IDisposable d) d.Dispose();
            _image.Source = null;
            _drawnWidth = 0;
        }

        public void Highlight(List<PdfMatch> matches, PdfMatch? current)
        {
            _marks.Children.Clear();
            foreach (var m in matches)
                foreach (var (x, y, w, h) in m.Boxes)
                {
                    var r = new Border
                    {
                        Width = Math.Max(2, w * Root.Width), Height = Math.Max(2, h * Root.Height), CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush(ReferenceEquals(m, current) ? Color.FromArgb(0x90, 0xFF, 0x8A, 0x00) : Color.FromArgb(0x70, 0xFF, 0xE0, 0x00)),
                    };
                    Canvas.SetLeft(r, x * Root.Width);
                    Canvas.SetTop(r, y * Root.Height);
                    _marks.Children.Add(r);
                }
        }

        public static WriteableBitmap ToBitmap(byte[] bgra, int width, int height)
        {
            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using var fb = bitmap.Lock();
            if (fb.RowBytes == width * 4) Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
            else for (var y = 0; y < height; y++) Marshal.Copy(bgra, y * width * 4, fb.Address + y * fb.RowBytes, width * 4);
            return bitmap;
        }
    }
}
