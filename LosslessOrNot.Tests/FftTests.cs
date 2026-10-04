using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Tests;

public sealed class FftTests
{
    // Checks the FFT itself against a known signal before any audio is involved —
    // if this is wrong, every downstream verdict is wrong.
    [Fact]
    public void PeakLandsOnTheToneFrequency()
    {
        const int n = 1024;
        const double sampleRate = 48000.0;
        const double tone = 3000.0;
        var re = new double[n];
        var im = new double[n];
        for (int i = 0; i < n; i++)
        {
            re[i] = Math.Sin(2 * Math.PI * tone * i / sampleRate);
        }

        Fft.Transform(re, im);

        int peakBin = 0;
        double peakMag = 0;
        for (int b = 1; b < n / 2; b++)
        {
            double m = re[b] * re[b] + im[b] * im[b];
            if (m > peakMag)
            {
                peakMag = m;
                peakBin = b;
            }
        }
        double gotHz = peakBin * sampleRate / n;
        Assert.True(Math.Abs(gotHz - tone) <= sampleRate / n, $"FFT peak at {gotHz:0.0} Hz, expected {tone:0.0} Hz");
    }

    // The synthetic brick-wall fixtures invert the transform through the forward
    // one; this pins down that the round trip is exact.
    [Fact]
    public void InverseThroughConjugation_RoundTrips()
    {
        const int n = 256;
        var random = new Random(7);
        double[] original = Enumerable.Range(0, n).Select(_ => random.NextDouble() - 0.5).ToArray();
        double[] re = (double[])original.Clone();
        var im = new double[n];

        Fft.Transform(re, im);
        for (int k = 0; k < n; k++)
        {
            im[k] = -im[k];
        }
        Fft.Transform(re, im);

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(original[i], re[i] / n, precision: 9);
        }
    }

    [Fact]
    public void RejectsLengthsThatAreNotPowersOfTwo()
    {
        Assert.Throws<ArgumentException>(() => Fft.Transform(new double[1000], new double[1000]));
    }
}
