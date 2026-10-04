using System.Diagnostics;
using static LosslessOrNot.Core.TextFormat;

namespace LosslessOrNot.Core.Spectral;

/// <summary>The measurements and conclusion <see cref="VerdictEngine"/> derives from a spectrum.</summary>
internal sealed record VerdictResult(
    int CutoffHz,
    int CliffHz,
    double ShelfDropDb,
    double EnergyAboveCutoffDb,
    SpectralVerdict Verdict,
    string LikelySource,
    double Confidence,
    IReadOnlyList<string> Evidence);

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

    /// <summary>A codec ZEROES the bins above its wall.</summary>
    public const double CodecFloorDb = -88.0;

    /// <summary>"Content reaches this far", relative to the peak.</summary>
    public const double ContentFloorDb = -70.0;

    /// <summary>Content past here is out of reach of lossy codecs.</summary>
    public const double LosslessTopHz = 20500.0;

    /// <summary>Below this many frames the confidence is scaled down.</summary>
    public const int MinFramesForFullConfidence = 60;

    /// <param name="db">Averaged power spectrum in dB relative to its peak, bin 0 = DC.</param>
    internal static VerdictResult Derive(ReadOnlySpan<double> db, int sampleRate, int framesAnalyzed)
    {
        int bins = db.Length;
        double binHz = (double)sampleRate / 2 / bins;
        double nyquist = (double)sampleRate / 2;

        // Smooth over ~200 Hz to suppress bin-level noise without blurring a shelf.
        int smoothN = Math.Max(1, (int)(200 / binHz + 0.5));
        double[] sm = SignalMath.MovingAverage(db, smoothN);

        // ── 1. How far up the band does real content reach? ──────────────────────
        // This is the number a person reads off a Spek plot: where the picture goes
        // black. The previous definition — the first point 3 dB below the median of
        // the whole 2 kHz..mid band — is meaningless for music, because that median
        // is dominated by bass and mids sitting 20-30 dB above the treble. It
        // reported 11-14 kHz for *every* file measured, lossless and lossy alike.
        double contentTop = 0;
        for (int b = bins - 1; b >= 0; b--)
        {
            if (sm[b] > ContentFloorDb)
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

        Debug.WriteLine(
            $"[spectrum] contentTop={contentTop:0} wallHz={wallHz:0} drop={wallDrop:0.0} floorAbove={floorAbove:0.0} nyq={nyquist:0}");

        // ── 3. Verdict ───────────────────────────────────────────────────────────
        // "Lossy" requires all three independent signals to agree: the wall is
        // abrupt, it sits where a codec could have put it, and it lands in a true
        // digital floor. Requiring all three is what stops a steep but natural
        // mastering roll-off — the hi-res case — from being condemned.
        bool isCodecWall = wallDrop >= WallDropMinDb
            && wallHz > 0 && wallHz <= CodecMaxWallHz
            && floorAbove <= CodecFloorDb;

        int cutoffHz = (int)contentTop;
        SpectralVerdict verdict;
        string likelySource = "";
        double confidence = 0.30;
        if (isCodecWall)
        {
            verdict = SpectralVerdict.Lossy;
            // Guess from where content actually ends, not from the steepest-drop bin:
            // the latter sits below the corner and biases every guess a tier low.
            likelySource = GuessEncoder(cutoffHz);
            confidence = wallDrop switch
            {
                >= 35 => 0.95,
                >= 27 => 0.85,
                _ => 0.75,
            };
        }
        else if (contentTop >= LosslessTopHz)
        {
            verdict = SpectralVerdict.Lossless;
            confidence = contentTop >= 21500 ? 0.90 : 0.75;
        }
        else
        {
            verdict = SpectralVerdict.Inconclusive;
        }

        var evidence = new List<string>();
        if (framesAnalyzed < MinFramesForFullConfidence)
        {
            confidence *= 0.6;
            evidence.Add(Fr($"Seulement {framesAnalyzed} trames d'analyse disponibles — confiance réduite."));
        }
        evidence.Add(Fr(
            $"Le contenu monte jusqu'à {contentTop / 1000:0.0} kHz (Nyquist de ce fichier : {nyquist / 1000:0.0} kHz)."));
        if (wallHz > 0)
        {
            evidence.Add(Fr(
                $"Chute la plus raide : {wallDrop:0.0} dB sur 1 kHz à partir de {wallHz / 1000:0.0} kHz, pour retomber à {floorAbove:0} dB."));
        }
        evidence.Add(Fr(
            $"Moyenne de {framesAnalyzed} trames FFT de {SpectralAnalyzer.FftSize} points ({binHz:0} Hz par bin)."));
        if (nyquist > 25000)
        {
            evidence.Add(
                "À cette fréquence d'échantillonnage, la bande au-delà de ~24 kHz est normalement vide : c'est une "
                + "propriété de l'enregistrement, pas de l'encodage, et ce n'est pas un indice de transcodage.");
        }
        evidence.Add(verdict switch
        {
            SpectralVerdict.Lossless =>
                "Le contenu dépasse 20,5 kHz, hors de portée des encodeurs avec perte grand public : "
                + "cohérent avec un vrai master lossless.",
            SpectralVerdict.Lossy =>
                "Un mur aussi abrupt, placé aussi bas et retombant dans un plancher quasi silencieux, est la "
                + "signature d'un encodeur avec perte, pas celle du programme musical.",
            _ =>
                "La coupure n'est pas assez nette, ou ne descend pas assez bas, pour distinguer un encodeur avec "
                + "perte d'un programme musical pauvre en aigus.",
        });

        return new VerdictResult(
            cutoffHz, (int)wallHz, wallDrop, floorAbove, verdict, likelySource, confidence, evidence);
    }

    /// <summary>
    /// Maps a cutoff to the encoder settings that typically produce it. Ranges
    /// overlap in reality, so the result is always phrased as approximate.
    /// </summary>
    public static string GuessEncoder(int cutoffHz) => cutoffHz switch
    {
        < 11500 => "≈64 kbps ou moins",
        < 15000 => "MP3 ≈128 kbps",
        < 16500 => "MP3 ≈160 kbps / AAC ≈96 kbps",
        < 18000 => "MP3 ≈192 kbps / AAC ≈128 kbps",
        < 19500 => "AAC ≈256 kbps",
        < 20500 => "MP3 ≈320 kbps",
        _ => "encodeur avec perte à haut débit",
    };
}
