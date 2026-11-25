using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.Rendering;

namespace Wheel.Core.Screen;

/// <summary>Reads QR codes and barcodes from pixels, and makes QR codes. All on this computer.</summary>
public static class QrCodes
{
    public const int MaxTextLength = 2000;

    /// <summary>Every QR code or barcode found in BGRA pixels (top-down rows, 4 bytes per pixel), best first.</summary>
    public static IReadOnlyList<string> Read(byte[] bgra, int width, int height)
    {
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true },
        };
        var source = new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var results = reader.DecodeMultiple(source);
        if (results is null || results.Length == 0)
        {
            // DecodeMultiple can miss a single code that Decode finds.
            var single = reader.Decode(source);
            return single?.Text is { Length: > 0 } t ? [t] : Array.Empty<string>();
        }
        return results.Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
    }

    /// <summary>A QR code for <paramref name="text"/>: square BGRA pixels, black on white, with a quiet margin.</summary>
    public static (int Width, int Height, byte[] Bgra) Make(string text, int size = 720)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("There's no text to put in a QR code.");
        if (text.Length > MaxTextLength) throw new ArgumentException($"That's too much text for a QR code (over {MaxTextLength} characters).");
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions { Width = size, Height = size, Margin = 2, CharacterSet = "UTF-8" },
        };
        var data = writer.Write(text);
        return (data.Width, data.Height, data.Pixels);
    }

    /// <summary>True for links a browser can open.</summary>
    public static bool IsWebLink(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
