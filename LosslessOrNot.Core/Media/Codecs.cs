namespace LosslessOrNot.Core.Media;

/// <summary>Knows which ffmpeg codecs are lossless by construction, and what to call them.</summary>
public static class Codecs
{
    // ffmpeg decoder names. Anything else — mp3, aac, vorbis, opus, wmav2, ac3,
    // musepack… — is a lossy format.
    private static readonly HashSet<string> LosslessCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "flac", "alac", "ape", "wavpack", "tta", "tak", "shorten", "mp4als", "ralf",
        "mlp", "truehd", "wmalossless", "dst",
    };

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flac"] = "FLAC",
        ["alac"] = "ALAC",
        ["ape"] = "Monkey's Audio",
        ["wavpack"] = "WavPack",
        ["tta"] = "TTA",
        ["tak"] = "TAK",
        ["mlp"] = "MLP",
        ["truehd"] = "TrueHD",
        ["wmalossless"] = "WMA Lossless",
        ["mp3"] = "MP3",
        ["mp2"] = "MP2",
        ["aac"] = "AAC",
        ["vorbis"] = "Vorbis",
        ["opus"] = "Opus",
        ["wmav1"] = "WMA",
        ["wmav2"] = "WMA",
        ["wmapro"] = "WMA Pro",
        ["ac3"] = "AC-3",
        ["eac3"] = "E-AC-3",
        ["dts"] = "DTS",
        ["musepack7"] = "Musepack",
        ["musepack8"] = "Musepack",
    };

    /// <summary>True for codecs that are lossless by construction, PCM included.</summary>
    public static bool IsLossless(string codecName) =>
        codecName.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase) || LosslessCodecs.Contains(codecName);

    /// <summary>A short display name such as "FLAC", "PCM" or "MP3".</summary>
    public static string DisplayName(string codecName)
    {
        if (string.IsNullOrEmpty(codecName))
        {
            return "?";
        }
        if (codecName.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase))
        {
            return "PCM";
        }
        return DisplayNames.TryGetValue(codecName, out string? name) ? name : codecName.ToUpperInvariant();
    }
}
