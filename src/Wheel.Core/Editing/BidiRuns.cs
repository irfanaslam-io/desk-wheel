namespace Wheel.Core.Editing;

/// <summary>
/// Mixed Arabic and English on one line ("© Desk Wheel · سري"): the line split into right-to-left and
/// left-to-right runs, in the order they appear on screen. Each run is then shaped on its own (HarfBuzz joins
/// the Arabic letters and lays the run out right to left). A simplified Unicode bidi: Arabic and Hebrew letters
/// are right-to-left, other letters and digits left-to-right, and spaces and punctuation follow their neighbours.
/// </summary>
public static class BidiRuns
{
    public readonly record struct Run(string Text, bool RightToLeft);

    public static IReadOnlyList<Run> Visual(string line)
    {
        if (line.Length == 0) return [];
        var strong = new int[line.Length]; // 1 = RTL, -1 = LTR, 0 = neutral
        for (var i = 0; i < line.Length; i++)
            strong[i] = IsRtl(line[i]) ? 1 : char.IsLetterOrDigit(line[i]) ? -1 : 0;

        var baseRtl = strong.FirstOrDefault(s => s != 0) == 1;
        var resolved = new bool[line.Length];
        for (var i = 0; i < line.Length; i++)
        {
            if (strong[i] != 0) { resolved[i] = strong[i] == 1; continue; }
            // A space or punctuation mark: the direction of both neighbours when they agree, else the line's.
            int before = 0, after = 0;
            for (var j = i - 1; j >= 0 && before == 0; j--) before = strong[j];
            for (var j = i + 1; j < line.Length && after == 0; j++) after = strong[j];
            resolved[i] = before != 0 && before == after ? before == 1 : baseRtl;
        }

        var runs = new List<Run>();
        var start = 0;
        for (var i = 1; i <= line.Length; i++)
            if (i == line.Length || resolved[i] != resolved[start])
            {
                runs.Add(new Run(line[start..i], resolved[start]));
                start = i;
            }
        if (baseRtl) runs.Reverse(); // a right-to-left line: its first run is drawn rightmost
        return runs;
    }

    public static bool IsRtl(char c) => c is >= '֐' and <= 'ࣿ' or >= 'יִ' and <= '﷿' or >= 'ﹰ' and <= '﻿';
}
