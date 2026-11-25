using System.Reflection;
using System.Runtime.InteropServices;

namespace Wheel.Core.Pdf;

/// <summary>
/// Minimal binding to PDFium (Google's PDF engine, BSD/Apache licence; binaries from bblanchon/pdfium-binaries).
/// The library is looked up in "engines/" beside the app, then the normal search path.
/// PDFium is not thread-safe, so every call goes through <see cref="Sync"/>.
/// </summary>
internal static class Pdfium
{
    private const string Lib = "pdfium";

    public static readonly object Sync = new();
    private static bool _initialised;

    static Pdfium() => NativeLibrary.SetDllImportResolver(typeof(Pdfium).Assembly, Resolve);

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib)
            return IntPtr.Zero;
        foreach (var candidate in Candidates())
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                return handle;
        return NativeLibrary.TryLoad(FileName, assembly, path, out var fallback) ? fallback : IntPtr.Zero;
    }

    private static string FileName =>
        OperatingSystem.IsWindows() ? "pdfium.dll" : OperatingSystem.IsMacOS() ? "libpdfium.dylib" : "libpdfium.so";

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "engines", FileName);
        yield return Path.Combine(AppContext.BaseDirectory, FileName);
    }

    /// <summary>True when the PDFium library can be loaded on this machine.</summary>
    public static bool IsAvailable()
    {
        try
        {
            lock (Sync)
            {
                EnsureInit();
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>Call inside <see cref="Sync"/>.</summary>
    public static void EnsureInit()
    {
        if (_initialised) return;
        FPDF_InitLibrary();
        _initialised = true;
    }

    // ---- fpdfview.h ----
    [DllImport(Lib)] public static extern void FPDF_InitLibrary();
    [DllImport(Lib)] public static extern CULong FPDF_GetLastError();
    [DllImport(Lib)] public static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);
    [DllImport(Lib)] public static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport(Lib)] public static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(Lib)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);
    [DllImport(Lib)] public static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Lib)] public static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Lib)] public static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Lib)] public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Lib)] public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, CULong color);
    [DllImport(Lib)] public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
    [DllImport(Lib)] public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Lib)] public static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Lib)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    public const int FPDF_ANNOT = 0x01; // draw annotations (form fields, stamps, comments)

    // ---- fpdf_text.h ----
    [DllImport(Lib)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Lib)] public static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(Lib)] public static extern int FPDFText_CountChars(IntPtr textPage);
    [DllImport(Lib)] public static extern int FPDFText_GetText(IntPtr textPage, int startIndex, int count, [Out] ushort[] result);

    // ---- fpdf_ppo.h / fpdf_edit.h / fpdf_save.h ----
    [DllImport(Lib)] public static extern IntPtr FPDF_CreateNewDocument();
    [DllImport(Lib)] public static extern int FPDF_ImportPages(IntPtr destDoc, IntPtr srcDoc, [MarshalAs(UnmanagedType.LPUTF8Str)] string? pageRange, int index);
    [DllImport(Lib)] public static extern void FPDFPage_SetRotation(IntPtr page, int rotate);
    [DllImport(Lib)] public static extern int FPDFPage_GetRotation(IntPtr page);
    [DllImport(Lib)] public static extern int FPDF_SaveAsCopy(IntPtr document, IntPtr fileWrite, CULong flags);

    // Adding text (page numbers).
    [DllImport(Lib)] public static extern IntPtr FPDFText_LoadStandardFont(IntPtr document, [MarshalAs(UnmanagedType.LPUTF8Str)] string font);
    [DllImport(Lib)] public static extern void FPDFFont_Close(IntPtr font);
    [DllImport(Lib)] public static extern IntPtr FPDFPageObj_CreateTextObj(IntPtr document, IntPtr font, float fontSize);
    [DllImport(Lib)] public static extern int FPDFText_SetText(IntPtr textObject, ushort[] text);
    [DllImport(Lib)] public static extern int FPDFPageObj_SetFillColor(IntPtr pageObject, uint r, uint g, uint b, uint a);
    [DllImport(Lib)] public static extern void FPDFPageObj_Transform(IntPtr pageObject, double a, double b, double c, double d, double e, double f);
    [DllImport(Lib)] public static extern int FPDFPageObj_GetBounds(IntPtr pageObject, out float left, out float bottom, out float right, out float top);
    [DllImport(Lib)] public static extern void FPDFPage_InsertObject(IntPtr page, IntPtr pageObject);
    [DllImport(Lib)] public static extern int FPDFPage_GenerateContent(IntPtr page);

    public const uint FPDF_NO_INCREMENTAL = 1 << 1;

    /// <summary>FPDF_FILEWRITE: { int version; WriteBlock* }.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileWrite
    {
        public int Version;
        public IntPtr WriteBlock;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WriteBlockFn(IntPtr self, IntPtr data, CULong size);

    /// <summary>Saves a document to a byte array. Call inside <see cref="Sync"/>.</summary>
    public static byte[] Save(IntPtr document)
    {
        using var buffer = new MemoryStream();
        WriteBlockFn write = (_, data, size) =>
        {
            var n = checked((int)size.Value);
            var chunk = new byte[n];
            Marshal.Copy(data, chunk, 0, n);
            buffer.Write(chunk, 0, n);
            return 1;
        };

        var fw = Marshal.AllocHGlobal(Marshal.SizeOf<FileWrite>());
        try
        {
            Marshal.StructureToPtr(new FileWrite { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(write) }, fw, false);
            if (FPDF_SaveAsCopy(document, fw, new CULong(FPDF_NO_INCREMENTAL)) == 0)
                throw new IOException("PDFium couldn't save the PDF.");
            GC.KeepAlive(write);
            return buffer.ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(fw);
        }
    }

    /// <summary>An open PDF. The file bytes stay in unmanaged memory until disposal, as PDFium requires.</summary>
    public sealed class Document : IDisposable
    {
        private readonly IntPtr _data;
        public IntPtr Handle { get; }

        public Document(string path)
        {
            var bytes = File.ReadAllBytes(path);
            _data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, _data, bytes.Length);
            Handle = FPDF_LoadMemDocument64(_data, (nuint)bytes.Length, null);
            if (Handle == IntPtr.Zero)
            {
                var code = FPDF_GetLastError().Value;
                Marshal.FreeHGlobal(_data);
                throw new InvalidDataException(code switch
                {
                    4 => "This PDF is password-protected.",
                    2 => "The PDF couldn't be found.",
                    3 => "This file isn't a valid PDF, or it's damaged.",
                    6 => "This PDF uses a security handler Desk Wheel doesn't support.",
                    _ => $"PDFium couldn't open the PDF (error {code}).",
                });
            }
        }

        public int PageCount => FPDF_GetPageCount(Handle);

        public void Dispose()
        {
            FPDF_CloseDocument(Handle);
            Marshal.FreeHGlobal(_data);
        }
    }
}
