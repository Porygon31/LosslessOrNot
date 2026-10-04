using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace LosslessOrNot.Core.Media;

/// <summary>Runs ffmpeg and ffprobe as hidden child processes.</summary>
internal static class ProcessRunner
{
    public readonly record struct Result(int ExitCode, string StdOut, string StdErr);

    /// <summary>
    /// Starts <paramref name="exe"/> without a console window (what Go's hideProcess
    /// did) with stdout and stderr redirected. Arguments go through ArgumentList, so
    /// paths containing spaces, quotes or accents need no escaping.
    /// </summary>
    public static Process Start(string exe, IEnumerable<string> args)
    {
        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new AudioAnalysisException($"Impossible de lancer {Path.GetFileName(exe)} : {ex.Message}", ex);
        }
        return process;
    }

    /// <summary>Runs to completion, capturing both output streams. Cancelling kills the process.</summary>
    public static async Task<Result> RunAsync(
        string exe, IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using Process process = Start(exe, args);
        // Both streams are drained concurrently: a child blocked writing to a full
        // stderr pipe while we wait on stdout would never exit.
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using (cancellationToken.Register(static p => Kill((Process)p!), process))
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        string output = await stdout.ConfigureAwait(false);
        string errors = await stderr.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new Result(process.ExitCode, output, errors);
    }

    /// <summary>Kills the process and its children; a process that already exited is ignored.</summary>
    public static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Exiting at this very moment, or access denied.
        }
    }

    /// <summary>The first non-empty line of a tool's diagnostics, for error messages.</summary>
    public static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
}
