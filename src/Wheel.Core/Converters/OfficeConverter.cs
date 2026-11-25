namespace Wheel.Core.Converters;

/// <summary>
/// Word, Excel and PowerPoint files (and their OpenDocument equivalents).
/// Uses Microsoft Office when it's installed and can write the format (the PDF then looks exactly as it does
/// in Office), otherwise LibreOffice (free), otherwise macOS's built-in textutil for word-processor text formats. LibreOffice runs headless with its own private
/// profile, so it works even while LibreOffice is open, and writes into a temporary folder first so
/// nothing is ever overwritten.
/// </summary>
public static class OfficeConverter
{
    public const string InstallHint = "Office conversions use Microsoft Office, or LibreOffice (free) from libreoffice.org";

    public static bool HasTextutil => OperatingSystem.IsMacOS() && File.Exists("/usr/bin/textutil");

    public static string? LocateLibreOffice()
    {
        IEnumerable<string> candidates;
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates =
            [
                "/Applications/LibreOffice.app/Contents/MacOS/soffice",
                Path.Combine(home, "Applications", "LibreOffice.app", "Contents", "MacOS", "soffice"),
            ];
        }
        else if (OperatingSystem.IsWindows())
        {
            candidates = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .Select(f => Environment.GetFolderPath(f))
                .Where(f => f.Length > 0)
                .Select(f => Path.Combine(f, "LibreOffice", "program", "soffice.exe"));
        }
        else
        {
            candidates = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(d => new[] { Path.Combine(d, "soffice"), Path.Combine(d, "libreoffice") });
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>LibreOffice's --convert-to value for a target extension.</summary>
    public static string LibreOfficeFilter(string targetExtension) => targetExtension.ToLowerInvariant() switch
    {
        ".txt" => "txt:Text (encoded):UTF8",
        ".csv" => "csv:Text - txt - csv (StarCalc):44,34,76", // comma, double quotes, UTF-8
        ".html" => "html",
        var e => e.TrimStart('.'),
    };

    public static IReadOnlyList<string> LibreOfficeArgs(string input, string targetExtension, string outputFolder, string profileFolder) =>
    [
        "-env:UserInstallation=" + new Uri(Path.GetFullPath(profileFolder) + Path.DirectorySeparatorChar).AbsoluteUri,
        "--headless", "--norestore", "--nolockcheck", "--nologo", "--nodefault",
        "--convert-to", LibreOfficeFilter(targetExtension),
        "--outdir", outputFolder,
        input,
    ];

    /// <summary>textutil's -convert format for a target extension, or null when textutil can't write it.</summary>
    public static string? TextutilFormat(string targetExtension) => targetExtension.ToLowerInvariant() switch
    {
        ".txt" => "txt",
        ".html" => "html",
        ".docx" => "docx",
        ".odt" => "odt",
        ".rtf" => "rtf",
        ".doc" => "doc",
        _ => null,
    };

    public static async Task<JobResult> RunAsync(WheelOption option, string input, CancellationToken ct = default)
    {
        var target = option.TargetExtension ?? throw new ArgumentException($"{option.Id} has no target format");
        var output = OutputPaths.Beside(input, target);

        JobResult? officeFailed = null;
        if (MicrosoftOffice.AppFor(option.Id) is { } app && MicrosoftOffice.CanDo(option.Id, target, MicrosoftOffice.Installed(), OperatingSystem.IsWindows()))
        {
            var r = await MicrosoftOffice.RunAsync(app, input, target, output, ct);
            if (r.Success) return r;
            officeFailed = r; // e.g. Office isn't activated: LibreOffice may still manage it
        }

        if (LocateLibreOffice() is { } soffice)
            return await WithLibreOfficeAsync(soffice, input, target, output, ct);
        if (officeFailed is not null) return officeFailed;

        if (HasTextutil && TextutilFormat(target) is { } format && FileClassifier.Classify(input) == FileKind.Document)
        {
            var r = await ProcessRunner.RunAsync("/usr/bin/textutil", ["-convert", format, "-output", output, input], ct);
            return r.ExitCode == 0 && File.Exists(output)
                ? JobResult.Ok(output)
                : JobResult.Fail($"{Path.GetFileName(input)}: textutil couldn't convert it. {r.ErrorTail.Trim()}".Trim());
        }

        return JobResult.Fail(InstallHint);
    }

    private static async Task<JobResult> WithLibreOfficeAsync(string soffice, string input, string target, string output, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "wheel-office-" + Guid.NewGuid().ToString("N"));
        var outFolder = Path.Combine(work, "out");
        Directory.CreateDirectory(outFolder);
        try
        {
            var r = await ProcessRunner.RunAsync(soffice, LibreOfficeArgs(input, target, outFolder, Path.Combine(work, "profile")), ct);
            var produced = Directory.EnumerateFiles(outFolder).FirstOrDefault();
            if (produced is null)
            {
                var why = r.ErrorTail.Trim();
                return JobResult.Fail($"{Path.GetFileName(input)}: LibreOffice couldn't convert it{(why.Length > 0 ? ": " + why : ".")}");
            }
            File.Move(produced, output); // output is a fresh, unused name: nothing is overwritten
            return JobResult.Ok(output);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
