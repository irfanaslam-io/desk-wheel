using System.Text;
using System.Text.RegularExpressions;

namespace Wheel.Core.Converters;

/// <summary>SRT ↔ VTT and subtitles → plain text. Pure string work, no dependencies.</summary>
public static partial class SubtitleConverter
{
    public sealed record Cue(TimeSpan Start, TimeSpan End, string Text);

    // 00:01:02,345 --> 00:01:04,000   (SRT uses a comma, VTT a dot; VTT may omit hours and add cue settings)
    [GeneratedRegex(@"^\s*((?:\d+:)?\d{1,2}:\d{2}[.,]\d{1,3})\s*-->\s*((?:\d+:)?\d{1,2}:\d{2}[.,]\d{1,3})")]
    private static partial Regex TimingLine();

    public static JobResult Run(WheelOption option, string input)
    {
        try
        {
            var cues = Parse(File.ReadAllText(input));
            if (cues.Count == 0)
                return JobResult.Fail("No subtitle cues found in this file.");

            var text = option.Id switch
            {
                "sub.to-srt" => ToSrt(cues),
                "sub.to-vtt" => ToVtt(cues),
                "sub.to-txt" => ToPlainText(cues),
                _ => throw new NotSupportedException(option.Id),
            };

            var output = OutputPaths.Beside(input, option.TargetExtension);
            File.WriteAllText(output, text, new UTF8Encoding(false));
            return JobResult.Ok(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return JobResult.Fail(ex.Message);
        }
    }

    /// <summary>Parses SRT or VTT. Tolerates BOMs, CRLF, missing cue numbers and VTT headers/notes.</summary>
    public static List<Cue> Parse(string content)
    {
        var cues = new List<Cue>();
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿').Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var m = TimingLine().Match(lines[i]);
            if (!m.Success)
                continue;

            var body = new List<string>();
            var j = i + 1;
            for (; j < lines.Length && lines[j].Trim().Length > 0; j++)
                body.Add(lines[j].TrimEnd());

            cues.Add(new Cue(ParseTime(m.Groups[1].Value), ParseTime(m.Groups[2].Value), string.Join("\n", body)));
            i = j;
        }

        return cues;
    }

    public static string ToSrt(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder();
        var n = 1;
        foreach (var c in cues)
        {
            sb.Append(n++).Append('\n');
            sb.Append(Format(c.Start, ',')).Append(" --> ").Append(Format(c.End, ',')).Append('\n');
            sb.Append(c.Text).Append("\n\n");
        }
        return sb.ToString();
    }

    public static string ToVtt(IEnumerable<Cue> cues)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var c in cues)
        {
            sb.Append(Format(c.Start, '.')).Append(" --> ").Append(Format(c.End, '.')).Append('\n');
            sb.Append(c.Text).Append("\n\n");
        }
        return sb.ToString();
    }

    /// <summary>Spoken text only: timings removed, simple tags like &lt;i&gt; stripped.</summary>
    public static string ToPlainText(IEnumerable<Cue> cues) =>
        string.Join("\n", cues.Select(c => Regex.Replace(c.Text, "<[^>]+>", "").Replace('\n', ' ').Trim())
                              .Where(t => t.Length > 0)) + "\n";

    private static TimeSpan ParseTime(string value)
    {
        var parts = value.Replace(',', '.').Split(':');
        var (h, m, s) = parts.Length == 3
            ? (int.Parse(parts[0]), int.Parse(parts[1]), parts[2])
            : (0, int.Parse(parts[0]), parts[1]);
        var secParts = s.Split('.');
        var ms = int.Parse(secParts[1].PadRight(3, '0')[..3]);
        return new TimeSpan(0, h, m, int.Parse(secParts[0]), ms);
    }

    private static string Format(TimeSpan t, char msSeparator) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{msSeparator}{t.Milliseconds:000}";
}
