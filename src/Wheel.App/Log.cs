using System;
using System.IO;

namespace Wheel.App;

/// <summary>Error log beside the settings. Records exceptions only, never file contents.</summary>
internal static class Log
{
    private static readonly string FilePath = Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "DeskWheel")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskWheel"),
        "log.txt");

    /// <summary>Step-by-step tracing, only with --debug: printed to the terminal and appended to the log.</summary>
    public static void Trace(string message)
    {
        if (!Program.Debug) return;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        Append(line);
    }

    /// <summary>Always recorded (even without --debug): which build started, and when.</summary>
    public static void Startup(string message) => Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");

    public static void Write(Exception ex)
    {
        if (Program.Debug) Console.WriteLine(ex);
        Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}");
    }

    private static void Append(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, text + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
