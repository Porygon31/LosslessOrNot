namespace LosslessOrNot.Core.Spectral;

/// <summary>What the region above a wall holds.</summary>
internal enum FloorKind
{
    /// <summary>No steep wall: the region above the steepest kHz means nothing.</summary>
    NotApplicable,

    /// <summary>Digital silence: at most −88 dB of the peak, or close to the format's rounding noise.</summary>
    DigitalSilence,

    /// <summary>A constant noise that ignores the music: dither added once everything above was removed.</summary>
    StationaryNoise,

    /// <summary>
    /// A faint image of the music, at least <see cref="FrameEvidence.LeakageDepthDb"/>
    /// below it: what a decoder's filter bank lets through above its cut.
    /// </summary>
    Leakage,

    /// <summary>
    /// A residue that follows the music but lies flat, instead of falling as music's
    /// treble does: clipping distortion, made when a lossy decode of a loud master
    /// overshoots full scale and is written to integers.
    /// </summary>
    Distortion,

    /// <summary>A residue rising and falling with the music, close below it: attenuated programme.</summary>
    FollowsMusic,

    /// <summary>Something remains, and nothing shows what it is.</summary>
    Residual,
}

/// <summary>
/// The weight of every indicator in log-odds of a lossy source — positive points to
/// an encoder, negative to a genuine master, 0 is neutral — and what they add up to.
/// </summary>
/// <param name="HasCodecWall">
/// The three criteria a codec's wall must meet: abrupt, at most 21 kHz, and nothing
/// musical above it. A lossy verdict requires them all.
/// </param>
/// <param name="ZoneCap">
/// The ceiling the wall's position put on the probability, when it sits where
/// encoders and sample-rate converters cut alike; null elsewhere.
/// </param>
internal sealed record Score(
    double Steepness,
    double Position,
    double Floor,
    double Persistence,
    double HighBand,
    FloorKind FloorKind,
    bool HasCodecWall,
    double LossyProbability,
    double? ZoneCap = null);

/// <summary>
/// Turns the measurements into the probability that the file went through a lossy
/// encoder. Each indicator adds its weight of evidence in log-odds, as in a naive
/// Bayes classifier or a logistic regression, so the confidence grows with the
/// amount of agreeing evidence instead of following a single measurement.
/// </summary>
/// <remarks>
/// Each indicator maps its measurement onto a contribution through anchor points,
/// interpolated linearly and held flat beyond the first and last. The anchors were
/// set by hand from the measurements made so far — MP3 320 lands at −98 dB, MP3 128
/// at −91 dB, a lossless 96 kHz master's roll-off at −85 dB — and each indicator can
/// be scaled by a weight that the calibration tool fits.
///
/// Calibration of 2026-10-05: 173 genuine lossless files (mostly electronic music,
/// 44.1 kHz, many mastered around −5 dBFS RMS) and 3,460 transcodes of them (LAME,
/// ffmpeg AAC, Vorbis, Opus; 16 and 24 bits, with and without dither). It led to
/// three changes, every weight staying at 1: a smaller drop counts as a wall when it
/// lands on a flat floor, a flat floor counts as nothing musical (241 and 265 more
/// fakes caught, no genuine file affected), and the probability is capped where
/// sample-rate converters cut (<see cref="ResamplerZoneCaps"/>). The fitted weights
/// were not kept: no weighting of these additive contributions can express that
/// zone, and the fit only inflated every weight. With these rules, 3 genuine files
/// of 173 come out "fake" (instead of 46 with the rules ported from Go), all with a
/// wall below 17 kHz; MP3 at 128–256 kbps and V2–V5 are caught 70–96 % of the time,
/// and 96 % of the files given a probability above 90 % were indeed transcodes.
/// </remarks>
internal static class ConfidenceModel
{
    // Fitted by LosslessOrNot.Calibration: 1 keeps an indicator's anchors as they are.
    public const double Intercept = 0.0;
    public const double SteepnessWeight = 1.0;
    public const double PositionWeight = 1.0;
    public const double FloorWeight = 1.0;
    public const double PersistenceWeight = 1.0;
    public const double HighBandWeight = 1.0;

    /// <summary>
    /// The probability never reaches 0 or 1: a spectrum alone cannot tell a codec's
    /// wall from a digital brick-wall filter applied at mastering.
    /// </summary>
    public const double MinProbability = 0.01;

    public const double MaxProbability = 0.99;

    /// <summary>
    /// A floor within this much of the format's rounding noise holds nothing else. It
    /// covers ffmpeg's mono downmix (0.5 or 0.707 per channel, channel noises
    /// correlated or not: ±3 dB) and TPDF dither (+4.8 dB).
    /// </summary>
    public const double QuantizationMarginDb = 9.0;

    /// <summary>At or above this correlation, the residue above the wall follows the music.</summary>
    public const double FollowsMusicCorrelation = 0.5;

    /// <summary>At or below this correlation, the residue ignores the music.</summary>
    public const double IgnoresMusicCorrelation = 0.2;

    /// <summary>A floor varying less than this (P90 − P10) is stationary.</summary>
    public const double StationarySpreadDb = 3.0;

    /// <summary>
    /// Above a wall, music keeps falling — measured: a loud DnB master at −5 dB/kHz
    /// from 17 to 21 kHz. Clipping distortion lies flat (the same master's MP3 decode:
    /// within 1 dB from 17 to 22 kHz), and noise-shaped dither rises. A slope at
    /// least this high is no music.
    /// </summary>
    public const double FlatSlopeDbPerKHz = -1.5;

    // dB lost over 1 kHz.
    private static readonly (double X, double Y)[] SteepnessAnchors =
        [(10, -1.5), (20, 0.5), (27, 1.5), (35, 2.5), (50, 3.0)];

    // Hz. No genuine master has a wall below 19.5 kHz; between 20 and 21 kHz, MP3
    // 320 kbps and the anti-alias filters of CD masters overlap; past 22 kHz no
    // consumer encoder cuts, so it outweighs the wall's steepness.
    private static readonly (double X, double Y)[] PositionAnchors =
        [(19500, 1.0), (20500, 0.0), (21000, -0.5), (22000, -4.0)];

    // Hz. From 19.1 to 21 kHz, the walls of high-bitrate encoders (MP3 320 kbps and
    // V0, AAC, Opus, Vorbis q6 and above) and those of sample-rate converters (a
    // master made at 48 kHz and delivered at 44.1 kHz) are indistinguishable. Measured
    // on the calibration corpus: a codec-like wall between 19.1 and 20 kHz was found
    // in 17.9 % of genuine files and 15.1 % of transcodes, between 20 and 21 kHz in
    // 4.6 % and 2.1 % — a likelihood ratio of 0.84 and 0.46, that is a probability of
    // 0.46 and 0.31 with even odds. The probability is capped there, so that such a
    // file is at best "suspect". Below 19 kHz, no genuine file had a wall.
    internal static readonly (double X, double Y)[] ResamplerZoneCaps =
        [(19000, MaxProbability), (19100, 0.55), (20000, 0.55), (20100, 0.50), (21000, 0.50)];

    /// <summary>Where the zone shared by encoders and sample-rate converters starts.</summary>
    public const double ResamplerZoneStartHz = 19100;

    // dB relative to the peak.
    private static readonly (double X, double Y)[] FloorVsPeakAnchors =
        [(-100, 2.0), (-88, 0.5), (-80, -1.0)];

    // dB above the format's rounding noise.
    private static readonly (double X, double Y)[] FloorVsQuantizationAnchors =
        [(0, 2.0), (QuantizationMarginDb, 0.5), (20, -1.0)];

    // Correlation between the music just below the wall and the residue above it.
    private static readonly (double X, double Y)[] FloorTrackingAnchors =
        [(0.15, 1.5), (0.3, 0.0), (FollowsMusicCorrelation, -2.0), (0.7, -4.0)];

    // Share of the HF-richest frames where the wall holds.
    private static readonly (double X, double Y)[] PersistenceAnchors =
        [(0.6, -1.0), (0.9, 0.0), (1.0, 1.0)];

    // Hz reached by the content: the old "content beyond 20.5 kHz" rule.
    private static readonly (double X, double Y)[] ReachAnchors =
        [(20500, -0.8), (21500, -1.5)];

    // How much of the reach evidence survives, by how well the energy beyond 21 kHz
    // follows the music: none when it is mere noise, double when it is music.
    private static readonly (double X, double Y)[] HighBandTrackingFactors =
        [(IgnoresMusicCorrelation, 0.0), (FollowsMusicCorrelation, 2.0)];

    public static Score Evaluate(Measurements m)
    {
        double steepness = SteepnessWeight * Interpolate(m.WallHz > 0 ? m.WallDropDb : 0, SteepnessAnchors);
        double position = 0, floor = 0, persistence = 0;
        FloorKind kind = FloorKind.NotApplicable;
        if (m.IsSteep)
        {
            position = PositionWeight * Interpolate(m.WallHz, PositionAnchors);
        }
        // What lies above a wall, and whether it holds, only say something about an
        // encoder when the wall sits where encoders cut: past 21 kHz it is the
        // anti-alias filter of a resampler or of the recording itself.
        if (IsCodecCandidate(m))
        {
            (kind, double floorEvidence) = AssessFloor(m);
            floor = FloorWeight * floorEvidence;
            if (m.Wall is { } wall)
            {
                persistence = PersistenceWeight * Interpolate(wall.Persistence, PersistenceAnchors);
            }
        }
        double highBand = HighBandWeight * HighBandEvidence(m);

        bool codecWall = IsCodecCandidate(m) && IsCodecFloor(kind);
        double logOdds = Intercept + steepness + position + floor + persistence + highBand;
        double probability = Math.Clamp(Logistic(logOdds), MinProbability, MaxProbability);
        double? zoneCap = null;
        if (codecWall && m.WallHz > ResamplerZoneStartHz - 100)
        {
            zoneCap = Interpolate(m.WallHz, ResamplerZoneCaps);
            probability = Math.Min(probability, zoneCap.Value);
        }
        return new Score(steepness, position, floor, persistence, highBand, kind, codecWall, probability, zoneCap);
    }

    /// <summary>An abrupt wall where lossy encoders cut: the first two criteria of a codec's wall.</summary>
    public static bool IsCodecCandidate(Measurements m) => m.IsSteep && m.WallHz <= VerdictEngine.CodecMaxWallHz;

    /// <summary>Whether what lies above a wall is nothing musical: the third criterion.</summary>
    public static bool IsCodecFloor(FloorKind kind) =>
        kind is FloorKind.DigitalSilence or FloorKind.StationaryNoise or FloorKind.Leakage or FloorKind.Distortion;

    /// <summary>
    /// What lies above the wall, and how much that says. Its level alone cannot tell
    /// silence from a quiet natural roll-off: whether it follows the music, and how
    /// far below it, can.
    /// </summary>
    /// <remarks>
    /// Following the music is only evidence of a natural roll-off close below the
    /// music. Far below it, every decoder leaves such an image of the music above its
    /// cut — a 24-bit decode shows it, a 16-bit one buries it in rounding noise.
    /// </remarks>
    private static (FloorKind Kind, double Evidence) AssessFloor(Measurements m)
    {
        double level = Interpolate(m.FloorAboveDb, FloorVsPeakAnchors);
        if (m.FloorAboveQuantizationDb is { } aboveQuantization)
        {
            level = Math.Max(level, Interpolate(aboveQuantization, FloorVsQuantizationAnchors));
        }
        WallTracking? tracking = m.Wall is { HasDynamics: true } wall ? wall : null;
        bool deep = tracking is { DepthDb: >= FrameEvidence.LeakageDepthDb };
        double follows = tracking is { } t && !deep ? Interpolate(t.Correlation, FloorTrackingAnchors) : 0;

        bool silent = m.FloorAboveDb <= VerdictEngine.CodecFloorDb || m.FloorAboveQuantizationDb <= QuantizationMarginDb;
        bool followsMusic = tracking is { Correlation: >= FollowsMusicCorrelation, FloorSpreadDb: >= StationarySpreadDb };
        bool flat = m.FloorSlopeDbPerKHz >= FlatSlopeDbPerKHz;
        FloorKind kind = tracking switch
        {
            _ when followsMusic && !deep && !flat => FloorKind.FollowsMusic,
            _ when silent => FloorKind.DigitalSilence,
            _ when followsMusic && deep => FloorKind.Leakage,
            _ when followsMusic => FloorKind.Distortion,
            { Correlation: <= IgnoresMusicCorrelation, FloorSpreadDb: < StationarySpreadDb } => FloorKind.StationaryNoise,
            // Whatever lies flat above a wall is no music, which keeps falling:
            // distortion when it partly follows the music, noise otherwise.
            { Correlation: > IgnoresMusicCorrelation } when flat => FloorKind.Distortion,
            _ when flat => FloorKind.StationaryNoise,
            _ => FloorKind.Residual,
        };
        return kind switch
        {
            // Deep, but attenuated music all the same: its depth proves nothing.
            FloorKind.FollowsMusic => (kind, Math.Min(level, 0) + follows),
            // Dither, a decoder's leakage or clipping distortion is expected to sit
            // above silence: its level is no evidence against.
            FloorKind.StationaryNoise => (kind, Math.Max(level, 0) + follows),
            FloorKind.Leakage or FloorKind.Distortion => (kind, Math.Max(level, 0) + ImpureFloorEvidence),
            _ => (kind, level + follows),
        };
    }

    /// <summary>
    /// A faint image of the music is what decoders leave, and flat distortion what
    /// clipping a decode leaves — but a digital brick-wall filter, or a mastering
    /// low-pass before a clipper, leaves the same: mild evidence.
    /// </summary>
    private const double ImpureFloorEvidence = 1.0;

    /// <summary>
    /// Content beyond what lossy encoders pass is evidence of a genuine master — when
    /// it is music. Energy that ignores the music, or sits at the format's rounding
    /// noise, is dither or hiss, and proves nothing.
    /// </summary>
    private static double HighBandEvidence(Measurements m)
    {
        // Above an abrupt wall where encoders cut, whatever reaches beyond 21 kHz is
        // what lies above that wall — clipping distortion of a loud decode follows
        // the music too — and the third criterion already judges it.
        if (m.ContentTopHz < VerdictEngine.LosslessTopHz || IsCodecCandidate(m))
        {
            return 0;
        }
        double reach = Interpolate(m.ContentTopHz, ReachAnchors);
        if (m.HighBandAboveQuantizationDb <= QuantizationMarginDb
            || m.HighBand is { DepthDb: >= FrameEvidence.LeakageDepthDb })
        {
            return 0;
        }
        return m.HighBand is { HasDynamics: true } high
            ? reach * Interpolate(high.Correlation, HighBandTrackingFactors)
            : reach;
    }

    /// <summary>Linear between anchors sorted by X, flat beyond the first and the last.</summary>
    internal static double Interpolate(double x, ReadOnlySpan<(double X, double Y)> anchors)
    {
        if (double.IsNaN(x) || x <= anchors[0].X)
        {
            return anchors[0].Y;
        }
        for (int i = 1; i < anchors.Length; i++)
        {
            if (x <= anchors[i].X)
            {
                (double x0, double y0) = anchors[i - 1];
                (double x1, double y1) = anchors[i];
                return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
            }
        }
        return anchors[^1].Y;
    }

    public static double Logistic(double logOdds) => 1 / (1 + Math.Exp(-logOdds));
}
