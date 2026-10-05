using System.Globalization;
using System.Text;
using LosslessOrNot.Core.Assessment;
using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Calibration;

/// <summary>Writes report.md: what the corpus says about the old rules, the current weights and the fitted ones.</summary>
internal static class Report
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly LosslessStatus[] Statuses =
        [LosslessStatus.Authentic, LosslessStatus.FakeLossless, LosslessStatus.Suspect, LosslessStatus.Inconclusive];

    private static readonly double[] CurrentWeights =
    [
        ConfidenceModel.SteepnessWeight,
        ConfidenceModel.PositionWeight,
        ConfidenceModel.FloorWeight,
        ConfidenceModel.PersistenceWeight,
        ConfidenceModel.HighBandWeight,
    ];

    public static string Write(
        IReadOnlyList<Row> rows,
        FitResult fit,
        string folder,
        double windowSec,
        int fftSize,
        IEnumerable<string> missingEncoders,
        int failures)
    {
        // The fitted probability of every row, clamped and capped as the app does.
        var fitted = rows.ToDictionary(
            r => r,
            r => ZoneCapped(r, Math.Clamp(
                LogisticFit.Probability(fit.Intercept, fit.Weights, r.Contributions),
                ConfidenceModel.MinProbability,
                ConfidenceModel.MaxProbability)));
        Row[] genuine = rows.Where(r => !r.IsTranscode).ToArray();
        Row[] fakes = rows.Where(r => r.IsTranscode).ToArray();

        var b = new StringBuilder();
        b.AppendLine($"# Calibration — {DateTime.Now.ToString("d MMMM yyyy HH:mm", Fr)}");
        b.AppendLine();
        b.AppendLine($"- Dossier : `{folder}`");
        b.AppendLine(
            $"- {genuine.Length} vrais lossless, {fakes.Length} transcodages, fenêtre de {windowSec.ToString("0", Fr)} s, "
            + $"trames FFT de {fftSize} points.");
        string missing = string.Join(", ", missingEncoders);
        b.AppendLine($"- Encodeurs absents de ce ffmpeg : {(missing.Length > 0 ? missing : "aucun")}.");
        if (failures > 0)
        {
            b.AppendLine($"- {failures} fichier(s) ou transcodage(s) n'ont pas pu être analysés (voir la console).");
        }
        b.AppendLine("- « Ancienne » : les règles portées de Go (3 critères, plancher −88 dB, contenu > −70 dB au-delà de 20,5 kHz).");
        b.AppendLine("- « Actuelle » : la probabilité avec les poids actuels de `ConfidenceModel`. « Ajustée » : avec les poids proposés plus bas.");

        b.AppendLine();
        b.AppendLine("## Vrais lossless");
        b.AppendLine();
        b.AppendLine("| Statut | Ancienne | Actuelle | Ajustée |");
        b.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (LosslessStatus status in Statuses)
        {
            b.AppendLine(
                $"| {Label(status)} | {Count(genuine, r => r.LegacyStatus == status)} | {Count(genuine, r => r.Status == status)} "
                + $"| {Count(genuine, r => r.StatusFor(fitted[r]) == status)} |");
        }

        b.AppendLine();
        b.AppendLine("## Transcodages détectés (« Faux lossless »)");
        b.AppendLine();
        b.AppendLine("| Recette | n | Ancienne | Actuelle | Ajustée | Confiance médiane | Mur médian | Contenu médian | Source devinée |");
        b.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (IGrouping<string, Row> recipe in fakes.GroupBy(r => r.Recipe))
        {
            Row[] g = recipe.ToArray();
            int top = (int)Median(g.Select(r => (double)r.CutoffHz));
            b.AppendLine(
                $"| {recipe.Key} | {g.Length} | {Share(g, r => r.LegacyStatus == LosslessStatus.FakeLossless)} "
                + $"| {Share(g, r => r.Status == LosslessStatus.FakeLossless)} "
                + $"| {Share(g, r => r.StatusFor(fitted[r]) == LosslessStatus.FakeLossless)} "
                + $"| {Median(g.Select(r => r.Confidence)).ToString("P0", Fr)} "
                + $"| {(Median(g.Select(r => (double)r.CliffHz)) / 1000).ToString("0.00", Fr)} kHz "
                + $"| {(top / 1000.0).ToString("0.00", Fr)} kHz | {VerdictEngine.GuessEncoder(top)} |");
        }

        b.AppendLine();
        b.AppendLine("## Transcodages sans signature spectrale");
        b.AppendLine();
        Row[] invisible = fakes.Where(r => !r.HasCodecCandidate).ToArray();
        if (invisible.Length == 0)
        {
            b.AppendLine("Aucun : chaque transcodage montre un mur abrupt sous 21 kHz.");
        }
        else
        {
            b.AppendLine(
                $"{invisible.Length} transcodage(s) sur {fakes.Length} ne montrent aucun mur abrupt sous 21 kHz : l'encodeur a "
                + "laissé passer toute la bande. Aucune analyse spectrale ne peut les distinguer de leur original ; ils sont "
                + "exclus de l'ajustement des poids. Seule une analyse de la grille MDCT de l'encodeur les trahirait.");
            b.AppendLine();
            foreach (IGrouping<string, Row> recipe in invisible.GroupBy(r => r.Recipe).OrderByDescending(g => g.Count()))
            {
                string rates = string.Join(", ", recipe.Select(r => r.SampleRate).Distinct().Order().Select(rate => (rate / 1000.0).ToString("0.#", Fr) + " kHz"));
                b.AppendLine($"- {recipe.Key} : {recipe.Count()} sur {fakes.Count(r => r.Recipe == recipe.Key)} ({rates})");
            }
        }

        b.AppendLine();
        b.AppendLine("## Vrais lossless signalés, à écouter et inspecter");
        b.AppendLine();
        Row[] flagged = genuine
            .Where(r => r.Status is LosslessStatus.FakeLossless or LosslessStatus.Suspect
                || r.StatusFor(fitted[r]) is LosslessStatus.FakeLossless or LosslessStatus.Suspect)
            .ToArray();
        if (flagged.Length == 0)
        {
            b.AppendLine("Aucun.");
        }
        else
        {
            b.AppendLine("| Fichier | Actuelle | Ajustée | Mur | Pente | Plancher | Corrélation | P |");
            b.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |");
            foreach (Row r in flagged)
            {
                b.AppendLine(
                    $"| `{r.Source}` | {Label(r.Status)} | {Label(r.StatusFor(fitted[r]))} | {(r.CliffHz / 1000.0).ToString("0.00", Fr)} kHz "
                    + $"| {r.DropDb.ToString("0", Fr)} dB | {r.FloorDb.ToString("0", Fr)} dB | {r.FloorCorrelation?.ToString("0.00", Fr) ?? "—"} "
                    + $"| {r.LossyProbability.ToString("P0", Fr)} |");
            }
        }

        b.AppendLine();
        b.AppendLine("## Fiabilité de la probabilité");
        b.AppendLine();
        b.AppendLine("Part de transcodages parmi les fichiers de chaque tranche de probabilité, vrais lossless et transcodages");
        b.AppendLine("pesant autant chacun (a priori 50/50). Une probabilité bien calibrée suit la diagonale.");
        b.AppendLine();
        b.AppendLine("| P prédite | n (actuelle) | transcodages (actuelle) | n (ajustée) | transcodages (ajustée) |");
        b.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        for (int bin = 0; bin < 10; bin++)
        {
            double lo = bin / 10.0, hi = (bin + 1) / 10.0;
            bool In(double p) => p >= lo && (p < hi || bin == 9);
            Row[] current = rows.Where(r => In(r.LossyProbability)).ToArray();
            Row[] adjusted = rows.Where(r => In(fitted[r])).ToArray();
            b.AppendLine(
                $"| {lo.ToString("0.0", Fr)}–{hi.ToString("0.0", Fr)} | {current.Length} | {BalancedShare(current, rows)} "
                + $"| {adjusted.Length} | {BalancedShare(adjusted, rows)} |");
        }

        b.AppendLine();
        b.AppendLine("## Poids");
        b.AppendLine();
        b.AppendLine("| Indice | Actuel | Ajusté |");
        b.AppendLine("| --- | ---: | ---: |");
        b.AppendLine($"| Intercept | {ConfidenceModel.Intercept.ToString("0.00", Fr)} | {fit.Intercept.ToString("0.00", Fr)} |");
        for (int i = 0; i < Row.Indicators.Length; i++)
        {
            double suggested = CurrentWeights[i] * fit.Weights[i];
            b.AppendLine($"| {Row.Indicators[i]} | {CurrentWeights[i].ToString("0.00", Fr)} | {suggested.ToString("0.00", Fr)} |");
        }
        b.AppendLine();
        b.AppendLine(
            $"Pénalité retenue par validation croisée (5 plis, par morceau) : λ = {fit.Lambda.ToString("0.##", Fr)} ; "
            + $"log-loss hors échantillon : {fit.CrossValidatedLogLoss.ToString("0.000", Fr)}.");
        if (fit.Weights.Any(w => w < 0))
        {
            b.AppendLine();
            b.AppendLine("⚠ Un poids ajusté est négatif : cet indice dit le contraire de ce que ses ancrages supposent. À examiner avant de reporter quoi que ce soit.");
        }
        b.AppendLine();
        b.AppendLine(
            "Ces poids ne sont qu'indicatifs : un modèle additif ne peut pas représenter la zone 19,1–21 kHz, où encodeurs "
            + "et convertisseurs de fréquence coupent pareillement et que l'application traite par un plafond. Avant de "
            + "les reporter, vérifier qu'ils réduisent les vrais lossless signalés sans faire chuter la détection.");
        b.AppendLine();
        b.AppendLine("À reporter dans `ConfidenceModel`, le cas échéant :");
        b.AppendLine();
        b.AppendLine("```csharp");
        b.AppendLine($"public const double Intercept = {Code(fit.Intercept)};");
        for (int i = 0; i < Row.Indicators.Length; i++)
        {
            b.AppendLine($"public const double {Row.Indicators[i]}Weight = {Code(CurrentWeights[i] * Math.Max(0, fit.Weights[i]))};");
        }
        b.AppendLine("```");

        b.AppendLine();
        b.AppendLine("## Seuil « Faux lossless »");
        b.AppendLine();
        double worstGenuine = genuine.Where(r => r.HasCodecWall).Select(r => fitted[r]).DefaultIfEmpty(0).Max();
        b.AppendLine(
            $"Avec les poids ajustés, le vrai lossless le plus suspect atteint {worstGenuine.ToString("P1", Fr)} "
            + $"(seuil actuel : {LosslessAssessor.FakeLosslessMinConfidence.ToString("P0", Fr)}).");
        foreach (double threshold in new[] { 0.75, 0.9, 0.95, 0.98 })
        {
            b.AppendLine(
                $"- seuil {threshold.ToString("P0", Fr)} : {Count(genuine, r => r.StatusFor(fitted[r], threshold) == LosslessStatus.FakeLossless)} vrai(s) "
                + $"lossless en « Faux lossless », {Share(fakes, r => r.StatusFor(fitted[r], threshold) == LosslessStatus.FakeLossless)} des transcodages détectés.");
        }
        return b.ToString();
    }

    // Where encoders and sample-rate converters cut alike, the app caps the probability.
    private static double ZoneCapped(Row r, double probability) =>
        r.HasCodecWall && r.CliffHz > ConfidenceModel.ResamplerZoneStartHz - 100
            ? Math.Min(probability, ConfidenceModel.Interpolate(r.CliffHz, ConfidenceModel.ResamplerZoneCaps))
            : probability;

    private static string Label(LosslessStatus status) => status switch
    {
        LosslessStatus.Authentic => "Lossless authentique",
        LosslessStatus.FakeLossless => "Faux lossless",
        LosslessStatus.Suspect => "Suspect",
        LosslessStatus.Inconclusive => "Non concluant",
        _ => status.ToString(),
    };

    private static int Count(IEnumerable<Row> rows, Func<Row, bool> predicate) => rows.Count(predicate);

    private static string Share(IReadOnlyCollection<Row> rows, Func<Row, bool> predicate) =>
        rows.Count == 0 ? "—" : ((double)rows.Count(predicate) / rows.Count).ToString("P0", Fr);

    // The share of transcodes in a bin, each class weighing half of the corpus.
    private static string BalancedShare(IReadOnlyCollection<Row> bin, IReadOnlyCollection<Row> all)
    {
        if (bin.Count == 0)
        {
            return "—";
        }
        int fakes = all.Count(r => r.IsTranscode), genuine = all.Count - fakes;
        double f = bin.Count(r => r.IsTranscode) / (double)Math.Max(1, fakes);
        double g = bin.Count(r => !r.IsTranscode) / (double)Math.Max(1, genuine);
        return (f / (f + g)).ToString("P0", Fr);
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }

    private static string Code(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
