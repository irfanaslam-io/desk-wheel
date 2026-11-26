using System.Globalization;
using System.IO.Compression;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Wheel.Core.Pdf;

/// <summary>A word placed on a PDF page: its baseline start, the baseline across the word, and the way up (its height).</summary>
public sealed record PlacedWord(string Text, double X, double Y, double AcrossX, double AcrossY, double UpX, double UpY);

/// <summary>
/// Makes a scanned PDF searchable: the recognised words go on each page as invisible text, exactly over the
/// words in the picture, so they can be searched, selected and copied while the page looks the same.
/// The text uses a tiny built-in font with no visible letters that maps straight to Unicode, so any language
/// works (Arabic included) without depending on the fonts installed on this computer.
/// </summary>
public static class SearchablePdf
{
    private const string FontKey = "/DWOcr";
    private const string FontName = "/DeskWheelOcr-Regular";

    /// <summary>"name (searchable).pdf" with the words on their pages. Pages without words are left as they are.</summary>
    public static JobResult Write(string input, IReadOnlyDictionary<int, List<PlacedWord>> pages)
    {
        try
        {
            using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            // PDFsharp's own content compression leaves streams without their end marker (readers cope, checkers
            // warn): the new streams are compressed here instead.
            doc.Options.CompressContentStreams = false;
            var font = AddFont(doc);
            var added = 0;
            foreach (var (index, words) in pages)
            {
                if (index < 0 || index >= doc.PageCount || words.Count == 0) continue;
                var page = doc.Pages[index];

                var fonts = page.Resources.Elements.GetDictionary("/Font");
                if (fonts is null)
                {
                    fonts = new PdfDictionary(doc);
                    page.Resources.Elements["/Font"] = fonts;
                }
                fonts.Elements[FontKey] = font.Reference;

                // The page's own drawing is wrapped in q … Q so whatever it leaves changed can't move the text.
                Compressed(page.Contents.PrependContent(), "q\n"u8.ToArray());
                Compressed(page.Contents.AppendContent(), Encoding.ASCII.GetBytes("Q\n" + Content(words)));
                added += words.Count;
            }
            if (added == 0)
                return JobResult.Fail($"No text was found in {Path.GetFileName(input)}.");
            var output = OutputPaths.Beside(input, ".pdf", "searchable");
            doc.Save(output);
            return JobResult.Ok(output);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return JobResult.Fail($"{Path.GetFileName(input)} has a password. Unlock it first.");
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or IOException or NotSupportedException)
        {
            return JobResult.Fail($"Couldn't add the text to {Path.GetFileName(input)}: {ex.Message}");
        }
    }

    /// <summary>
    /// The text-layer drawing commands. Each word: invisible (3 Tr), font size 1 and a text matrix that
    /// stretches it over the word's box. Every character of the built-in font is half an em wide.
    /// </summary>
    public static string Content(IEnumerable<PlacedWord> words)
    {
        var sb = new StringBuilder("BT\n3 Tr\n").Append(FontKey).Append(" 1 Tf\n");
        foreach (var w in words)
        {
            var drawn = Ocr.OcrWords.DrawingOrder(w.Text);
            var units = drawn.Where(c => !char.IsSurrogate(c) && !char.IsControl(c)).ToArray();
            if (units.Length == 0) continue;
            var natural = units.Length * 0.5;
            // The baseline sits a fifth of the way up the box, where the font's letters stand.
            double ox = w.X + w.UpX * 0.2, oy = w.Y + w.UpY * 0.2;
            sb.Append(N(w.AcrossX / natural)).Append(' ').Append(N(w.AcrossY / natural)).Append(' ')
              .Append(N(w.UpX)).Append(' ').Append(N(w.UpY)).Append(' ')
              .Append(N(ox)).Append(' ').Append(N(oy)).Append(" Tm <");
            foreach (var c in units) sb.Append(((int)c).ToString("X4"));
            sb.Append("> Tj\n");
        }
        return sb.Append("ET\n").ToString();
    }

    private static string N(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The Type 0 font: Identity-H (each character code is its UTF-16 value) and a matching ToUnicode map.</summary>
    private static PdfDictionary AddFont(PdfDocument doc)
    {
        using var stream = typeof(SearchablePdf).Assembly.GetManifestResourceStream("Wheel.Core.Pdf.GlyphlessFont.ttf")
                           ?? throw new IOException("The text-layer font is missing from Desk Wheel.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var ttf = buffer.ToArray();

        var file = Indirect(doc);
        Compressed(file, ttf);
        file.Elements.SetInteger("/Length1", ttf.Length);

        var descriptor = Indirect(doc);
        descriptor.Elements.SetName("/Type", "/FontDescriptor");
        descriptor.Elements.SetName("/FontName", FontName);
        descriptor.Elements.SetInteger("/Flags", 5);
        descriptor.Elements["/FontBBox"] = new PdfArray(doc, new PdfInteger(0), new PdfInteger(-200), new PdfInteger(500), new PdfInteger(800));
        descriptor.Elements.SetInteger("/ItalicAngle", 0);
        descriptor.Elements.SetInteger("/Ascent", 800);
        descriptor.Elements.SetInteger("/Descent", -200);
        descriptor.Elements.SetInteger("/CapHeight", 800);
        descriptor.Elements.SetInteger("/StemV", 80);
        descriptor.Elements["/FontFile2"] = file.Reference;

        var cid = Indirect(doc);
        cid.Elements.SetName("/Type", "/Font");
        cid.Elements.SetName("/Subtype", "/CIDFontType2");
        cid.Elements.SetName("/BaseFont", FontName);
        var info = new PdfDictionary(doc);
        info.Elements["/Registry"] = new PdfString("Adobe");
        info.Elements["/Ordering"] = new PdfString("Identity");
        info.Elements.SetInteger("/Supplement", 0);
        cid.Elements["/CIDSystemInfo"] = info;
        cid.Elements["/FontDescriptor"] = descriptor.Reference;
        cid.Elements.SetInteger("/DW", 500);
        // Every character code draws glyph 1 (the font has only that and .notdef): readers then find a proper
        // box for every character, which they need to pick up rotated text.
        var map = Indirect(doc);
        var gids = new byte[65536 * 2];
        for (var i = 1; i < gids.Length; i += 2) gids[i] = 1;
        Compressed(map, gids);
        cid.Elements["/CIDToGIDMap"] = map.Reference;

        var toUnicode = Indirect(doc);
        Compressed(toUnicode, Encoding.ASCII.GetBytes(ToUnicodeMap()));

        var font = Indirect(doc);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type0");
        font.Elements.SetName("/BaseFont", FontName);
        font.Elements.SetName("/Encoding", "/Identity-H");
        font.Elements["/DescendantFonts"] = new PdfArray(doc, cid.Reference!);
        font.Elements["/ToUnicode"] = toUnicode.Reference;
        return font;
    }

    /// <summary>Code n means U+n, for the whole Basic Multilingual Plane (surrogates left out).</summary>
    public static string ToUnicodeMap()
    {
        var ranges = Enumerable.Range(0, 256).Where(hi => hi is < 0xD8 or > 0xDF).ToList();
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
          .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n")
          .Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n")
          .Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var block in ranges.Chunk(100))
        {
            sb.Append(block.Length).Append(" beginbfrange\n");
            foreach (var hi in block)
                sb.Append($"<{hi:X2}00> <{hi:X2}FF> <{hi:X2}00>\n");
            sb.Append("endbfrange\n");
        }
        return sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n").ToString();
    }

    private static PdfDictionary Indirect(PdfDocument doc)
    {
        var dict = new PdfDictionary(doc);
        doc.Internals.AddObject(dict);
        return dict;
    }

    private static void Compressed(PdfDictionary dict, byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data);
        dict.CreateStream(output.ToArray());
        dict.Elements.SetName("/Filter", "/FlateDecode");
    }
}
