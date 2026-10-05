using System.Globalization;
using LosslessOrNot.Core.Media;

namespace LosslessOrNot.Tests;

/// <summary>ffmpeg and ffprobe as found on this machine, or null when they are not installed.</summary>
internal static class Ffmpeg
{
    public const string SkipReason = "ffmpeg/ffprobe not found (PATH or application folder)";

    public static FfmpegTools? Tools { get; } = FfmpegLocator.Locate();

    public static string Exe => Tools?.FfmpegPath ?? throw new InvalidOperationException(SkipReason);

    // Builds differ: libmp3lame, libopus or libvorbis may be missing.
    private static readonly Lazy<string> Encoders = new(() => Tools is null
        ? ""
        : ProcessRunner.RunAsync(Exe, ["-hide_banner", "-encoders"]).GetAwaiter().GetResult().StdOut);

    public static bool HasEncoder(string name) => Encoders.Value.Contains($" {name} ", StringComparison.Ordinal);

    /// <summary>Why a test needing these encoders must be skipped; null when it can run.</summary>
    public static string? SkipReasonFor(string[] encoders) =>
        Tools is null ? SkipReason
        : encoders.FirstOrDefault(e => !HasEncoder(e)) is { } missing ? $"this ffmpeg has no {missing} encoder"
        : null;
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

/// <summary>A [Theory] skipped unless ffmpeg and every named encoder are available.</summary>
public sealed class FfmpegEncoderTheoryAttribute : TheoryAttribute
{
    public FfmpegEncoderTheoryAttribute(params string[] encoders)
    {
        Skip = Ffmpeg.SkipReasonFor(encoders);
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

    /// <summary>
    /// 12 s of stereo pink noise at 44.1 kHz/16-bit whose level swells and fades like
    /// music (about 18 dB of dynamics): without them, nothing could follow the music
    /// or ignore it. A fixed seed keeps it identical from run to run.
    /// </summary>
    /// <param name="gainDb">
    /// Extra gain, clipped when written to 16 bits: +30 dB gives the RMS of a loud
    /// electronic master, about −5 dBFS, whose lossy decodes overshoot full scale.
    /// </param>
    public async Task<string> MusicLikeMasterAsync(string name, int gainDb = 0)
    {
        string output = PathOf(name);
        await RunFfmpegAsync(
            "-f", "lavfi", "-i", "anoisesrc=d=12:c=pink:r=44100:a=0.3:seed=7",
            "-af", Invariant($"volume=volume='0.12+0.88*abs(sin(2*PI*0.37*t)*sin(2*PI*0.11*t+1))':eval=frame,volume={gainDb}dB"),
            "-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le", output);
        return output;
    }

    /// <summary>
    /// The fake the app hunts: <paramref name="source"/> through a lossy encoder, then
    /// decoded back into FLAC with <paramref name="flacArgs"/> (16-bit without dither
    /// when omitted, as ffmpeg does by default).
    /// </summary>
    public async Task<string> TranscodeAsync(
        string source, string name, string[] encodeArgs, string lossyExtension, string[]? flacArgs = null)
    {
        string lossy = PathOf(name + "." + lossyExtension);
        await RunFfmpegAsync(["-i", source, .. encodeArgs, lossy]);
        string flac = PathOf(name + ".flac");
        await RunFfmpegAsync(["-i", lossy, .. flacArgs ?? ["-sample_fmt", "s16"], "-c:a", "flac", flac]);
        return flac;
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
