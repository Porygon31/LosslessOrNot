using LosslessOrNot.Core.Spectral;
using static LosslessOrNot.Tests.Signals;

namespace LosslessOrNot.Tests;

/// <summary>
/// The analysis now consumes audio as ffmpeg decodes it, in blocks of whatever size
/// the pipe delivers. Splitting the audio must not change the result in any way.
/// </summary>
public sealed class SpectralAccumulatorTests
{
    [Fact]
    public void ChunkedInput_GivesExactlyTheSameResultAsOneBlock()
    {
        float[] samples = BrickWallNoise(16000, log2Length: 18);
        SpectralAnalysis whole = SpectralAnalyzer.Analyze(samples, Rate);

        var accumulator = new SpectralAccumulator(Rate, samples.Length);
        var random = new Random(42);
        for (int offset = 0; offset < samples.Length;)
        {
            int size = Math.Min(random.Next(1, 10_000), samples.Length - offset);
            accumulator.Add(samples.AsSpan(offset, size));
            offset += size;
        }
        SpectralAnalysis chunked = accumulator.Complete(new AnalysisRequest(0, (double)samples.Length / Rate, 0));

        Assert.Equal(whole.FramesAnalyzed, chunked.FramesAnalyzed);
        Assert.Equal(whole.Verdict, chunked.Verdict);
        Assert.Equal(whole.CutoffHz, chunked.CutoffHz);
        Assert.Equal(whole.CliffHz, chunked.CliffHz);
        Assert.Equal(whole.ShelfDropDb, chunked.ShelfDropDb);
        Assert.Equal(whole.EnergyAboveCutoffDb, chunked.EnergyAboveCutoffDb);
        Assert.Equal(whole.AvgSpectrumDb, chunked.AvgSpectrumDb);
        Assert.Equal(whole.Columns.Length, chunked.Columns.Length);
        for (int c = 0; c < whole.Columns.Length; c++)
        {
            Assert.Equal(whole.Columns[c], chunked.Columns[c]);
        }
    }

    // The old 900-frame cap silently limited every analysis to ~42 s at 44.1 kHz,
    // whatever the window. Now every frame counts, and only the spectrogram is
    // thinned out so that it stays small.
    [Fact]
    public void LongInput_CountsEveryFrame_AndCapsTheSpectrogram()
    {
        float[] samples = WhiteNoise(Rate * 120);

        SpectralAnalysis res = SpectralAnalyzer.Analyze(samples, Rate);

        int expectedFrames = (samples.Length - SpectralAnalyzer.FftSize) / SpectralAnalyzer.FftHop + 1;
        Assert.Equal(expectedFrames, res.FramesAnalyzed);
        Assert.True(res.Columns.Length <= SpectralAnalyzer.MaxColumns, $"{res.Columns.Length} columns");
        Assert.True(res.Columns.Length >= expectedFrames / 2, $"{res.Columns.Length} columns for {expectedFrames} frames");
        Assert.Equal(120, res.AnalyzedSec, precision: 6);
    }
}
