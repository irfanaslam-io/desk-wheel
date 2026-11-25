using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Wheel.App.Platform;
using Wheel.Core;
using Wheel.Core.Editing;

namespace Wheel.App;

/// <summary>
/// The screenshot editor: arrows, boxes, highlight, text, numbered steps, blur, redaction and crop,
/// with undo/redo. Marks are stored in image pixels and drawn by <see cref="EditorRenderer"/>,
/// so the view and the saved file always match. Saving never touches the original.
/// </summary>
internal sealed class EditorWindow : Window
{
    private static readonly (string Name, uint Argb)[] Palette =
    [
        ("Red", 0xFFE5383B), ("Orange", 0xFFE8622C), ("Yellow", 0xFFF6C90E), ("Green", 0xFF2BB673), ("Blue", 0xFF3D8BFD),
    ];

    private static readonly (EditTool Tool, string Label, Key Key, IconKind Icon, string How)[] Tools =
    [
        (EditTool.Arrow, "Arrow", Key.A, IconKind.Arrow, "Drag from the tail to the tip"),
        (EditTool.Rectangle, "Box", Key.R, IconKind.Box, "Drag to outline an area"),
        (EditTool.Highlight, "Highlight", Key.H, IconKind.Highlight, "Drag over what matters"),
        (EditTool.Text, "Text", Key.T, IconKind.Text, "Click where the text goes, type, press Enter"),
        (EditTool.Step, "Steps", Key.N, IconKind.Steps, "Click to add numbered markers 1, 2, 3…"),
        (EditTool.Blur, "Blur", Key.B, IconKind.Blur, "Drag over anything private: it's pixelated"),
        (EditTool.Redact, "Redact", Key.X, IconKind.Redact, "Drag to black it out: the content is removed, not covered"),
        (EditTool.Crop, "Crop", Key.C, IconKind.Crop, "Drag the area to keep"),
    ];

    private readonly string _sourcePath;
    private readonly SKBitmap _source;
    private readonly IPlatform _platform;
    private readonly Action<string, string> _notify;
    private readonly EditHistory _history = new();

    private readonly Image _image = new() { Stretch = Stretch.Uniform, Effect = new DropShadowEffect { BlurRadius = 24, OffsetX = 0, OffsetY = 4, Opacity = 0.28, Color = Colors.Black } };
    private readonly Canvas _overlay = new() { Background = Brushes.Transparent };
    private readonly Dictionary<EditTool, ToggleButton> _toolButtons = new();
    private readonly List<Border> _swatches = new();
    private readonly Button _undo, _redo, _frame;
    private readonly TextBlock _status;

    private EditTool _tool = EditTool.Arrow;
    private uint _color = Palette[0].Argb;
    private SKBitmap? _rendered;
    private Point? _dragStart;       // in overlay (view) coordinates
    private Control? _ghost;         // live preview while dragging
    private TextBox? _typing;
    private (double X, double Y) _typingAt;
    private bool _dirty;

    public EditorWindow(string sourcePath, SKBitmap source, IPlatform platform, Action<string, string> notify)
    {
        _sourcePath = sourcePath;
        _source = source;
        _platform = platform;
        _notify = notify;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Ui.Window(this, $"Edit · {System.IO.Path.GetFileName(sourcePath)}");

        // ---- toolbar: tools | colours | beautify ······ undo redo | copy save done ----
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (tool, label, key, icon, how) in Tools)
        {
            var b = new ToggleButton { Content = Ui.Icon(icon, 18), Padding = new Thickness(8), IsChecked = tool == _tool, CornerRadius = Ui.Radius };
            b.Classes.Add("tool");
            ToolTip.SetTip(b, $"{label}  ({key})\n{how}");
            var t = tool;
            b.Click += (_, _) => SelectTool(t);
            _toolButtons[tool] = b;
            tools.Children.Add(b);
        }

        var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (name, argb) in Palette)
        {
            var swatch = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromUInt32(argb)),
                BorderBrush = Ui.Text, BorderThickness = new Thickness(argb == _color ? 2 : 0),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(swatch, name);
            var c = argb;
            swatch.PointerPressed += (_, _) => SelectColor(c);
            _swatches.Add(swatch);
            colors.Children.Add(swatch);
        }

        _frame = Ui.Secondary("Beautify", NextFrame, IconKind.Sparkle,
            "Background, rounded corners and a shadow. Click again for the next style (F)");

        _undo = Ui.IconButton(IconKind.Undo, OperatingSystem.IsMacOS() ? "Undo (⌘Z)" : "Undo (Ctrl+Z)", Undo, 18);
        _redo = Ui.IconButton(IconKind.Redo, OperatingSystem.IsMacOS() ? "Redo (⇧⌘Z)" : "Redo (Ctrl+Y)", Redo, 18);
        var copy = Ui.Secondary("Copy", () => _ = CopyAsync(), IconKind.Copy, "Copy the edited picture");
        var save = Ui.Secondary("Save", () => _ = SaveAsync(closeAfter: false), IconKind.Save, "Save a copy beside the original");
        var done = Ui.Primary("Done", () => _ = SaveAsync(closeAfter: true), IconKind.Check, "Save and close");

        Control Separator() => new Border { Width = 1, Height = 22, Background = Ui.Line, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center };
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Children = { tools, Separator(), colors, Separator(), _frame },
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { _undo, _redo, Separator(), copy, save, done },
        };
        var toolbar = new DockPanel { Margin = new Thickness(12, 8) };
        DockPanel.SetDock(right, Dock.Right);
        toolbar.Children.Add(right);
        toolbar.Children.Add(left);
        var toolbarBar = new Border { Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = toolbar };

        // ---- status bar: size · what the tool does · saved state ----
        _status = new TextBlock { FontSize = Ui.CaptionSize, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
        var statusBar = new Border
        {
            Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 6), Child = _status,
        };

        // ---- canvas: rendered image with a transparent layer on top for drags and typing ----
        var stage = new Grid { Margin = new Thickness(24), Children = { _image, _overlay } };
        _overlay.PointerPressed += OnPressed;
        _overlay.PointerMoved += OnMoved;
        _overlay.PointerReleased += OnReleased;
        _overlay.Cursor = new Cursor(StandardCursorType.Cross);

        var root = new DockPanel { Background = Ui.Stage };
        DockPanel.SetDock(toolbarBar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbarBar);
        root.Children.Add(statusBar);
        root.Children.Add(stage);
        Content = root;

        KeyDown += OnKey;
        Closing += (_, e) =>
        {
            // Unsaved marks: keep them rather than silently losing work; one more Close discards.
            if (_dirty)
            {
                _dirty = false;
                e.Cancel = true;
                _notify("Unsaved changes", "Close again to discard them, or click Save.");
            }
        };
        Closed += (_, _) =>
        {
            _rendered?.Dispose();
            _source.Dispose();
        };

        SizeToImage();
        Redraw();
    }

    /// <summary>Opens the window big enough for the image, but at most ~85% of the screen.</summary>
    private void SizeToImage()
    {
        var area = Screens.Primary?.WorkingArea;
        var scale = Screens.Primary?.Scaling ?? 1;
        double maxW = area is { } a ? a.Width / scale * 0.85 : 1200, maxH = area is { } b ? b.Height / scale * 0.85 : 800;
        // Screenshots are taken at full pixel density, so show them at their on-screen size.
        double imgW = _source.Width / scale, imgH = _source.Height / scale;
        var fit = Math.Min(1, Math.Min((maxW - 48) / imgW, (maxH - 140) / imgH));
        Width = Math.Max(900, imgW * fit + 48);
        Height = Math.Max(480, imgH * fit + 140);
    }

    // ---------- tools and colours ----------

    private void SelectTool(EditTool tool)
    {
        CommitTyping();
        _tool = tool;
        foreach (var (t, b) in _toolButtons)
            b.IsChecked = t == tool;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var (_, label, key, _, how) = Array.Find(Tools, t => t.Tool == _tool);
        var size = _rendered is null ? "" : $"{_rendered.Width} × {_rendered.Height} px";
        _status.Text = $"{size}    ·    {label} ({key}): {how}    ·    {(_dirty ? "Unsaved changes" : "No unsaved changes")}";
    }

    private void SelectColor(uint argb)
    {
        _color = argb;
        for (var i = 0; i < _swatches.Count; i++)
            _swatches[i].BorderThickness = new Thickness(Palette[i].Argb == argb ? 2 : 0);
    }

    private static Button MakeButton(string label, Action run)
    {
        var b = new Button { Content = label, Padding = new Thickness(10, 5) };
        b.Click += (_, _) => run();
        return b;
    }

    // ---------- view ↔ image ----------

    /// <summary>Scale and letterbox offset of the rendered image inside the overlay.</summary>
    private (double Scale, double OffsetX, double OffsetY) ViewTransform()
    {
        if (_rendered is null || _overlay.Bounds.Width <= 0) return (1, 0, 0);
        var scale = Math.Min(_overlay.Bounds.Width / _rendered.Width, _overlay.Bounds.Height / _rendered.Height);
        return (scale, (_overlay.Bounds.Width - _rendered.Width * scale) / 2, (_overlay.Bounds.Height - _rendered.Height * scale) / 2);
    }

    private (double X, double Y) ToImage(Point view)
    {
        var (scale, ox, oy) = ViewTransform();
        var pad = FramePadding();
        return EditGeometry.ViewToImage(view.X - ox - pad * scale, view.Y - oy - pad * scale, scale, _history.Current.Crop);
    }

    /// <summary>With Beautify on, the image sits inside the frame's padding.</summary>
    private double FramePadding()
    {
        var state = _history.Current;
        if (state.Frame is null) return 0;
        var crop = state.Crop?.ClampTo(_source.Width, _source.Height);
        return Frames.Layout(crop?.Width ?? _source.Width, crop?.Height ?? _source.Height).Padding;
    }

    /// <summary>Beautify: Off → Sunset → Ocean → … → Off. Undoable like any other change.</summary>
    private void NextFrame()
    {
        CommitTyping();
        var next = Frames.Next(_history.Current.Frame);
        Apply(_history.Current with { Frame = next });
    }

    private void UpdateFrameButton()
    {
        var frame = _history.Current.Frame;
        _frame.Content = Ui.IconText(IconKind.Sparkle, frame is null ? "Beautify" : $"Beautify · {frame.Name}");
        _frame.Classes.Set("primary", frame is not null);
    }

    // ---------- pointer ----------

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_overlay).Properties.IsLeftButtonPressed) return;
        var at = e.GetPosition(_overlay);
        CommitTyping();

        switch (_tool)
        {
            case EditTool.Step:
            {
                var (x, y) = ToImage(at);
                var state = _history.Current;
                Apply(state.With(new Annotation(EditTool.Step, x, y, x, y, _color, Number: state.NextStepNumber)));
                return;
            }
            case EditTool.Text:
                StartTyping(at);
                return;
            default:
                _dragStart = at;
                e.Pointer.Capture(_overlay);
                return;
        }
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var now = e.GetPosition(_overlay);
        if (_ghost is not null) _overlay.Children.Remove(_ghost);

        var brush = new SolidColorBrush(_tool is EditTool.Redact or EditTool.Blur or EditTool.Crop ? 0xFFFFFFFF : _color);
        if (_tool == EditTool.Arrow)
        {
            _ghost = new Line { StartPoint = start, EndPoint = now, Stroke = brush, StrokeThickness = 3 };
        }
        else
        {
            var r = new Rect(start, now).Normalize();
            var shape = new Rectangle
            {
                Width = r.Width, Height = r.Height, Stroke = brush, StrokeThickness = 2,
                StrokeDashArray = _tool is EditTool.Crop or EditTool.Blur or EditTool.Redact ? new Avalonia.Collections.AvaloniaList<double> { 4, 3 } : null,
                Fill = _tool == EditTool.Highlight ? new SolidColorBrush(Color.FromUInt32(_color), 0.35) : null,
            };
            Canvas.SetLeft(shape, r.X);
            Canvas.SetTop(shape, r.Y);
            _ghost = shape;
        }
        _overlay.Children.Add(_ghost);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStart is not { } start) return;
        _dragStart = null;
        e.Pointer.Capture(null);
        if (_ghost is not null) _overlay.Children.Remove(_ghost);
        _ghost = null;

        var (x1, y1) = ToImage(start);
        var (x2, y2) = ToImage(e.GetPosition(_overlay));
        if (Math.Abs(x2 - x1) < EditGeometry.MinimumSize && Math.Abs(y2 - y1) < EditGeometry.MinimumSize)
            return; // a click, not a drag

        var state = _history.Current;
        if (_tool == EditTool.Crop)
        {
            var crop = Wheel.Core.Editing.PixelRect.FromCorners(x1, y1, x2, y2).ClampTo(_source.Width, _source.Height);
            if (crop.Width >= EditGeometry.MinimumSize && crop.Height >= EditGeometry.MinimumSize)
                Apply(state with { Crop = crop });
            SelectTool(EditTool.Arrow); // cropping is usually done once
            return;
        }
        Apply(state.With(new Annotation(_tool, x1, y1, x2, y2, _color)));
    }

    // ---------- text ----------

    private void StartTyping(Point at)
    {
        _typingAt = ToImage(at);
        _typing = new TextBox
        {
            MinWidth = 160, AcceptsReturn = false, Watermark = "Type, then press Enter",
            Foreground = new SolidColorBrush(_color), FontSize = 16, FontWeight = FontWeight.Bold,
        };
        Canvas.SetLeft(_typing, at.X);
        Canvas.SetTop(_typing, at.Y);
        _typing.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitTyping(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelTyping(); e.Handled = true; }
        };
        _overlay.Children.Add(_typing);
        _typing.Focus();
    }

    private void CommitTyping()
    {
        if (_typing is null) return;
        var text = _typing.Text?.Trim();
        CancelTyping();
        if (!string.IsNullOrEmpty(text))
            Apply(_history.Current.With(new Annotation(EditTool.Text, _typingAt.X, _typingAt.Y, _typingAt.X, _typingAt.Y, _color, text)));
    }

    private void CancelTyping()
    {
        if (_typing is null) return;
        _overlay.Children.Remove(_typing);
        _typing = null;
    }

    // ---------- history and drawing ----------

    private void Apply(EditState next)
    {
        _history.Apply(next);
        _dirty = true;
        Redraw();
    }

    private void Undo()
    {
        CancelTyping();
        if (_history.Undo()) { _dirty = true; Redraw(); }
    }

    private void Redo()
    {
        if (_history.Redo()) { _dirty = true; Redraw(); }
    }

    private void Redraw()
    {
        _rendered?.Dispose();
        _rendered = EditorRenderer.Render(_source, _history.Current);
        _image.Source = ToAvalonia(_rendered);
        _undo.IsEnabled = _history.CanUndo;
        _redo.IsEnabled = _history.CanRedo;
        UpdateFrameButton();
        UpdateStatus();
    }

    /// <summary>Copies Skia pixels straight into an Avalonia bitmap (no PNG round trip).</summary>
    internal static WriteableBitmap ToAvalonia(SKBitmap bitmap)
    {
        var wb = new WriteableBitmap(new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        var rowBytes = bitmap.Width * 4;
        var buffer = new byte[rowBytes];
        for (var y = 0; y < bitmap.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, buffer, 0, rowBytes);
            System.Runtime.InteropServices.Marshal.Copy(buffer, 0, fb.Address + y * fb.RowBytes, rowBytes);
        }
        return wb;
    }

    // ---------- save / copy ----------

    private async Task SaveAsync(bool closeAfter)
    {
        CommitTyping();
        try
        {
            var ext = System.IO.Path.GetExtension(_sourcePath).ToLowerInvariant() is ".jpg" or ".jpeg" ? ".jpg" : ".png";
            var output = OutputPaths.Beside(_sourcePath, ext, "edited");
            using (var final = EditorRenderer.Render(_source, _history.Current))
                await Task.Run(() => ImageEngine.SaveEdited(final, output));
            _dirty = false;
            UpdateStatus();
            AppHistory.Record("Edited", output, _sourcePath, _history.Current.Frame is null ? "Edited" : "Edited and beautified");
            _notify("Saved", System.IO.Path.GetFileName(output));
            if (closeAfter) Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _notify("Couldn't save", ex.Message);
        }
    }

    private async Task CopyAsync()
    {
        CommitTyping();
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wheel-edit-{Guid.NewGuid():N}.png");
        try
        {
            using (var final = EditorRenderer.Render(_source, _history.Current))
                await Task.Run(() => ImageEngine.SaveEdited(final, temp));
            await _platform.CopyImageAsync(temp);
            _notify("Copied", "The edited image is on the clipboard");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _notify("Couldn't copy", ex.Message);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    // ---------- keyboard ----------

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_typing is not null) return; // typing in the text box
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (command && e.Key == Key.Z) { if (shift) Redo(); else Undo(); }
        else if (command && e.Key == Key.Y) Redo();
        else if (command && e.Key == Key.S) _ = SaveAsync(closeAfter: false);
        else if (command && e.Key == Key.C) _ = CopyAsync();
        else if (!command && e.Key == Key.Escape) Close();
        else if (!command && e.Key == Key.F) NextFrame();
        else if (!command && Array.Find(Tools, t => t.Key == e.Key) is { Label: not null } match) SelectTool(match.Tool);
        else return;
        e.Handled = true;
    }
}
