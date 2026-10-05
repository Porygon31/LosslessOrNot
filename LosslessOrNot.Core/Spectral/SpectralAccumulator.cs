namespace LosslessOrNot.Core.Spectral;

/// <summary>
/// The FFT pass, fed with audio as it is decoded. Only one frame of samples is
/// held at a time, so memory no longer grows with the length of the window the
/// user chose — at 600 s of 192 kHz audio, keeping every sample would take
/// over 400 MB per file.
/// </summary>
internal sealed class SpectralAccumulator
{
    private readonly PowerFrames _input;
    private readonly Action<double[]> _onFrame;
    private readonly int _bins;
    private readonly (int Lo, int Hi)[] _bands;

    // At any other FFT size, a 4096-point pass runs alongside, only to find the peak
    // levels are measured against: see ReferencePeak.
    private readonly PowerFrames? _reference;
    private readonly Action<double[]>? _onReferenceFrame;
    private readonly double[]? _referenceAcc;
    private int _referenceFrames;

    private readonly int _sampleRate;
    private readonly int _bitsPerSample;
    private readonly int _framesPerColumn;
    private readonly FrameBands _frameBands;
    private readonly double[] _acc;
    private readonly List<double[]> _columns = [];
    private double[]? _openColumn;
    private int _framesInColumn;
    private double _rawPeak;
    private long _samples;
    private int _frames;

    /// <param name="expectedSamples">
    /// Roughly how many samples will be fed. It decides how many frames each
    /// spectrogram column gathers, so that a long window still fits in
    /// <see cref="SpectralAnalyzer.MaxColumns"/> columns.
    /// </param>
    /// <param name="bitsPerSample">The integer depth the samples were rounded to; 0 when unknown.</param>
    /// <param name="fftSize">Points per frame; see <see cref="SpectralAnalyzer.IsValidFftSize"/>.</param>
    public SpectralAccumulator(
        int sampleRate, long expectedSamples, int bitsPerSample = 0, int fftSize = SpectralAnalyzer.DefaultFftSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (!SpectralAnalyzer.IsValidFftSize(fftSize))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fftSize),
                fftSize,
                $"A power of two from {SpectralAnalyzer.MinFftSize} to {SpectralAnalyzer.MaxFftSize}.");
        }
        _sampleRate = sampleRate;
        _bitsPerSample = bitsPerSample is >= 8 and <= 32 ? bitsPerSample : 0;
        _input = new PowerFrames(fftSize);
        _onFrame = ProcessFrame;
        _bins = fftSize / 2;
        _bands = SpectrogramBands(_bins, Math.Min(SpectralAnalyzer.SpecRowCount, _bins));
        _acc = new double[_bins];
        if (fftSize != SpectralAnalyzer.LevelReferenceFftSize)
        {
            _reference = new PowerFrames(SpectralAnalyzer.LevelReferenceFftSize);
            _onReferenceFrame = AccumulateReference;
            _referenceAcc = new double[SpectralAnalyzer.LevelReferenceFftSize / 2];
        }
        long expectedFrames = Math.Max(1, (expectedSamples - fftSize) / (fftSize / 2) + 1);
        _framesPerColumn = (int)Math.Max(1, (expectedFrames + SpectralAnalyzer.MaxColumns - 1) / SpectralAnalyzer.MaxColumns);
        _frameBands = new FrameBands(sampleRate, _bins, expectedFrames);
    }

    /// <summary>Samples per FFT frame: less audio than this cannot be analysed.</summary>
    public int FrameSize => _input.Size;

    /// <summary>
    /// The power that rounding to <paramref name="bits"/>-bit integers adds to every
    /// bin, in the units of the accumulated spectrum: a uniform error of one LSB
    /// (2^(1−bits) for samples in [−1, 1)) has a variance of LSB²/12.
    /// </summary>
    private double QuantizationNoisePower(int bits) => Math.Pow(2, 2 * (1 - bits)) / 12 * _input.WindowPower;

    public void Add(ReadOnlySpan<float> samples)
    {
        _samples += samples.Length;
        _input.Add(samples, _onFrame);
        _reference?.Add(samples, _onReferenceFrame!);
    }

    private void ProcessFrame(double[] power)
    {
        for (int b = 0; b < _bins; b++)
        {
            double p = power[b];
            _acc[b] += p;
            if (p > _rawPeak)
            {
                _rawPeak = p;
            }
        }
        PoolIntoColumn(power);
        _frameBands.Add(power);
        _frames++;
    }

    private void AccumulateReference(double[] power)
    {
        for (int b = 0; b < power.Length; b++)
        {
            _referenceAcc![b] += power[b];
        }
        _referenceFrames++;
    }

    // MAX-pooling, not averaging, in frequency and — when there are more frames
    // than columns — in time: averaging would hide a narrow but real component,
    // which is exactly the signal a spectrogram exists to show. Pooling here
    // rather than keeping raw frames is what the Go code computed at the end;
    // the reference peak is still taken over the raw per-frame values.
    private void PoolIntoColumn(double[] power)
    {
        if (_openColumn is null)
        {
            _openColumn = new double[_bands.Length];
            _columns.Add(_openColumn);
            _framesInColumn = 0;
        }
        for (int r = 0; r < _bands.Length; r++)
        {
            double m = _openColumn[r];
            for (int j = _bands[r].Lo; j < _bands[r].Hi; j++)
            {
                if (power[j] > m)
                {
                    m = power[j];
                }
            }
            _openColumn[r] = m;
        }
        if (++_framesInColumn == _framesPerColumn)
        {
            _openColumn = null;
        }
    }

    /// <exception cref="AudioAnalysisException">Too little audio, or nothing but silence.</exception>
    public SpectralAnalysis Complete(AnalysisRequest request)
    {
        if (_samples < FrameSize)
        {
            throw new AudioAnalysisException($"Pas assez d'audio décodé ({_samples} échantillons).");
        }
        if (_frames == 0)
        {
            throw new AudioAnalysisException("Aucune trame audio non silencieuse à analyser.");
        }

        // Average power -> dB relative to the loudest bin.
        var mean = new double[_bins];
        for (int b = 0; b < _bins; b++)
        {
            mean[b] = _acc[b] / _frames;
        }
        double peak = ReferencePeak(mean);
        if (peak <= 0)
        {
            throw new AudioAnalysisException("Spectre silencieux.");
        }
        var db = new double[_bins];
        for (int b = 0; b < _bins; b++)
        {
            db[b] = 10 * Math.Log10(mean[b] / peak + 1e-20);
        }
        // Rounding noise is a level in absolute terms: on the relative scale it sits
        // higher the quieter the music.
        double? quantizationFloorDb = _bitsPerSample > 0
            ? 10 * Math.Log10(QuantizationNoisePower(_bitsPerSample) / peak)
            : null;

        // The end of the decoded audio. Go used frames × hop, which falls up to a
        // frame short and made "12 s analysed, from 0 to 11.9 s" contradict itself.
        double analyzedSec = (double)_samples / _sampleRate;
        double timeEnd = request.StartSec + analyzedSec;
        var scope = new AnalysisScope(request.StartSec, timeEnd, request.RequestedSec, analyzedSec, request.FileDurationSec);
        VerdictResult verdict = VerdictEngine.Derive(
            new SpectrumEvidence(db, _sampleRate, _frames, FrameSize, _bitsPerSample, quantizationFloorDb, _frameBands), scope);
        Measurements m = verdict.Measurements;
        // What lies above the steepest drop only describes a wall when it is one.
        WallTracking? wall = ConfidenceModel.IsCodecCandidate(m) ? m.Wall : null;
        return new SpectralAnalysis
        {
            SampleRate = _sampleRate,
            NyquistHz = _sampleRate / 2,
            CutoffHz = verdict.CutoffHz,
            CutoffConfidence = verdict.Confidence,
            CliffHz = verdict.CliffHz,
            ShelfDropDb = verdict.ShelfDropDb,
            EnergyAboveCutoffDb = verdict.EnergyAboveCutoffDb,
            Verdict = verdict.Verdict,
            LikelySource = verdict.LikelySource,
            LossyProbability = verdict.Score.LossyProbability,
            HasCodecWall = verdict.Score.HasCodecWall,
            BitsPerSample = _bitsPerSample,
            ContentThresholdDb = verdict.ContentThresholdDb,
            QuantizationFloorDb = quantizationFloorDb,
            FloorAboveQuantizationDb = ConfidenceModel.IsCodecCandidate(m) ? m.FloorAboveQuantizationDb : null,
            FloorMusicCorrelation = wall is { HasDynamics: true } tracked ? tracked.Correlation : null,
            WallPersistence = wall?.Persistence,
            FloorDepthDb = wall?.DepthDb,
            FloorSlopeDbPerKHz = ConfidenceModel.IsCodecCandidate(m) ? m.FloorSlopeDbPerKHz : null,
            Measurements = m,
            Score = verdict.Score,
            // Beyond a wall where encoders cut, what reaches past 21 kHz is what lies above that wall.
            HighBandMusicCorrelation = !ConfidenceModel.IsCodecCandidate(m) && m.HighBand is { HasDynamics: true } high
                ? high.Correlation
                : null,
            Evidence = verdict.Evidence,
            Criteria = verdict.Criteria,
            SupportingEvidence = verdict.SupportingEvidence,
            Observations = verdict.Observations,
            Conclusion = verdict.Conclusion,
            AvgSpectrumDb = SpectralAnalyzer.DownsampleSpectrum(db, SpectralAnalyzer.AvgSpectrumPoints),
            FramesAnalyzed = _frames,
            FftSize = FrameSize,
            Columns = SpectralAnalyzer.QuantizeColumns(_columns, _rawPeak),
            SpecFloorDb = SpectralAnalyzer.SpecFloorDb,
            TimeStartSec = request.StartSec,
            TimeEndSec = timeEnd,
            FreqBinHz = (double)_sampleRate / 2 / _bands.Length,
            RequestedSec = request.RequestedSec,
            AnalyzedSec = analyzedSec,
            FileDurationSec = request.FileDurationSec,
        };
    }

    /// <summary>
    /// The level every other is measured against: the loudest bin of the averaged
    /// spectrum — that of a 4096-point analysis, in the units of this one.
    /// </summary>
    /// <remarks>
    /// A tone keeps its power in one bin whatever the FFT size, while noise spreads
    /// its power over all of them: each time the size doubles, a tonal peak — the bass
    /// of most music — rises 3 dB against the treble. Measured on the calibration
    /// corpus at 8192 points, the floor above codecs' walls sank about 3 dB under the
    /// peak, and content stopped earlier, though nothing but the FFT had changed.
    /// Every threshold was calibrated against the 4096-point peak: against it,
    /// broadband levels read the same at any FFT size.
    /// </remarks>
    private double ReferencePeak(double[] mean)
    {
        if (_reference is null || _referenceFrames == 0)
        {
            return mean.Max();
        }
        double referencePeak = _referenceAcc!.Max() / _referenceFrames;
        return referencePeak * _input.WindowPower / _reference.WindowPower;
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
}
