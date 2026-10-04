using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Tests;

/// <summary>In-memory test signals, so the analysis can be exercised without ffmpeg.</summary>
internal static class Signals
{
    public const int Rate = 48000;

    public static float[] WhiteNoise(int length, int seed = 1234)
    {
        var random = new Random(seed);
        var samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = (float)(random.NextDouble() - 0.5);
        }
        return samples;
    }

    /// <summary>
    /// White noise with every component above <paramref name="cutoffHz"/> removed
    /// exactly, by zeroing the FFT bins of one long block: a perfect brick wall
    /// landing in a true digital floor, which is the shape a lossy encoder leaves.
    /// </summary>
    public static float[] BrickWallNoise(int cutoffHz, int log2Length = 19) =>
        ShapedNoise(log2Length, hz => hz > cutoffHz ? 0 : 1);

    /// <summary>
    /// White noise losing <paramref name="dbPerKHz"/> per kHz above
    /// <paramref name="fromHz"/>: a gradual roll-off, as programme material with
    /// little treble has, and no wall at all.
    /// </summary>
    public static float[] SlopedNoise(double fromHz, double dbPerKHz, int log2Length = 19) =>
        ShapedNoise(log2Length, hz => hz <= fromHz ? 1 : Math.Pow(10, -(hz - fromHz) / 1000 * dbPerKHz / 20));

    /// <summary>White noise whose spectrum is multiplied by <paramref name="gain"/>(frequency in Hz).</summary>
    private static float[] ShapedNoise(int log2Length, Func<double, double> gain)
    {
        int n = 1 << log2Length;
        float[] noise = WhiteNoise(n);
        var re = new double[n];
        var im = new double[n];
        for (int i = 0; i < n; i++)
        {
            re[i] = noise[i];
        }

        Fft.Transform(re, im);
        for (int k = 0; k <= n / 2; k++)
        {
            double g = gain((double)k * Rate / n);
            re[k] *= g;
            im[k] *= g;
            // The mirrored bin keeps the signal real.
            if (k > 0 && k < n / 2)
            {
                re[n - k] *= g;
                im[n - k] *= g;
            }
        }
        // Inverse transform through the forward one: ifft(x) = conj(fft(conj(x))) / n.
        for (int k = 0; k < n; k++)
        {
            im[k] = -im[k];
        }
        Fft.Transform(re, im);

        var samples = new float[n];
        for (int i = 0; i < n; i++)
        {
            samples[i] = (float)(re[i] / n);
        }
        return samples;
    }
}
