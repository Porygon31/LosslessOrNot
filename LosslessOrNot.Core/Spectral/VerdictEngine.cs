using System.Diagnostics;
using static LosslessOrNot.Core.TextFormat;

namespace LosslessOrNot.Core.Spectral;

/// <summary>Everything a verdict is derived from.</summary>
/// <param name="Db">
/// Averaged power spectrum in dB relative to its peak — as a 4096-point analysis
/// measures it, whatever the FFT size — bin 0 = DC.
/// </param>
/// <param name="FftSize">Samples per frame.</param>
/// <param name="BitsPerSample">The file's integer depth; 0 when unknown.</param>
/// <param name="QuantizationFloorDb">The rounding noise of that depth per bin, in dB relative to the peak.</param>
/// <param name="Bands">The frame-by-frame band levels.</param>
internal sealed record SpectrumEvidence(
    double[] Db,
    int SampleRate,
    int Frames,
    int FftSize,
    int BitsPerSample,
    double? QuantizationFloorDb,
    FrameBands Bands);

/// <summary>The measurements and conclusion <see cref="VerdictEngine"/> derives from a spectrum.</summary>
internal sealed record VerdictResult(
    int CutoffHz,
    int CliffHz,
    double ShelfDropDb,
    double EnergyAboveCutoffDb,
    SpectralVerdict Verdict,
    string LikelySource,
    double Confidence,
    double ContentThresholdDb,
    Measurements Measurements,
    Score Score,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<AnalysisPoint> Criteria,
    IReadOnlyList<AnalysisPoint> SupportingEvidence,
    IReadOnlyList<AnalysisPoint> Observations,
    string Conclusion);

/// <summary>Which part of the file an analysis covered.</summary>
/// <param name="EndSec">End of the spectrogram's time axis.</param>
/// <param name="FileDurationSec">0 when unknown.</param>
internal readonly record struct AnalysisScope(
    double StartSec,
    double EndSec,
    double RequestedSec,
    double AnalyzedSec,
    double FileDurationSec)
{
    // Decoders may round the last packet: half a second short is not "shorter".
    public bool IsShorterThanRequested => AnalyzedSec + 0.5 < RequestedSec;
}

/// <summary>
/// Measures where content actually ends, looks for a codec-shaped wall, and
/// attaches a confidence and the evidence behind it.
/// </summary>
public static class VerdictEngine
{
    // Thresholds separating a codec's low-pass from ordinary programme roll-off.
    // They are all ABSOLUTE frequencies and levels, deliberately NOT fractions of
    // Nyquist.
    //
    // Judging against Nyquist is what made every hi-res file read as fake. A genuine
    // 96 kHz master has no content above roughly 24 kHz — that is a property of
    // microphones, mastering and human hearing, not of the encoding — so its
    // cutoff/Nyquist ratio is around 0.5 however perfect the master is. The old rule
    // ("steep shelf AND ratio < 0.90 => lossy") therefore condemned every 88.2/96/
    // 192 kHz file on sight. Measured: a Spek-verified lossless 96 kHz FLAC scored
    // ratio 0.29 with a 25.5 dB shelf and was labelled "Fake Lossless".

    /// <summary>No consumer encoder places a low-pass below this.</summary>
    public const double WallSearchLoHz = 8000.0;

    /// <summary>...nor passes content above this.</summary>
    public const double CodecMaxWallHz = 21000.0;

    /// <summary>A codec wall is abrupt; programme material is not.</summary>
    public const double WallDropMinDb = 20.0;

    /// <summary>
    /// The drop that still makes a wall when it lands on a flat floor: clipping
    /// distortion caps the drop. Measured: a loud DnB master's MP3 128 kbps decode lost
    /// 19.6 dB over 1 kHz at 16.3 kHz onto a floor flat to 22 kHz, while the master
    /// itself never lost more than 8.7 dB over 1 kHz.
    /// </summary>
    public const double FlatFloorWallDropMinDb = 15.0;

    /// <summary>A codec ZEROES the bins above its wall.</summary>
    public const double CodecFloorDb = -88.0;

    /// <summary>"Content reaches this far", relative to the peak.</summary>
    public const double ContentFloorDb = -70.0;

    /// <summary>
    /// Content must also stand this far above the format's rounding noise: on a quiet
    /// track that noise alone can exceed <see cref="ContentFloorDb"/>, and it is not content.
    /// </summary>
    public const double ContentAboveQuantizationDb = 10.0;

    /// <summary>Content past here is out of reach of lossy codecs.</summary>
    public const double LosslessTopHz = 20500.0;

    /// <summary>Below this many frames the confidence is scaled down.</summary>
    public const int MinFramesForFullConfidence = 60;

    /// <summary>What the confidence is multiplied by below <see cref="MinFramesForFullConfidence"/> frames.</summary>
    public const double FewFramesFactor = 0.6;

    /// <summary>A lossless verdict needs the probability of a lossy source to be this low at most.</summary>
    public const double LosslessMaxLossyProbability = 0.25;

    /// <summary>
    /// The absence of a wall never quite proves the absence of an encoder: at 44.1 kHz,
    /// AAC at 256 kbps, LAME V0 and Vorbis q6 and above often keep the full band, and
    /// 17 % of the calibration transcodes looked exactly like their original.
    /// </summary>
    public const double MaxLosslessConfidence = 0.90;

    /// <summary>The confidence of an inconclusive verdict: not presentable as a finding.</summary>
    public const double InconclusiveConfidence = 0.30;

    internal static VerdictResult Derive(SpectrumEvidence evidence, AnalysisScope scope)
    {
        ReadOnlySpan<double> db = evidence.Db;
        int bins = db.Length;
        double binHz = (double)evidence.SampleRate / 2 / bins;
        double nyquist = (double)evidence.SampleRate / 2;
        int framesAnalyzed = evidence.Frames;

        // Smooth over ~200 Hz to suppress bin-level noise without blurring a shelf.
        int smoothN = Math.Max(1, (int)(200 / binHz + 0.5));
        double[] sm = SignalMath.MovingAverage(db, smoothN);

        // ── 1. How far up the band does real content reach? ──────────────────────
        // This is the number a person reads off a Spek plot: where the picture goes
        // black. The previous definition — the first point 3 dB below the median of
        // the whole 2 kHz..mid band — is meaningless for music, because that median
        // is dominated by bass and mids sitting 20-30 dB above the treble. It
        // reported 11-14 kHz for *every* file measured, lossless and lossy alike.
        //
        // On a quiet track the format's own rounding noise can sit above -70 dB of the
        // peak: it then read as content all the way to Nyquist, and a quiet MP3
        // transcode was called lossless. Content must clear that noise too.
        double contentThreshold = Math.Max(
            ContentFloorDb, (evidence.QuantizationFloorDb ?? double.NegativeInfinity) + ContentAboveQuantizationDb);
        double contentTop = 0;
        for (int b = bins - 1; b >= 0; b--)
        {
            if (sm[b] > contentThreshold)
            {
                contentTop = (b + 1) * binHz;
                break;
            }
        }

        // ── 2. The steepest 1 kHz drop, and the level it lands in ────────────────
        // The landing level is the decisive measurement: a codec zeroes every bin
        // above its wall, so the level collapses to the numerical floor, whereas a
        // mastering roll-off — however steep — leaves noise behind. Measured: MP3 320
        // lands at -98 dB, MP3 128 at -91 dB, while a lossless 96 kHz master's much
        // steeper-looking roll-off lands at -85 dB.
        int step = Math.Max(1, (int)(1000 / binHz + 0.5));
        int loBin = (int)(WallSearchLoHz / binHz + 0.5);
        int wallBin = -1;
        double wallDrop = 0, floorAbove = 0;
        for (int b = loBin; b + step < bins; b++)
        {
            double d = sm[b] - sm[b + step];
            if (d <= wallDrop)
            {
                continue;
            }
            int hi = Math.Min(b + 3 * step, bins);
            if (hi <= b + step)
            {
                continue;
            }
            wallBin = b;
            wallDrop = d;
            floorAbove = SignalMath.Percentile(sm.AsSpan(b + step, hi - (b + step)), 0.5);
        }
        double wallHz = wallBin > 0 ? (wallBin + 1) * binHz : 0;

        // ── 3. Frame by frame: what the average hides ────────────────────────────
        // Whether the wall holds at every instant, whether what lies above it follows
        // the music, and whether what lies beyond 21 kHz does.
        var m = new Measurements(
            contentTop,
            wallHz,
            wallDrop,
            floorAbove,
            nyquist,
            framesAnalyzed,
            evidence.BitsPerSample,
            evidence.QuantizationFloorDb,
            HighBandLevel(db, binHz, nyquist),
            wallHz > 0 ? FrameEvidence.TrackWall(evidence.Bands, wallHz, nyquist) : null,
            FrameEvidence.TrackHighBand(evidence.Bands, nyquist),
            wallBin > 0 ? sm[wallBin] : 0,
            wallHz > 0 ? SlopeAbove(sm, wallHz, binHz, nyquist) : null,
            evidence.FftSize);

        Debug.WriteLine(
            $"[spectrum] contentTop={contentTop:0} wallHz={wallHz:0} drop={wallDrop:0.0} floorAbove={floorAbove:0.0} "
            + $"aboveQuant={m.FloorAboveQuantizationDb:0.0} r={m.Wall?.Correlation:0.00} persistence={m.Wall?.Persistence:0.00} "
            + $"rHigh={m.HighBand?.Correlation:0.00} nyq={nyquist:0}");

        // ── 4. Verdict ───────────────────────────────────────────────────────────
        // "Lossy" still requires the three signals of a codec's wall to agree: it is
        // abrupt, it sits where a codec could have put it, and nothing musical is left
        // above it. Requiring all three is what stops a steep but natural mastering
        // roll-off — the hi-res case — from being condemned. The probability then
        // weighs every indicator, so that it also takes evidence to reach a high
        // confidence, and contrary evidence can still hold the verdict back.
        Score score = ConfidenceModel.Evaluate(m);
        // Above a codec's wall, noise loud enough to clear the content threshold —
        // dither, above all noise-shaped dither — read as content up to Nyquist, and
        // the source was guessed from Nyquist. It is not content: the music ends at
        // the wall's corner.
        if (score.HasCodecWall && contentTop > wallHz + NoiseAboveWallMarginHz)
        {
            contentTop = WallCorner(sm, wallBin, floorAbove, binHz);
            m = m with { ContentTopHz = contentTop };
            score = ConfidenceModel.Evaluate(m);
        }
        double probability = score.LossyProbability;
        int cutoffHz = (int)contentTop;
        SpectralVerdict verdict;
        string likelySource = "";
        double confidence;
        if (score.HasCodecWall && probability >= 0.5)
        {
            verdict = SpectralVerdict.Lossy;
            // Guess from where content actually ends, not from the steepest-drop bin:
            // the latter sits below the corner and biases every guess a tier low.
            likelySource = GuessEncoder(cutoffHz);
            confidence = probability;
        }
        else if (contentTop >= LosslessTopHz && probability <= LosslessMaxLossyProbability)
        {
            verdict = SpectralVerdict.Lossless;
            confidence = Math.Min(1 - probability, MaxLosslessConfidence);
        }
        else
        {
            verdict = SpectralVerdict.Inconclusive;
            confidence = InconclusiveConfidence;
        }

        var lines = new List<string>();
        if (scope.IsShorterThanRequested)
        {
            lines.Add(Fr(
                $"Seulement {scope.AnalyzedSec:0.0} s d'audio sur les {scope.RequestedSec:0} s demandées : le fichier a été analysé en entier."));
        }
        if (framesAnalyzed < MinFramesForFullConfidence)
        {
            confidence *= FewFramesFactor;
            lines.Add(Fr($"Seulement {framesAnalyzed} trames d'analyse disponibles — confiance réduite."));
        }
        lines.Add(Fr(
            $"Le contenu monte jusqu'à {contentTop / 1000:0.0} kHz (Nyquist de ce fichier : {nyquist / 1000:0.0} kHz)."));
        if (wallHz > 0)
        {
            lines.Add(Fr(
                $"Chute la plus raide : {wallDrop:0.0} dB sur 1 kHz à partir de {wallHz / 1000:0.0} kHz, pour retomber à {floorAbove:0} dB."));
        }
        if (m.IsSteep && m.FloorAboveQuantizationDb is { } aboveQuantization)
        {
            lines.Add(Fr(
                $"Au-dessus du mur, le niveau est à {aboveQuantization:+0;-0;0} dB du bruit de quantification {m.BitsPerSample} bits."));
        }
        if (m.IsSteep && m.Wall is { } wall)
        {
            if (wall.HasDynamics)
            {
                lines.Add(Fr(
                    $"Au-dessus du mur, le niveau est {wall.DepthDb:0} dB sous la musique et {Following(wall.Correlation)} ses variations (corrélation {wall.Correlation:0.00})."));
            }
            lines.Add(Fr($"Le mur tient dans {wall.Persistence:P0} des trames les plus riches en aigus."));
        }
        if (contentTop >= LosslessTopHz && m.HighBand is { HasDynamics: true } high)
        {
            lines.Add(Fr(
                $"Au-delà de 21 kHz, l'énergie est {high.DepthDb:0} dB sous la bande 12–20 kHz et {Following(high.Correlation)} ses variations (corrélation {high.Correlation:0.00})."));
        }
        lines.Add(Fr(
            $"Moyenne de {framesAnalyzed} trames FFT de {evidence.FftSize} points ({binHz:0.#} Hz par bin)."));
        if (nyquist > 25000)
        {
            lines.Add(
                "À cette fréquence d'échantillonnage, la bande au-delà de ~24 kHz est normalement vide : c'est une "
                + "propriété de l'enregistrement, pas de l'encodage, et ce n'est pas un indice de transcodage.");
        }
        lines.Add(Fr($"Probabilité de transcodage, tous indices confondus : {probability:P0}."));
        if (score.ZoneCap is { } cap && probability >= cap)
        {
            lines.Add(Fr(
                $"Plafonnée à {cap:P0} : à {wallHz / 1000:0.0} kHz, un encodeur à haut débit et un convertisseur de fréquence (48 → 44,1 kHz) coupent pareillement."));
        }
        lines.Add(verdict switch
        {
            SpectralVerdict.Lossless =>
                "Le contenu dépasse 20,5 kHz, hors de portée des encodeurs avec perte grand public : "
                + "cohérent avec un vrai master lossless.",
            SpectralVerdict.Lossy =>
                "Un mur aussi abrupt, placé aussi bas, au-dessus duquel rien de musical ne subsiste, est la "
                + "signature d'un encodeur avec perte, pas celle du programme musical.",
            _ when score.HasCodecWall =>
                "Le spectre a la forme d'un mur d'encodeur, mais les autres indices le contredisent : impossible de "
                + "conclure.",
            _ =>
                "La coupure n'est pas assez nette, ou ne descend pas assez bas, pour distinguer un encodeur avec "
                + "perte d'un programme musical pauvre en aigus.",
        });

        var judgement = new Judgement(verdict, confidence, likelySource, score);
        IReadOnlyList<AnalysisPoint> criteria = VerdictExplainer.Criteria(m, score);
        return new VerdictResult(
            cutoffHz,
            (int)wallHz,
            wallDrop,
            floorAbove,
            verdict,
            likelySource,
            confidence,
            contentThreshold,
            m,
            score,
            lines,
            criteria,
            VerdictExplainer.SupportingEvidence(m, score),
            VerdictExplainer.Observations(m, judgement, scope),
            VerdictExplainer.Conclusion(m, judgement, criteria));
    }

    /// <summary>
    /// Least-squares slope, in dB per kHz, of the smoothed spectrum from 1 to 5 kHz
    /// above the wall; null with less than 2 kHz of room there.
    /// </summary>
    private static double? SlopeAbove(double[] sm, double wallHz, double binHz, double nyquist)
    {
        int lo = (int)((wallHz + 1000) / binHz);
        int hi = Math.Min(sm.Length, (int)(Math.Min(wallHz + 5000, nyquist - 300) / binHz));
        if ((hi - lo) * binHz < 2000)
        {
            return null;
        }
        double n = hi - lo, sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int b = lo; b < hi; b++)
        {
            double x = b * binHz / 1000;
            sx += x;
            sy += sm[b];
            sxx += x * x;
            sxy += x * sm[b];
        }
        return (n * sxy - sx * sy) / (n * sxx - sx * sx);
    }

    /// <summary>Content found this far past a codec's wall is the noise above it, not music.</summary>
    private const double NoiseAboveWallMarginHz = 1500.0;

    /// <summary>Where the wall crosses halfway, in dB, from the music below it to the floor above.</summary>
    private static double WallCorner(double[] sm, int wallBin, double floorAbove, double binHz)
    {
        double middle = (sm[wallBin] + floorAbove) / 2;
        for (int b = wallBin; b < sm.Length; b++)
        {
            if (sm[b] < middle)
            {
                return b * binHz;
            }
        }
        return (wallBin + 1) * binHz;
    }

    /// <summary>"suit", "suit à peine" or "ne suit pas", by correlation with the music.</summary>
    internal static string Following(double correlation) => correlation switch
    {
        >= ConfidenceModel.FollowsMusicCorrelation => "suit",
        > ConfidenceModel.IgnoresMusicCorrelation => "suit à peine",
        _ => "ne suit pas",
    };

    /// <summary>The averaged level beyond 21 kHz, in dB relative to the peak; null when the file stops short of it.</summary>
    private static double? HighBandLevel(ReadOnlySpan<double> db, double binHz, double nyquist)
    {
        int lo = (int)Math.Ceiling(FrameEvidence.HighBandLoHz / binHz);
        int hi = Math.Min(db.Length, (int)(Math.Min(FrameEvidence.HighBandHiHz, nyquist - 300) / binHz));
        if (hi <= lo)
        {
            return null;
        }
        double power = 0;
        for (int b = lo; b < hi; b++)
        {
            power += Math.Pow(10, db[b] / 10);
        }
        return 10 * Math.Log10(power / (hi - lo) + 1e-20);
    }

    /// <summary>
    /// Maps a cutoff to the encoder settings that typically produce it. Ranges
    /// overlap in reality, so the result is always phrased as approximate.
    /// </summary>
    /// <remarks>
    /// Set from the calibration of 2026-10-05, where the content of real music
    /// transcoded with LAME 3.100 reached (median): 16.6 kHz at 128 kbps and V5,
    /// 18.7 kHz at 192 kbps and V2, 19.4 kHz at 256 kbps, 19.9–20.0 kHz at 320 kbps and
    /// V0 — like ffmpeg's AAC, Opus and Vorbis q6 and above; Vorbis q3 reached 18.0 kHz.
    /// The older ranges read every LAME setting one tier too low.
    /// </remarks>
    public static string GuessEncoder(int cutoffHz) => cutoffHz switch
    {
        < 11500 => "≈64 kbps ou moins",
        < 14500 => "MP3 ≈80 kbps",
        < 15800 => "MP3 ≈96–112 kbps",
        < 17300 => "MP3 ≈128 kbps (V5)",
        < 18300 => "MP3 ≈160 kbps / Vorbis q3",
        < 19000 => "MP3 ≈192 kbps (V2)",
        < 19650 => "MP3 ≈256 kbps",
        < 20500 => "MP3 ≈320 kbps (V0), AAC, Opus ou Vorbis",
        _ => "encodeur avec perte à haut débit",
    };
}
