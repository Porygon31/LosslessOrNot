using LosslessOrNot.Core;
using LosslessOrNot.Core.Assessment;
using LosslessOrNot.Core.Spectral;
using Xunit.Abstractions;

namespace LosslessOrNot.Tests;

/// <summary>
/// Real encoders at their default settings, decoded back into FLAC with and without
/// dither, on a master with the dynamics of music: the probability must be sure of
/// these, and must not doubt the master.
/// </summary>
public sealed class TranscodeFfmpegTests(ITestOutputHelper output) : IDisposable
{
    private readonly FixtureDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [FfmpegEncoderTheory("libmp3lame")]
    [InlineData("-b:a", "128k")]
    [InlineData("-b:a", "192k")]
    [InlineData("-q:a", "2")]
    public async Task Mp3Transcode_IsFakeLosslessWithHighConfidence(string option, string value)
    {
        string master = await _dir.MusicLikeMasterAsync("master.wav");
        string flac = await _dir.TranscodeAsync(master, "fake", ["-c:a", "libmp3lame", option, value], "mp3");

        TrackResult result = await AnalyzeAsync(flac);

        Assert.Equal(LosslessStatus.FakeLossless, result.Assessment.Status);
        Assert.True(result.Spectral!.CutoffConfidence >= 0.9, $"confidence {result.Spectral.CutoffConfidence:0.00}");
    }

    [FfmpegFact]
    public async Task AacTranscode_IsFakeLosslessWithHighConfidence()
    {
        string master = await _dir.MusicLikeMasterAsync("master.wav");
        string flac = await _dir.TranscodeAsync(master, "fake", ["-c:a", "aac", "-b:a", "96k"], "m4a");

        TrackResult result = await AnalyzeAsync(flac);

        Assert.Equal(LosslessStatus.FakeLossless, result.Assessment.Status);
        Assert.True(result.Spectral!.CutoffConfidence >= 0.9, $"confidence {result.Spectral.CutoffConfidence:0.00}");
    }

    // ffmpeg's AAC at 128 kbps cuts near 19.6 kHz: where sample-rate converters cut
    // too. The wall is found, but the file can only be suspect.
    [FfmpegFact]
    public async Task WallWhereResamplersCutToo_IsOnlySuspect()
    {
        string master = await _dir.MusicLikeMasterAsync("master.wav");
        string flac = await _dir.TranscodeAsync(master, "fake", ["-c:a", "aac", "-b:a", "128k"], "m4a");

        TrackResult result = await AnalyzeAsync(flac);

        Assert.InRange(result.Spectral!.CliffHz, (int)ConfidenceModel.ResamplerZoneStartHz, 21000);
        Assert.Equal(SpectralVerdict.Lossy, result.Spectral.Verdict);
        Assert.Equal(LosslessStatus.Suspect, result.Assessment.Status);
        Assert.Contains("convertisseur de fréquence", result.Spectral.Conclusion);
    }

    // Dither fills the silence above the wall with noise: it must not hide the transcode.
    [FfmpegEncoderTheory("libmp3lame")]
    [InlineData("triangular")]
    [InlineData("shibata")]
    public async Task DitheredDecode_IsStillFakeLossless(string ditherMethod)
    {
        string master = await _dir.MusicLikeMasterAsync("master.wav");
        string flac = await _dir.TranscodeAsync(
            master,
            "fake",
            ["-c:a", "libmp3lame", "-b:a", "192k"],
            "mp3",
            ["-af", $"aresample=osf=s16:dither_method={ditherMethod}", "-sample_fmt", "s16"]);

        TrackResult result = await AnalyzeAsync(flac);

        Assert.Equal(LosslessStatus.FakeLossless, result.Assessment.Status);
        // Dither above the wall is no content: the music ends at LAME's 192 kbps low-pass.
        Assert.InRange(result.Spectral!.CutoffHz, 17500, 19500);
    }

    // A 24-bit decode puts the rounding noise 48 dB lower, which uncovers what the
    // decoder's filter bank lets through above the cut: a faint image of the music.
    // The wall must still be recognised — as suspect only when it sits where
    // sample-rate converters cut too (Opus, Vorbis and AAC here).
    [FfmpegEncoderTheory("libmp3lame", "libvorbis", "libopus")]
    [InlineData("libmp3lame", "-b:a", "192k", "mp3")]
    [InlineData("aac", "-b:a", "128k", "m4a")]
    [InlineData("libvorbis", "-q:a", "5", "ogg")]
    [InlineData("libopus", "-b:a", "128k", "opus")]
    public async Task TwentyFourBitDecode_StillShowsTheCodecWall(string encoder, string option, string value, string extension)
    {
        string master = await _dir.MusicLikeMasterAsync("master.wav");
        string flac = await _dir.TranscodeAsync(
            master,
            "fake",
            ["-c:a", encoder, option, value],
            extension,
            ["-ar", "44100", "-sample_fmt", "s32", "-bits_per_raw_sample", "24"]);

        TrackResult result = await AnalyzeAsync(flac);

        Assert.True(result.Spectral!.HasCodecWall, "the codec's wall should be recognised");
        Assert.Equal(SpectralVerdict.Lossy, result.Spectral.Verdict);
        Assert.Equal(
            result.Spectral.CliffHz >= ConfidenceModel.ResamplerZoneStartHz ? LosslessStatus.Suspect : LosslessStatus.FakeLossless,
            result.Assessment.Status);
    }

    // A loud master (RMS about −5 dBFS, as electronic music often is): its MP3 decode
    // overshoots full scale and clips when written to 16 bits, and the distortion
    // fills the treble above the wall with a flat floor that follows the music. The
    // master itself, clipped as well, must stay genuine.
    [FfmpegEncoderTheory("libmp3lame")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoudMaster_AndItsClippedMp3Decode(bool transcode)
    {
        string master = await _dir.MusicLikeMasterAsync("loud.wav", gainDb: 30);
        string flac = transcode
            ? await _dir.TranscodeAsync(master, "fake", ["-c:a", "libmp3lame", "-b:a", "128k"], "mp3")
            : _dir.PathOf("genuine.flac");
        if (!transcode)
        {
            await _dir.RunFfmpegAsync("-i", master, "-c:a", "flac", flac);
        }

        TrackResult result = await AnalyzeAsync(flac);

        Assert.Equal(transcode ? LosslessStatus.FakeLossless : LosslessStatus.Authentic, result.Assessment.Status);
    }

    [FfmpegFact]
    public async Task MusicLikeMaster_IsAuthenticWithHighConfidence()
    {
        string master = await _dir.MusicLikeMasterAsync("master.wav");
        string flac = _dir.PathOf("genuine.flac");
        await _dir.RunFfmpegAsync("-i", master, "-c:a", "flac", flac);

        TrackResult result = await AnalyzeAsync(flac);

        Assert.Equal(LosslessStatus.Authentic, result.Assessment.Status);
        Assert.True(result.Spectral!.CutoffConfidence >= 0.9, $"confidence {result.Spectral.CutoffConfidence:0.00}");
    }

    private async Task<TrackResult> AnalyzeAsync(string path)
    {
        TrackResult result = await new TrackAnalyzer(Ffmpeg.Tools!).AnalyzeAsync(path);
        output.WriteLine(
            $"{Path.GetFileName(path)}: {result.Info?.CodecName} {result.Info?.SampleRate} Hz {result.Info?.BitsPerSample} bits -> "
            + $"{result.Assessment.Status} \"{result.Assessment.Label}\" {result.Error}");
        if (result.Spectral is { } s)
        {
            output.WriteLine(
                $"  content to {s.CutoffHz} Hz, wall {s.ShelfDropDb:0.0} dB at {s.CliffHz} Hz, floor {s.EnergyAboveCutoffDb:0.0} dB "
                + $"({s.FloorAboveQuantizationDb:0.0} dB above quantisation), r {s.FloorMusicCorrelation:0.00}, "
                + $"persistence {s.WallPersistence:0.00}, depth {s.FloorDepthDb:0.0} dB, r>21k {s.HighBandMusicCorrelation:0.00} "
                + $"-> {s.Verdict} (P {s.LossyProbability:0.000}, conf {s.CutoffConfidence:0.00}) {s.LikelySource}");
            foreach (AnalysisPoint point in s.Criteria.Concat(s.SupportingEvidence))
            {
                output.WriteLine($"  [{point.Signal}] {point.Title} — {point.Value} (weight {point.Weight:0.00})");
            }
        }
        return result;
    }
}
