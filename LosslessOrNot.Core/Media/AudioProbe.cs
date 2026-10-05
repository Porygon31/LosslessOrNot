using System.Globalization;
using System.Text.Json;

namespace LosslessOrNot.Core.Media;

/// <summary>What ffprobe reports about the first audio stream of a file.</summary>
public sealed record AudioInfo
{
    /// <summary>The ffmpeg codec name, e.g. "flac", "alac", "mp3", "pcm_s24le".</summary>
    public required string CodecName { get; init; }

    public string CodecLongName { get; init; } = "";

    /// <summary>The container as ffprobe names it, e.g. "flac" or "mov,mp4,m4a,3gp,3g2,mj2".</summary>
    public string FormatName { get; init; } = "";

    public int SampleRate { get; init; }

    public int Channels { get; init; }

    /// <summary>Bit depth; 0 for codecs where it means nothing (MP3, AAC…).</summary>
    public int BitsPerSample { get; init; }

    /// <summary>
    /// The depth of integer samples, which sets the rounding noise the format carries;
    /// 0 for floating-point PCM and when unknown.
    /// </summary>
    public int IntegerBitsPerSample =>
        CodecName.StartsWith("pcm_f", StringComparison.OrdinalIgnoreCase) || BitsPerSample is < 8 or > 32
            ? 0
            : BitsPerSample;

    /// <summary>Bits per second; 0 when unknown.</summary>
    public long BitRate { get; init; }

    public double DurationSec { get; init; }

    /// <summary>
    /// True when the codec is lossless by construction — that is, when the file
    /// claims to be lossless. The codec decides, not the extension: an .m4a can
    /// hold AAC as well as ALAC.
    /// </summary>
    public bool IsLosslessCodec => Codecs.IsLossless(CodecName);
}

/// <summary>Reads stream metadata with ffprobe.</summary>
public static class AudioProbe
{
    public static async Task<AudioInfo> ProbeAsync(
        string ffprobeExe, string filePath, CancellationToken cancellationToken = default)
    {
        ProcessRunner.Result result = await ProcessRunner.RunAsync(
            ffprobeExe,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-select_streams", "a:0", filePath],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            string why = ProcessRunner.FirstLine(result.StdErr);
            throw new AudioAnalysisException(
                why.Length > 0 ? $"ffprobe : {why}" : $"ffprobe a échoué (code {result.ExitCode}).");
        }
        return Parse(result.StdOut);
    }

    internal static AudioInfo Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("streams", out JsonElement streams)
                || streams.ValueKind != JsonValueKind.Array
                || streams.GetArrayLength() == 0)
            {
                throw new AudioAnalysisException("Aucune piste audio dans ce fichier.");
            }
            JsonElement stream = streams[0];
            JsonElement format = root.TryGetProperty("format", out JsonElement f) ? f : default;

            // FLAC and ALAC report their depth in bits_per_raw_sample (they decode to
            // s32 even for 24-bit), PCM in bits_per_sample.
            int bits = GetInt(stream, "bits_per_raw_sample");
            if (bits <= 0)
            {
                bits = GetInt(stream, "bits_per_sample");
            }
            long bitRate = GetLong(stream, "bit_rate");
            if (bitRate <= 0)
            {
                bitRate = GetLong(format, "bit_rate");
            }
            double duration = GetDouble(format, "duration");
            if (duration <= 0)
            {
                duration = GetDouble(stream, "duration");
            }

            return new AudioInfo
            {
                CodecName = GetString(stream, "codec_name"),
                CodecLongName = GetString(stream, "codec_long_name"),
                FormatName = GetString(format, "format_name"),
                SampleRate = GetInt(stream, "sample_rate"),
                Channels = GetInt(stream, "channels"),
                BitsPerSample = bits,
                BitRate = bitRate,
                DurationSec = duration,
            };
        }
        catch (JsonException ex)
        {
            throw new AudioAnalysisException("Réponse de ffprobe illisible.", ex);
        }
    }

    // ffprobe prints most numbers as JSON strings ("44100", "245.123000"), always
    // with a '.' decimal separator, so they are parsed with the invariant culture.
    // Under a French locale the current culture would read "245.123" as 245123.
    private static double GetDouble(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return 0;
        }
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => 0,
        };
    }

    private static long GetLong(JsonElement element, string name) => (long)GetDouble(element, name);

    private static int GetInt(JsonElement element, string name) => (int)GetDouble(element, name);

    private static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
