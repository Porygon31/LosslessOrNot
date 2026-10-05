using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Tests;

/// <summary>The probability model on hand-made measurements, independent of any signal.</summary>
public sealed class ConfidenceModelTests
{
    private static readonly (double X, double Y)[] Anchors = [(10, -1), (20, 1), (30, 2)];

    [Theory]
    [InlineData(0, -1)]
    [InlineData(10, -1)]
    [InlineData(15, 0)]
    [InlineData(25, 1.5)]
    [InlineData(30, 2)]
    [InlineData(99, 2)]
    [InlineData(double.NaN, -1)]
    public void Interpolate_IsLinearBetweenAnchorsAndFlatBeyond(double x, double expected)
    {
        Assert.Equal(expected, ConfidenceModel.Interpolate(x, Anchors), precision: 12);
    }

    [Fact]
    public void CodecWall_NeedsAllThreeCriteria()
    {
        Assert.True(ConfidenceModel.Evaluate(Codec()).HasCodecWall);
        Assert.False(ConfidenceModel.Evaluate(Codec() with { WallDropDb = 12 }).HasCodecWall);
        Assert.False(ConfidenceModel.Evaluate(Codec() with { WallHz = 22500 }).HasCodecWall);
        Assert.False(ConfidenceModel.Evaluate(Codec() with { FloorAboveDb = -60, QuantizationFloorDb = -100 }).HasCodecWall);
    }

    // The floor may be far above -88 dB of the peak and still be digital silence:
    // what counts is that it sits at the format's own rounding noise.
    [Fact]
    public void FloorAtTheQuantisationNoise_IsDigitalSilence_WhateverItsLevel()
    {
        Score score = ConfidenceModel.Evaluate(Codec() with { FloorAboveDb = -62, QuantizationFloorDb = -64 });

        Assert.Equal(FloorKind.DigitalSilence, score.FloorKind);
        Assert.True(score.HasCodecWall);
    }

    [Fact]
    public void ResidueFollowingTheMusic_DeniesTheCodecWall()
    {
        Score score = ConfidenceModel.Evaluate(Codec() with { Wall = new WallTracking(1, 0.9, 20, 18, 40) });

        Assert.Equal(FloorKind.FollowsMusic, score.FloorKind);
        Assert.False(score.HasCodecWall);
        Assert.True(score.Floor < 0, $"floor weight {score.Floor:0.00}");
    }

    // Far below the music, following it is what every decoder's filter bank does.
    [Fact]
    public void FaintImageOfTheMusic_IsLeakage()
    {
        Score score = ConfidenceModel.Evaluate(Codec() with { FloorAboveDb = -86, Wall = new WallTracking(1, 0.95, 20, 18, 90) });

        Assert.Equal(FloorKind.Leakage, score.FloorKind);
        Assert.True(score.HasCodecWall);
        Assert.True(score.Floor > 0, $"floor weight {score.Floor:0.00}");
    }

    // Above 19.1 kHz, encoders and sample-rate converters cut alike: whatever the
    // evidence, the file can only be suspect.
    [Theory]
    [InlineData(18500, false)]
    [InlineData(19500, true)]
    [InlineData(20800, true)]
    public void WallWhereResamplersCutToo_CapsTheProbability(double wallHz, bool capped)
    {
        Score score = ConfidenceModel.Evaluate(Codec() with { WallHz = wallHz, WallDropDb = 60 });

        Assert.True(score.HasCodecWall);
        Assert.Equal(capped, score.ZoneCap is not null);
        Assert.True(capped ? score.LossyProbability <= 0.55 : score.LossyProbability >= 0.95, $"P {score.LossyProbability:0.00}");
    }

    // A floor lying flat above a wall is no music, which keeps falling — even when
    // it is neither silent nor clearly following the music.
    [Fact]
    public void FlatFloor_IsNothingMusical()
    {
        Score score = ConfidenceModel.Evaluate(
            Codec() with { FloorAboveDb = -70, Wall = new WallTracking(1, 0.35, 20, 12, 40), FloorSlopeDbPerKHz = -0.2 });
        Score sloping = ConfidenceModel.Evaluate(
            Codec() with { FloorAboveDb = -70, Wall = new WallTracking(1, 0.35, 20, 12, 40), FloorSlopeDbPerKHz = -5 });

        Assert.Equal(FloorKind.Distortion, score.FloorKind);
        Assert.True(score.HasCodecWall);
        Assert.Equal(FloorKind.Residual, sloping.FloorKind);
        Assert.False(sloping.HasCodecWall);
    }

    // Clipping distortion caps the drop: 15 dB onto a flat floor still make a wall.
    [Theory]
    [InlineData(17, -0.3, true)]
    [InlineData(17, -4.0, false)]
    [InlineData(13, -0.3, false)]
    public void SmallerDropOntoAFlatFloor_IsAWall(double dropDb, double slope, bool steep)
    {
        Measurements m = Codec() with { WallDropDb = dropDb, FloorSlopeDbPerKHz = slope };

        Assert.Equal(steep, m.IsSteep);
    }

    [Fact]
    public void ConstantNoise_IsACodecWallWithDither()
    {
        Score score = ConfidenceModel.Evaluate(Codec() with { FloorAboveDb = -55, Wall = new WallTracking(1, 0.02, 20, 1, 40) });

        Assert.Equal(FloorKind.StationaryNoise, score.FloorKind);
        Assert.True(score.HasCodecWall);
    }

    // Without dynamics, a correlation means nothing: it must not decide anything.
    [Fact]
    public void FlatMusic_LeavesTheCorrelationOut()
    {
        Score withTracking = ConfidenceModel.Evaluate(Codec() with { Wall = new WallTracking(1, 0.9, 2, 2, 60) });

        Assert.Equal(FloorKind.DigitalSilence, withTracking.FloorKind);
        Assert.Equal(ConfidenceModel.Evaluate(Codec() with { Wall = new WallTracking(1, 0.0, 2, 2, 60) }).Floor, withTracking.Floor);
    }

    [Fact]
    public void Probability_StaysWithinItsBounds()
    {
        Score certain = ConfidenceModel.Evaluate(Codec() with { WallDropDb = 90, Wall = new WallTracking(1, 0, 20, 1, 60) });
        Score genuine = ConfidenceModel.Evaluate(new Measurements(
            24000, 13000, 1, -2, 24000, 500, 16, -100, -10, null, new HighBandTracking(0.95, 20, 10)));

        Assert.Equal(ConfidenceModel.MaxProbability, certain.LossyProbability);
        Assert.InRange(genuine.LossyProbability, ConfidenceModel.MinProbability, 0.02);
    }

    // Energy beyond 21 kHz that ignores the music is dither or hiss: it no longer
    // counts as proof of a genuine master, as content reach alone used to.
    [Fact]
    public void HighBandNoise_IsNoLosslessEvidence()
    {
        var reach = new Measurements(22000, 13000, 1, -2, 24000, 500, 16, -100, -60, null, null);

        Assert.True(ConfidenceModel.Evaluate(reach).HighBand < 0);
        Assert.Equal(0, ConfidenceModel.Evaluate(reach with { HighBand = new HighBandTracking(0.05, 20, 30) }).HighBand);
        Assert.Equal(0, ConfidenceModel.Evaluate(reach with { HighBandDb = -95 }).HighBand);
        Assert.True(
            ConfidenceModel.Evaluate(reach with { HighBand = new HighBandTracking(0.9, 20, 30) }).HighBand
            < ConfidenceModel.Evaluate(reach).HighBand);
    }

    /// <summary>An MP3-like wall at 16 kHz, 40 dB deep within 1 kHz, landing at -98 dB.</summary>
    private static Measurements Codec() => new(
        ContentTopHz: 16200,
        WallHz: 15800,
        WallDropDb: 40,
        FloorAboveDb: -98,
        NyquistHz: 22050,
        Frames: 900,
        BitsPerSample: 16,
        QuantizationFloorDb: null,
        HighBandDb: -110,
        Wall: null,
        HighBand: null);
}
