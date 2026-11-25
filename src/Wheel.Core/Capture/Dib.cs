using System.Buffers.Binary;

namespace Wheel.Core.Capture;

/// <summary>
/// Windows clipboard bitmaps (CF_DIB: a BITMAPINFOHEADER followed by pixels) ↔ top-down 32-bit BGRA.
/// Pure byte work, so it is tested on any OS.
/// </summary>
public static class Dib
{
    private const int HeaderSize = 40;   // BITMAPINFOHEADER
    private const int BI_RGB = 0, BI_BITFIELDS = 3;

    /// <summary>Decodes a 24- or 32-bit DIB into top-down BGRA (alpha forced opaque). Throws on other formats.</summary>
    public static (int Width, int Height, byte[] Bgra) ToBgra(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < HeaderSize)
            throw new InvalidDataException("Clipboard image is too small.");

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        var width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var compression = BinaryPrimitives.ReadInt32LittleEndian(dib[16..]);
        var colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);

        if (width <= 0 || rawHeight == 0 || bitCount is not (24 or 32))
            throw new InvalidDataException($"Unsupported clipboard image ({bitCount}-bit).");
        if (compression is not (BI_RGB or BI_BITFIELDS))
            throw new InvalidDataException("Compressed clipboard images aren't supported.");

        var topDown = rawHeight < 0;
        var height = Math.Abs(rawHeight);

        // Pixels start after the header, the three colour masks (BITFIELDS with a v1 header) and any palette.
        var offset = headerSize;
        if (compression == BI_BITFIELDS && headerSize == HeaderSize) offset += 12;
        offset += colorsUsed * 4;

        var srcStride = ((width * bitCount + 31) / 32) * 4;
        if (dib.Length < offset + srcStride * height)
            throw new InvalidDataException("Clipboard image data is truncated.");

        var bgra = new byte[width * height * 4];
        var bytesPerPixel = bitCount / 8;
        for (var y = 0; y < height; y++)
        {
            var srcRow = offset + (topDown ? y : height - 1 - y) * srcStride;
            var dstRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var s = srcRow + x * bytesPerPixel;
                var d = dstRow + x * 4;
                bgra[d] = dib[s];
                bgra[d + 1] = dib[s + 1];
                bgra[d + 2] = dib[s + 2];
                bgra[d + 3] = 255; // screenshots are opaque; many apps leave this byte as 0
            }
        }
        return (width, height, bgra);
    }

    /// <summary>Top-down BGRA → a 32-bit bottom-up DIB, the layout every Windows app accepts from the clipboard.</summary>
    public static byte[] FromBgra(int width, int height, ReadOnlySpan<byte> bgra)
    {
        var stride = width * 4;
        var dib = new byte[HeaderSize + stride * height];
        var span = dib.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);      // positive = bottom-up
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], 1);         // planes
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], 32);        // bits per pixel
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], BI_RGB);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], stride * height);
        for (var y = 0; y < height; y++)
            bgra.Slice(y * stride, stride).CopyTo(span[(HeaderSize + (height - 1 - y) * stride)..]);
        return dib;
    }
}
