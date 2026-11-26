using System.Globalization;

namespace Wheel.Core.Ocr;

/// <summary>
/// One recognised word and where it is: fractions (0–1) of the picture's width and height, from the top-left.
/// </summary>
public sealed record OcrWord(string Text, double X, double Y, double Width, double Height);

/// <summary>Reads word positions from each text recognition engine into <see cref="OcrWord"/>s.</summary>
public static class OcrWords
{
    /// <summary>
    /// Desk Wheel's own format (the Mac helper and the Windows script): one word per line,
    /// "x&lt;TAB&gt;y&lt;TAB&gt;width&lt;TAB&gt;height&lt;TAB&gt;text", fractions from the top-left.
    /// </summary>
    public static List<OcrWord> ParseTsv(string output)
    {
        var words = new List<OcrWord>();
        foreach (var line in output.Replace("\r\n", "\n").Split('\n'))
        {
            var parts = line.Split('\t', 5);
            if (parts.Length < 5 || parts[4].Trim().Length == 0) continue;
            if (!TryNumber(parts[0], out var x) || !TryNumber(parts[1], out var y) ||
                !TryNumber(parts[2], out var w) || !TryNumber(parts[3], out var h) || w <= 0 || h <= 0) continue;
            words.Add(Clamp(new OcrWord(parts[4].Trim(), x, y, w, h)));
        }
        return words;
    }

    /// <summary>
    /// Tesseract's "tsv" output: level 1 is the page (its size), level 5 rows are words in pixels.
    /// </summary>
    public static List<OcrWord> FromTesseractTsv(string tsv)
    {
        var words = new List<OcrWord>();
        double pageWidth = 0, pageHeight = 0;
        foreach (var line in tsv.Replace("\r\n", "\n").Split('\n'))
        {
            var c = line.Split('\t');
            if (c.Length < 12 || c[0] == "level") continue;
            if (!int.TryParse(c[0], out var level) || !TryNumber(c[6], out var left) || !TryNumber(c[7], out var top)
                || !TryNumber(c[8], out var width) || !TryNumber(c[9], out var height)) continue;
            if (level == 1) { pageWidth = width; pageHeight = height; continue; }
            var text = c[11].Trim();
            if (level != 5 || text.Length == 0 || pageWidth <= 0 || pageHeight <= 0 || width <= 0 || height <= 0) continue;
            words.Add(Clamp(new OcrWord(text, left / pageWidth, top / pageHeight, width / pageWidth, height / pageHeight)));
        }
        return words;
    }

    /// <summary>
    /// The characters in the order they are drawn, left to right, as text layers store them: right-to-left
    /// words (Arabic, Hebrew) are reversed, keeping each letter with its marks. PDF readers put them back
    /// in reading order when you copy or search.
    /// </summary>
    public static string DrawingOrder(string word)
    {
        if (!word.Any(IsRightToLeft)) return word;
        var elements = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(word);
        while (e.MoveNext()) elements.Add((string)e.Current);
        elements.Reverse();
        return string.Concat(elements);
    }

    public static bool IsRightToLeft(char c) =>
        c is >= '֐' and <= 'ࣿ' or >= 'יִ' and <= '﷿' or >= 'ﹰ' and <= '﻿';

    private static bool TryNumber(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static OcrWord Clamp(OcrWord w)
    {
        var x = Math.Clamp(w.X, 0, 1);
        var y = Math.Clamp(w.Y, 0, 1);
        return w with { X = x, Y = y, Width = Math.Clamp(w.Width, 0, 1 - x), Height = Math.Clamp(w.Height, 0, 1 - y) };
    }
}
