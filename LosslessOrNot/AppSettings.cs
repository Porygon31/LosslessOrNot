using System.Text.Json;
using System.Text.Json.Serialization;
using LosslessOrNot.Charts;

namespace LosslessOrNot;

/// <summary>User preferences, kept as JSON in %APPDATA%\LosslessOrNot\settings.json.</summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>ffmpeg.exe or the folder holding it; null to search the usual places.</summary>
    public string? FfmpegPath { get; set; }

    public SpectrogramPalette Palette { get; set; } = SpectrogramPalette.Magma;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LosslessOrNot", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable or corrupt settings file must never stop the app from starting.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; failing to save them is not worth an error.
        }
    }
}
