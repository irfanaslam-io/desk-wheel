using System.Diagnostics;

namespace Wheel.Core;

/// <summary>
/// Runs a command-line tool (ffmpeg, sips, ...) with no window. Arguments are passed as a list,
/// never joined into one string, so file names can't inject options or shell syntax.
/// </summary>
public static class ProcessRunner
{
    /// <param name="ExitCode">Process exit code.</param>
    /// <param name="ErrorTail">Last 600 characters of stderr (or stdout when stderr is empty), for error messages.</param>
    /// <param name="Output">Full standard output.</param>
    public sealed record Result(int ExitCode, string ErrorTail, string Output);

    public static async Task<Result> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            // OCR returns Arabic text: read it as UTF-8 everywhere (Windows would otherwise use the OEM code page).
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var p = Process.Start(psi) ?? throw new IOException($"Couldn't start {Path.GetFileName(exe)}.");
        var stderr = p.StandardError.ReadToEndAsync(ct);
        var stdout = p.StandardOutput.ReadToEndAsync(ct);

        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        var err = await stderr;
        var output = await stdout;
        var tail = err.Length > 0 ? err : output; // sips reports errors on stdout
        return new Result(p.ExitCode, tail.Length > 600 ? tail[^600..] : tail, output);
    }
}
