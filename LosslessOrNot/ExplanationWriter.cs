using LosslessOrNot.Core;
using LosslessOrNot.Core.Assessment;
using LosslessOrNot.Core.Spectral;
using static LosslessOrNot.Core.TextFormat;

namespace LosslessOrNot;

/// <summary>
/// Fills the "Explications" tab: why a file got its status, criterion by
/// criterion, with the values measured on it and the thresholds they are held to.
/// </summary>
internal static class ExplanationWriter
{
    private static readonly (PointSignal Signal, string Meaning)[] Legend =
    [
        (PointSignal.LossyLike, "évoque un encodeur avec perte"),
        (PointSignal.LosslessLike, "rassurant"),
        (PointSignal.Info, "information"),
        (PointSignal.Warning, "limite de l'analyse"),
    ];

    /// <param name="indent">How far explanations sit from the margin, in pixels.</param>
    public static void Write(RichTextComposer text, TrackEntry? entry, int indent)
    {
        text.Reset(indent);
        TrackResult? result = entry?.Result;
        if (entry is null || result is null)
        {
            text.Line(
                entry is null
                    ? "Sélectionnez un fichier analysé pour savoir pourquoi il a reçu son statut."
                    : "Ce fichier n'a pas encore été analysé.",
                color: SystemColors.GrayText);
            return;
        }

        LosslessAssessment assessment = result.Assessment;
        text.Line($"Pourquoi « {assessment.Label} » ?", font: text.Title, color: StatusStyle.For(assessment).Fore);
        text.Line(assessment.Explanation);

        SpectralAnalysis? spectral = result.Spectral;
        if (spectral is null)
        {
            WriteFailure(text, result);
            text.ScrollToTop();
            return;
        }
        if (assessment.Status == LosslessStatus.LossyFormat)
        {
            text.Gap();
            text.Line(
                "Le format suffit à conclure : MP3, AAC, Opus, Vorbis… sont avec perte par construction. L'analyse "
                + "ci-dessous indique seulement quelle qualité d'encodage le spectre laisse deviner.",
                color: SystemColors.GrayText);
        }

        text.Heading("Les 3 critères d'un encodeur avec perte");
        text.Line(
            "Un encodeur avec perte (MP3, AAC…) supprime les aigus au-dessus d'une fréquence de coupure. Le fichier "
            + "n'est jugé « avec perte » que si les trois critères sont réunis à la fois, et si l'ensemble des indices "
            + "le confirme.",
            color: SystemColors.GrayText);
        for (int i = 0; i < spectral.Criteria.Count; i++)
        {
            WritePoint(text, spectral.Criteria[i], $"{i + 1}. ", indent);
        }

        if (spectral.SupportingEvidence.Count > 0)
        {
            text.Heading("Indices complémentaires");
            text.Line(
                "Mesurés trame par trame, là où le spectre moyen ne voit rien : ils renforcent ou affaiblissent les "
                + "critères.",
                color: SystemColors.GrayText);
            for (int i = 0; i < spectral.SupportingEvidence.Count; i++)
            {
                WritePoint(text, spectral.SupportingEvidence[i], $"{spectral.Criteria.Count + i + 1}. ", indent);
            }
        }

        // A lossy format is lossy whatever the spectrum: there is no transcode to weigh.
        if (assessment.Status != LosslessStatus.LossyFormat)
        {
            text.Gap();
            text.Line(Fr($"→ Probabilité de transcodage : {spectral.LossyProbability:P0}"), font: text.Bold);
            text.Line(
                "Chaque poids multiplie les chances d'un transcodage, ou celles d'un vrai lossless ; tous ensemble, ils "
                + "donnent cette probabilité.",
                color: SystemColors.GrayText);
        }
        text.Gap();
        text.Line("→ " + spectral.Conclusion, font: text.Bold);

        text.Heading("Autres indices");
        foreach (AnalysisPoint point in spectral.Observations)
        {
            WritePoint(text, point, "", indent);
        }

        text.Gap();
        text.Paragraph(0);
        text.Run("Légende :  ", color: SystemColors.GrayText);
        foreach ((PointSignal signal, string meaning) in Legend)
        {
            text.Run(ThemeColors.Symbol(signal) + " ", text.Symbol, ThemeColors.For(signal));
            text.Run(meaning + "    ", color: SystemColors.GrayText);
        }
        text.EndParagraph();
        text.ScrollToTop();
    }

    // The symbol sits in the margin; the title, its wrapped lines, the explanation
    // and the threshold all line up on the tab stop.
    private static void WritePoint(RichTextComposer text, AnalysisPoint point, string number, int indent)
    {
        text.Gap();
        text.Paragraph(0, hanging: indent);
        text.Run(ThemeColors.Symbol(point.Signal) + "\t", text.Symbol, ThemeColors.For(point.Signal));
        text.Run(number + point.Title, text.Bold);
        text.Run(" — " + point.Value);
        text.EndParagraph();
        text.Line(point.Explanation, indent);
        if (point.Threshold.Length > 0)
        {
            text.Line("Seuil : " + point.Threshold, indent, text.Italic, SystemColors.GrayText);
        }
        if (point.Weight is { } weight)
        {
            text.Line("Poids : " + DescribeWeight(weight), indent, text.Italic, SystemColors.GrayText);
        }
    }

    // A contribution in log-odds reads best as the factor it multiplies the odds by.
    private static string DescribeWeight(double logOdds)
    {
        if (Math.Abs(logOdds) < 0.1)
        {
            return "neutre";
        }
        double factor = Math.Exp(Math.Abs(logOdds));
        string times = factor < 10 ? Fr($"×{factor:0.#}") : Fr($"×{factor:0}");
        return logOdds > 0 ? times + " vers un encodeur avec perte" : times + " vers un vrai lossless";
    }

    private static void WriteFailure(RichTextComposer text, TrackResult result)
    {
        if (result.Assessment.Status == LosslessStatus.LossyFormat)
        {
            // The format alone settled it; the spectrum could only have told more.
            if (result.Error is { } error)
            {
                text.Heading("Analyse spectrale impossible");
                text.Line(error);
            }
            return;
        }
        text.Heading("Ce qui s'est passé");
        text.Line(result.Error ?? "L'analyse n'a pas pu aboutir.");
        text.Heading("Pistes");
        text.Line("•  le fichier est peut-être endommagé, incomplet ou vide ;");
        text.Line("•  son format n'est peut-être pas pris en charge par ffmpeg ;");
        text.Line("•  vérifiez que ffmpeg et ffprobe fonctionnent (bouton « ffmpeg… »).");
    }
}
