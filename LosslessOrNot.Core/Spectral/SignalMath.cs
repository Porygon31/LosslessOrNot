namespace LosslessOrNot.Core.Spectral;

internal static class SignalMath
{
    /// <summary>Centred moving average over <paramref name="n"/> points, the window clamped at the edges.</summary>
    public static double[] MovingAverage(ReadOnlySpan<double> x, int n)
    {
        var output = new double[x.Length];
        if (n <= 1)
        {
            x.CopyTo(output);
            return output;
        }
        int half = n / 2;
        for (int i = 0; i < x.Length; i++)
        {
            int lo = Math.Max(0, i - half);
            int hi = Math.Min(x.Length - 1, i + half);
            double s = 0;
            for (int j = lo; j <= hi; j++)
            {
                s += x[j];
            }
            output[i] = s / (hi - lo + 1);
        }
        return output;
    }

    /// <summary>Nearest-rank percentile (<paramref name="p"/> in 0..1) of a sorted copy; 0 when empty.</summary>
    public static double Percentile(ReadOnlySpan<double> x, double p)
    {
        if (x.IsEmpty)
        {
            return 0;
        }
        double[] c = x.ToArray();
        Array.Sort(c);
        int i = Math.Clamp((int)(p * (c.Length - 1)), 0, c.Length - 1);
        return c[i];
    }
}
