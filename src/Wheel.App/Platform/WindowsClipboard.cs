using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Wheel.Core.Capture;
using Wheel.Core.Clipboard;

namespace Wheel.App.Platform;

/// <summary>Windows clipboard for clipboard history and copying files (Win32 clipboard API).</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsPlatform
{
    private const uint CF_HDROP = 15;
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? file, uint size);

    /// <summary>
    /// Formats that password managers and other apps set on things that shouldn't be remembered
    /// (documented by Microsoft for clipboard history and monitors). Anything carrying one is skipped.
    /// </summary>
    private static readonly uint ExcludeFormat = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint HistoryFormat = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint ViewerIgnoreFormat = RegisterClipboardFormat("Clipboard Viewer Ignore");
    private static readonly uint DropEffectFormat = RegisterClipboardFormat("Preferred DropEffect");

    public long ClipboardChangeCount() => GetClipboardSequenceNumber();

    public ClipboardContent? ReadClipboard()
    {
        if (!OpenClipboardWithRetry()) return null;
        try
        {
            if ((ExcludeFormat != 0 && IsClipboardFormatAvailable(ExcludeFormat)) ||
                (ViewerIgnoreFormat != 0 && IsClipboardFormatAvailable(ViewerIgnoreFormat)))
                return null;
            if (HistoryFormat != 0 && IsClipboardFormatAvailable(HistoryFormat) &&
                ReadGlobal(GetClipboardData(HistoryFormat)) is { Length: >= 4 } allowed && BitConverter.ToInt32(allowed, 0) == 0)
                return null;

            if (IsClipboardFormatAvailable(CF_HDROP))
            {
                var drop = GetClipboardData(CF_HDROP);
                if (drop != IntPtr.Zero)
                {
                    var files = new List<string>();
                    var count = DragQueryFile(drop, 0xFFFFFFFF, null, 0);
                    for (uint i = 0; i < count; i++)
                    {
                        var length = DragQueryFile(drop, i, null, 0);
                        var sb = new StringBuilder((int)length + 1);
                        DragQueryFile(drop, i, sb, length + 1);
                        if (sb.Length > 0) files.Add(sb.ToString());
                    }
                    if (files.Count > 0) return new ClipboardContent(null, null, files.ToArray());
                }
            }

            byte[]? png = null;
            if (PngFormat != 0 && IsClipboardFormatAvailable(PngFormat))
                png = ReadGlobal(GetClipboardData(PngFormat));
            if (png is null && IsClipboardFormatAvailable(CF_DIB) && ReadGlobal(GetClipboardData(CF_DIB)) is { Length: > 40 } dib)
            {
                var (w, h, bgra) = Dib.ToBgra(dib);
                png = ImageEngine.EncodePng(w, h, bgra);
            }
            if (png is not null && png.Length <= ClipboardStore.MaxImageBytes && PngInfo.Size(png) is { } size)
                return new ClipboardContent(null, png, null, size.Width, size.Height);

            if (IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                var handle = GetClipboardData(CF_UNICODETEXT);
                var ptr = handle == IntPtr.Zero ? IntPtr.Zero : GlobalLock(handle);
                if (ptr != IntPtr.Zero)
                {
                    try { return new ClipboardContent(Marshal.PtrToStringUni(ptr), null, null); }
                    finally { GlobalUnlock(handle); }
                }
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException)
        {
            Log.Write(ex);
            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Puts files on the clipboard as Explorer's Copy does (CF_HDROP, "copy" effect).</summary>
    public void CopyFiles(IReadOnlyList<string> paths)
    {
        // DROPFILES header (20 bytes: offset to the list, point, non-client flag, wide-chars flag),
        // then each full path null-terminated, then one more null.
        var list = new StringBuilder();
        foreach (var p in paths) list.Append(Path.GetFullPath(p)).Append('\0');
        list.Append('\0');
        var text = Encoding.Unicode.GetBytes(list.ToString());
        var bytes = new byte[20 + text.Length];
        BitConverter.GetBytes(20).CopyTo(bytes, 0);  // pFiles
        BitConverter.GetBytes(1).CopyTo(bytes, 16);  // fWide
        text.CopyTo(bytes, 20);

        if (!OpenClipboardWithRetry())
            throw new IOException("The clipboard is busy. Try again.");
        try
        {
            EmptyClipboard();
            var drop = ToGlobal(bytes);
            if (SetClipboardData(CF_HDROP, drop) == IntPtr.Zero) GlobalFree(drop);
            if (DropEffectFormat != 0)
            {
                var effect = ToGlobal(BitConverter.GetBytes(1)); // DROPEFFECT_COPY
                if (SetClipboardData(DropEffectFormat, effect) == IntPtr.Zero) GlobalFree(effect);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }
}
