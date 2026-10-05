using LosslessOrNot.Core.Spectral;
using Xunit.Abstractions;
using static LosslessOrNot.Tests.Signals;

namespace LosslessOrNot.Tests;

/// <summary>
/// The points shown in the "Explications" tab must say the same thing as the
/// verdict: all three criteria ✘ exactly when the verdict is lossy.
/// </summary>
public sealed class ExplanationTests(ITestOutputHelper output)
{
    [Fact]
    public void BrickWall_MeetsAllThreeCriteria()
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(BrickWallNoise(16000), Rate);
        Log(res);

        Assert.Equal(SpectralVerdict.Lossy, res.Verdict);
        Assert.Equal(3, res.Criteria.Count);
        Assert.All(res.Criteria, point => Assert.Equal(PointSignal.LossyLike, point.Signal));
        Assert.Contains("3 critères sont réunis", res.Conclusion);
        Assert.Contains(res.LikelySource, res.Conclusion);
    }

    [Fact]
    public void FullBand_MeetsNoCriterion_AndReachesBeyondLossyEncoders()
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(WhiteNoise(Rate * 12), Rate);
        Log(res);

        Assert.DoesNotContain(res.Criteria, point => point.Signal == PointSignal.LossyLike);
        Assert.Equal(PointSignal.LosslessLike, Observation(res, "Étendue du contenu").Signal);
        Assert.Contains("cohérent avec un vrai lossless", res.Conclusion);
    }

    // No wall at all: the drop is gentle, so where it sits and what lies above it
    // mean nothing, and with treble fading out before 20.5 kHz nothing is proven.
    [Fact]
    public void GentleRollOff_IsExplainedAsInconclusive()
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(SlopedNoise(fromHz: 8000, dbPerKHz: 6), Rate);
        Log(res);

        Assert.Equal(SpectralVerdict.Inconclusive, res.Verdict);
        Assert.Equal(PointSignal.LosslessLike, res.Criteria[0].Signal);
        Assert.Equal(PointSignal.Info, res.Criteria[1].Signal);
        Assert.Equal(PointSignal.Info, res.Criteria[2].Signal);
        Assert.Contains("sans objet", res.Criteria[1].Value);
        Assert.Contains("impossible de trancher", res.Conclusion);
    }

    [Fact]
    public void FewFrames_AreFlaggedAsALimit()
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(BrickWallNoise(16000, log2Length: 16), Rate);
        Log(res);

        Assert.Equal(PointSignal.Warning, Observation(res, "Audio analysé").Signal);
        Assert.Equal(PointSignal.Warning, Observation(res, "Confiance").Signal);
        Assert.Contains("réduite", Observation(res, "Confiance").Explanation);
    }

    [Fact]
    public void EveryPoint_IsFullyExplained()
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(BrickWallNoise(16000), Rate);

        Assert.All(res.Criteria.Concat(res.SupportingEvidence).Concat(res.Observations), point =>
        {
            Assert.False(string.IsNullOrWhiteSpace(point.Title));
            Assert.False(string.IsNullOrWhiteSpace(point.Value));
            Assert.False(string.IsNullOrWhiteSpace(point.Explanation));
        });
        Assert.All(res.Criteria.Concat(res.SupportingEvidence), point => Assert.False(string.IsNullOrWhiteSpace(point.Threshold)));
        Assert.DoesNotContain(res.Observations, point => point.Title == "Durée du fichier");
    }

    // The probability is part of the explanation: the "Confiance" point tells how it
    // was reached, whatever the verdict.
    [Fact]
    public void Confidence_ExplainsTheProbability()
    {
        SpectralAnalysis res = SpectralAnalyzer.Analyze(SlopedNoise(fromHz: 8000, dbPerKHz: 6), Rate);

        Assert.Contains("probabilité de transcodage", Observation(res, "Confiance").Explanation);
    }

    private static AnalysisPoint Observation(SpectralAnalysis res, string title) =>
        Assert.Single(res.Observations, point => point.Title == title);

    private void Log(SpectralAnalysis res)
    {
        foreach (AnalysisPoint point in res.Criteria.Concat(res.Observations))
        {
            output.WriteLine($"[{point.Signal}] {point.Title} — {point.Value}");
        }
        output.WriteLine("=> " + res.Conclusion);
    }
}
