using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Wheel.Core;
using Wheel.Core.Capture;

namespace Wheel.App.Platform;

/// <summary>
/// macOS: a listen-only CoreGraphics event tap watches modifier keys system-wide
/// (needs Privacy &amp; Security → Input Monitoring). Shift = Convert, ⌘ = Tools, ⌃ = Actions.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class MacPlatform : IPlatform
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    // Carbon hotkey: works without any privacy permission and doesn't leak the keypress to other apps.
    private const uint KeyboardEventClass = 0x6B657962; // 'keyb'
    private const uint HotKeyPressedKind = 5;
    private const uint SpaceKeyCode = 49;
    private const uint OptionKeyMask = 0x0800, ControlKeyMask = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID { public uint Signature; public uint Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec { public uint EventClass; public uint EventKind; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CarbonEventHandler(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID id, IntPtr target, uint options, out IntPtr hotKeyRef);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);
    [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target, CarbonEventHandler handler, uint numTypes, EventTypeSpec[] list, IntPtr userData, out IntPtr handlerRef);

    private readonly CarbonEventHandler _hotkeyHandler; // kept alive for native code
    private readonly List<IntPtr> _hotKeyRefs = new();
    private const uint ShiftKeyMask = 0x0200;
    private const uint Key1 = 18, Key2 = 19, Key3 = 20; // kVK_ANSI_1..3
    private const uint KeyV = 9; // kVK_ANSI_V
    private const uint ParamDirectObject = 0x646F626A;  // 'dobj'
    private const uint TypeEventHotKeyID = 0x686B6964;  // 'hkid'

    [DllImport(Carbon)]
    private static extern int GetEventParameter(IntPtr inEvent, uint name, uint desiredType, IntPtr actualType,
                                                 nuint bufferSize, IntPtr actualSize, out EventHotKeyID data);
    private string _hotkeyName = "⌥ Space";

    // CGEventTap constants
    private const uint SessionEventTap = 1, HeadInsertEventTap = 0, ListenOnly = 1;
    private const uint KeyDown = 10, FlagsChanged = 12;
    private const uint LeftMouseDown = 1, LeftMouseUp = 2, LeftMouseDragged = 6;
    private const int CombinedSessionState = 0;
    private const uint TapDisabledByTimeout = 0xFFFFFFFE, TapDisabledByUserInput = 0xFFFFFFFF;
    private const uint KeyboardEventKeycode = 9;
    private const long EscapeKeyCode = 53;
    private const int HidSystemState = 1;
    private const uint LeftMouseButton = 0;

    private const ulong FlagShift = 0x00020000, FlagControl = 0x00040000, FlagCommand = 0x00100000;
    private const ulong WatchedFlags = FlagShift | FlagControl | FlagCommand;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X, Y; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr TapCallback(IntPtr proxy, uint type, IntPtr cgEvent, IntPtr userInfo);

    [DllImport(CoreGraphics)] private static extern IntPtr CGEventTapCreate(uint tap, uint place, uint options, ulong eventsOfInterest, TapCallback callback, IntPtr userInfo);
    [DllImport(CoreGraphics)] private static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);
    [DllImport(CoreGraphics)] private static extern ulong CGEventGetFlags(IntPtr cgEvent);
    [DllImport(CoreGraphics)] private static extern long CGEventGetIntegerValueField(IntPtr cgEvent, uint field);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGEventSourceButtonState(int stateId, uint button);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr cgEvent);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightListenEventAccess();
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRequestListenEventAccess();

    [DllImport(CoreFoundation)] private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, nint order);
    [DllImport(CoreFoundation)] private static extern IntPtr CFRunLoopGetCurrent();
    [DllImport(CoreFoundation)] private static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);
    [DllImport(CoreFoundation)] private static extern void CFRunLoopRun();
    [DllImport(CoreFoundation)] private static extern void CFRunLoopStop(IntPtr runLoop);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr obj);

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nuint SendUInt(IntPtr receiver, IntPtr selector);

    private readonly TapCallback _callback; // kept in a field so the GC can't collect it while native code holds it
    private IntPtr _tap;
    private IntPtr _runLoop;
    private ulong _lastFlags;
    private volatile bool _mouseDownSeen;   // from the event tap's own mouse events
    private volatile bool _draggingSeen;

    public event Action<WheelMode>? WheelKeyTapped;
    public event Action? EscapePressed;
    public event Action? HotkeyPressed;

    public string HotkeyName => _hotkeyName;

    public MacPlatform()
    {
        _callback = OnEvent;
        _hotkeyHandler = OnHotkey;
    }

    /// <summary>⌥ Space, or ⌃⌥ Space if another app (Alfred, Raycast…) already owns ⌥ Space.</summary>
    public bool RegisterHotkey(out string? problem)
    {
        var types = new[] { new EventTypeSpec { EventClass = KeyboardEventClass, EventKind = HotKeyPressedKind } };
        var installed = InstallEventHandler(GetApplicationEventTarget(), _hotkeyHandler, 1, types, IntPtr.Zero, out _);
        if (installed != 0)
        {
            problem = $"Couldn't set up the keyboard shortcut (error {installed}).";
            return false;
        }

        // Screenshot shortcuts ⌥⇧1 / ⌥⇧2 / ⌥⇧3 (ids 2-4). A taken one is skipped; the menu still works.
        foreach (var (key, id) in new[] { (Key1, 2u), (Key2, 3u), (Key3, 4u), (KeyV, 5u) })
            if (RegisterEventHotKey(key, OptionKeyMask | ShiftKeyMask, Id(id), GetApplicationEventTarget(), 0, out var r) == 0)
                _hotKeyRefs.Add(r);

        foreach (var (mods, name) in new[] { (OptionKeyMask, "⌥ Space"), (OptionKeyMask | ControlKeyMask, "⌃⌥ Space") })
        {
            if (RegisterEventHotKey(SpaceKeyCode, mods, Id(1), GetApplicationEventTarget(), 0, out var r) == 0)
            {
                _hotKeyRefs.Add(r);
                _hotkeyName = name;
                problem = null;
                return true;
            }
        }
        problem = "⌥ Space and ⌃⌥ Space are both taken by other apps.";
        return false;

        static EventHotKeyID Id(uint n) => new() { Signature = 0x5768656C /* 'Whel' */, Id = n };
    }

    public event Action<CaptureKind>? CaptureHotkeyPressed;
    public event Action? ClipboardHotkeyPressed;

    public bool CaptureWindowShadow { get; set; } = true;

    public string CaptureShortcut(CaptureKind kind) => kind switch
    {
        CaptureKind.Area => "⌥⇧1",
        CaptureKind.Window => "⌥⇧2",
        _ => "⌥⇧3",
    };

    private int OnHotkey(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        try
        {
            var id = GetEventParameter(theEvent, ParamDirectObject, TypeEventHotKeyID, IntPtr.Zero,
                                       (nuint)Marshal.SizeOf<EventHotKeyID>(), IntPtr.Zero, out var hk) == 0 ? hk.Id : 1;
            Dispatcher.UIThread.Post(() =>
            {
                switch (id)
                {
                    case 2: CaptureHotkeyPressed?.Invoke(CaptureKind.Area); break;
                    case 3: CaptureHotkeyPressed?.Invoke(CaptureKind.Window); break;
                    case 4: CaptureHotkeyPressed?.Invoke(CaptureKind.FullScreen); break;
                    case 5: ClipboardHotkeyPressed?.Invoke(); break;
                    default: HotkeyPressed?.Invoke(); break;
                }
            });
        }
        catch (Exception ex) { Log.Write(ex); }
        return 0; // noErr: handled
    }

    /// <summary>Asks Finder for its selection (first use shows "Desk Wheel wants to control Finder").</summary>
    public async Task<(string[]? Files, string? Problem)> GetSelectedFilesAsync()
    {
        string[] script =
        [
            "-e", "tell application \"Finder\"",
            "-e", "set out to \"\"",
            "-e", "repeat with i in (get selection)",
            "-e", "set out to out & POSIX path of (i as alias) & linefeed",
            "-e", "end repeat",
            "-e", "return out",
            "-e", "end tell",
        ];
        var r = await ProcessRunner.RunAsync("/usr/bin/osascript", script);
        if (r.ExitCode != 0)
        {
            Log.Trace($"Finder selection failed: {r.ErrorTail.Trim()}");
            return r.ErrorTail.Contains("-1743") || r.ErrorTail.Contains("Not authorized", StringComparison.OrdinalIgnoreCase)
                ? (null, "Allow Desk Wheel to control Finder: System Settings → Privacy & Security → Automation → Desk Wheel → Finder.")
                : (null, "Couldn't read the Finder selection. " + r.ErrorTail.Trim());
        }

        Log.Trace($"  Finder's answer: \"{r.Output.Trim().Replace("\n", " | ")}\"");
        var files = r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Length > 1 ? p.TrimEnd('/') : p)
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .ToArray();
        return (files, null);
    }

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    public void BringToFront()
    {
        try
        {
            var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            SendBool(app, sel_registerName("activateIgnoringOtherApps:"), true);
        }
        catch (Exception ex) { Log.Write(ex); }
    }

    public string FileManagerName => "Finder";
    public string[] KeyLegend => ["⇧", "⌘", "⌃"];

    public string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "DeskWheel");

    public bool StartListening(out string? problem)
    {
        if (!CGPreflightListenEventAccess())
        {
            CGRequestListenEventAccess(); // shows the system prompt once
            problem = "Allow Desk Wheel in System Settings → Privacy & Security → Input Monitoring, then reopen Desk Wheel.";
            return false;
        }

        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            const ulong mask = (1UL << (int)FlagsChanged) | (1UL << (int)KeyDown)
                             | (1UL << (int)LeftMouseDown) | (1UL << (int)LeftMouseUp) | (1UL << (int)LeftMouseDragged);
            _tap = CGEventTapCreate(SessionEventTap, HeadInsertEventTap, ListenOnly, mask, _callback, IntPtr.Zero);
            if (_tap == IntPtr.Zero)
            {
                ready.Set();
                return;
            }
            var source = CFMachPortCreateRunLoopSource(IntPtr.Zero, _tap, 0);
            _runLoop = CFRunLoopGetCurrent();
            CFRunLoopAddSource(_runLoop, source, CommonModes());
            CFRelease(source);
            CGEventTapEnable(_tap, true);
            ready.Set();
            CFRunLoopRun(); // events are delivered on this thread until Dispose stops the loop
        })
        { IsBackground = true, Name = "Desk Wheel event tap" };
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));

        if (_tap == IntPtr.Zero)
        {
            problem = "macOS didn't allow Desk Wheel to watch the keyboard. Check Privacy & Security → Input Monitoring.";
            return false;
        }
        problem = null;
        return true;
    }

    /// <summary>Runs on the tap thread: never block here, just post to the UI thread.</summary>
    private IntPtr OnEvent(IntPtr proxy, uint type, IntPtr cgEvent, IntPtr userInfo)
    {
        // An exception escaping a native callback kills the whole process, so nothing may leave this method.
        try
        {
            return HandleEvent(type, cgEvent);
        }
        catch (Exception ex)
        {
            Log.Write(ex);
            return cgEvent;
        }
    }

    private IntPtr HandleEvent(uint type, IntPtr cgEvent)
    {
        switch (type)
        {
            case LeftMouseDown:
                _mouseDownSeen = true;
                _draggingSeen = false;
                return cgEvent;
            case LeftMouseUp:
                _mouseDownSeen = false;
                _draggingSeen = false;
                return cgEvent;
            case LeftMouseDragged:
                _mouseDownSeen = true;
                if (!_draggingSeen)
                {
                    _draggingSeen = true;
                    Dispatcher.UIThread.Post(() => Log.Trace("Mouse drag started (seen by the event tap)."));
                }
                return cgEvent;
        }

        if (type is TapDisabledByTimeout or TapDisabledByUserInput)
        {
            CGEventTapEnable(_tap, true);
            return cgEvent;
        }

        if (type == FlagsChanged)
        {
            var flags = CGEventGetFlags(cgEvent) & WatchedFlags;
            var newlyDown = flags & ~_lastFlags;
            _lastFlags = flags;

            WheelMode? mode = (newlyDown & FlagShift) != 0 ? WheelMode.Convert
                : (newlyDown & FlagCommand) != 0 ? WheelMode.Tools
                : (newlyDown & FlagControl) != 0 ? WheelMode.Actions
                : null;
            if (mode is { } m)
                Dispatcher.UIThread.Post(() => WheelKeyTapped?.Invoke(m));
        }
        else if (type == KeyDown && CGEventGetIntegerValueField(cgEvent, KeyboardEventKeycode) == EscapeKeyCode)
        {
            Dispatcher.UIThread.Post(() => EscapePressed?.Invoke());
        }
        return cgEvent;
    }

    private static IntPtr CommonModes()
    {
        var lib = NativeLibrary.Load(CoreFoundation);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(lib, "kCFRunLoopCommonModes"));
    }

    /// <summary>
    /// Mid-drag check from three sources, any one is enough: the event tap's own mouse events,
    /// NSEvent.pressedMouseButtons, and the HID/session button state.
    /// </summary>
    public bool IsPrimaryButtonDown() =>
        _mouseDownSeen || (PressedMouseButtons() & 1) != 0
        || CGEventSourceButtonState(HidSystemState, LeftMouseButton)
        || CGEventSourceButtonState(CombinedSessionState, LeftMouseButton);

    public string DescribeInput() =>
        $"tap={_mouseDownSeen}, dragging={_draggingSeen}, NSEvent={(PressedMouseButtons() & 1) != 0}, " +
        $"HID={CGEventSourceButtonState(HidSystemState, LeftMouseButton)}, session={CGEventSourceButtonState(CombinedSessionState, LeftMouseButton)}";

    private static nuint PressedMouseButtons()
    {
        try { return SendUInt(objc_getClass("NSEvent"), sel_registerName("pressedMouseButtons")); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return 0; }
    }

    /// <summary>CoreGraphics gives points (top-left origin); Avalonia positions windows in pixels, so scale by the screen under the pointer.</summary>
    public PixelPoint CursorPixel(Screens screens)
    {
        var evt = CGEventCreate(IntPtr.Zero);
        var pt = CGEventGetLocation(evt);
        CFRelease(evt);

        foreach (var screen in screens.All)
        {
            var p = new PixelPoint((int)(pt.X * screen.Scaling), (int)(pt.Y * screen.Scaling));
            if (screen.Bounds.Contains(p))
                return p;
        }
        var scale = screens.Primary?.Scaling ?? 1;
        return new PixelPoint((int)(pt.X * scale), (int)(pt.Y * scale));
    }

    public bool ForegroundIsFileManager()
    {
        try
        {
            var workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            var app = Send(workspace, sel_registerName("frontmostApplication"));
            if (app == IntPtr.Zero) return false;
            var bundleId = Send(app, sel_registerName("bundleIdentifier"));
            if (bundleId == IntPtr.Zero) return false;
            var utf8 = Send(bundleId, sel_registerName("UTF8String"));
            return Marshal.PtrToStringUTF8(utf8) == "com.apple.finder";
        }
        catch (Exception ex)
        {
            Log.Write(ex);
            return true; // if the check itself fails, don't block the gesture
        }
    }

    public WheelMode? ModeForKey(Key key) => key switch
    {
        Key.LeftShift or Key.RightShift => WheelMode.Convert,
        Key.LWin or Key.RWin => WheelMode.Tools,          // Avalonia reports ⌘ as the Win key
        Key.LeftCtrl or Key.RightCtrl => WheelMode.Actions,
        _ => null,
    };

    public void CopyText(string text)
    {
        var psi = new ProcessStartInfo("/usr/bin/pbcopy")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        psi.Environment["LANG"] = "en_US.UTF-8";
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(text);
        p.StandardInput.Close();
        p.WaitForExit(3000);
    }

    public void Reveal(string path) => Open("-R", path);

    public void OpenTerminal(string folder) => Open("-a", "Terminal", folder);

    public void OpenFolder(string folder) => Open(folder);

    public void OpenFile(string path) => Open(path);

    public void OpenInBrowser(string htmlPath)
    {
        // "open page.html" would use the .html handler, which for developers is often a code editor.
        // Ask Launch Services which app opens https links, and open the page with that.
        var browser = DefaultBrowserPath();
        Log.Trace($"  Opening {htmlPath} in {browser ?? "the default app for .html"}");
        if (browser is null || !OpenAndWait("-a", browser, htmlPath))
            if (!OpenAndWait(htmlPath))
                throw new IOException("macOS couldn't open the page. See wheel-debug.txt for details.");
    }

    /// <summary>Runs open and waits briefly, so a refusal (unknown app, no permission) is noticed and logged.</summary>
    private static bool OpenAndWait(params string[] args)
    {
        var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi);
        if (process is null) return false;
        if (!process.WaitForExit(10_000)) return true; // still starting the app: assume it's on its way
        if (process.ExitCode == 0) return true;
        Log.Trace($"  open {string.Join(' ', args)} failed ({process.ExitCode}): {process.StandardError.ReadToEnd().Trim()}");
        return false;
    }

    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const uint Utf8Encoding = 0x08000100; // kCFStringEncodingUTF8
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, byte[] utf8, uint encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFURLCreateWithString(IntPtr alloc, IntPtr urlString, IntPtr baseUrl);
    [DllImport(CoreFoundation)] private static extern IntPtr CFURLCopyFileSystemPath(IntPtr url, nint pathStyle);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr str, byte[] buffer, nint bufferSize, uint encoding);
    [DllImport(CoreServices)] private static extern IntPtr LSCopyDefaultApplicationURLForURL(IntPtr url, uint roles, IntPtr error);

    private static string? DefaultBrowserPath()
    {
        IntPtr text = IntPtr.Zero, url = IntPtr.Zero, app = IntPtr.Zero, path = IntPtr.Zero;
        try
        {
            text = CFStringCreateWithCString(IntPtr.Zero, System.Text.Encoding.UTF8.GetBytes("https://example.com\0"), Utf8Encoding);
            if (text == IntPtr.Zero) return null;
            url = CFURLCreateWithString(IntPtr.Zero, text, IntPtr.Zero);
            if (url == IntPtr.Zero) return null;
            app = LSCopyDefaultApplicationURLForURL(url, 0xFFFFFFFF /* kLSRolesAll */, IntPtr.Zero);
            if (app == IntPtr.Zero) return null;
            path = CFURLCopyFileSystemPath(app, 0 /* POSIX */);
            if (path == IntPtr.Zero) return null;
            var buffer = new byte[4096];
            if (!CFStringGetCString(path, buffer, buffer.Length, Utf8Encoding)) return null;
            var result = System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is var n and >= 0 ? n : buffer.Length);
            return Directory.Exists(result) ? result : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            foreach (var cf in new[] { path, app, url, text })
                if (cf != IntPtr.Zero) CFRelease(cf);
        }
    }

    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightScreenCaptureAccess();
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRequestScreenCaptureAccess();
    [DllImport(CoreGraphics)] private static extern int CGGetDisplaysWithPoint(CGPoint point, uint maxDisplays, [Out] uint[] displays, out uint count);
    [DllImport(CoreGraphics)] private static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] displays, out uint count);

    /// <summary>
    /// macOS's own screencapture tool: the same selection screen as ⌘⇧4 (Space switches to window mode, Esc cancels).
    /// Needs the Screen Recording permission, like every capture app.
    /// </summary>
    public async Task<(bool Ok, string? Problem)> CaptureAsync(CaptureKind kind, string outputPng)
    {
        if (!CGPreflightScreenCaptureAccess())
        {
            CGRequestScreenCaptureAccess(); // shows the system prompt / adds Wheel to the list
            return (false, "Allow Desk Wheel in System Settings → Privacy & Security → Screen & System Audio Recording, then quit and reopen Desk Wheel.");
        }

        var args = new List<string> { "-x" }; // no shutter sound
        switch (kind)
        {
            case CaptureKind.Area: args.Add("-i"); break;
            case CaptureKind.Window:
                args.AddRange(["-i", "-w"]); // window selection only
                if (!CaptureWindowShadow) args.Add("-o");
                break;
            default: args.AddRange(["-D", DisplayUnderPointer().ToString()]); break;
        }
        args.Add(outputPng);

        var r = await ProcessRunner.RunAsync("/usr/sbin/screencapture", args);
        Log.Trace($"  screencapture {string.Join(' ', args)} → exit {r.ExitCode} {r.ErrorTail.Trim()}");
        if (File.Exists(outputPng) && new FileInfo(outputPng).Length > 0)
            return (true, null);
        return r.ExitCode == 0 || string.IsNullOrWhiteSpace(r.ErrorTail)
            ? (false, null) // Esc pressed: nothing to report
            : (false, "Screenshot failed: " + r.ErrorTail.Trim());
    }

    public int ScreenNumberUnderPointer() => DisplayUnderPointer() - 1;

    /// <summary>screencapture's -D number (1 = main display) for the display under the pointer.</summary>
    private static int DisplayUnderPointer()
    {
        try
        {
            var evt = CGEventCreate(IntPtr.Zero);
            var pt = CGEventGetLocation(evt);
            CFRelease(evt);
            var hit = new uint[1];
            var all = new uint[16];
            if (CGGetDisplaysWithPoint(pt, 1, hit, out var n) == 0 && n > 0
                && CGGetActiveDisplayList(16, all, out var total) == 0)
            {
                var index = Array.IndexOf(all, hit[0], 0, (int)total);
                if (index >= 0) return index + 1;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { Log.Write(ex); }
        return 1;
    }

    /// <summary>Puts the PNG on the clipboard as an image, via AppleScript (path passed as an argument, never spliced into the script).</summary>
    public async Task CopyImageAsync(string pngPath)
    {
        var r = await ProcessRunner.RunAsync("/usr/bin/osascript",
        [
            "-e", "on run argv",
            "-e", "set the clipboard to (read (POSIX file (item 1 of argv)) as «class PNGf»)",
            "-e", "end run",
            pngPath,
        ]);
        if (r.ExitCode != 0)
            throw new IOException("Couldn't copy the image: " + r.ErrorTail.Trim());
    }

    private static void Open(params string[] args)
    {
        var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }

    private static string LaunchAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", "app.deskwheel.plist");

    /// <summary>A per-user LaunchAgent (no admin rights). Takes effect at the next login.</summary>
    public bool StartAtLogin
    {
        get => File.Exists(LaunchAgentPath);
        set
        {
            if (!value)
            {
                File.Delete(LaunchAgentPath);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentPath)!);
            var exe = System.Security.SecurityElement.Escape(Environment.ProcessPath ?? "");
            File.WriteAllText(LaunchAgentPath, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                  <key>Label</key><string>app.deskwheel</string>
                  <key>ProgramArguments</key><array><string>{exe}</string></array>
                  <key>RunAtLoad</key><true/>
                </dict>
                </plist>
                """);
        }
    }

    public void Dispose()
    {
        foreach (var r in _hotKeyRefs)
            UnregisterEventHotKey(r);
        _hotKeyRefs.Clear();
        if (_runLoop != IntPtr.Zero)
            CFRunLoopStop(_runLoop);
    }
}
