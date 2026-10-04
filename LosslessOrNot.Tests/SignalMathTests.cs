using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Tests;

public sealed class SignalMathTests
{
    [Fact]
    public void MovingAverage_ClampsItsWindowAtTheEdges()
    {
        double[] result = SignalMath.MovingAverage([0, 0, 3, 0, 0], 3);

        Assert.Equal(new double[] { 0, 1, 1, 1, 0 }, result);
    }

    [Fact]
    public void Percentile_SortsACopyAndLeavesTheInputAlone()
    {
        double[] input = [5, 1, 3];

        Assert.Equal(3, SignalMath.Percentile(input, 0.5));
        Assert.Equal(new double[] { 5, 1, 3 }, input);
        Assert.Equal(0, SignalMath.Percentile([], 0.5));
    }

    // Max-pooling, not averaging: a narrow but real high-frequency component must
    // survive the reduction to 512 points.
    [Fact]
    public void DownsampleSpectrum_KeepsANarrowPeak()
    {
        double[] db = Enumerable.Repeat(-100.0, 2048).ToArray();
        db[1500] = -10;

        double[] reduced = SpectralAnalyzer.DownsampleSpectrum(db, 512);

        Assert.Equal(512, reduced.Length);
        Assert.Equal(-10, reduced.Max());
        Assert.Equal(-10, reduced[1500 / 4]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, 0)]
    [InlineData(19.9, 0)]
    [InlineData(20, 5)]
    [InlineData(100, 25)]
    [InlineData(400, 60)]
    [InlineData(double.NaN, 0)]
    public void AnalysisStart_SkipsTheIntroButNeverMoreThanAMinute(double durationSec, double expectedStartSec)
    {
        Assert.Equal(expectedStartSec, SpectralAnalyzer.AnalysisStartSec(durationSec));
    }

    [Theory]
    [InlineData(11499, "≈64 kbps ou moins")]
    [InlineData(11500, "MP3 ≈128 kbps")]
    [InlineData(14999, "MP3 ≈128 kbps")]
    [InlineData(15000, "MP3 ≈160 kbps / AAC ≈96 kbps")]
    [InlineData(17999, "MP3 ≈192 kbps / AAC ≈128 kbps")]
    [InlineData(19000, "AAC ≈256 kbps")]
    [InlineData(20499, "MP3 ≈320 kbps")]
    [InlineData(20500, "encodeur avec perte à haut débit")]
    public void GuessEncoder_MapsCutoffBands(int cutoffHz, string expected)
    {
        Assert.Equal(expected, VerdictEngine.GuessEncoder(cutoffHz));
    }
}
