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

    /// <summary>
    /// Noise losing <paramref name="tiltDbPerKHz"/> per kHz, as music's treble does,
    /// then falling by <paramref name="residualDb"/> at <paramref name="cutoffHz"/> but
    /// not to silence: as abrupt as a codec's wall, yet what remains above it is the
    /// same signal, attenuated, and still falling.
    /// </summary>
    public static float[] SteepResidualNoise(int cutoffHz, double residualDb, double tiltDbPerKHz = 0, int log2Length = 19) =>
        ShapedNoise(log2Length, hz =>
            Math.Pow(10, -tiltDbPerKHz * hz / 1000 / 20) * (hz > cutoffHz ? Math.Pow(10, residualDb / 20) : 1));

    /// <summary>Hard clipping at ±<paramref name="limit"/>, as writing an overshooting decode to integers does.</summary>
    public static float[] Clip(float[] samples, double limit) =>
        samples.Select(s => (float)Math.Clamp(s, -limit, limit)).ToArray();

    /// <summary>
    /// White noise holding only frequencies above <paramref name="fromHz"/>, scaled by
    /// <paramref name="gain"/>, and independent of the other signals here: the shape of
    /// noise-shaped dither added after a lossy decode.
    /// </summary>
    public static float[] HighNoise(double fromHz, double gain, int log2Length = 19) =>
        ShapedNoise(log2Length, hz => hz > fromHz ? gain : 0, seed: 4321);

    /// <summary>
    /// Gives a signal the dynamics of music: a smoothly varying gain, drawn at random
    /// every quarter second between <paramref name="minGain"/> and 1 on a log scale.
    /// </summary>
    public static float[] Modulate(float[] samples, double minGain = 0.05, int seed = 99)
    {
        var random = new Random(seed);
        int segment = Rate / 4;
        int points = samples.Length / segment + 2;
        var gains = new double[points];
        for (int i = 0; i < points; i++)
        {
            gains[i] = Math.Pow(minGain, random.NextDouble());
        }
        var output = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            double position = (double)i / segment;
            int k = (int)position;
            double gain = gains[k] + (gains[k + 1] - gains[k]) * (position - k);
            output[i] = (float)(samples[i] * gain);
        }
        return output;
    }

    /// <summary>Rounds to the grid of <paramref name="bits"/>-bit integers, without dither, as a decoder writing PCM does.</summary>
    public static float[] Quantize(float[] samples, int bits)
    {
        double scale = Math.Pow(2, bits - 1);
        var output = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            output[i] = (float)(Math.Clamp(Math.Round(samples[i] * scale), -scale, scale - 1) / scale);
        }
        return output;
    }

    public static float[] Scale(float[] samples, double gain) => samples.Select(s => (float)(s * gain)).ToArray();

    public static float[] Add(float[] a, float[] b) => a.Zip(b, (x, y) => x + y).ToArray();

    /// <summary>White noise whose spectrum is multiplied by <paramref name="gain"/>(frequency in Hz).</summary>
    private static float[] ShapedNoise(int log2Length, Func<double, double> gain, int seed = 1234)
    {
        int n = 1 << log2Length;
        float[] noise = WhiteNoise(n, seed);
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
