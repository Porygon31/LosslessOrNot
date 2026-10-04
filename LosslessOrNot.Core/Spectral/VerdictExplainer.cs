using static LosslessOrNot.Core.TextFormat;

namespace LosslessOrNot.Core.Spectral;

/// <summary>What <see cref="VerdictEngine"/> measured, handed over to be explained.</summary>
internal readonly record struct Measurements(
    double ContentTopHz,
    double WallHz,
    double WallDropDb,
    double FloorAboveDb,
    double NyquistHz,
    int Frames,
    double Confidence,
    SpectralVerdict Verdict,
    string LikelySource);

/// <summary>
/// Spells out, for someone who does not read spectra, what each measurement means,
/// the rule applied to it, and how the three rules add up to the verdict.
/// </summary>
internal static class VerdictExplainer
{
    /// <summary>
    /// The three tests a lossy encoder's wall must pass, in the order the verdict
    /// applies them. They are all ✘ exactly when the verdict is "lossy".
    /// </summary>
    public static IReadOnlyList<AnalysisPoint> Criteria(Measurements m)
    {
        bool hasWall = m.WallHz > 0;
        bool steep = hasWall && m.WallDropDb >= VerdictEngine.WallDropMinDb;
        // Where the steepest drop sits, and what lies above it, only mean something
        // when that drop is a wall; otherwise they describe the least flat kHz.
        string notApplicable = steep ? "" : " (sans objet : pas de coupure nette)";

        var abrupt = new AnalysisPoint(
            "Coupure nette",
            hasWall ? Fr($"{m.WallDropDb:0.0} dB perdus en 1 kHz") : "aucune baisse mesurable",
            "La plus forte baisse de niveau mesurée sur une tranche de 1 kHz, au-delà de 8 kHz. Un encodeur avec perte "
            + "(MP3, AAC…) coupe les aigus d'un coup ; un vrai enregistrement s'atténue progressivement, à cause des "
            + "micros, du mixage ou du mastering.",
            Fr($"{VerdictEngine.WallDropMinDb:0} dB ou plus en 1 kHz : coupure typique d'un encodeur."),
            !hasWall ? PointSignal.Info : steep ? PointSignal.LossyLike : PointSignal.LosslessLike);

        var position = new AnalysisPoint(
            "Position de la coupure",
            hasWall ? Fr($"{m.WallHz / 1000:0.00} kHz") + notApplicable : "—",
            "Les encodeurs grand public placent leur coupure entre ~11 et 20,5 kHz selon le débit : vers 16 kHz pour un "
            + "MP3 à 128 kbps, vers 20 kHz à 320 kbps. Une coupure plus haute — celle d'un fichier 96 kHz "
            + "suréchantillonné, vers 24 kHz — ne vient pas d'un encodeur avec perte.",
            Fr($"{VerdictEngine.CodecMaxWallHz / 1000:0} kHz ou moins : zone où coupent les encodeurs avec perte."),
            !steep ? PointSignal.Info
                : m.WallHz <= VerdictEngine.CodecMaxWallHz ? PointSignal.LossyLike : PointSignal.LosslessLike);

        var floor = new AnalysisPoint(
            "Niveau au-dessus de la coupure",
            hasWall ? Fr($"{m.FloorAboveDb:0} dB") + notApplicable : "—",
            "Le niveau qui reste juste au-dessus de la coupure, comparé au plus fort du spectre. Un encodeur efface tout "
            + "au-dessus de sa coupure : il n'y reste qu'un silence numérique. Une atténuation naturelle, même très "
            + "raide, laisse du bruit de fond.",
            Fr($"{VerdictEngine.CodecFloorDb:0} dB ou moins : silence numérique laissé par un encodeur."),
            !steep ? PointSignal.Info
                : m.FloorAboveDb <= VerdictEngine.CodecFloorDb ? PointSignal.LossyLike : PointSignal.LosslessLike);

        return [abrupt, position, floor];
    }

    /// <summary>Everything else the verdict rests on or is limited by.</summary>
    public static IReadOnlyList<AnalysisPoint> Observations(Measurements m, AnalysisScope scope)
    {
        var points = new List<AnalysisPoint>();
        if (scope.IsShorterThanRequested)
        {
            bool fileIsShort = scope.FileDurationSec > 0 && scope.FileDurationSec < scope.RequestedSec;
            points.Add(new AnalysisPoint(
                "Durée du fichier",
                fileIsShort
                    ? Fr($"{scope.FileDurationSec:0.0} s, moins que les {scope.RequestedSec:0} s demandées")
                    : Fr($"{scope.AnalyzedSec:0.0} s décodées sur les {scope.RequestedSec:0} s demandées"),
                fileIsShort
                    ? "Le fichier est plus court que la durée d'analyse choisie : il a été analysé en entier. Le résultat "
                      + "reste valable, mais il repose sur moins d'audio."
                    : "Le décodage s'est arrêté avant la durée demandée, le fichier finissant plus tôt que prévu : "
                      + "l'analyse porte sur l'audio disponible.",
                "",
                PointSignal.Warning));
        }

        points.Add(new AnalysisPoint(
            "Étendue du contenu",
            Fr($"jusqu'à {m.ContentTopHz / 1000:0.00} kHz (Nyquist : {m.NyquistHz / 1000:0.00} kHz)"),
            "La fréquence la plus haute où il reste du son, à moins de 70 dB sous le maximum : c'est là que le "
            + "spectrogramme devient noir. Au-delà de 20,5 kHz, aucun encodeur avec perte courant ne laisse passer de "
            + "contenu. En dessous, ce n'est pas une preuve : un enregistrement peut simplement manquer d'aigus.",
            Fr($"plus de {VerdictEngine.LosslessTopHz / 1000:0.0} kHz : hors de portée des encodeurs avec perte."),
            m.ContentTopHz >= VerdictEngine.LosslessTopHz ? PointSignal.LosslessLike : PointSignal.Info));

        bool fewFrames = m.Frames < VerdictEngine.MinFramesForFullConfidence;
        string scale = m.Verdict switch
        {
            SpectralVerdict.Lossy =>
                "Plus la coupure est nette, plus la signature est sûre : 95 % à partir de 35 dB perdus en 1 kHz, "
                + "85 % à partir de 27 dB, 75 % au-delà de 20 dB.",
            SpectralVerdict.Lossless => "90 % quand le contenu dépasse 21,5 kHz, 75 % entre 20,5 et 21,5 kHz.",
            _ => "30 % : l'analyse ne permet pas de trancher.",
        };
        points.Add(new AnalysisPoint(
            "Confiance",
            Fr($"{m.Confidence:P0}"),
            fewFrames ? scale + " Elle est réduite de 40 % parce que moins de 60 trames ont pu être analysées." : scale,
            "",
            fewFrames ? PointSignal.Warning : PointSignal.Info));

        points.Add(new AnalysisPoint(
            "Audio analysé",
            Fr($"{scope.AnalyzedSec:0.0} s, de {scope.StartSec:0.0} s à {scope.EndSec:0.0} s ({m.Frames} trames)"),
            "L'extrait est découpé en trames de 4096 échantillons qui se chevauchent de moitié ; leurs spectres sont "
            + "moyennés et les passages silencieux ignorés. Plus il y a de trames, plus la mesure est stable. La durée "
            + "analysée se règle dans la barre d'outils.",
            Fr($"{VerdictEngine.MinFramesForFullConfidence} trames ou plus (≈ 3 s à 44,1 kHz) pour une confiance pleine."),
            fewFrames ? PointSignal.Warning : PointSignal.Info));

        if (m.NyquistHz > 25000)
        {
            points.Add(new AnalysisPoint(
                "Fichier haute résolution",
                Fr($"Nyquist : {m.NyquistHz / 1000:0.0} kHz"),
                "À cette fréquence d'échantillonnage, la bande au-delà de ~24 kHz est normalement vide : c'est une "
                + "propriété des micros et du mastering, pas de l'encodage. Ce vide n'est donc pas un indice de "
                + "transcodage — c'est pourquoi les seuils sont des fréquences fixes et non des fractions de Nyquist.",
                "",
                PointSignal.Info));
        }
        return points;
    }

    /// <summary>One sentence tying the criteria and the content reach to the verdict.</summary>
    public static string Conclusion(Measurements m, IReadOnlyList<AnalysisPoint> criteria)
    {
        int met = criteria.Count(point => point.Signal == PointSignal.LossyLike);
        string reach = Fr($"{m.ContentTopHz / 1000:0.00} kHz");
        string top = Fr($"{VerdictEngine.LosslessTopHz / 1000:0.0} kHz");
        string partial = met == 1 ? "1 critère sur 3 seulement" : $"{met} critères sur 3 seulement";
        return m.Verdict switch
        {
            SpectralVerdict.Lossy =>
                $"Les 3 critères sont réunis : c'est la signature d'un encodeur avec perte (source probable : {m.LikelySource}).",
            SpectralVerdict.Lossless when met == 0 =>
                $"Aucun critère d'encodeur avec perte n'est réuni, et le contenu monte jusqu'à {reach}, au-delà des {top} "
                + "qu'aucun encodeur avec perte ne dépasse : cohérent avec un vrai lossless.",
            SpectralVerdict.Lossless =>
                $"{partial} (il en faut 3), et le contenu monte jusqu'à {reach}, au-delà des {top} qu'aucun encodeur "
                + "avec perte ne dépasse : cohérent avec un vrai lossless.",
            _ when met == 0 =>
                $"Aucun critère d'encodeur avec perte n'est réuni, mais le contenu s'arrête à {reach}, sous les {top} qui "
                + "prouveraient un vrai lossless : impossible de trancher.",
            _ =>
                $"{partial} (il en faut 3 pour conclure à un encodeur avec perte), et le contenu s'arrête à {reach}, sous "
                + $"les {top} qui prouveraient un vrai lossless : impossible de trancher.",
        };
    }
}
