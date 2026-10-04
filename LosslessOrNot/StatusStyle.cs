using LosslessOrNot.Core.Assessment;

namespace LosslessOrNot;

/// <summary>How each status looks: a tinted background for its list cell, a contrasting colour for its text.</summary>
internal static class StatusStyle
{
    public static (Color Back, Color Fore) For(LosslessAssessment assessment) =>
        ThemeColors.IsDark ? Dark(assessment) : Classic(assessment);

    // Pale cells with dark text on the light theme…
    private static (Color Back, Color Fore) Classic(LosslessAssessment assessment) => assessment switch
    {
        { Status: LosslessStatus.Authentic, IsHiRes: true } => (Color.FromArgb(235, 228, 252), Color.FromArgb(81, 45, 168)),
        { Status: LosslessStatus.Authentic } => (Color.FromArgb(221, 244, 225), Color.FromArgb(27, 94, 32)),
        { Status: LosslessStatus.FakeLossless } => (Color.FromArgb(253, 222, 222), Color.FromArgb(183, 28, 28)),
        { Status: LosslessStatus.Suspect } => (Color.FromArgb(255, 232, 210), Color.FromArgb(191, 80, 0)),
        { Status: LosslessStatus.Inconclusive } => (Color.FromArgb(255, 245, 204), Color.FromArgb(122, 96, 0)),
        { Status: LosslessStatus.LossyFormat } => (Color.FromArgb(227, 234, 242), Color.FromArgb(55, 71, 79)),
        _ => (Color.FromArgb(238, 238, 238), Color.FromArgb(120, 20, 20)),
    };

    // …and deep cells with light text on the dark one, where the pale ones would glare.
    private static (Color Back, Color Fore) Dark(LosslessAssessment assessment) => assessment switch
    {
        { Status: LosslessStatus.Authentic, IsHiRes: true } => (Color.FromArgb(52, 40, 88), Color.FromArgb(196, 176, 255)),
        { Status: LosslessStatus.Authentic } => (Color.FromArgb(28, 62, 38), Color.FromArgb(132, 222, 152)),
        { Status: LosslessStatus.FakeLossless } => (Color.FromArgb(92, 30, 30), Color.FromArgb(255, 150, 150)),
        { Status: LosslessStatus.Suspect } => (Color.FromArgb(92, 54, 18), Color.FromArgb(255, 184, 112)),
        { Status: LosslessStatus.Inconclusive } => (Color.FromArgb(80, 70, 18), Color.FromArgb(240, 214, 110)),
        { Status: LosslessStatus.LossyFormat } => (Color.FromArgb(40, 52, 64), Color.FromArgb(176, 196, 216)),
        _ => (Color.FromArgb(64, 40, 40), Color.FromArgb(236, 150, 150)),
    };

    /// <summary>Sort order of the status column: the files that need attention first.</summary>
    public static int Rank(TrackEntry entry) => entry.Result?.Assessment.Status switch
    {
        LosslessStatus.FakeLossless => 0,
        LosslessStatus.Suspect => 1,
        LosslessStatus.Inconclusive => 2,
        LosslessStatus.Error => 3,
        LosslessStatus.LossyFormat => 4,
        LosslessStatus.Authentic => 5,
        _ => 6,
    };
}
