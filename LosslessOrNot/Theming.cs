using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot;

/// <summary>The two looks the user can choose from.</summary>
public enum AppTheme
{
    Classic,
    Dark,
}

/// <summary>
/// Colours the application picks itself, in a variant for each theme. Standard
/// controls follow the theme on their own once Application.SetColorMode has run;
/// these are the colours they cannot know about.
/// </summary>
internal static class ThemeColors
{
    /// <summary>True when the dark theme is really in effect: it needs Windows 11 and no high contrast.</summary>
    public static bool IsDark => Application.IsDarkModeEnabled;

    public static Color Problem => IsDark ? Color.FromArgb(255, 128, 128) : Color.Firebrick;

    public static Color For(PointSignal signal) => (signal, IsDark) switch
    {
        (PointSignal.LossyLike, false) => Color.FromArgb(198, 40, 40),
        (PointSignal.LossyLike, true) => Color.FromArgb(255, 128, 128),
        (PointSignal.LosslessLike, false) => Color.FromArgb(46, 125, 50),
        (PointSignal.LosslessLike, true) => Color.FromArgb(120, 214, 140),
        (PointSignal.Warning, false) => Color.FromArgb(191, 110, 0),
        (PointSignal.Warning, true) => Color.FromArgb(255, 190, 90),
        (_, false) => Color.FromArgb(25, 103, 210),
        (_, true) => Color.FromArgb(128, 182, 255),
    };

    public static string Symbol(PointSignal signal) => signal switch
    {
        PointSignal.LossyLike => "✘",
        PointSignal.LosslessLike => "✔",
        PointSignal.Warning => "⚠",
        _ => "ℹ",
    };
}
