using LosslessOrNot.Core.Assessment;
using LosslessOrNot.Core.Media;
using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Tests;

public sealed class LosslessAssessorTests
{
    [Theory]
    [InlineData("mp3")]
    [InlineData("aac")]
    [InlineData("opus")]
    [InlineData("vorbis")]
    [InlineData("wmav2")]
    public void LossyCodec_IsALossyFormat_WhateverTheSpectrumSays(string codec)
    {
        Assert.Equal(LosslessStatus.LossyFormat, LosslessAssessor.Assess(Info(codec), Spectral(SpectralVerdict.Lossless, 0.90)).Status);
        Assert.Equal(LosslessStatus.LossyFormat, LosslessAssessor.Assess(Info(codec), null).Status);
    }

    [Theory]
    [InlineData(SpectralVerdict.Lossy, 0.95, LosslessStatus.FakeLossless)]
    [InlineData(SpectralVerdict.Lossy, 0.75, LosslessStatus.FakeLossless)]
    [InlineData(SpectralVerdict.Lossy, 0.57, LosslessStatus.Suspect)]
    [InlineData(SpectralVerdict.Lossless, 0.90, LosslessStatus.Authentic)]
    [InlineData(SpectralVerdict.Lossless, 0.45, LosslessStatus.Authentic)]
    [InlineData(SpectralVerdict.Inconclusive, 0.30, LosslessStatus.Inconclusive)]
    public void LosslessCodec_FollowsTheSpectralVerdict(SpectralVerdict verdict, double confidence, LosslessStatus expected)
    {
        LosslessAssessment assessment = LosslessAssessor.Assess(Info("flac"), Spectral(verdict, confidence, "MP3 ≈320 kbps"));

        Assert.Equal(expected, assessment.Status);
    }

    [Fact]
    public void FakeLossless_NamesTheLikelySource()
    {
        LosslessAssessment assessment = LosslessAssessor.Assess(Info("flac"), Spectral(SpectralVerdict.Lossy, 0.95, "MP3 ≈128 kbps"));

        Assert.Contains("MP3 ≈128 kbps", assessment.Explanation);
    }

    [Fact]
    public void LosslessCodec_WithoutASpectrum_CannotBeVerified()
    {
        Assert.Equal(LosslessStatus.Error, LosslessAssessor.Assess(Info("flac"), null).Status);
    }

    [Theory]
    [InlineData(24, 96000, true)]
    [InlineData(24, 88200, true)]
    [InlineData(16, 96000, false)]
    [InlineData(24, 48000, false)]
    public void HiRes_NeedsBoth24BitsAnd88kHz(int bits, int sampleRate, bool expected)
    {
        LosslessAssessment assessment = LosslessAssessor.Assess(Info("flac", bits, sampleRate), Spectral(SpectralVerdict.Lossless, 0.90));

        Assert.Equal(LosslessStatus.Authentic, assessment.Status);
        Assert.Equal(expected, assessment.IsHiRes);
    }

    [Theory]
    [InlineData("flac", true)]
    [InlineData("alac", true)]
    [InlineData("ape", true)]
    [InlineData("wavpack", true)]
    [InlineData("pcm_s16le", true)]
    [InlineData("pcm_s24be", true)]
    [InlineData("mp3", false)]
    [InlineData("aac", false)]
    public void Codecs_KnowWhichAreLossless(string codec, bool expected)
    {
        Assert.Equal(expected, Codecs.IsLossless(codec));
    }

    private static AudioInfo Info(string codec, int bits = 16, int sampleRate = 44100) =>
        new() { CodecName = codec, BitsPerSample = bits, SampleRate = sampleRate };

    private static SpectralAnalysis Spectral(SpectralVerdict verdict, double confidence, string likelySource = "") =>
        new() { Verdict = verdict, CutoffConfidence = confidence, LikelySource = likelySource };
}
