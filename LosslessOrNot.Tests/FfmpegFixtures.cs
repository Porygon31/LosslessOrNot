using System.Globalization;
using LosslessOrNot.Core.Media;

namespace LosslessOrNot.Tests;

/// <summary>ffmpeg and ffprobe as found on this machine, or null when they are not installed.</summary>
internal static class Ffmpeg
{
    public const string SkipReason = "ffmpeg/ffprobe not found (PATH or application folder)";

    public static FfmpegTools? Tools { get; } = FfmpegLocator.Locate();

    public static string Exe => Tools?.FfmpegPath ?? throw new InvalidOperationException(SkipReason);
}

/// <summary>A [Fact] skipped when ffmpeg is unavailable — the equivalent of Go's t.Skip.</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (Ffmpeg.Tools is null)
        {
            Skip = Ffmpeg.SkipReason;
        }
    }
}

/// <summary>A [Theory] skipped when ffmpeg is unavailable.</summary>
public sealed class FfmpegTheoryAttribute : TheoryAttribute
{
    public FfmpegTheoryAttribute()
    {
        if (Ffmpeg.Tools is null)
        {
            Skip = Ffmpeg.SkipReason;
        }
    }
}

/// <summary>A private temp folder where fixtures are generated with ffmpeg; deleted on dispose.</summary>
public sealed class FixtureDir : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("losslessornot-").FullName;

    public string PathOf(string name) => Path.Combine(Root, name);

    /// <summary>Runs ffmpeg quietly and fails the test, with ffmpeg's own message, if it fails.</summary>
    public async Task RunFfmpegAsync(params string[] args)
    {
        ProcessRunner.Result result = await ProcessRunner.RunAsync(Ffmpeg.Exe, ["-y", "-v", "error", "-nostdin", .. args]);
        Assert.True(result.ExitCode == 0, $"ffmpeg {string.Join(' ', args)} failed:\n{result.StdErr}");
    }

    /// <summary>
    /// 12 s of mono 16-bit white noise at 48 kHz, optionally through ONE 2-pole
    /// low-pass. One stage on purpose: Butterworth is -3 dB exactly at the corner,
    /// so the fixture's true cutoff equals the nominal one. Cascading stages would
    /// move the -3 dB point well below the corner and make the expectation wrong.
    /// </summary>
    /// <param name="amplitude">Peak level; leave headroom when the noise will be resampled.</param>
    public async Task<string> WhiteNoiseAsync(string name, int lowpassHz = 0, double amplitude = 1.0)
    {
        string output = PathOf(name);
        List<string> args = ["-f", "lavfi", "-i", Invariant($"anoisesrc=d=12:c=white:r=48000:a={amplitude}")];
        if (lowpassHz > 0)
        {
            args.AddRange(["-af", Invariant($"lowpass=f={lowpassHz}:poles=2")]);
        }
        args.AddRange(["-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le", output]);
        await RunFfmpegAsync([.. args]);
        return output;
    }

    /// <summary>
    /// A real LAME encode with the low-pass pinned by -cutoff, so the fixture's
    /// ground truth is exact rather than inferred from the bitrate default.
    /// </summary>
    /// <param name="source">Audio to encode; full-band white noise when omitted.</param>
    public async Task<string> Mp3Async(string name, int bitrateKbps, int cutoffHz, string? source = null)
    {
        source ??= await WhiteNoiseAsync("src_" + name + ".wav");
        string output = PathOf(name);
        await RunFfmpegAsync(
            "-i", source,
            "-c:a", "libmp3lame", "-b:a", Invariant($"{bitrateKbps}k"), "-cutoff", Invariant($"{cutoffHz}"),
            output);
        return output;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it to the OS temp cleanup.
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
