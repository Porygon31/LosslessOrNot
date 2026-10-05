namespace LosslessOrNot.Core.Spectral;

/// <summary>
/// Cuts a stream of samples into Hann-windowed frames that overlap by half, and
/// hands over the power spectrum of every frame that is not silent.
/// </summary>
internal sealed class PowerFrames
{
    // RMS below which a frame counts as silent.
    private const double SilenceRms = 1e-4;

    private readonly double[] _window;
    private readonly float[] _frame;
    private readonly double[] _re;
    private readonly double[] _im;
    private readonly double[] _power;
    private int _filled;

    /// <param name="size">Samples per frame: a power of two.</param>
    public PowerFrames(int size)
    {
        Size = size;
        _window = Fft.HannWindow(size);
        WindowPower = _window.Sum(w => w * w);
        _frame = new float[size];
        _re = new double[size];
        _im = new double[size];
        _power = new double[size / 2];
    }

    /// <summary>Samples per frame.</summary>
    public int Size { get; }

    /// <summary>Σw²: the power a white noise of unit variance puts in every bin.</summary>
    public double WindowPower { get; }

    /// <summary>
    /// Feeds samples in. <paramref name="onFrame"/> receives the power of bins 0 to
    /// Size/2 − 1 of every frame they complete, silent frames excepted; the array is
    /// reused from one frame to the next.
    /// </summary>
    public void Add(ReadOnlySpan<float> samples, Action<double[]> onFrame)
    {
        while (!samples.IsEmpty)
        {
            int take = Math.Min(Size - _filled, samples.Length);
            samples[..take].CopyTo(_frame.AsSpan(_filled));
            _filled += take;
            samples = samples[take..];
            if (_filled == Size)
            {
                if (Transform())
                {
                    onFrame(_power);
                }
                // 50 % overlap: the second half of this frame opens the next one.
                int hop = Size / 2;
                Array.Copy(_frame, hop, _frame, 0, Size - hop);
                _filled = Size - hop;
            }
        }
    }

    private bool Transform()
    {
        // Skip near-silent frames: they carry no spectral information and would
        // drag the average down, making a real cutoff look lower than it is.
        double energy = 0;
        foreach (float s in _frame)
        {
            energy += (double)s * s;
        }
        if (Math.Sqrt(energy / Size) < SilenceRms)
        {
            return false;
        }

        for (int i = 0; i < Size; i++)
        {
            _re[i] = _frame[i] * _window[i];
            _im[i] = 0;
        }
        Fft.Transform(_re, _im);
        for (int b = 0; b < _power.Length; b++)
        {
            _power[b] = _re[b] * _re[b] + _im[b] * _im[b];
        }
        return true;
    }
}
