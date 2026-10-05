namespace LosslessOrNot.Core.Spectral;

/// <summary>
/// Deliberately three-valued: when the evidence is weak the analysis says so
/// instead of guessing.
/// </summary>
public enum SpectralVerdict
{
    Inconclusive,
    Lossless,
    Lossy,
}

/// <summary>The honest, evidence-carrying result of the FFT pass.</summary>
public sealed class SpectralAnalysis
{
    public int SampleRate { get; init; }

    public int NyquistHz { get; init; }

    /// <summary>The highest frequency carrying sustained energy. 0 = unknown.</summary>
    public int CutoffHz { get; init; }

    /// <summary>Confidence 0..1 for the verdict. Low values MUST NOT be presented as fact.</summary>
    public double CutoffConfidence { get; init; }

    /// <summary>
    /// Where the steepest drop was found — the low-pass wall itself. Reported
    /// separately from <see cref="CutoffHz"/> because they are different
    /// measurements and conflating them hid a bug.
    /// </summary>
    public int CliffHz { get; init; }

    /// <summary>
    /// How sharply energy falls across the wall, in dB over 1 kHz. A steep drop is
    /// the signature of a lossy encoder's low-pass; a gentle roll-off is normal
    /// programme material.
    /// </summary>
    public double ShelfDropDb { get; init; }

    /// <summary>Residual level just above the wall, relative to the spectrum peak.</summary>
    public double EnergyAboveCutoffDb { get; init; }

    public SpectralVerdict Verdict { get; init; }

    /// <summary>For example "MP3 ≈320 kbps"; empty unless the verdict is lossy.</summary>
    public string LikelySource { get; init; } = "";

    /// <summary>
    /// The probability, 0..1, that the audio went through a lossy encoder: every
    /// indicator's weight, added up in log-odds. Whatever the verdict.
    /// </summary>
    public double LossyProbability { get; init; }

    /// <summary>Whether the three criteria of a codec's wall are all met.</summary>
    public bool HasCodecWall { get; init; }

    /// <summary>The integer depth the analysis assumed; 0 when unknown.</summary>
    public int BitsPerSample { get; init; }

    /// <summary>
    /// The level content must exceed, in dB relative to the peak: -70 dB, or 10 dB
    /// above the rounding noise when that is higher.
    /// </summary>
    public double ContentThresholdDb { get; init; }

    /// <summary>The rounding noise of the file's bit depth in each bin, in dB relative to the peak; null when unknown.</summary>
    public double? QuantizationFloorDb { get; init; }

    /// <summary>The floor above the wall compared with that rounding noise, in dB; null when unknown.</summary>
    public double? FloorAboveQuantizationDb { get; init; }

    /// <summary>
    /// Spearman correlation, frame by frame, between the music just below the wall and
    /// the residue above it; null without a wall, or without enough frames or dynamics.
    /// </summary>
    public double? FloorMusicCorrelation { get; init; }

    /// <summary>Share of the treble-richest frames where the wall holds; null when it cannot be measured.</summary>
    public double? WallPersistence { get; init; }

    /// <summary>
    /// How far the region above the wall lies below the music under it, in the frames
    /// richest in treble; null when it cannot be measured.
    /// </summary>
    public double? FloorDepthDb { get; init; }

    /// <summary>
    /// How the averaged spectrum slopes, in dB per kHz, from 1 to 5 kHz above the wall:
    /// music keeps falling, clipping distortion and dither do not. Null without a wall
    /// or room above it.
    /// </summary>
    public double? FloorSlopeDbPerKHz { get; init; }

    /// <summary>Every raw measurement, for the calibration tool: the public ones above are filtered for display.</summary>
    internal Measurements? Measurements { get; init; }

    /// <summary>The weight of every indicator, for the calibration tool.</summary>
    internal Score? Score { get; init; }

    /// <summary>
    /// Spearman correlation between the 12–20 kHz band and the energy beyond 21 kHz;
    /// null when it cannot be measured.
    /// </summary>
    public double? HighBandMusicCorrelation { get; init; }

    /// <summary>Human-readable justification, shown next to the verdict.</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>
    /// The three tests a lossy encoder's wall must pass — abrupt, low enough, and
    /// nothing musical above it — each with its value, meaning, threshold and weight.
    /// </summary>
    public IReadOnlyList<AnalysisPoint> Criteria { get; init; } = [];

    /// <summary>
    /// The frame-by-frame indicators that strengthen or weaken the criteria: whether
    /// the wall holds at every instant, and whether content beyond 21 kHz is music.
    /// </summary>
    public IReadOnlyList<AnalysisPoint> SupportingEvidence { get; init; } = [];

    /// <summary>Everything else that informs the verdict: content reach, confidence, audio analysed…</summary>
    public IReadOnlyList<AnalysisPoint> Observations { get; init; } = [];

    /// <summary>One sentence tying the criteria to the verdict.</summary>
    public string Conclusion { get; init; } = "";

    /// <summary>Seconds the user asked to analyse.</summary>
    public double RequestedSec { get; init; }

    /// <summary>Seconds actually decoded and analysed; less than requested for a short file.</summary>
    public double AnalyzedSec { get; init; }

    /// <summary>The file's duration as ffprobe reported it; 0 when unknown.</summary>
    public double FileDurationSec { get; init; }

    /// <summary>
    /// The averaged magnitude spectrum in dB relative to its peak, max-pooled to at
    /// most <see cref="SpectralAnalyzer.AvgSpectrumPoints"/> points spanning 0..Nyquist.
    /// </summary>
    public double[] AvgSpectrumDb { get; init; } = [];

    /// <summary>How much evidence the verdict rests on.</summary>
    public int FramesAnalyzed { get; init; }

    /// <summary>Samples in each of those frames; they overlap by half.</summary>
    public int FftSize { get; init; } = SpectralAnalyzer.DefaultFftSize;

    // ── Interactive spectrogram data ─────────────────────────────────────────

    /// <summary>
    /// Time × frequency matrix of dB values, quantised to bytes: a full double
    /// matrix would be tens of MB for a single track, whereas the display only ever
    /// resolves roughly 1 dB steps. 0 = <see cref="SpecFloorDb"/>, 255 = 0 dB, and
    /// <c>Columns[t][0]</c> is the lowest frequency band.
    /// </summary>
    public byte[][] Columns { get; init; } = [];

    public double SpecFloorDb { get; init; }

    /// <summary>
    /// Locate the analysed window inside the track, so a hover readout can report a
    /// real timestamp rather than an offset.
    /// </summary>
    public double TimeStartSec { get; init; }

    public double TimeEndSec { get; init; }

    /// <summary>The width of one spectrogram row, in Hz.</summary>
    public double FreqBinHz { get; init; }
}
