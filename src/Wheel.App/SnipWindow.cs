using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Wheel.Core.Editing;

namespace Wheel.App;

/// <summary>What to do with a finished snip.</summary>
internal enum SnipAction { Done, Copy, Save, Pin, Edit }

internal sealed record SnipResult(SKBitmap Image, SnipAction Action);

/// <summary>
/// Area screenshot, marked up in place (in the spirit of Flameshot): drag over part of a still picture of the
/// screen, then draw on it right there (pencil, line, arrow, box, circle, highlight, text, numbered steps,
/// blur, redact) with a toolbar beside the selection. Marks live on the screen picture itself, so the
/// selection can be moved (Move tool, or Space while dragging) and resized by its handles at any time, and
/// the marks stay where they were drawn. The mouse wheel sets the line thickness.
/// Finish with Enter (save and copy), Ctrl+C, Ctrl+S, pin or the full editor; Esc cancels.
/// Marks are data, burned into the pixels by <see cref="EditorRenderer"/> exactly as the full editor does.
/// </summary>
internal sealed class SnipWindow : Window
{
    private static readonly uint[] Palette = [0xFFE5383B, 0xFFE8622C, 0xFFF6C90E, 0xFF2BB673, 0xFF3D8BFD, 0xFFFFFFFF, 0xFF111111];

    private enum Tool { Move, Pen, Line, Arrow, Box, Ellipse, Highlight, Text, Step, Blur, Redact }

    private static readonly (Tool Tool, string Tip, Key Key, IconKind Icon)[] Tools =
    [
        (Tool.Pen, "Pencil (P)", Key.P, IconKind.Edit),
        (Tool.Line, "Line (L)", Key.L, IconKind.Line),
        (Tool.Arrow, "Arrow (A)", Key.A, IconKind.Arrow),
        (Tool.Box, "Box (R)", Key.R, IconKind.Box),
        (Tool.Ellipse, "Circle (C)", Key.C, IconKind.Ellipse),
        (Tool.Highlight, "Highlight (H)", Key.H, IconKind.Highlight),
        (Tool.Text, "Text (T): click, type, Enter", Key.T, IconKind.Text),
        (Tool.Step, "Numbered steps (N)", Key.N, IconKind.Steps),
        (Tool.Blur, "Blur (B): pixelates", Key.B, IconKind.Blur),
        (Tool.Redact, "Redact (X): removes the pixels", Key.X, IconKind.Redact),
        (Tool.Move, "Move the selection (M, or hold Space)", Key.M, IconKind.Move),
    ];

    private const double HandleSize = 9;
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0xE8, 0x62, 0x2C));
    private static readonly IBrush BarBrush = new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x21, 0x2E));
    private static readonly IBrush Selected = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

    private readonly SKBitmap _screen;      // the whole screen, in its own pixels
    private readonly Bitmap _picture;
    private readonly double _px;            // screen pixels per view unit
    private readonly Canvas _canvas = new();
    private readonly Image _marked = new() { Stretch = Stretch.Fill, IsVisible = false, IsHitTestVisible = false };
    private readonly Avalonia.Controls.Shapes.Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x78, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle _border = new() { Stroke = Accent, StrokeThickness = 1.5, IsVisible = false, IsHitTestVisible = false };
    private readonly Border _size = Label();
    private readonly Border _thickness = Label();
    private readonly Border _hint;
    private readonly Border _bar;
    private readonly List<Border> _handles = new();
    private readonly Dictionary<Tool, Button> _toolButtons = new();
    private readonly Border _colorDot = new() { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5) };
    private readonly Button _undo, _redo;
    private readonly TaskCompletionSource<SnipResult?> _result = new();
    private readonly EditHistory _history = new();
    private readonly Avalonia.Threading.DispatcherTimer _hideThickness = new() { Interval = TimeSpan.FromSeconds(1.2) };

    private Rect _sel;                      // selection, in view units
    private bool _selected;
    private Tool _tool = Tool.Arrow;
    private int _colorIndex;
    private double _lineWidth;              // in screen pixels
    private Point? _dragFrom;
    private Rect _selAtDragStart;
    private bool _moving;
    private bool _spaceHeld;
    private (bool L, bool T, bool R, bool B)? _resizing;
    private Control? _ghost;
    private List<(double X, double Y)>? _ink;
    private Polyline? _inkLine;
    private TextBox? _typing;
    private (double X, double Y) _typingAt;

    private SnipWindow(SKBitmap screen, Bitmap picture, Avalonia.PixelRect screenBounds, double scaling)
    {
        _screen = screen;
        _picture = picture;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = Brushes.Black;
        Position = screenBounds.Position;
        Width = screenBounds.Width / scaling;
        Height = screenBounds.Height / scaling;
        _px = screen.Width / Width;
        _lineWidth = Math.Max(3, Math.Round(2.5 * _px));
        Cursor = new Cursor(StandardCursorType.Cross);

        _canvas.Children.Add(new Image { Source = picture, Stretch = Stretch.Fill, Width = Width, Height = Height });
        _canvas.Children.Add(_marked);          // the picture with the marks burned in
        _dim.Data = new RectangleGeometry(new Rect(0, 0, Width, Height));
        _canvas.Children.Add(_dim);             // darkens outside the selection (marks there too)
        _canvas.Children.Add(_border);
        _canvas.Children.Add(_size);
        _canvas.Children.Add(_thickness);
        for (var i = 0; i < 8; i++)
        {
            var h = new Border { Width = HandleSize, Height = HandleSize, CornerRadius = new CornerRadius(HandleSize / 2), Background = Brushes.White, BorderBrush = Accent, BorderThickness = new Thickness(2), IsVisible = false, IsHitTestVisible = false };
            _handles.Add(h);
            _canvas.Children.Add(h);
        }

        _hint = new Border
        {
            Background = BarBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6), IsHitTestVisible = false,
            Child = new TextBlock { Text = "Drag over the area · Esc to cancel", Foreground = Brushes.White, FontSize = 13 },
        };
        Canvas.SetLeft(_hint, Width / 2 - 130);
        Canvas.SetTop(_hint, 40);
        _canvas.Children.Add(_hint);

        // Toolbar: drawing tools, move | colour | undo, redo | pin, editor, copy, save, done, close
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (tool, tip, _, icon) in Tools)
        {
            if (tool == Tool.Move) row.Children.Add(Separator());
            var b = RoundButton(icon, tip, () => SelectTool(tool));
            _toolButtons[tool] = b;
            row.Children.Add(b);
        }
        row.Children.Add(Separator());
        var color = RoundButton(null, "Colour (1–7, or right-click)", NextColor);
        color.Content = _colorDot;
        row.Children.Add(color);
        _undo = RoundButton(IconKind.Undo, "Undo (Ctrl+Z)", Undo);
        _redo = RoundButton(IconKind.Redo, "Redo (Ctrl+Y)", Redo);
        row.Children.Add(_undo);
        row.Children.Add(_redo);
        row.Children.Add(Separator());
        row.Children.Add(RoundButton(IconKind.Pin, "Pin on screen (Ctrl+P)", () => Finish(SnipAction.Pin)));
        row.Children.Add(RoundButton(IconKind.Sparkle, "Open in the full editor (Ctrl+E): crop, beautify…", () => Finish(SnipAction.Edit)));
        row.Children.Add(RoundButton(IconKind.Copy, "Copy (Ctrl+C)", () => Finish(SnipAction.Copy)));
        row.Children.Add(RoundButton(IconKind.Save, "Save (Ctrl+S)", () => Finish(SnipAction.Save)));
        var done = RoundButton(IconKind.Check, "Done: save and copy (Enter)", () => Finish(SnipAction.Done));
        done.Background = Accent;
        row.Children.Add(done);
        row.Children.Add(RoundButton(IconKind.Close, "Cancel (Esc)", Cancel));
        _bar = new Border
        {
            Background = BarBrush, CornerRadius = new CornerRadius(22), Padding = new Thickness(6), Child = row, IsVisible = false,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, OffsetY = 4, Color = Color.FromArgb(0x60, 0, 0, 0) }),
        };
        _canvas.Children.Add(_bar);

        Content = _canvas;
        SelectTool(Tool.Arrow);
        UpdateColor();
        _hideThickness.Tick += (_, _) => { _thickness.IsVisible = false; _hideThickness.Stop(); };

        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerWheelChanged += OnWheel;
        KeyDown += OnKey;
        KeyUp += (_, e) => { if (e.Key == Key.Space) _spaceHeld = false; };
        Deactivated += (_, _) => { if (!_selected) Cancel(); };
        Closed += (_, _) =>
        {
            _result.TrySetResult(null);
            _hideThickness.Stop();
            _picture.Dispose();
            _screen.Dispose();
        };
    }

    /// <summary>Shows the snip over the screen in <paramref name="screenshot"/>; the finished image, or null when cancelled.</summary>
    public static async Task<SnipResult?> ShowAsync(string screenshot, Avalonia.PixelRect screenBounds, double scaling)
    {
        var screen = SKBitmap.Decode(screenshot) ?? throw new IOException("Couldn't read the screen picture.");
        Bitmap picture;
        using (var stream = File.OpenRead(screenshot))
            picture = new Bitmap(stream);
        var window = new SnipWindow(screen, picture, screenBounds, scaling);
        window.Show();
        window.Activate();
        return await window._result.Task;
    }

    // ---------- building blocks ----------

    private static Border Label() => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xD0, 0, 0, 0)), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2),
        IsVisible = false, IsHitTestVisible = false, Child = new TextBlock { Foreground = Brushes.White, FontSize = 12 },
    };

    private static Button RoundButton(IconKind? icon, string tip, Action run)
    {
        var b = new Button
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Padding = new Thickness(0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand), Focusable = false,
        };
        if (icon is { } i) b.Content = Ui.Icon(i, 17, Brushes.White);
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => run();
        return b;
    }

    private static Control Separator() => new Border { Width = 1, Height = 22, Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };

    private uint Color32 => Palette[_colorIndex];

    private void SelectTool(Tool tool)
    {
        CommitTyping();
        _tool = tool;
        foreach (var (t, b) in _toolButtons) b.Background = t == tool ? Selected : Brushes.Transparent;
    }

    private void NextColor() { _colorIndex = (_colorIndex + 1) % Palette.Length; UpdateColor(); }

    private void UpdateColor()
    {
        _colorDot.Background = new SolidColorBrush(Color.FromUInt32(Color32));
        if (_typing is not null) _typing.Foreground = new SolidColorBrush(Color.FromUInt32(Color32));
    }

    private static void SetText(Border label, string text) => ((TextBlock)label.Child!).Text = text;

    // ---------- coordinates ----------

    private (double X, double Y) ToImage(Point p) =>
        (Math.Clamp(p.X * _px, 0, _screen.Width), Math.Clamp(p.Y * _px, 0, _screen.Height));

    private SKRectI SelectionPixels()
    {
        int x1 = (int)Math.Round(_sel.X * _px), y1 = (int)Math.Round(_sel.Y * _px);
        int x2 = (int)Math.Round(_sel.Right * _px), y2 = (int)Math.Round(_sel.Bottom * _px);
        return new SKRectI(Math.Clamp(x1, 0, _screen.Width), Math.Clamp(y1, 0, _screen.Height), Math.Clamp(x2, 0, _screen.Width), Math.Clamp(y2, 0, _screen.Height));
    }

    private Point Inside(Point p) => new(Math.Clamp(p.X, _sel.X, _sel.Right), Math.Clamp(p.Y, _sel.Y, _sel.Bottom));

    // ---------- selection ----------

    private void ShowSelection(Rect r)
    {
        _sel = r;
        _border.IsVisible = _size.IsVisible = true;
        _border.Width = r.Width;
        _border.Height = r.Height;
        Canvas.SetLeft(_border, r.X);
        Canvas.SetTop(_border, r.Y);
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(r));
        var px = SelectionPixels();
        SetText(_size, $"{px.Width} × {px.Height}");
        Canvas.SetLeft(_size, r.X);
        Canvas.SetTop(_size, r.Y > 28 ? r.Y - 26 : r.Y + 6);

        var points = new[]
        {
            r.TopLeft, new Point(r.Center.X, r.Y), r.TopRight, new Point(r.Right, r.Center.Y),
            r.BottomRight, new Point(r.Center.X, r.Bottom), r.BottomLeft, new Point(r.X, r.Center.Y),
        };
        for (var i = 0; i < 8; i++)
        {
            Canvas.SetLeft(_handles[i], points[i].X - HandleSize / 2);
            Canvas.SetTop(_handles[i], points[i].Y - HandleSize / 2);
            _handles[i].IsVisible = _selected;
        }
        if (_selected) PlaceBar();
    }

    private void Select(Rect r)
    {
        _selected = true;
        _hint.IsVisible = false;
        _bar.IsVisible = true;
        ShowSelection(r);
        Redraw();
    }

    private void PlaceBar()
    {
        _bar.Measure(Size.Infinity);
        var size = _bar.DesiredSize;
        var x = Math.Clamp(_sel.Center.X - size.Width / 2, 8, Math.Max(8, Width - size.Width - 8));
        var y = _sel.Bottom + 10 + size.Height <= Height - 8 ? _sel.Bottom + 10       // below
              : _sel.Y - 10 - size.Height >= 8 ? _sel.Y - 10 - size.Height             // above
              : _sel.Bottom - size.Height - 10;                                        // inside, at the bottom
        Canvas.SetLeft(_bar, x);
        Canvas.SetTop(_bar, y);
    }

    /// <summary>Which edges a point grabs (handles and borders of the selection).</summary>
    private (bool L, bool T, bool R, bool B)? EdgesAt(Point p)
    {
        if (!_selected) return null;
        const double grab = 7;
        bool l = Math.Abs(p.X - _sel.X) <= grab, r = Math.Abs(p.X - _sel.Right) <= grab;
        bool t = Math.Abs(p.Y - _sel.Y) <= grab, b = Math.Abs(p.Y - _sel.Bottom) <= grab;
        var near = p.X >= _sel.X - grab && p.X <= _sel.Right + grab && p.Y >= _sel.Y - grab && p.Y <= _sel.Bottom + grab;
        return near && (l || r || t || b) ? (l, t, r, b) : null;
    }

    private Rect Clamp(Rect r)
    {
        var x = Math.Clamp(r.X, 0, Math.Max(0, Width - r.Width));
        var y = Math.Clamp(r.Y, 0, Math.Max(0, Height - r.Height));
        return new Rect(x, y, Math.Min(r.Width, Width), Math.Min(r.Height, Height));
    }

    // ---------- drawing ----------

    private void Redraw()
    {
        using var rendered = EditorRenderer.Render(_screen, _history.Current);
        _marked.Source = EditorWindow.ToAvalonia(rendered);
        _marked.Width = Width;
        _marked.Height = Height;
        _marked.IsVisible = _history.Current.Annotations.Count > 0;
        _undo.Opacity = _history.CanUndo ? 1 : 0.35; // faded rather than disabled, so the dark bar keeps its look
        _redo.Opacity = _history.CanRedo ? 1 : 0.35;
    }

    private void Apply(EditState next) { _history.Apply(next); Redraw(); }
    private void Undo() { CancelTyping(); if (_history.Undo()) Redraw(); }
    private void Redo() { if (_history.Redo()) Redraw(); }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var p = e.GetPosition(_canvas);
        if (point.Properties.IsRightButtonPressed)
        {
            if (_selected) NextColor(); else Cancel(); // right-click: next colour (or cancel before selecting)
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        CommitTyping();

        if (_selected && EdgesAt(p) is { } edges)
        {
            _resizing = edges;
            e.Pointer.Capture(this);
            return;
        }
        if (_selected && (_tool == Tool.Move || _spaceHeld) && _sel.Contains(p))
        {
            _moving = true;
            _dragFrom = p;
            _selAtDragStart = _sel;
            e.Pointer.Capture(this);
            return;
        }
        if (!_selected || (!_sel.Contains(p) && _history.Current.Annotations.Count == 0))
        {
            // (Re)start the selection. Once something is drawn, clicks outside are ignored so nothing is lost.
            _selected = false;
            _bar.IsVisible = false;
            foreach (var h in _handles) h.IsVisible = false;
            _dragFrom = p;
            e.Pointer.Capture(this);
            return;
        }
        if (!_sel.Contains(p)) return; // drawing happens inside the selection

        switch (_tool)
        {
            case Tool.Step:
            {
                var (x, y) = ToImage(p);
                var state = _history.Current;
                Apply(state.With(new Annotation(EditTool.Step, x, y, x, y, Color32, Number: state.NextStepNumber, Size: _lineWidth)));
                return;
            }
            case Tool.Text:
                StartTyping(p);
                return;
            case Tool.Pen:
                _ink = [ToImage(p)];
                _inkLine = new Polyline { Stroke = new SolidColorBrush(Color32), StrokeThickness = _lineWidth / _px, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, IsHitTestVisible = false, Points = { p } };
                _canvas.Children.Insert(_canvas.Children.IndexOf(_dim), _inkLine);
                _dragFrom = p;
                e.Pointer.Capture(this);
                return;
            default:
                _dragFrom = p;
                e.Pointer.Capture(this);
                return;
        }
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        if (_resizing is { } edges)
        {
            double l = _sel.X, t = _sel.Y, r = _sel.Right, b = _sel.Bottom;
            if (edges.L) l = Math.Clamp(p.X, 0, r - 16);
            if (edges.R) r = Math.Clamp(p.X, l + 16, Width);
            if (edges.T) t = Math.Clamp(p.Y, 0, b - 16);
            if (edges.B) b = Math.Clamp(p.Y, t + 16, Height);
            ShowSelection(new Rect(new Point(l, t), new Point(r, b)));
            return;
        }
        if (_dragFrom is not { } from)
        {
            UpdateCursor(p);
            return;
        }
        if (_moving)
        {
            ShowSelection(Clamp(_selAtDragStart.Translate(p - from)));
            return;
        }
        if (!_selected)
        {
            ShowSelection(new Rect(from, p).Normalize());
            return;
        }

        p = Inside(p);
        if (_tool == Tool.Pen && _ink is not null && _inkLine is not null)
        {
            var next = ToImage(p);
            var last = _ink[^1];
            if (Math.Abs(next.X - last.X) + Math.Abs(next.Y - last.Y) >= 1.5)
            {
                _ink.Add(next);
                _inkLine.Points.Add(p);
            }
            return;
        }

        // Live preview of the mark being drawn.
        if (_ghost is not null) _canvas.Children.Remove(_ghost);
        var brush = new SolidColorBrush(_tool is Tool.Redact or Tool.Blur ? 0xFFFFFFFF : Color32);
        var thickness = Math.Max(1.5, _lineWidth / _px);
        var area = new Rect(from, p).Normalize();
        _ghost = _tool switch
        {
            Tool.Arrow or Tool.Line => new Line { StartPoint = from, EndPoint = p, Stroke = brush, StrokeThickness = thickness, StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false },
            Tool.Ellipse => Placed(new Ellipse { Width = area.Width, Height = area.Height, Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false }, area),
            _ => Placed(new Rectangle
            {
                Width = area.Width, Height = area.Height, Stroke = brush, IsHitTestVisible = false,
                StrokeThickness = _tool is Tool.Box ? thickness : 1.5,
                StrokeDashArray = _tool is Tool.Blur or Tool.Redact ? new Avalonia.Collections.AvaloniaList<double> { 4, 3 } : null,
                Fill = _tool == Tool.Highlight ? new SolidColorBrush(Color.FromUInt32(Color32), 0.35) : null,
            }, area),
        };
        _canvas.Children.Insert(_canvas.Children.IndexOf(_dim), _ghost);
    }

    private static Control Placed(Control c, Rect r)
    {
        Canvas.SetLeft(c, r.X);
        Canvas.SetTop(c, r.Y);
        return c;
    }

    private void UpdateCursor(Point p)
    {
        if (!_selected) return;
        if (EdgesAt(p) is { } e)
            Cursor = new Cursor((e.L && e.T) || (e.R && e.B) ? StandardCursorType.TopLeftCorner
                : (e.R && e.T) || (e.L && e.B) ? StandardCursorType.TopRightCorner
                : e.L || e.R ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
        else if ((_tool == Tool.Move || _spaceHeld) && _sel.Contains(p))
            Cursor = new Cursor(StandardCursorType.SizeAll);
        else
            Cursor = new Cursor(_tool == Tool.Text && _sel.Contains(p) ? StandardCursorType.Ibeam : StandardCursorType.Cross);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        e.Pointer.Capture(null);
        if (_resizing is not null || _moving)
        {
            _resizing = null;
            _moving = false;
            _dragFrom = null;
            return;
        }
        if (_dragFrom is not { } from) return;
        _dragFrom = null;

        if (!_selected)
        {
            var r = new Rect(from, p).Normalize();
            if (r.Width * _px < 8 || r.Height * _px < 8)
            {
                // A click, not a drag: start over.
                _border.IsVisible = _size.IsVisible = false;
                _dim.Data = new RectangleGeometry(new Rect(0, 0, Width, Height));
                return;
            }
            Select(r);
            return;
        }

        if (_tool == Tool.Pen && _ink is not null)
        {
            if (_inkLine is not null) _canvas.Children.Remove(_inkLine);
            var ink = _ink;
            _ink = null;
            _inkLine = null;
            Apply(_history.Current.With(Annotation.Freehand(ink, Color32, _lineWidth)));
            return;
        }

        if (_ghost is not null) _canvas.Children.Remove(_ghost);
        _ghost = null;
        var (x1, y1) = ToImage(from);
        var (x2, y2) = ToImage(Inside(p));
        if (Math.Abs(x2 - x1) < EditGeometry.MinimumSize && Math.Abs(y2 - y1) < EditGeometry.MinimumSize) return;
        var kind = _tool switch
        {
            Tool.Line => EditTool.Line, Tool.Arrow => EditTool.Arrow, Tool.Box => EditTool.Rectangle, Tool.Ellipse => EditTool.Ellipse,
            Tool.Highlight => EditTool.Highlight, Tool.Blur => EditTool.Blur, _ => EditTool.Redact,
        };
        Apply(_history.Current.With(new Annotation(kind, x1, y1, x2, y2, Color32, Size: _lineWidth)));
    }

    /// <summary>The mouse wheel sets the line thickness (shown briefly beside the pointer).</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!_selected) return;
        _lineWidth = Math.Clamp(_lineWidth + Math.Sign(e.Delta.Y) * Math.Max(1, Math.Round(_px)), 1, 60 * Math.Max(1, _px));
        var p = e.GetPosition(_canvas);
        SetText(_thickness, $"Thickness {Math.Round(_lineWidth / _px)}");
        Canvas.SetLeft(_thickness, p.X + 16);
        Canvas.SetTop(_thickness, p.Y + 16);
        _thickness.IsVisible = true;
        _hideThickness.Stop();
        _hideThickness.Start();
        e.Handled = true;
    }

    // ---------- text ----------

    private void StartTyping(Point at)
    {
        _typingAt = ToImage(at);
        _typing = new TextBox
        {
            MinWidth = 140, AcceptsReturn = false, Watermark = "Type, then Enter",
            Foreground = new SolidColorBrush(Color.FromUInt32(Color32)), FontSize = Math.Max(14, _lineWidth * 6 / _px), FontWeight = FontWeight.Bold,
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
        };
        Canvas.SetLeft(_typing, at.X);
        Canvas.SetTop(_typing, at.Y);
        _typing.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitTyping(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelTyping(); e.Handled = true; }
        };
        _canvas.Children.Add(_typing);
        _typing.Focus();
    }

    private void CommitTyping()
    {
        if (_typing is null) return;
        var text = _typing.Text?.Trim();
        CancelTyping();
        if (!string.IsNullOrEmpty(text))
            Apply(_history.Current.With(new Annotation(EditTool.Text, _typingAt.X, _typingAt.Y, _typingAt.X, _typingAt.Y, Color32, text, Size: _lineWidth)));
    }

    private void CancelTyping()
    {
        if (_typing is null) return;
        _canvas.Children.Remove(_typing);
        _typing = null;
        Focus();
    }

    // ---------- finishing ----------

    private void Finish(SnipAction action)
    {
        if (_result.Task.IsCompleted || !_selected) return;
        CommitTyping();
        var px = SelectionPixels();
        if (px.Width < 1 || px.Height < 1) return;
        var crop = new Wheel.Core.Editing.PixelRect(px.Left, px.Top, px.Width, px.Height);
        _result.TrySetResult(new SnipResult(EditorRenderer.Render(_screen, _history.Current with { Crop = crop }), action));
        Close();
    }

    private void Cancel()
    {
        if (_result.Task.IsCompleted) return;
        _result.TrySetResult(null);
        Close();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_typing is not null) return; // the text box handles its own keys
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.Escape) Cancel();
        else if (!_selected) return;
        else if (e.Key is Key.Enter or Key.Return) Finish(SnipAction.Done);
        else if (e.Key == Key.Space) _spaceHeld = true;
        else if (command && e.Key == Key.C) Finish(SnipAction.Copy);
        else if (command && e.Key == Key.S) Finish(SnipAction.Save);
        else if (command && e.Key == Key.P) Finish(SnipAction.Pin);
        else if (command && e.Key == Key.E) Finish(SnipAction.Edit);
        else if (command && e.Key == Key.Z) { if (shift) Redo(); else Undo(); }
        else if (command && e.Key == Key.Y) Redo();
        else if (!command && e.Key >= Key.D1 && e.Key <= Key.D7) { _colorIndex = e.Key - Key.D1; UpdateColor(); }
        else if (!command && e.Key is Key.Up or Key.Down or Key.Left or Key.Right) Nudge(e.Key, shift ? 10 : 1);
        else if (!command && Array.Find(Tools, t => t.Key == e.Key) is { Tip: not null } match) SelectTool(match.Tool);
        else return;
        e.Handled = true;
    }

    /// <summary>Arrow keys move the selection by a pixel (Shift: ten).</summary>
    private void Nudge(Key key, double pixels)
    {
        var d = pixels / _px;
        var v = key switch { Key.Up => new Vector(0, -d), Key.Down => new Vector(0, d), Key.Left => new Vector(-d, 0), _ => new Vector(d, 0) };
        ShowSelection(Clamp(_sel.Translate(v)));
    }
}
