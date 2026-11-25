using System;
using SkiaSharp;
using Wheel.Core.Editing;

namespace Wheel.App;

/// <summary>
/// Draws an <see cref="EditState"/> onto a copy of the image with Skia. Used both for the editor's live view and
/// for Save/Copy, so what you see is exactly what you get. Blur and redaction change the pixels themselves.
/// </summary>
internal static class EditorRenderer
{
    public static SKBitmap Render(SKBitmap source, EditState state)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var work = new SKBitmap(info);
        using (var canvas = new SKCanvas(work))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0);
            canvas.Flush();

            float w = source.Width, h = source.Height;
            var stroke = (float)EditGeometry.StrokeWidth(w, h);
            foreach (var a in state.Annotations)
                Draw(canvas, work, a, stroke, w, h);
            canvas.Flush();
        }

        if (state.Crop is { } crop)
            work = CropTo(work, crop);
        if (state.Frame is { } frame)
            work = Framed(work, frame);
        return work;
    }

    private static SKBitmap CropTo(SKBitmap work, PixelRect crop)
    {
        var c = crop.ClampTo(work.Width, work.Height);
        if (c.Width < 1 || c.Height < 1)
            return work;
        var cropped = new SKBitmap(new SKImageInfo((int)c.Width, (int)c.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(cropped))
        {
            canvas.DrawBitmap(work, new SKRect((float)c.X, (float)c.Y, (float)c.Right, (float)c.Bottom),
                              new SKRect(0, 0, cropped.Width, cropped.Height));
            canvas.Flush();
        }
        work.Dispose();
        return cropped;
    }

    /// <summary>Beautify: the image on a gradient, with padding, rounded corners and a soft shadow.</summary>
    private static SKBitmap Framed(SKBitmap content, FrameStyle style)
    {
        var layout = Frames.Layout(content.Width, content.Height);
        var framed = new SKBitmap(new SKImageInfo((int)layout.Width, (int)layout.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(framed))
        {
            using (var background = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(framed.Width, framed.Height),
                    new[] { new SKColor(style.FromArgb), new SKColor(style.ToArgb) }, null, SKShaderTileMode.Clamp),
            })
                canvas.DrawRect(0, 0, framed.Width, framed.Height, background);

            var pad = (float)layout.Padding;
            var radius = (float)layout.Radius;
            var box = new SKRoundRect(SKRect.Create(pad, pad, content.Width, content.Height), radius, radius);

            var shadow = (float)layout.Shadow;
            using (var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0, 0, 0, 0x55),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(1, shadow / 2)),
            })
            {
                canvas.Save();
                canvas.Translate(0, shadow * 0.4f); // light from above: the shadow falls a little below
                canvas.DrawRoundRect(box, paint);
                canvas.Restore();
            }

            canvas.Save();
            canvas.ClipRoundRect(box, SKClipOperation.Intersect, true);
            canvas.DrawBitmap(content, pad, pad);
            canvas.Restore();
            canvas.Flush();
        }
        content.Dispose();
        return framed;
    }

    private static void Draw(SKCanvas canvas, SKBitmap work, Annotation a, float stroke, float w, float h)
    {
        var color = new SKColor(a.ColorArgb);
        if (a.Size > 0) stroke = (float)a.Size; // the width chosen when it was drawn
        var box = a.Bounds.ClampTo(w, h);
        var rect = new SKRect((float)box.X, (float)box.Y, (float)box.Right, (float)box.Bottom);

        switch (a.Tool)
        {
            case EditTool.Arrow:
            {
                var head = stroke * 5;
                var (left, right) = EditGeometry.ArrowHead(a.X1, a.Y1, a.X2, a.Y2, head);
                // The shaft stops inside the head, so the tip stays sharp.
                var len = Math.Sqrt(Math.Pow(a.X2 - a.X1, 2) + Math.Pow(a.Y2 - a.Y1, 2));
                var t = len > 0 ? Math.Max(0, (len - head * 0.6) / len) : 0;
                var endX = (float)(a.X1 + (a.X2 - a.X1) * t);
                var endY = (float)(a.Y1 + (a.Y2 - a.Y1) * t);
                using var line = Stroke(color, stroke);
                canvas.DrawLine((float)a.X1, (float)a.Y1, endX, endY, line);
                using var path = new SKPath();
                path.MoveTo((float)a.X2, (float)a.Y2);
                path.LineTo((float)left.X, (float)left.Y);
                path.LineTo((float)right.X, (float)right.Y);
                path.Close();
                using var fill = Fill(color);
                canvas.DrawPath(path, fill);
                break;
            }
            case EditTool.Line:
            {
                using var line = Stroke(color, stroke);
                canvas.DrawLine((float)a.X1, (float)a.Y1, (float)a.X2, (float)a.Y2, line);
                break;
            }
            case EditTool.Ellipse:
            {
                using var paint = Stroke(color, stroke);
                canvas.DrawOval(rect, paint);
                break;
            }
            case EditTool.Pen when a.Points is { Count: >= 2 } pts:
            {
                // Smoothed through the midpoints, so quick strokes look like ink rather than straight segments.
                using var path = new SKPath();
                path.MoveTo((float)pts[0], (float)pts[1]);
                if (pts.Count == 2) path.LineTo((float)pts[0] + 0.01f, (float)pts[1]);
                for (var i = 2; i + 3 < pts.Count; i += 2)
                    path.QuadTo((float)pts[i], (float)pts[i + 1], (float)(pts[i] + pts[i + 2]) / 2, (float)(pts[i + 1] + pts[i + 3]) / 2);
                if (pts.Count >= 4) path.LineTo((float)pts[^2], (float)pts[^1]);
                using var ink = Stroke(color, stroke);
                canvas.DrawPath(path, ink);
                break;
            }
            case EditTool.Rectangle:
            {
                using var paint = Stroke(color, stroke);
                canvas.DrawRoundRect(rect, stroke, stroke, paint);
                break;
            }
            case EditTool.Highlight:
            {
                using var paint = Fill(color.WithAlpha(0x60));
                canvas.DrawRect(rect, paint);
                break;
            }
            case EditTool.Redact:
            {
                using var paint = Fill(SKColors.Black);
                canvas.DrawRect(rect, paint);
                break;
            }
            case EditTool.Blur:
                Pixelate(canvas, work, box);
                break;
            case EditTool.Step:
            {
                var radius = a.Size > 0 ? Math.Max(12f, stroke * 5) : (float)EditGeometry.StepRadius(w, h);
                using var circle = Fill(color);
                canvas.DrawCircle((float)a.X1, (float)a.Y1, radius, circle);
                using var font = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), radius * 1.2f);
                using var white = Fill(SKColors.White);
                var label = a.Number.ToString();
                using var measure = new SKPaint { Typeface = font.Typeface, TextSize = font.Size };
                var textWidth = measure.MeasureText(label);
                canvas.DrawText(label, (float)a.X1 - textWidth / 2, (float)a.Y1 + radius * 0.42f, font, white);
                break;
            }
            case EditTool.Text when !string.IsNullOrWhiteSpace(a.Text):
            {
                var size = a.Size > 0 ? Math.Max(16f, stroke * 6) : (float)EditGeometry.FontSize(w, h);
                using var font = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), size);
                using var outline = Stroke(new SKColor(0, 0, 0, 170), Math.Max(2, size / 7));
                using var fill = Fill(color);
                var y = (float)a.Y1 + size;
                var typeface = TypefaceFor(a.Text!);
                foreach (var line in a.Text!.Split('\n'))
                {
                    if (!TryDrawShaped(canvas, line, (float)a.X1, y, typeface, size, new SKColor(0, 0, 0, 170), color, Math.Max(2, size / 7)))
                    {
                        canvas.DrawText(line, (float)a.X1, y, font, outline); // dark edge keeps text readable on any background
                        canvas.DrawText(line, (float)a.X1, y, font, fill);
                    }
                    y += size * 1.25f;
                }
                break;
            }
        }
        canvas.Flush();
    }

    /// <summary>A bold typeface with the text's script: the default one, or a font that has Arabic letters.</summary>
    private static SKTypeface TypefaceFor(string text)
    {
        foreach (var c in text)
        {
            if (!Wheel.Core.Ocr.OcrText.IsArabic(c)) continue;
            var arabic = SKFontManager.Default.MatchCharacter(null, SKFontStyle.Bold, null, c);
            if (arabic is not null) return arabic;
            break;
        }
        return SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
    }

    /// <summary>
    /// Draws text through HarfBuzz, which joins Arabic letters and orders right-to-left runs.
    /// False when shaping isn't available, so the caller can draw it plainly.
    /// </summary>
    private static bool TryDrawShaped(SKCanvas canvas, string line, float x, float y, SKTypeface typeface, float size,
                                      SKColor edge, SKColor color, float edgeWidth)
    {
        try
        {
            DrawShaped(canvas, line, x, y, typeface, size, edge, color, edgeWidth);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeLoadException
                                       or MissingMethodException or System.IO.FileNotFoundException)
        {
            Log.Write(ex);
            return false;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void DrawShaped(SKCanvas canvas, string line, float x, float y, SKTypeface typeface, float size,
                                   SKColor edge, SKColor color, float edgeWidth)
    {
        using var shaper = new SkiaSharp.HarfBuzz.SKShaper(typeface);
        using var outline = new SKPaint
        {
            Typeface = typeface, TextSize = size, IsAntialias = true, Color = edge,
            Style = SKPaintStyle.Stroke, StrokeWidth = edgeWidth, StrokeJoin = SKStrokeJoin.Round,
        };
        using var fill = new SKPaint { Typeface = typeface, TextSize = size, IsAntialias = true, Color = color, Style = SKPaintStyle.Fill };
        SkiaSharp.HarfBuzz.CanvasExtensions.DrawShapedText(canvas, shaper, line, x, y, outline);
        SkiaSharp.HarfBuzz.CanvasExtensions.DrawShapedText(canvas, shaper, line, x, y, fill);
    }

    /// <summary>Shrinks the region to coarse blocks and scales it back up with no smoothing: the original detail is gone.</summary>
    private static void Pixelate(SKCanvas canvas, SKBitmap work, PixelRect region)
    {
        if (region.Width < 2 || region.Height < 2) return;
        canvas.Flush();
        var block = EditGeometry.BlurBlock(region);
        var src = new SKRectI((int)region.X, (int)region.Y, (int)Math.Ceiling(region.Right), (int)Math.Ceiling(region.Bottom));
        int sw = Math.Max(1, src.Width / block), sh = Math.Max(1, src.Height / block);

        using var small = new SKBitmap(new SKImageInfo(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var smallCanvas = new SKCanvas(small))
        using (var smooth = new SKPaint { FilterQuality = SKFilterQuality.Medium })
        {
            smallCanvas.DrawBitmap(work, src, new SKRect(0, 0, sw, sh), smooth);
            smallCanvas.Flush();
        }
        using var blocky = new SKPaint { FilterQuality = SKFilterQuality.None };
        canvas.DrawBitmap(small, new SKRect(0, 0, sw, sh), src, blocky);
    }

    private static SKPaint Stroke(SKColor color, float width) => new()
    {
        Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true,
        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    };

    private static SKPaint Fill(SKColor color) => new() { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
}
