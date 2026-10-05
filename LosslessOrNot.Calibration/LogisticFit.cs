namespace LosslessOrNot.Calibration;

/// <summary>One file for the fit: the contribution of every indicator, and the truth.</summary>
/// <param name="Group">The original the file comes from: cross-validation never splits a group.</param>
internal readonly record struct Sample(double[] X, bool Lossy, int Group);

/// <param name="Weights">Multipliers of the current contributions: 1 keeps an indicator as it is.</param>
/// <param name="CrossValidatedLogLoss">Mean log-loss on held-out originals, classes weighted equally.</param>
internal sealed record FitResult(double Intercept, double[] Weights, double Lambda, double CrossValidatedLogLoss);

/// <summary>
/// A logistic regression over the indicators' contributions, which fits how much each
/// one should weigh. Genuine files and fakes count equally whatever their numbers —
/// a 50/50 prior — and a penalty pulls every weight towards 1, the hand-set anchors,
/// and the intercept towards 0, so that a corpus too easy to separate cannot push
/// them to infinity, nor the mix of recipes set the prior.
/// </summary>
internal static class LogisticFit
{
    private static readonly double[] Lambdas = [0.01, 0.1, 1, 10, 100];

    /// <summary>Picks the penalty that predicts held-out originals best, then fits on everything.</summary>
    public static FitResult FitWithCrossValidation(IReadOnlyList<Sample> samples, int folds = 5)
    {
        double bestLoss = double.PositiveInfinity, bestLambda = 1;
        foreach (double lambda in Lambdas)
        {
            double loss = 0, weight = 0;
            for (int fold = 0; fold < folds; fold++)
            {
                Sample[] train = samples.Where(s => s.Group % folds != fold).ToArray();
                Sample[] test = samples.Where(s => s.Group % folds == fold).ToArray();
                if (test.Length == 0 || !HasBothClasses(train))
                {
                    continue;
                }
                (double intercept, double[] weights) = Fit(train, lambda);
                (double wLossy, double wGenuine) = ClassWeights(test);
                foreach (Sample s in test)
                {
                    double p = Math.Clamp(Probability(intercept, weights, s.X), 1e-6, 1 - 1e-6);
                    double w = s.Lossy ? wLossy : wGenuine;
                    loss -= w * (s.Lossy ? Math.Log(p) : Math.Log(1 - p));
                    weight += w;
                }
            }
            if (weight > 0 && loss / weight < bestLoss)
            {
                bestLoss = loss / weight;
                bestLambda = lambda;
            }
        }
        (double i, double[] ws) = Fit(samples, bestLambda);
        return new FitResult(i, ws, bestLambda, bestLoss);
    }

    /// <summary>Newton's method on the penalised, class-weighted log-likelihood.</summary>
    public static (double Intercept, double[] Weights) Fit(IReadOnlyList<Sample> samples, double lambda)
    {
        int d = samples[0].X.Length;
        int n = d + 1;
        var theta = new double[n];
        for (int j = 1; j < n; j++)
        {
            theta[j] = 1;
        }
        (double wLossy, double wGenuine) = ClassWeights(samples);
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var gradient = new double[n];
            var hessian = new double[n, n];
            foreach (Sample s in samples)
            {
                double p = Probability(theta[0], theta[1..], s.X);
                double w = s.Lossy ? wLossy : wGenuine;
                double residual = p - (s.Lossy ? 1 : 0);
                double curvature = Math.Max(p * (1 - p), 1e-12);
                for (int a = 0; a < n; a++)
                {
                    double xa = a == 0 ? 1 : s.X[a - 1];
                    gradient[a] += w * residual * xa;
                    for (int b = 0; b < n; b++)
                    {
                        double xb = b == 0 ? 1 : s.X[b - 1];
                        hessian[a, b] += w * curvature * xa * xb;
                    }
                }
            }
            gradient[0] += 2 * lambda * theta[0];
            hessian[0, 0] += 2 * lambda;
            for (int j = 1; j < n; j++)
            {
                gradient[j] += 2 * lambda * (theta[j] - 1);
                hessian[j, j] += 2 * lambda;
            }
            double[] step = Solve(hessian, gradient);
            double size = 0;
            for (int j = 0; j < n; j++)
            {
                theta[j] -= step[j];
                size = Math.Max(size, Math.Abs(step[j]));
            }
            if (size < 1e-9)
            {
                break;
            }
        }
        return (theta[0], theta[1..]);
    }

    public static double Probability(double intercept, double[] weights, double[] x)
    {
        double z = intercept;
        for (int j = 0; j < x.Length; j++)
        {
            z += weights[j] * x[j];
        }
        return 1 / (1 + Math.Exp(-z));
    }

    private static bool HasBothClasses(IEnumerable<Sample> samples) =>
        samples.Any(s => s.Lossy) && samples.Any(s => !s.Lossy);

    // Each class weighs half of the total, whatever its size.
    private static (double Lossy, double Genuine) ClassWeights(IReadOnlyCollection<Sample> samples)
    {
        int lossy = samples.Count(s => s.Lossy), genuine = samples.Count - lossy;
        return (lossy > 0 ? samples.Count / (2.0 * lossy) : 0, genuine > 0 ? samples.Count / (2.0 * genuine) : 0);
    }

    // Gaussian elimination with partial pivoting; the system is tiny.
    private static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
            {
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = row;
                }
            }
            if (pivot != col)
            {
                for (int k = 0; k < n; k++)
                {
                    (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                }
                (x[col], x[pivot]) = (x[pivot], x[col]);
            }
            for (int row = col + 1; row < n; row++)
            {
                double f = m[row, col] / m[col, col];
                for (int k = col; k < n; k++)
                {
                    m[row, k] -= f * m[col, k];
                }
                x[row] -= f * x[col];
            }
        }
        for (int row = n - 1; row >= 0; row--)
        {
            double sum = x[row];
            for (int k = row + 1; k < n; k++)
            {
                sum -= m[row, k] * x[k];
            }
            x[row] = sum / m[row, row];
        }
        return x;
    }
}
