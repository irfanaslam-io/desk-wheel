using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;

namespace Wheel.App;

/// <summary>
/// Entry point, macOS and Windows.
/// <list type="bullet">
/// <item><c>Wheel</c> with no files: lives in the menu bar / tray and listens for the drag gesture (one instance per user).</item>
/// <item><c>Wheel file1 file2…</c> (Windows "Send to", or a terminal): a clickable wheel for those files, then exits.</item>
/// <item><c>Wheel --view picture.jpg</c> (File Explorer's "Open with"): the picture viewer, then exits.</item>
/// <item><c>Wheel --self-test [folder]</c>: checks the shipped engines on generated files (see <see cref="SelfTest"/>).</item>
/// </list>
/// </summary>
internal static class Program
{
    /// <summary>Files passed on the command line; empty for the normal background app.</summary>
    public static string[] Files { get; private set; } = [];

    /// <summary>--view: the picture (or folder) to open in the viewer; null otherwise.</summary>
    public static string? View { get; private set; }

    /// <summary>--debug: print each step (startup, keys, decisions) to the terminal and the log.</summary>
    public static bool Debug { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // DeskWheel --self-test [folder]: check this copy's engines on generated files, then exit (used by the build).
        if (args.Length > 0 && args[0] == "--self-test")
            return SelfTest.Run(args.Length > 1 ? args[1] : null);

        Debug = args.Contains("--debug");
        RenameMigration.Run(); // "Wheel" → "Desk Wheel": carry over settings and login item, once

        // Record crashes from any thread, not just the UI thread.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Write(ex);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write(e.Exception);
            e.SetObserved();
        };
        Files = args.Where(a => File.Exists(a) || Directory.Exists(a)).Select(Path.GetFullPath).ToArray();
        var viewAt = Array.IndexOf(args, "--view");
        if (viewAt >= 0 && viewAt + 1 < args.Length && (File.Exists(args[viewAt + 1]) || Directory.Exists(args[viewAt + 1])))
            View = Path.GetFullPath(args[viewAt + 1]);
        var version = typeof(Program).Assembly.GetName().Version;
        Log.Startup($"Desk Wheel {version} starting{(Debug ? " (debug)" : "")}. OS: {Environment.OSVersion}, .NET {Environment.Version}");
        Log.Trace($"Desk Wheel {version} starting. Files given: {Files.Length}");

        Mutex? single = null;
        if (Files.Length == 0)
        {
            single = new Mutex(initiallyOwned: true, "Wheel.Background.v1", out var isFirst);
            if (!isFirst)
            {
                Log.Trace("Another Desk Wheel is already running (check the menu bar / tray). Quit it first. Exiting.");
                return 0;
            }
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Log.Write(ex);
            throw;
        }
        finally
        {
            single?.Dispose();
        }
    }

    // Also used by the Avalonia previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false }) // menu-bar app: no Dock icon
            .LogToTrace();
}
