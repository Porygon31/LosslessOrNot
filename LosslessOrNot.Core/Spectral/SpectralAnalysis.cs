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

    /// <summary>Human-readable justification, shown next to the verdict.</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>
    /// The three tests a lossy encoder's wall must pass — abrupt, low enough, and
    /// landing in a digital floor — each with its value, meaning and threshold.
    /// </summary>
    public IReadOnlyList<AnalysisPoint> Criteria { get; init; } = [];

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
