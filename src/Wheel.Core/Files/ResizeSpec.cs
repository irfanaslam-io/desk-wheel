using System.Globalization;
using System.Text.RegularExpressions;

namespace Wheel.Core.Files;

/// <summary>
/// What "Size…" was asked for: "1920x1080" (fit inside, keeping proportions), "1920" (that width),
/// "x1080" (that height) or "50%". Images are never made bigger than asked, and never distorted.
/// </summary>
public sealed partial record ResizeSpec(int? Width, int? Height, double? Percent)
{
    public static ResizeSpec? Parse(string? text)
    {
        var t = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("×", "x").Replace("px", "");
        if (t.Length == 0) return null;
        if (t.EndsWith('%'))
            return double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p is > 0 and <= 1000
                ? new ResizeSpec(null, null, p) : null;
        var m = Size().Match(t);
        if (!m.Success) return null;
        int? w = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : null;
        int? h = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : null;
        if (w is null && h is null) return null;
        if (w is <= 0 or > 30000 || h is <= 0 or > 30000) return null;
        return new ResizeSpec(w, h, null);
    }

    [GeneratedRegex(@"^(\d+)?(?:x(\d+))?$")]
    private static partial Regex Size();

    /// <summary>The new size for an image of <paramref name="width"/>×<paramref name="height"/>.</summary>
    public (int Width, int Height) For(int width, int height)
    {
        double scale;
        if (Percent is { } p) scale = p / 100;
        else if (Width is { } w && Height is { } h) scale = Math.Min((double)w / width, (double)h / height);
        else if (Width is { } w2) scale = (double)w2 / width;
        else scale = (double)Height!.Value / height;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>For the file name: "1920x1080", "50%".</summary>
    public string Label => Percent is { } p ? $"{p:0.#}%" : $"{Width?.ToString() ?? ""}x{Height?.ToString() ?? ""}".Trim('x');
}

/// <summary>The sizes in an app-icon set.</summary>
public static class IconSet
{
    public static readonly int[] PngSizes = [16, 32, 64, 128, 256, 512, 1024];

    /// <summary>macOS .iconset names (iconutil): icon_16x16.png, icon_16x16@2x.png (32 px), … up to 512@2x.</summary>
    public static IReadOnlyList<(string Name, int Pixels)> MacIconset() =>
        new[] { 16, 32, 128, 256, 512 }
            .SelectMany(s => new[] { ($"icon_{s}x{s}.png", s), ($"icon_{s}x{s}@2x.png", s * 2) })
            .ToList();
}
