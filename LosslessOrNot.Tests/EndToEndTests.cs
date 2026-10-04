using LosslessOrNot.Core;
using LosslessOrNot.Core.Assessment;
using Xunit.Abstractions;

namespace LosslessOrNot.Tests;

/// <summary>
/// The question the app exists to answer, asked of real files through the full
/// pipeline: ffprobe, ffmpeg decode, FFT, verdict, final status.
/// </summary>
public sealed class EndToEndTests(ITestOutputHelper output) : IDisposable
{
    private readonly FixtureDir _dir = new();

    public void Dispose() => _dir.Dispose();

    // The real-world fake: an MP3 decoded and re-encoded as a CD-quality FLAC.
    [FfmpegFact]
    public async Task FlacTranscodedFromAnMp3_IsFakeLossless()
    {
        string master = await MasterAsync("master.wav");
        string mp3 = await _dir.Mp3Async("lossy.mp3", 128, 16000, master);
        string flac = _dir.PathOf("fake.flac");
        await _dir.RunFfmpegAsync("-i", mp3, "-c:a", "flac", "-sample_fmt", "s16", flac);

        TrackResult result = await AnalyzeAsync(flac);

        Assert.Equal(LosslessStatus.FakeLossless, result.Assessment.Status);
        Assert.Contains("MP3", result.Assessment.Explanation);
    }

    [FfmpegFact]
    public async Task FlacOfAFullBandMaster_IsAuthentic()
    {
        string master = await MasterAsync("master.wav");
        string flac = _dir.PathOf("genuine.flac");
        await _dir.RunFfmpegAsync("-i", master, "-c:a", "flac", flac);

        TrackResult result = await AnalyzeAsync(flac);

        Assert.Equal(LosslessStatus.Authentic, result.Assessment.Status);
        Assert.False(result.Assessment.IsHiRes);
    }

    [FfmpegFact]
    public async Task Mp3_IsALossyFormat()
    {
        string master = await MasterAsync("master.wav");
        string mp3 = _dir.PathOf("track.mp3");
        await _dir.RunFfmpegAsync("-i", master, "-c:a", "libmp3lame", "-b:a", "320k", mp3);

        TrackResult result = await AnalyzeAsync(mp3);

        Assert.Equal(LosslessStatus.LossyFormat, result.Assessment.Status);
        Assert.Equal("mp3", result.Info?.CodecName);
    }

    // One unreadable file must come back as an error result, not stop a batch.
    [FfmpegFact]
    public async Task UnreadableFile_IsAnErrorResult()
    {
        string path = _dir.PathOf("broken.flac");
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, 4096).Select(i => (byte)(i * 37)).ToArray());

        TrackResult result = await AnalyzeAsync(path);

        Assert.Equal(LosslessStatus.Error, result.Assessment.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [FfmpegFact]
    public async Task Cancellation_StopsTheAnalysis()
    {
        string master = await MasterAsync("master.wav");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new TrackAnalyzer(Ffmpeg.Tools!).AnalyzeAsync(master, cts.Token));
    }

    /// <summary>
    /// 12 s of stereo pink noise at 44.1 kHz/16-bit. Pink rather than white because
    /// its spectrum falls with frequency like music's, so the loudest bin sits well
    /// above the treble — as it does in real recordings — and the level leaves the
    /// headroom real masters have, so nothing clips once decoded from MP3.
    /// </summary>
    private async Task<string> MasterAsync(string name)
    {
        string path = _dir.PathOf(name);
        await _dir.RunFfmpegAsync(
            "-f", "lavfi", "-i", "anoisesrc=d=12:c=pink:r=44100:a=0.3",
            "-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le", path);
        return path;
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
                + $"-> {s.Verdict} (conf {s.CutoffConfidence:0.00}) {s.LikelySource}");
        }
        return result;
    }
}
