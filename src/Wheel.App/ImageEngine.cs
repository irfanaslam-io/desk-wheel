using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SkiaSharp;
using Wheel.Core;
using Wheel.Core.Converters;

namespace Wheel.App;

/// <summary>
/// Image conversions and tools, the same on macOS and Windows.
/// SkiaSharp (shipped with Avalonia) reads PNG/JPG/WebP/GIF/BMP/ICO and writes PNG/JPG/WebP/ICO.
/// Anything else goes through a temporary PNG and a helper: sips on macOS (built in), ffmpeg on Windows.
/// </summary>
internal static class ImageEngine
{
    private const string Sips = "/usr/bin/sips";

    /// <summary>JPG / WebP quality for conversions (Settings → Image quality). Compression tools use their own lower value.</summary>
    public static int Quality { get; set; } = 90;

    public static Task<JobResult> RunAsync(WheelOption option, string input, Wheel.Core.Files.ResizeSpec? size = null) => Task.Run(async () =>
    {
        var temp = Path.Combine(Path.GetTempPath(), "wheel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            return await Run(option, input, temp, size);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException
                                   or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            return JobResult.Fail(Friendly(ex, input));
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    });

    private static async Task<JobResult> Run(WheelOption option, string input, string temp, Wheel.Core.Files.ResizeSpec? size)
    {
        var bitmap = await DecodeAsync(input, temp);
        try
        {
            if (option.Id == "img.icons")
                return JobResult.Ok(await MakeIconSetAsync(bitmap, input, temp));

            if (option.Id == "img.favicon")
                return JobResult.Ok(MakeFavicons(bitmap, input));

            if (option.Id == "img.to-ico")
            {
                var ico = OutputPaths.Beside(input, ".ico");
                WriteIco(bitmap, ico);
                return JobResult.Ok(ico);
            }

            var extension = option.TargetExtension;
            var quality = Math.Clamp(Quality, 50, 100);

            switch (option.Id)
            {
                case "img.compress":
                    quality = 70;
                    extension = ".jpg";
                    break;
                case "img.resize-half":
                    Replace(ref bitmap, Half(bitmap));
                    extension = EncodableExtension(input);
                    break;
                case "img.resize" when size is not null:
                {
                    var (w, h) = size.For(bitmap.Width, bitmap.Height);
                    Replace(ref bitmap, Scale(bitmap, w, h));
                    extension = EncodableExtension(input);
                    var resized = OutputPaths.Beside(input, extension, $"{w}x{h}");
                    await SaveAsync(bitmap, resized, quality, temp);
                    return JobResult.Ok(resized);
                }
                case "img.rotate":
                    Replace(ref bitmap, Orient(bitmap, SKEncodedOrigin.RightTop)); // 90° clockwise
                    extension = EncodableExtension(input);
                    break;
                case "img.square":
                {
                    var side = Math.Min(bitmap.Width, bitmap.Height);
                    var square = NewBitmap(side, side);
                    using (var canvas = new SKCanvas(square))
                        canvas.DrawBitmap(bitmap, new SKRect((bitmap.Width - side) / 2f, (bitmap.Height - side) / 2f, (bitmap.Width + side) / 2f, (bitmap.Height + side) / 2f), new SKRect(0, 0, side, side));
                    Replace(ref bitmap, square);
                    extension = EncodableExtension(input);
                    break;
                }
                case "img.flip":
                    Replace(ref bitmap, Orient(bitmap, SKEncodedOrigin.TopRight));
                    extension = EncodableExtension(input);
                    break;
                case "img.grey":
                    Replace(ref bitmap, Grey(bitmap));
                    extension = EncodableExtension(input);
                    break;
                case "img.strip":
                    // Re-encoding from pixels writes no EXIF, GPS or XMP.
                    extension = EncodableExtension(input);
                    break;
            }

            var output = OutputPaths.Beside(input, extension, Suffixes.For(option));
            await SaveAsync(bitmap, output, quality, temp);
            return JobResult.Ok(output);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    // ---------- grey, watermark, combine, favicons ----------

    private static SKBitmap Grey(SKBitmap src)
    {
        var dst = NewBitmap(src.Width, src.Height);
        using var canvas = new SKCanvas(dst);
        using var paint = new SKPaint
        {
            // Rec. 709 luminance: how bright each colour looks to people.
            ColorFilter = SKColorFilter.CreateColorMatrix([
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0.2126f, 0.7152f, 0.0722f, 0, 0,
                0, 0, 0, 1, 0]),
        };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(src, 0, 0, paint);
        canvas.Flush();
        return dst;
    }

    /// <summary>"photo (watermarked).jpg": the text in the bottom-right corner, white with a soft dark edge so it shows on any picture.</summary>
    public static Task<JobResult> WatermarkAsync(string input, string text) => Task.Run(async () =>
    {
        var temp = Path.Combine(Path.GetTempPath(), "wheel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using var bitmap = await DecodeAsync(input, temp);
            using var canvas = new SKCanvas(bitmap);
            var typeface = EditorRenderer.TypefaceFor(text);
            var size = (float)Math.Clamp(Math.Sqrt(bitmap.Width * (double)bitmap.Height) * 0.045, 12, 400);
            var width = EditorRenderer.MeasureShaped(text, typeface, size);
            var margin = size * 0.8f;
            float x = bitmap.Width - width - margin, y = bitmap.Height - margin;
            var edge = new SKColor(0, 0, 0, 120);
            var fill = new SKColor(255, 255, 255, 215);
            if (!EditorRenderer.TryDrawShaped(canvas, text, x, y, typeface, size, edge, fill, size / 9))
            {
                using var outline = new SKPaint { Typeface = typeface, TextSize = size, IsAntialias = true, Color = edge, Style = SKPaintStyle.Stroke, StrokeWidth = size / 9 };
                using var paint = new SKPaint { Typeface = typeface, TextSize = size, IsAntialias = true, Color = fill };
                canvas.DrawText(text, x, y, outline);
                canvas.DrawText(text, x, y, paint);
            }
            canvas.Flush();
            var output = OutputPaths.Beside(input, EncodableExtension(input), "watermarked");
            await SaveAsync(bitmap, output, Math.Clamp(Quality, 50, 100), temp);
            return JobResult.Ok(output);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            return JobResult.Fail(Friendly(ex, input));
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    });

    /// <summary>
    /// For PDF watermarks: the text drawn once, large, grey and see-through, as PNG bytes (any language, Arabic joined
    /// properly). PdfTools places it across each page.
    /// </summary>
    public static byte[] WatermarkPicture(string text)
    {
        var typeface = EditorRenderer.TypefaceFor(text);
        const float size = 220;
        var width = (int)Math.Ceiling(EditorRenderer.MeasureShaped(text, typeface, size) + size * 0.6f);
        var height = (int)Math.Ceiling(size * 1.45f);
        using var bitmap = NewBitmap(Math.Max(1, width), height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            var grey = new SKColor(120, 120, 120, 70);
            if (!EditorRenderer.TryDrawShaped(canvas, text, size * 0.3f, size * 1.1f, typeface, size, SKColors.Transparent, grey, 0))
            {
                using var paint = new SKPaint { Typeface = typeface, TextSize = size, IsAntialias = true, Color = grey };
                canvas.DrawText(text, size * 0.3f, size * 1.1f, paint);
            }
            canvas.Flush();
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// One picture from several: "row" (side by side, same height), "column" (stacked, same width) or "grid".
    /// Saved as "Combined (row).jpg" beside the first; PNG when any picture has transparency.
    /// </summary>
    public static Task<JobResult> CombineAsync(IReadOnlyList<string> inputs, string layout) => Task.Run(async () =>
    {
        var temp = Path.Combine(Path.GetTempPath(), "wheel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var pictures = new List<SKBitmap>();
        try
        {
            var ordered = inputs.OrderBy(Path.GetFileName, Wheel.Core.Files.NaturalComparer.Instance).ToList();
            foreach (var input in ordered) pictures.Add(await DecodeAsync(input, temp));
            const int MaxSide = 12000;
            var gap = 0;
            List<(SKBitmap Picture, SKRect Where)> placed = new();
            int width, height;
            switch (layout)
            {
                case "row":
                {
                    var h = Math.Min(pictures.Min(p => p.Height), 2400);
                    float x = 0;
                    foreach (var p in pictures) { var w = p.Width * (float)h / p.Height; placed.Add((p, SKRect.Create(x, 0, w, h))); x += w + gap; }
                    (width, height) = ((int)Math.Ceiling(x - gap), h);
                    break;
                }
                case "column":
                {
                    var w = Math.Min(pictures.Min(p => p.Width), 2400);
                    float y = 0;
                    foreach (var p in pictures) { var h = p.Height * (float)w / p.Width; placed.Add((p, SKRect.Create(0, y, w, h))); y += h + gap; }
                    (width, height) = (w, (int)Math.Ceiling(y - gap));
                    break;
                }
                default: // grid: near-square, every cell the same size, pictures fitted inside
                {
                    var columns = (int)Math.Ceiling(Math.Sqrt(pictures.Count));
                    var rows = (int)Math.Ceiling(pictures.Count / (double)columns);
                    var cell = Math.Min(1200, pictures.Min(p => Math.Max(p.Width, p.Height)));
                    gap = Math.Max(4, cell / 60);
                    for (var i = 0; i < pictures.Count; i++)
                    {
                        var p = pictures[i];
                        var scale = Math.Min((float)cell / p.Width, (float)cell / p.Height);
                        float w = p.Width * scale, h = p.Height * scale;
                        float cx = (i % columns) * (cell + gap), cy = (i / columns) * (cell + gap);
                        placed.Add((p, SKRect.Create(cx + (cell - w) / 2, cy + (cell - h) / 2, w, h)));
                    }
                    (width, height) = (columns * cell + (columns - 1) * gap, rows * cell + (rows - 1) * gap);
                    break;
                }
            }
            var shrink = Math.Min(1f, MaxSide / (float)Math.Max(width, height));
            using var result = NewBitmap(Math.Max(1, (int)(width * shrink)), Math.Max(1, (int)(height * shrink)));
            using (var canvas = new SKCanvas(result))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            {
                canvas.Clear(layout == "grid" ? SKColors.White : SKColors.Transparent);
                canvas.Scale(shrink);
                foreach (var (picture, where) in placed) canvas.DrawBitmap(picture, where, paint);
                canvas.Flush();
            }
            var transparent = pictures.Any(p => p.AlphaType != SKAlphaType.Opaque && Path.GetExtension(inputs[0]).ToLowerInvariant() is ".png" or ".webp" or ".gif");
            var output = OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(ordered[0]), $"Combined ({layout}){(transparent ? ".png" : ".jpg")}"));
            await SaveAsync(result, output, Math.Clamp(Quality, 50, 100), temp);
            return JobResult.Ok(output);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            return JobResult.Fail(ex is OutOfMemoryException ? "The pictures are too big to combine." : ex.Message);
        }
        finally
        {
            foreach (var p in pictures) p.Dispose();
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    });

    /// <summary>
    /// "name (favicon)" folder for a website: favicon.ico (16, 32, 48), favicon-16x16.png, favicon-32x32.png,
    /// apple-touch-icon.png (180, on white), android-chrome-192x192.png, android-chrome-512x512.png,
    /// site.webmanifest, and favicon.html with the lines to paste into the page's &lt;head&gt;.
    /// </summary>
    private static string MakeFavicons(SKBitmap src, string input)
    {
        var folder = OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(input), OutputPaths.StemOf(input) + " (favicon)"));
        Directory.CreateDirectory(folder);
        void Png(int size, string name, bool onWhite = false)
        {
            using var icon = Square(src, size);
            using var final = onWhite ? OnWhite(icon) : null;
            Encode(final ?? icon, SKEncodedImageFormat.Png, 100, Path.Combine(folder, name));
        }
        Png(16, "favicon-16x16.png");
        Png(32, "favicon-32x32.png");
        Png(180, "apple-touch-icon.png", onWhite: true); // iOS shows transparency as black
        Png(192, "android-chrome-192x192.png");
        Png(512, "android-chrome-512x512.png");
        WriteIcoSizes(src, Path.Combine(folder, "favicon.ico"), [16, 32, 48]);
        File.WriteAllText(Path.Combine(folder, "site.webmanifest"), """
            {
              "icons": [
                { "src": "/android-chrome-192x192.png", "sizes": "192x192", "type": "image/png" },
                { "src": "/android-chrome-512x512.png", "sizes": "512x512", "type": "image/png" }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(folder, "favicon.html"), """
            <!-- Put the files in your website's top folder, then paste these lines into <head>: -->
            <link rel="icon" href="/favicon.ico" sizes="any">
            <link rel="icon" type="image/png" sizes="32x32" href="/favicon-32x32.png">
            <link rel="icon" type="image/png" sizes="16x16" href="/favicon-16x16.png">
            <link rel="apple-touch-icon" href="/apple-touch-icon.png">
            <link rel="manifest" href="/site.webmanifest">
            """);
        return folder;
    }

    /// <summary>An .ico holding several sizes (each a PNG), as browsers and Windows expect for favicons.</summary>
    private static void WriteIcoSizes(SKBitmap src, string path, int[] sizes)
    {
        var images = sizes.Select(size =>
        {
            using var icon = Square(src, size);
            using var data = icon.Encode(SKEncodedImageFormat.Png, 100);
            return (size, bytes: data.ToArray());
        }).ToList();
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, bytes) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write((uint)bytes.Length);
            w.Write((uint)offset);
            offset += bytes.Length;
        }
        foreach (var (_, bytes) in images) w.Write(bytes);
    }

    /// <summary>
    /// PDF Grey and Flatten: every page rendered at 200 DPI and put back as a JPEG picture page of the same size.
    /// Grey also turns it black and white. Forms, comments and text are locked in.
    /// </summary>
    public static Task<JobResult> RebuildPdfAsync(string input, bool grey) => Task.Run(() =>
    {
        var output = OutputPaths.Beside(input, ".pdf", grey ? "grey" : "flattened");
        try
        {
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            using (var document = SKDocument.CreatePdf(stream))
            {
                PdfConverter.RenderEach(input, 200, (_, wPt, hPt, width, height, bgra, stride) =>
                {
                    using var page = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
                    var dst = page.GetPixels();
                    for (var y = 0; y < height; y++)
                        Marshal.Copy(bgra, y * stride, dst + y * page.RowBytes, width * 4);
                    page.NotifyPixelsChanged();
                    using var shown = grey ? Grey(page) : null;
                    // Encoded as JPEG first: Skia's PDF writer then embeds the JPEG as it is (small files).
                    using var jpeg = (shown ?? page).Encode(SKEncodedImageFormat.Jpeg, 82);
                    using var image = SKImage.FromEncodedData(jpeg);
                    var canvas = document.BeginPage(wPt, hPt);
                    canvas.DrawImage(image, new SKRect(0, 0, wPt, hPt));
                    document.EndPage();
                });
                document.Close();
            }
            return JobResult.Ok(output);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or DllNotFoundException)
        {
            try { File.Delete(output); } catch (IOException) { }
            return JobResult.Fail($"Couldn't make the copy of {Path.GetFileName(input)}: {ex.Message}");
        }
    });

    private static void Replace(ref SKBitmap current, SKBitmap next)
    {
        if (!ReferenceEquals(current, next))
            current.Dispose();
        current = next;
    }

    // ---------- PDF helpers ----------

    /// <summary>
    /// PDF compression: one picture from inside a PDF, re-encoded as JPEG (quality 72) and scaled down to at most
    /// <see cref="PdfTools.MaxPictureSide"/> on its longer side. Null keeps the original.
    /// </summary>
    public static SmallerPicture? ShrinkPdfPicture(PdfPicture picture)
    {
        try
        {
            using var source = picture.Jpeg is { } jpeg ? SKBitmap.Decode(jpeg) : FromRgb(picture.Rgb!, picture.Width, picture.Height);
            if (source is null) return null;
            var scale = Math.Min(1.0, (double)PdfTools.MaxPictureSide / Math.Max(source.Width, source.Height));
            int w = Math.Max(1, (int)Math.Round(source.Width * scale)), h = Math.Max(1, (int)Math.Round(source.Height * scale));
            using var sized = scale < 1 ? Scale(source, w, h) : source.Copy();
            using var data = sized.Encode(SKEncodedImageFormat.Jpeg, 72);
            return data is null ? null : new SmallerPicture(data.ToArray(), w, h);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            return null;
        }
    }

    private static SKBitmap FromRgb(byte[] rgb, int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var rgba = new byte[width * height * 4];
        for (int i = 0, j = 0; i < rgb.Length - 2 && j < rgba.Length; i += 3, j += 4)
        {
            rgba[j] = rgb[i]; rgba[j + 1] = rgb[i + 1]; rgba[j + 2] = rgb[i + 2]; rgba[j + 3] = 255;
        }
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        bitmap.NotifyPixelsChanged();
        return bitmap;
    }

    /// <summary>BGRA pixels (top-down rows) as PNG bytes.</summary>
    public static byte[] EncodePng(int width, int height, byte[] bgra)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bitmap.GetPixels(), Math.Min(bgra.Length, width * height * 4));
        bitmap.NotifyPixelsChanged();
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Encodes one rendered PDF page (BGRA from PDFium) as PNG or JPG. Used by the PDF engine.</summary>
    public static void WriteRenderedPage(string output, int width, int height, byte[] bgra, int stride)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var rowBytes = width * 4;
        var dst = bitmap.GetPixels();
        for (var y = 0; y < height; y++)
            System.Runtime.InteropServices.Marshal.Copy(bgra, y * stride, dst + y * bitmap.RowBytes, rowBytes);
        bitmap.NotifyPixelsChanged();

        var ext = Path.GetExtension(output).ToLowerInvariant();
        Encode(bitmap, ext is ".jpg" or ".jpeg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, 90, output);
    }

    /// <summary>
    /// Images → one PDF, one page per image, in name order, each page A4-wide with the image's proportions.
    /// One image → "name.pdf"; several → "Images.pdf" beside the first.
    /// </summary>
    public static Task<JobResult> ImagesToPdfAsync(IReadOnlyList<string> inputs) => Task.Run(async () =>
    {
        var temp = Path.Combine(Path.GetTempPath(), "wheel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string? output = null;
        try
        {
            var ordered = inputs.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
            output = ordered.Count == 1
                ? OutputPaths.Beside(ordered[0], ".pdf")
                : OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(ordered[0]), "Images.pdf"));

            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            using (var document = SKDocument.CreatePdf(stream))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            {
                const float pageWidth = 595; // A4 width in points
                foreach (var input in ordered)
                {
                    using var bitmap = await DecodeAsync(input, temp);
                    var pageHeight = pageWidth * bitmap.Height / bitmap.Width;
                    var canvas = document.BeginPage(pageWidth, pageHeight);
                    canvas.DrawBitmap(bitmap, new SKRect(0, 0, pageWidth, pageHeight), paint);
                    document.EndPage();
                }
                document.Close();
            }
            return JobResult.Ok(output);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException
                                   or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            if (output is not null) try { File.Delete(output); } catch (IOException) { }
            return JobResult.Fail(ex.Message);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    });

    /// <summary>Re-saves a PNG screenshot as JPG (Settings → screenshot format) and deletes the PNG.</summary>
    public static string ConvertCaptureToJpg(string png)
    {
        using var bitmap = TryDecode(png) ?? throw new IOException("Couldn't read the screenshot.");
        var jpg = OutputPaths.Unique(Path.ChangeExtension(png, ".jpg"));
        using (var flat = OnWhite(bitmap))
            Encode(flat, SKEncodedImageFormat.Jpeg, Math.Clamp(Quality, 50, 100), jpg);
        File.Delete(png);
        return jpg;
    }

    /// <summary>Loads an image for the editor: upright, 32-bit, any format this machine can read.</summary>
    public static async Task<SKBitmap> LoadForEditingAsync(string path)
    {
        var temp = Path.Combine(Path.GetTempPath(), "wheel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try { return await DecodeAsync(path, temp); }
        finally { try { Directory.Delete(temp, recursive: true); } catch (IOException) { } }
    }

    /// <summary>Saves an edited image as PNG or JPG (by extension).</summary>
    public static void SaveEdited(SKBitmap bitmap, string path)
    {
        var jpg = Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg";
        if (jpg)
        {
            using var flat = OnWhite(bitmap);
            Encode(flat, SKEncodedImageFormat.Jpeg, Math.Clamp(Quality, 50, 100), path);
        }
        else
        {
            Encode(bitmap, SKEncodedImageFormat.Png, 100, path);
        }
    }

    // ---------- decode ----------

    /// <summary>Decodes with Skia and applies the EXIF orientation; falls back to sips/ffmpeg for HEIC, TIFF, AVIF…</summary>
    private static async Task<SKBitmap> DecodeAsync(string input, string temp)
    {
        var decoded = TryDecode(input);
        if (decoded is null)
        {
            var png = Path.Combine(temp, "decoded.png");
            if (OperatingSystem.IsMacOS() && File.Exists(Sips))
                await Check(ProcessRunner.RunAsync(Sips, ["-s", "format", "png", input, "--out", png]));
            else if (FfmpegConverter.Locate() is { } ffmpeg)
                await Check(ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-i", input, "-frames:v", "1", png]));
            decoded = File.Exists(png) ? TryDecode(png) : null;
        }
        return decoded ?? throw new NotSupportedException("Can't read this image.");
    }

    private static SKBitmap? TryDecode(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null)
            return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            return null;
        }

        var upright = Orient(bitmap, codec.EncodedOrigin);
        if (!ReferenceEquals(upright, bitmap))
            bitmap.Dispose();
        return upright;
    }

    // ---------- pixel operations ----------

    /// <summary>Redraws the bitmap so it looks right for the given EXIF origin (also used for "rotate 90°").</summary>
    private static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return src;

        float w = src.Width, h = src.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                          or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var dst = NewBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height);

        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Transparent);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:    canvas.Translate(w, 0); canvas.Scale(-1, 1); break;                       // mirror
            case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;                 // 180°
            case SKEncodedOrigin.BottomLeft:  canvas.Translate(0, h); canvas.Scale(1, -1); break;                       // flip
            case SKEncodedOrigin.LeftTop:     canvas.Scale(-1, 1); canvas.RotateDegrees(90); break;                     // transpose
            case SKEncodedOrigin.RightTop:    canvas.Translate(h, 0); canvas.RotateDegrees(90); break;                  // 90° cw
            case SKEncodedOrigin.RightBottom: canvas.Translate(h, w); canvas.Scale(1, -1); canvas.RotateDegrees(90); break; // transverse
            case SKEncodedOrigin.LeftBottom:  canvas.Translate(0, w); canvas.RotateDegrees(-90); break;                 // 270° cw
        }
        canvas.DrawBitmap(src, 0, 0);
        canvas.Flush();
        return dst;
    }

    private static SKBitmap Scale(SKBitmap src, int width, int height)
    {
        var dst = NewBitmap(width, height);
        using var canvas = new SKCanvas(dst);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(src, new SKRect(0, 0, width, height), paint);
        canvas.Flush();
        return dst;
    }

    /// <summary>A square version for icons: the picture centred, transparent around it.</summary>
    private static SKBitmap Square(SKBitmap src, int size)
    {
        var scale = Math.Min((float)size / src.Width, (float)size / src.Height);
        float w = src.Width * scale, h = src.Height * scale;
        var dst = NewBitmap(size, size);
        using var canvas = new SKCanvas(dst);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(src, SKRect.Create((size - w) / 2, (size - h) / 2, w, h), paint);
        canvas.Flush();
        return dst;
    }

    /// <summary>
    /// "name (icons)" folder: icon-16.png … icon-1024.png, icon.ico, and on Mac icon.icns (made with iconutil).
    /// </summary>
    private static async Task<string> MakeIconSetAsync(SKBitmap src, string input, string temp)
    {
        var folder = OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(input), OutputPaths.StemOf(input) + " (icons)"));
        Directory.CreateDirectory(folder);
        foreach (var size in Wheel.Core.Files.IconSet.PngSizes)
        {
            using var icon = Square(src, size);
            Encode(icon, SKEncodedImageFormat.Png, 100, Path.Combine(folder, $"icon-{size}.png"));
        }
        WriteIco(src, Path.Combine(folder, "icon.ico"));

        if (OperatingSystem.IsMacOS() && File.Exists("/usr/bin/iconutil"))
        {
            var iconset = Path.Combine(temp, "icon.iconset");
            Directory.CreateDirectory(iconset);
            foreach (var (name, pixels) in Wheel.Core.Files.IconSet.MacIconset())
            {
                using var icon = Square(src, pixels);
                Encode(icon, SKEncodedImageFormat.Png, 100, Path.Combine(iconset, name));
            }
            var r = await ProcessRunner.RunAsync("/usr/bin/iconutil", ["-c", "icns", iconset, "-o", Path.Combine(folder, "icon.icns")]);
            if (r.ExitCode != 0) Log.Write(new IOException("iconutil: " + r.ErrorTail));
        }
        return folder;
    }

    private static SKBitmap Half(SKBitmap src)
    {
        var dst = NewBitmap(Math.Max(1, src.Width / 2), Math.Max(1, src.Height / 2));
        using var canvas = new SKCanvas(dst);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(src, new SKRect(0, 0, dst.Width, dst.Height), paint);
        canvas.Flush();
        return dst;
    }

    /// <summary>For formats without transparency: put the picture on white so transparent areas don't turn black.</summary>
    private static SKBitmap OnWhite(SKBitmap src)
    {
        var dst = NewBitmap(src.Width, src.Height);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(src, 0, 0);
        canvas.Flush();
        return dst;
    }

    private static SKBitmap NewBitmap(int width, int height) =>
        new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

    // ---------- encode ----------

    private static async Task SaveAsync(SKBitmap bitmap, string output, int quality, string temp)
    {
        var ext = Path.GetExtension(output).ToLowerInvariant();
        switch (ext)
        {
            case ".png":
                Encode(bitmap, SKEncodedImageFormat.Png, 100, output);
                return;
            case ".jpg" or ".jpeg":
                using (var flat = OnWhite(bitmap))
                    Encode(flat, SKEncodedImageFormat.Jpeg, quality, output);
                return;
            case ".webp":
                Encode(bitmap, SKEncodedImageFormat.Webp, quality, output);
                return;
        }

        // BMP, TIFF, GIF, HEIC: hand a lossless PNG to the platform helper.
        var png = Path.Combine(temp, "encode.png");
        using (var flat = ext is ".bmp" or ".gif" ? OnWhite(bitmap) : null)
            Encode(flat ?? bitmap, SKEncodedImageFormat.Png, 100, png);

        if (OperatingSystem.IsMacOS() && File.Exists(Sips))
        {
            var format = ext switch { ".tif" or ".tiff" => "tiff", ".heic" or ".heif" => "heic", ".gif" => "gif", ".bmp" => "bmp", _ => null }
                         ?? throw new NotSupportedException($"Can't write {ext} images.");
            await Check(ProcessRunner.RunAsync(Sips, ["-s", "format", format, png, "--out", output]));
        }
        else if (FfmpegConverter.Locate() is { } ffmpeg && ext is ".tif" or ".tiff" or ".gif" or ".bmp")
        {
            await Check(ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-n", "-i", png, output]));
        }
        else
        {
            throw new NotSupportedException($"Writing {ext.TrimStart('.').ToUpperInvariant()} needs ffmpeg on this computer.");
        }

        if (!File.Exists(output) || new FileInfo(output).Length == 0)
            throw new IOException($"Couldn't write {Path.GetFileName(output)}.");
    }

    private static void Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality)
                         ?? throw new NotSupportedException($"This build can't write {format} images.");
        try
        {
            using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            data.SaveTo(fs);
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>A 256 px square icon holding one PNG, centred, aspect kept. Windows and macOS both read it.</summary>
    private static void WriteIco(SKBitmap src, string path)
    {
        const int size = 256;
        var scale = Math.Min((float)size / src.Width, (float)size / src.Height);
        float dw = src.Width * scale, dh = src.Height * scale;

        using var square = NewBitmap(size, size);
        using (var canvas = new SKCanvas(square))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(src, SKRect.Create((size - dw) / 2, (size - dh) / 2, dw, dh), paint);
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(square);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = png.ToArray();

        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0);  // ICONDIR: reserved
        w.Write((ushort)1);  // type: icon
        w.Write((ushort)1);  // one image
        w.Write((byte)0);    // ICONDIRENTRY: width 256 stored as 0
        w.Write((byte)0);    // height 256 stored as 0
        w.Write((byte)0);    // palette colours
        w.Write((byte)0);    // reserved
        w.Write((ushort)1);  // colour planes
        w.Write((ushort)32); // bits per pixel
        w.Write((uint)bytes.Length);
        w.Write((uint)22);   // data offset = 6 + 16
        w.Write(bytes);
    }

    /// <summary>Same format as the source when this machine can write it, else the closest everyday format.</summary>
    private static string EncodableExtension(string input)
    {
        var ext = Path.GetExtension(input).ToLowerInvariant();
        var canFallback = (OperatingSystem.IsMacOS() && File.Exists(Sips)) || FfmpegConverter.Locate() is not null;
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" => Path.GetExtension(input),
            ".bmp" or ".tif" or ".tiff" or ".gif" when canFallback => Path.GetExtension(input),
            ".heic" or ".heif" when OperatingSystem.IsMacOS() => Path.GetExtension(input),
            ".ico" or ".bmp" or ".tif" or ".tiff" or ".gif" => ".png",
            _ => ".jpg", // HEIC on Windows, AVIF, JFIF
        };
    }

    private static async Task Check(Task<ProcessRunner.Result> run)
    {
        var r = await run;
        if (r.ExitCode != 0)
            throw new IOException(string.IsNullOrWhiteSpace(r.ErrorTail) ? "The image helper failed." : r.ErrorTail.Trim());
    }

    private static string Friendly(Exception ex, string input)
    {
        var ext = Path.GetExtension(input).ToLowerInvariant();
        if (ex is NotSupportedException && ext is ".heic" or ".heif" && OperatingSystem.IsWindows())
            return "Reading HEIC on Windows needs ffmpeg. Run: winget install Gyan.FFmpeg";
        if (ex is NotSupportedException && ex.Message.StartsWith("Can't read", StringComparison.Ordinal))
            return $"This image looks damaged or isn't really a {ext.TrimStart('.').ToUpperInvariant()} file.";
        return ex.Message;
    }
}
