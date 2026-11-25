using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Wheel.Core.Converters;

/// <summary>A picture inside a PDF, handed to the app to be made smaller.</summary>
/// <param name="Jpeg">The JPEG bytes, when the picture is stored as JPEG.</param>
/// <param name="Rgb">Raw 8-bit RGB pixels (3 bytes per pixel, top-down), when stored uncompressed or zipped.</param>
public sealed record PdfPicture(int Width, int Height, byte[]? Jpeg, byte[]? Rgb);

/// <summary>The smaller version: JPEG bytes and their size.</summary>
public sealed record SmallerPicture(byte[] Jpeg, int Width, int Height);

/// <summary>
/// PDF passwords and compression, done inside Wheel with PDFsharp (MIT licence): nothing to install.
/// Lock uses AES-256 (the PDF 2.0 standard). Compress re-encodes the pictures inside the PDF at a size
/// that still prints well; text and drawings are untouched, so they stay sharp and selectable.
/// </summary>
public static class PdfTools
{
    /// <summary>Pictures are made no larger than this on their longer side (about 150 DPI on an A4 page).</summary>
    public const int MaxPictureSide = 1754;

    /// <summary>"name (protected).pdf", openable only with <paramref name="password"/>.</summary>
    public static JobResult Protect(string input, string password)
    {
        try
        {
            using var doc = Open(input, null);
            if (doc.SecuritySettings.IsEncrypted)
                return JobResult.Fail($"{Path.GetFileName(input)} already has a password. Unlock it first.");
            doc.SecuritySettings.UserPassword = password;
            doc.SecuritySettings.OwnerPassword = password;
            doc.SecurityHandler.SetEncryptionToV5();
            var output = OutputPaths.Beside(input, ".pdf", "protected");
            doc.Save(output);
            return JobResult.Ok(output);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return JobResult.Fail($"{Path.GetFileName(input)} already has a password. Unlock it first.");
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or IOException or NotSupportedException)
        {
            return JobResult.Fail($"Couldn't add a password to {Path.GetFileName(input)}: {ex.Message}");
        }
    }

    /// <summary>"name (unlocked).pdf" without a password, given the right one.</summary>
    public static JobResult Unlock(string input, string password)
    {
        try
        {
            using var doc = Open(input, password);
            doc.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
            var output = OutputPaths.Beside(input, ".pdf", "unlocked");
            doc.Save(output);
            return JobResult.Ok(output);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return JobResult.Fail("The password is wrong.");
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or IOException or NotSupportedException)
        {
            return JobResult.Fail($"Couldn't unlock {Path.GetFileName(input)}: {ex.Message}");
        }
    }

    /// <summary>
    /// "name (compressed).pdf". <paramref name="shrink"/> (from the app's image engine) re-encodes one picture,
    /// or returns null to keep it. When the result isn't smaller, nothing is kept.
    /// </summary>
    public static JobResult Compress(string input, Func<PdfPicture, SmallerPicture?> shrink)
    {
        string? output = null;
        try
        {
            using var doc = Open(input, null);
            var done = new HashSet<PdfObjectID>();
            foreach (var page in doc.Pages)
                ShrinkPictures(page.Elements.GetDictionary("/Resources"), shrink, done, depth: 0);

            doc.Options.CompressContentStreams = true;
            doc.Options.NoCompression = false;
            output = OutputPaths.Beside(input, ".pdf", "compressed");
            doc.Save(output);
            var before = new FileInfo(input).Length;
            var after = new FileInfo(output).Length;
            if (after >= before * 0.97)
            {
                File.Delete(output);
                return JobResult.Fail($"{Path.GetFileName(input)} is already as small as it gets.");
            }
            return JobResult.Ok(output);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return JobResult.Fail($"{Path.GetFileName(input)} has a password. Unlock it first.");
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or IOException or NotSupportedException)
        {
            if (output is not null && File.Exists(output)) File.Delete(output);
            return JobResult.Fail($"Couldn't compress {Path.GetFileName(input)}: {ex.Message}");
        }
    }

    private static PdfDocument Open(string input, string? password) =>
        password is null
            ? PdfReader.Open(input, PdfDocumentOpenMode.Modify)
            : PdfReader.Open(input, password, PdfDocumentOpenMode.Modify);

    /// <summary>Walks a resource dictionary's pictures (and forms, which have their own resources).</summary>
    private static void ShrinkPictures(PdfDictionary? resources, Func<PdfPicture, SmallerPicture?> shrink, HashSet<PdfObjectID> done, int depth)
    {
        if (resources is null || depth > 8) return;
        var xobjects = resources.Elements.GetDictionary("/XObject");
        if (xobjects is null) return;
        foreach (var key in xobjects.Elements.Keys.ToList())
        {
            if (xobjects.Elements[key] is not PdfReference { Value: PdfDictionary item } reference) continue;
            if (!done.Add(reference.ObjectID)) continue; // shared pictures are done once
            var subtype = item.Elements.GetName("/Subtype");
            if (subtype == "/Form")
                ShrinkPictures(item.Elements.GetDictionary("/Resources"), shrink, done, depth + 1);
            else if (subtype == "/Image")
                ShrinkPicture(item, shrink);
        }
    }

    private static void ShrinkPicture(PdfDictionary image, Func<PdfPicture, SmallerPicture?> shrink)
    {
        if (image.Stream is null) return;
        // Leave alone what can't be re-encoded as plain JPEG without changing how it looks.
        if (image.Elements.ContainsKey("/SMask") || image.Elements.ContainsKey("/Mask") || image.Elements.ContainsKey("/Decode")
            || image.Elements.GetBoolean("/ImageMask")) return;
        if (image.Elements.GetName("/ColorSpace") != "/DeviceRGB" || image.Elements.GetInteger("/BitsPerComponent") != 8) return;

        int width = image.Elements.GetInteger("/Width"), height = image.Elements.GetInteger("/Height");
        if (width < 16 || height < 16) return;
        var filters = Filters(image);
        var original = image.Stream.Value;
        if (original.Length < 40_000) return; // small pictures: not worth it

        PdfPicture picture;
        if (filters is ["/DCTDecode"])
            picture = new PdfPicture(width, height, original, null);
        else if (filters.Count == 0 || filters is ["/FlateDecode"])
        {
            if (image.Elements.ContainsKey("/DecodeParms")) return; // predictor-encoded rows: rare for photos, left alone
            var raw = filters.Count == 0 ? original : image.Stream.UnfilteredValue;
            if (raw.Length != width * height * 3) return;
            picture = new PdfPicture(width, height, null, raw);
        }
        else return;

        var smaller = shrink(picture);
        if (smaller is null || smaller.Jpeg.Length >= original.Length * 0.9) return;
        image.Stream.Value = smaller.Jpeg;
        image.Elements.SetName("/Filter", "/DCTDecode");
        image.Elements.Remove("/DecodeParms");
        image.Elements.SetInteger("/Width", smaller.Width);
        image.Elements.SetInteger("/Height", smaller.Height);
        image.Elements.SetInteger("/Length", smaller.Jpeg.Length);
    }

    private static List<string> Filters(PdfDictionary image)
    {
        var item = image.Elements["/Filter"];
        if (item is PdfReference r) item = r.Value;
        return item switch
        {
            PdfName name => [name.Value],
            PdfArray array => array.Elements.Select(e => (e as PdfName)?.Value ?? "").ToList(),
            _ => [],
        };
    }
}
