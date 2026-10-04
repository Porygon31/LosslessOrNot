namespace LosslessOrNot.Charts;

public enum SpectrogramPalette
{
    Magma,
    Viridis,
    Grey,
}

/// <summary>
/// Perceptually ordered colour ramps: dark = quiet, bright = loud. Magma and
/// viridis stay monotonic in luminance, so a louder band always looks louder —
/// rainbow maps do not, and misread badly.
/// </summary>
internal static class ColorPalettes
{
    // Nine evenly spaced stops of matplotlib's maps, linearly interpolated.
    private static readonly int[] MagmaStops =
        [0x000004, 0x1D1147, 0x51127C, 0x822681, 0xB73779, 0xE75263, 0xFC8961, 0xFEC488, 0xFCFDBF];

    private static readonly int[] ViridisStops =
        [0x440154, 0x472D7B, 0x3B528B, 0x2C728E, 0x21918C, 0x28AE80, 0x5EC962, 0xADDC30, 0xFDE725];

    private static readonly Dictionary<SpectrogramPalette, int[]> Cache = [];

    /// <summary>256 opaque ARGB colours, indexed by a spectrogram byte.</summary>
    public static int[] Lookup(SpectrogramPalette palette)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(palette, out int[]? lut))
            {
                lut = Build(palette);
                Cache[palette] = lut;
            }
            return lut;
        }
    }

    private static int[] Build(SpectrogramPalette palette)
    {
        var lut = new int[256];
        for (int i = 0; i < lut.Length; i++)
        {
            double v = i / 255.0;
            lut[i] = palette switch
            {
                SpectrogramPalette.Viridis => Interpolate(ViridisStops, v),
                SpectrogramPalette.Grey => Grey(v),
                _ => Interpolate(MagmaStops, v),
            };
        }
        return lut;
    }

    private static int Interpolate(int[] stops, double v)
    {
        double position = v * (stops.Length - 1);
        int i = Math.Min((int)position, stops.Length - 2);
        double t = position - i;
        int a = stops[i], b = stops[i + 1];
        int r = Mix(a >> 16, b >> 16, t);
        int g = Mix((a >> 8) & 0xFF, (b >> 8) & 0xFF, t);
        int bl = Mix(a & 0xFF, b & 0xFF, t);
        return Color.FromArgb(r, g, bl).ToArgb();
    }

    private static int Grey(double v)
    {
        int g = (int)Math.Round(255 * Math.Pow(v, 0.8));
        return Color.FromArgb(g, g, g).ToArgb();
    }

    private static int Mix(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);
}
