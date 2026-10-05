namespace LosslessOrNot.Core.Media;

/// <summary>Which files are worth probing when a folder is scanned.</summary>
public static class AudioFiles
{
    /// <summary>
    /// Extensions picked up when a folder is added. They only decide what is worth
    /// probing: ffprobe has the final word on what a file really contains.
    /// </summary>
    public static IReadOnlySet<string> Extensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".flac", ".wav", ".aif", ".aiff", ".aifc", ".m4a", ".alac", ".ape", ".wv", ".tta",
        ".mp3", ".aac", ".ogg", ".oga", ".opus", ".wma",
    };

    public static bool HasAudioExtension(string path) => Extensions.Contains(Path.GetExtension(path));
}
