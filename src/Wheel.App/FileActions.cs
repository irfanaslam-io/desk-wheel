using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Wheel.App.Platform;
using Wheel.Core;

namespace Wheel.App;

/// <summary>The Actions wheel. OS-specific parts (clipboard, Finder/Explorer, terminal) go through the platform.</summary>
internal static class FileActions
{
    public static JobResult Run(IPlatform platform, WheelOption option, IReadOnlyList<string> paths)
    {
        try
        {
            switch (option.Id)
            {
                case "act.copy-file":
                    platform.CopyFiles(paths);
                    return JobResult.Ok();

                case "act.copy-path":
                    platform.CopyText(string.Join(Environment.NewLine, paths));
                    return JobResult.Ok();

                case "act.copy-name":
                    platform.CopyText(string.Join(Environment.NewLine, paths.Select(p => Path.GetFileName(Trim(p)))));
                    return JobResult.Ok();

                case "act.terminal":
                    platform.OpenTerminal(FolderOf(paths[0]));
                    return JobResult.Ok();

                case "act.reveal":
                    platform.Reveal(paths[0]);
                    return JobResult.Ok();

                case "act.copy-desktop":
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    return JobResult.Ok(paths.Select(p => CopyInto(p, desktop)).ToArray());

                case "act.base64":
                {
                    var info = new FileInfo(paths[0]);
                    if (info.Length > 10 * 1024 * 1024)
                        return JobResult.Fail("That picture is over 10 MB: too big to paste as text. Compress it first.");
                    platform.CopyText(Wheel.Core.Files.DataUrl.For(paths[0], File.ReadAllBytes(paths[0])));
                    return JobResult.Ok();
                }

                default:
                    return JobResult.Fail($"Unknown action {option.Id}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return JobResult.Fail(ex.Message);
        }
    }

    private static string Trim(string path) => path.TrimEnd('/', '\\');

    private static string FolderOf(string path) =>
        Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string CopyInto(string source, string targetFolder)
    {
        var target = OutputPaths.Unique(Path.Combine(targetFolder, Path.GetFileName(Trim(source))));
        if (Directory.Exists(source))
            CopyDirectory(source, target);
        else
            File.Copy(source, target);
        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
