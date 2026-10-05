using LosslessOrNot.Core.Spectral;
using Xunit.Abstractions;
using static LosslessOrNot.Tests.Signals;

namespace LosslessOrNot.Tests;

/// <summary>
/// The cases the averaged spectrum alone got wrong, and that the frame-by-frame
/// indicators and the probability now settle. Every signal is given the dynamics of
/// music: without them, nothing can follow the music or ignore it.
/// </summary>
public sealed class ConfidenceTests(ITestOutputHelper output)
{
    // A quiet track's rounding noise sits only ~60 dB under the music: above the
    // -88 dB floor rule, and even above the -70 dB content threshold, so the old
    // rules read the noise as content and called this transcode lossless.
    [Theory]
    [InlineData(SpectralAnalyzer.DefaultFftSize)]
    [InlineData(8192)]
    public void QuietTranscode_IsLossy_NotLossless(int fftSize)
    {
        float[] samples = Quantize(Scale(Modulate(BrickWallNoise(16000)), 0.05), 16);

        SpectralAnalysis res = SpectralAnalyzer.Analyze(samples, Rate, bitsPerSample: 16, fftSize: fftSize);
        Log(res);

        Assert.True(res.EnergyAboveCutoffDb > VerdictEngine.CodecFloorDb, "the old floor rule should have missed it");
        Assert.Equal(SpectralVerdict.Lossy, res.Verdict);
        Assert.True(res.CutoffConfidence >= 0.9, $"confidence {res.CutoffConfidence:0.00}");
        Assert.InRange(res.FloorAboveQuantizationDb!.Value, -6, 6);
        Assert.InRange(res.CutoffHz, 15000, 17000);
    }

    [Theory]
    [InlineData(SpectralAnalyzer.DefaultFftSize)]
    [InlineData(8192)]
    public void LoudTranscode_ReachesNearCertainty(int fftSize)
    {
        float[] samples = Quantize(Scale(Modulate(BrickWallNoise(16000)), 0.5), 16);

        SpectralAnalysis res = SpectralAnalyzer.Analyze(samples, Rate, bitsPerSample: 16, fftSize: fftSize);
        Log(res);

        Assert.Equal(SpectralVerdict.Lossy, res.Verdict);
        Assert.True(res.CutoffConfidence >= 0.95, $"confidence {res.CutoffConfidence:0.00}");
        Assert.True(res.WallPersistence >= 0.9, $"persistence {res.WallPersistence:0.00}");
        Assert.InRange(res.FloorMusicCorrelation!.Value, -0.2, 0.2);
    }

    // As abrupt as a codec's wall and landing below -88 dB of the peak — the old rules
    // called it lossy — but what remains above it rises and falls with the music, only
    // 50 dB below it: attenuated programme, which an encoder would have erased.
    [Theory]
    [InlineData(SpectralAnalyzer.DefaultFftSize)]
    [InlineData(8192)]
    public void ResidueFollowingTheMusic_IsNotCalledLossy(int fftSize)
    {
        float[] samples = Modulate(SteepResidualNoise(16000, residualDb: -50, tiltDbPerKHz: 2.5));

        SpectralAnalysis res = SpectralAnalyzer.Analyze(samples, Rate, fftSize: fftSize);
        Log(res);

        Assert.True(
            res.ShelfDropDb >= VerdictEngine.WallDropMinDb && res.EnergyAboveCutoffDb <= VerdictEngine.CodecFloorDb,
            "the fixture should meet the old rules' three criteria");
        Assert.NotEqual(SpectralVerdict.Lossy, res.Verdict);
        Assert.True(res.FloorMusicCorrelation >= 0.5, $"r = {res.FloorMusicCorrelation:0.00}");
        Assert.Equal(PointSignal.LosslessLike, res.Criteria[2].Signal);
    }

    // A lossy decode of a loud master overshoots full scale, and writing it to integers
    // clips it: the distortion fills the treble above the wall and follows the music,
    // since it comes from its peaks — but it lies flat, where music would keep falling.
    // Measured on a DnB master (RMS −5 dBFS): its MP3 decode peaked at +4.5 dBFS.
    [Theory]
    [InlineData(SpectralAnalyzer.DefaultFftSize)]
    [InlineData(8192)]
    public void ClippedDecodeOfALoudMaster_IsStillLossy(int fftSize)
    {
        // A master compressed to about 10 dB of dynamics, clipped at 1.5 σ of its
        // loudest passages: the louder the music, the more distortion.
        float[] samples = Quantize(Clip(Modulate(BrickWallNoise(16000), minGain: 0.3), 0.35), 16);

        SpectralAnalysis res = SpectralAnalyzer.Analyze(samples, Rate, bitsPerSample: 16, fftSize: fftSize);
        Log(res);

        Assert.True(res.FloorMusicCorrelation >= 0.5, $"r = {res.FloorMusicCorrelation:0.00}");
        Assert.True(res.FloorSlopeDbPerKHz >= ConfidenceModel.FlatSlopeDbPerKHz, $"slope {res.FloorSlopeDbPerKHz:0.0} dB/kHz");
        Assert.Equal(SpectralVerdict.Lossy, res.Verdict);
        Assert.Contains("écrêtage", res.Criteria[2].Value);
    }

    // The same image of the music, but 95 dB below it: that is what a decoder's filter
    // bank leaves above its cut, uncovered by a 24-bit decode — not attenuated programme.
    [Theory]
    [InlineData(SpectralAnalyzer.DefaultFftSize)]
    [InlineData(8192)]
    public void FaintImageOfTheMusic_IsDecoderLeakage(int fftSize)
    {
        float[] samples = Modulate(SteepResidualNoise(16000, residualDb: -95));

        SpectralAnalysis res = SpectralAnalyzer.Analyze(samples, Rate, fftSize: fftSize);
        Log(res);

        Assert.True(res.FloorMusicCorrelation >= 0.5, $"r = {res.FloorMusicCorrelation:0.00}");
        Assert.True(res.FloorDepthDb >= FrameEvidence.LeakageDepthDb, $"depth {res.FloorDepthDb:0.0} dB");
        Assert.Equal(SpectralVerdict.Lossy, res.Verdict);
        Assert.True(res.CutoffConfidence >= 0.9, $"confidence {res.CutoffConfidence:0.00}");
    }

    // Noise-shaped dither added after a lossy decode: the treble above the wall is
    // full of noise, loud enough to pass for content up to Nyquist. It is no proof of
    // a genuine master: it ignores the music.
    [Theory]
    [InlineData(SpectralAnalyzer.DefaultFftSize)]
    [InlineData(8192)]
    public void ConstantNoiseAboveTheWall_IsNoProofOfLossless(int fftSize)
    {
        float[] samples = Add(Modulate(BrickWallNoise(16000)), HighNoise(16000, gain: 1e-3));

        SpectralAnalysis res = SpectralAnalyzer.Analyze(samples, Rate, fftSize: fftSize);
        Log(res);

        Assert.True(
            res.AvgSpectrumDb[^2] > VerdictEngine.ContentFloorDb,
            "the noise should clear the -70 dB content threshold up to Nyquist, as the old rule read it");
        Assert.Equal(SpectralVerdict.Lossy, res.Verdict);
        Assert.InRange(res.FloorMusicCorrelation!.Value, -0.2, 0.2);
        // The noise above the wall is not content: the music ends at the wall.
        Assert.InRange(res.CutoffHz, 15000, 17000);
    }

    [Theory]
    [InlineData(SpectralAnalyzer.DefaultFftSize)]
    [InlineData(8192)]
    public void FullBandMusic_IsLosslessWithHighConfidence(int fftSize)
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(Modulate(WhiteNoise(Rate * 12)), Rate, fftSize: fftSize);
        Log(res);

        Assert.Equal(SpectralVerdict.Lossless, res.Verdict);
        Assert.True(res.CutoffConfidence >= 0.9, $"confidence {res.CutoffConfidence:0.00}");
        Assert.True(res.HighBandMusicCorrelation >= 0.5, $"r = {res.HighBandMusicCorrelation:0.00}");
    }

    [Theory]
    [InlineData(SpectralAnalyzer.DefaultFftSize)]
    [InlineData(8192)]
    public void EveryCountedPoint_CarriesItsWeight(int fftSize)
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(
            Quantize(Scale(Modulate(BrickWallNoise(16000)), 0.5), 16), Rate, bitsPerSample: 16, fftSize: fftSize);

        Assert.All(res.Criteria, point => Assert.NotNull(point.Weight));
        Assert.All(res.Criteria, point => Assert.True(point.Weight > 0, $"{point.Title}: {point.Weight:0.00}"));
        Assert.NotNull(res.SupportingEvidence[0].Weight);
    }

    private void Log(SpectralAnalysis res)
    {
        output.WriteLine(
            $"content to {res.CutoffHz} Hz, wall {res.ShelfDropDb:0.0} dB at {res.CliffHz} Hz, floor {res.EnergyAboveCutoffDb:0.0} dB "
            + $"({res.FloorAboveQuantizationDb:0.0} dB above quantisation), r {res.FloorMusicCorrelation:0.00}, "
            + $"persistence {res.WallPersistence:0.00}, r>21k {res.HighBandMusicCorrelation:0.00} "
            + $"-> {res.Verdict} (P {res.LossyProbability:0.000}, conf {res.CutoffConfidence:0.00})");
        foreach (AnalysisPoint point in res.Criteria.Concat(res.SupportingEvidence))
        {
            output.WriteLine($"  [{point.Signal}] {point.Title} — {point.Value} (weight {point.Weight:0.00})");
        }
    }
}
