using LosslessOrNot.Core.Media;
using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Core.Assessment;

/// <summary>
/// Combines the codec a file claims (from ffprobe) with the spectral verdict.
/// </summary>
/// <remarks>
/// Inspired by Antra's analyzerQualityBadge, minus its bitrate heuristics: they are
/// crude enough to brand a genuine quiet or mono master as fake. Wrongly branding a
/// real master is the worse error, so uncertainty is reported as uncertainty.
/// </remarks>
public static class LosslessAssessor
{
    /// <summary>Below this confidence a lossy-looking spectrum is reported as suspect, not asserted.</summary>
    public const double FakeLosslessMinConfidence = 0.75;

    public static LosslessAssessment Assess(AudioInfo info, SpectralAnalysis? spectral)
    {
        string codec = Codecs.DisplayName(info.CodecName);
        if (!info.IsLosslessCodec)
        {
            return new LosslessAssessment(
                LosslessStatus.LossyFormat,
                "Format avec perte",
                $"Le fichier est encodé en {codec}, un format avec perte : il ne peut pas être lossless, quel que soit son spectre.");
        }
        if (spectral is null)
        {
            return Failed("L'analyse spectrale n'a pas pu être menée : impossible de vérifier ce fichier.");
        }

        switch (spectral.Verdict)
        {
            case SpectralVerdict.Lossy when spectral.CutoffConfidence >= FakeLosslessMinConfidence:
                return new LosslessAssessment(
                    LosslessStatus.FakeLossless,
                    "Faux lossless",
                    $"Encodé en {codec}, mais son spectre porte le mur d'un encodeur avec perte : "
                    + $"probablement transcodé depuis {spectral.LikelySource}.");

            case SpectralVerdict.Lossy:
                return new LosslessAssessment(
                    LosslessStatus.Suspect,
                    "Suspect",
                    $"Le spectre ressemble à celui d'un encodeur avec perte ({spectral.LikelySource}), "
                    + "mais trop peu d'audio a pu être analysé pour l'affirmer.");

            case SpectralVerdict.Lossless:
                bool hiRes = info.BitsPerSample >= 24 && info.SampleRate >= 88200;
                return new LosslessAssessment(
                    LosslessStatus.Authentic,
                    hiRes ? "Lossless Hi-Res" : "Lossless authentique",
                    $"Encodé en {codec}, avec du contenu au-delà de ce que laisse passer un encodeur avec perte : "
                    + "cohérent avec un vrai master lossless.",
                    hiRes);

            default:
                return new LosslessAssessment(
                    LosslessStatus.Inconclusive,
                    "Non concluant",
                    $"Encodé en {codec}, mais le spectre ne permet pas de trancher : peu d'aigus, "
                    + "sans le mur net d'un encodeur avec perte.");
        }
    }

    public static LosslessAssessment Failed(string message) => new(LosslessStatus.Error, "Erreur", message);
}
