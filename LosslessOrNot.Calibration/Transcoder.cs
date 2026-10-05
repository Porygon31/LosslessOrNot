using System.Globalization;
using LosslessOrNot.Core;
using LosslessOrNot.Core.Media;

namespace LosslessOrNot.Calibration;

/// <summary>Cuts excerpts and makes fakes out of them with ffmpeg.</summary>
internal sealed class Transcoder(FfmpegTools tools)
{
    /// <summary>The encoders this ffmpeg build offers.</summary>
    public async Task<HashSet<string>> EncodersAsync()
    {
        ProcessRunner.Result result = await ProcessRunner.RunAsync(tools.FfmpegPath, ["-hide_banner", "-encoders"]);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in result.StdOut.Split('\n'))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].StartsWith('A'))
            {
                names.Add(parts[1]);
            }
        }
        return names;
    }

    /// <summary>
    /// The audio the analysis reads from <paramref name="source"/>, as WAV: transcoding
    /// only that excerpt keeps the fakes comparable to their original and saves time.
    /// </summary>
    public Task ExtractAsync(
        string source, double startSec, double durationSec, int bitsPerSample, string wav, CancellationToken cancellationToken) =>
        RunAsync(
            cancellationToken,
            "-ss", Invariant(startSec), "-i", source, "-t", Invariant(durationSec),
            "-map", "0:a:0", "-ac", "2", "-c:a", bitsPerSample > 16 ? "pcm_s24le" : "pcm_s16le", wav);

    /// <summary>Encodes <paramref name="excerpt"/> with the recipe, then decodes it back into FLAC at <paramref name="sampleRate"/>.</summary>
    /// <returns>The fake FLAC.</returns>
    public async Task<string> MakeAsync(
        Recipe recipe, string excerpt, int sampleRate, string folder, CancellationToken cancellationToken)
    {
        string lossy = Path.Combine(folder, recipe.Name + "." + recipe.Extension);
        await RunAsync(cancellationToken, ["-i", excerpt, "-c:a", recipe.Encoder, .. recipe.EncodeArgs, lossy]);
        string flac = Path.Combine(folder, recipe.Name + ".flac");
        await RunAsync(cancellationToken, ["-i", lossy, "-ar", Invariant(sampleRate), .. recipe.FlacArgs, "-c:a", "flac", flac]);
        File.Delete(lossy);
        return flac;
    }

    private async Task RunAsync(CancellationToken cancellationToken, params string[] args)
    {
        ProcessRunner.Result result = await ProcessRunner.RunAsync(
            tools.FfmpegPath, ["-y", "-v", "error", "-nostdin", .. args], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new AudioAnalysisException($"ffmpeg {string.Join(' ', args)} : {ProcessRunner.FirstLine(result.StdErr)}");
        }
    }

    private static string Invariant(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
