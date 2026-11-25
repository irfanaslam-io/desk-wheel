namespace Wheel.Core.Screen;

/// <summary>How a picked colour is written out.</summary>
public static class ScreenColor
{
    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    public static string Rgb(byte r, byte g, byte b) => $"rgb({r}, {g}, {b})";

    /// <summary>"hsl(18, 80%, 54%)".</summary>
    public static string Hsl(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
        double h = 0, s = 0, l = (max + min) / 2;
        var d = max - min;
        if (d > 0)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == rf ? (gf - bf) / d + (gf < bf ? 6 : 0) : max == gf ? (bf - rf) / d + 2 : (rf - gf) / d + 4;
            h *= 60;
        }
        return $"hsl({Math.Round(h)}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)";
    }

    /// <summary>Black or white, whichever reads better on this colour.</summary>
    public static bool PrefersDarkText(byte r, byte g, byte b) => (0.299 * r + 0.587 * g + 0.114 * b) > 150;
}
