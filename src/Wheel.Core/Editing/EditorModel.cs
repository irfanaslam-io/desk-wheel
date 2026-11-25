namespace Wheel.Core.Editing;

/// <summary>Tools in the screenshot editor.</summary>
public enum EditTool
{
    Arrow,
    Rectangle,
    Highlight,
    Text,
    Step,
    /// <summary>Pixelate a region (visual masking).</summary>
    Blur,
    /// <summary>Solid black box. Burned into the pixels on export, so the hidden content is gone, not just covered.</summary>
    Redact,
    Crop,
    /// <summary>Freehand drawing (the points in <see cref="Annotation.Points"/>).</summary>
    Pen,
    Line,
    /// <summary>An oval inside the dragged box.</summary>
    Ellipse,
}

/// <summary>A rectangle in image pixels with positive size.</summary>
public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    /// <summary>The rectangle spanned by two corners dragged in any direction.</summary>
    public static PixelRect FromCorners(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    /// <summary>Kept inside 0..width × 0..height.</summary>
    public PixelRect ClampTo(double width, double height)
    {
        var x = Math.Clamp(X, 0, width);
        var y = Math.Clamp(Y, 0, height);
        return new PixelRect(x, y, Math.Clamp(Right, 0, width) - x, Math.Clamp(Bottom, 0, height) - y);
    }
}

/// <summary>
/// One mark on the image, in original-image pixel coordinates (so it survives cropping and zooming).
/// Arrows and lines use start → end; boxes and ovals use the two corners; text and steps use the start point;
/// freehand uses <paramref name="Points"/> (x0, y0, x1, y1, …). <paramref name="Size"/> is the line width in
/// pixels (text and step markers scale with it); 0 means "a size that suits the image".
/// </summary>
public sealed record Annotation(EditTool Tool, double X1, double Y1, double X2, double Y2, uint ColorArgb, string? Text = null, int Number = 0,
                                double Size = 0, IReadOnlyList<double>? Points = null)
{
    public PixelRect Bounds => PixelRect.FromCorners(X1, Y1, X2, Y2);

    /// <summary>A freehand mark from its points (bounds included, so it crops and clamps like the rest).</summary>
    public static Annotation Freehand(IReadOnlyList<(double X, double Y)> points, uint color, double size)
    {
        var flat = new double[points.Count * 2];
        for (var i = 0; i < points.Count; i++) { flat[2 * i] = points[i].X; flat[2 * i + 1] = points[i].Y; }
        return new Annotation(EditTool.Pen, points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y), color,
                              Size: size, Points: flat);
    }
}

/// <summary>Everything that makes up an edit: the marks in drawing order, an optional crop, and an optional frame.</summary>
public sealed record EditState(IReadOnlyList<Annotation> Annotations, PixelRect? Crop, FrameStyle? Frame = null)
{
    public static readonly EditState Empty = new(Array.Empty<Annotation>(), null);

    public EditState With(Annotation annotation) => this with { Annotations = [.. Annotations, annotation] };

    /// <summary>The number the next step marker gets (1, 2, 3…).</summary>
    public int NextStepNumber => Annotations.Count(a => a.Tool == EditTool.Step) + 1;
}

/// <summary>Undo / redo over immutable states. Any new edit clears the redo list.</summary>
public sealed class EditHistory
{
    private readonly Stack<EditState> _undo = new();
    private readonly Stack<EditState> _redo = new();

    public EditState Current { get; private set; } = EditState.Empty;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Apply(EditState next)
    {
        _undo.Push(Current);
        _redo.Clear();
        Current = next;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(Current);
        Current = _undo.Pop();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Push(Current);
        Current = _redo.Pop();
        return true;
    }
}

/// <summary>Sizes and shapes shared by the on-screen preview and the exported image, so they match exactly.</summary>
public static class EditGeometry
{
    /// <summary>Line width that reads well at any screenshot size: about 1/320 of the longer side, at least 3 px.</summary>
    public static double StrokeWidth(double imageWidth, double imageHeight) =>
        Math.Max(3, Math.Round(Math.Max(imageWidth, imageHeight) / 320));

    /// <summary>Text size: 8× the stroke, at least 20 px.</summary>
    public static double FontSize(double imageWidth, double imageHeight) => Math.Max(20, StrokeWidth(imageWidth, imageHeight) * 8);

    /// <summary>Step-marker circle radius.</summary>
    public static double StepRadius(double imageWidth, double imageHeight) => Math.Max(14, StrokeWidth(imageWidth, imageHeight) * 5);

    /// <summary>Pixelation block size: coarse enough that text can't be read back (at least 10 px, ~1/12 of the region).</summary>
    public static int BlurBlock(PixelRect region) => (int)Math.Max(10, Math.Round(Math.Max(region.Width, region.Height) / 12));

    /// <summary>The two back corners of an arrowhead at (x2, y2), pointing away from (x1, y1).</summary>
    public static ((double X, double Y) Left, (double X, double Y) Right) ArrowHead(double x1, double y1, double x2, double y2, double size)
    {
        var angle = Math.Atan2(y2 - y1, x2 - x1);
        const double spread = Math.PI / 7; // about 26° each side
        return ((x2 - size * Math.Cos(angle - spread), y2 - size * Math.Sin(angle - spread)),
                (x2 - size * Math.Cos(angle + spread), y2 - size * Math.Sin(angle + spread)));
    }

    /// <summary>
    /// Maps a point on the editor's view to original-image pixels. The view shows the cropped image
    /// (or the whole image) scaled by <paramref name="scale"/>.
    /// </summary>
    public static (double X, double Y) ViewToImage(double viewX, double viewY, double scale, PixelRect? crop) =>
        (viewX / scale + (crop?.X ?? 0), viewY / scale + (crop?.Y ?? 0));

    /// <summary>Marks smaller than this (in image pixels) are treated as accidental clicks and dropped.</summary>
    public const double MinimumSize = 4;
}

/// <summary>A "Beautify" background: a diagonal gradient behind the image.</summary>
public sealed record FrameStyle(string Name, uint FromArgb, uint ToArgb);

/// <summary>Where the image sits inside a frame, in pixels of the final picture.</summary>
public readonly record struct FrameLayout(double Width, double Height, double Padding, double Radius, double Shadow);

/// <summary>Beautify: puts a screenshot on a background with padding, rounded corners and a soft shadow.</summary>
public static class Frames
{
    public static readonly FrameStyle[] Presets =
    [
        new("Sunset", 0xFFFFB26B, 0xFFE8622C),
        new("Ocean", 0xFF6DD5FA, 0xFF2563EB),
        new("Grape", 0xFFC084FC, 0xFF6D28D9),
        new("Mint", 0xFF9BE7C4, 0xFF0F9D78),
        new("Graphite", 0xFF4B5563, 0xFF111827),
        new("Paper", 0xFFF8FAFC, 0xFFE2E8F0),
    ];

    /// <summary>The style after <paramref name="current"/> in the cycle Off → presets → Off.</summary>
    public static FrameStyle? Next(FrameStyle? current)
    {
        if (current is null) return Presets[0];
        var i = Array.FindIndex(Presets, p => p.Name == current.Name);
        return i < 0 || i == Presets.Length - 1 ? null : Presets[i + 1];
    }

    /// <summary>
    /// Padding is 7% of the longer side (40–200 px), corners 1.2% of the shorter side (8–28 px),
    /// so a small snippet and a full-screen capture both look balanced.
    /// </summary>
    public static FrameLayout Layout(double contentWidth, double contentHeight)
    {
        var padding = Math.Round(Math.Clamp(Math.Max(contentWidth, contentHeight) * 0.07, 40, 200));
        var radius = Math.Round(Math.Clamp(Math.Min(contentWidth, contentHeight) * 0.012, 8, 28));
        var shadow = Math.Round(padding * 0.35);
        return new FrameLayout(contentWidth + padding * 2, contentHeight + padding * 2, padding, radius, shadow);
    }
}
