using System.Runtime.Versioning;
using System.Text;

namespace Wheel.Core.Converters;

/// <summary>The three Microsoft Office apps Wheel can drive.</summary>
public enum OfficeApp { Word, Excel, PowerPoint }

/// <summary>
/// Converts Office files with the Microsoft Office already installed on the computer, so most people need
/// nothing extra. Office does the conversion itself, so the PDF looks exactly as it does in Word, Excel or
/// PowerPoint.
///
/// Windows: Office's automation interface, driven from PowerShell (part of Windows). Word and Excel run in
/// a separate, invisible copy; PowerPoint (which only runs once) is left open when it was already open.
/// Mac: AppleScript through osascript. Office for Mac is sandboxed and would ask for permission to open a
/// file elsewhere, so the file is copied into Office's own folder, converted there, and the result moved out.
///
/// The document is always opened read-only and closed without saving: the original is never changed.
/// </summary>
public static class MicrosoftOffice
{
    /// <summary>How long one conversion may take before it's given up (a stuck dialog, a huge file).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(4);

    /// <summary>Which app handles an option: "doc." Word, "xls." Excel, "ppt." PowerPoint.</summary>
    public static OfficeApp? AppFor(string optionId) => optionId.Split('.')[0] switch
    {
        "doc" => OfficeApp.Word,
        "xls" => OfficeApp.Excel,
        "ppt" => OfficeApp.PowerPoint,
        _ => null,
    };

    public static string Name(OfficeApp app) => app switch
    {
        OfficeApp.Word => "Microsoft Word",
        OfficeApp.Excel => "Microsoft Excel",
        _ => "Microsoft PowerPoint",
    };

    /// <summary>
    /// Office's own "save as" format number on Windows (WdSaveFormat, XlFileFormat, PpSaveAsFileType), or null
    /// when Wheel doesn't use Office for that output. Plain text is left to LibreOffice/textutil, because Word
    /// would write it in the old Windows code page instead of UTF-8.
    /// </summary>
    public static int? WindowsFormat(OfficeApp app, string targetExtension) => (app, targetExtension.ToLowerInvariant()) switch
    {
        (OfficeApp.Word, ".pdf") => 17,        // wdFormatPDF
        (OfficeApp.Word, ".docx") => 16,       // wdFormatXMLDocument
        (OfficeApp.Word, ".odt") => 23,        // wdFormatOpenDocumentText
        (OfficeApp.Word, ".html") => 10,       // wdFormatFilteredHTML (clean HTML, no Office markup)
        (OfficeApp.Excel, ".pdf") => 0,        // xlTypePDF (ExportAsFixedFormat: the whole workbook)
        (OfficeApp.Excel, ".xlsx") => 51,      // xlOpenXMLWorkbook
        (OfficeApp.Excel, ".csv") => 62,       // xlCSVUTF8 (first sheet)
        (OfficeApp.Excel, ".ods") => 60,       // xlOpenDocumentSpreadsheet
        (OfficeApp.PowerPoint, ".pdf") => 32,  // ppSaveAsPDF
        (OfficeApp.PowerPoint, ".pptx") => 24, // ppSaveAsOpenXMLPresentation
        (OfficeApp.PowerPoint, ".odp") => 35,  // ppSaveAsOpenDocumentPresentation
        _ => null,
    };

    /// <summary>On a Mac, Office is used for PDF (Office's AppleScript is reliable for that, less so for the rest).</summary>
    public static bool MacCanWrite(string targetExtension) => targetExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the installed Office (<paramref name="apps"/>) can produce this option's output.</summary>
    public static bool CanDo(string optionId, string? targetExtension, IReadOnlySet<OfficeApp>? apps, bool windows)
    {
        if (apps is null || targetExtension is null || AppFor(optionId) is not { } app || !apps.Contains(app)) return false;
        return windows ? WindowsFormat(app, targetExtension) is not null : MacCanWrite(targetExtension);
    }

    // ── Finding Office ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The Office apps installed on this computer (empty when none).</summary>
    public static IReadOnlySet<OfficeApp> Installed()
    {
        var found = new HashSet<OfficeApp>();
        foreach (var app in Enum.GetValues<OfficeApp>())
        {
            if (OperatingSystem.IsWindows() ? WindowsHas(app) : OperatingSystem.IsMacOS() && MacApp(app) is not null)
                found.Add(app);
        }
        return found;
    }

    [SupportedOSPlatform("windows")]
    private static bool WindowsHas(OfficeApp app)
    {
        try
        {
            // Every desktop Office (MSI or Click-to-Run, 32 or 64-bit) registers "Word.Application" and so on.
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ProgId(app) + @"\CLSID");
            return key?.GetValue(null) is string clsid && clsid.Length > 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static string ProgId(OfficeApp app) => app switch
    {
        OfficeApp.Word => "Word.Application",
        OfficeApp.Excel => "Excel.Application",
        _ => "PowerPoint.Application",
    };

    public static string MacBundleId(OfficeApp app) => app switch
    {
        OfficeApp.Word => "com.microsoft.Word",
        OfficeApp.Excel => "com.microsoft.Excel",
        _ => "com.microsoft.Powerpoint",
    };

    /// <summary>The app's location on a Mac, or null.</summary>
    public static string? MacApp(OfficeApp app)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var bundle = Name(app) + ".app";
        return new[] { Path.Combine("/Applications", bundle), Path.Combine(home, "Applications", bundle) }.FirstOrDefault(Directory.Exists);
    }

    // ── Windows: PowerShell scripts ─────────────────────────────────────────────────────────────────

    /// <summary>A PowerShell string literal: single quotes, with any quote inside doubled. Nothing in it is expanded.</summary>
    public static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>
    /// The PowerShell script that converts <paramref name="input"/> to <paramref name="output"/>. A made-up
    /// password is passed when opening, so a password-protected file fails at once instead of waiting for
    /// someone to type a password into a hidden window.
    /// </summary>
    public static string WindowsScript(OfficeApp app, string input, string output, int format)
    {
        var s = new StringBuilder();
        s.AppendLine("$ErrorActionPreference = 'Stop'");
        s.AppendLine($"$in = {PsQuote(input)}");
        s.AppendLine($"$out = {PsQuote(output)}");
        s.AppendLine("$app = $null; $doc = $null");
        s.AppendLine("try {");
        switch (app)
        {
            case OfficeApp.Word:
                s.AppendLine("  $app = New-Object -ComObject Word.Application");
                s.AppendLine("  $app.Visible = $false; $app.DisplayAlerts = 0");
                s.AppendLine("  $app.AutomationSecurity = 3  # msoAutomationSecurityForceDisable: never run macros");
                // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, PasswordDocument)
                s.AppendLine("  $doc = $app.Documents.Open($in, $false, $true, $false, 'wheel-no-password')");
                s.AppendLine($"  $doc.SaveAs2($out, {format})");
                s.AppendLine("  $doc.Close(0); $doc = $null");
                s.AppendLine("  if ($app.Documents.Count -eq 0) { $app.Quit() }");
                break;
            case OfficeApp.Excel:
                s.AppendLine("  $app = New-Object -ComObject Excel.Application");
                s.AppendLine("  $app.Visible = $false; $app.DisplayAlerts = $false; $app.ScreenUpdating = $false");
                s.AppendLine("  $app.AutomationSecurity = 3");
                // Open(FileName, UpdateLinks, ReadOnly, Format, Password)
                s.AppendLine("  $doc = $app.Workbooks.Open($in, 0, $true, [Type]::Missing, 'wheel-no-password')");
                if (format == 0)
                    s.AppendLine("  $doc.ExportAsFixedFormat(0, $out)");
                else
                {
                    s.AppendLine("  $doc.Worksheets.Item(1).Activate()");
                    s.AppendLine($"  $doc.SaveAs($out, {format})");
                }
                s.AppendLine("  $doc.Close($false); $doc = $null");
                s.AppendLine("  if ($app.Workbooks.Count -eq 0) { $app.Quit() }");
                break;
            default:
                s.AppendLine("  $app = New-Object -ComObject PowerPoint.Application");
                s.AppendLine("  $app.DisplayAlerts = 1  # ppAlertsNone");
                s.AppendLine("  $app.AutomationSecurity = 3");
                // Open(FileName, ReadOnly, Untitled, WithWindow): read-only, no window.
                s.AppendLine("  $doc = $app.Presentations.Open($in, -1, 0, 0)");
                s.AppendLine($"  $doc.SaveAs($out, {format})");
                s.AppendLine("  $doc.Close(); $doc = $null");
                s.AppendLine("  if ($app.Presentations.Count -eq 0) { $app.Quit() }");
                break;
        }
        s.AppendLine("} catch {");
        s.AppendLine("  [Console]::Error.WriteLine($_.Exception.Message)");
        s.AppendLine("  try { if ($doc) { $doc.Close(0) } } catch {}");
        s.AppendLine("  exit 1");
        s.AppendLine("} finally {");
        s.AppendLine("  if ($doc) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($doc) }");
        s.AppendLine("  if ($app) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($app) }");
        s.AppendLine("  [GC]::Collect(); [GC]::WaitForPendingFinalizers()");
        s.AppendLine("}");
        return s.ToString();
    }

    /// <summary>powershell.exe arguments; the script goes in encoded, so paths and quotes can't break the command line.</summary>
    public static IReadOnlyList<string> PowerShellArgs(string script) =>
    [
        "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
        "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
    ];

    // ── Mac: AppleScript ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The AppleScript that saves argv[1] as a PDF at argv[2] (paths are passed as arguments, never pasted
    /// into the script). Quits the app afterwards only when Wheel was the one that opened it.
    /// </summary>
    public static string MacScript(OfficeApp app)
    {
        var id = MacBundleId(app);
        var save = app switch
        {
            OfficeApp.Word =>
                """
                    set d to active document
                    save as d file name outHfs file format format PDF
                    close d saving no
                """,
            OfficeApp.Excel =>
                """
                    set d to active workbook
                    save workbook as d filename outHfs file format PDF file format
                    close d saving no
                """,
            _ =>
                """
                    set d to active presentation
                    save d in outHfs as save as PDF
                    close d
                """,
        };
        var count = app switch
        {
            OfficeApp.Word => "count of documents",
            OfficeApp.Excel => "count of workbooks",
            _ => "count of presentations",
        };
        return $$"""
            on run argv
                set inPath to item 1 of argv
                set outHfs to (POSIX file (item 2 of argv)) as string
                set wasRunning to application id "{{id}}" is running
                with timeout of 240 seconds
                    tell application id "{{id}}"
                        open (POSIX file inPath as alias)
            {{save}}
                        if (not wasRunning) and ({{count}}) is 0 then quit
                    end tell
                end timeout
            end run
            """;
    }

    /// <summary>The folder Office for Mac may freely read and write (its sandbox), or null when it hasn't been set up yet.</summary>
    public static string? MacSandboxFolder(OfficeApp app)
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Containers", MacBundleId(app), "Data");
        return Directory.Exists(data) ? data : null;
    }

    // ── Running ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Converts with Office into <paramref name="output"/> (a fresh name that doesn't exist yet).</summary>
    public static async Task<JobResult> RunAsync(OfficeApp app, string input, string targetExtension, string output, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            return OperatingSystem.IsWindows()
                ? await OnWindowsAsync(app, input, targetExtension, output, timeout.Token)
                : await OnMacAsync(app, input, output, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return JobResult.Fail($"{Name(app)} took too long with {Path.GetFileName(input)}. Is it showing a message?");
        }
    }

    private static async Task<JobResult> OnWindowsAsync(OfficeApp app, string input, string target, string output, CancellationToken ct)
    {
        var format = WindowsFormat(app, target) ?? throw new ArgumentException($"{Name(app)} isn't used for {target}");
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) powershell = "powershell.exe";
        var r = await ProcessRunner.RunAsync(powershell, PowerShellArgs(WindowsScript(app, Path.GetFullPath(input), Path.GetFullPath(output), format)), ct);
        return r.ExitCode == 0 && File.Exists(output)
            ? JobResult.Ok(output)
            : JobResult.Fail(Explain(app, input, r.ErrorTail));
    }

    private static async Task<JobResult> OnMacAsync(OfficeApp app, string input, string output, CancellationToken ct)
    {
        // Work inside Office's own folder, so it never has to ask for permission to open or save.
        var root = MacSandboxFolder(app) ?? Path.GetTempPath();
        var work = Path.Combine(root, "wheel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var copy = Path.Combine(work, Path.GetFileName(input));
            File.Copy(input, copy);
            var pdf = Path.Combine(work, Path.GetFileNameWithoutExtension(input) + ".pdf");
            var r = await ProcessRunner.RunAsync("/usr/bin/osascript", ["-e", MacScript(app), copy, pdf], ct);
            if (!File.Exists(pdf))
                return JobResult.Fail(Explain(app, input, r.ErrorTail));
            File.Move(pdf, output); // output is a fresh, unused name: nothing is overwritten
            return JobResult.Ok(output);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>A plain-language reason from Office's error text.</summary>
    public static string Explain(OfficeApp app, string input, string error)
    {
        var name = Path.GetFileName(input);
        var e = error.Trim();
        if (e.Contains("password", StringComparison.OrdinalIgnoreCase))
            return $"{name} has a password. Open it in {Name(app)}, remove the password, and try again.";
        if (e.Contains("-1743") || e.Contains("Not authorized to send Apple events", StringComparison.OrdinalIgnoreCase))
            return $"Desk Wheel isn't allowed to use {Name(app)}. Turn it on in System Settings › Privacy & Security › Automation › Desk Wheel.";
        if (e.Contains("80080005") || e.Contains("80040154"))
            return $"{Name(app)} couldn't be started. Open it once yourself (to finish any setup or sign-in), then try again.";
        return $"{Name(app)} couldn't convert {name}{(e.Length > 0 ? ": " + e.Split('\n').Last(l => l.Trim().Length > 0).Trim() : ".")}";
    }
}
