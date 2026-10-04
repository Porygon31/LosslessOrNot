namespace LosslessOrNot.Core.Spectral;

/// <summary>
/// The FFT pass, fed with audio as it is decoded. Only one frame of samples is
/// held at a time, so memory no longer grows with the length of the window the
/// user chose — at 600 s of 192 kHz audio, keeping every sample would take
/// over 400 MB per file.
/// </summary>
internal sealed class SpectralAccumulator
{
    private const int FrameSize = SpectralAnalyzer.FftSize;
    private const int Hop = SpectralAnalyzer.FftHop;
    private const int Bins = FrameSize / 2;

    // RMS below which a frame counts as silent.
    private const double SilenceRms = 1e-4;

    private static readonly double[] Window = Fft.HannWindow(FrameSize);
    private static readonly (int Lo, int Hi)[] Bands = SpectrogramBands(Bins, Math.Min(SpectralAnalyzer.SpecRowCount, Bins));

    private readonly int _sampleRate;
    private readonly int _framesPerColumn;
    private readonly float[] _frame = new float[FrameSize];
    private readonly double[] _re = new double[FrameSize];
    private readonly double[] _im = new double[FrameSize];
    private readonly double[] _power = new double[Bins];
    private readonly double[] _acc = new double[Bins];
    private readonly List<double[]> _columns = [];
    private double[]? _openColumn;
    private int _framesInColumn;
    private int _filled;
    private double _rawPeak;
    private long _samples;
    private int _frames;

    /// <param name="expectedSamples">
    /// Roughly how many samples will be fed. It decides how many frames each
    /// spectrogram column gathers, so that a long window still fits in
    /// <see cref="SpectralAnalyzer.MaxColumns"/> columns.
    /// </param>
    public SpectralAccumulator(int sampleRate, long expectedSamples)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _sampleRate = sampleRate;
        long expectedFrames = Math.Max(1, (expectedSamples - FrameSize) / Hop + 1);
        _framesPerColumn = (int)Math.Max(1, (expectedFrames + SpectralAnalyzer.MaxColumns - 1) / SpectralAnalyzer.MaxColumns);
    }

    public void Add(ReadOnlySpan<float> samples)
    {
        _samples += samples.Length;
        while (!samples.IsEmpty)
        {
            int take = Math.Min(FrameSize - _filled, samples.Length);
            samples[..take].CopyTo(_frame.AsSpan(_filled));
            _filled += take;
            samples = samples[take..];
            if (_filled == FrameSize)
            {
                ProcessFrame();
                // 50 % overlap: the second half of this frame opens the next one.
                Array.Copy(_frame, Hop, _frame, 0, FrameSize - Hop);
                _filled = FrameSize - Hop;
            }
        }
    }

    private void ProcessFrame()
    {
        // Skip near-silent frames: they carry no spectral information and would
        // drag the average down, making a real cutoff look lower than it is.
        double energy = 0;
        foreach (float s in _frame)
        {
            energy += (double)s * s;
        }
        if (Math.Sqrt(energy / FrameSize) < SilenceRms)
        {
            return;
        }

        for (int i = 0; i < FrameSize; i++)
        {
            _re[i] = _frame[i] * Window[i];
            _im[i] = 0;
        }
        Fft.Transform(_re, _im);

        for (int b = 0; b < Bins; b++)
        {
            double p = _re[b] * _re[b] + _im[b] * _im[b];
            _power[b] = p;
            _acc[b] += p;
            if (p > _rawPeak)
            {
                _rawPeak = p;
            }
        }
        PoolIntoColumn();
        _frames++;
    }

    // MAX-pooling, not averaging, in frequency and — when there are more frames
    // than columns — in time: averaging would hide a narrow but real component,
    // which is exactly the signal a spectrogram exists to show. Pooling here
    // rather than keeping raw frames is what the Go code computed at the end;
    // the reference peak is still taken over the raw per-frame values.
    private void PoolIntoColumn()
    {
        if (_openColumn is null)
        {
            _openColumn = new double[Bands.Length];
            _columns.Add(_openColumn);
            _framesInColumn = 0;
        }
        for (int r = 0; r < Bands.Length; r++)
        {
            double m = _openColumn[r];
            for (int j = Bands[r].Lo; j < Bands[r].Hi; j++)
            {
                if (_power[j] > m)
                {
                    m = _power[j];
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
        var mean = new double[Bins];
        double peak = 0;
        for (int b = 0; b < Bins; b++)
        {
            mean[b] = _acc[b] / _frames;
            if (mean[b] > peak)
            {
                peak = mean[b];
            }
        }
        if (peak <= 0)
        {
            throw new AudioAnalysisException("Spectre silencieux.");
        }
        var db = new double[Bins];
        for (int b = 0; b < Bins; b++)
        {
            db[b] = 10 * Math.Log10(mean[b] / peak + 1e-20);
        }

        // The end of the decoded audio. Go used frames × hop, which falls up to a
        // frame short and made "12 s analysed, from 0 to 11.9 s" contradict itself.
        double analyzedSec = (double)_samples / _sampleRate;
        double timeEnd = request.StartSec + analyzedSec;
        var scope = new AnalysisScope(request.StartSec, timeEnd, request.RequestedSec, analyzedSec, request.FileDurationSec);
        VerdictResult verdict = VerdictEngine.Derive(db, _sampleRate, _frames, scope);
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
            Evidence = verdict.Evidence,
            Criteria = verdict.Criteria,
            Observations = verdict.Observations,
            Conclusion = verdict.Conclusion,
            AvgSpectrumDb = SpectralAnalyzer.DownsampleSpectrum(db, SpectralAnalyzer.AvgSpectrumPoints),
            FramesAnalyzed = _frames,
            Columns = SpectralAnalyzer.QuantizeColumns(_columns, _rawPeak),
            SpecFloorDb = SpectralAnalyzer.SpecFloorDb,
            TimeStartSec = request.StartSec,
            TimeEndSec = timeEnd,
            FreqBinHz = (double)_sampleRate / 2 / Bands.Length,
            RequestedSec = request.RequestedSec,
            AnalyzedSec = analyzedSec,
            FileDurationSec = request.FileDurationSec,
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
}
