using LosslessOrNot.Core.Spectral;
using Xunit.Abstractions;

namespace LosslessOrNot.Tests;

/// <summary>
/// The Go suite (spectrum_test.go) ported test for test: same ffmpeg fixtures, same
/// tolerances. Skipped when ffmpeg is not installed.
/// </summary>
public sealed class SpectralAnalyzerFfmpegTests(ITestOutputHelper output) : IDisposable
{
    private readonly FixtureDir _dir = new();

    public void Dispose() => _dir.Dispose();

    // TestCutoffDetection: given audio with a KNOWN brick wall, does the analyser
    // land on it?
    //
    // This used to use synthetic `lowpass` fixtures and assert the filter's corner
    // frequency. Those fixtures are not valid ground truth for what CutoffHz means:
    // "the highest frequency still carrying content", which is what a person reads
    // off a Spek plot. A single 2-pole low-pass is only -12 dB/octave, so a 15 kHz
    // fixture genuinely still has energy at 24 kHz, far above the -70 dB content
    // floor. Real encoders brick-wall, so they are the only fixtures with a
    // defensible answer.
    [FfmpegTheory]
    [InlineData("lp15k.mp3", 128, 15000, 1800)]
    [InlineData("lp19k.mp3", 320, 19000, 1800)]
    public async Task CutoffDetection_LandsOnARealEncodersWall(string name, int bitrate, int cutoffHz, double tolHz)
    {
        string path = await _dir.Mp3Async(name, bitrate, cutoffHz);

        SpectralAnalysis res = await AnalyzeAsync(path, 48000);

        double diff = Math.Abs(res.CutoffHz - cutoffHz);
        Assert.True(diff <= tolHz, $"{name}: cutoff {res.CutoffHz} Hz, wanted ~{cutoffHz} Hz (diff {diff:0})");
        Assert.NotEmpty(res.Evidence);
    }

    // TestHiResNotCalledLossy is the regression guard for the reported bug: a
    // Spek-verified lossless 96 kHz FLAC was branded "Fake Lossless".
    //
    // The cause was judging the cutoff as a FRACTION OF NYQUIST. A genuine hi-res
    // master has no content above ~24 kHz — that is the recording chain and human
    // hearing, not the encoding — so at 96 kHz its ratio is ~0.5 however perfect it
    // is. Both fixtures below have a steep wall that a ratio test would damn;
    // neither may be called lossy, because the wall sits above any frequency a
    // consumer lossy encoder can reach.
    [FfmpegFact]
    public async Task HiRes_IsNotCalledLossy()
    {
        // (a) The classic "hi-res" shape: 48 kHz full-band content resampled up to
        //     96 kHz, so the resampler's anti-alias filter leaves a very steep wall
        //     at 24 kHz landing in a deep floor.
        //
        //     The source needs headroom. The Go fixture used full-scale noise, whose
        //     resampled overshoots clip when written back to 16 bits; the clipping
        //     distortion then fills the band up to 48 kHz, there is no wall left, and
        //     the test passed without ever exercising the rule it guards.
        string src48 = await _dir.WhiteNoiseAsync("hires_src.wav", amplitude: 0.5);
        string up96 = _dir.PathOf("hires_upsampled.wav");
        await _dir.RunFfmpegAsync("-i", src48, "-ar", "96000", "-c:a", "pcm_s16le", up96);

        SpectralAnalysis upsampled = await AnalyzeAsync(up96, 96000);
        // The fixture really has a wall as abrupt and as deep as a codec's — only its
        // position, above anything a lossy encoder passes, keeps it from being lossy.
        Assert.True(
            upsampled.ShelfDropDb >= VerdictEngine.WallDropMinDb
            && upsampled.EnergyAboveCutoffDb <= VerdictEngine.CodecFloorDb
            && upsampled.CliffHz > VerdictEngine.CodecMaxWallHz,
            $"fixture has no codec-like wall above 21 kHz (wall {upsampled.ShelfDropDb:0.0} dB at {upsampled.CliffHz} Hz, "
            + $"floor {upsampled.EnergyAboveCutoffDb:0.0} dB)");

        // (b) A 96 kHz master band-limited around 22 kHz, as real recordings are.
        string lp96 = _dir.PathOf("hires_lp22k.wav");
        await _dir.RunFfmpegAsync(
            "-f", "lavfi", "-i", "anoisesrc=d=12:c=white:r=96000",
            "-af", "lowpass=f=22000:poles=2,lowpass=f=22000:poles=2",
            "-ac", "1", "-ar", "96000", "-c:a", "pcm_s16le", lp96);
        SpectralAnalysis bandLimited = await AnalyzeAsync(lp96, 96000);

        foreach ((string name, SpectralAnalysis res) in new[] { ("upsampled", upsampled), ("band-limited", bandLimited) })
        {
            Assert.True(
                res.Verdict != SpectralVerdict.Lossy,
                $"{name}: hi-res lossless wrongly judged LOSSY (content to {res.CutoffHz} Hz, "
                + $"wall {res.ShelfDropDb:0.0} dB at {res.CliffHz} Hz, floor {res.EnergyAboveCutoffDb:0.0} dB)");
        }
    }

    // TestGentleRollOffNotCalledLossy: programme material with little treble rolls
    // off gradually and never lands in a digital floor. It must not be branded lossy
    // no matter how far down the content ends up.
    [FfmpegFact]
    public async Task GentleRollOff_IsNotCalledLossy()
    {
        string path = await _dir.WhiteNoiseAsync("gentle15k.wav", lowpassHz: 15000);

        SpectralAnalysis res = await AnalyzeAsync(path, 48000);

        Assert.True(
            res.Verdict != SpectralVerdict.Lossy,
            $"gentle 2-pole roll-off wrongly judged lossy (wall {res.ShelfDropDb:0.0} dB at {res.CliffHz} Hz, "
            + $"floor {res.EnergyAboveCutoffDb:0.0} dB)");
    }

    // TestFullBandNotCalledLossy is the regression guard that matters most: real
    // full-band audio must NEVER be branded lossy. Wrongly calling a genuine master
    // "Fake Lossless" is the worse error and was the reported complaint.
    [FfmpegFact]
    public async Task FullBand_IsNotCalledLossy()
    {
        string path = await _dir.WhiteNoiseAsync("full.wav");

        SpectralAnalysis res = await AnalyzeAsync(path, 48000);

        Assert.True(
            res.Verdict != SpectralVerdict.Lossy,
            $"full-band audio wrongly judged lossy (cutoff {res.CutoffHz} Hz, shelf {res.ShelfDropDb:0.0} dB)");
    }

    // TestResolutionBeatsOldProbe: the old probe could only report one of
    // {8k,12k,16k,17k,19k,21k}. Verify we can land on a value it could never produce.
    [FfmpegFact]
    public async Task Resolution_BeatsTheOldSixPointProbe()
    {
        // Real encoder, not a synthetic filter — see CutoffDetection.
        string path = await _dir.Mp3Async("lp14k.mp3", 128, 14000);

        SpectralAnalysis res = await AnalyzeAsync(path, 48000);

        int[] legacy = [0, 8000, 12000, 16000, 17000, 19000, 21000];
        Assert.DoesNotContain(res.CutoffHz, legacy);
        Assert.True(Math.Abs(res.CutoffHz - 14000) <= 2000, $"cutoff {res.CutoffHz} Hz not near the true 14000 Hz");
    }

    // TestRealEncoders is the test that actually matters: encode with real lossy
    // encoders and confirm the analyser both finds a plausible cutoff AND calls it
    // lossy. Synthetic filters have gentle slopes; real encoders brick-wall, which
    // is precisely the signal this feature is meant to detect.
    //
    // -cutoff pins LAME's low-pass so the ground truth is known exactly. Relying on
    // the bitrate default was wrong: LAME did NOT low-pass white noise at 128k where
    // expected, and the analyser was correctly reporting the ~20.6 kHz content that
    // was really there.
    [FfmpegTheory]
    [InlineData("mp3_16k.mp3", 128, 16000, 14500, 17500)]
    [InlineData("mp3_20k.mp3", 320, 20000, 18500, 21500)]
    public async Task RealEncoders_AreFoundAndCalledLossy(string name, int bitrate, int cutoffHz, int wantLo, int wantHi)
    {
        string path = await _dir.Mp3Async(name, bitrate, cutoffHz);

        SpectralAnalysis res = await AnalyzeAsync(path, 48000);

        Assert.InRange(res.CutoffHz, wantLo, wantHi);
        Assert.Equal(SpectralVerdict.Lossy, res.Verdict);
    }

    // TestSpectrogramColumns validates the spectrogram matrix. A UI that draws wrong
    // data is worse than no UI, so it is checked against a known low-passed fixture:
    // rows above the cutoff must be dark.
    //
    // A REAL encoder, not a synthetic filter: a single 2-pole low-pass is only
    // -12 dB/octave, so at Nyquist it is ~24 dB down — genuinely still visible.
    [FfmpegFact]
    public async Task SpectrogramColumns_ShowTheLowPass()
    {
        string path = await _dir.Mp3Async("spec_mp3_12k.mp3", 128, 12000);

        SpectralAnalysis res = await AnalyzeAsync(path, 48000);

        Assert.NotEmpty(res.Columns);
        int rows = res.Columns[0].Length;
        Assert.Equal(SpectralAnalyzer.SpecRowCount, rows);
        Assert.True(res.FreqBinHz > 0, $"FreqBinHz not set ({res.FreqBinHz})");
        Assert.True(res.TimeEndSec > res.TimeStartSec, $"bad time window: {res.TimeStartSec}..{res.TimeEndSec}");

        // Mean brightness below vs above the 12 kHz cutoff. Nyquist is 24 kHz, so the
        // cutoff sits at row rows/2.
        int half = rows / 2;
        double lo = 0, hi = 0;
        int nlo = 0, nhi = 0;
        foreach (byte[] column in res.Columns)
        {
            for (int r = 0; r < column.Length; r++)
            {
                if (r < half - 10)
                {
                    lo += column[r];
                    nlo++;
                }
                else if (r > half + 10)
                {
                    hi += column[r];
                    nhi++;
                }
            }
        }
        double loAvg = lo / nlo, hiAvg = hi / nhi;
        output.WriteLine($"mean brightness below cutoff={loAvg:0.0}, above={hiAvg:0.0} (0-255)");
        Assert.True(loAvg > hiAvg * 2, $"spectrogram does not show the low-pass: below={loAvg:0.0} above={hiAvg:0.0}");

        // The matrix must stay small — the whole point of quantising to bytes.
        int total = res.Columns.Length * rows;
        output.WriteLine($"matrix: {res.Columns.Length} columns x {rows} rows = {total} bytes");
        Assert.True(total <= 1 << 21, $"spectrogram matrix too large: {total} bytes");
    }

    // The call the Go tests made: analyseSpectrum(path, ffmpeg, rate, 12).
    private async Task<SpectralAnalysis> AnalyzeAsync(string path, int sampleRate)
    {
        SpectralAnalysis res = await SpectralAnalyzer.AnalyzeFileAsync(path, Ffmpeg.Exe, sampleRate, durationSec: 12);
        output.WriteLine(
            $"{Path.GetFileName(path)}: content to {res.CutoffHz} Hz, wall {res.ShelfDropDb:0.0} dB at {res.CliffHz} Hz, "
            + $"floor {res.EnergyAboveCutoffDb:0.0} dB -> {res.Verdict} (conf {res.CutoffConfidence:0.00}) {res.LikelySource}");
        return res;
    }
}
