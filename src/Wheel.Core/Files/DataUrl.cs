namespace Wheel.Core.Files;

/// <summary>"data:image/png;base64,…": a picture as text, for pasting into HTML, CSS or code.</summary>
public static class DataUrl
{
    public static string For(string path, byte[] bytes) => $"data:{MimeType(path)};base64,{Convert.ToBase64String(bytes)}";

    public static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".avif" => "image/avif",
        ".heic" or ".heif" => "image/heic",
        ".tif" or ".tiff" => "image/tiff",
        _ => "application/octet-stream",
    };
}
