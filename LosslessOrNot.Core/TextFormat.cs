using System.Globalization;

namespace LosslessOrNot.Core;

/// <summary>
/// Culture for every user-facing number. The interface is French-only, so evidence
/// must read "16,0 kHz" whatever the machine's regional settings. Command lines
/// handed to ffmpeg must NOT use it: they use the invariant culture.
/// </summary>
public static class TextFormat
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Formats an interpolated string with <see cref="Culture"/>.</summary>
    public static string Fr(FormattableString text) => text.ToString(Culture);
}
