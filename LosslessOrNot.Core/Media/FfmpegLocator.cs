namespace LosslessOrNot.Core.Media;

/// <summary>The two tools the analysis needs.</summary>
public sealed record FfmpegTools(string FfmpegPath, string FfprobePath)
{
    /// <summary>The folder ffmpeg was found in, for display.</summary>
    public string Folder => Path.GetDirectoryName(FfmpegPath) ?? FfmpegPath;
}

/// <summary>
/// Finds ffmpeg and ffprobe: an explicitly configured location first (the exe
/// itself or its folder), then the application folder, then the PATH.
/// </summary>
public static class FfmpegLocator
{
    /// <returns>Null unless both ffmpeg and ffprobe were found.</returns>
    public static FfmpegTools? Locate(string? configuredPath = null)
    {
        string[] folders = CandidateFolders(configuredPath).ToArray();
        string? ffmpeg = Find(folders, "ffmpeg");
        string? ffprobe = Find(folders, "ffprobe");
        return ffmpeg is not null && ffprobe is not null ? new FfmpegTools(ffmpeg, ffprobe) : null;
    }

    private static IEnumerable<string> CandidateFolders(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string configured = configuredPath.Trim().Trim('"');
            if (File.Exists(configured))
            {
                yield return Path.GetDirectoryName(Path.GetFullPath(configured)) ?? configured;
            }
            else if (Directory.Exists(configured))
            {
                yield return configured;
                // The usual Windows builds unpack to <folder>\bin\ffmpeg.exe.
                yield return Path.Combine(configured, "bin");
            }
        }

        string app = AppContext.BaseDirectory;
        yield return app;
        yield return Path.Combine(app, "ffmpeg");
        yield return Path.Combine(app, "ffmpeg", "bin");

        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string folder in path.Split(
            Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return folder.Trim('"');
        }
    }

    private static string? Find(IEnumerable<string> folders, string name)
    {
        string fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (string folder in folders)
        {
            try
            {
                string candidate = Path.Combine(folder, fileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                // A malformed PATH entry: skip it.
            }
        }
        return null;
    }
}
