using static LosslessOrNot.Core.TextFormat;

namespace LosslessOrNot.Core.Spectral;

/// <summary>What <see cref="VerdictEngine"/> concluded, handed over to be explained.</summary>
internal sealed record Judgement(SpectralVerdict Verdict, double Confidence, string LikelySource, Score Score);

/// <summary>
/// Spells out, for someone who does not read spectra, what each measurement means,
/// the rule applied to it, what it weighs, and how it all adds up to the verdict.
/// </summary>
internal static class VerdictExplainer
{
    private const string NotApplicable = " (sans objet : pas de coupure nette)";
    private const string OutOfReach = " (sans objet : coupure au-delà de 21 kHz, hors de portée des encodeurs)";

    /// <summary>
    /// The three tests a lossy encoder's wall must pass, in the order the verdict
    /// applies them. A lossy verdict needs all three ✘ — and a probability of at
    /// least 50 %, which contrary indicators can deny it.
    /// </summary>
    public static IReadOnlyList<AnalysisPoint> Criteria(Measurements m, Score s)
    {
        bool hasWall = m.WallHz > 0;
        bool steep = m.IsSteep;
        // Where the steepest drop sits, and what lies above it, only mean something
        // when that drop is a wall; otherwise they describe the least flat kHz.
        string notApplicable = steep ? "" : NotApplicable;

        var abrupt = new AnalysisPoint(
            "Coupure nette",
            hasWall ? Fr($"{m.WallDropDb:0.0} dB perdus en 1 kHz") : "aucune baisse mesurable",
            "La plus forte baisse de niveau mesurée sur une tranche de 1 kHz, au-delà de 8 kHz. Un encodeur avec perte "
            + "(MP3, AAC…) coupe les aigus d'un coup ; un vrai enregistrement s'atténue progressivement, à cause des "
            + "micros, du mixage ou du mastering.",
            Fr($"{VerdictEngine.WallDropMinDb:0} dB ou plus en 1 kHz, ou {VerdictEngine.FlatFloorWallDropMinDb:0} dB s'ils retombent sur un plancher plat : coupure typique d'un encodeur."),
            !hasWall ? PointSignal.Info : steep ? PointSignal.LossyLike : PointSignal.LosslessLike,
            s.Steepness);

        var position = new AnalysisPoint(
            "Position de la coupure",
            hasWall ? Fr($"{m.WallHz / 1000:0.00} kHz") + notApplicable : "—",
            "Les encodeurs grand public placent leur coupure entre ~11 et 20,5 kHz selon le débit : vers 16 kHz pour un "
            + "MP3 à 128 kbps, vers 19,5 kHz à 320 kbps. Sous 19 kHz, aucun vrai master mesuré ne coupe aussi net. Entre "
            + "19,1 et 21 kHz, la coupure d'un encodeur à haut débit (MP3 320 kbps, V0, AAC, Opus, Vorbis) se confond "
            + "avec le filtre d'un convertisseur de fréquence (un master produit en 48 kHz et livré en 44,1 kHz) : la "
            + "probabilité y est plafonnée et le fichier au mieux « suspect ». Une coupure plus haute — celle d'un "
            + "fichier 96 kHz suréchantillonné, vers 24 kHz — ne vient pas d'un encodeur avec perte.",
            Fr($"{VerdictEngine.CodecMaxWallHz / 1000:0} kHz ou moins : zone où coupent les encodeurs avec perte."),
            !steep ? PointSignal.Info
                : m.WallHz <= VerdictEngine.CodecMaxWallHz ? PointSignal.LossyLike : PointSignal.LosslessLike,
            steep ? s.Position : null);

        bool candidate = ConfidenceModel.IsCodecCandidate(m);
        var floor = new AnalysisPoint(
            "Ce qui reste au-dessus de la coupure",
            !hasWall ? "—"
                : candidate ? DescribeFloor(m, s)
                : Fr($"{m.FloorAboveDb:0} dB") + (steep ? OutOfReach : NotApplicable),
            "Le niveau qui reste juste au-dessus de la coupure. Un encodeur efface tout au-dessus de sa coupure : il n'y "
            + "reste qu'un silence numérique — au niveau du bruit d'arrondi du format, 16 ou 24 bits — ou, si le décodage "
            + "a ajouté du dither, un bruit constant qui ignore la musique ; un décodage en 24 bits laisse aussi voir une "
            + "image de la musique, très loin sous elle, que le décodeur laisse filtrer ; et le décodage d'un master très "
            + "fort sature, ce qui remplit les aigus d'une distorsion plate. Une atténuation naturelle, même très raide, "
            + "laisse un résidu qui monte et descend avec la musique, peu en dessous d'elle, et qui continue de décroître.",
            Fr($"{VerdictEngine.CodecFloorDb:0} dB ou moins, ou au plus +{ConfidenceModel.QuantizationMarginDb:0} dB au-dessus du bruit de quantification,")
            + Fr($" ou un bruit constant, ou une image de la musique à plus de {FrameEvidence.LeakageDepthDb:0} dB sous elle,")
            + " ou une distorsion plate — mais jamais un résidu qui la suit de près en décroissant.",
            !candidate ? PointSignal.Info
                : ConfidenceModel.IsCodecFloor(s.FloorKind) ? PointSignal.LossyLike
                : PointSignal.LosslessLike,
            candidate ? s.Floor : null);

        return [abrupt, position, floor];
    }

    private static string DescribeFloor(Measurements m, Score s)
    {
        string value = Fr($"{m.FloorAboveDb:0} dB");
        if (m.FloorAboveQuantizationDb is { } q)
        {
            value += Fr($", {q:+0;-0;0} dB par rapport au bruit de quantification {m.BitsPerSample} bits");
        }
        if (m.Wall is { HasDynamics: true } wall)
        {
            value += Fr($", à {wall.DepthDb:0} dB sous la musique, et {VerdictEngine.Following(wall.Correlation)} ses variations (r = {wall.Correlation:0.00})");
        }
        if (m.FloorSlopeDbPerKHz is { } slope)
        {
            value += Fr($", pente {slope:+0.0;-0.0;0.0} dB/kHz");
        }
        return value + s.FloorKind switch
        {
            FloorKind.DigitalSilence => " : silence numérique",
            FloorKind.StationaryNoise => " : bruit constant",
            FloorKind.Leakage => " : fuite du décodeur",
            FloorKind.Distortion => " : distorsion d'écrêtage",
            FloorKind.FollowsMusic => " : musique atténuée",
            _ => "",
        };
    }

    /// <summary>The frame-by-frame indicators that strengthen or weaken the three criteria.</summary>
    public static IReadOnlyList<AnalysisPoint> SupportingEvidence(Measurements m, Score s)
    {
        string persistenceValue;
        PointSignal persistenceSignal = PointSignal.Info;
        double? persistenceWeight = null;
        if (!ConfidenceModel.IsCodecCandidate(m))
        {
            persistenceValue = (m.IsSteep ? OutOfReach : NotApplicable).Trim(' ', '(', ')');
        }
        else if (m.Wall is not { } wall)
        {
            persistenceValue = "non mesurable : trop peu de trames, ou pas de place au-dessus de la coupure";
        }
        else
        {
            persistenceValue = Fr($"le mur tient dans {wall.Persistence:P0} des trames les plus riches en aigus");
            persistenceSignal = wall.Persistence >= 0.9 ? PointSignal.LossyLike
                : wall.Persistence < 0.6 ? PointSignal.LosslessLike
                : PointSignal.Info;
            persistenceWeight = s.Persistence;
        }
        var persistence = new AnalysisPoint(
            "Mur permanent",
            persistenceValue,
            "On prend le quart des trames où la musique est la plus riche juste sous la coupure : c'est là qu'un filtrage "
            + "naturel déborderait au-dessus. Un encodeur coupe au même endroit à chaque instant : son mur y tient "
            + Fr($"toujours, avec au moins {FrameEvidence.PersistenceDropDb:0} dB d'écart de part et d'autre."),
            "90 % des trames ou plus : mur permanent, comme celui d'un encodeur.",
            persistenceSignal,
            persistenceWeight);

        string highValue;
        PointSignal highSignal = PointSignal.Info;
        double? highWeight = s.HighBand;
        if (m.HighBandDb is null)
        {
            highValue = m.ContentTopHz >= VerdictEngine.LosslessTopHz
                ? Fr($"contenu jusqu'à {m.ContentTopHz / 1000:0.00} kHz ; le fichier s'arrête avant 21 kHz")
                : "sans objet : le fichier s'arrête avant 21 kHz";
            highWeight = s.HighBand != 0 ? s.HighBand : null;
        }
        else if (m.ContentTopHz < VerdictEngine.LosslessTopHz)
        {
            highValue = Fr($"sans objet : le contenu s'arrête à {m.ContentTopHz / 1000:0.00} kHz");
            highWeight = null;
        }
        else if (ConfidenceModel.IsCodecCandidate(m))
        {
            highValue = "sans objet : ce qui dépasse un mur est jugé par le critère 3";
            highWeight = null;
        }
        else if (m.HighBandAboveQuantizationDb <= ConfidenceModel.QuantizationMarginDb)
        {
            highValue = "rien de plus que le bruit de quantification";
        }
        else if (m.HighBand is { DepthDb: >= FrameEvidence.LeakageDepthDb } leak)
        {
            highValue = Fr($"seulement une image de la musique, {leak.DepthDb:0} dB sous elle : une fuite, pas du contenu");
        }
        else if (m.HighBand is { HasDynamics: true } high)
        {
            highValue = Fr($"l'énergie {VerdictEngine.Following(high.Correlation)} la musique (r = {high.Correlation:0.00})");
        }
        else
        {
            highValue = Fr($"contenu jusqu'à {m.ContentTopHz / 1000:0.00} kHz ; musique trop uniforme pour vérifier qu'il la suit");
        }
        if (highWeight <= -0.5)
        {
            highSignal = PointSignal.LosslessLike;
        }
        var highBand = new AnalysisPoint(
            "Contenu musical au-delà de 21 kHz",
            highValue,
            "Aucun encodeur avec perte courant ne laisse passer de musique au-delà de 21 kHz. Mais un dither ou un souffle "
            + "peuvent y mettre de l'énergie sans être de la musique : ce qui compte, c'est que cette énergie monte et "
            + "descende avec la musique de la bande 12–20 kHz.",
            Fr($"corrélation de {ConfidenceModel.FollowsMusicCorrelation:0.0} ou plus avec la bande 12–20 kHz : vrai contenu musical."),
            highSignal,
            highWeight);

        return [persistence, highBand];
    }

    /// <summary>Everything else the verdict rests on or is limited by.</summary>
    public static IReadOnlyList<AnalysisPoint> Observations(Measurements m, Judgement j, AnalysisScope scope)
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
            "La fréquence la plus haute où il reste du son, à moins de 70 dB sous le maximum et au moins 10 dB au-dessus "
            + "du bruit de quantification : c'est là que le spectrogramme devient noir. Au-dessus du mur d'un encodeur, "
            + "un bruit constant (dither) ne compte pas : le contenu s'arrête au mur. Au-delà de 20,5 kHz, aucun "
            + "encodeur avec perte courant ne laisse passer de contenu. En dessous, ce n'est pas une preuve : un "
            + "enregistrement peut simplement manquer d'aigus.",
            Fr($"plus de {VerdictEngine.LosslessTopHz / 1000:0.0} kHz : hors de portée des encodeurs avec perte."),
            m.ContentTopHz >= VerdictEngine.LosslessTopHz ? PointSignal.LosslessLike : PointSignal.Info));

        bool fewFrames = m.Frames < VerdictEngine.MinFramesForFullConfidence;
        double probability = j.Score.LossyProbability;
        string sum = Fr($"Les poids des indices ci-dessus s'additionnent : probabilité de transcodage {probability:P0}. ");
        if (j.Score.ZoneCap is { } cap && probability >= cap)
        {
            sum += Fr($"Elle est plafonnée à {cap:P0}, car à {m.WallHz / 1000:0.0} kHz la coupure d'un encodeur à haut débit et celle d'un convertisseur de fréquence se confondent. ");
        }
        string scale = j.Verdict switch
        {
            SpectralVerdict.Lossy => sum + "Pour un verdict « avec perte », la confiance est cette probabilité.",
            SpectralVerdict.Lossless =>
                sum
                + Fr($"Pour un verdict « lossless », la confiance est son complément, plafonné à {VerdictEngine.MaxLosslessConfidence:P0}")
                + " : l'absence de mur ne prouve jamais tout à fait l'absence d'encodeur.",
            _ => sum + Fr($"{VerdictEngine.InconclusiveConfidence:P0} : l'analyse ne permet pas de trancher."),
        };
        string reduced = Fr(
            $" Elle est réduite de {1 - VerdictEngine.FewFramesFactor:P0} parce que moins de {VerdictEngine.MinFramesForFullConfidence} trames ont pu être analysées.");
        points.Add(new AnalysisPoint(
            "Confiance",
            Fr($"{j.Confidence:P0}"),
            fewFrames ? scale + reduced : scale,
            "",
            fewFrames ? PointSignal.Warning : PointSignal.Info));

        double fullConfidenceSec = VerdictEngine.MinFramesForFullConfidence * (m.FftSize / 2.0) / 44100;
        points.Add(new AnalysisPoint(
            "Audio analysé",
            Fr($"{scope.AnalyzedSec:0.0} s, de {scope.StartSec:0.0} s à {scope.EndSec:0.0} s ({m.Frames} trames)"),
            Fr($"L'extrait est découpé en trames de {m.FftSize} échantillons qui se chevauchent de moitié ; leurs spectres ")
            + "sont moyennés et les passages silencieux ignorés. Plus il y a de trames, plus la mesure est stable. La "
            + "durée analysée se règle dans la barre d'outils.",
            Fr($"{VerdictEngine.MinFramesForFullConfidence} trames ou plus (≈ {fullConfidenceSec:0} s à 44,1 kHz) pour une confiance pleine."),
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
    public static string Conclusion(Measurements m, Judgement j, IReadOnlyList<AnalysisPoint> criteria)
    {
        int met = criteria.Count(point => point.Signal == PointSignal.LossyLike);
        string reach = Fr($"{m.ContentTopHz / 1000:0.00} kHz");
        string top = Fr($"{VerdictEngine.LosslessTopHz / 1000:0.0} kHz");
        string partial = met == 1 ? "1 critère sur 3 seulement" : $"{met} critères sur 3 seulement";
        return j.Verdict switch
        {
            SpectralVerdict.Lossy when j.Score.ZoneCap is not null && j.Score.LossyProbability >= j.Score.ZoneCap =>
                $"Les 3 critères sont réunis : c'est la signature d'un encodeur avec perte (source probable : {j.LikelySource}), "
                + "mais à cette position c'est aussi celle d'un convertisseur de fréquence : impossible de l'affirmer.",
            SpectralVerdict.Lossy =>
                $"Les 3 critères sont réunis : c'est la signature d'un encodeur avec perte (source probable : {j.LikelySource}).",
            SpectralVerdict.Lossless when met == 0 =>
                $"Aucun critère d'encodeur avec perte n'est réuni, et le contenu monte jusqu'à {reach}, au-delà des {top} "
                + "qu'aucun encodeur avec perte ne dépasse : cohérent avec un vrai lossless.",
            SpectralVerdict.Lossless when met == 3 =>
                $"Les 3 critères sont réunis, mais les autres indices les contredisent et le contenu monte jusqu'à {reach}, "
                + $"au-delà des {top} qu'aucun encodeur avec perte ne dépasse : cohérent avec un vrai lossless.",
            SpectralVerdict.Lossless =>
                $"{partial} (il en faut 3), et le contenu monte jusqu'à {reach}, au-delà des {top} qu'aucun encodeur "
                + "avec perte ne dépasse : cohérent avec un vrai lossless.",
            _ when met == 3 =>
                "Les 3 critères sont réunis, mais les autres indices les contredisent : impossible de trancher.",
            _ when met == 0 =>
                $"Aucun critère d'encodeur avec perte n'est réuni, mais le contenu s'arrête à {reach}, sous les {top} qui "
                + "prouveraient un vrai lossless : impossible de trancher.",
            _ =>
                $"{partial} (il en faut 3 pour conclure à un encodeur avec perte), et le contenu s'arrête à {reach}, sous "
                + $"les {top} qui prouveraient un vrai lossless : impossible de trancher.",
        };
    }
}
