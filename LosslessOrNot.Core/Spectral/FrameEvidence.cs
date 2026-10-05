namespace LosslessOrNot.Core.Spectral;

/// <summary>How the region above a wall behaves from frame to frame.</summary>
/// <param name="Persistence">
/// Among the quarter of frames richest in content just below the wall, the share
/// where the wall still holds — at least <see cref="FrameEvidence.PersistenceDropDb"/>
/// between the bands on either side of it.
/// </param>
/// <param name="Correlation">
/// Spearman correlation between the level just below the wall and the level above
/// it: attenuated music follows the programme, a codec's floor or added dither does not.
/// </param>
/// <param name="MusicSpreadDb">How much the level just below the wall varies (P90 − P10).</param>
/// <param name="FloorSpreadDb">How much the level above the wall varies (P90 − P10).</param>
/// <param name="DepthDb">
/// How far the region above the wall lies below the music under it, in the frames
/// richest in treble (median).
/// </param>
internal readonly record struct WallTracking(
    double Persistence,
    double Correlation,
    double MusicSpreadDb,
    double FloorSpreadDb,
    double DepthDb)
{
    /// <summary>Whether the music varies enough for a correlation with it to mean anything.</summary>
    public bool HasDynamics => MusicSpreadDb >= FrameEvidence.MinMusicSpreadDb;
}

/// <summary>Whether the content beyond 21 kHz follows the music.</summary>
/// <param name="Correlation">Spearman correlation between the 12–20 kHz band and the band beyond 21 kHz.</param>
/// <param name="MusicSpreadDb">How much the 12–20 kHz band varies (P90 − P10).</param>
/// <param name="DepthDb">How far the band beyond 21 kHz lies below the 12–20 kHz band (median over frames).</param>
internal readonly record struct HighBandTracking(double Correlation, double MusicSpreadDb, double DepthDb)
{
    public bool HasDynamics => MusicSpreadDb >= FrameEvidence.MinMusicSpreadDb;
}

/// <summary>
/// Reads <see cref="FrameBands"/> around the wall found on the averaged spectrum,
/// and beyond 21 kHz.
/// </summary>
internal static class FrameEvidence
{
    /// <summary>Below this many kept frames, nothing is measured: the statistics would be noise.</summary>
    public const int MinFrames = 30;

    /// <summary>Music varying less than this gives a correlation nothing to follow.</summary>
    public const double MinMusicSpreadDb = 6.0;

    /// <summary>The level difference across the wall that counts as "the wall holds".</summary>
    public const double PersistenceDropDb = 20.0;

    /// <summary>
    /// An image of the music this far below it is no content: it is what a decoder's
    /// filter bank lets through above its cut. Measured on 24-bit decodes, where the
    /// rounding noise no longer hides it, with the music right under the cut as the
    /// reference: MP3 192 kbps 90 dB, AAC 128 kbps 93 dB, Vorbis q5 105 dB, Opus
    /// 128 kbps 74 dB.
    /// </summary>
    public const double LeakageDepthDb = 65.0;

    // The band just below the wall, where the music is: the wall's first bin is the
    // start of its steepest kHz, so the content ends there.
    private const double BelowSpanHz = 1500.0;
    private const double BelowGapHz = 100.0;

    // The band above the wall: the same zone as the averaged floor, which starts
    // 1 kHz up so as to clear the encoders' transition band.
    private const double AboveStartHz = 1000.0;
    private const double AboveEndHz = 3000.0;
    private const double NyquistMarginHz = 200.0;
    private const double MinBandHz = 300.0;

    private const double TopShare = 0.25;

    /// <summary>The band where only a genuine master carries music, and the reference it must follow.</summary>
    public const double HighBandLoHz = 21000.0;
    public const double HighBandHiHz = 22000.0;
    public const double ReferenceLoHz = 12000.0;
    public const double ReferenceHiHz = 20000.0;

    /// <returns>Null when there are too few frames or no room above the wall.</returns>
    public static WallTracking? TrackWall(FrameBands bands, double wallHz, double nyquistHz)
    {
        if (bands.Count < MinFrames || wallHz <= 0)
        {
            return null;
        }
        double aboveLo = wallHz + AboveStartHz;
        double aboveHi = Math.Min(wallHz + AboveEndHz, Math.Min(nyquistHz - NyquistMarginHz, FrameBands.TopHz));
        if (aboveHi - aboveLo < MinBandHz)
        {
            return null;
        }
        double[] below = bands.Levels(wallHz - BelowSpanHz, wallHz - BelowGapHz);
        double[] above = bands.Levels(aboveLo, aboveHi);
        if (below.Length == 0 || above.Length == 0)
        {
            return null;
        }

        // The frames richest in content just below the wall are where a natural
        // roll-off would spill over it; a codec's wall holds in every one of them.
        double richest = SignalMath.Percentile(below, 1 - TopShare);
        var depths = new List<double>();
        int held = 0;
        for (int i = 0; i < below.Length; i++)
        {
            if (below[i] >= richest)
            {
                double depth = below[i] - above[i];
                depths.Add(depth);
                if (depth >= PersistenceDropDb)
                {
                    held++;
                }
            }
        }
        return new WallTracking(
            depths.Count > 0 ? (double)held / depths.Count : 0,
            SignalMath.SpearmanCorrelation(below, above),
            SignalMath.Spread(below),
            SignalMath.Spread(above),
            SignalMath.Percentile(depths.ToArray(), 0.5));
    }

    /// <returns>Null when there are too few frames or the file stops short of 21 kHz.</returns>
    public static HighBandTracking? TrackHighBand(FrameBands bands, double nyquistHz)
    {
        double hi = Math.Min(HighBandHiHz, nyquistHz - 300);
        if (bands.Count < MinFrames || hi - HighBandLoHz < MinBandHz)
        {
            return null;
        }
        double[] high = bands.Levels(HighBandLoHz, hi);
        double[] reference = bands.Levels(ReferenceLoHz, ReferenceHiHz);
        if (high.Length == 0 || reference.Length == 0)
        {
            return null;
        }
        double[] depths = reference.Zip(high, (r, h) => r - h).ToArray();
        return new HighBandTracking(
            SignalMath.SpearmanCorrelation(reference, high), SignalMath.Spread(reference), SignalMath.Percentile(depths, 0.5));
    }
}
