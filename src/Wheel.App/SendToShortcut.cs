using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wheel.App;

/// <summary>Adds "Desk Wheel" to Explorer's right-click → Send to menu (a .lnk in the user's SendTo folder).</summary>
[SupportedOSPlatform("windows")]
internal static class SendToShortcut
{
    public static bool Install(out string message)
    {
        object? shell = null;
        object? link = null;
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            var lnkPath = Path.Combine(folder, "Desk Wheel (convert or tools).lnk");
            var type = Type.GetTypeFromProgID("WScript.Shell")
                       ?? throw new InvalidOperationException("Windows Script Host isn't available.");

            shell = Activator.CreateInstance(type);
            link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            var linkType = link!.GetType();
            linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Environment.ProcessPath! });
            linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Open Desk Wheel for these files" });
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);

            message = "Right-click files → Send to → Desk Wheel.";
            return true;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidOperationException
                                   or IOException or UnauthorizedAccessException)
        {
            message = (ex.InnerException ?? ex).Message;
            return false;
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}
