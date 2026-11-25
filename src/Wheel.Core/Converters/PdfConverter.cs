using System.Runtime.InteropServices;
using System.Text;
using Wheel.Core.Pdf;
using static Wheel.Core.Pdf.Pdfium;

namespace Wheel.Core.Converters;

/// <summary>
/// PDF → images / text, and merge, split, rotate, all through PDFium.
/// Image encoding is passed in (<see cref="ImageWriter"/>), so this stays free of any graphics library.
/// </summary>
public static class PdfConverter
{
    public const int RenderDpi = 300;
    private const int MaxPixels = 10_000; // longest side, so a poster-sized page can't exhaust memory

    /// <summary>Writes one rendered page. Pixels are 32-bit BGRA, white background, <paramref name="stride"/> bytes per row.</summary>
    public delegate void ImageWriter(string outputPath, int width, int height, byte[] bgra, int stride);

    public static bool IsAvailable() => Pdfium.IsAvailable();

    public static JobResult Run(WheelOption option, IReadOnlyList<string> inputs, ImageWriter? writeImage = null)
    {
        try
        {
            return option.Id switch
            {
                "pdf.to-png" or "pdf.to-jpg" => Each(inputs, p => ToImages(p, option.TargetExtension!, writeImage
                    ?? throw new InvalidOperationException("No image writer."))),
                "pdf.to-txt" => Each(inputs, p => [ToText(p)]),
                "pdf.merge" => JobResult.Ok(Merge(inputs)),
                "pdf.split" => Each(inputs, Split),
                "pdf.rotate" => Each(inputs, p => [Rotate(p)]),
                "pdf.numbers" => Each(inputs, p => [AddPageNumbers(p)]),
                _ => JobResult.Fail($"Unknown PDF option {option.Id}"),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                   or DllNotFoundException or EntryPointNotFoundException)
        {
            return JobResult.Fail(ex is DllNotFoundException or EntryPointNotFoundException
                ? "The PDF engine (PDFium) is missing from this build."
                : ex.Message);
        }
    }

    private static JobResult Each(IReadOnlyList<string> inputs, Func<string, IEnumerable<string>> work)
    {
        var outputs = new List<string>();
        foreach (var input in inputs)
            outputs.AddRange(work(input));
        return new JobResult(true, outputs);
    }

    /// <summary>
    /// Renders every page at 300 DPI. One page → "name.png" beside the PDF;
    /// several → a "name (pages)" folder holding "name-001.png", "name-002.png"…
    /// </summary>
    public static IEnumerable<string> ToImages(string input, string extension, ImageWriter write)
    {
        lock (Sync)
        {
            EnsureInit();
            using var doc = new Document(input);
            var count = doc.PageCount;
            if (count == 0)
                throw new InvalidDataException("This PDF has no pages.");

            var stem = OutputPaths.StemOf(input);
            string folder = "";
            if (count > 1)
            {
                folder = PagesFolder(input);
                Directory.CreateDirectory(folder);
            }

            var outputs = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var output = count == 1
                    ? OutputPaths.Beside(input, extension)
                    : Path.Combine(folder, $"{stem}-{i + 1:000}{extension}");
                RenderPage(doc.Handle, i, output, write);
                outputs.Add(output);
            }
            return count == 1 ? outputs : [folder];
        }
    }

    /// <summary>Renders every page into <paramref name="folder"/> as page-001.png… (for OCR of scanned PDFs).</summary>
    public static IReadOnlyList<string> RenderPages(string input, string folder, ImageWriter write)
    {
        lock (Sync)
        {
            EnsureInit();
            using var doc = new Document(input);
            Directory.CreateDirectory(folder);
            var outputs = new List<string>();
            for (var i = 0; i < doc.PageCount; i++)
            {
                var output = Path.Combine(folder, $"page-{i + 1:000}.png");
                RenderPage(doc.Handle, i, output, write);
                outputs.Add(output);
            }
            return outputs;
        }
    }

    private static void RenderPage(IntPtr doc, int index, string output, ImageWriter write)
    {
        var page = FPDF_LoadPage(doc, index);
        if (page == IntPtr.Zero)
            throw new InvalidDataException($"Page {index + 1} couldn't be read.");
        try
        {
            var scale = RenderDpi / 72.0; // PDF sizes are in points (1/72 inch)
            double w = FPDF_GetPageWidthF(page) * scale, h = FPDF_GetPageHeightF(page) * scale;
            var shrink = Math.Min(1.0, MaxPixels / Math.Max(w, h));
            int width = Math.Max(1, (int)Math.Round(w * shrink)), height = Math.Max(1, (int)Math.Round(h * shrink));

            var bitmap = FPDFBitmap_Create(width, height, 1);
            if (bitmap == IntPtr.Zero)
                throw new IOException($"Not enough memory to render page {index + 1}.");
            try
            {
                FPDFBitmap_FillRect(bitmap, 0, 0, width, height, new CULong(0xFFFFFFFF)); // opaque white
                FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, FPDF_ANNOT);
                var stride = FPDFBitmap_GetStride(bitmap);
                var pixels = new byte[stride * height];
                Marshal.Copy(FPDFBitmap_GetBuffer(bitmap), pixels, 0, pixels.Length);
                write(output, width, height, pixels, stride);
            }
            finally
            {
                FPDFBitmap_Destroy(bitmap);
            }
        }
        finally
        {
            FPDF_ClosePage(page);
        }
    }

    /// <summary>All text, page by page, separated by a blank line. Scanned PDFs (images only) have no text: that needs OCR.</summary>
    public static string ToText(string input)
    {
        var sb = new StringBuilder();
        lock (Sync)
        {
            EnsureInit();
            using var doc = new Document(input);
            for (var i = 0; i < doc.PageCount; i++)
            {
                var page = FPDF_LoadPage(doc.Handle, i);
                if (page == IntPtr.Zero) continue;
                var text = FPDFText_LoadPage(page);
                try
                {
                    var n = text == IntPtr.Zero ? 0 : FPDFText_CountChars(text);
                    if (n > 0)
                    {
                        var buffer = new ushort[n + 1]; // + terminator
                        var written = FPDFText_GetText(text, 0, n, buffer);
                        var chars = new char[Math.Max(0, written - 1)];
                        for (var c = 0; c < chars.Length; c++) chars[c] = (char)buffer[c];
                        if (sb.Length > 0) sb.Append("\n\n");
                        sb.Append(new string(chars).Replace("\r\n", "\n").TrimEnd());
                    }
                }
                finally
                {
                    if (text != IntPtr.Zero) FPDFText_ClosePage(text);
                    FPDF_ClosePage(page);
                }
            }
        }

        if (sb.Length == 0)
            throw new InvalidDataException("This PDF has no text to extract (it may be scanned images; OCR is coming later).");

        var output = OutputPaths.Beside(input, ".txt");
        File.WriteAllText(output, sb.Append('\n').ToString(), new UTF8Encoding(false));
        return output;
    }

    /// <summary>Joins PDFs in file-name order (natural: "2" before "10") into "Merged.pdf" beside the first.</summary>
    public static string Merge(IReadOnlyList<string> inputs)
    {
        if (inputs.Count < 2)
            throw new IOException("Select two or more PDFs to merge.");

        var ordered = inputs.OrderBy(p => Path.GetFileName(p), NaturalComparer.Instance).ToList();
        lock (Sync)
        {
            EnsureInit();
            var merged = FPDF_CreateNewDocument();
            try
            {
                var at = 0;
                foreach (var path in ordered)
                {
                    using var src = new Document(path);
                    var pages = src.PageCount;
                    if (FPDF_ImportPages(merged, src.Handle, null, at) == 0)
                        throw new InvalidDataException($"Couldn't copy pages from {Path.GetFileName(path)}.");
                    at += pages;
                }
                var dir = OutputPaths.FolderFor(ordered[0]);
                var output = OutputPaths.Unique(Path.Combine(dir, "Merged.pdf"));
                File.WriteAllBytes(output, Save(merged));
                return output;
            }
            finally
            {
                FPDF_CloseDocument(merged);
            }
        }
    }

    /// <summary>One PDF per page in a "name (pages)" folder. A single-page PDF has nothing to split.</summary>
    public static IEnumerable<string> Split(string input)
    {
        lock (Sync)
        {
            EnsureInit();
            using var src = new Document(input);
            var count = src.PageCount;
            if (count < 2)
                throw new InvalidDataException("This PDF has only one page.");

            var folder = PagesFolder(input);
            Directory.CreateDirectory(folder);
            var stem = OutputPaths.StemOf(input);
            for (var i = 0; i < count; i++)
            {
                var single = FPDF_CreateNewDocument();
                try
                {
                    if (FPDF_ImportPages(single, src.Handle, (i + 1).ToString(), 0) == 0)
                        throw new InvalidDataException($"Couldn't copy page {i + 1}.");
                    File.WriteAllBytes(Path.Combine(folder, $"{stem}-{i + 1:000}.pdf"), Save(single));
                }
                finally
                {
                    FPDF_CloseDocument(single);
                }
            }
            return [folder];
        }
    }

    /// <summary>"1 / 12" centred at the bottom of every page; saved as "name (numbered).pdf".</summary>
    public static string AddPageNumbers(string input)
    {
        lock (Sync)
        {
            EnsureInit();
            using var doc = new Document(input);
            var font = FPDFText_LoadStandardFont(doc.Handle, "Helvetica");
            if (font == IntPtr.Zero) throw new IOException("The PDF engine couldn't load a font.");
            try
            {
                var count = doc.PageCount;
                for (var i = 0; i < count; i++)
                {
                    var page = FPDF_LoadPage(doc.Handle, i);
                    if (page == IntPtr.Zero) continue;
                    try
                    {
                        // Page size before /Rotate, since the number is placed in the page's own coordinates.
                        var turned = FPDFPage_GetRotation(page) % 2 == 1;
                        var width = turned ? FPDF_GetPageHeightF(page) : FPDF_GetPageWidthF(page);
                        var size = Math.Clamp(width / 60f, 8f, 12f);
                        var text = FPDFPageObj_CreateTextObj(doc.Handle, font, size);
                        var label = $"{i + 1} / {count}";
                        FPDFText_SetText(text, label.Select(c => (ushort)c).Append((ushort)0).ToArray());
                        FPDFPageObj_SetFillColor(text, 90, 90, 90, 255);
                        FPDFPageObj_GetBounds(text, out var left, out _, out var right, out _);
                        FPDFPageObj_Transform(text, 1, 0, 0, 1, width / 2 - (right - left) / 2, size * 2);
                        FPDFPage_InsertObject(page, text);
                        if (FPDFPage_GenerateContent(page) == 0)
                            throw new IOException($"Couldn't write page {i + 1}.");
                    }
                    finally
                    {
                        FPDF_ClosePage(page);
                    }
                }
                var output = OutputPaths.Beside(input, ".pdf", "numbered");
                File.WriteAllBytes(output, Save(doc.Handle));
                return output;
            }
            finally
            {
                FPDFFont_Close(font);
            }
        }
    }

    /// <summary>Turns every page 90° clockwise; saved as "name (rotated).pdf".</summary>
    public static string Rotate(string input)
    {
        lock (Sync)
        {
            EnsureInit();
            using var doc = new Document(input);
            for (var i = 0; i < doc.PageCount; i++)
            {
                var page = FPDF_LoadPage(doc.Handle, i);
                if (page == IntPtr.Zero) continue;
                FPDFPage_SetRotation(page, (FPDFPage_GetRotation(page) + 1) % 4); // 0..3 = 0°, 90°, 180°, 270°
                FPDF_ClosePage(page);
            }
            var output = OutputPaths.Beside(input, ".pdf", "rotated");
            File.WriteAllBytes(output, Save(doc.Handle));
            return output;
        }
    }

    private static string PagesFolder(string input)
    {
        return OutputPaths.Unique(Path.Combine(OutputPaths.FolderFor(input), OutputPaths.StemOf(input) + " (pages)"));
    }

    /// <summary>"page2" before "page10".</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? a, string? b)
        {
            if (a is null || b is null) return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    var na = a[si..i].TrimStart('0');
                    var nb = b[sj..j].TrimStart('0');
                    var c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
