using System.Text;

namespace Wheel.Core.Ocr;

/// <summary>Engine-independent clean-up and description of recognised text.</summary>
public static class OcrText
{
    /// <summary>
    /// Normalises line endings, trims each line, and collapses runs of blank lines to one,
    /// so text from any engine pastes the same way.
    /// </summary>
    public static string Tidy(string raw)
    {
        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim());
        var sb = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blank++;
                continue;
            }
            if (sb.Length > 0)
                sb.Append(blank > 0 ? "\n\n" : "\n");
            sb.Append(line);
            blank = 0;
        }
        return sb.ToString();
    }

    public static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>First line or so, for a notification: "Invoice 2026-114 · Total…".</summary>
    public static string Preview(string text, int max = 70)
    {
        var flat = string.Join(" · ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= max ? flat : flat[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>True when most letters are Arabic script (used to pick between engine results).</summary>
    public static bool IsMostlyArabic(string text)
    {
        int arabic = 0, letters = 0;
        foreach (var c in text)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (IsArabic(c)) arabic++;
        }
        return letters > 0 && arabic * 2 >= letters;
    }

    public static bool IsArabic(char c) =>
        c is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ࢠ' and <= 'ࣿ'
            or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿';

    /// <summary>"Copied 42 words", "Copied 1 word".</summary>
    public static string CopiedSummary(string text)
    {
        var n = WordCount(text);
        return $"Copied {n} word{(n == 1 ? "" : "s")}";
    }
}
