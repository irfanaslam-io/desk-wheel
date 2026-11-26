using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Wheel.Core.Disk;

namespace Wheel.App;

/// <summary>
/// The disk chart: the folder in the middle, what's inside it in rings around it, each wedge as big as its share.
/// Hover a wedge to see what it is, click a folder to go into it, click the middle to go back up.
/// </summary>
internal sealed class SunburstView : Control
{
    private const int MaxRings = 6;
    private static readonly Color FreeColor = Color.FromRgb(0x5A, 0x5D, 0x63);

    private List<SunburstArc> _arcs = [];
    private DiskNode? _centre;
    private long _freeSpace;
    private SunburstArc? _hover;
    private HashSet<DiskNode> _selected = [];
    private DiskNode? _highlight;
    private int _rings = MaxRings;

    /// <summary>A folder wedge was clicked: show what's inside it.</summary>
    public event Action<DiskNode>? Open;
    /// <summary>A file wedge was clicked (Ctrl / ⌘ adds it to the selection).</summary>
    public event Action<DiskNode, bool>? Pick;
    /// <summary>The middle was clicked: go up a folder.</summary>
    public event Action? Up;
    /// <summary>Right-click on a wedge.</summary>
    public event Action<DiskNode, PointerEventArgs>? Menu;

    public SunburstView()
    {
        ClipToBounds = true;
        Focusable = false;
    }

    public void Show(DiskNode centre, long freeSpace)
    {
        _centre = centre;
        _freeSpace = freeSpace;
        _hover = null;
        _arcs = SunburstLayout.Layout(centre, MaxRings, minSweep: 0.003, freeSpace);
        _rings = Math.Max(1, _arcs.Count == 0 ? 1 : _arcs.Max(a => a.Ring));
        InvalidateVisual();
    }

    public void SetSelection(IEnumerable<DiskNode> nodes)
    {
        _selected = nodes.ToHashSet();
        InvalidateVisual();
    }

    /// <summary>The item the pointer is over in the list, outlined here too.</summary>
    public DiskNode? Highlight
    {
        get => _highlight;
        set
        {
            if (ReferenceEquals(_highlight, value)) return;
            _highlight = value;
            InvalidateVisual();
        }
    }

    // ---------------------------------------------------------------- geometry

    private (Point Centre, double Inner, double Ring) Geometry()
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var inner = size * 0.14;
        var ring = Math.Max(4, (size / 2 - inner - 6) / Math.Max(_rings, 3));
        return (centre, inner, ring);
    }

    private static Point At(Point c, double radius, double turn)
    {
        var a = turn * Math.PI * 2 - Math.PI / 2;
        return new Point(c.X + radius * Math.Cos(a), c.Y + radius * Math.Sin(a));
    }

    private static StreamGeometry Wedge(Point c, double r0, double r1, double start, double sweep)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        if (sweep >= 0.9999)
        {
            // A whole ring: two half circles (an arc can't end where it starts).
            ctx.BeginFigure(At(c, r1, 0), true);
            ctx.ArcTo(At(c, r1, 0.5), new Size(r1, r1), 0, false, SweepDirection.Clockwise);
            ctx.ArcTo(At(c, r1, 0), new Size(r1, r1), 0, false, SweepDirection.Clockwise);
            ctx.LineTo(At(c, r0, 0));
            ctx.ArcTo(At(c, r0, 0.5), new Size(r0, r0), 0, false, SweepDirection.CounterClockwise);
            ctx.ArcTo(At(c, r0, 0), new Size(r0, r0), 0, false, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
            return g;
        }
        var large = sweep > 0.5;
        ctx.BeginFigure(At(c, r1, start), true);
        ctx.ArcTo(At(c, r1, start + sweep), new Size(r1, r1), 0, large, SweepDirection.Clockwise);
        ctx.LineTo(At(c, r0, start + sweep));
        ctx.ArcTo(At(c, r0, start), new Size(r0, r0), 0, large, SweepDirection.CounterClockwise);
        ctx.EndFigure(true);
        return g;
    }

    /// <summary>Bright in the first ring, a little deeper further out; files paler than folders.</summary>
    private static Color ColorFor(SunburstArc arc, bool hover)
    {
        if (arc.IsFreeSpace) return hover ? Color.FromRgb(0x6C, 0x70, 0x77) : FreeColor;
        var light = Ui.IsLight;
        var s = arc.IsGroup ? 0.12 : arc.Node is { IsFolder: false } ? 0.42 : 0.62;
        var v = (light ? 0.90 : 0.82) - (arc.Ring - 1) * 0.06;
        if (arc.IsGroup) v -= 0.12;
        if (hover) v = Math.Min(1, v + 0.12);
        return Hsv(arc.Hue, s, v);
    }

    private static Color Hsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360 / 60;
        var i = (int)Math.Floor(h);
        var f = h - i;
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    // ---------------------------------------------------------------- drawing

    public override void Render(DrawingContext dc)
    {
        if (_centre is null) return;
        var (c, inner, ring) = Geometry();
        var gap = new Pen(new SolidColorBrush(Ui.IsLight ? Colors.White : Color.FromRgb(0x1C, 0x1D, 0x21)), 1);

        foreach (var arc in _arcs)
        {
            var r0 = inner + (arc.Ring - 1) * ring;
            var hover = ReferenceEquals(arc, _hover) || (arc.Node is not null && ReferenceEquals(arc.Node, _highlight));
            dc.DrawGeometry(new SolidColorBrush(ColorFor(arc, hover)), gap, Wedge(c, r0, r0 + ring, arc.Start, arc.Sweep));
        }

        // Selected items: outlined (drawn after, so the outline isn't covered by a neighbour).
        if (_selected.Count > 0)
        {
            var outline = new Pen(new SolidColorBrush(Ui.IsLight ? Colors.Black : Colors.White), 2.5);
            foreach (var arc in _arcs.Where(a => a.Node is not null && _selected.Contains(a.Node)))
            {
                var r0 = inner + (arc.Ring - 1) * ring;
                dc.DrawGeometry(null, outline, Wedge(c, r0, r0 + ring, arc.Start, arc.Sweep));
            }
        }

        foreach (var arc in _arcs)
            Label(dc, c, inner + (arc.Ring - 1) * ring, ring, arc);

        // The middle: the folder, or what's under the pointer.
        var middle = _hover is { } h && (h.Node is not null || h.IsGroup || h.IsFreeSpace) ? h : null;
        dc.DrawEllipse(new SolidColorBrush(Ui.IsLight ? Color.FromRgb(0xE4, 0xE6, 0xEA) : Color.FromRgb(0x2A, 0x2C, 0x31)),
            new Pen(Ui.Line, 1), c, inner - 3, inner - 3);
        var title = middle switch
        {
            { IsFreeSpace: true } => "Free space",
            { IsGroup: true } g => $"{g.GroupCount:N0} small items",
            { Node: { } n } => n.Name,
            _ => _centre.Name,
        };
        var size = DiskScanner.FormatSize(middle?.Size ?? _centre.Size);
        var detail = middle is null
            ? _freeSpace > 0 ? $"{DiskScanner.FormatSize(_freeSpace)} free" : $"{_centre.FileCount:N0} files"
            : middle.Node is { IsFolder: true } folder ? $"{folder.FileCount:N0} files"
            : middle.Node is { } file ? file.Kind : "";
        var width = (inner - 10) * 2;
        DrawCentred(dc, title, 14, FontWeight.SemiBold, Ui.Text, new Point(c.X, c.Y - 18), width);
        DrawCentred(dc, size, 13, FontWeight.Normal, Ui.Text, new Point(c.X, c.Y + 2), width);
        DrawCentred(dc, detail, 11.5, FontWeight.Normal, Ui.Muted, new Point(c.X, c.Y + 20), width);
    }

    private static FormattedText Text(string text, double size, FontWeight weight, IBrush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);

    private static void DrawCentred(DrawingContext dc, string text, double size, FontWeight weight, IBrush brush, Point at, double maxWidth)
    {
        if (text.Length == 0) return;
        var t = Fit(text, size, weight, brush, maxWidth);
        if (t is null) return;
        dc.DrawText(t, new Point(at.X - t.Width / 2, at.Y - t.Height / 2));
    }

    /// <summary>The text, shortened with "…" to fit; null if even a few letters don't.</summary>
    private static FormattedText? Fit(string text, double size, FontWeight weight, IBrush brush, double maxWidth)
    {
        var t = Text(text, size, weight, brush);
        if (t.Width <= maxWidth) return t;
        for (var n = text.Length - 1; n >= 3; n--)
        {
            t = Text(text[..n].TrimEnd() + "…", size, weight, brush);
            if (t.Width <= maxWidth) return t;
        }
        return null;
    }

    /// <summary>
    /// The name on the wedge: along the ring when the wedge is wide, across it when it's long and thin,
    /// turned so it never reads upside down.
    /// </summary>
    private static void Label(DrawingContext dc, Point c, double r0, double ring, SunburstArc arc)
    {
        var name = arc.IsFreeSpace ? "Free Space" : arc.Node?.Name;
        if (name is null) return;
        const double fontSize = 11;
        var mid = arc.Start + arc.Sweep / 2;
        var rMid = r0 + ring / 2;
        var along = arc.Sweep * Math.PI * 2 * rMid; // length of the wedge's middle line
        var brush = Brushes.Black;
        if (arc.IsFreeSpace || (!Ui.IsLight && arc.Ring > 3)) brush = Brushes.White;

        var angle = mid * 360 - 90; // degrees, 0 = 3 o'clock
        FormattedText? t;
        double rotation;
        if (along > ring * 1.4 && (t = Fit(name, fontSize, FontWeight.Medium, brush, along - 10)) is not null && ring > t.Height + 4)
        {
            rotation = angle + 90; // along the ring
            if (mid is > 0.25 and < 0.75) rotation += 180;
        }
        else if (along > fontSize + 4 && (t = Fit(name, fontSize, FontWeight.Medium, brush, ring - 8)) is not null)
        {
            rotation = angle; // across: from the middle outwards
            if (mid > 0.5) rotation += 180;
        }
        else return;

        var p = At(c, rMid, mid);
        using (dc.PushTransform(Matrix.CreateTranslation(-t.Width / 2, -t.Height / 2)
                                * Matrix.CreateRotation(rotation * Math.PI / 180)
                                * Matrix.CreateTranslation(p.X, p.Y)))
            dc.DrawText(t, new Point(0, 0));
    }

    // ---------------------------------------------------------------- pointer

    /// <summary>The wedge under a point; ring 0 is the middle.</summary>
    private (int Ring, SunburstArc? Arc) HitTest(Point p)
    {
        var (c, inner, ring) = Geometry();
        var dx = p.X - c.X;
        var dy = p.Y - c.Y;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < inner) return (0, null);
        var index = (int)((r - inner) / ring) + 1;
        if (index > _rings) return (-1, null);
        var turn = (Math.Atan2(dy, dx) + Math.PI / 2) / (Math.PI * 2);
        return (index, SunburstLayout.HitTest(_arcs, index, turn));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var (ringIndex, arc) = HitTest(e.GetPosition(this));
        Cursor = arc?.Node is not null || ringIndex == 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        if (ReferenceEquals(arc, _hover)) return;
        _hover = arc;
        if (arc?.Node is { } node) ToolTip.SetTip(this, node.FullPath);
        else ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        var (ringIndex, arc) = HitTest(point.Position);
        if (point.Properties.IsRightButtonPressed)
        {
            if (arc?.Node is { } n) Menu?.Invoke(n, e);
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (ringIndex == 0) Up?.Invoke();
        else if (arc?.Node is { IsFolder: true } folder && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta)) Open?.Invoke(folder);
        else if (arc?.Node is { } node) Pick?.Invoke(node, e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
        e.Handled = true;
    }
}
