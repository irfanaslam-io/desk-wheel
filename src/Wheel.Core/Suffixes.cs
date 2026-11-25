namespace Wheel.Core;

/// <summary>Words added to output names by tools, so "photo (compressed).jpg" says what happened.</summary>
public static class Suffixes
{
    public static string? For(WheelOption option)
    {
        if (option.Mode != WheelMode.Tools)
            return null;

        var verb = option.Id[(option.Id.IndexOf('.') + 1)..];
        return verb switch
        {
            "compress" => "compressed",
            "resize-half" or "half" => "50%",
            "rotate" => "rotated",
            "strip" => "no metadata",
            "mute" => "muted",
            "snapshot" => "frame",
            "normalize" => "normalized",
            "mono" => "mono",
            "to-pdf" => null,
            "merge" or "split" => null,
            _ => verb,
        };
    }
}
