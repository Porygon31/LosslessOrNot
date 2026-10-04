using LosslessOrNot.Core;
using LosslessOrNot.Core.Spectral;
using Xunit.Abstractions;
using static LosslessOrNot.Tests.Signals;

namespace LosslessOrNot.Tests;

/// <summary>
/// The analysis fed with in-memory signals, so the verdict logic is covered even
/// on a machine without ffmpeg. The ffmpeg-based tests remain the ground truth for
/// real encoders.
/// </summary>
public sealed class SyntheticSpectrumTests(ITestOutputHelper output)
{
    [Fact]
    public void FullBandNoise_IsLossless()
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(WhiteNoise(Rate * 12), Rate);
        Log(res);

        Assert.Equal(SpectralVerdict.Lossless, res.Verdict);
        Assert.True(res.CutoffHz > 23000, $"content to {res.CutoffHz} Hz, expected close to Nyquist");
    }

    [Fact]
    public void BrickWallNoise_IsLossy_WithTheWallWhereItWasPut()
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(BrickWallNoise(16000), Rate);
        Log(res);

        Assert.Equal(SpectralVerdict.Lossy, res.Verdict);
        Assert.InRange(res.CutoffHz, 15700, 16300);
        Assert.True(res.CutoffConfidence >= 0.75, $"confidence {res.CutoffConfidence:0.00}");
        Assert.NotEmpty(res.LikelySource);
    }

    [Fact]
    public void FewFrames_ReduceTheConfidence()
    {
        // 2^16 samples is only 31 frames, under the 60 needed for full confidence.
        SpectralAnalysis res = SpectralAnalyzer.Analyze(BrickWallNoise(16000, log2Length: 16), Rate);
        Log(res);

        Assert.Equal(SpectralVerdict.Lossy, res.Verdict);
        Assert.True(res.CutoffConfidence < 0.75, $"confidence {res.CutoffConfidence:0.00}");
        Assert.Contains(res.Evidence, line => line.Contains("confiance réduite"));
    }

    [Fact]
    public void Spectrogram_HasTheExpectedShape()
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(WhiteNoise(Rate * 12), Rate, timeStartSec: 30);

        Assert.Equal(280, res.FramesAnalyzed);
        Assert.Equal(res.FramesAnalyzed, res.Columns.Length);
        Assert.All(res.Columns, column => Assert.Equal(SpectralAnalyzer.SpecRowCount, column.Length));
        Assert.Equal(SpectralAnalyzer.AvgSpectrumPoints, res.AvgSpectrumDb.Length);
        Assert.Equal(Rate / 2.0 / SpectralAnalyzer.SpecRowCount, res.FreqBinHz);
        Assert.Equal(30, res.TimeStartSec);
        Assert.True(res.TimeEndSec > res.TimeStartSec);
        Assert.Equal(12, res.AnalyzedSec, precision: 6);
    }

    [Fact]
    public void Silence_IsAnErrorNotAVerdict()
    {
        Assert.Throws<AudioAnalysisException>(() => SpectralAnalyzer.Analyze(new float[Rate * 2], Rate));
    }

    [Fact]
    public void LessThanOneFrame_IsAnError()
    {
        Assert.Throws<AudioAnalysisException>(() => SpectralAnalyzer.Analyze(new float[1000], Rate));
    }

    private void Log(SpectralAnalysis res) =>
        output.WriteLine(
            $"content to {res.CutoffHz} Hz, wall {res.ShelfDropDb:0.0} dB at {res.CliffHz} Hz, floor {res.EnergyAboveCutoffDb:0.0} dB "
            + $"-> {res.Verdict} (conf {res.CutoffConfidence:0.00}, {res.FramesAnalyzed} frames) {res.LikelySource}");
}
