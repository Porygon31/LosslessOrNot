using LosslessOrNot.Core;
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

    [Fact]
    public void Spearman_FollowsRanksNotValues()
    {
        double[] x = [1, 2, 3, 4, 5];

        Assert.Equal(1, SignalMath.SpearmanCorrelation(x, [1, 4, 9, 16, 1000]), precision: 12);
        Assert.Equal(-1, SignalMath.SpearmanCorrelation(x, [5, 4, 3, 2, 1]), precision: 12);
    }

    [Fact]
    public void Spearman_AveragesTiesAndIsZeroForAConstant()
    {
        // Ranks of [1, 2, 2, 3] are [0, 1.5, 1.5, 3].
        Assert.Equal(1, SignalMath.SpearmanCorrelation([1, 2, 2, 3], [10, 20, 20, 30]), precision: 12);
        Assert.Equal(0, SignalMath.SpearmanCorrelation([1, 2, 3, 4], [7, 7, 7, 7]));
        Assert.Equal(0, SignalMath.SpearmanCorrelation([1, 2], [2, 1]));
        Assert.Throws<ArgumentException>(() => SignalMath.SpearmanCorrelation([1, 2, 3], [1, 2]));
    }

    [Fact]
    public void Spearman_OfIndependentSeries_IsNearZero()
    {
        var random = new Random(5);
        double[] x = Enumerable.Range(0, 4000).Select(_ => random.NextDouble()).ToArray();
        double[] y = Enumerable.Range(0, 4000).Select(_ => random.NextDouble()).ToArray();

        Assert.InRange(SignalMath.SpearmanCorrelation(x, y), -0.05, 0.05);
    }

    [Fact]
    public void Spread_IgnoresTheExtremes()
    {
        // Nearest rank over 12 points: the 10th percentile is the 2nd value, the 90th the 10th.
        double[] x = [-1000, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 1000];

        Assert.Equal(9 - 1, SignalMath.Spread(x));
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
    [InlineData(0, 45, 0)]
    [InlineData(12, 45, 0)]
    [InlineData(19.9, 5, 0)]
    [InlineData(20, 5, 5)]
    [InlineData(100, 45, 25)]
    [InlineData(400, 45, 60)]
    [InlineData(double.NaN, 45, 0)]
    public void AnalysisStart_SkipsTheIntroButNeverMoreThanAMinute(double durationSec, double windowSec, double expectedStartSec)
    {
        Assert.Equal(expectedStartSec, SpectralAnalyzer.AnalysisStartSec(durationSec, windowSec));
    }

    // The excerpt must fit in the file: a long window moves its start earlier, and a
    // window longer than the file starts at 0 so that the whole file is analysed.
    [Theory]
    [InlineData(100, 90, 10)]
    [InlineData(400, 380, 20)]
    [InlineData(30, 45, 0)]
    [InlineData(100, 100, 0)]
    public void AnalysisStart_KeepsTheWindowInsideTheFile(double durationSec, double windowSec, double expectedStartSec)
    {
        Assert.Equal(expectedStartSec, SpectralAnalyzer.AnalysisStartSec(durationSec, windowSec));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(45, 45)]
    [InlineData(10_000, 600)]
    public void AnalysisOptions_ClampTheWindowToWhatTheAnalysisAllows(double requested, double expected)
    {
        Assert.Equal(expected, new AnalysisOptions(requested).WindowSec);
    }

    // The medians measured for LAME 3.100 on real music must land on their setting.
    [Theory]
    [InlineData(11499, "≈64 kbps ou moins")]
    [InlineData(16590, "MP3 ≈128 kbps (V5)")]
    [InlineData(17960, "MP3 ≈160 kbps / Vorbis q3")]
    [InlineData(18670, "MP3 ≈192 kbps (V2)")]
    [InlineData(19360, "MP3 ≈256 kbps")]
    [InlineData(19960, "MP3 ≈320 kbps (V0), AAC, Opus ou Vorbis")]
    [InlineData(20500, "encodeur avec perte à haut débit")]
    public void GuessEncoder_MapsCutoffBands(int cutoffHz, string expected)
    {
        Assert.Equal(expected, VerdictEngine.GuessEncoder(cutoffHz));
    }
}
