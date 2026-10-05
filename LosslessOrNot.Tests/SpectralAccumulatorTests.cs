using LosslessOrNot.Core;
using LosslessOrNot.Core.Spectral;
using static LosslessOrNot.Tests.Signals;

namespace LosslessOrNot.Tests;

/// <summary>
/// The analysis now consumes audio as ffmpeg decodes it, in blocks of whatever size
/// the pipe delivers. Splitting the audio must not change the result in any way.
/// </summary>
public sealed class SpectralAccumulatorTests
{
    [Theory]
    [InlineData(SpectralAnalyzer.DefaultFftSize)]
    [InlineData(8192)]
    public void ChunkedInput_GivesExactlyTheSameResultAsOneBlock(int fftSize)
    {
        float[] samples = Quantize(Modulate(BrickWallNoise(16000, log2Length: 18)), 16);
        SpectralAnalysis whole = SpectralAnalyzer.Analyze(samples, Rate, bitsPerSample: 16, fftSize: fftSize);

        var accumulator = new SpectralAccumulator(Rate, samples.Length, bitsPerSample: 16, fftSize);
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
        Assert.Equal(whole.FloorAboveQuantizationDb, chunked.FloorAboveQuantizationDb);
        Assert.Equal(whole.FloorMusicCorrelation, chunked.FloorMusicCorrelation);
        Assert.Equal(whole.WallPersistence, chunked.WallPersistence);
        Assert.Equal(whole.LossyProbability, chunked.LossyProbability);
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
    [Theory]
    [InlineData(SpectralAnalyzer.DefaultFftSize)]
    [InlineData(8192)]
    public void LongInput_CountsEveryFrame_AndCapsTheSpectrogram(int fftSize)
    {
        float[] samples = WhiteNoise(Rate * 120);

        SpectralAnalysis res = SpectralAnalyzer.Analyze(samples, Rate, fftSize: fftSize);

        // Frames overlap by half.
        int expectedFrames = (samples.Length - fftSize) / (fftSize / 2) + 1;
        Assert.Equal(expectedFrames, res.FramesAnalyzed);
        Assert.Equal(fftSize, res.FftSize);
        Assert.True(res.Columns.Length <= SpectralAnalyzer.MaxColumns, $"{res.Columns.Length} columns");
        Assert.True(res.Columns.Length >= expectedFrames / 2, $"{res.Columns.Length} columns for {expectedFrames} frames");
        Assert.Equal(120, res.AnalyzedSec, precision: 6);
    }

    // The frame-by-frame bands keep at most MaxFrames frames, evenly spread — even
    // when far more frames arrive than announced, as with a duration ffprobe got wrong.
    [Theory]
    [InlineData(10_000, 10_000)]
    [InlineData(10_000, 100)]
    public void FrameBands_StayBounded(int frames, long announced)
    {
        var bands = new FrameBands(Rate, SpectralAnalyzer.DefaultFftSize / 2, announced);
        var power = new double[SpectralAnalyzer.DefaultFftSize / 2];
        for (int i = 0; i < frames; i++)
        {
            // Every bin of frame i holds i, so a band's level gives the frame back.
            Array.Fill(power, i);
            bands.Add(power);
        }

        Assert.InRange(bands.Count, FrameBands.MaxFrames / 2, 2 * FrameBands.MaxFrames);
        int[] kept = bands.Levels(0, FrameBands.BandHz).Select(db => (int)Math.Round(Math.Pow(10, db / 10))).ToArray();
        Assert.Equal(bands.Count, kept.Length);
        Assert.Equal(0, kept[0]);
        Assert.All(kept.Zip(kept.Skip(1), (a, b) => b - a), step => Assert.Equal(kept[1] - kept[0], step));
    }

    [Fact]
    public void FrameBands_LevelsCoverWholeBandsOnly()
    {
        var bands = new FrameBands(Rate, SpectralAnalyzer.DefaultFftSize / 2, 1);
        bands.Add(Enumerable.Repeat(1.0, SpectralAnalyzer.DefaultFftSize / 2).ToArray());

        Assert.Single(bands.Levels(1000, 1100));
        Assert.Empty(bands.Levels(1010, 1100));
        Assert.Equal(0, bands.Levels(15000, 17000)[0], precision: 9);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(6000)]
    [InlineData(32768)]
    public void FftSize_MustBeAPowerOfTwoInRange(int fftSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpectralAccumulator(Rate, Rate, fftSize: fftSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisOptions(FftSize: fftSize));
    }

    // A finer FFT narrows the bins, not the levels: rounding noise is white, so the
    // floor it sets relative to a broadband peak does not move with the frame size.
    [Fact]
    public void QuantizationFloor_DoesNotDependOnTheFftSize()
    {
        float[] samples = Quantize(Scale(Modulate(BrickWallNoise(16000)), 0.05), 16);

        double? at4096 = SpectralAnalyzer.Analyze(samples, Rate, bitsPerSample: 16).FloorAboveQuantizationDb;
        double? at8192 = SpectralAnalyzer.Analyze(samples, Rate, bitsPerSample: 16, fftSize: 8192).FloorAboveQuantizationDb;

        Assert.NotNull(at4096);
        Assert.NotNull(at8192);
        Assert.InRange(at8192!.Value - at4096!.Value, -1, 1);
    }

    // A tone keeps its power in one bin while noise spreads it over all of them: at
    // 8192 points, a bass note rises 3 dB against the treble. Levels are measured
    // against the peak of a 4096-point pass, so the floor above a wall and the reach
    // of the content read the same at both sizes.
    [Fact]
    public void LevelsUnderATonalPeak_DoNotDependOnTheFftSize()
    {
        float[] noise = Scale(BrickWallNoise(16000), 0.05);
        float[] bass = Enumerable.Range(0, noise.Length).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 110 * i / Rate))).ToArray();
        float[] samples = Quantize(Add(noise, bass), 16);

        SpectralAnalysis at4096 = SpectralAnalyzer.Analyze(samples, Rate, bitsPerSample: 16);
        SpectralAnalysis at8192 = SpectralAnalyzer.Analyze(samples, Rate, bitsPerSample: 16, fftSize: 8192);

        Assert.InRange(at8192.EnergyAboveCutoffDb - at4096.EnergyAboveCutoffDb, -1, 1);
        Assert.InRange(at8192.QuantizationFloorDb!.Value - at4096.QuantizationFloorDb!.Value, -0.5, 0.5);
        Assert.InRange(at8192.CutoffHz - at4096.CutoffHz, -100, 100);
    }
}
