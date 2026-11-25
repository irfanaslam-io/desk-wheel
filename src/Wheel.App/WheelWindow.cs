using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Wheel.App.Platform;
using Wheel.Core;

namespace Wheel.App;

/// <summary>
/// The radial menu, shared by macOS and Windows. Two ways in:
/// drag mode (opened mid-drag; the window is the drop target, so Finder/Explorer deliver the files here)
/// and click mode (opened with files already known; pick with the mouse or arrow keys).
/// Hover is worked out from the pointer's angle and distance from the centre.
/// </summary>
internal sealed class WheelWindow : Window
{
    private const double WheelSize = 380;
    private const double Centre = WheelSize / 2;
    private const double InnerRadius = 74;
    private const double OuterRadius = 176;
    private const double LabelRadius = 126;

    private static readonly IBrush SegmentFill = Ui.WheelSegment;
    private static readonly IBrush SegmentStroke = Ui.WheelStroke;
    private static readonly IBrush CentreFill = Ui.WheelCentre;
    private static readonly IBrush TextMain = Ui.WheelText;
    private static readonly IBrush TextQuiet = Ui.WheelTextQuiet;
    // Alpha 1: invisible, but the whole disk catches drops (fully transparent pixels can let them fall through).
    private static readonly IBrush HitDisk = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0));

    private readonly Canvas _canvas = new() { Width = WheelSize, Height = WheelSize };
    private readonly List<Path> _segments = new();
    private readonly IPlatform _platform;
    private readonly Capabilities _caps;
    private readonly bool _clickMode;
    private readonly DispatcherTimer _watch;
    private readonly DispatcherTimer _leaveTimer = new();
    private IReadOnlyList<string> _paths;
    private IReadOnlyList<WheelOption> _options = Array.Empty<WheelOption>(); // the segments on the current page
    private int _page;
    private readonly DispatcherTimer _pageFlip = new() { Interval = TimeSpan.FromMilliseconds(650) };

    /// <summary>Option ids pinned to the front of their wheel. Set by the app from settings.</summary>
    public Func<IReadOnlyList<string>> Pinned { get; set; } = () => Array.Empty<string>();

    /// <summary>Pins or unpins an option (right-click a segment). Set by the app.</summary>
    public Action<string>? TogglePin { get; set; }
    private IReadOnlyList<string>? _batchFor;          // the selection _batch was worked out for
    private FolderContents.Batch? _batch;              // a folder selection's files, used by Convert and Tools
    private WheelMode _mode;

    /// <summary>The wheel showing now (Convert, Tools, Capture…).</summary>
    public WheelMode Mode => _mode;
    private int? _hover;
    private bool _nonFileDrag;
    private DateTime? _buttonUpAt;
    private bool _closing;
    private TextBlock? _hint;

    /// <summary>Raised after the window has closed, so a slow job never holds up the drag.</summary>
    public event Action<WheelOption, IReadOnlyList<string>>? OptionChosen;

    public bool HasChoice { get; private set; }

    /// <summary>
    /// Asked when Convert / Tools / Actions opens with no files: returns the files selected in Finder / Explorer.
    /// Null or empty leaves the wheel waiting for a dropped file.
    /// </summary>
    public Func<Task<string[]?>>? RequestSelection { get; set; }

    /// <summary>Moves to another wheel, picking up the Finder / Explorer selection when it needs files.</summary>
    private async Task SwitchToAsync(WheelMode mode)
    {
        if (mode != WheelMode.Capture && _paths.Count == 0 && RequestSelection is not null)
        {
            try
            {
                if (await RequestSelection() is { Length: > 0 } files)
                    _paths = files;
                    _page = 0;
            }
            catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException)
            {
                Log.Write(ex);
            }
        }
        if (!_closing)
            SetMode(mode);
    }

    public WheelWindow(IPlatform platform, Capabilities caps, WheelMode mode, IReadOnlyList<string> paths, bool clickMode)
    {
        _platform = platform;
        _caps = caps;
        _mode = mode;
        _paths = paths;
        _clickMode = clickMode;

        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = clickMode;   // drag mode must never steal focus from Finder/Explorer
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = WheelSize;
        Height = WheelSize;
        Title = "Desk Wheel";
        Content = _canvas;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) =>
        {
            SetHover(null);
            if (_leaveTimer.IsEnabled || _leaveArmed)
            {
                // Moving between parts of the wheel also raises "leave": only close if the drag stays away.
                _leaveTimer.Stop();
                _leaveTimer.Interval = TimeSpan.FromMilliseconds(900);
                _leaveTimer.Start();
            }
        });
        AddHandler(DragDrop.DropEvent, OnDrop);

        if (clickMode)
        {
            PointerMoved += (_, e) => SetHover(HitTest(e.GetPosition(_canvas)));
            PointerReleased += (_, e) =>
            {
                var point = e.GetPosition(_canvas);
                if (IsInCentre(point) && e.InitialPressMouseButton is MouseButton.Left or MouseButton.Right)
                    _ = SwitchToAsync(NextMode()); // centre: Screenshot → Convert → Tools → Actions
                else if (e.InitialPressMouseButton == MouseButton.Right)
                    PinAt(HitTest(point)); // right-click a segment: pin it to the front of this wheel
                else if (e.InitialPressMouseButton == MouseButton.Left)
                    ChooseAt(HitTest(point));
            };
            KeyDown += OnKeyDown;
            Deactivated += (_, _) => CloseWheel(); // clicked somewhere else
        }

        _watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _watch.Tick += WatchMouseButton;
        _leaveTimer.Tick += (_, _) => CloseWheel();
        // While dragging a file, resting on "More ›" / "‹ Back" turns the page (you can't click mid-drag).
        _pageFlip.Tick += (_, _) =>
        {
            _pageFlip.Stop();
            if (_hover is int h && h < _options.Count && _options[h].Engine == EngineKind.Navigation)
                TurnPage(_options[h].Id);
        };
        Closed += (_, _) => { _watch.Stop(); _leaveTimer.Stop(); _pageFlip.Stop(); };

        Render();
    }

    /// <summary>Shows the wheel centred on the pointer (key gesture, hotkey).</summary>
    public void ShowAtCursor() => ShowAt(_platform.CursorPixel(Screens), closeWhenButtonUp: true);

    /// <summary>
    /// Shows the wheel centred on a screen point, kept inside that screen's work area.
    /// <paramref name="closeWhenButtonUp"/>: drag mode closes when the mouse button is released;
    /// otherwise (opened from the floating button) it closes when the drag leaves it and doesn't come back.
    /// </summary>
    public void ShowAt(PixelPoint centre, bool closeWhenButtonUp)
    {
        var screen = Screens.ScreenFromPoint(centre) ?? Screens.Primary;
        var scale = screen?.Scaling ?? 1;
        var size = (int)(WheelSize * scale);
        var x = centre.X - size / 2;
        var y = centre.Y - size / 2;
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            x = Math.Max(area.X, Math.Min(x, area.Right - size));
            y = Math.Max(area.Y, Math.Min(y, area.Bottom - size));
        }
        Position = new PixelPoint(x, y);
        Show();

        if (_clickMode)
        {
            _platform.BringToFront();
            Activate();
        }
        else if (closeWhenButtonUp)
        {
            _watch.Start();
        }
        else
        {
            // No drag arriving within 3 s (or leaving for good) closes the wheel.
            _leaveTimer.Interval = TimeSpan.FromSeconds(3);
            _leaveTimer.Start();
        }
    }

    /// <summary>Switch wheels (another key tapped while open).</summary>
    public void SetMode(WheelMode mode)
    {
        if (mode == _mode) return;
        _mode = mode;
        _hover = null;
        _page = 0;
        Render();
    }

    public void CloseWheel()
    {
        if (_closing) return;
        _closing = true;
        _watch.Stop();
        _leaveTimer.Stop();
        Close();
    }

    // ---------- drag and drop ----------

    private bool _leaveArmed;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_leaveTimer.IsEnabled)
        {
            _leaveTimer.Stop();
            _leaveArmed = true; // opened from the floating button: watch for the drag leaving
        }

        if (_paths.Count == 0 && !_nonFileDrag)
        {
            var files = e.Data.Contains(DataFormats.Files)
                ? e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray()
                : null;
            if (files is { Length: > 0 })
                _paths = files;
            else
                _nonFileDrag = true;
            Log.Trace($"  Drag reached the wheel: {(files is { Length: > 0 } ? $"{files.Length} file(s)" : "no files in it")}, offered effects: {e.DragEffects}");
            Render();
        }

        SetHover(HitTest(e.GetPosition(_canvas)));
        e.DragEffects = _hover is null ? DragDropEffects.None : SafeEffect(e.DragEffects);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var index = HitTest(e.GetPosition(_canvas));
        e.DragEffects = index is null ? DragDropEffects.None : SafeEffect(e.DragEffects);
        ChooseAt(index);
    }

    /// <summary>
    /// Never answer Move: the drag source would then delete the original.
    /// Copy (or Link) tells it to leave its file alone; Wheel only reads it.
    /// </summary>
    private static DragDropEffects SafeEffect(DragDropEffects offered) =>
        offered.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
        : offered.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : DragDropEffects.Copy; // some sources offer nothing explicit; Copy is always safe

    /// <summary>Drag mode: close once the button is up and no drop arrived (dropped elsewhere or cancelled).</summary>
    private void WatchMouseButton(object? sender, EventArgs e)
    {
        if (_platform.IsPrimaryButtonDown())
        {
            _buttonUpAt = null;
            return;
        }
        _buttonUpAt ??= DateTime.UtcNow;
        // Grace period: the drop can arrive a moment after the button goes up.
        if (DateTime.UtcNow - _buttonUpAt.Value > TimeSpan.FromMilliseconds(400))
            CloseWheel();
    }

    private void TurnPage(string navId)
    {
        _page = Math.Max(0, navId == OptionRegistry.MoreId ? _page + 1 : _page - 1);
        _hover = null;
        Render();
    }

    private void PinAt(int? index)
    {
        if (index is not int i || i >= _options.Count || _options[i].Engine == EngineKind.Navigation || TogglePin is null) return;
        TogglePin(_options[i].Id);
        Render();
    }

    private void ChooseAt(int? index)
    {
        if (index is int n && n < _options.Count && _options[n].Engine == EngineKind.Navigation)
        {
            TurnPage(_options[n].Id);
            return;
        }
        WheelOption? option = index is int i && i < _options.Count && (_paths.Count > 0 || _mode == WheelMode.Capture) ? _options[i] : null;
        var paths = option is null ? _paths : TargetsFor(option);
        HasChoice = option is not null;
        _watch.Stop();
        Opacity = 0; // feels instant; the real close waits until we're out of the drop callback

        Dispatcher.UIThread.Post(() =>
        {
            CloseWheel();
            if (option is not null)
                OptionChosen?.Invoke(option, paths);
        });
    }

    // ---------- keyboard (click mode) ----------

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_platform.ModeForKey(e.Key) is { } mode)
        {
            _ = SwitchToAsync(mode);
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Escape: CloseWheel(); break;
            case Key.Right or Key.Down or Key.Tab: Step(+1); break;
            case Key.Left or Key.Up: Step(-1); break;
            case Key.Enter: ChooseAt(_hover); break; // Return is the same key in Avalonia
            default: return;
        }
        e.Handled = true;
    }

    private void Step(int delta)
    {
        if (_options.Count == 0) return;
        SetHover(_hover is int h ? (h + delta + _options.Count) % _options.Count : 0);
    }

    // ---------- geometry ----------

    /// <summary>Segment under a point, or null for the centre (cancel) and outside the ring.</summary>
    private int? HitTest(Point p)
    {
        var n = _options.Count;
        if (n == 0) return null;
        double dx = p.X - Centre, dy = p.Y - Centre;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < InnerRadius || r > OuterRadius + 4) return null;

        var step = 360.0 / n;
        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;   // 0 = right, 90 = down (y grows downward)
        var fromTop = angle + 90 + step / 2;                // segment 0 is centred at the top
        fromTop = ((fromTop % 360) + 360) % 360;
        return (int)(fromTop / step) % n;
    }

    private static bool IsInCentre(Point p)
    {
        double dx = p.X - Centre, dy = p.Y - Centre;
        return dx * dx + dy * dy < InnerRadius * InnerRadius;
    }

    private static Point Polar(double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(Centre + radius * Math.Cos(rad), Centre + radius * Math.Sin(rad));
    }

    private static Geometry Sector(int index, int count)
    {
        var step = 360.0 / count;
        var a0 = -90 - step / 2 + index * step;
        var a1 = a0 + step;
        var large = step > 180;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Polar(InnerRadius, a0), true);
            ctx.LineTo(Polar(OuterRadius, a0));
            ctx.ArcTo(Polar(OuterRadius, a1), new Size(OuterRadius, OuterRadius), 0, large, SweepDirection.Clockwise);
            ctx.LineTo(Polar(InnerRadius, a1));
            ctx.ArcTo(Polar(InnerRadius, a0), new Size(InnerRadius, InnerRadius), 0, large, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }
        return geometry;
    }

    // ---------- drawing ----------

    /// <summary>The order the centre steps through. With no files, only the Screenshot wheel has anything on it.</summary>
    private static readonly WheelMode[] Cycle = [WheelMode.Capture, WheelMode.Convert, WheelMode.Tools, WheelMode.Actions];

    private WheelMode NextMode()
    {
        var i = Array.IndexOf(Cycle, _mode);
        return Cycle[(i + 1) % Cycle.Length];
    }

    private static IBrush AccentFor(WheelMode mode) => mode switch
    {
        WheelMode.Convert => new SolidColorBrush(Color.FromRgb(0xE8, 0x62, 0x2C)), // orange
        WheelMode.Tools => new SolidColorBrush(Color.FromRgb(0x1F, 0x9E, 0x8F)),   // teal
        WheelMode.Capture => new SolidColorBrush(Color.FromRgb(0x3D, 0x8B, 0xFD)), // blue
        _ => new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xE0)),                 // violet
    };

    private void Render()
    {
        _canvas.Children.Clear();
        _segments.Clear();
        _options = PageOfOptions();
        var accent = AccentFor(_mode);

        Place(new Ellipse { Width = OuterRadius * 2 + 8, Height = OuterRadius * 2 + 8, Fill = HitDisk },
              Centre - OuterRadius - 4, Centre - OuterRadius - 4);

        if (_options.Count <= 1)
        {
            // A plain ring: empty while waiting for files, or one full segment when only one option fits.
            // Avalonia draws an ellipse's stroke inside its bounds, so size it to the outer edge.
            var ring = new Ellipse
            {
                Width = OuterRadius * 2, Height = OuterRadius * 2,
                Stroke = _options.Count == 1 && _hover == 0 ? accent : SegmentFill,
                StrokeThickness = OuterRadius - InnerRadius,
            };
            Place(ring, Centre - OuterRadius, Centre - OuterRadius);
            if (_options.Count == 1)
                AddLabel(0, 1);
        }
        else
        {
            for (var i = 0; i < _options.Count; i++)
            {
                var seg = new Path
                {
                    Data = Sector(i, _options.Count),
                    Fill = i == _hover ? accent : SegmentFill,
                    Stroke = SegmentStroke,
                    StrokeThickness = 1,
                };
                _segments.Add(seg);
                _canvas.Children.Add(seg);
                AddLabel(i, _options.Count);
            }
        }

        // Centre: wheel name, hint, and which key opens which wheel.
        var disc = new Ellipse
        {
            Width = (InnerRadius - 6) * 2, Height = (InnerRadius - 6) * 2,
            Fill = CentreFill, Stroke = accent, StrokeThickness = 2,
        };
        Place(disc, Centre - InnerRadius + 6, Centre - InnerRadius + 6);

        var title = Text(ModeName(_mode), 15, FontWeight.SemiBold, accent);
        title.Width = 120;
        title.TextAlignment = TextAlignment.Center;
        Place(title, Centre - 60, Centre - 44);

        _hint = Text(DefaultHint(), 11.5, FontWeight.Normal, TextQuiet);
        _hint.Width = 106;
        _hint.Height = 46;
        _hint.TextAlignment = TextAlignment.Center;
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.TextTrimming = TextTrimming.CharacterEllipsis;
        _hint.MaxLines = 3;
        Place(_hint, Centre - 53, Centre - 20);

        // Which wheel you're on: one dot per wheel, in cycle order; the current one is filled in its colour.
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, IsHitTestVisible = false };
        foreach (var m in Cycle)
        {
            var available = true;
            dots.Children.Add(new Ellipse
            {
                Width = 7, Height = 7,
                Fill = m == _mode ? AccentFor(m) : Brushes.Transparent,
                Stroke = available ? TextQuiet : SegmentStroke,
                StrokeThickness = m == _mode ? 0 : 1,
            });
        }
        dots.Measure(Size.Infinity);
        Place(dots, Centre - dots.DesiredSize.Width / 2, Centre + 36);

        if (_hover is int h && _options.Count > 0)
            _hint.Text = _options[h].Hint;
    }

    private FolderContents.Batch? Batch()
    {
        if (!ReferenceEquals(_batchFor, _paths))
        {
            _batchFor = _paths;
            _batch = FolderContents.Of(_paths);
        }
        return _batch;
    }

    private IReadOnlyList<WheelOption> CurrentOptions()
    {
        if (_paths.Count == 0 && _mode != WheelMode.Capture)
            return Array.Empty<WheelOption>();
        var own = OptionRegistry.GetOptions(_mode, _paths, _caps); // for folders: ZIP on Tools, the actions
        if (_mode is not (WheelMode.Convert or WheelMode.Tools) || Batch() is not { } batch)
            return own;

        // ZIP is for the folder itself; Edit is one image at a time; Rename renames the files inside.
        var inside = OptionRegistry.GetOptions(_mode, batch.Files, _caps)
            .Where(o => o.Id != "any.zip" && o.Id != "img.edit").ToList();
        return inside.Concat(own.Where(o => o.Id != "any.rename" || inside.All(i => i.Id != "any.rename"))).ToList();
    }

    /// <summary>The segments to draw: pinned ones first, then one page of at most eight.</summary>
    private IReadOnlyList<WheelOption> PageOfOptions()
    {
        var all = OptionRegistry.WithPinsFirst(CurrentOptions(), Pinned());
        _page = Math.Min(_page, OptionRegistry.PageCount(all.Count) - 1);
        return OptionRegistry.Page(all, _page, _mode);
    }

    /// <summary>The files an option works on: a folder's contents, except ZIP and the actions, which use the folder.</summary>
    private IReadOnlyList<string> TargetsFor(WheelOption option) =>
        option.Mode is WheelMode.Convert or WheelMode.Tools && option.Id != "any.zip" && Batch() is { } batch
            ? batch.Files
            : _paths;

    private void AddLabel(int index, int count)
    {
        var step = 360.0 / count;
        var mid = Polar(LabelRadius, -90 + index * step);
        var option = _options[index];
        var pinned = option.Engine != EngineKind.Navigation && Pinned().Contains(option.Id);
        var label = Text(pinned ? "★ " + option.Label : option.Label, 15, FontWeight.SemiBold, TextMain);
        label.Measure(Size.Infinity);
        Place(label, mid.X - label.DesiredSize.Width / 2, mid.Y - label.DesiredSize.Height / 2);
    }

    private void SetHover(int? index)
    {
        if (index == _hover) return;
        var previous = _hover;
        _hover = index;
        _pageFlip.Stop();
        if (!_clickMode && index is int nav && nav < _options.Count && _options[nav].Engine == EngineKind.Navigation)
            _pageFlip.Start();

        if (_options.Count == 1)
        {
            Render(); // the single-option ring recolours as a whole
            return;
        }
        if (previous is int old && old < _segments.Count)
            _segments[old].Fill = SegmentFill;
        if (index is int i && i < _segments.Count)
            _segments[i].Fill = AccentFor(_mode);
        if (_hint is not null)
            _hint.Text = index is int j && j < _options.Count ? _options[j].Hint : DefaultHint();
    }

    private string DefaultHint()
    {
        if (_nonFileDrag) return "Only files and folders can go on the wheel";
        if (_mode == WheelMode.Capture)
            return "Centre: next wheel\nRight-click: pin";
        if (_paths.Count == 0)
            return _clickMode ? "Select files in your file manager first\nCentre: next wheel" : "Move onto the wheel";
        if (_options.Count == 0)
        {
            var officeFiles = _paths.Count > 0 && _paths.All(p => FileClassifier.Classify(p) is FileKind.Document or FileKind.Spreadsheet or FileKind.Presentation);
            var none = _mode == WheelMode.Convert
                ? officeFiles ? "Office files need Microsoft Office or LibreOffice" : "No conversions for these files"
                : "Nothing here for these files";
            return _clickMode ? none + "\nCentre: next wheel" : none;
        }
        var what = _paths.Count == 1 ? System.IO.Path.GetFileName(_paths[0].TrimEnd('/', '\\')) : $"{_paths.Count} items";
        if (_mode is WheelMode.Convert or WheelMode.Tools && Batch() is { } batch)
            what = $"{FolderContents.Describe(batch)} in {what}";
        return _clickMode ? $"{what}\nCentre: next wheel" : $"{what}\nDrop on a segment";
    }

    private static string ModeName(WheelMode mode) => mode switch
    {
        WheelMode.Convert => "Convert",
        WheelMode.Tools => "Tools",
        WheelMode.Capture => "Screenshot",
        _ => "Actions",
    };

    private static TextBlock Text(string text, double size, FontWeight weight, IBrush brush) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        IsHitTestVisible = false,
    };

    private void Place(Control control, double left, double top)
    {
        Canvas.SetLeft(control, left);
        Canvas.SetTop(control, top);
        _canvas.Children.Add(control);
    }
}
