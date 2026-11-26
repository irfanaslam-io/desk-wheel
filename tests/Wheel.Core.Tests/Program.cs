using System.Diagnostics;
using System.IO.Compression;
using Wheel.Core;
using Wheel.Core.Converters;
using Wheel.Core.Ocr;

// Minimal test runner: each test is a named lambda; failures are collected and reported.
var tests = new List<(string Name, Func<Task> Body)>();
void Test(string name, Action body) => tests.Add((name, () => { body(); return Task.CompletedTask; }));
void TestAsync(string name, Func<Task> body) => tests.Add((name, body));

static void Eq<T>(T expected, T actual, string? what = null)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{what ?? "value"}: expected <{expected}> but got <{actual}>");
}
static void True(bool cond, string what) { if (!cond) throw new Exception(what); }

var work = Path.Combine(Path.GetTempPath(), "wheel-tests-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(work);
string Dir(string name) { var d = Path.Combine(work, name.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(d); return d; }
var caps = new Capabilities(HasFfmpeg: true);
var noFfmpeg = new Capabilities(HasFfmpeg: false);
string[] Ids(IEnumerable<WheelOption> o) => o.Select(x => x.Id).ToArray();

// ---------- Classifier ----------
Test("classifies common extensions case-insensitively", () =>
{
    Eq(FileKind.Image, FileClassifier.Classify("a.HEIC"));
    Eq(FileKind.Video, FileClassifier.Classify("clip.mkv"));
    Eq(FileKind.Audio, FileClassifier.Classify("song.flac"));
    Eq(FileKind.Archive, FileClassifier.Classify("x.tar.gz"));
    Eq(FileKind.Subtitle, FileClassifier.Classify("x.vtt"));
    Eq(FileKind.Other, FileClassifier.Classify("x.unknown"));
    Eq(FileKind.Folder, FileClassifier.Classify(work));
});

// ---------- Option registry ----------
Test("convert wheel hides the format the file already has", () =>
{
    var ids = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["photo.png"], caps));
    True(!ids.Contains("img.to-png"), "PNG offered for a PNG");
    True(ids.Contains("img.to-jpg"), "JPG missing");
});

Test(".jpeg counts as already JPG", () =>
{
    var ids = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["photo.jpeg"], caps));
    True(!ids.Contains("img.to-jpg"), "JPG offered for a JPEG");
});

Test("mixed PNG + JPG keeps both targets", () =>
{
    var ids = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["a.png", "b.jpg"], caps));
    True(ids.Contains("img.to-png") && ids.Contains("img.to-jpg"), "mixed selection lost a target");
});

Test("image + video has no common conversions but still gets ZIP tool and actions", () =>
{
    Eq(0, OptionRegistry.GetOptions(WheelMode.Convert, ["a.png", "b.mp4"], caps).Count, "convert count");
    True(Ids(OptionRegistry.GetOptions(WheelMode.Tools, ["a.png", "b.mp4"], caps)).Contains("any.zip"), "zip tool missing");
    Eq(6, OptionRegistry.GetOptions(WheelMode.Actions, ["a.png", "b.mp4"], caps).Count, "action count");
    True(Ids(OptionRegistry.GetOptions(WheelMode.Actions, ["a.png"], caps)).Contains("act.copy-image"), "one image: copy picture");
    True(!Ids(OptionRegistry.GetOptions(WheelMode.Actions, ["a.pdf"], caps)).Contains("act.copy-image"), "a PDF: no copy picture");
});

Test("a folder works on its most common kind of file, skipping hidden files", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "wheel-folder-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(dir, "sub"));
    foreach (var name in new[] { "b.jpg", "a.png", "c.heic", "notes.txt", "one.pdf", ".DS_Store", ".hidden.png" })
        File.WriteAllText(Path.Combine(dir, name), "x");
    File.WriteAllText(Path.Combine(dir, "sub", "deep.png"), "x");
    try
    {
        var batch = FolderContents.Of([dir]);
        True(batch is not null, "no batch");
        Eq(FileKind.Image, batch!.Kind, "kind");
        Eq("a.png,b.jpg,c.heic", string.Join(",", batch.Files.Select(Path.GetFileName)), "files");
        Eq("3 images", FolderContents.Describe(batch), "describe");
        True(FolderContents.Of([dir, Path.Combine(dir, "a.png")]) is null, "a file in the selection should turn it off");
        True(FolderContents.Of([Path.Combine(dir, "sub", "missing")]) is null, "missing folder");
    }
    finally { Directory.Delete(dir, true); }
});

Test("archives alone get no ZIP tool; mixed with other files they do", () =>
{
    Eq("any.rename", string.Join(",", Ids(OptionRegistry.GetOptions(WheelMode.Tools, ["a.zip"], caps))), "zip of a zip: only Rename");
    True(Ids(OptionRegistry.GetOptions(WheelMode.Tools, ["a.zip", "b.png"], caps)).Contains("any.zip"), "mixed zip missing");
});

Test("ffmpeg options vanish when ffmpeg is missing", () =>
{
    Eq(0, OptionRegistry.GetOptions(WheelMode.Convert, ["clip.mp4"], noFfmpeg).Count, "video convert count");
    var img = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["a.png"], noFfmpeg));
    True(!img.Contains("img.to-avif"), "AVIF shown without ffmpeg");
    True(img.Contains("img.to-jpg") && img.Contains("img.to-webp"), "built-in image option hidden by mistake");
});

Test("BMP/TIFF/GIF need sips or ffmpeg; HEIC output needs sips (Mac)", () =>
{
    var bare = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["a.png"], noFfmpeg));
    True(!bare.Contains("img.to-bmp") && !bare.Contains("img.to-heic"), "fallback formats shown with no tools");

    var windows = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["a.png"], new Capabilities(HasFfmpeg: true, HasSips: false)));
    True(windows.Contains("img.to-tiff") && !windows.Contains("img.to-heic"), "Windows with ffmpeg: wrong set");

    var mac = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["a.png"], new Capabilities(HasFfmpeg: false, HasSips: true)));
    True(mac.Contains("img.to-heic") && mac.Contains("img.to-gif") && !mac.Contains("img.to-avif"), "Mac without ffmpeg: wrong set");
});

Test("the Screenshot wheel works with no files, and only there", () =>
{
    var capture = Ids(OptionRegistry.GetOptions(WheelMode.Capture, Array.Empty<string>(), noFfmpeg));
    True(capture.Contains("cap.area") && capture.Contains("cap.window") && capture.Contains("cap.full"), "capture options missing");
    Eq(0, OptionRegistry.GetOptions(WheelMode.Convert, Array.Empty<string>(), caps).Count, "convert with no files");
    True(!Ids(OptionRegistry.GetOptions(WheelMode.Tools, ["a.png"], caps)).Any(i => i.StartsWith("cap.")), "capture leaked into Tools");
});

Test("Text options show only when text recognition is available", () =>
{
    var ocr = caps with { HasOcr = true };
    True(!Ids(OptionRegistry.GetOptions(WheelMode.Capture, Array.Empty<string>(), caps)).Contains("cap.text"), "cap.text without OCR");
    True(Ids(OptionRegistry.GetOptions(WheelMode.Capture, Array.Empty<string>(), ocr)).Contains("cap.text"), "cap.text missing");
    Eq(13, OptionRegistry.GetOptions(WheelMode.Capture, Array.Empty<string>(), ocr).Count, "capture count");
    Eq("cap.area,cap.window,cap.full,cap.text,cap.clipboard,cap.record,cap.color,nav.more", string.Join(",", Ids(OptionRegistry.Page(OptionRegistry.GetOptions(WheelMode.Capture, Array.Empty<string>(), ocr), 0, WheelMode.Capture))), "first page of the Screenshot wheel");
    var tools = Ids(OptionRegistry.GetOptions(WheelMode.Tools, ["a.png"], ocr));
    True(tools.Contains("img.ocr") && tools.Contains("any.zip"), "image Text tool or ZIP missing");
    True(!Ids(OptionRegistry.GetOptions(WheelMode.Tools, ["a.png"], caps)).Contains("img.ocr"), "img.ocr without OCR");
});

Test("Markdown files get Read and HTML, only when the page files ship", () =>
{
    Eq(FileKind.Markdown, FileClassifier.Classify("README.md"), "kind");
    var md = caps with { HasMarkdown = true };
    Eq("md.read,md.to-html", string.Join(",", Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["README.md"], md))), "convert");
    True(Ids(OptionRegistry.GetOptions(WheelMode.Tools, ["README.md"], md)).Contains("md.read-tools"), "tools read");
    Eq(0, OptionRegistry.GetOptions(WheelMode.Convert, ["README.md"], caps).Count, "without page files");
});

Test("the Markdown page can't be broken out of, and points images at the file's folder", () =>
{
    var assets = new Wheel.Core.Markdown.MarkdownAssets("/*m*/", "/*p*/", "var x='</SCRIPT>';", "/*css*/", "", "");
    var notes = Path.Combine(Path.GetTempPath(), "My Notes"); // a real path on this system (C:\… on Windows)
    var html = Wheel.Core.Markdown.MarkdownPage.Build("# Hi\n</script><script>alert(1)</script> & <b>", "a<b>.md", notes, assets);
    True(!html.Contains("</script><script>alert(1)"), "source closed the script element");
    True(html.Contains("<title>a&lt;b&gt;.md</title>"), "title not encoded");
    var expectedBase = new Uri(Path.GetFullPath(notes) + Path.DirectorySeparatorChar).AbsoluteUri;
    True(expectedBase.EndsWith("/My%20Notes/") && html.Contains($"<base href=\"{expectedBase}\">"), "base href: " + expectedBase);
    True(!html.Contains("'</SCRIPT>'"), "library text closed the script element");
    True(html.Contains("Content-Security-Policy"), "no CSP");
});

Test("Beautify cycles through its styles and back to off, with balanced padding", () =>
{
    var seen = new List<string>();
    Wheel.Core.Editing.FrameStyle? f = null;
    do { f = Wheel.Core.Editing.Frames.Next(f); if (f is not null) seen.Add(f.Name); } while (f is not null && seen.Count < 20);
    Eq(Wheel.Core.Editing.Frames.Presets.Length, seen.Count, "cycle length");
    var small = Wheel.Core.Editing.Frames.Layout(200, 100);
    Eq(40.0, small.Padding, "minimum padding");
    Eq(280.0, small.Width, "width includes padding");
    var big = Wheel.Core.Editing.Frames.Layout(5120, 2880);
    Eq(200.0, big.Padding, "maximum padding");
    Eq(28.0, big.Radius, "maximum radius");
    var state = Wheel.Core.Editing.EditState.Empty with { Frame = Wheel.Core.Editing.Frames.Presets[1] };
    var history = new Wheel.Core.Editing.EditHistory();
    history.Apply(state);
    True(history.Undo() && history.Current.Frame is null, "frame is undoable");
});

Test("history keeps the newest entries first, survives a broken line, and searches", () =>
{
    var file = Path.Combine(Path.GetTempPath(), "wheel-history-" + Guid.NewGuid().ToString("N"), "history.jsonl");
    var store = new Wheel.Core.History.HistoryStore(file);
    var changes = 0;
    store.Changed += () => changes++;
    var t = DateTimeOffset.Now;
    store.Add(new(t, "Screenshot", "/shots/Screenshot 1.png"));
    store.Add(new(t.AddMinutes(1), "Converted", "/photos/cat.webp", "/photos/cat.jpg", "Convert to WebP"));
    File.AppendAllText(file, "{\"broken\": \n");
    store.Add(new(t.AddMinutes(2), "Edited", "/shots/Screenshot 1 (edited).png", "/shots/Screenshot 1.png", "Edited"));
    var all = store.Load();
    Eq(3, all.Count, "count");
    Eq("Screenshot 1 (edited).png", all[0].Name, "newest first");
    Eq(3, changes, "change events");
    Eq("cat.webp", Wheel.Core.History.HistoryStore.Filter(all, "webp").Single().Name, "search by action/name");
    Eq(1, Wheel.Core.History.HistoryStore.Filter(all, "SCREENSHOT").Count(e => e.Kind == "Screenshot"), "search by kind");
    store.Remove(all[1]);
    Eq(2, store.Load().Count, "remove");
    store.Clear();
    Eq(0, store.Load().Count, "clear");
    Directory.Delete(Path.GetDirectoryName(file)!, true);
});

Test("Office files: PDF needs an office suite; text formats also work with textutil", () =>
{
    Eq(FileKind.Spreadsheet, FileClassifier.Classify("budget.xlsx"), "xlsx kind");
    Eq(FileKind.Presentation, FileClassifier.Classify("deck.PPTX"), "pptx kind");
    var none = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["report.docx"], caps));
    True(!none.Any(id => id.StartsWith("doc.")), "office options without any engine");
    var textutil = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["report.docx"], caps with { HasTextutil = true }));
    Eq("doc.to-txt,doc.to-html,doc.to-odt", string.Join(",", textutil), "textutil: no PDF, no DOCX→DOCX");
    var lo = caps with { HasLibreOffice = true };
    Eq("doc.to-pdf,doc.to-docx,doc.to-html,doc.to-odt", string.Join(",", Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["notes.txt"], lo))), "txt with LibreOffice");
    Eq("xls.to-pdf,xls.to-xlsx,xls.to-ods", string.Join(",", Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["data.csv"], lo))), "csv");
    Eq("ppt.to-pdf,ppt.to-odp", string.Join(",", Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["deck.pptx"], lo))), "pptx");
    True(!Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["data.xlsx"], caps with { HasTextutil = true })).Any(), "textutil can't do spreadsheets");
});

Test("Microsoft Office: used for PDF when installed, per app, without LibreOffice", () =>
{
    var word = caps with { MicrosoftOffice = new HashSet<Wheel.Core.Converters.OfficeApp> { Wheel.Core.Converters.OfficeApp.Word } };
    var docIds = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["report.docx"], word));
    True(docIds.Contains("doc.to-pdf"), "Word → PDF with Word alone");
    True(!Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["deck.pptx"], word)).Any(), "no PowerPoint, no LibreOffice: nothing for pptx");
    var all = caps with { MicrosoftOffice = new HashSet<Wheel.Core.Converters.OfficeApp>(Enum.GetValues<Wheel.Core.Converters.OfficeApp>()) };
    True(Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["budget.xlsx"], all)).Contains("xls.to-pdf"), "Excel → PDF");
    True(Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["deck.pptx"], all)).Contains("ppt.to-pdf"), "PowerPoint → PDF");
    Eq("PDF", OptionRegistry.GetOptions(WheelMode.Convert, ["deck.pptx"], all).First(o => o.Id == "ppt.to-pdf").Label, "label");
    // Windows uses Office for the other formats too; a Mac only for PDF.
    True(Wheel.Core.Converters.MicrosoftOffice.CanDo("xls.to-csv", ".csv", all.MicrosoftOffice, windows: true), "Excel CSV on Windows");
    True(!Wheel.Core.Converters.MicrosoftOffice.CanDo("xls.to-csv", ".csv", all.MicrosoftOffice, windows: false), "not on Mac");
    True(!Wheel.Core.Converters.MicrosoftOffice.CanDo("doc.to-txt", ".txt", all.MicrosoftOffice, windows: true), "TXT left to UTF-8 tools");
    Eq(17, Wheel.Core.Converters.MicrosoftOffice.WindowsFormat(Wheel.Core.Converters.OfficeApp.Word, ".PDF"), "wdFormatPDF");
    Eq(0, Wheel.Core.Converters.MicrosoftOffice.WindowsFormat(Wheel.Core.Converters.OfficeApp.Excel, ".pdf"), "xlTypePDF");
    Eq(32, Wheel.Core.Converters.MicrosoftOffice.WindowsFormat(Wheel.Core.Converters.OfficeApp.PowerPoint, ".pdf"), "ppSaveAsPDF");
});

Test("Microsoft Office scripts: read-only, no macros, paths can't break out, Office left open if it was", () =>
{
    var evil = @"C:\Docs\Bob's ""Q1""; $(rm -r C:\).docx";
    var ps = Wheel.Core.Converters.MicrosoftOffice.WindowsScript(Wheel.Core.Converters.OfficeApp.Word, evil, @"C:\Docs\out.pdf", 17);
    True(ps.Contains(@"$in = 'C:\Docs\Bob''s ""Q1""; $(rm -r C:\).docx'"), "single-quoted, quote doubled, nothing expanded");
    True(ps.Contains("$doc.SaveAs2($out, 17)"), "pdf format");
    True(ps.Contains("AutomationSecurity = 3"), "macros off");
    True(ps.Contains("Documents.Open($in, $false, $true"), "read-only");
    True(ps.Contains("if ($app.Documents.Count -eq 0) { $app.Quit() }"), "quit only when nothing else is open");
    var xl = Wheel.Core.Converters.MicrosoftOffice.WindowsScript(Wheel.Core.Converters.OfficeApp.Excel, "a.xlsx", "a.pdf", 0);
    True(xl.Contains("ExportAsFixedFormat(0, $out)"), "whole workbook to PDF");
    var args = Wheel.Core.Converters.MicrosoftOffice.PowerShellArgs(ps);
    Eq("-EncodedCommand", args[^2], "encoded");
    Eq(ps, System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(args[^1])), "round trip");
    var mac = Wheel.Core.Converters.MicrosoftOffice.MacScript(Wheel.Core.Converters.OfficeApp.PowerPoint);
    True(mac.Contains("on run argv") && mac.Contains("application id \"com.microsoft.Powerpoint\""), "argv + bundle id");
    True(mac.Contains("save d in outHfs as save as PDF"), "ppt pdf");
    True(mac.Contains("if (not wasRunning)"), "leaves a running PowerPoint open");
    True(Wheel.Core.Converters.MicrosoftOffice.Explain(Wheel.Core.Converters.OfficeApp.Word, "x.docx", "The password is incorrect").Contains("has a password"), "password message");
    True(Wheel.Core.Converters.MicrosoftOffice.Explain(Wheel.Core.Converters.OfficeApp.Excel, "x.xlsx", "execution error: Not authorized to send Apple events to Microsoft Excel. (-1743)").Contains("Automation"), "permission message");
});

Test("LibreOffice is run headless, with a private profile, into a separate folder", () =>
{
    var profile = Path.Combine(Path.GetTempPath(), "my profile");
    var args = Wheel.Core.Converters.OfficeConverter.LibreOfficeArgs("/docs/My Report.docx", ".pdf", "/tmp/out", profile);
    Eq("-env:UserInstallation=" + new Uri(Path.GetFullPath(profile) + Path.DirectorySeparatorChar).AbsoluteUri, args[0], "profile uri");
    True(args[0].EndsWith("/my%20profile/"), "spaces encoded, trailing slash");
    True(args.Contains("--headless") && args.Contains("--norestore"), "headless");
    Eq("pdf", args[args.Count - 4], "filter");
    Eq("/tmp/out", args[args.Count - 2], "outdir");
    Eq("/docs/My Report.docx", args[^1], "input last, as one argument");
    Eq("txt:Text (encoded):UTF8", Wheel.Core.Converters.OfficeConverter.LibreOfficeFilter(".txt"), "utf-8 text");
    Eq("csv:Text - txt - csv (StarCalc):44,34,76", Wheel.Core.Converters.OfficeConverter.LibreOfficeFilter(".CSV"), "utf-8 csv");
    Eq(null, Wheel.Core.Converters.OfficeConverter.TextutilFormat(".pdf"), "textutil can't write PDF");
    Eq("docx", Wheel.Core.Converters.OfficeConverter.TextutilFormat(".docx"), "textutil docx");
});

Test("clipboard history: newest first, no duplicates, pins survive clearing, images on disk", () =>
{
    var folder = Path.Combine(Path.GetTempPath(), "wheel-clip-" + Guid.NewGuid().ToString("N"));
    var store = new Wheel.Core.Clipboard.ClipboardStore(folder);
    var t = DateTimeOffset.Now;
    var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 5, 0, 0, 0, 2, 0xD0, 8, 6 };
    Eq((1280, 720), Wheel.Core.Clipboard.PngInfo.Size(png)!.Value, "png size");
    var a = store.Add(new("hello world", null, null), t)!;
    store.Add(new(null, png, null, 1280, 720), t.AddSeconds(1));
    store.Add(new(null, null, ["/docs/report.pdf", "/docs/b.txt"]), t.AddSeconds(2));
    Eq(null, store.Add(new("   ", null, null), t.AddSeconds(3)), "blank text ignored");
    Eq("2 files", store.Entries[0].Title, "files title");
    Eq("Image 1280×720", store.Entries[1].Title, "image title");
    True(File.Exists(store.ImagePath(store.Entries[1])), "image saved");
    store.Add(new("hello world", null, null), t.AddSeconds(4));
    Eq(3, store.Entries.Count, "same text again is not duplicated");
    Eq(a.Id, store.Entries[0].Id, "it moved to the top");
    store.SetPinned(store.Entries[0], true);
    Eq("hello world", Wheel.Core.Clipboard.ClipboardStore.Filter(store.Entries, "HELLO").Single().Text, "search");
    var image = store.Entries.First(e => e.Kind == Wheel.Core.Clipboard.ClipKind.Image);
    store.ClearUnpinned();
    Eq(1, store.Entries.Count, "only the pin is left");
    True(!File.Exists(store.ImagePath(image)), "image file removed");
    var reopened = new Wheel.Core.Clipboard.ClipboardStore(folder);
    True(reopened.Entries.Single().Pinned, "saved and reloaded");
    for (var i = 0; i < Wheel.Core.Clipboard.ClipboardStore.MaxEntries + 20; i++)
        reopened.Add(new($"item {i}", null, null), t.AddMinutes(i));
    Eq(Wheel.Core.Clipboard.ClipboardStore.MaxEntries + 1, reopened.Entries.Count, "limit (plus the pin)");
    Directory.Delete(folder, true);
});

Test("QR codes: made and read back, including Arabic text", () =>
{
    foreach (var text in new[] { "https://irfanaslam.io", "مرحبا بالعالم — Wheel 2026" })
    {
        var (w, h, bgra) = Wheel.Core.Screen.QrCodes.Make(text, 400);
        Eq(400, w, "width");
        // Put it on a bigger grey "screen" with an offset, as it would be in a screenshot.
        int sw = 900, sh = 700;
        var screen = new byte[sw * sh * 4];
        for (var i = 0; i < screen.Length; i += 4) { screen[i] = screen[i + 1] = screen[i + 2] = 200; screen[i + 3] = 255; }
        for (var y = 0; y < h; y++)
            Buffer.BlockCopy(bgra, y * w * 4, screen, ((y + 150) * sw + 300) * 4, w * 4);
        var found = Wheel.Core.Screen.QrCodes.Read(screen, sw, sh);
        Eq(text, found.FirstOrDefault(), "read back");
    }
    Eq(0, Wheel.Core.Screen.QrCodes.Read(new byte[100 * 100 * 4], 100, 100).Count, "nothing in a blank image");
    True(Wheel.Core.Screen.QrCodes.IsWebLink("https://example.com/a?b=c") && !Wheel.Core.Screen.QrCodes.IsWebLink("WIFI:S:home;;"), "links");
});

Test("colours are written as HEX, RGB and HSL", () =>
{
    Eq("#E8622C", Wheel.Core.Screen.ScreenColor.Hex(232, 98, 44), "hex");
    Eq("rgb(232, 98, 44)", Wheel.Core.Screen.ScreenColor.Rgb(232, 98, 44), "rgb");
    Eq("hsl(17, 80%, 54%)", Wheel.Core.Screen.ScreenColor.Hsl(232, 98, 44), "hsl");
    Eq("hsl(0, 0%, 100%)", Wheel.Core.Screen.ScreenColor.Hsl(255, 255, 255), "white");
});

Test("screen recording: even sizes, the right screen device, both platforms", () =>
{
    var list = "[AVFoundation indev @ 0x1] AVFoundation video devices:\n[AVFoundation indev @ 0x1] [0] FaceTime HD Camera\n"
             + "[AVFoundation indev @ 0x1] [1] Capture screen 0\n[AVFoundation indev @ 0x1] [2] Capture screen 1\n";
    Eq(2, Wheel.Core.Screen.RecordingPlan.MacScreenDevice(list, 1), "second screen");
    Eq(null, Wheel.Core.Screen.RecordingPlan.MacScreenDevice(list, 5), "missing screen");
    var mac = Wheel.Core.Screen.RecordingPlan.Mac(1, new(10, 20, 641, 361), "/tmp/out.mp4");
    True(mac.Contains("1:none") && mac.Contains("crop=640:360:10:20"), "mac crop is even");
    var win = Wheel.Core.Screen.RecordingPlan.Windows(new(-1920, 0, 801, 600), "C:/out.mp4");
    True(win.Contains("-1920") && win.Contains("800x600") && win[^1] == "C:/out.mp4", "windows offset and size");
    Eq("Screen Recording 2026-11-24 at 14.05.09.mp4", Wheel.Core.Screen.RecordingPlan.FileName(new DateTime(2026, 11, 24, 14, 5, 9)), "name");
});

Test("resize sizes: fit inside, width only, height only, percent; never distorted", () =>
{
    var box = Wheel.Core.Files.ResizeSpec.Parse("1920 x 1080")!;
    Eq((1440, 1080), box.For(4000, 3000), "fit inside a box");
    Eq((800, 600), Wheel.Core.Files.ResizeSpec.Parse("800")!.For(4000, 3000), "width");
    Eq((960, 720), Wheel.Core.Files.ResizeSpec.Parse("x720")!.For(4000, 3000), "height");
    Eq((1000, 750), Wheel.Core.Files.ResizeSpec.Parse("25%")!.For(4000, 3000), "percent");
    Eq(null, Wheel.Core.Files.ResizeSpec.Parse("big"), "nonsense");
    Eq(null, Wheel.Core.Files.ResizeSpec.Parse("0x0"), "zero");
    Eq(10, Wheel.Core.Files.IconSet.MacIconset().Count, "iconset entries");
});

Test("bulk rename: preview, numbering in natural order, no overwriting, case-only renames, undo", () =>
{
    var d = Dir("rename");
    foreach (var n in new[] { "photo 10.jpg", "photo 2.jpg", "photo 1.jpg", "keep.txt", "Trip 3.jpg" })
        File.WriteAllText(Path.Combine(d, n), n);
    var files = Directory.GetFiles(d, "*.jpg");
    var date = new DateTime(2026, 11, 24);
    var plan = Wheel.Core.Files.RenamePlan.Build(files, new("Holiday {n}"), _ => date);
    Eq("photo 1.jpg>Holiday 1.jpg,photo 2.jpg>Holiday 2.jpg,photo 10.jpg>Holiday 3.jpg,Trip 3.jpg>Holiday 4.jpg",
       string.Join(",", plan.Select(i => Path.GetFileName(i.From) + ">" + Path.GetFileName(i.To))), "natural order");
    var clash = Wheel.Core.Files.RenamePlan.Build(files, new("same"), _ => date);
    Eq(3, clash.Count(i => i.Problem is not null), "only the first can take a repeated name");
    var taken = Wheel.Core.Files.RenamePlan.Build([Path.Combine(d, "photo 1.jpg")], new("keep"), _ => date);
    True(taken[0].Problem is null, "different extension is fine");
    var exists = Wheel.Core.Files.RenamePlan.Build([Path.Combine(d, "photo 1.jpg")], new("photo 2"), _ => date);
    Eq("A file with that name already exists", exists[0].Problem, "never overwrite");
    Eq("Names can't contain / \\ : * ? \" < > |", Wheel.Core.Files.RenamePlan.Build([Path.Combine(d, "keep.txt")], new("a/b"), _ => date)[0].Problem, "bad characters");

    var (done, failed) = Wheel.Core.Files.RenamePlan.Apply(Wheel.Core.Files.RenamePlan.Build(files, new("{date} {name}", Find: "photo", Replace: "img", Case: Wheel.Core.Files.NameCase.Upper), _ => date));
    Eq(0, failed.Count, "no failures");
    True(File.Exists(Path.Combine(d, "2026-11-24 IMG 10.jpg")) && File.ReadAllText(Path.Combine(d, "2026-11-24 IMG 10.jpg")) == "photo 10.jpg", "renamed, content kept");
    Eq(4, Wheel.Core.Files.RenamePlan.Undo(done), "undo count");
    True(File.Exists(Path.Combine(d, "photo 10.jpg")) && File.Exists(Path.Combine(d, "Trip 3.jpg")), "names restored");

    var caseOnly = Wheel.Core.Files.RenamePlan.Build([Path.Combine(d, "keep.txt")], new("{name}", Case: Wheel.Core.Files.NameCase.Upper), _ => date);
    Wheel.Core.Files.RenamePlan.Apply(caseOnly);
    Eq("KEEP.txt", Path.GetFileName(Directory.GetFiles(d, "*.txt").Single()), "case-only rename");
});

Test("watched folders skip hidden, partial and Wheel-made files, and files that don't fit", () =>
{
    var webp = OptionRegistry.All.First(o => o.Id == "img.to-webp");
    var none = new HashSet<string>();
    True(Wheel.Core.Files.WatchDecider.ShouldProcess("/in/cat.jpg", webp, caps, none), "a new photo");
    True(!Wheel.Core.Files.WatchDecider.ShouldProcess("/in/cat.webp", webp, caps, none), "already WebP");
    True(!Wheel.Core.Files.WatchDecider.ShouldProcess("/in/.cat.jpg", webp, caps, none), "hidden");
    True(!Wheel.Core.Files.WatchDecider.ShouldProcess("/in/cat.jpg.crdownload", webp, caps, none), "still downloading");
    True(!Wheel.Core.Files.WatchDecider.ShouldProcess("/in/cat (compressed).jpg", webp, caps, none), "Wheel output name");
    True(!Wheel.Core.Files.WatchDecider.ShouldProcess("/in/dog.jpg", webp, caps, new HashSet<string> { "/in/dog.jpg" }), "made by Wheel");
    True(!Wheel.Core.Files.WatchDecider.ShouldProcess("/in/notes.txt", webp, caps, none), "not an image");
});

Test("bundled ffmpeg: the right H.264 encoder per system, and options it can't do are hidden", () =>
{
    var listing = " V....D libx264              libx264 H.264 / AVC\n V....D h264_videotoolbox    VideoToolbox H.264 Encoder\n A....D aac                  AAC\n A....D libmp3lame           MP3\n ------\n";
    var parsed = FfmpegConverter.ParseEncoders(listing);
    True(parsed.Contains("libx264") && parsed.Contains("h264_videotoolbox") && parsed.Contains("libmp3lame"), "parsed: " + string.Join(",", parsed));
    Eq("libx264", FfmpegConverter.H264Encoder(parsed), "x264 first when present");
    var mac = new HashSet<string> { "h264_videotoolbox", "aac", "libmp3lame", "libopus", "libvpx-vp9", "libvorbis", "libaom-av1" };
    var win = new HashSet<string> { "h264_mf", "libopenh264", "aac", "libmp3lame", "libopus", "libvpx-vp9", "libvorbis", "libaom-av1" };
    Eq("h264_videotoolbox", FfmpegConverter.H264Encoder(mac), "Mac");
    Eq("h264_mf", FfmpegConverter.H264Encoder(win), "Windows");
    var mp4 = OptionRegistry.All.First(o => o.Id == "vid.to-mp4");
    var args = FfmpegConverter.BuildArguments(mp4, "in.mov", "out.mp4", encoders: win);
    True(args.Contains("h264_mf") && args.Contains("nv12") && !args.Contains("libx264"), "Windows recipe: " + string.Join(" ", args));
    True(FfmpegConverter.BuildArguments(mp4, "in.mov", "out.mp4", encoders: mac).Contains("h264_videotoolbox"), "Mac recipe");
    var noH264 = caps with { HasFfmpeg = true, FfmpegEncoders = new HashSet<string> { "aac", "libmp3lame" } };
    var video = Ids(OptionRegistry.GetOptions(WheelMode.Convert, ["clip.mov"], noH264));
    True(!video.Contains("vid.to-mp4") && !video.Contains("vid.to-webm") && video.Contains("vid.to-mp3") && video.Contains("vid.to-gif"), "hidden: " + string.Join(",", video));
    var rec = Wheel.Core.Screen.RecordingPlan.Mac(1, new(0, 0, 640, 360), "/tmp/r.mp4", FfmpegConverter.H264Args("h264_videotoolbox", FfmpegConverter.VideoQuality.Normal));
    True(rec.Contains("h264_videotoolbox") && !rec.Contains("libx264"), "recording uses the system encoder");
});

Test("OCR text is tidied the same way for every engine", () =>
{
    Eq("Invoice 114\nTotal: 250 SAR\n\nThank you", OcrText.Tidy("  Invoice 114 \r\nTotal: 250 SAR\r\n\r\n\r\n  Thank you  \n\n"), "tidy");
    Eq("", OcrText.Tidy(" \n \r\n"), "empty");
    Eq(3, OcrText.WordCount("one two\nthree"), "words");
    Eq("Copied 1 word", OcrText.CopiedSummary("hello"), "singular");
    Eq("a · b", OcrText.Preview("a\nb"), "preview join");
    Eq(10, OcrText.Preview(new string('x', 50), 10).Length, "preview cut");
    True(OcrText.IsMostlyArabic("مرحبا بالعالم 2026"), "arabic");
    True(!OcrText.IsMostlyArabic("Hello مرحبا world again"), "mostly english");
    True(!OcrText.IsMostlyArabic("123 456"), "no letters");
});

Test("no wheel page ever has more than 8 segments, and paging shows every option once", () =>
{
    var full = caps with { HasOcr = true, HasLibreOffice = true, HasMarkdown = true };
    foreach (var ext in new[] { ".png", ".mp4", ".mp3", ".zip", ".srt", ".pdf", ".docx", ".md" })
        foreach (var mode in Enum.GetValues<WheelMode>())
        {
            var all = OptionRegistry.GetOptions(mode, ["f" + ext], full);
            var seen = new List<string>();
            for (var page = 0; page < OptionRegistry.PageCount(all.Count); page++)
            {
                var shown = OptionRegistry.Page(all, page, mode);
                True(shown.Count <= OptionRegistry.MaxSegments, $"{ext} {mode} page {page} too many");
                seen.AddRange(shown.Where(o => o.Engine != EngineKind.Navigation).Select(o => o.Id));
            }
            Eq(string.Join(",", all.Select(o => o.Id)), string.Join(",", seen), $"{ext} {mode} paging");
        }
    var twenty = Enumerable.Range(1, 20).Select(i => new WheelOption($"o{i}", $"{i}", "", WheelMode.Tools, EngineKind.Image)).ToList();
    Eq("o1,o2,o3,o4,o5,o6,o7,nav.more", string.Join(",", Ids(OptionRegistry.Page(twenty, 0, WheelMode.Tools))), "first page");
    Eq("nav.back,o8,o9,o10,o11,o12,o13,nav.more", string.Join(",", Ids(OptionRegistry.Page(twenty, 1, WheelMode.Tools))), "middle page");
    Eq("nav.back,o20", string.Join(",", Ids(OptionRegistry.Page(twenty, 3, WheelMode.Tools))), "last page");
    Eq("o5,o1,o2", string.Join(",", Ids(OptionRegistry.WithPinsFirst(twenty.Take(5).ToList(), ["o5", "gone"]).Take(3).ToList())), "pins first");
});

Test("every option id is unique", () =>
{
    var ids = OptionRegistry.All.Select(o => o.Id).ToList();
    Eq(ids.Count, ids.Distinct().Count(), "distinct ids");
});

// ---------- Output naming ----------
Test("output goes beside the original and never overwrites", () =>
{
    var d = Dir("naming");
    var src = Path.Combine(d, "photo.png");
    File.WriteAllText(src, "x");
    var first = OutputPaths.Beside(src, ".jpg");
    Eq(Path.Combine(d, "photo.jpg"), first);
    File.WriteAllText(first, "x");
    Eq(Path.Combine(d, "photo (1).jpg"), OutputPaths.Beside(src, ".jpg"));
});

Test("same-type output gets a descriptive suffix", () =>
{
    var d = Dir("suffix");
    var src = Path.Combine(d, "photo.jpg");
    Eq(Path.Combine(d, "photo (compressed).jpg"), OutputPaths.Beside(src, ".jpg", "compressed"));
    Eq(Path.Combine(d, "photo (edited).jpg"), OutputPaths.Beside(src, null));
});

Test("compound extensions are kept whole", () =>
{
    Eq(".tar.gz", OutputPaths.FullExtension("backup.tar.gz"));
    Eq("backup", OutputPaths.StemOf("backup.tar.gz"));
    Eq("my.report", OutputPaths.StemOf("my.report.pdf"));
});

// ---------- Subtitles ----------
const string Srt = "﻿1\r\n00:00:01,500 --> 00:00:03,000\r\nHello <i>there</i>\r\n\r\n2\r\n00:01:02,005 --> 00:01:04,100\r\nSecond line\r\ncontinues\r\n";

Test("SRT parses (BOM, CRLF, multi-line cues)", () =>
{
    var cues = SubtitleConverter.Parse(Srt);
    Eq(2, cues.Count, "cue count");
    Eq(TimeSpan.FromMilliseconds(1500), cues[0].Start);
    Eq("Second line\ncontinues", cues[1].Text);
});

Test("SRT → VTT → SRT round trip keeps timings", () =>
{
    var vtt = SubtitleConverter.ToVtt(SubtitleConverter.Parse(Srt));
    True(vtt.StartsWith("WEBVTT"), "VTT header");
    True(vtt.Contains("00:01:02.005 --> 00:01:04.100"), "VTT uses dots");
    var back = SubtitleConverter.Parse(SubtitleConverter.ToSrt(SubtitleConverter.Parse(vtt)));
    Eq(TimeSpan.FromMilliseconds(64100), back[1].End);
});

Test("VTT without hours and with cue settings parses", () =>
{
    var cues = SubtitleConverter.Parse("WEBVTT\n\nNOTE hi\n\n01:02.300 --> 01:04.000 align:start\nShort form\n");
    Eq(1, cues.Count, "cue count");
    Eq(TimeSpan.FromMilliseconds(62300), cues[0].Start);
});

Test("subtitles → plain text strips tags and timings", () =>
{
    Eq("Hello there\nSecond line continues\n", SubtitleConverter.ToPlainText(SubtitleConverter.Parse(Srt)));
});

Test("subtitle file conversion writes beside the source", () =>
{
    var d = Dir("subs");
    var src = Path.Combine(d, "movie.srt");
    File.WriteAllText(src, Srt);
    var opt = OptionRegistry.All.First(o => o.Id == "sub.to-vtt");
    var r = SubtitleConverter.Run(opt, src);
    True(r.Success, r.Error ?? "failed");
    Eq(Path.Combine(d, "movie.vtt"), r.Outputs[0]);
});

// ---------- Archives ----------
Test("ZIP a folder + file, extract, contents match", () =>
{
    var d = Dir("zip");
    var folder = Dir("zip/Project");
    Directory.CreateDirectory(Path.Combine(folder, "sub"));
    File.WriteAllText(Path.Combine(folder, "sub", "a.txt"), "alpha");
    var loose = Path.Combine(d, "loose.txt");
    File.WriteAllText(loose, "beta");

    var r = ArchiveConverter.ZipSelection([folder, loose]);
    True(r.Success, r.Error ?? "zip failed");
    Eq(Path.Combine(d, "Archive.zip"), r.Outputs[0]);

    var outDir = ArchiveConverter.Extract(r.Outputs[0]);
    Eq("alpha", File.ReadAllText(Path.Combine(outDir, "Project", "sub", "a.txt")));
    Eq("beta", File.ReadAllText(Path.Combine(outDir, "loose.txt")));
});

Test("ZIP → TAR.GZ → ZIP round trip", () =>
{
    var d = Dir("repack");
    var src = Dir("repack/data");
    File.WriteAllText(Path.Combine(src, "n.txt"), "hello");
    var zip = Path.Combine(d, "data.zip");
    ZipFile.CreateFromDirectory(src, zip);

    var tgz = ArchiveConverter.Repack(zip, ".tar.gz");
    Eq(Path.Combine(d, "data.tar.gz"), tgz);
    var zip2 = ArchiveConverter.Repack(tgz, ".zip");
    Eq(Path.Combine(d, "data (1).zip"), zip2);
    var outDir = ArchiveConverter.Extract(zip2);
    Eq("hello", File.ReadAllText(Path.Combine(outDir, "n.txt")));
});

Test("plain .gz decompresses to the inner file", () =>
{
    var d = Dir("gz");
    var gz = Path.Combine(d, "notes.txt.gz");
    using (var fs = File.Create(gz))
    using (var g = new GZipStream(fs, CompressionLevel.Optimal))
        g.Write("plain text"u8);
    var output = ArchiveConverter.Extract(gz);
    Eq(Path.Combine(d, "notes.txt"), output);
    Eq("plain text", File.ReadAllText(output));
});

Test("zip-slip entry cannot escape the extraction folder", () =>
{
    var d = Dir("slip");
    var evil = Path.Combine(d, "evil.zip");
    using (var z = ZipFile.Open(evil, ZipArchiveMode.Create))
    using (var w = new StreamWriter(z.CreateEntry("../../escaped.txt").Open()))
        w.Write("pwned");
    var opt = OptionRegistry.All.First(o => o.Id == "arc.extract");
    var r = ArchiveConverter.Run(opt, [evil]);
    True(!r.Success, "malicious archive reported success");
    True(!File.Exists(Path.Combine(work, "escaped.txt")) && !File.Exists(Path.Combine(d, "..", "escaped.txt")), "file escaped");
});

// ---------- Settings-driven behaviour ----------

Test("a chosen output folder receives every result; clearing it restores 'beside the original'", () =>
{
    var src = Path.Combine(Dir("override-src"), "photo.png");
    File.WriteAllText(src, "x");
    var target = Path.Combine(work, "override-out");
    try
    {
        OutputPaths.OverrideFolder = target;
        Eq(Path.Combine(target, "photo.jpg"), OutputPaths.Beside(src, ".jpg"));
        True(Directory.Exists(target), "override folder not created");
    }
    finally
    {
        OutputPaths.OverrideFolder = null;
    }
    Eq(Path.Combine(work, "override-src", "photo.jpg"), OutputPaths.Beside(src, ".jpg"));
});

Test("a custom ffmpeg location is used, and clearing it searches again", () =>
{
    var fake = Path.Combine(Dir("fake-ffmpeg"), OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    File.WriteAllText(fake, "");
    try
    {
        FfmpegConverter.SetOverride(fake);
        Eq(fake, FfmpegConverter.Locate());
    }
    finally
    {
        FfmpegConverter.SetOverride(null);
    }
    True(FfmpegConverter.Locate() != fake, "override not cleared");
});

// ---------- Screenshot editor logic ----------

Test("editor history: undo, redo, and a new edit clears redo", () =>
{
    var h = new Wheel.Core.Editing.EditHistory();
    var a = new Wheel.Core.Editing.Annotation(Wheel.Core.Editing.EditTool.Rectangle, 0, 0, 10, 10, 0xFFFF0000);
    var b = a with { X2 = 20 };
    h.Apply(h.Current.With(a));
    h.Apply(h.Current.With(b));
    Eq(2, h.Current.Annotations.Count, "after two edits");
    True(h.Undo(), "undo");
    Eq(1, h.Current.Annotations.Count, "after undo");
    True(h.Redo(), "redo");
    Eq(2, h.Current.Annotations.Count, "after redo");
    h.Undo();
    h.Apply(h.Current.With(a));
    True(!h.CanRedo, "redo survived a new edit");
});

Test("step markers number themselves 1, 2, 3", () =>
{
    var s = Wheel.Core.Editing.EditState.Empty;
    Eq(1, s.NextStepNumber);
    s = s.With(new(Wheel.Core.Editing.EditTool.Step, 5, 5, 5, 5, 0, Number: 1));
    s = s.With(new(Wheel.Core.Editing.EditTool.Arrow, 0, 0, 9, 9, 0));
    Eq(2, s.NextStepNumber);
});

Test("rectangles normalise from any drag direction and clamp to the image", () =>
{
    var r = Wheel.Core.Editing.PixelRect.FromCorners(50, 40, 10, 100);
    Eq((10d, 40d, 40d, 60d), (r.X, r.Y, r.Width, r.Height));
    var c = new Wheel.Core.Editing.PixelRect(-5, 90, 30, 30).ClampTo(100, 100);
    Eq((0d, 90d, 25d, 10d), (c.X, c.Y, c.Width, c.Height));
});

Test("view clicks map to image pixels through zoom and crop", () =>
{
    var crop = new Wheel.Core.Editing.PixelRect(100, 50, 400, 300);
    var (x, y) = Wheel.Core.Editing.EditGeometry.ViewToImage(20, 10, 0.5, crop);
    Eq((140d, 70d), (x, y));
});

Test("arrowheads point back along the shaft", () =>
{
    var (l, r) = Wheel.Core.Editing.EditGeometry.ArrowHead(0, 0, 100, 0, 20);
    True(l.X < 100 && r.X < 100, "head corners should be behind the tip");
    True(Math.Abs(l.Y + r.Y) < 1e-9, "head should be symmetric");
});

Test("blur blocks are coarse enough to hide text", () =>
{
    Eq(10, Wheel.Core.Editing.EditGeometry.BlurBlock(new(0, 0, 60, 20)));
    Eq(50, Wheel.Core.Editing.EditGeometry.BlurBlock(new(0, 0, 600, 100)));
});

// ---------- Screenshots ----------

Test("screenshot names are dated, colon-free and unique", () =>
{
    var d = Dir("shots");
    var when = new DateTime(2026, 11, 23, 21, 40, 12);
    var first = Wheel.Core.Capture.CaptureNaming.NewPath(d, when);
    Eq(Path.Combine(d, "Screenshot 2026-11-23 at 21.40.12.png"), first);
    File.WriteAllText(first, "x");
    Eq(Path.Combine(d, "Screenshot 2026-11-23 at 21.40.12 (1).png"), Wheel.Core.Capture.CaptureNaming.NewPath(d, when));
});

Test("DIB round trip keeps every pixel (3x2, top-down in, bottom-up on the clipboard)", () =>
{
    var bgra = new byte[3 * 2 * 4];
    for (var i = 0; i < bgra.Length; i++) bgra[i] = (byte)(i * 7);
    for (var i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
    var dib = Wheel.Core.Capture.Dib.FromBgra(3, 2, bgra);
    var (w, h, back) = Wheel.Core.Capture.Dib.ToBgra(dib);
    Eq((3, 2), (w, h), "size");
    True(back.AsSpan().SequenceEqual(bgra), "pixels changed in the round trip");
});

Test("24-bit bottom-up DIB with padded rows decodes upright", () =>
{
    // 1x2 image: top pixel red, bottom pixel blue. Rows are padded to 4 bytes; bottom row comes first.
    var dib = new byte[40 + 2 * 4];
    BitConverter.GetBytes(40).CopyTo(dib, 0);
    BitConverter.GetBytes(1).CopyTo(dib, 4);
    BitConverter.GetBytes(2).CopyTo(dib, 8);
    BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
    BitConverter.GetBytes((ushort)24).CopyTo(dib, 14);
    dib[40] = 255; dib[41] = 0; dib[42] = 0;      // bottom row: blue (B,G,R)
    dib[44] = 0; dib[45] = 0; dib[46] = 255;      // top row: red
    var (_, _, px) = Wheel.Core.Capture.Dib.ToBgra(dib);
    Eq((byte)255, px[2], "top pixel should be red");
    Eq((byte)255, px[4], "bottom pixel should be blue");
    Eq((byte)255, px[3], "alpha forced opaque");
});

Test("32-bit BITFIELDS DIB skips the colour masks", () =>
{
    var dib = new byte[40 + 12 + 4];
    BitConverter.GetBytes(40).CopyTo(dib, 0);
    BitConverter.GetBytes(1).CopyTo(dib, 4);
    BitConverter.GetBytes(1).CopyTo(dib, 8);
    BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);
    BitConverter.GetBytes(3).CopyTo(dib, 16);     // BI_BITFIELDS
    dib[52] = 10; dib[53] = 20; dib[54] = 30;
    var (_, _, px) = Wheel.Core.Capture.Dib.ToBgra(dib);
    Eq((10, 20, 30), (px[0], px[1], px[2]), "pixel read from the wrong offset");
});

// ---------- PDF (PDFium) ----------

// A tiny but valid PDF: one page per string, each showing that text in Helvetica.
static byte[] MakePdf(params string[] pageTexts)
{
    var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "" };
    var kids = new List<string>();
    foreach (var text in pageTexts)
    {
        var stream = $"BT /F1 24 Tf 72 700 Td ({text}) Tj ET";
        var pageNo = objects.Count + 1;
        objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {pageNo + 1} 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>");
        objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream");
        kids.Add($"{pageNo} 0 R");
    }
    objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", kids)}] /Count {pageTexts.Length} >>";

    var sb = new System.Text.StringBuilder("%PDF-1.4\n");
    var offsets = new List<int>();
    for (var i = 0; i < objects.Count; i++)
    {
        offsets.Add(sb.Length);
        sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
    }
    var xref = sb.Length;
    sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
    foreach (var o in offsets) sb.Append($"{o:0000000000} 00000 n \n");
    sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
}

if (!PdfConverter.IsAvailable())
{
    Console.WriteLine("SKIP PDF tests: PDFium library not found (run scripts/get-pdfium.sh)");
}
else
{
    var pdfDir = Dir("pdf");
    string Pdf(string name, params string[] pages)
    {
        var path = Path.Combine(pdfDir, name);
        File.WriteAllBytes(path, MakePdf(pages));
        return path;
    }
    WheelOption Opt(string id) => OptionRegistry.All.First(o => o.Id == id);

    Test("PDF options appear only with PDFium, Merge only for 2+ files", () =>
    {
        var withPdf = new Capabilities(false, HasPdfium: true);
        var one = Ids(OptionRegistry.GetOptions(WheelMode.Tools, ["a.pdf"], withPdf));
        var two = Ids(OptionRegistry.GetOptions(WheelMode.Tools, ["a.pdf", "b.pdf"], withPdf));
        True(!one.Contains("pdf.merge") && two.Contains("pdf.merge"), "merge visibility");
        Eq(0, OptionRegistry.GetOptions(WheelMode.Convert, ["a.pdf"], new Capabilities(false)).Count, "PDF options without PDFium");
    });

    Test("page numbers are added to every page, as real text", () =>
    {
        var src = Pdf("numbers.pdf", "Alpha", "Beta", "Gamma");
        var r = PdfConverter.Run(Opt("pdf.numbers"), [src]);
        True(r.Success, r.Error ?? "");
        Eq("numbers (numbered).pdf", Path.GetFileName(r.Outputs[0]), "name");
        var txt = PdfConverter.Run(Opt("pdf.to-txt"), [r.Outputs[0]]);
        var text = File.ReadAllText(txt.Outputs[0]);
        True(text.Contains("1 / 3") && text.Contains("3 / 3") && text.Contains("Beta"), "numbers and original text: " + text);
    });

    Test("PDF lock (AES-256) and unlock, inside Wheel: wrong password refused, right one works", () =>
    {
        var src = Pdf("secret.pdf", "Top secret");
        var locked = Wheel.Core.Converters.PdfTools.Protect(src, "pa ss/wörd");
        True(locked.Success, locked.Error ?? "");
        Eq("secret (protected).pdf", Path.GetFileName(locked.Outputs[0]), "name");
        True(!PdfConverter.Run(Opt("pdf.to-txt"), [locked.Outputs[0]]).Success, "a locked PDF shouldn't open without the password");
        True(!Wheel.Core.Converters.PdfTools.Protect(locked.Outputs[0], "x").Success, "locking twice is refused");
        var wrong = Wheel.Core.Converters.PdfTools.Unlock(locked.Outputs[0], "nope");
        Eq("The password is wrong.", wrong.Error, "wrong password");
        var unlocked = Wheel.Core.Converters.PdfTools.Unlock(locked.Outputs[0], "pa ss/wörd");
        True(unlocked.Success, unlocked.Error ?? "");
        var text = PdfConverter.Run(Opt("pdf.to-txt"), [unlocked.Outputs[0]]);
        True(File.ReadAllText(text.Outputs[0]).Contains("Top secret"), "readable again");
    });

    if (FfmpegConverter.Locate() is { } ffmpegForPdf)
        Test("PDF compression shrinks big pictures, keeps the text, and refuses when it can't help", () =>
        {
            // A PDF holding a big photo-like JPEG, made with PDFsharp.
            var jpg = Path.Combine(pdfDir, "photo.jpg");
            var r = ProcessRunner.RunAsync(ffmpegForPdf, ["-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=3000x2000", "-frames:v", "1", "-q:v", "1", jpg]).GetAwaiter().GetResult();
            True(r.ExitCode == 0, r.ErrorTail);
            var big = Path.Combine(pdfDir, "brochure.pdf");
            using (var doc = new PdfSharp.Pdf.PdfDocument())
            {
                var page = doc.AddPage();
                using var g = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
                using var image = PdfSharp.Drawing.XImage.FromFile(jpg);
                g.DrawImage(image, 20, 60, 550, 367);
                doc.Save(big);
            }
            var shrinkCalls = 0;
            Wheel.Core.Converters.SmallerPicture? Shrink(Wheel.Core.Converters.PdfPicture p)
            {
                shrinkCalls++;
                True(p.Jpeg is not null && p.Width == 3000 && p.Height == 2000, "the photo is handed over as JPEG");
                var inFile = Path.Combine(pdfDir, "in.jpg");
                var outFile = Path.Combine(pdfDir, "out.jpg");
                File.WriteAllBytes(inFile, p.Jpeg!);
                ProcessRunner.RunAsync(ffmpegForPdf, ["-y", "-loglevel", "error", "-i", inFile, "-vf", "scale=1500:1000", "-q:v", "8", outFile]).GetAwaiter().GetResult();
                return new(File.ReadAllBytes(outFile), 1500, 1000);
            }
            var result = Wheel.Core.Converters.PdfTools.Compress(big, Shrink);
            True(result.Success, result.Error ?? "");
            Eq(1, shrinkCalls, "one picture");
            True(new FileInfo(result.Outputs[0]).Length < new FileInfo(big).Length / 2, "at least halved");
            var pages = PdfConverter.RenderPages(result.Outputs[0], Path.Combine(pdfDir, "compressed-pages"), (path, w, h, bgra, stride) => File.WriteAllBytes(path, [1]));
            Eq(1, pages.Count, "still opens and renders");

            var textOnly = Pdf("text-only.pdf", "Just words");
            var none = Wheel.Core.Converters.PdfTools.Compress(textOnly, _ => throw new InvalidOperationException("no pictures to shrink"));
            True(!none.Success && none.Error!.Contains("already as small"), "text-only PDF: " + none.Error);
            True(!File.Exists(Path.Combine(pdfDir, "text-only (compressed).pdf")), "nothing left behind");
        });

    Test("scanned-PDF reading renders each page into a temporary folder", () =>
    {
        var src = Pdf("scan.pdf", "One", "Two", "Three");
        var folder = Path.Combine(pdfDir, "ocr-temp");
        var pages = PdfConverter.RenderPages(src, folder, (path, w, h, bgra, stride) => File.WriteAllBytes(path, [1]));
        Eq("page-001.png,page-002.png,page-003.png", string.Join(",", pages.Select(Path.GetFileName)), "page files");
        True(pages.All(File.Exists), "pages written");
        True(!File.Exists(Path.Combine(pdfDir, "scan.png")), "nothing written beside the PDF");
    });

    Test("PDF → PNG renders every page at 300 DPI into a pages folder", () =>
    {
        var src = Pdf("report.pdf", "First page", "Second page");
        var rendered = new List<(string Path, int W, int H, bool HasInk)>();
        var r = PdfConverter.Run(Opt("pdf.to-png"), [src], (path, w, h, bgra, stride) =>
        {
            // Any non-white pixel means the text was actually drawn.
            var ink = false;
            for (var i = 0; i < bgra.Length && !ink; i += 4) ink = bgra[i] < 200;
            rendered.Add((path, w, h, ink));
            File.WriteAllBytes(path, [1]);
        });
        True(r.Success, r.Error ?? "failed");
        Eq(2, rendered.Count, "pages rendered");
        Eq(Path.Combine(pdfDir, "report (pages)"), r.Outputs[0], "output folder");
        Eq(Path.Combine(pdfDir, "report (pages)", "report-002.png"), rendered[1].Path, "second page name");
        Eq((2550, 3300), (rendered[0].W, rendered[0].H), "Letter at 300 DPI");
        True(rendered.All(x => x.HasInk), "a page rendered blank");
    });

    Test("single-page PDF → JPG lands beside the PDF", () =>
    {
        var src = Pdf("flyer.pdf", "Only page");
        var r = PdfConverter.Run(Opt("pdf.to-jpg"), [src], (path, _, _, _, _) => File.WriteAllBytes(path, [1]));
        True(r.Success, r.Error ?? "failed");
        Eq(Path.Combine(pdfDir, "flyer.jpg"), r.Outputs[0]);
    });

    Test("PDF → TXT extracts each page's text", () =>
    {
        var src = Pdf("letter.pdf", "Hello Wheel", "Riyadh 2026");
        var r = PdfConverter.Run(Opt("pdf.to-txt"), [src]);
        True(r.Success, r.Error ?? "failed");
        var text = File.ReadAllText(r.Outputs[0]);
        True(text.Contains("Hello Wheel") && text.Contains("Riyadh 2026"), $"text was: {text}");
    });

    Test("merge joins PDFs in natural name order", () =>
    {
        var d = Dir("pdf/merge");
        File.WriteAllBytes(Path.Combine(d, "part10.pdf"), MakePdf("Ten"));
        File.WriteAllBytes(Path.Combine(d, "part2.pdf"), MakePdf("Two-a", "Two-b"));
        var r = PdfConverter.Run(Opt("pdf.merge"), [Path.Combine(d, "part10.pdf"), Path.Combine(d, "part2.pdf")]);
        True(r.Success, r.Error ?? "failed");
        Eq(Path.Combine(d, "Merged.pdf"), r.Outputs[0]);
        var text = PdfConverter.ToText(r.Outputs[0]);
        var txt = File.ReadAllText(text);
        True(txt.IndexOf("Two-a") < txt.IndexOf("Two-b") && txt.IndexOf("Two-b") < txt.IndexOf("Ten"), $"order wrong: {txt}");
    });

    Test("split makes one PDF per page, each readable", () =>
    {
        var src = Pdf("deck.pdf", "Alpha", "Beta", "Gamma");
        var r = PdfConverter.Run(Opt("pdf.split"), [src]);
        True(r.Success, r.Error ?? "failed");
        var files = Directory.GetFiles(r.Outputs[0], "*.pdf").OrderBy(f => f).ToArray();
        Eq(3, files.Length, "split files");
        True(File.ReadAllText(PdfConverter.ToText(files[1])).Contains("Beta"), "page 2 content");
    });

    Test("rotate turns every page and keeps the text", () =>
    {
        var src = Pdf("scan.pdf", "Sideways");
        var r = PdfConverter.Run(Opt("pdf.rotate"), [src]);
        True(r.Success, r.Error ?? "failed");
        Eq(Path.Combine(pdfDir, "scan (rotated).pdf"), r.Outputs[0]);
        // A rotated Letter page renders landscape: wider than tall.
        var size = (0, 0);
        PdfConverter.Run(Opt("pdf.to-png"), [r.Outputs[0]], (p, w, h, _, _) => { size = (w, h); File.WriteAllBytes(p, [1]); });
        True(size.Item1 > size.Item2, $"not landscape after rotating: {size}");
    });

    Test("a damaged PDF gives a clear message", () =>
    {
        var bad = Path.Combine(pdfDir, "broken.pdf");
        File.WriteAllText(bad, "not a pdf at all");
        var r = PdfConverter.Run(Opt("pdf.to-txt"), [bad]);
        True(!r.Success && r.Error!.Contains("valid PDF"), r.Error ?? "no error");
    });
}

// ---------- ffmpeg recipes (run for real when ffmpeg is installed) ----------
var ffmpeg = FfmpegConverter.Locate();
if (ffmpeg is null)
{
    Console.WriteLine("SKIP ffmpeg tests: ffmpeg not found on PATH");
}
else
{
    var media = Dir("media");
    // File names with spaces and a leading dash prove arguments can't be injected.
    var video = Path.Combine(media, "-my clip.mp4");
    var shortVideo = Path.Combine(media, "short.mp4");
    var audio = Path.Combine(media, "voice note.mp3");
    var image = Path.Combine(media, "picture.png");

    void Gen(params string[] args)
    {
        var psi = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception("could not generate fixture " + args[^1]);
    }
    var fixturesReady = false;
    void EnsureFixtures()
    {
        if (fixturesReady) return;
        Gen("-f", "lavfi", "-i", "testsrc=duration=2:size=320x240:rate=24", "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", video);
        Gen("-f", "lavfi", "-i", "testsrc=duration=0.4:size=160x120:rate=10", "-c:v", "libx264", "-pix_fmt", "yuv420p", shortVideo);
        Gen("-f", "lavfi", "-i", "sine=frequency=300:duration=2", "-c:a", "libmp3lame", audio);
        Gen("-f", "lavfi", "-i", "testsrc=size=200x150", "-frames:v", "1", image);
        fixturesReady = true;
    }

    // Every ffmpeg option in the catalogue runs against a matching fixture and must produce a non-empty file.
    foreach (var opt in OptionRegistry.All.Where(o => o.Engine == EngineKind.Ffmpeg))
    {
        var input = opt.Id.Split('.')[0] switch { "vid" => video, "aud" => audio, _ => image };
        TestAsync($"ffmpeg {opt.Id}", async () =>
        {
            EnsureFixtures();
            var r = await FfmpegConverter.RunAsync(opt, input);
            True(r.Success, r.Error ?? "failed");
            var output = r.Outputs[0];
            True(new FileInfo(output).Length > 0, "empty output");
            True(Path.GetDirectoryName(output) == media, "output not beside source");
            if (opt.TargetExtension is { } ext)
                True(output.EndsWith(ext), $"wrong extension: {output}");
        });
    }

    TestAsync("ffmpeg snapshot falls back to first frame for clips under 1 s", async () =>
    {
        EnsureFixtures();
        var opt = OptionRegistry.All.First(o => o.Id == "vid.snapshot");
        var r = await FfmpegConverter.RunAsync(opt, shortVideo);
        True(r.Success, r.Error ?? "failed");
    });

    TestAsync("ffmpeg reports a friendly error for a fake video", async () =>
    {
        var fake = Path.Combine(media, "not really.mp4");
        File.WriteAllText(fake, "this is text, not video");
        var opt = OptionRegistry.All.First(o => o.Id == "vid.to-webm");
        var r = await FfmpegConverter.RunAsync(opt, fake);
        True(!r.Success, "fake video converted");
        True(!string.IsNullOrWhiteSpace(r.Error), "no error message");
        True(!Directory.EnumerateFiles(media, "not really*.webm").Any(), "left a broken output behind");
    });
}

Test("Freehand marks keep their points and bounds; line width travels with each mark", () =>
{
    var pen = Wheel.Core.Editing.Annotation.Freehand([(10, 40), (25, 12), (60, 30)], 0xFFE5383B, 6);
    Eq(Wheel.Core.Editing.EditTool.Pen, pen.Tool, "tool");
    Eq(6, pen.Points!.Count, "flat x,y pairs");
    Eq(new Wheel.Core.Editing.PixelRect(10, 12, 50, 28), pen.Bounds, "bounds");
    Eq(6.0, pen.Size, "size");
    var state = Wheel.Core.Editing.EditState.Empty.With(pen).With(new(Wheel.Core.Editing.EditTool.Ellipse, 0, 0, 5, 5, 0xFF000000, Size: 3));
    Eq(2, state.Annotations.Count, "both kept");
    Eq(0.0, new Wheel.Core.Editing.Annotation(Wheel.Core.Editing.EditTool.Arrow, 0, 0, 1, 1, 0).Size, "old marks: automatic size");
});

// ---------- updates ----------

Test("Update feed: versions, this computer's installer, bad feeds ignored", () =>
{
    True(Wheel.Core.Updates.UpdateFeed.IsNewer("0.17.0", "0.18.0"), "newer");
    True(Wheel.Core.Updates.UpdateFeed.IsNewer("0.9.9", "v0.10.0"), "numeric, with v");
    True(!Wheel.Core.Updates.UpdateFeed.IsNewer("0.18.0", "0.18.0"), "same");
    True(!Wheel.Core.Updates.UpdateFeed.IsNewer("1.0.0", "0.99.0"), "older");
    True(Wheel.Core.Updates.UpdateFeed.IsNewer("1.2", "1.2.1-beta"), "suffix ignored");
    Eq("windows-arm64", Wheel.Core.Updates.UpdateFeed.AssetKey(true, System.Runtime.InteropServices.Architecture.Arm64), "key");
    Eq("mac-x64", Wheel.Core.Updates.UpdateFeed.AssetKey(false, System.Runtime.InteropServices.Architecture.X64), "key");
    True(Wheel.Core.Updates.UpdateFeed.Parse("not json") is null, "garbage");
    True(Wheel.Core.Updates.UpdateFeed.Parse("""{"version":"latest","assets":{}}""") is null, "bad version");
    var info = Wheel.Core.Updates.UpdateFeed.Parse("""{"version":"0.18.0","notes":"Faster","assets":{"mac-arm64":{"url":"https://x/a.dmg","sha256":"ab"}}}""");
    Eq("Faster", info!.Notes, "notes");
    Eq("https://x/a.dmg", info.Assets["mac-arm64"].Url, "url");
    Eq("/Applications/DeskWheel.app", Wheel.Core.Updates.UpdateInstall.MacAppBundle("/Applications/DeskWheel.app/Contents/MacOS/DeskWheel"), "bundle");
    True(Wheel.Core.Updates.UpdateInstall.MacAppBundle("/usr/local/bin/DeskWheel") is null, "not in a bundle");
    True(Wheel.Core.Updates.UpdateInstall.WindowsSetupArgs("l.txt").Contains("/VERYSILENT"), "silent upgrade");
});

Test("Update signature: only the right key, only the exact feed (and openssl-made signatures work)", () =>
{
    using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
    using var other = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
    var pub = key.ExportSubjectPublicKeyInfoPem();
    var feed = System.Text.Encoding.UTF8.GetBytes("""{"version":"9.0.0","assets":{}}""");
    var sig = key.SignData(feed, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence);
    True(Wheel.Core.Updates.UpdateFeed.SignatureValid(feed, sig, pub), "valid");
    var tampered = (byte[])feed.Clone(); tampered[3] ^= 1;
    True(!Wheel.Core.Updates.UpdateFeed.SignatureValid(tampered, sig, pub), "tampered feed accepted");
    True(!Wheel.Core.Updates.UpdateFeed.SignatureValid(feed, sig, other.ExportSubjectPublicKeyInfoPem()), "wrong key accepted");
    True(!Wheel.Core.Updates.UpdateFeed.SignatureValid(feed, sig, "not a key"), "junk key");
    Eq(Convert.ToBase64String(sig), Convert.ToBase64String(Wheel.Core.Updates.UpdateFeed.DecodeSignature(System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(sig) + "\n"))), "base64 form");

    // Releases are signed by openssl (scripts/make-update-feed.py): check that exact format.
    var dir = Directory.CreateTempSubdirectory("dw-sig").FullName;
    var keyFile = Path.Combine(dir, "k.pem"); var feedFile = Path.Combine(dir, "update.json");
    File.WriteAllBytes(feedFile, feed);
    try
    {
        foreach (var args in new[] { $"ecparam -name prime256v1 -genkey -noout -out \"{keyFile}\"", $"dgst -sha256 -sign \"{keyFile}\" -out \"{feedFile}.sig\" \"{feedFile}\"", $"ec -in \"{keyFile}\" -pubout -out \"{dir}/pub.pem\"" })
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("openssl", args) { RedirectStandardError = true });
            p!.WaitForExit();
            True(p.ExitCode == 0, "openssl " + args.Split(' ')[0]);
        }
        True(Wheel.Core.Updates.UpdateFeed.SignatureValid(feed, File.ReadAllBytes(feedFile + ".sig"), File.ReadAllText(Path.Combine(dir, "pub.pem"))), "openssl signature rejected");
    }
    catch (System.ComponentModel.Win32Exception) { Console.WriteLine("        (openssl not installed: interop part skipped)"); }
    finally { Directory.Delete(dir, true); }
});

TestAsync("Update check: signed and newer → installable; tampered, unsigned or older → not", async () =>
{
    using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
    var pub = key.ExportSubjectPublicKeyInfoPem();
    var sha = new string('a', 64);
    string Feed(string v, string url = "https://dl.example/DeskWheel.dmg") => "{\"version\":\"" + v + "\",\"assets\":{\"mac-arm64\":{\"url\":\"" + url + "\",\"sha256\":\"" + sha + "\"}}}";
    HttpClient Server(string feed, byte[]? sig) => new(new FakeHttp(url =>
        url.EndsWith(".sig") ? sig : System.Text.Encoding.UTF8.GetBytes(feed)));
    byte[] Sign(string f) => key.SignData(System.Text.Encoding.UTF8.GetBytes(f), System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence);

    var good = Feed("0.18.0");
    var r = await Wheel.Core.Updates.UpdateChecker.CheckAsync(Server(good, Sign(good)), "https://u/update.json", "0.17.0", pub, "mac-arm64");
    True(r.Available && r.Verified && r.CanInstall, "signed update should install");

    var evil = Feed("0.18.0", "https://evil.example/x.dmg");
    r = await Wheel.Core.Updates.UpdateChecker.CheckAsync(Server(evil, Sign(good)), "https://u/update.json", "0.17.0", pub, "mac-arm64");
    True(!r.Available && r.Problem is not null, "tampered feed must be ignored");

    r = await Wheel.Core.Updates.UpdateChecker.CheckAsync(Server(good, null), "https://u/update.json", "0.17.0", pub, "mac-arm64");
    True(!r.Available, "missing signature must be ignored");

    r = await Wheel.Core.Updates.UpdateChecker.CheckAsync(Server(good, Sign(good)), "https://u/update.json", "0.18.0", pub, "mac-arm64");
    True(!r.Available && r.Problem is null, "same version: up to date");

    r = await Wheel.Core.Updates.UpdateChecker.CheckAsync(Server(good, null), "https://u/update.json", "0.17.0", null, "mac-arm64");
    True(r.Available && !r.CanInstall, "no key built in: tell, but don't install");

    var plain = Feed("0.18.0", "http://dl.example/x.dmg");
    r = await Wheel.Core.Updates.UpdateChecker.CheckAsync(Server(plain, Sign(plain)), "https://u/update.json", "0.17.0", pub, "mac-arm64");
    True(r.Available && !r.CanInstall, "non-https download must not install");

    r = await Wheel.Core.Updates.UpdateChecker.CheckAsync(Server(good, Sign(good)), "https://u/update.json", "0.17.0", pub, "windows-x64");
    True(r.Available && !r.CanInstall, "no installer for this computer");

    var file = Path.Combine(work, "download.bin");
    File.WriteAllText(file, "hello");
    True(Wheel.Core.Updates.UpdateFeed.Sha256Matches(file, "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824"), "sha256");
    True(!Wheel.Core.Updates.UpdateFeed.Sha256Matches(file, sha), "sha mismatch");
});

Test("Mac update script: valid bash, waits, keeps the old app if the copy fails", () =>
{
    var script = Path.Combine(work, "swap.sh");
    File.WriteAllText(script, Wheel.Core.Updates.UpdateInstall.MacSwapScript.Replace("\r\n", "\n"));
    if (OperatingSystem.IsWindows()) return;
    using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("bash", $"-n \"{script}\"") { RedirectStandardError = true });
    p!.WaitForExit();
    True(p.ExitCode == 0, "bash syntax: " + p.StandardError.ReadToEnd());
    True(Wheel.Core.Updates.UpdateInstall.MacSwapScript.Contains("mv \"$backup\" \"$target\""), "restores on failure");
    True(Wheel.Core.Updates.UpdateInstall.MacSwapScript.Contains("kill -0"), "waits for the app to quit");
});

// ---------- usage statistics ----------

TestAsync("Usage statistics: only codes and numbers, never names or paths; nothing without consent", async () =>
{
    var clean = Wheel.Core.Telemetry.UsageStats.Clean(new Dictionary<string, object>
    {
        ["tool"] = "img.to-png", ["files"] = 3, ["os"] = "mac",
        ["file"] = "/Users/irfan/Secret Plans.pdf", ["name"] = "Invoice March.docx", ["text"] = "hello there",
        ["Upper"] = "x", ["clipboard"] = new string('a', 80), ["obj"] = new object(),
    });
    Eq(3, clean.Count, "kept");
    True(clean.ContainsKey("tool") && clean.ContainsKey("files") && clean.ContainsKey("os"), "codes kept");
    Eq("2-5", Wheel.Core.Telemetry.UsageStats.Bucket(3), "bucket");
    Eq("21+", Wheel.Core.Telemetry.UsageStats.Bucket(400), "bucket");

    var folder = Path.Combine(work, "usage");
    var allowed = false;
    var stats = new Wheel.Core.Telemetry.UsageStats(folder, "G-TEST123", "secret", () => allowed, new Dictionary<string, object> { ["app_version"] = "0.18.0" });
    stats.Track("tool_used", new Dictionary<string, object> { ["tool"] = "pdf.compress" });
    Eq(0, stats.Waiting(), "tracked without consent");
    allowed = true;
    stats.Track("tool_used", new Dictionary<string, object> { ["tool"] = "pdf.compress", ["path"] = "C:\\Users\\me\\file.pdf" });
    stats.Track("Bad Name!");
    Eq(1, stats.Waiting(), "queued");

    var posts = new List<(string Url, string Json)>();
    await stats.FlushAsync((url, json) => { posts.Add((url, json)); return Task.FromResult(true); });
    Eq(1, posts.Count, "one request");
    True(posts[0].Url.StartsWith("https://www.google-analytics.com/mp/collect?measurement_id=G-TEST123&api_secret=secret"), "endpoint");
    var body = System.Text.Json.JsonDocument.Parse(posts[0].Json).RootElement;
    var ev = body.GetProperty("events")[0];
    Eq("tool_used", ev.GetProperty("name").GetString(), "event");
    Eq("pdf.compress", ev.GetProperty("params").GetProperty("tool").GetString(), "tool");
    True(!posts[0].Json.Contains("Users") && !posts[0].Json.Contains("file.pdf"), "a path leaked");
    True(body.GetProperty("client_id").GetString()!.Length > 10, "client id");
    Eq(0, stats.Waiting(), "sent and removed");

    for (var i = 0; i < 60; i++) stats.Track("screenshot", new Dictionary<string, object> { ["kind"] = "area" });
    var count = 0;
    await stats.FlushAsync((_, json) => { count += System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("events").GetArrayLength(); return Task.FromResult(count < 50); });
    Eq(35, stats.Waiting(), "failed batch stays queued (first 25 sent, the rest kept)");
    allowed = false;
    await stats.FlushAsync((_, _) => throw new Exception("must not send"));
    Eq(0, stats.Waiting(), "turning off deletes the queue");
});

Console.WriteLine($"Running {tests.Count} tests in {work}");
// ---------------------------------------------------------------- JSON
Test("JSON: kinds and wheel options", () =>
{
    Eq(FileKind.Json, FileClassifier.Classify("data.json"), "kind");
    Eq(FileKind.Json, FileClassifier.Classify("settings.jsonc"), "jsonc");
    var labels = OptionRegistry.GetOptions(WheelMode.Convert, ["/x/data.json"], new Capabilities(true, false, true, true, true, false, false, false, null, null)).Select(o => o.Label).ToList();
    True(labels.Contains("View") && labels.Contains("Format") && labels.Contains("CSV"), string.Join(",", labels));
});
Test("JSON: format, minify keep order, numbers and Arabic; comments allowed", () =>
{
    var dir = Directory.CreateTempSubdirectory("dw-json").FullName;
    var f = Path.Combine(dir, "data.json");
    File.WriteAllText(f, "\uFEFF{ // settings\n \"z\": 1.50, \"a\": [1, 2,], \"name\": \"محمد\", \"html\": \"</script>\" }");
    var formatted = Wheel.Core.Json.JsonTools.Format(f);
    True(formatted.Success, formatted.Error ?? "");
    Eq("data (formatted).json", Path.GetFileName(formatted.Outputs[0]), "name");
    var text = File.ReadAllText(formatted.Outputs[0]);
    True(text.IndexOf("\"z\"") < text.IndexOf("\"a\""), "key order kept");
    True(text.Contains("1.50") && text.Contains("محمد") && text.Contains("\n  \"a\""), text);
    var min = File.ReadAllText(Wheel.Core.Json.JsonTools.Minify(f).Outputs[0]);
    Eq("{\"z\":1.50,\"a\":[1,2],\"name\":\"محمد\",\"html\":\"</script>\"}", min, "minified");
    // The viewer page can't be broken out of by the data.
    var page = Wheel.Core.Json.JsonTools.BuildPage(f);
    True(!page.Contains("</script>\"") && page.Contains("\\u003c/script>"), "escaped");
    True(page.Contains("data.json") && !page.Contains("{{"), "placeholders filled");
});
Test("JSON: mistakes are reported with line and column; the page still opens", () =>
{
    var dir = Directory.CreateTempSubdirectory("dw-json").FullName;
    var f = Path.Combine(dir, "bad.json");
    File.WriteAllText(f, "{\n  \"a\": 1\n  \"b\": 2\n}");
    var r = Wheel.Core.Json.JsonTools.Format(f);
    True(!r.Success && r.Error!.Contains("line 3"), r.Error ?? "");
    True(Wheel.Core.Json.JsonTools.BuildPage(f).Contains("line 3"), "page shows the error");
});
Test("JSON: records to CSV (union of keys, nested as JSON, formulas defused)", () =>
{
    var dir = Directory.CreateTempSubdirectory("dw-json").FullName;
    var f = Path.Combine(dir, "people.json");
    File.WriteAllText(f, "{\"data\":[{\"name\":\"Ali, K\",\"age\":30},{\"name\":\"=cmd\",\"tags\":[\"a\",\"b\"]}]}");
    var r = Wheel.Core.Json.JsonTools.ToCsv(f);
    True(r.Success, r.Error ?? "");
    var lines = File.ReadAllLines(r.Outputs[0]);
    Eq("name,age,tags", lines[0].TrimStart('\uFEFF'), "header");
    Eq("\"Ali, K\",30,", lines[1], "row 1");
    Eq("'=cmd,,\"[\"\"a\"\",\"\"b\"\"]\"", lines[2], "row 2");
});

// ---------------------------------------------------------------- picture viewer
Test("Viewer: a picture opens its folder in Explorer's order; folders and selections", () =>
{
    var dir = Directory.CreateTempSubdirectory("dw-view").FullName;
    foreach (var n in new[] { "photo 10.jpg", "photo 2.png", "Photo 1.webp", "notes.txt", ".hidden.jpg", "photo 02.jpg" })
        File.WriteAllText(Path.Combine(dir, n), "x");
    var (pics, start) = Wheel.Core.Files.ImageFolder.For([Path.Combine(dir, "photo 2.png")]);
    Eq("Photo 1.webp|photo 2.png|photo 02.jpg|photo 10.jpg", string.Join("|", pics.Select(Path.GetFileName)), "order");
    Eq(1, start, "starts at the picture");
    Eq(4, Wheel.Core.Files.ImageFolder.For([dir]).Pictures.Count, "folder");
    var two = Wheel.Core.Files.ImageFolder.For([Path.Combine(dir, "photo 10.jpg"), Path.Combine(dir, "notes.txt"), Path.Combine(dir, "Photo 1.webp")]);
    Eq(2, two.Pictures.Count, "only the chosen pictures");
    True(OptionRegistry.GetOptions(WheelMode.Tools, [Path.Combine(dir, "photo 2.png")], new Capabilities(true, false, true, true, true, false, false, false, null, null)).Any(o => o.Id == "img.view"), "View on the Tools wheel");
});

var failed = 0;
foreach (var (name, body) in tests)
{
    try
    {
        await body();
        Console.WriteLine($"  PASS  {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}\n        {ex.Message}");
    }
}

try { Directory.Delete(work, true); } catch (IOException) { }
Console.WriteLine(failed == 0 ? $"All {tests.Count} tests passed." : $"{failed} of {tests.Count} tests failed.");
return failed == 0 ? 0 : 1;

/// <summary>A pretend web server for the update tests: returns bytes per URL, or 404 for null.</summary>
sealed class FakeHttp(Func<string, byte[]?> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = respond(request.RequestUri!.ToString());
        return Task.FromResult(body is null
            ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
