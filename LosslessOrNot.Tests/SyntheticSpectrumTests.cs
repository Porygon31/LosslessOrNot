using LosslessOrNot.Core;
using LosslessOrNot.Core.Spectral;
using Xunit.Abstractions;

namespace LosslessOrNot.Tests;

/// <summary>
/// The analysis fed with in-memory signals, so the verdict logic is covered even
/// on a machine without ffmpeg. The ffmpeg-based tests remain the ground truth for
/// real encoders.
/// </summary>
public sealed class SyntheticSpectrumTests(ITestOutputHelper output)
{
    private const int Rate = 48000;

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

    private static float[] WhiteNoise(int length, int seed = 1234)
    {
        var random = new Random(seed);
        var samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = (float)(random.NextDouble() - 0.5);
        }
        return samples;
    }

    /// <summary>
    /// White noise with every component above <paramref name="cutoffHz"/> removed
    /// exactly, by zeroing the FFT bins of one long block: a perfect brick wall
    /// landing in a true digital floor, which is the shape a lossy encoder leaves.
    /// </summary>
    private static float[] BrickWallNoise(int cutoffHz, int log2Length = 19)
    {
        int n = 1 << log2Length;
        float[] noise = WhiteNoise(n);
        var re = new double[n];
        var im = new double[n];
        for (int i = 0; i < n; i++)
        {
            re[i] = noise[i];
        }

        Fft.Transform(re, im);
        int keep = (int)((long)cutoffHz * n / Rate);
        for (int k = keep + 1; k < n - keep; k++)
        {
            re[k] = 0;
            im[k] = 0;
        }
        // Inverse transform through the forward one: ifft(x) = conj(fft(conj(x))) / n.
        for (int k = 0; k < n; k++)
        {
            im[k] = -im[k];
        }
        Fft.Transform(re, im);

        var samples = new float[n];
        for (int i = 0; i < n; i++)
        {
            samples[i] = (float)(re[i] / n);
        }
        return samples;
    }

    private void Log(SpectralAnalysis res) =>
        output.WriteLine(
            $"content to {res.CutoffHz} Hz, wall {res.ShelfDropDb:0.0} dB at {res.CliffHz} Hz, floor {res.EnergyAboveCutoffDb:0.0} dB "
            + $"-> {res.Verdict} (conf {res.CutoffConfidence:0.00}, {res.FramesAnalyzed} frames) {res.LikelySource}");
}
