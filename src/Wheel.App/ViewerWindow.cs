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
using Wheel.Core.Files;

namespace Wheel.App;

/// <summary>
/// Picture viewer: steps through the pictures in a folder with Next and Back (arrow keys, buttons, or the
/// sides of the picture), zooms with the mouse wheel, pans by dragging, and goes full screen.
/// Opened from the Tools wheel ("View"), or by "Open with › Desk Wheel" in File Explorer.
/// </summary>
internal sealed class ViewerWindow : Window
{
    private const double MaxZoom = 16;

    private readonly IPlatform _platform;
    private readonly Func<string, Task> _edit;
    private readonly List<string> _pictures;
    private int _index;

    private readonly Canvas _stage = new() { ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14)) };
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly TextBlock _name = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _info = Ui.Caption("", wrap: false);
    private readonly TextBlock _zoomText = new() { MinWidth = 46, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
    private readonly TextBlock _message = new() { Foreground = Brushes.White, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _prev, _next;
    private readonly Border _bar;

    private readonly Dictionary<string, Bitmap> _cache = new(StringComparer.OrdinalIgnoreCase);
    private Bitmap? _shown;
    private double _zoom = 1;
    private bool _fit = true;
    private Vector _pan;
    private Point? _dragFrom;
    private Vector _panAtDrag;
    private int _loadVersion;

    public ViewerWindow(IReadOnlyList<string> pictures, int start, IPlatform platform, Func<string, Task> edit)
    {
        _platform = platform;
        _edit = edit;
        _pictures = pictures.ToList();
        _index = Math.Clamp(start, 0, Math.Max(0, _pictures.Count - 1));
        Ui.Window(this, "Desk Wheel");
        Width = 1100;
        Height = 760;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _prev = SideButton(IconKind.Back, "Previous picture (←)", () => Go(-1));
        _next = SideButton(IconKind.Forward, "Next picture (→)", () => Go(1));
        _stage.Children.Add(_image);
        var overlay = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        _prev.Margin = new Thickness(14, 0);
        _next.Margin = new Thickness(14, 0);
        Grid.SetColumn(_next, 2);
        overlay.Children.Add(_prev);
        overlay.Children.Add(_next);
        var stageArea = new Panel();
        stageArea.Children.Add(_stage);
        stageArea.Children.Add(_message);
        stageArea.Children.Add(overlay);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(Ui.IconButton(IconKind.ZoomOut, "Zoom out (−)", () => ZoomBy(1 / 1.25)));
        tools.Children.Add(_zoomText);
        tools.Children.Add(Ui.IconButton(IconKind.ZoomIn, "Zoom in (+)", () => ZoomBy(1.25)));
        tools.Children.Add(Ui.IconButton(IconKind.Image, "Fit to window (0) · actual size (1)", ToggleFit));
        tools.Children.Add(Ui.IconButton(IconKind.Edit, "Edit and mark up (E)", () => _ = EditCurrent()));
        tools.Children.Add(Ui.IconButton(IconKind.Copy, "Copy the picture (Ctrl+C)", () => _ = CopyCurrent()));
        tools.Children.Add(Ui.IconButton(IconKind.Folder, $"Show in {platform.FileManagerName}", () => { if (Current is { } p) _platform.Reveal(p); }));
        tools.Children.Add(Ui.IconButton(IconKind.Expand, "Full screen (F11)", ToggleFullScreen));
        foreach (var b in tools.Children.OfType<Button>()) b.Focusable = false; // keys stay with the viewer

        var title = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(_name);
        title.Children.Add(_info);
        var barGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        barGrid.Children.Add(title);
        Grid.SetColumn(tools, 1);
        barGrid.Children.Add(tools);
        _bar = new Border { Padding = new Thickness(14, 8), Child = barGrid, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };

        var root = new DockPanel();
        DockPanel.SetDock(_bar, Dock.Top);
        root.Children.Add(_bar);
        root.Children.Add(stageArea);
        Content = root;

        _stage.SizeChanged += (_, _) => Layout();
        _stage.PointerWheelChanged += OnWheel;
        _stage.PointerPressed += OnPressed;
        _stage.PointerMoved += OnMoved;
        _stage.PointerReleased += (_, e) => { _dragFrom = null; e.Pointer.Capture(null); UpdateCursor(); };
        _stage.DoubleTapped += (_, _) => ToggleFit();
        // Tunnel: before focus navigation, which would otherwise take the arrow keys.
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += (_, _) => _ = ShowCurrent();
        Closed += (_, _) =>
        {
            _image.Source = null;
            foreach (var b in _cache.Values) b.Dispose();
            _cache.Clear();
        };
    }

    private string? Current => _pictures.Count > 0 ? _pictures[_index] : null;

    private static Button SideButton(IconKind icon, string tip, Action run)
    {
        var b = new Button
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x1B, 0x1C, 0x20)), BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Focusable = false,
            Content = Ui.Icon(icon, 22, Brushes.White),
        };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => run();
        return b;
    }

    // ---------- pictures ----------

    private void Go(int step)
    {
        if (_pictures.Count < 2) return;
        _index = (_index + step + _pictures.Count) % _pictures.Count; // wraps round, like Photos
        _ = ShowCurrent();
    }

    private void GoTo(int index)
    {
        if (_pictures.Count == 0) return;
        _index = Math.Clamp(index, 0, _pictures.Count - 1);
        _ = ShowCurrent();
    }

    private async Task ShowCurrent()
    {
        var version = ++_loadVersion;
        _prev.IsVisible = _next.IsVisible = _pictures.Count > 1;
        if (Current is not { } path)
        {
            _name.Text = "No pictures";
            _info.Text = "";
            _message.Text = "There are no pictures here.";
            _image.Source = null;
            return;
        }
        _name.Text = Path.GetFileName(path);
        Title = $"{Path.GetFileName(path)} · Desk Wheel";
        _info.Text = $"{_index + 1} of {_pictures.Count} · loading…";
        _message.Text = "";

        var bitmap = await Load(path);
        if (version != _loadVersion) return; // moved on while loading
        if (bitmap is null)
        {
            _image.Source = null;
            _shown = null;
            _message.Text = $"Can't show {Path.GetFileName(path)}.\nIt may be damaged, or in a format this computer can't read.";
            _info.Text = $"{_index + 1} of {_pictures.Count}";
            return;
        }
        _shown = bitmap;
        _image.Source = bitmap;
        _fit = true;
        _pan = default;
        Layout();
        long size = 0;
        try { size = new FileInfo(path).Length; } catch (IOException) { }
        _info.Text = $"{_index + 1} of {_pictures.Count} · {bitmap.PixelSize.Width} × {bitmap.PixelSize.Height} · {Size(size)}";

        // Have the neighbours ready, and forget pictures far away.
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
        if (_pictures.Count > 1)
        {
            keep.Add(_pictures[(_index + 1) % _pictures.Count]);
            keep.Add(_pictures[(_index - 1 + _pictures.Count) % _pictures.Count]);
        }
        foreach (var old in _cache.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            if (!ReferenceEquals(_cache[old], _image.Source)) _cache[old].Dispose();
            _cache.Remove(old);
        }
        foreach (var p in keep) if (!_cache.ContainsKey(p)) _ = Load(p);
    }

    private async Task<Bitmap?> Load(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;
        try
        {
            using var decoded = await Task.Run(() => ImageEngine.LoadForEditingAsync(path));
            var bitmap = EditorWindow.ToAvalonia(decoded);
            if (_cache.TryGetValue(path, out var raced)) { bitmap.Dispose(); return raced; }
            _cache[path] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Write(ex);
            return null;
        }
    }

    // ---------- zoom and pan ----------

    private double FitZoom =>
        _shown is null || _stage.Bounds.Width < 1 ? 1
        : Math.Min(1, Math.Min(_stage.Bounds.Width / _shown.Size.Width, _stage.Bounds.Height / _shown.Size.Height));

    private void Layout()
    {
        if (_shown is null) return;
        if (_fit) { _zoom = FitZoom; _pan = default; }
        var w = _shown.Size.Width * _zoom;
        var h = _shown.Size.Height * _zoom;
        // Keep the picture on screen: centred when smaller than the window, edges never past the middle.
        var maxX = Math.Max(0, (w - _stage.Bounds.Width) / 2);
        var maxY = Math.Max(0, (h - _stage.Bounds.Height) / 2);
        _pan = new Vector(Math.Clamp(_pan.X, -maxX, maxX), Math.Clamp(_pan.Y, -maxY, maxY));
        _image.Width = w;
        _image.Height = h;
        Canvas.SetLeft(_image, (_stage.Bounds.Width - w) / 2 + _pan.X);
        Canvas.SetTop(_image, (_stage.Bounds.Height - h) / 2 + _pan.Y);
        _zoomText.Text = $"{Math.Round(_zoom * 100)}%";
        UpdateCursor();
    }

    private void ZoomBy(double factor, Point? at = null)
    {
        if (_shown is null) return;
        var old = _zoom;
        _zoom = Math.Clamp(_zoom * factor, Math.Min(FitZoom, 0.05), MaxZoom);
        _fit = false;
        if (at is { } p)
        {
            // Zoom around the pointer: the spot under it stays under it.
            var centre = new Point(_stage.Bounds.Width / 2 + _pan.X, _stage.Bounds.Height / 2 + _pan.Y);
            var k = _zoom / old;
            _pan = new Vector(p.X - (p.X - centre.X) * k - _stage.Bounds.Width / 2, p.Y - (p.Y - centre.Y) * k - _stage.Bounds.Height / 2);
        }
        Layout();
    }

    private void ToggleFit()
    {
        if (_shown is null) return;
        if (_fit) { _fit = false; _zoom = 1; _pan = default; }
        else _fit = true;
        Layout();
    }

    private bool CanPan => _shown is not null && (_image.Width > _stage.Bounds.Width + 1 || _image.Height > _stage.Bounds.Height + 1);

    private void UpdateCursor() =>
        _stage.Cursor = new Cursor(CanPan ? (_dragFrom is null ? StandardCursorType.Hand : StandardCursorType.SizeAll) : StandardCursorType.Arrow);

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        ZoomBy(e.Delta.Y > 0 ? 1.15 : 1 / 1.15, e.GetPosition(_stage));
        e.Handled = true;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_stage);
        if (point.Properties.IsXButton1Pressed) { Go(-1); return; } // mouse back / forward buttons
        if (point.Properties.IsXButton2Pressed) { Go(1); return; }
        if (!point.Properties.IsLeftButtonPressed || !CanPan) return;
        _dragFrom = e.GetPosition(_stage);
        _panAtDrag = _pan;
        e.Pointer.Capture(_stage);
        UpdateCursor();
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from) return;
        var p = e.GetPosition(_stage);
        _pan = _panAtDrag + (p - from);
        Layout();
    }

    // ---------- actions ----------

    private async Task EditCurrent()
    {
        if (Current is { } p) await _edit(p);
    }

    private async Task CopyCurrent()
    {
        if (Current is not { } p) return;
        try
        {
            await _platform.CopyImageAsync(p);
            _info.Text = $"{_index + 1} of {_pictures.Count} · copied";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Log.Write(ex);
        }
    }

    private void ToggleFullScreen()
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        _bar.IsVisible = WindowState != WindowState.FullScreen;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.Right or Key.Down or Key.PageDown or Key.Space: Go(1); break;
            case Key.Left or Key.Up or Key.PageUp or Key.Back: Go(-1); break;
            case Key.Home: GoTo(0); break;
            case Key.End: GoTo(_pictures.Count - 1); break;
            case Key.Escape:
                if (WindowState == WindowState.FullScreen) ToggleFullScreen(); else Close();
                break;
            case Key.F11 or Key.F: ToggleFullScreen(); break;
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Alt): ToggleFullScreen(); break;
            case Key.C when command: _ = CopyCurrent(); break;
            case Key.E: _ = EditCurrent(); break;
            case Key.D0 or Key.NumPad0: _fit = true; Layout(); break;
            case Key.D1 or Key.NumPad1: _fit = false; _zoom = 1; _pan = default; Layout(); break;
            case Key.OemPlus or Key.Add: ZoomBy(1.25); break;
            case Key.OemMinus or Key.Subtract: ZoomBy(1 / 1.25); break;
            case Key.W when command: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    private static string Size(long bytes) =>
        bytes < 1024 * 1024 ? $"{bytes / 1024.0:0} KB" : $"{bytes / 1024.0 / 1024:0.#} MB";
}
