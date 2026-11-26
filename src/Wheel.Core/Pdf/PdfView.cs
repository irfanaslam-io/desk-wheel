using static Wheel.Core.Pdf.Pdfium;

namespace Wheel.Core.Pdf;

/// <summary>A place found by Find: the page, and the found text's boxes as fractions of the page (from the top-left).</summary>
public sealed record PdfMatch(int Page, IReadOnlyList<(double X, double Y, double Width, double Height)> Boxes);

/// <summary>
/// A PDF kept open for viewing: page sizes, pages drawn at any size, page text and Find. Every call takes PDFium's
/// lock (PDFium isn't thread-safe), so pages can be drawn in the background while the window stays responsive.
/// </summary>
public sealed class PdfView : IDisposable
{
    private readonly Document _doc;
    private readonly (double Width, double Height)[] _sizes;
    private readonly string?[] _text;
    private bool _disposed;

    private PdfView(Document doc)
    {
        _doc = doc;
        _sizes = new (double, double)[doc.PageCount];
        _text = new string?[doc.PageCount];
        for (var i = 0; i < _sizes.Length; i++)
        {
            var page = FPDF_LoadPage(doc.Handle, i);
            _sizes[i] = page == IntPtr.Zero ? (612, 792) : (FPDF_GetPageWidthF(page), FPDF_GetPageHeightF(page)); // as shown (/Rotate applied)
            if (page != IntPtr.Zero) FPDF_ClosePage(page);
        }
    }

    /// <exception cref="InvalidDataException">Not a PDF, damaged, or needs a (different) password.</exception>
    public static PdfView Open(string path, string? password = null)
    {
        lock (Sync)
        {
            EnsureInit();
            return new PdfView(new Document(path, password));
        }
    }

    public int PageCount => _sizes.Length;

    /// <summary>The page's size in points (1/72 inch), as it's shown.</summary>
    public (double Width, double Height) PageSize(int page) => _sizes[page];

    /// <summary>The page drawn <paramref name="width"/> × <paramref name="height"/> pixels, BGRA, white behind.</summary>
    public byte[] Render(int page, int width, int height)
    {
        lock (Sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var handle = FPDF_LoadPage(_doc.Handle, page);
            if (handle == IntPtr.Zero) throw new InvalidDataException($"Page {page + 1} couldn't be read.");
            var bitmap = FPDFBitmap_Create(width, height, 1);
            try
            {
                FPDFBitmap_FillRect(bitmap, 0, 0, width, height, new System.Runtime.InteropServices.CULong(0xFFFFFFFF));
                FPDF_RenderPageBitmap(bitmap, handle, 0, 0, width, height, 0, FPDF_ANNOT | 0x02 /* LCD text */);
                var stride = FPDFBitmap_GetStride(bitmap);
                var buffer = FPDFBitmap_GetBuffer(bitmap);
                var result = new byte[width * height * 4];
                for (var y = 0; y < height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(buffer + y * stride, result, y * width * 4, width * 4);
                return result;
            }
            finally
            {
                FPDFBitmap_Destroy(bitmap);
                FPDF_ClosePage(handle);
            }
        }
    }

    /// <summary>The page's text (empty for scanned pages).</summary>
    public string PageText(int page)
    {
        if (_text[page] is { } cached) return cached;
        lock (Sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var handle = FPDF_LoadPage(_doc.Handle, page);
            if (handle == IntPtr.Zero) return _text[page] = "";
            var text = FPDFText_LoadPage(handle);
            try
            {
                var count = text == IntPtr.Zero ? 0 : FPDFText_CountChars(text);
                if (count <= 0) return _text[page] = "";
                var buffer = new ushort[count + 1];
                FPDFText_GetText(text, 0, count, buffer);
                var chars = new char[count];
                for (var i = 0; i < count; i++) chars[i] = (char)buffer[i];
                return _text[page] = new string(chars);
            }
            finally
            {
                if (text != IntPtr.Zero) FPDFText_ClosePage(text);
                FPDF_ClosePage(handle);
            }
        }
    }

    /// <summary>Every place the text appears (ignoring case), in page order, with boxes to highlight.</summary>
    public List<PdfMatch> Find(string query, int max = 500)
    {
        var matches = new List<PdfMatch>();
        if (string.IsNullOrWhiteSpace(query)) return matches;
        for (var p = 0; p < PageCount && matches.Count < max; p++)
        {
            var text = PageText(p);
            var at = 0;
            var starts = new List<int>();
            while ((at = text.IndexOf(query, at, StringComparison.CurrentCultureIgnoreCase)) >= 0)
            {
                starts.Add(at);
                at += Math.Max(1, query.Length);
            }
            if (starts.Count == 0) continue;
            lock (Sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var handle = FPDF_LoadPage(_doc.Handle, p);
                if (handle == IntPtr.Zero) continue;
                var textPage = FPDFText_LoadPage(handle);
                try
                {
                    const int size = 100_000;
                    foreach (var start in starts)
                    {
                        var boxes = new List<(double, double, double, double)>();
                        for (var i = start; i < start + query.Length; i++)
                        {
                            if (FPDFText_GetCharBox(textPage, i, out var l, out var r, out var b, out var t) == 0 || r <= l) continue;
                            FPDF_PageToDevice(handle, 0, 0, size, size, 0, l, t, out var x1, out var y1);
                            FPDF_PageToDevice(handle, 0, 0, size, size, 0, r, b, out var x2, out var y2);
                            boxes.Add((Math.Min(x1, x2) / (double)size, Math.Min(y1, y2) / (double)size,
                                       Math.Abs(x2 - x1) / (double)size, Math.Abs(y2 - y1) / (double)size));
                        }
                        matches.Add(new PdfMatch(p, boxes));
                        if (matches.Count >= max) break;
                    }
                }
                finally
                {
                    if (textPage != IntPtr.Zero) FPDFText_ClosePage(textPage);
                    FPDF_ClosePage(handle);
                }
            }
        }
        return matches;
    }

    public void Dispose()
    {
        lock (Sync)
        {
            if (_disposed) return;
            _disposed = true;
            _doc.Dispose();
        }
    }
}
