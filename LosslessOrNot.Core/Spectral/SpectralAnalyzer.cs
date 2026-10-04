using LosslessOrNot.Core.Media;

namespace LosslessOrNot.Core.Spectral;

/// <summary>
/// Real FFT-based spectral analysis, ported from Antra's spectrum.go (v1.1.8 FEAT-5).
/// </summary>
/// <remarks>
/// It replaced a six-point frequency probe, which could only ever report a cutoff
/// from the fixed set {8k, 12k, 16k, 17k, 19k, 21k}. That resolution cannot tell a
/// 320 kbps MP3 (~20 kHz) from a 256 kbps AAC (~19 kHz) from a genuine lossless
/// master (Nyquist), and it produced a bare integer with no confidence attached, so
/// "Fake Lossless" was asserted from what was often a very weak signal.
///
/// This decodes real PCM through ffmpeg, windows it, runs an FFT, and averages the
/// magnitude spectrum over many frames. Every reported conclusion carries a
/// confidence and the evidence behind it, and the analysis explicitly reports
/// "inconclusive" rather than guessing.
/// </remarks>
public static class SpectralAnalyzer
{
    /// <summary>~11.7 Hz per bin at 48 kHz — fine enough to place a cutoff precisely.</summary>
    public const int FftSize = 4096;

    /// <summary>50 % overlap.</summary>
    public const int FftHop = 2048;

    /// <summary>Caps the work on long tracks: ~40 s of audio at 48 kHz.</summary>
    public const int MaxFrames = 900;

    /// <summary>Length of audio decoded for the analysis, in seconds.</summary>
    public const double AnalysisWindowSec = 45.0;

    /// <summary>
    /// Spectrogram resolution. 256 frequency rows is plenty for a display a few
    /// hundred pixels tall, and keeps the matrix small.
    /// </summary>
    public const int SpecRowCount = 256;

    /// <summary>dB floor of the spectrogram's byte quantisation.</summary>
    public const double SpecFloorDb = -100.0;

    /// <summary>Points kept in <see cref="SpectralAnalysis.AvgSpectrumDb"/>.</summary>
    public const int AvgSpectrumPoints = 512;

    /// <summary>Used when the sample rate is unknown.</summary>
    public const int DefaultSampleRate = 44100;

    // RMS below which a frame counts as silent.
    private const double SilenceRms = 1e-4;

    /// <summary>
    /// Decodes up to <see cref="AnalysisWindowSec"/> of <paramref name="filePath"/>
    /// through ffmpeg, at its native sample rate, and analyses it.
    /// </summary>
    public static async Task<SpectralAnalysis> AnalyzeFileAsync(
        string filePath,
        string ffmpegExe,
        int sampleRate,
        double durationSec,
        CancellationToken cancellationToken = default)
    {
        if (sampleRate <= 0)
        {
            sampleRate = DefaultSampleRate;
        }
        double start = AnalysisStartSec(durationSec);
        ReadOnlyMemory<float> samples = await PcmDecoder
            .DecodeMonoAsync(filePath, ffmpegExe, sampleRate, start, AnalysisWindowSec, cancellationToken)
            .ConfigureAwait(false);
        return Analyze(samples.Span, sampleRate, start);
    }

    /// <summary>
    /// Skips intros: analyses from a point likely to be dense — a quarter of the way
    /// in, at most a minute in, and from the start for tracks under 20 s.
    /// </summary>
    internal static double AnalysisStartSec(double durationSec)
    {
        // Written so that an unknown (0 or NaN) duration also starts at 0.
        if (!(durationSec >= 20))
        {
            return 0;
        }
        return Math.Min(durationSec * 0.25, 60);
    }

    /// <summary>Runs the full FFT pass over mono samples and derives an evidence-backed verdict.</summary>
    /// <param name="timeStartSec">Where <paramref name="samples"/> start in the track, for the time axis.</param>
    /// <exception cref="AudioAnalysisException">Too little audio, or nothing but silence.</exception>
    public static SpectralAnalysis Analyze(ReadOnlySpan<float> samples, int sampleRate, double timeStartSec = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (samples.Length < FftSize)
        {
            throw new AudioAnalysisException($"Pas assez d'audio décodé ({samples.Length} échantillons).");
        }

        double[] window = Fft.HannWindow(FftSize);
        const int bins = FftSize / 2;
        var acc = new double[bins];
        var power = new double[bins];
        var re = new double[FftSize];
        var im = new double[FftSize];

        // Per-frame spectra kept for the interactive spectrogram, max-pooled to
        // SpecRowCount bands as soon as each frame is computed. That is exactly what
        // the Go code computed — it kept every raw frame and pooled at the end — but
        // ~1.8 MB per track instead of ~15 MB: pooling uses the same band boundaries,
        // and the reference peak is still taken over the raw per-frame values.
        int specRows = Math.Min(SpecRowCount, bins);
        (int Lo, int Hi)[] bands = SpectrogramBands(bins, specRows);
        var pooledColumns = new List<double[]>(MaxFrames);
        double rawPeak = 0;
        int frames = 0;

        for (int off = 0; off + FftSize <= samples.Length && frames < MaxFrames; off += FftHop)
        {
            ReadOnlySpan<float> frame = samples.Slice(off, FftSize);

            // Skip near-silent frames: they carry no spectral information and would
            // drag the average down, making a real cutoff look lower than it is.
            double energy = 0;
            foreach (float s in frame)
            {
                energy += (double)s * s;
            }
            if (Math.Sqrt(energy / FftSize) < SilenceRms)
            {
                continue;
            }

            for (int i = 0; i < FftSize; i++)
            {
                re[i] = frame[i] * window[i];
                im[i] = 0;
            }
            Fft.Transform(re, im);

            for (int b = 0; b < bins; b++)
            {
                double p = re[b] * re[b] + im[b] * im[b];
                power[b] = p;
                acc[b] += p;
                if (p > rawPeak)
                {
                    rawPeak = p;
                }
            }
            pooledColumns.Add(MaxPool(power, bands));
            frames++;
        }
        if (frames == 0)
        {
            throw new AudioAnalysisException("Aucune trame audio non silencieuse à analyser.");
        }

        // Average power -> dB relative to the loudest bin.
        double peak = 0;
        for (int b = 0; b < bins; b++)
        {
            acc[b] /= frames;
            if (acc[b] > peak)
            {
                peak = acc[b];
            }
        }
        if (peak <= 0)
        {
            throw new AudioAnalysisException("Spectre silencieux.");
        }
        var db = new double[bins];
        for (int b = 0; b < bins; b++)
        {
            db[b] = 10 * Math.Log10(acc[b] / peak + 1e-20);
        }

        VerdictResult verdict = VerdictEngine.Derive(db, sampleRate, frames);
        return new SpectralAnalysis
        {
            SampleRate = sampleRate,
            NyquistHz = sampleRate / 2,
            CutoffHz = verdict.CutoffHz,
            CutoffConfidence = verdict.Confidence,
            CliffHz = verdict.CliffHz,
            ShelfDropDb = verdict.ShelfDropDb,
            EnergyAboveCutoffDb = verdict.EnergyAboveCutoffDb,
            Verdict = verdict.Verdict,
            LikelySource = verdict.LikelySource,
            Evidence = verdict.Evidence,
            AvgSpectrumDb = DownsampleSpectrum(db, AvgSpectrumPoints),
            FramesAnalyzed = frames,
            Columns = QuantizeColumns(pooledColumns, rawPeak),
            SpecFloorDb = SpecFloorDb,
            TimeStartSec = timeStartSec,
            TimeEndSec = timeStartSec + (double)frames * FftHop / sampleRate,
            FreqBinHz = (double)sampleRate / 2 / specRows,
        };
    }

    /// <summary>Row r of the spectrogram covers bins [Lo, Hi): the boundaries Go's quantiseColumns used.</summary>
    private static (int Lo, int Hi)[] SpectrogramBands(int bins, int rows)
    {
        var bands = new (int Lo, int Hi)[rows];
        double group = (double)bins / rows;
        for (int r = 0; r < rows; r++)
        {
            bands[r] = ((int)(r * group), Math.Min((int)((r + 1) * group), bins));
        }
        return bands;
    }

    // MAX-pooling, not averaging: averaging would hide a narrow but real
    // high-frequency component, which is exactly the signal a spectrogram exists
    // to show.
    private static double[] MaxPool(double[] power, (int Lo, int Hi)[] bands)
    {
        var pooled = new double[bands.Length];
        for (int r = 0; r < bands.Length; r++)
        {
            double m = 0;
            for (int j = bands[r].Lo; j < bands[r].Hi; j++)
            {
                if (power[j] > m)
                {
                    m = power[j];
                }
            }
            pooled[r] = m;
        }
        return pooled;
    }

    /// <summary>
    /// Turns pooled per-frame power into bytes: 0 = <see cref="SpecFloorDb"/>,
    /// 255 = <paramref name="rawPeak"/>.
    /// </summary>
    internal static byte[][] QuantizeColumns(IReadOnlyList<double[]> pooledColumns, double rawPeak)
    {
        // The reference level must come from the RAW per-frame values. Using the
        // averaged-spectrum peak here was a scale mismatch (raw frame power is much
        // larger than the frame-averaged power), which pushed every cell towards 255
        // and made the spectrogram a near-uniform bright block — the low-pass was
        // invisible.
        if (pooledColumns.Count == 0 || rawPeak <= 0)
        {
            return [];
        }
        var output = new byte[pooledColumns.Count][];
        for (int i = 0; i < output.Length; i++)
        {
            double[] column = pooledColumns[i];
            var row = new byte[column.Length];
            for (int r = 0; r < column.Length; r++)
            {
                double d = 10 * Math.Log10(column[r] / rawPeak + 1e-20);
                // Map [SpecFloorDb, 0] dB onto [0, 255].
                double v = Math.Clamp((d - SpecFloorDb) / (0 - SpecFloorDb), 0, 1);
                row[r] = (byte)(v * 255);
            }
            output[i] = row;
        }
        return output;
    }

    /// <summary>
    /// Reduces the spectrum to <paramref name="target"/> points by max-pooling, so a
    /// narrow but real high-frequency component is not averaged into invisibility.
    /// </summary>
    internal static double[] DownsampleSpectrum(ReadOnlySpan<double> db, int target)
    {
        if (db.Length <= target)
        {
            return db.ToArray();
        }
        var output = new double[target];
        double group = (double)db.Length / target;
        for (int i = 0; i < target; i++)
        {
            int lo = (int)(i * group);
            int hi = Math.Min((int)((i + 1) * group), db.Length);
            double m = double.NegativeInfinity;
            for (int j = lo; j < hi; j++)
            {
                if (db[j] > m)
                {
                    m = db[j];
                }
            }
            output[i] = m;
        }
        return output;
    }
}
