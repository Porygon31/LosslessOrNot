using System.Globalization;
using LosslessOrNot.Core;
using LosslessOrNot.Core.Media;

namespace LosslessOrNot.Tests;

/// <summary>
/// ffmpeg and ffprobe always speak with a '.' decimal separator. These pin down
/// that nothing exchanged with them depends on the machine's locale — on a French
/// system the current culture would turn 45.00 into "45,00".
/// </summary>
public sealed class CultureTests
{
    private const string FlacProbe = """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "flac",
              "codec_long_name": "FLAC (Free Lossless Audio Codec)",
              "codec_type": "audio",
              "sample_fmt": "s32",
              "sample_rate": "96000",
              "channels": 2,
              "bits_per_sample": 0,
              "bits_per_raw_sample": "24"
            }
          ],
          "format": { "format_name": "flac", "duration": "245.120000", "bit_rate": "3012345" }
        }
        """;

    private const string Mp3Probe = """
        {
          "streams": [
            { "codec_name": "mp3", "sample_fmt": "fltp", "sample_rate": "44100", "channels": 2,
              "bits_per_sample": 0, "bit_rate": "320000", "duration": "200.5" }
          ],
          "format": { "format_name": "mp3" }
        }
        """;

    [Fact]
    public void ProbeOutput_IsParsedWithTheInvariantCulture()
    {
        AudioInfo info = UnderFrenchCulture(() => AudioProbe.Parse(FlacProbe));

        Assert.Equal("flac", info.CodecName);
        Assert.Equal(96000, info.SampleRate);
        Assert.Equal(24, info.BitsPerSample);
        Assert.Equal(2, info.Channels);
        Assert.Equal(3012345, info.BitRate);
        Assert.Equal(245.12, info.DurationSec, precision: 6);
        Assert.True(info.IsLosslessCodec);
    }

    [Fact]
    public void ProbeOutput_FallsBackToStreamLevelValues()
    {
        AudioInfo info = UnderFrenchCulture(() => AudioProbe.Parse(Mp3Probe));

        Assert.Equal(0, info.BitsPerSample);
        Assert.Equal(320000, info.BitRate);
        Assert.Equal(200.5, info.DurationSec, precision: 6);
        Assert.False(info.IsLosslessCodec);
    }

    [Fact]
    public void ProbeOutput_WithoutAnAudioStream_IsAnError()
    {
        Assert.Throws<AudioAnalysisException>(() => AudioProbe.Parse("""{ "streams": [], "format": {} }"""));
        Assert.Throws<AudioAnalysisException>(() => AudioProbe.Parse("not json"));
    }

    [Fact]
    public void DecoderArguments_UseADotDecimalSeparator()
    {
        string[] args = UnderFrenchCulture(() => PcmDecoder.BuildArguments(@"C:\Musique\Été 85\01.flac", 44100, 61.25, 45));

        Assert.Equal("61.25", ValueAfter(args, "-ss"));
        Assert.Equal("45.00", ValueAfter(args, "-t"));
        Assert.Equal("44100", ValueAfter(args, "-ar"));
        Assert.Equal(@"C:\Musique\Été 85\01.flac", ValueAfter(args, "-i"));
    }

    [Fact]
    public void Evidence_IsWrittenInFrench_WhateverTheMachineCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("16,5 kHz", TextFormat.Fr($"{16.5:0.0} kHz"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static string ValueAfter(string[] args, string option) => args[Array.IndexOf(args, option) + 1];

    private static T UnderFrenchCulture<T>(Func<T> action)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
