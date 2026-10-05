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

    /// <summary>How widely a series ranges, ignoring its extremes: the 90th percentile minus the 10th.</summary>
    public static double Spread(ReadOnlySpan<double> x) => Percentile(x, 0.9) - Percentile(x, 0.1);

    /// <summary>
    /// Spearman's rank correlation: does one series rise when the other does,
    /// whatever the shape of the relation? Ties share their average rank. 0 when
    /// either series is constant or has fewer than 3 points.
    /// </summary>
    public static double SpearmanCorrelation(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        if (x.Length != y.Length)
        {
            throw new ArgumentException("The two series must have the same length.", nameof(y));
        }
        return x.Length < 3 ? 0 : PearsonCorrelation(Ranks(x), Ranks(y));
    }

    /// <summary>Pearson's linear correlation; 0 when either series is constant.</summary>
    public static double PearsonCorrelation(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        int n = x.Length;
        if (n == 0 || y.Length != n)
        {
            return 0;
        }
        double mx = 0, my = 0;
        for (int i = 0; i < n; i++)
        {
            mx += x[i];
            my += y[i];
        }
        mx /= n;
        my /= n;
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - mx, dy = y[i] - my;
            sxy += dx * dy;
            sxx += dx * dx;
            syy += dy * dy;
        }
        return sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : 0;
    }

    private static double[] Ranks(ReadOnlySpan<double> x)
    {
        double[] keys = x.ToArray();
        int[] order = new int[keys.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }
        Array.Sort(keys, order);
        var ranks = new double[keys.Length];
        for (int i = 0; i < keys.Length;)
        {
            int j = i + 1;
            while (j < keys.Length && keys[j] == keys[i])
            {
                j++;
            }
            double rank = (i + j - 1) / 2.0;
            for (int k = i; k < j; k++)
            {
                ranks[order[k]] = rank;
            }
            i = j;
        }
        return ranks;
    }
}
