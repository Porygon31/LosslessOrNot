namespace LosslessOrNot.Core.Spectral;

/// <summary>
/// The power of every analysed frame in 100 Hz bands: what the averaged spectrum
/// throws away. The verdict uses it to tell a codec's floor, the same at every
/// instant, from attenuated music, which rises and falls with the programme.
/// </summary>
/// <remarks>
/// Memory stays bounded whatever the window: beyond <see cref="MaxFrames"/> frames,
/// only one frame in a regular stride is kept, and the frequency range stops at
/// <see cref="TopHz"/>, past every frequency the verdict looks at.
/// </remarks>
internal sealed class FrameBands
{
    public const double BandHz = 100.0;

    /// <summary>Walls sit below 21 kHz and the lossless test looks at 21–22 kHz: nothing above matters.</summary>
    public const double TopHz = 24000.0;

    /// <summary>At most 4096 × 240 floats, under 4 MB.</summary>
    public const int MaxFrames = 4096;

    private readonly (int Lo, int Hi)[] _bins;
    private readonly List<float[]> _frames = [];
    private long _stride;
    private long _seen;

    /// <param name="bins">FFT bins spanning 0..Nyquist.</param>
    /// <param name="expectedFrames">Roughly how many frames will be offered; it sets the stride.</param>
    public FrameBands(int sampleRate, int bins, long expectedFrames)
    {
        double binHz = sampleRate / 2.0 / bins;
        double top = Math.Min(TopHz, sampleRate / 2.0);
        int count = Math.Max(1, (int)Math.Ceiling(top / BandHz - 1e-9));
        _bins = new (int, int)[count];
        for (int j = 0; j < count; j++)
        {
            int lo = Math.Min(bins - 1, (int)Math.Round(j * BandHz / binHz));
            int hi = Math.Min(bins, (int)Math.Round(Math.Min((j + 1) * BandHz, top) / binHz));
            _bins[j] = (lo, Math.Max(lo + 1, hi));
        }
        _stride = Math.Max(1, (expectedFrames + MaxFrames - 1) / MaxFrames);
    }

    /// <summary>Frames kept.</summary>
    public int Count => _frames.Count;

    /// <summary>Offers one frame's power spectrum; one in every stride is kept.</summary>
    public void Add(ReadOnlySpan<double> power)
    {
        if (_seen++ % _stride != 0)
        {
            return;
        }
        var row = new float[_bins.Length];
        for (int j = 0; j < row.Length; j++)
        {
            (int lo, int hi) = _bins[j];
            double sum = 0;
            for (int b = lo; b < hi; b++)
            {
                sum += power[b];
            }
            row[j] = (float)(sum / (hi - lo));
        }
        _frames.Add(row);

        // More frames than expected (a duration ffprobe got wrong): drop every other
        // frame and double the stride, so the kept frames stay evenly spread.
        if (_frames.Count >= 2 * MaxFrames)
        {
            for (int i = 0; i < MaxFrames; i++)
            {
                _frames[i] = _frames[2 * i];
            }
            _frames.RemoveRange(MaxFrames, _frames.Count - MaxFrames);
            _stride *= 2;
        }
    }

    /// <summary>
    /// The level of every kept frame, in dB of absolute power, over the bands lying
    /// entirely within [<paramref name="loHz"/>, <paramref name="hiHz"/>]. Empty when
    /// no band does.
    /// </summary>
    public double[] Levels(double loHz, double hiHz)
    {
        int first = Math.Max(0, (int)Math.Ceiling(loHz / BandHz - 1e-9));
        int last = Math.Min(_bins.Length, (int)Math.Floor(hiHz / BandHz + 1e-9));
        if (last <= first)
        {
            return [];
        }
        var levels = new double[_frames.Count];
        for (int i = 0; i < levels.Length; i++)
        {
            float[] row = _frames[i];
            double sum = 0;
            for (int j = first; j < last; j++)
            {
                sum += row[j];
            }
            levels[i] = 10 * Math.Log10(sum / (last - first) + 1e-30);
        }
        return levels;
    }
}
