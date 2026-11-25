namespace Wheel.Core.Clipboard;

/// <summary>Reads a PNG's size from its header, without decoding it.</summary>
public static class PngInfo
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static (int Width, int Height)? Size(ReadOnlySpan<byte> png)
    {
        // Signature, then the IHDR chunk: length (4), "IHDR" (4), width (4, big-endian), height (4).
        if (png.Length < 24 || !png[..8].SequenceEqual(Signature) || png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R')
            return null;
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return w > 0 && h > 0 ? (w, h) : null;
    }
}
