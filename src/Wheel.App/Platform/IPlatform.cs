using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Wheel.Core;
using Wheel.Core.Capture;

namespace Wheel.App.Platform;

/// <summary>
/// Everything that differs between macOS and Windows. The rest of the app (wheel, jobs, tray, toasts) is shared.
/// </summary>
internal interface IPlatform : IDisposable
{
    /// <summary>"Finder" or "File Explorer".</summary>
    string FileManagerName { get; }

    /// <summary>Key names in wheel order (Convert, Tools, Actions), e.g. ⇧ ⌘ ⌃ on Mac.</summary>
    string[] KeyLegend { get; }

    /// <summary>Where settings and the log live.</summary>
    string DataFolder { get; }

    /// <summary>A wheel key went down (first press only). Raised on the UI thread.</summary>
    event Action<WheelMode>? WheelKeyTapped;

    /// <summary>Esc went down. Raised on the UI thread.</summary>
    event Action? EscapePressed;

    /// <summary>The "open the wheel for selected files" hotkey was pressed. Raised on the UI thread.</summary>
    event Action? HotkeyPressed;

    /// <summary>The hotkey as shown to people, e.g. "⌥ Space" or "Ctrl+Shift+Space".</summary>
    string HotkeyName { get; }

    /// <summary>
    /// Files selected in the front Finder / File Explorer window. Null with a user-facing reason when they
    /// can't be read (e.g. permission); an empty array when nothing is selected.
    /// </summary>
    Task<(string[]? Files, string? Problem)> GetSelectedFilesAsync();

    /// <summary>Makes Wheel the active app so the clickable wheel gets mouse and keyboard input.</summary>
    void BringToFront();

    /// <summary>A screenshot shortcut was pressed (⌥⇧1/2/3 on Mac, Alt+Shift+1/2/3 on Windows). Raised on the UI thread.</summary>
    event Action<CaptureKind>? CaptureHotkeyPressed;

    /// <summary>The shortcut for a capture kind, as shown in menus.</summary>
    string CaptureShortcut(CaptureKind kind);

    /// <summary>
    /// Takes a screenshot into <paramref name="outputPng"/> using the system's own capture screen.
    /// Ok=false with Problem=null means the person cancelled (Esc).
    /// </summary>
    Task<(bool Ok, string? Problem)> CaptureAsync(CaptureKind kind, string outputPng);

    /// <summary>macOS: the display under the pointer, numbered as ffmpeg's "Capture screen N" (0 = main). Windows: 0.</summary>
    int ScreenNumberUnderPointer();

    /// <summary>macOS: keep the shadow around window screenshots. Ignored on Windows.</summary>
    bool CaptureWindowShadow { get; set; }

    /// <summary>Puts an image file on the clipboard as an image (pasteable into chat, mail, documents).</summary>
    Task CopyImageAsync(string pngPath);

    /// <summary>Opens a folder in Finder / File Explorer.</summary>
    void OpenFolder(string folder);

    /// <summary>Opens a file with its usual app (Preview, Photos, Word…).</summary>
    void OpenFile(string path);

    /// <summary>Opens a local HTML page in the default web browser (not whatever app edits .html files).</summary>
    void OpenInBrowser(string htmlPath);

    /// <summary>Registers the hotkey (needs no permission). False with a reason if another app already uses it.</summary>
    bool RegisterHotkey(out string? problem);

    /// <summary>Starts watching modifier keys for the optional drag gesture. False with a reason (e.g. a permission is missing).</summary>
    bool StartListening(out string? problem);

    /// <summary>True while the primary mouse button is physically held (i.e. mid-drag).</summary>
    bool IsPrimaryButtonDown();

    /// <summary>Debug detail: each mouse-state source, so --debug output shows which one sees the drag.</summary>
    string DescribeInput();

    /// <summary>The pointer, in the same pixel coordinates as Avalonia's Window.Position.</summary>
    PixelPoint CursorPixel(Screens screens);

    /// <summary>The frontmost app is Finder / File Explorer (including the desktop).</summary>
    bool ForegroundIsFileManager();

    /// <summary>Which wheel a key opens in click mode (Send to / keyboard).</summary>
    WheelMode? ModeForKey(Key key);

    void CopyText(string text);

    /// <summary>Puts files on the clipboard as Finder's / Explorer's Copy does, ready to paste anywhere.</summary>
    void CopyFiles(System.Collections.Generic.IReadOnlyList<string> paths);

    /// <summary>Goes up whenever anything is copied (cheap: polled for clipboard history).</summary>
    long ClipboardChangeCount();

    /// <summary>
    /// What the clipboard holds now. Null when it's empty, unreadable, or marked as private by the app that
    /// copied it (password managers): such content is never recorded.
    /// </summary>
    Wheel.Core.Clipboard.ClipboardContent? ReadClipboard();

    /// <summary>The clipboard-history shortcut was pressed (⌥⇧V on Mac, Alt+Shift+V on Windows).</summary>
    event Action? ClipboardHotkeyPressed;
    void Reveal(string path);
    void OpenTerminal(string folder);

    bool StartAtLogin { get; set; }

    static IPlatform Create() =>
        OperatingSystem.IsWindows() ? new WindowsPlatform()
        : OperatingSystem.IsMacOS() ? new MacPlatform()
        : throw new PlatformNotSupportedException("Desk Wheel runs on macOS and Windows.");
}
