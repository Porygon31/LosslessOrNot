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

    /// <summary>Seconds of audio analysed unless the user chooses otherwise.</summary>
    public const double DefaultWindowSec = 45.0;

    /// <summary>Shortest window the user may choose: still well over the 60 frames of full confidence.</summary>
    public const double MinWindowSec = 5.0;

    /// <summary>Longest window the user may choose.</summary>
    public const double MaxWindowSec = 600.0;

    /// <summary>
    /// Spectrogram resolution. 256 frequency rows is plenty for a display a few
    /// hundred pixels tall, and keeps the matrix small.
    /// </summary>
    public const int SpecRowCount = 256;

    /// <summary>
    /// Most spectrogram columns kept: beyond this, neighbouring frames share a
    /// column, so a long window still yields a matrix of at most 512 KB.
    /// </summary>
    public const int MaxColumns = 2048;

    /// <summary>dB floor of the spectrogram's byte quantisation.</summary>
    public const double SpecFloorDb = -100.0;

    /// <summary>Points kept in <see cref="SpectralAnalysis.AvgSpectrumDb"/>.</summary>
    public const int AvgSpectrumPoints = 512;

    /// <summary>Used when the sample rate is unknown.</summary>
    public const int DefaultSampleRate = 44100;

    /// <summary>
    /// Decodes <paramref name="windowSec"/> seconds of <paramref name="filePath"/>
    /// through ffmpeg, at its native sample rate, and analyses them as they arrive.
    /// A file shorter than the window is analysed whole, and the result says so.
    /// </summary>
    /// <param name="durationSec">The file's duration; 0 when unknown.</param>
    /// <param name="windowSec">Clamped to [<see cref="MinWindowSec"/>, <see cref="MaxWindowSec"/>].</param>
    public static async Task<SpectralAnalysis> AnalyzeFileAsync(
        string filePath,
        string ffmpegExe,
        int sampleRate,
        double durationSec,
        double windowSec = DefaultWindowSec,
        CancellationToken cancellationToken = default)
    {
        if (sampleRate <= 0)
        {
            sampleRate = DefaultSampleRate;
        }
        windowSec = Math.Clamp(windowSec, MinWindowSec, MaxWindowSec);
        double start = AnalysisStartSec(durationSec, windowSec);
        double expectedSec = durationSec > 0 ? Math.Min(windowSec, durationSec - start) : windowSec;

        var accumulator = new SpectralAccumulator(sampleRate, (long)(expectedSec * sampleRate));
        await PcmDecoder
            .DecodeMonoAsync(filePath, ffmpegExe, sampleRate, start, windowSec, accumulator, cancellationToken)
            .ConfigureAwait(false);
        return accumulator.Complete(new AnalysisRequest(start, windowSec, Math.Max(0, durationSec)));
    }

    /// <summary>
    /// Skips intros: analyses from a point likely to be dense — a quarter of the way
    /// in, at most a minute in, and from the start for tracks under 20 s — but never
    /// so late that the window would run past the end of the file.
    /// </summary>
    internal static double AnalysisStartSec(double durationSec, double windowSec = DefaultWindowSec)
    {
        // Written so that an unknown (0 or NaN) duration also starts at 0.
        if (!(durationSec >= 20))
        {
            return 0;
        }
        double preferred = Math.Min(durationSec * 0.25, 60);
        return Math.Max(0, Math.Min(preferred, durationSec - windowSec));
    }

    /// <summary>Runs the full FFT pass over mono samples and derives an evidence-backed verdict.</summary>
    /// <param name="timeStartSec">Where <paramref name="samples"/> start in the track, for the time axis.</param>
    /// <exception cref="AudioAnalysisException">Too little audio, or nothing but silence.</exception>
    public static SpectralAnalysis Analyze(ReadOnlySpan<float> samples, int sampleRate, double timeStartSec = 0)
    {
        var accumulator = new SpectralAccumulator(sampleRate, samples.Length);
        accumulator.Add(samples);
        return accumulator.Complete(new AnalysisRequest(timeStartSec, (double)samples.Length / sampleRate, 0));
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
