using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Win32;
using SkiaSharp;
using Wheel.Core;
using Wheel.Core.Capture;

namespace Wheel.App.Platform;

/// <summary>
/// Windows: a low-level keyboard hook (WH_KEYBOARD_LL) watches Shift / Ctrl / Alt.
/// It never swallows keys and only posts to the UI thread (Windows drops hooks that take ~300 ms).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsPlatform : IPlatform
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;
    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_ESCAPE = 0x1B, VK_SPACE = 0x20;
    private const int VK_1 = 0x31, VK_2 = 0x32, VK_3 = 0x33, VK_V = 0x56;
    private const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    private const int SM_SWAPBUTTON = 23;
    private const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x0002;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr mem);

    private readonly LowLevelKeyboardProc _proc; // kept in a field so the GC can't collect it
    private readonly HashSet<uint> _down = new();
    private IntPtr _hook;

    public event Action<WheelMode>? WheelKeyTapped;
    public event Action? EscapePressed;
    public event Action? HotkeyPressed;

    public string HotkeyName => "Ctrl+Shift+Space";

    /// <summary>The hotkey is handled inside the keyboard hook, so registering it just installs the hook.</summary>
    public bool RegisterHotkey(out string? problem) => EnsureHook(out problem);

    public void BringToFront() { } // Avalonia's Activate() is enough on Windows

    public event Action<CaptureKind>? CaptureHotkeyPressed;
    public event Action? ClipboardHotkeyPressed;

    public bool CaptureWindowShadow { get; set; } = true; // Windows' snipping overlay decides

    public string CaptureShortcut(CaptureKind kind) => kind switch
    {
        CaptureKind.Area => "Alt+Shift+1",
        CaptureKind.Window => "Alt+Shift+2",
        _ => "Alt+Shift+3",
    };

    public void OpenFolder(string folder) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false })?.Dispose();

    public int ScreenNumberUnderPointer() => 0; // Windows records by desktop coordinates instead

    public void OpenFile(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    // The .html handler on Windows is the default browser unless the user changed it.
    public void OpenInBrowser(string htmlPath) =>
        Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true })?.Dispose();

    // ---------- screenshots ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr bmp, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr mem);

    // ---------- windows on screen (for window screenshots) ----------

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetCloaked(IntPtr hWnd, int attribute, out int value, int size);

    public bool OwnCaptureOverlay => true;

    public IReadOnlyList<Avalonia.PixelRect> VisibleWindows()
    {
        const int GWL_EXSTYLE = -20, DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
        const long WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20;
        var own = (uint)Environment.ProcessId;
        var found = new List<Avalonia.PixelRect>();
        var name = new System.Text.StringBuilder(64);
        EnumWindows((hWnd, _) => // top-level windows, front to back
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == own) return true;
            var ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            if ((ex & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT)) != 0) return true;
            if (DwmGetCloaked(hWnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true; // other virtual desktops, suspended apps
            name.Clear();
            GetClassName(hWnd, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW") return true; // the desktop itself: the overlay falls back to the whole screen
            // The visible frame, without the invisible resize borders and shadow that GetWindowRect includes.
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(hWnd, out r))
                return true;
            if (r.Right - r.Left >= 24 && r.Bottom - r.Top >= 24)
                found.Add(new Avalonia.PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            return found.Count < 400;
        }, IntPtr.Zero);
        return found;
    }

    private const uint CF_DIB = 8;
    private const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    private static readonly uint PngFormat = RegisterClipboardFormat("PNG");

    /// <summary>
    /// Full screen: captured directly (GDI), the monitor under the pointer. Area and window normally go through
    /// Desk Wheel's own overlay (<see cref="OwnCaptureOverlay"/>); this path, Windows' snipping overlay
    /// (ms-screenclip:) picked up from the clipboard, is only a fallback.
    /// </summary>
    public async Task<(bool Ok, string? Problem)> CaptureAsync(CaptureKind kind, string outputPng)
    {
        try
        {
            if (kind == CaptureKind.FullScreen)
            {
                CaptureMonitorUnderPointer(outputPng);
                return (true, null);
            }

            var before = GetClipboardSequenceNumber();
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true })?.Dispose();

            // Wait for the snip to land on the clipboard. If the overlay closes without one, it was cancelled.
            var started = DateTime.UtcNow;
            while (DateTime.UtcNow - started < TimeSpan.FromMinutes(2))
            {
                await Task.Delay(200);
                if (GetClipboardSequenceNumber() != before)
                {
                    await Task.Delay(150); // let the snipping tool finish writing every format
                    return SaveClipboardImage(outputPng) ? (true, null) : (false, "The snip didn't contain an image.");
                }
                if (DateTime.UtcNow - started > TimeSpan.FromSeconds(2) && Process.GetProcessesByName("ScreenClippingHost").Length == 0)
                    return (false, null); // overlay gone, nothing captured: cancelled
            }
            return (false, null);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException or InvalidOperationException)
        {
            Log.Write(ex);
            return (false, "Screenshot failed: " + ex.Message);
        }
    }

    private void CaptureMonitorUnderPointer(string outputPng)
    {
        GetCursorPos(out var p);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(p, 2 /* MONITOR_DEFAULTTONEAREST */), ref info);
        var r = info.rcMonitor;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;

        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, SRCCOPY | CAPTUREBLT))
                throw new IOException("Windows didn't allow the screen copy.");
            SelectObject(mem, old);
            var header = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var px = new byte[w * h * 4];
            GetDIBits(mem, bmp, 0, (uint)h, px, ref header, 0);
            for (var i = 3; i < px.Length; i += 4) px[i] = 255; // GDI leaves alpha at 0
            ImageEngine.WriteRenderedPage(outputPng, w, h, px, w * 4);
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>Saves the clipboard image: the PNG format if present (lossless as the snipping tool made it), else CF_DIB.</summary>
    private bool SaveClipboardImage(string outputPng)
    {
        if (!OpenClipboardWithRetry()) return false;
        try
        {
            if (PngFormat != 0 && ReadGlobal(GetClipboardData(PngFormat)) is { Length: > 0 } png)
            {
                File.WriteAllBytes(outputPng, png);
                return true;
            }
            if (ReadGlobal(GetClipboardData(CF_DIB)) is { Length: > 0 } dib)
            {
                var (w, h, bgra) = Dib.ToBgra(dib);
                ImageEngine.WriteRenderedPage(outputPng, w, h, bgra, w * 4);
                return true;
            }
            return false;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static byte[]? ReadGlobal(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        var size = (int)GlobalSize(handle).ToUInt64();
        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static IntPtr ToGlobal(byte[] bytes)
    {
        var mem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
        var ptr = GlobalLock(mem);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        GlobalUnlock(mem);
        return mem;
    }

    private static bool OpenClipboardWithRetry()
    {
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    /// <summary>Puts the image on the clipboard as a bitmap (every app) and as PNG (apps that keep quality).</summary>
    public Task CopyImageAsync(string pngPath)
    {
        var png = File.ReadAllBytes(pngPath);
        using var decoded = SKBitmap.Decode(png) ?? throw new IOException("Couldn't read the screenshot.");
        using var bgraBitmap = decoded.Copy(SKColorType.Bgra8888) ?? throw new IOException("Couldn't convert the screenshot.");
        var pixels = new byte[bgraBitmap.Width * bgraBitmap.Height * 4];
        for (var y = 0; y < bgraBitmap.Height; y++)
            Marshal.Copy(bgraBitmap.GetPixels() + y * bgraBitmap.RowBytes, pixels, y * bgraBitmap.Width * 4, bgraBitmap.Width * 4);
        var dib = Dib.FromBgra(bgraBitmap.Width, bgraBitmap.Height, pixels);

        if (!OpenClipboardWithRetry())
            throw new IOException("The clipboard is busy. Try again.");
        try
        {
            EmptyClipboard();
            var dibMem = ToGlobal(dib);
            if (SetClipboardData(CF_DIB, dibMem) == IntPtr.Zero) GlobalFree(dibMem);
            if (PngFormat != 0)
            {
                var pngMem = ToGlobal(png);
                if (SetClipboardData(PngFormat, pngMem) == IntPtr.Zero) GlobalFree(pngMem);
            }
        }
        finally
        {
            CloseClipboard();
        }
        return Task.CompletedTask;
    }

    public WindowsPlatform() => _proc = HookCallback;

    public string FileManagerName => "File Explorer";
    public string[] KeyLegend => ["Shift", "Ctrl", "Alt"];
    public string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskWheel");

    /// <summary>Must be called on the UI thread: the hook is serviced by that thread's message loop.</summary>
    public bool StartListening(out string? problem) => EnsureHook(out problem);

    private bool EnsureHook(out string? problem)
    {
        problem = null;
        if (_hook != IntPtr.Zero)
            return true;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
        {
            problem = $"Windows didn't allow the keyboard hook (error {Marshal.GetLastWin32Error()}). Use right-click → Send to instead.";
            return false;
        }
        problem = null;
        return true;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();
            var injected = (data.flags & LLKHF_INJECTED) != 0;

            if (msg is WM_KEYUP or WM_SYSKEYUP)
            {
                _down.Remove(data.vkCode);
            }
            else if ((msg is WM_KEYDOWN or WM_SYSKEYDOWN) && !injected && data.vkCode is >= VK_1 and <= VK_3 && IsAltShiftOnly())
            {
                // Alt+Shift+1/2/3: screenshot area / window / full screen. Swallowed so the app below sees nothing.
                var kind = data.vkCode switch { VK_1 => CaptureKind.Area, VK_2 => CaptureKind.Window, _ => CaptureKind.FullScreen };
                Dispatcher.UIThread.Post(() => CaptureHotkeyPressed?.Invoke(kind));
                return (IntPtr)1;
            }
            else if ((msg is WM_KEYDOWN or WM_SYSKEYDOWN) && !injected && data.vkCode == VK_V && IsAltShiftOnly())
            {
                // Alt+Shift+V: clipboard history.
                Dispatcher.UIThread.Post(() => ClipboardHotkeyPressed?.Invoke());
                return (IntPtr)1;
            }
            else if ((msg is WM_KEYDOWN or WM_SYSKEYDOWN) && !injected && data.vkCode == VK_SPACE && IsCtrlShiftOnly())
            {
                // Ctrl+Shift+Space: open the wheel for the selected files. Swallowed so no space gets typed.
                Dispatcher.UIThread.Post(() => HotkeyPressed?.Invoke());
                return (IntPtr)1;
            }
            else if ((msg is WM_KEYDOWN or WM_SYSKEYDOWN) && !injected && _down.Add(data.vkCode)) // Add is false on auto-repeat
            {
                WheelMode? mode = data.vkCode switch
                {
                    VK_LSHIFT or VK_RSHIFT => WheelMode.Convert,
                    VK_LCONTROL or VK_RCONTROL => WheelMode.Tools,
                    VK_LMENU or VK_RMENU => WheelMode.Actions,
                    _ => null,
                };
                if (mode is { } m)
                    Dispatcher.UIThread.Post(() => WheelKeyTapped?.Invoke(m));
                else if (data.vkCode == VK_ESCAPE)
                    Dispatcher.UIThread.Post(() => EscapePressed?.Invoke());
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool IsAltShiftOnly() =>
        (_down.Contains(VK_LMENU) || _down.Contains(VK_RMENU))
        && (_down.Contains(VK_LSHIFT) || _down.Contains(VK_RSHIFT))
        && !_down.Contains(VK_LCONTROL) && !_down.Contains(VK_RCONTROL);

    private bool IsCtrlShiftOnly() =>
        (_down.Contains(VK_LCONTROL) || _down.Contains(VK_RCONTROL))
        && (_down.Contains(VK_LSHIFT) || _down.Contains(VK_RSHIFT))
        && !_down.Contains(VK_LMENU) && !_down.Contains(VK_RMENU);

    /// <summary>
    /// Selected items in the front File Explorer window, via the Shell.Application COM object
    /// (runs on the UI thread, which is STA). The desktop isn't an Explorer window, so it's not covered yet.
    /// </summary>
    public Task<(string[]? Files, string? Problem)> GetSelectedFilesAsync()
    {
        try
        {
            var foreground = GetForegroundWindow().ToInt64();
            var shellType = Type.GetTypeFromProgID("Shell.Application")
                            ?? throw new InvalidOperationException("Windows Shell isn't available.");
            var shell = Activator.CreateInstance(shellType)!;
            var windows = Call(shell, "Windows")!;
            var count = Convert.ToInt32(Get(windows, "Count"));
            for (var i = 0; i < count; i++)
            {
                var window = Call(windows, "Item", i);
                if (window is null || Convert.ToInt64(Get(window, "HWND")) != foreground)
                    continue;
                var items = Call(Get(window, "Document")!, "SelectedItems")!;
                var n = Convert.ToInt32(Get(items, "Count"));
                var files = new List<string>();
                for (var j = 0; j < n; j++)
                    if (Call(items, "Item", j) is { } item && Get(item, "Path") is string path && (File.Exists(path) || Directory.Exists(path)))
                        files.Add(path);
                return Task.FromResult<(string[]?, string?)>((files.ToArray(), null));
            }
            return Task.FromResult<(string[]?, string?)>((Array.Empty<string>(), null));
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidOperationException or InvalidCastException)
        {
            Log.Write(ex);
            return Task.FromResult<(string[]?, string?)>((null, "Couldn't read the File Explorer selection."));
        }

        static object? Call(object target, string name, params object[] args) =>
            target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);
        static object? Get(object target, string name) =>
            target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
    }

    public string DescribeInput() => $"button={IsPrimaryButtonDown()}, swapped={GetSystemMetrics(SM_SWAPBUTTON) != 0}";

    public bool IsPrimaryButtonDown()
    {
        // GetAsyncKeyState reads physical buttons; with swapped buttons the primary one is the right.
        var vk = GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    /// <summary>Avalonia on Windows is per-monitor DPI aware, so physical pixels match Window.Position.</summary>
    public PixelPoint CursorPixel(Screens screens)
    {
        GetCursorPos(out var p);
        return new PixelPoint(p.X, p.Y);
    }

    /// <summary>File Explorer windows and the desktop both belong to explorer.exe.</summary>
    public bool ForegroundIsFileManager()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public WheelMode? ModeForKey(Key key) => key switch
    {
        Key.LeftShift or Key.RightShift => WheelMode.Convert,
        Key.LeftCtrl or Key.RightCtrl => WheelMode.Tools,
        Key.LeftAlt or Key.RightAlt => WheelMode.Actions,
        _ => null,
    };

    public void CopyText(string text)
    {
        // The clipboard can be briefly locked by another app: retry for up to ~200 ms.
        var opened = false;
        for (var i = 0; i < 10 && !(opened = OpenClipboard(IntPtr.Zero)); i++)
            Thread.Sleep(20);
        if (!opened)
            throw new IOException("The clipboard is busy. Try again.");

        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var mem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            var ptr = GlobalLock(mem);
            Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
            Marshal.WriteInt16(ptr, text.Length * 2, 0);
            GlobalUnlock(mem);
            if (SetClipboardData(CF_UNICODETEXT, mem) == IntPtr.Zero)
                GlobalFree(mem); // on success the clipboard owns the memory
        }
        finally
        {
            CloseClipboard();
        }
    }

    // explorer.exe needs exactly: /select,"C:\path\file"
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOp
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOp op);

    public Task<(IReadOnlyList<string> Moved, string? Problem)> MoveToTrashAsync(IReadOnlyList<string> paths) => Task.Run(() =>
    {
        const uint delete = 3;
        // Allow undo (Recycle Bin), no "are you sure" (Desk Wheel asks), no progress window, but do warn
        // if something is too big for the Recycle Bin and would be deleted for good.
        const ushort flags = 0x0040 | 0x0010 | 0x0004 | 0x4000;
        var op = new ShFileOp
        {
            Function = delete,
            From = string.Concat(paths.Select(p => Path.GetFullPath(p) + "\0")) + "\0",
            Flags = flags,
        };
        var code = SHFileOperation(ref op);
        var moved = paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
        string? problem = moved.Count == paths.Count ? null
            : op.Aborted ? "Cancelled: nothing more was deleted."
            : code != 0 ? $"Windows couldn't move some items to the Recycle Bin (code 0x{code:X}). They may be open in another app."
            : "Some items couldn't be moved to the Recycle Bin.";
        return ((IReadOnlyList<string>)moved, problem);
    });

    public void Reveal(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();

    /// <summary>Windows Terminal if installed, else PowerShell.</summary>
    public void OpenTerminal(string folder)
    {
        try
        {
            var wt = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
            wt.ArgumentList.Add("-d");
            wt.ArgumentList.Add(folder);
            Process.Start(wt)?.Dispose();
        }
        catch (Win32Exception)
        {
            Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = folder })?.Dispose();
        }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>HKCU Run key: no admin rights needed.</summary>
    public bool StartAtLogin
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("DeskWheel") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("DeskWheel", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("DeskWheel", throwOnMissingValue: false);
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
