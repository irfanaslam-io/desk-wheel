namespace Wheel.Core;

/// <summary>Decides a <see cref="FileKind"/> from a path's extension (no file I/O except the folder check).</summary>
public static class FileClassifier
{
    private static readonly Dictionary<string, FileKind> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images (decoded by Windows Imaging Component; WebP/HEIC/AVIF need the free Microsoft Store codecs)
        [".jpg"] = FileKind.Image, [".jpeg"] = FileKind.Image, [".png"] = FileKind.Image,
        [".bmp"] = FileKind.Image, [".gif"] = FileKind.Image, [".tif"] = FileKind.Image,
        [".tiff"] = FileKind.Image, [".webp"] = FileKind.Image, [".heic"] = FileKind.Image,
        [".heif"] = FileKind.Image, [".avif"] = FileKind.Image, [".ico"] = FileKind.Image,
        [".jfif"] = FileKind.Image,

        // Video
        [".mp4"] = FileKind.Video, [".mov"] = FileKind.Video, [".mkv"] = FileKind.Video,
        [".webm"] = FileKind.Video, [".avi"] = FileKind.Video, [".wmv"] = FileKind.Video,
        [".m4v"] = FileKind.Video, [".flv"] = FileKind.Video, [".3gp"] = FileKind.Video,

        // Audio
        [".mp3"] = FileKind.Audio, [".m4a"] = FileKind.Audio, [".wav"] = FileKind.Audio,
        [".flac"] = FileKind.Audio, [".ogg"] = FileKind.Audio, [".opus"] = FileKind.Audio,
        [".aiff"] = FileKind.Audio, [".aif"] = FileKind.Audio, [".wma"] = FileKind.Audio,
        [".aac"] = FileKind.Audio,

        // Archives
        [".zip"] = FileKind.Archive, [".tar"] = FileKind.Archive, [".gz"] = FileKind.Archive,
        [".tgz"] = FileKind.Archive,

        // Subtitles
        [".srt"] = FileKind.Subtitle, [".vtt"] = FileKind.Subtitle,

        [".pdf"] = FileKind.Pdf,

        // Office (LibreOffice, or textutil on Mac for word-processor text formats)
        [".docx"] = FileKind.Document, [".doc"] = FileKind.Document, [".rtf"] = FileKind.Document,
        [".odt"] = FileKind.Document, [".txt"] = FileKind.Document,
        [".xlsx"] = FileKind.Spreadsheet, [".xls"] = FileKind.Spreadsheet, [".ods"] = FileKind.Spreadsheet,
        [".csv"] = FileKind.Spreadsheet,
        [".pptx"] = FileKind.Presentation, [".ppt"] = FileKind.Presentation, [".odp"] = FileKind.Presentation,

        [".md"] = FileKind.Markdown, [".markdown"] = FileKind.Markdown, [".mdown"] = FileKind.Markdown, [".mkd"] = FileKind.Markdown,
    };

    public static FileKind Classify(string path)
    {
        if (Directory.Exists(path))
            return FileKind.Folder;

        var ext = Path.GetExtension(path);
        // ".tar.gz" ends in ".gz" and lands on Archive already.
        return ByExtension.TryGetValue(ext, out var kind) ? kind : FileKind.Other;
    }
}
