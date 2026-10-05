namespace LosslessOrNot.Calibration;

/// <summary>One way of making a fake: a lossy encode, then a decode back into FLAC.</summary>
/// <param name="Name">Short identifier, used in the CSV and the report.</param>
/// <param name="Encoder">The ffmpeg encoder, which must be present in the build.</param>
/// <param name="FlacArgs">How the decode is written back: sample format, dither…</param>
/// <param name="NeedsShapingRate">Noise-shaped dither only exists for 44.1 and 48 kHz.</param>
internal sealed record Recipe(
    string Name,
    string Encoder,
    string[] EncodeArgs,
    string Extension,
    string[] FlacArgs,
    bool NeedsShapingRate = false)
{
    private static readonly string[] Plain16 = ["-sample_fmt", "s16"];
    private static readonly string[] Triangular16 = ["-af", "aresample=osf=s16:dither_method=triangular", "-sample_fmt", "s16"];
    private static readonly string[] Shaped16 = ["-af", "aresample=osf=s16:dither_method=shibata", "-sample_fmt", "s16"];
    private static readonly string[] Plain24 = ["-sample_fmt", "s32", "-bits_per_raw_sample", "24"];

    /// <summary>
    /// The encoders people actually transcode from, at their default low-pass, and
    /// the decodes that hide a transcode best: with dither, with noise shaping, in 24 bits.
    /// </summary>
    public static IReadOnlyList<Recipe> All { get; } =
    [
        new("mp3-128", "libmp3lame", ["-b:a", "128k"], "mp3", Plain16),
        new("mp3-192", "libmp3lame", ["-b:a", "192k"], "mp3", Plain16),
        new("mp3-256", "libmp3lame", ["-b:a", "256k"], "mp3", Plain16),
        new("mp3-320", "libmp3lame", ["-b:a", "320k"], "mp3", Plain16),
        new("mp3-v0", "libmp3lame", ["-q:a", "0"], "mp3", Plain16),
        new("mp3-v2", "libmp3lame", ["-q:a", "2"], "mp3", Plain16),
        new("mp3-v5", "libmp3lame", ["-q:a", "5"], "mp3", Plain16),
        new("aac-128", "aac", ["-b:a", "128k"], "m4a", Plain16),
        new("aac-256", "aac", ["-b:a", "256k"], "m4a", Plain16),
        new("vorbis-q3", "libvorbis", ["-q:a", "3"], "ogg", Plain16),
        new("vorbis-q6", "libvorbis", ["-q:a", "6"], "ogg", Plain16),
        new("vorbis-q9", "libvorbis", ["-q:a", "9"], "ogg", Plain16),
        new("opus-96", "libopus", ["-b:a", "96k"], "opus", Plain16),
        new("opus-160", "libopus", ["-b:a", "160k"], "opus", Plain16),
        new("mp3-192-tpdf", "libmp3lame", ["-b:a", "192k"], "mp3", Triangular16),
        new("mp3-320-tpdf", "libmp3lame", ["-b:a", "320k"], "mp3", Triangular16),
        new("mp3-192-shaped", "libmp3lame", ["-b:a", "192k"], "mp3", Shaped16, NeedsShapingRate: true),
        new("mp3-320-shaped", "libmp3lame", ["-b:a", "320k"], "mp3", Shaped16, NeedsShapingRate: true),
        new("mp3-192-24bit", "libmp3lame", ["-b:a", "192k"], "mp3", Plain24),
        new("aac-256-24bit", "aac", ["-b:a", "256k"], "m4a", Plain24),
    ];

    public bool Applies(int sampleRate) => !NeedsShapingRate || sampleRate is 44100 or 48000;
}
