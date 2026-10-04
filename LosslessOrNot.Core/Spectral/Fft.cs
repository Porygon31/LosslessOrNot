namespace LosslessOrNot.Core.Spectral;

/// <summary>The FFT and analysis window behind <see cref="SpectralAnalyzer"/>.</summary>
internal static class Fft
{
    /// <summary>
    /// Iterative in-place radix-2 Cooley–Tukey FFT. <paramref name="re"/> and
    /// <paramref name="im"/> must share the same power-of-two length.
    /// </summary>
    public static void Transform(Span<double> re, Span<double> im)
    {
        int n = re.Length;
        if (im.Length != n)
        {
            throw new ArgumentException("re and im must have the same length.", nameof(im));
        }
        if (n <= 1)
        {
            return;
        }
        if ((n & (n - 1)) != 0)
        {
            throw new ArgumentException("The length must be a power of two.", nameof(re));
        }

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double ang = -2 * Math.PI / length;
            double wRe = Math.Cos(ang), wIm = Math.Sin(ang);
            int half = length / 2;
            for (int i = 0; i < n; i += length)
            {
                double curRe = 1.0, curIm = 0.0;
                for (int j = 0; j < half; j++)
                {
                    double uRe = re[i + j], uIm = im[i + j];
                    double vRe = re[i + j + half] * curRe - im[i + j + half] * curIm;
                    double vIm = re[i + j + half] * curIm + im[i + j + half] * curRe;
                    re[i + j] = uRe + vRe;
                    im[i + j] = uIm + vIm;
                    re[i + j + half] = uRe - vRe;
                    im[i + j + half] = uIm - vIm;
                    double nRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nRe;
                }
            }
        }
    }

    /// <summary>Returns a precomputed (symmetric) Hann window of size <paramref name="n"/>.</summary>
    public static double[] HannWindow(int n)
    {
        var w = new double[n];
        for (int i = 0; i < n; i++)
        {
            w[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
        }
        return w;
    }
}
